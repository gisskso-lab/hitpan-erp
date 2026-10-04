using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HitPan.API.Services.LocalSwap;

/// <summary>
/// 교체 일꾼 런처 — 계약 <c>docs/설계/erp/20260930_계약_local-swap_request.md</c> §1·§4.
/// </summary>
/// <remarks>
/// <para>🔴 <b>작업 폴더 = <c>{app}\rollback</c></b>(Program Files — 관리자만 쓴다). <c>%ProgramData%\HitPan</c> 은 Users 가 하위 폴더를
/// 먼저 만들어 가질 수 있어 일꾼 사본·요청서를 바꿔치면 SYSTEM 실행을 얻는다(병렬이슈 01). 그래도 쓰기 전마다
/// <see cref="ISwapFolderGuard"/>(C-8·C-12 공용 판정)를 받는다 — 걸리면 작업 등록 0.</para>
/// <para>🔴 <b>한 번에 하나</b>(병렬이슈 03): <c>swap.lock</c> 을 <see cref="FileMode.CreateNew"/> 로만 만든다(있으면 실패) ·
/// <c>schtasks /Create</c> 에 <c>/F</c> 를 쓰지 않는다(이미 있으면 등록 실패) · 끝난 교체 뒤 10분 쿨다운.</para>
/// <para>🔴 <b>작업 명령줄은 고정 틀</b>(병렬이슈 04): 들어가는 값은 서버가 정한 설치 경로·모드 상수·서버가 만든 32자리 번호뿐.</para>
/// </remarks>
public sealed class LocalSwapLauncher : ILocalSwapLauncher
{
    public const string TaskName = "HitPan-LocalSwap";
    public const string WorkFolderName = "rollback";
    public const string RequestFileName = "request.json";
    public const string LockFileName = "swap.lock";
    public const string ScriptFileName = "local-swap.ps1";
    /// <summary>판 이력 <c>{app}\rollback\versions-seen.txt</c> — 계약 §2 봉합 합의(쓰기 = 갈래 M 기록기 · 읽기 = <c>RollbackMaterialFinder</c>·일꾼 S1).</summary>
    public const string VersionsSeenFileName = "versions-seen.txt";

    /// <summary>열린 요청(<c>requested</c>·<c>running</c>)이 이보다 오래 안 바뀌면 죽은 것으로 본다.</summary>
    public static readonly TimeSpan OpenRequestStale = TimeSpan.FromMinutes(30);
    /// <summary>끝난 교체(두 모드 합쳐) 뒤 다음 교체까지(병렬이슈 03).</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);
    /// <summary>워치독 <c>UpdateLockFile.Ttl</c> 과 같은 값 — 그 규칙을 따를 뿐 워치독 코드는 안 바꾼다.</summary>
    public static readonly TimeSpan WatchdogLockTtl = TimeSpan.FromMinutes(15);
    /// <summary>schtasks <c>/TR</c> 길이 한도.</summary>
    public const int MaxTaskCommandLength = 261;

    private static readonly string[] Parts = { "api", "web", "watchdog" };
    // 작1 §20 B — 끝은 \z: .NET 의 $ 는 마지막 \n 앞에서도 맞아 「32자리+줄바꿈」을 통과시켰다(선행검증서 20261005 CodeQL6 §2).
    private static readonly Regex TicketShape = new("^[0-9a-f]{32}\\z", RegexOptions.CultureInvariant);

    private readonly ILocalSwapEnvironment _env;
    private readonly ISchtasksRunner _schtasks;
    private readonly ISwapFolderGuard _guard;
    private readonly ILogger<LocalSwapLauncher> _logger;
    private readonly object _gate = new();

    public LocalSwapLauncher(ILocalSwapEnvironment env, ISchtasksRunner schtasks, ISwapFolderGuard guard, ILogger<LocalSwapLauncher> logger)
    {
        _env = env;
        _schtasks = schtasks;
        _guard = guard;
        _logger = logger;
    }

    /// <summary><c>{app}\rollback</c>. 설치 루트를 못 찾으면 null.</summary>
    public static string? WorkDir(string? appRoot) => appRoot is null ? null : Path.Combine(appRoot, WorkFolderName);

    public SwapRequest? ReadLast()
    {
        var work = WorkDir(_env.AppRoot);
        if (work is null) return null;
        var path = Path.Combine(work, RequestFileName);
        if (!File.Exists(path)) return null;
        try
        {
            return SwapRequest.FromJson(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "[LocalSwap] 요청서를 읽지 못했습니다: {Path}", path);
            return null;
        }
    }

    public string? CheckBusy() => CheckBusy(null);

    public string? CheckBusy(string? owner)
    {
        if (!_env.IsWindows) return SwapReasons.NotWindows;
        var app = _env.AppRoot;
        if (app is null) return SwapReasons.RequestInvalid;
        var now = _env.UtcNow;

        // 봉합 F-4 — 프로세스 안 예약(받기·백업 중): 남의 예약이면 바쁨(owner null = 누구의 예약이든)
        if (IsReservedByOther(owner, now)) return SwapReasons.SwapInProgress;

        // ── 두 수동 동작(한 틀) ──
        var last = ReadLast();
        if (last is not null)
        {
            var age = now - last.LastTouchedUtc;
            if (SwapStates.IsOpen(last.State) && age < OpenRequestStale) return SwapReasons.SwapInProgress;
            if (!SwapStates.IsOpen(last.State) && last.State != SwapStates.Refused && age >= TimeSpan.Zero && age < Cooldown)
                return SwapReasons.Cooldown;
        }
        if (IsLockFresh(Path.Combine(app, WorkFolderName, LockFileName), OpenRequestStale, now, 1)) return SwapReasons.SwapInProgress;
        // 20260930작1 봉합 06ⓑ — 여기까지 왔으면 열린 요청도 swap.lock 도 30분 넘게 묵었다(또는 없다).
        //   종전: 작업이 있기만 하면 바쁨 ⇒ 일꾼이 아예 못 떠도 영구 「진행 중」. 이제: 묵은 요청서를 끝 상태로 적고 남은 작업을 지운다.
        if (ClearStale(app, ref last) is { } stuck) return stuck;

        // ── 자동 업데이트(워치독) 진행 표식 — 설계 §1 P-d ──
        // 20260930작1 봉합2(설계 §15-0 판정 순서) — 워치독 진행 표식을 .rbk 보다 먼저 전부 본다(종전: .rbk 가 update.lock·자가교체 작업 앞).
        if (File.Exists(Path.Combine(app, "update-swap.marker"))) return SwapReasons.UpdateInProgress;
        if (Directory.Exists(Path.Combine(app, "watchdog.new"))) return SwapReasons.UpdateInProgress;
        if (IsLockFresh(Path.Combine(app, "update.lock"), WatchdogLockTtl, now, 0)) return SwapReasons.UpdateInProgress;
        if (TaskExists("HitPanWatchdogSelfReplace") || TaskExists("HitPanWatchdogSelfReplaceRecover"))
            return SwapReasons.UpdateInProgress;

        // 봉합2 N-1ⓒ·N-3ⓑ — .rbk 는 「진행 중」이 아니라 모양으로 가른다(M2·M3·M4 → swap_interrupted · M5 = 잔재, 바쁨 아님).
        //   종전(봉합 06ⓑ): .rbk 가 있으면 broken 아니면 무조건 update_in_progress ⇒ 성공 뒤 잔재 하나로 영구 거짓 「진행 중」.
        if (JudgeLeftoverRbk(app, last) is { } rbk) return rbk;

        // 봉합2 N-2 — 워치독 .old 가 남아 있으면(교체 표식 없음 · 위에서 이미 걸렀다) ERP 를 멈추기 전에 거부한다.
        //   .old 는 보기만 — 워치독이 다음 교체 시작 때 지운다(UpdateOrchestrator.ClearStaleOldDirsForSwap).
        foreach (var p in Parts)
            if (Directory.Exists(Path.Combine(app, p + ".old"))) return SwapReasons.UpdateCleanupPending;
        return null;
    }

    /// <summary>
    /// 20260930작1 봉합2 — 설계 §15-0 판정표(계약 §4 봉합 2차). <c>{p}.rbk</c> 가 하나도 없으면 null(M1).
    /// M2 <c>{p}.rbk</c> 있는데 살아 있는 <c>{p}</c> 없음 · M3 살아 있는 api·watchdog 판이 다르거나 못 읽음 ·
    /// M4 마지막 요청 <c>broken</c> 이고 그 뒤 판이 안 바뀜 ⇒ <c>swap_interrupted</c>. 그 밖(M5 = 잔재)은 null — 다음 교체의 일꾼 S0 가 치운다.
    /// 폴더는 보기만 한다(지우기·옮기기 0).
    /// </summary>
    private string? JudgeLeftoverRbk(string app, SwapRequest? last)
    {
        var any = false;
        foreach (var p in Parts)
        {
            if (!Directory.Exists(Path.Combine(app, p + ".rbk"))) continue;
            any = true;
            if (!Directory.Exists(Path.Combine(app, p))) return SwapReasons.SwapInterrupted; // M2 — S4 한가운데 끊김
        }
        if (!any) return null; // M1

        var api = NormalizeVersion(_env.CurrentVersion);
        var wd = NormalizeVersion(_env.WatchdogVersion);
        if (api is null || wd is null || api != wd) return SwapReasons.SwapInterrupted; // M3

        // 20260930작1 봉합3 16ⓑ — 정지 전 단계(S0·S1·S2)에서 끝난 broken 은 M4 가 아니다: 정지는 S3 · 첫 .rbk 는 S4(일꾼 순서)
        //   ⇒ 프로그램 폴더·서비스를 안 바꿨다. 단계가 비었거나 모르는 값이면 종전대로 M4(보수).
        if (last?.State == SwapStates.Broken && !IsBeforeStop(last.Step))
        {
            // M4 — S7R(원위치 0 · 세 폴더 전부 to)를 잔재로 오판하지 않게: 그때 .rbk 는 검증된 옛 판의 유일한 사본.
            var unchanged = last.PartsAfter is { } after
                ? NormalizeVersion(after.Api) == api && NormalizeVersion(after.Watchdog) == wd
                : NormalizeVersion(last.From) == api || NormalizeVersion(last.To) == api;
            if (unchanged) return SwapReasons.SwapInterrupted;
        }

        _logger.LogDebug("[LocalSwap] 지난 교체의 잔재(.rbk)가 남아 있습니다 — 판 {Version} 로 맞음 · 바쁨 아님(다음 교체가 먼저 치웁니다).", api);
        return null; // M5
    }

    /// <summary>
    /// 20260930작1 봉합3 16ⓑ — 일꾼 단계가 정지(S3) 전(S0·S1·S2)이면 true. null·빈 값·모르는 값은 false(종전 M4 판정 그대로).
    /// 근거: 일꾼 <c>local-swap.ps1</c> 은 S3 에서 처음 멈추고(<c>$stopped = $true</c>) S4 <c>Invoke-Swap</c> 에서 처음 <c>.rbk</c> 를 만든다.
    /// </summary>
    private static bool IsBeforeStop(string? step) =>
        step is not null && (string.Equals(step, "S0", StringComparison.Ordinal)
                             || string.Equals(step, "S1", StringComparison.Ordinal)
                             || string.Equals(step, "S2", StringComparison.Ordinal));

    /// <summary><c>M.m.b</c> 로 맞춘다(4자리면 뒤를 버린다). 못 읽거나 <c>0.0.0</c>(<c>VersionInfo</c> 못 읽음 표식)이면 null.</summary>
    internal static string? NormalizeVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !Version.TryParse(raw.Trim(), out var v) || v.Build < 0) return null;
        if (v.Major == 0 && v.Minor == 0 && v.Build == 0) return null;
        return $"{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>
    /// 봉합 06ⓑ(계약 §4 봉합 합의) — 열린 요청·<c>swap.lock</c> 이 둘 다 묵었을 때만 부른다.
    /// ① 열린 요청서를 끝 상태로(<c>requested</c> → <c>refused</c>/<c>worker_not_started</c> · <c>running</c> → <c>broken</c>/<c>worker_interrupted</c>)
    /// — <c>updated_at</c>·<c>step</c> 은 그대로(쿨다운을 새로 걸지 않는다) · <paramref name="last"/> 도 같이 바꾼다(뒤 판정이 본다).
    /// ② 남은 1회용 작업을 지운다. 지우기가 실패하면 <c>swap_in_progress</c>(그대로 바쁨), 아니면 null.
    /// 🔴 잠금(<c>_gate</c>) 안에서 요청서·<c>swap.lock</c> 을 <b>다시</b> 읽는다 — 그 사이 <see cref="Launch"/> 가 새 요청을 걸었으면
    /// 옛 판정으로 새 요청서를 덮거나 새 작업을 지우지 않는다(그 경우 바쁨).
    /// </summary>
    private string? ClearStale(string app, ref SwapRequest? last)
    {
        lock (_gate)
        {
            var now = _env.UtcNow;
            if (IsLockFresh(Path.Combine(app, WorkFolderName, LockFileName), OpenRequestStale, now, 1)) return SwapReasons.SwapInProgress;
            last = ReadLast();
            if (last is not null && SwapStates.IsOpen(last.State) && now - last.LastTouchedUtc < OpenRequestStale)
                return SwapReasons.SwapInProgress;
            if (last is not null && !SwapStates.IsOpen(last.State) && last.State != SwapStates.Refused
                && now - last.LastTouchedUtc >= TimeSpan.Zero && now - last.LastTouchedUtc < Cooldown)
                return SwapReasons.Cooldown; // 다시 읽는 사이 끝난 교체(바깥 판정과 같은 규칙)

            if (last is not null && SwapStates.IsOpen(last.State))
            {
                var wasRunning = last.State == SwapStates.Running;
                last.State = wasRunning ? SwapStates.Broken : SwapStates.Refused;
                last.Reason = wasRunning ? SwapReasons.WorkerInterrupted : SwapReasons.WorkerNotStarted;
                _logger.LogWarning("[LocalSwap] 요청서가 {Min}분 넘게 {Old} 로 멈춰 있어 끝 상태 {State}/{Reason} 로 적습니다(번호 {Ticket} · 단계 {Step}).",
                    OpenRequestStale.TotalMinutes, wasRunning ? SwapStates.Running : SwapStates.Requested, last.State, last.Reason, last.Ticket, last.Step);
                try { WriteAtomic(Path.Combine(app, WorkFolderName, RequestFileName), last.ToJson()); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "[LocalSwap] 묵은 요청서에 끝 상태를 적지 못했습니다({Reason}).", last.Reason);
                }
            }

            if (!TaskExists(TaskName)) return null;
            _logger.LogWarning("[LocalSwap] 1회용 작업 {Task} 가 남아 있는데 요청·잠금이 {Min}분 넘게 묵었습니다 — 작업을 지웁니다.",
                TaskName, OpenRequestStale.TotalMinutes);
            var del = _schtasks.Run($"/Delete /TN \"{TaskName}\" /F");
            if (del == 0) return null;
            _logger.LogWarning("[LocalSwap] 남은 작업 {Task} 를 지우지 못했습니다(exit={Code}) — 진행 중으로 둡니다.", TaskName, del);
            return SwapReasons.SwapInProgress;
        }
    }

    public SwapLaunchResult Launch(SwapLaunchInput input)
    {
        lock (_gate)
        {
            return LaunchCore(input);
        }
    }

    /// <summary>
    /// 봉합 07(계약 §4 봉합 합의) — <see cref="Launch"/> 앞 정적 판정 한 벌. <see cref="LaunchCore"/> 도 이것을 부른다.
    /// </summary>
    public string? CheckReady()
    {
        if (!_env.IsWindows) return SwapReasons.NotWindows;
        var app = _env.AppRoot;
        var slot = _env.Slot;
        if (app is null || slot is null or < 1) return SwapReasons.RequestInvalid;
        if (!File.Exists(_env.ScriptSourcePath)) return SwapReasons.ScriptMissing;

        var work = Path.Combine(app, WorkFolderName);
        var run = Path.Combine(work, "run");
        try
        {
            _guard.EnsureSafe(work);
            _guard.EnsureSafe(run);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[LocalSwap] 작업 폴더가 안전하지 않아 교체를 걸지 않았습니다(병렬이슈 01).");
            return SwapReasons.FolderUnsafe;
        }
        return null;
    }

    // ── 봉합 F-4 — 프로세스 안 예약(파일·작업 등록 0 · API 재시작이면 사라진다 · 교체가 시작되면 swap.lock 이 이어받는다) ──
    private string? _reservedBy;
    private DateTime _reservedAtUtc;

    public bool TryReserve(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner)) return false;
        lock (_gate)
        {
            var now = _env.UtcNow;
            ExpireReservation(now);
            if (_reservedBy is not null && !string.Equals(_reservedBy, owner, StringComparison.Ordinal)) return false;
            _reservedBy = owner;
            _reservedAtUtc = now;
            return true;
        }
    }

    public void Release(string owner)
    {
        lock (_gate)
        {
            if (_reservedBy is not null && string.Equals(_reservedBy, owner, StringComparison.Ordinal)) _reservedBy = null;
        }
    }

    private bool IsReservedByOther(string? owner, DateTime now)
    {
        lock (_gate)
        {
            ExpireReservation(now);
            return _reservedBy is not null && (owner is null || !string.Equals(_reservedBy, owner, StringComparison.Ordinal));
        }
    }

    /// <summary>예약이 30분(<see cref="OpenRequestStale"/>) 넘게 갱신되지 않았으면 푼다 — 해제 누락이 영구 바쁨으로 번지지 않게. <c>_gate</c> 안에서만.</summary>
    private void ExpireReservation(DateTime now)
    {
        if (_reservedBy is null) return;
        var age = now - _reservedAtUtc;
        if (age >= TimeSpan.Zero && age < OpenRequestStale) return;
        _logger.LogWarning("[LocalSwap] 예약({Owner})이 {Min}분 넘게 풀리지 않아 없는 것으로 봅니다.", _reservedBy, OpenRequestStale.TotalMinutes);
        _reservedBy = null;
    }

    private SwapLaunchResult LaunchCore(SwapLaunchInput input)
    {
        // 봉합 07 — 정적 판정은 CheckReady 한 벌(호출부가 받기·백업 전에 부르는 것과 같은 판정)
        if (CheckReady() is { } notReady) return SwapLaunchResult.Refuse(notReady);
        var app = _env.AppRoot!;
        var slot = _env.Slot!;
        if (input.Mode is not (SwapModes.Update or SwapModes.Rollback)) return SwapLaunchResult.Refuse(SwapReasons.RequestInvalid);

        var ticket = input.Ticket ?? NewTicket();
        if (!TicketShape.IsMatch(ticket)) return SwapLaunchResult.Refuse(SwapReasons.TicketInvalid);

        var work = Path.Combine(app, WorkFolderName);
        var run = Path.Combine(work, "run");

        // 봉합 F-4 — 자기 예약(input.Owner)은 바쁨으로 보지 않는다
        if (CheckBusy(input.Owner) is { } busy) return SwapLaunchResult.Refuse(busy);

        var lockPath = Path.Combine(work, LockFileName);
        if (!TryCreateLock(lockPath, ticket)) return SwapLaunchResult.Refuse(SwapReasons.SwapInProgress);

        var ok = false;
        try
        {
            var request = new SwapRequest
            {
                Ticket = ticket,
                Mode = input.Mode,
                State = SwapStates.Requested,
                From = input.From,
                To = input.To,
                Material = input.Material,
                AppRoot = app,
                Slot = slot.Value,
                ApiPort = _env.ApiPort,
                RequestedBy = input.RequestedBy,
                RequestedAt = _env.UtcNow,
                Entry = input.Entry,
                AutoState = input.AutoState,
            };
            WriteAtomic(Path.Combine(work, RequestFileName), request.ToJson());
            File.Copy(_env.ScriptSourcePath, Path.Combine(run, ScriptFileName), overwrite: true);

            var command = BuildTaskCommand(Path.Combine(run, ScriptFileName), input.Mode, ticket);
            if (command.Length > MaxTaskCommandLength)
            {
                _logger.LogWarning("[LocalSwap] 작업 명령줄이 {Len}자라 schtasks 한도({Max})를 넘습니다 — 설치 경로가 너무 깁니다.",
                    command.Length, MaxTaskCommandLength);
                MarkRefused(work, request, SwapReasons.TaskRegisterFailed);
                return SwapLaunchResult.Refuse(SwapReasons.TaskRegisterFailed);
            }

            var at = DateTime.Now.AddMinutes(1);
            var sd = at.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
            var st = at.ToString("HH:mm", CultureInfo.InvariantCulture);
            // /F 없음 — 같은 이름이 이미 있으면 등록 실패(병렬이슈 03)
            var create = _schtasks.Run(
                $"/Create /TN \"{TaskName}\" /TR \"{command}\" /SC ONCE /SD {sd} /ST {st} /RU SYSTEM /RL HIGHEST");
            if (create != 0)
            {
                _logger.LogWarning("[LocalSwap] 1회용 작업 등록 실패(exit={Code}) — 교체를 걸지 않았습니다(U-3).", create);
                MarkRefused(work, request, SwapReasons.TaskRegisterFailed);
                return SwapLaunchResult.Refuse(SwapReasons.TaskRegisterFailed);
            }
            var start = _schtasks.Run($"/Run /TN \"{TaskName}\"");
            if (start != 0)
            {
                _logger.LogWarning("[LocalSwap] 1회용 작업 실행 실패(exit={Code}) — 작업을 지우고 멈춥니다.", start);
                _schtasks.Run($"/Delete /TN \"{TaskName}\" /F");
                MarkRefused(work, request, SwapReasons.TaskRegisterFailed);
                return SwapLaunchResult.Refuse(SwapReasons.TaskRegisterFailed);
            }

            ok = true;
            _logger.LogInformation("[LocalSwap] 교체 시작 — {Mode} {From} → {To} · 재료 {Kind} · 번호 {Ticket}",
                input.Mode, input.From, input.To, input.Material.Kind, ticket);
            return new SwapLaunchResult(true, SwapReasons.Ok, ticket);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[LocalSwap] 요청서·일꾼 사본을 쓰지 못해 교체를 걸지 않았습니다.");
            return SwapLaunchResult.Refuse(SwapReasons.TaskRegisterFailed);
        }
        finally
        {
            // 일꾼이 돌기 시작하면 잠금은 일꾼이 푼다. 여기서 끝나면 런처가 푼다.
            if (!ok) TryDelete(lockPath);
        }
    }

    /// <summary>
    /// schtasks <c>/TR</c> 고정 틀. 값 = 설치 경로(서버) · 모드 상수 · 서버가 만든 번호뿐(병렬이슈 04).
    /// 경로는 <c>\"</c> 로 감싼다 — 공백이 있으면 인자가 쪼개진다(워치독 <c>PsRunner</c> 와 같은 규칙).
    /// </summary>
    public static string BuildTaskCommand(string scriptPath, string mode, string ticket)
    {
        if (mode is not (SwapModes.Update or SwapModes.Rollback)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!TicketShape.IsMatch(ticket)) throw new ArgumentException("ticket shape", nameof(ticket));
        return "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \\\"" + scriptPath + "\\\" -Mode " + mode + " -Ticket " + ticket;
    }

    /// <summary>1회용 번호 — 32자리 소문자 16진(일꾼 <c>-Ticket</c> 모양과 같다).</summary>
    public static string NewTicket() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private bool TaskExists(string name) => _schtasks.Run($"/Query /TN \"{name}\"") == 0;

    /// <summary>파일 내용 <c>UTC|…</c> 기준으로 유효한가(워치독 <c>UpdateLockFile</c> 와 같은 판정 — 미래·못 읽음 = 무효).</summary>
    /// <param name="stampIndex"><c>|</c> 로 나눈 몇 번째 칸이 시각인가 — <c>update.lock</c>(워치독) = 0 · <c>swap.lock</c>(<c>번호|UTC</c>) = 1.</param>
    public static bool IsLockFresh(string path, TimeSpan ttl, DateTime nowUtc, int stampIndex)
    {
        if (!File.Exists(path)) return false;
        string raw;
        try { raw = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 못 읽는 잠금은 누가 쥐고 있다는 뜻이다 — 보수적으로 바쁨.
            return true;
        }
        var parts = raw.Trim().Split('|');
        if (parts.Length <= stampIndex) return false;
        if (!DateTime.TryParse(parts[stampIndex], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)) return false;
        var age = nowUtc - t.ToUniversalTime();
        return age >= TimeSpan.Zero && age <= ttl;
    }

    private bool TryCreateLock(string path, string ticket)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var body = Encoding.ASCII.GetBytes(ticket + "|" + _env.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                fs.Write(body, 0, body.Length);
                return true;
            }
            catch (IOException ex) when (File.Exists(path))
            {
                if (attempt == 0 && !IsLockFresh(path, OpenRequestStale, _env.UtcNow, 1))
                {
                    _logger.LogWarning(ex, "[LocalSwap] 묵은 교체 잠금({Min}분 넘음)을 지우고 다시 잡습니다.", OpenRequestStale.TotalMinutes);
                    TryDelete(path);
                    continue;
                }
                return false;
            }
        }
        return false;
    }

    private void MarkRefused(string work, SwapRequest request, string reason)
    {
        request.State = SwapStates.Refused;
        request.Reason = reason;
        request.UpdatedAt = _env.UtcNow;
        try { WriteAtomic(Path.Combine(work, RequestFileName), request.ToJson()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[LocalSwap] 거부 상태를 요청서에 적지 못했습니다({Reason}).", reason);
        }
    }

    private static void WriteAtomic(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[LocalSwap] 파일을 지우지 못했습니다: {Path}", path);
        }
    }
}

/// <summary>설치 환경 — 실제 PC.</summary>
public sealed class LocalSwapEnvironment : ILocalSwapEnvironment
{
    private readonly ILogger<LocalSwapEnvironment> _logger;

    public LocalSwapEnvironment(ILogger<LocalSwapEnvironment> logger) => _logger = logger;

    public bool IsWindows => OperatingSystem.IsWindows();

    public string? AppRoot
    {
        get
        {
            var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var parent = Directory.GetParent(baseDir)?.FullName;
            // {app}\watchdog 가 있어야 진짜 설치 루트다(WatchdogHandoffBootstrap.ResolveAppRoot 와 같은 판정)
            return parent is not null && Directory.Exists(Path.Combine(parent, "watchdog")) ? parent : null;
        }
    }

    public string ScriptSourcePath => Path.Combine(AppContext.BaseDirectory, "Rollback", LocalSwapLauncher.ScriptFileName);

    public string CurrentVersion => VersionInfo.Current;

    // 20260930작1 I-API 7 — SLOT_INDEX·API_PORT 는 워치독과 「같은 한 벌」로 읽는다(링크 컴파일한 워치독 원본 DbConfReader ·
    //   HitPan.API.csproj <Compile Link>). 일꾼이 이 슬롯으로 keepalive 작업 이름을 만들고, 워치독 UpdateProcessGate 도
    //   DbConfReader.GetValue("SLOT_INDEX") 로 같은 이름을 만든다 ⇒ 두 쪽 판정(찾는 db.conf 순서·환경변수 폴백 여부)이 갈리면
    //   서로 다른 작업을 멈추고 켠다. 종전 TenantConfigReader 는 exe 폴더를 먼저 보고 환경변수로 폴백해 워치독과 달랐다.
    public int? Slot => int.TryParse(HitPan.Watchdog.DbConfReader.GetValue("SLOT_INDEX"), out var s) && s >= 1 ? s : null;

    public int ApiPort => int.TryParse(HitPan.Watchdog.DbConfReader.GetValue("API_PORT"), out var p) && p > 0 ? p : 5257;

    public string WatchdogStagingDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HitPan", "Updates", "staging");

    public DateTime UtcNow => DateTime.UtcNow;

    /// <summary>20260930작1 봉합2 N-3 — <c>{app}\watchdog\HitPan.Watchdog.exe</c> FileVersion <c>M.m.b</c>. 설치 루트·파일·판 표식이 없으면 null(읽기만).</summary>
    public string? WatchdogVersion
    {
        get
        {
            var app = AppRoot;
            if (app is null) return null;
            // ⬛ 봉합2 B1: {app}\watchdog\HitPan.Watchdog.exe 의 FileVersion 만 읽었다.
            // 20260930작1 봉합2 B2(PM 추가) — 게시 파이프라인(deploy-update.yml · build-manifest.ps1)이 판의 출처로 쓰는
            //   HitPan.Watchdog.dll 을 먼저 읽고, 없을 때만 exe(한 벌 출처). self-contained apphost exe 의 버전 자원이 dll 과 같다는 보장은 ⚠️미확인.
            return ReadWatchdogVersion(Path.Combine(app, "watchdog"), _logger);
        }
    }

    /// <summary>워치독 판 파일 이름 — 먼저 읽는 순서(dll → exe).</summary>
    public static readonly string[] WatchdogVersionFiles = { "HitPan.Watchdog.dll", "HitPan.Watchdog.exe" };

    /// <summary>
    /// 20260930작1 봉합2 B2 — <paramref name="watchdogDir"/> 의 <c>HitPan.Watchdog.dll</c> FileVersion <c>M.m.b</c>. dll 이 없을 때만 exe.
    /// 있는 첫 파일의 판을 쓴다(dll 이 있는데 판을 못 읽으면 exe 로 넘어가지 않고 null — 두 출처를 섞지 않는다). 읽기만.
    /// </summary>
    public static string? ReadWatchdogVersion(string watchdogDir, ILogger logger)
    {
        foreach (var name in WatchdogVersionFiles)
        {
            var file = Path.Combine(watchdogDir, name);
            if (!File.Exists(file)) continue;
            try
            {
                var fv = FileVersionInfo.GetVersionInfo(file);
                if (fv.FileMajorPart == 0 && fv.FileMinorPart == 0 && fv.FileBuildPart == 0) return null;
                return $"{fv.FileMajorPart}.{fv.FileMinorPart}.{fv.FileBuildPart}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "[LocalSwap] 워치독 판을 읽지 못했습니다: {Path}", file);
                return null;
            }
        }
        return null;
    }
}

/// <summary>schtasks — 실제 PC(창 없이 · 셸 없이).</summary>
public sealed class SchtasksRunner : ISchtasksRunner
{
    private readonly ILogger<SchtasksRunner> _logger;

    public SchtasksRunner(ILogger<SchtasksRunner> logger) => _logger = logger;

    public int Run(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return -1;
            p.StandardOutput.ReadToEnd();
            var err = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(30_000))
            {
                p.Kill(entireProcessTree: true);
                _logger.LogWarning("[LocalSwap] schtasks 30초 초과: {Args}", arguments);
                return -1;
            }
            if (p.ExitCode != 0 && !arguments.StartsWith("/Query", StringComparison.Ordinal))
                _logger.LogWarning("[LocalSwap] schtasks exit={Code} {Args} {Err}", p.ExitCode, arguments, err.Trim());
            return p.ExitCode;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _logger.LogWarning(ex, "[LocalSwap] schtasks 실행 실패: {Args}", arguments);
            return -1;
        }
    }
}

/// <summary>작업 폴더 안전 판정 — 백업 폴더와 같은 C-8·C-12 공용 판정을 부른다(복붙 0 · 병렬이슈 01).</summary>
public sealed class SwapFolderGuard : ISwapFolderGuard
{
    public void EnsureSafe(string path) => HitPan.Application.Services.BackupService.EnsureRestrictedSystemFolder(path);
}
