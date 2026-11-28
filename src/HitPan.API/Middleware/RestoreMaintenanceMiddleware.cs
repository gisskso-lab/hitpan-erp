using HitPan.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace HitPan.API.Middleware;

/// <summary>
/// 🔴 W-G1-52 갈래 A-2① — 복원 점검 중에도 통과시킬 끝점 표시(설계 §15-5 ① ⓑ 허용 목록 = 복원 진행 · 상태 확인).
/// </summary>
/// <remarks>
/// 이름 목록(경로 글자)이 아니라 끝점에 붙인다 — 경로가 바뀌어도 허용 목록이 따라간다.
/// 붙이는 곳 = A-2② 복원 진행 · 마지막 결과 끝점 둘뿐. 다른 끝점에 붙이면 점검 중 쓰기가 열린다([3-V] 볼 곳).
/// 정적 파일 · <c>/health</c> 는 표시 없이 통과(아래 미들웨어가 <c>/api</c> · <c>/hubs</c> 만 가름).
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AllowDuringRestoreMaintenanceAttribute : Attribute
{
}

/// <summary>
/// 🔴 W-G1-52 갈래 A-2① — 복원 점검 상태(프로세스 안 깃발 1개 · 설계 §15-5 ① ⓐ ~ ⓓ). Singleton.
/// </summary>
/// <remarks>
/// <para>ⓐ 요청은 <b>셈을 먼저 올리고</b> 깃발을 본다 · 깃발 켜기와 셈 읽기는 <b>한 자물쇠</b>(<see cref="_gate"/>) —
/// 「깃발 꺼짐 확인 → (그 사이 점검 켜짐) → 쓰기」 틈이 없다.</para>
/// <para>ⓒ 배경 일꾼 = 이름 목록이 아니라 <c>IHostedService</c> <b>전부</b>를 센다. 히트판 어셈블리의 <see cref="BackgroundService"/> 중
/// 도는 것은 멈추고(<see cref="BackgroundService.StopAsync"/>) 끝에 다시 띄운다(<see cref="BackgroundService.StartAsync"/>).
/// 기동 한 번짜리(끝난 일꾼 · 몸 없는 <c>IHostedService</c>)는 세기만 한다. 프레임워크 것(웹 서버 = <c>GenericWebHostService</c>)은
/// <b>멈추면 복원 요청까지 죽으므로</b> 손 0.</para>
/// <para>ⓓ 끝(성공 · 실패)에 <see cref="IMemoryCache"/> 를 통째 비운다(<c>SessionValidityMiddleware</c> 10초 캐시 포함 → 끊긴 세션이 바로 401).</para>
/// </remarks>
public sealed class RestoreMaintenanceState : IRestoreMaintenance
{
    private readonly object _gate = new();
    private readonly IServiceProvider _services;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RestoreMaintenanceState> _logger;
    private readonly Dictionary<string, DateTime> _stamps = new(StringComparer.Ordinal);
    private readonly List<BackgroundService> _paused = new();
    private bool _active;
    private int _inFlight;

    /// <summary>도는 셈이 0 이 되었나 다시 보는 간격.</summary>
    public static readonly TimeSpan DrainPoll = TimeSpan.FromMilliseconds(50);

    public RestoreMaintenanceState(IServiceProvider services, IMemoryCache cache, ILogger<RestoreMaintenanceState> logger)
    {
        _services = services;
        _cache = cache;
        _logger = logger;
    }

    public bool IsActive { get { lock (_gate) return _active; } }

    public int InFlight { get { lock (_gate) return _inFlight; } }

    public bool TryEnterRequest()
    {
        lock (_gate)
        {
            _inFlight++;                       // 셈 먼저
            if (!_active) return true;         // 그다음 깃발
            _inFlight--;
            return false;
        }
    }

    public void ExitRequest()
    {
        lock (_gate)
        {
            if (_inFlight > 0) _inFlight--;
            else _logger.LogWarning("복원 점검 — 도는 셈이 이미 0 인데 퇴장이 왔다(입장 · 퇴장 짝 어긋남).");
        }
    }

    public async Task<RestoreMaintenanceBeginResult> BeginAsync(TimeSpan drainTimeout, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_active) return Denied("already_active", Array.Empty<string>());
            _active = true;                    // 이 순간부터 새 입장은 503 — 셈 읽기와 같은 자물쇠
        }

        var counted = new List<string>();
        var started = false;
        try
        {
            // ⓑ 도는 요청이 0 이 될 때까지(한도 넘으면 멈춤 · 바뀐 것 0)
            var deadline = DateTime.UtcNow + drainTimeout;
            while (InFlight > 0)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    _logger.LogWarning("복원 점검 — 도는 요청이 {N}건 남아 {Timeout} 안에 비지 않았다. 점검을 켜지 않는다.", InFlight, drainTimeout);
                    return Denied("drain_timeout", counted);
                }
                await Task.Delay(DrainPoll, ct).ConfigureAwait(false);
            }

            // ⓒ IHostedService 전부를 센다 → 히트판의 도는 일꾼만 멈춤
            var remaining = deadline - DateTime.UtcNow;
            if (remaining < TimeSpan.FromSeconds(1)) remaining = TimeSpan.FromSeconds(1);
            using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stopCts.CancelAfter(remaining);

            var paused = new List<string>();
            foreach (var hs in _services.GetServices<IHostedService>())
            {
                var type = hs.GetType();
                counted.Add(type.FullName ?? type.Name);
                if (!IsOurs(type) || hs is not BackgroundService bg) continue;          // 프레임워크 것 · 기동 한 번 몸 없는 것 = 세기만
                if (bg.ExecuteTask is null || bg.ExecuteTask.IsCompleted) continue;    // 끝난 일꾼(기동 한 번) = 세기만

                await bg.StopAsync(stopCts.Token).ConfigureAwait(false);
                if (bg.ExecuteTask is { IsCompleted: false } still)
                {
                    // 도는 채로 다시 띄우면 몸이 둘이 된다 — 멈춘 뒤에 한 번 다시 띄운다(멈춤 요청은 이미 갔다)
                    _logger.LogWarning("복원 점검 — 배경 일꾼 {Type} 이 {Timeout} 안에 멈추지 않았다. 점검을 켜지 않는다.", type.FullName, remaining);
                    _ = RestartWhenStoppedAsync(bg, still);
                    return Denied("worker_stop_timeout", counted);
                }
                lock (_gate) _paused.Add(bg);
                paused.Add(type.FullName ?? type.Name);
            }

            started = true;
            _logger.LogInformation("복원 점검 켬 — 센 IHostedService {Count}개 · 멈춘 일꾼 {Paused}개({Names}).",
                counted.Count, paused.Count, string.Join(", ", paused));
            return new RestoreMaintenanceBeginResult(true, null, counted, paused);
        }
        finally
        {
            if (!started)
            {
                await RestartPausedAsync().ConfigureAwait(false);
                lock (_gate) _active = false;  // 못 켰으면 바뀐 것 0 — 깃발 끔 · 멈춘 일꾼 다시
            }
        }
    }

    public async Task EndAsync(string tenantId, CancellationToken ct)
    {
        // ⓓ 프로세스 안 캐시 비움 → 표지 → 깃발 끔(첫 요청이 새 값 · 새 표지를 본다) → 일꾼 다시
        if (_cache is MemoryCache mc) mc.Compact(1.0);
        else _logger.LogWarning("복원 점검 — IMemoryCache 가 MemoryCache 가 아니라 비우지 못했다({Type}).", _cache.GetType().FullName);

        // 초 단위로 내림 — JWT exp 가 초 단위(내림)라, 같은 초에 새로 받은 토큰이 표지보다 앞으로 읽히지 않게
        var now = DateTime.UtcNow;
        var stamp = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        lock (_gate)
        {
            if (!string.IsNullOrEmpty(tenantId)) _stamps[tenantId] = stamp;
            else _logger.LogWarning("복원 점검 끔 — 회사 번호가 비어 표지를 못 올렸다(DB restore_history 표지가 재기동 뒤 대신한다).");
            _active = false;
        }

        await RestartPausedAsync().ConfigureAwait(false);
        _logger.LogInformation("복원 점검 끔 — 캐시 비움 · 표지 {Stamp:o} (이보다 앞에 발급된 토큰 = 401).", stamp);
    }

    public DateTime? GetStampUtc(string tenantId)
    {
        lock (_gate) return _stamps.TryGetValue(tenantId, out var s) ? s : null;
    }

    public void RememberStampUtc(string tenantId, DateTime? stampUtc)
    {
        var value = stampUtc ?? DateTime.MinValue;
        var now = DateTime.UtcNow;
        if (value > now)
        {
            // 시계가 뒤로 간 PC — 미래 표지를 그대로 두면 새 로그인 토큰까지 그 시각까지 전부 401(잠금). 읽은 지금으로 깎는다.
            _logger.LogError("복원 시각 표지가 미래다({Stamp:o} > {Now:o}) — 지금으로 깎는다. 서버 시계를 확인하라.", value, now);
            value = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        }
        lock (_gate)
        {
            if (!_stamps.TryGetValue(tenantId, out var old) || value > old) _stamps[tenantId] = value;
        }
    }

    private async Task RestartPausedAsync()
    {
        List<BackgroundService> toStart;
        lock (_gate) { toStart = _paused.ToList(); _paused.Clear(); }
        foreach (var bg in toStart)
        {
            try
            {
                await bg.StartAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "복원 점검 끝 — 배경 일꾼 {Type} 다시 띄우기 실패. 다음 재기동 때 돈다.", bg.GetType().FullName);
            }
        }
    }

    private async Task RestartWhenStoppedAsync(BackgroundService bg, Task running)
    {
        try
        {
            await running.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "복원 점검 — 늦게 멈춘 배경 일꾼 {Type} 이 예외로 끝났다. 다시 띄운다.", bg.GetType().FullName);
        }

        try
        {
            await bg.StartAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "복원 점검 — 늦게 멈춘 배경 일꾼 {Type} 다시 띄우기 실패. 다음 재기동 때 돈다.", bg.GetType().FullName);
        }
    }

    private static bool IsOurs(Type t) =>
        (t.Assembly.GetName().Name ?? string.Empty).StartsWith("HitPan", StringComparison.Ordinal);

    private static RestoreMaintenanceBeginResult Denied(string reason, IReadOnlyList<string> counted) =>
        new(false, reason, counted, Array.Empty<string>());
}

/// <summary>
/// 🔴 W-G1-52 갈래 A-2① — 복원 점검 중 <c>/api</c> · <c>/hubs</c> 요청을 503 으로 돌린다(설계 §15-5 ① ⓐ ⓑ · GET 도 기본 거절).
/// </summary>
/// <remarks>
/// <para>자리 = 인증 뒤(<c>Program.cs</c> <c>UseAuthorization</c> 다음) — 끝점 표시(<see cref="AllowDuringRestoreMaintenanceAttribute"/>)는
/// 앞단 자동 라우팅이 이미 골라 둔 끝점에서 읽는다. 새 로그인(<c>/api/auth/*</c>)도 표시가 없어 503.</para>
/// <para><c>/hubs</c> 는 <b>셈하지 않는다</b> — 알림 허브 연결은 화면이 열려 있는 내내 「도는 요청」 이라 세면 점검이 영영 못 켜진다.
/// 점검 중 새 연결만 503 · 허브 메서드는 묶음 구독뿐(DB 쓰기 0 · 개발명세서 4-0 ⑥).</para>
/// </remarks>
public sealed class RestoreMaintenanceMiddleware
{
    /// <summary>점검 중 문구(설계 §15-5 ① ⓐ 글 — 화면 문구는 B 갈래가 다시 본다).</summary>
    public const string MaintenanceMessage = "대표님이 자료를 되살리는 중입니다 — 잠시 뒤 다시 시도해 주세요.";

    private readonly RequestDelegate _next;
    private readonly IRestoreMaintenance _maintenance;

    public RestoreMaintenanceMiddleware(RequestDelegate next, IRestoreMaintenance maintenance)
    {
        _next = next;
        _maintenance = maintenance;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var isApi = path.StartsWithSegments("/api");
        var isHub = path.StartsWithSegments("/hubs");
        if (!isApi && !isHub) { await _next(context); return; }   // 정적 파일 · /health · /swagger

        if (context.GetEndpoint()?.Metadata.GetMetadata<AllowDuringRestoreMaintenanceAttribute>() is not null)
        {
            await _next(context);                                   // 허용 목록(복원 진행 · 상태 확인) — 셈 밖
            return;
        }

        if (isHub)
        {
            if (_maintenance.IsActive) { await WriteMaintenanceAsync(context); return; }
            await _next(context);
            return;
        }

        if (!_maintenance.TryEnterRequest()) { await WriteMaintenanceAsync(context); return; }
        try
        {
            await _next(context);
        }
        finally
        {
            _maintenance.ExitRequest();
        }
    }

    private static Task WriteMaintenanceAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers["Retry-After"] = "30";
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(
            "{\"message\":\"" + MaintenanceMessage + "\",\"code\":\"restore_in_progress\"}");
    }
}
