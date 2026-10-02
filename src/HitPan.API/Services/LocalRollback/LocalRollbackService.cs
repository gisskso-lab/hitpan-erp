using System.Collections.Concurrent;
using HitPan.API.Services.LocalSwap;
using HitPan.API.Services.ManualUpdate;

namespace HitPan.API.Services.LocalRollback;

/// <summary>
/// 수동 되돌리기 — 판정(읽기만) + 1회용 확인 번호 + 런처 호출. 20260930작1 갈래 A.
/// </summary>
/// <remarks>
/// <para>🔴 <b>고객 자료 불변</b>(설계 §5): 생성자 인자에 자료 연결·저장소 0. 판정은 파일 존재·글자 파일·zip 목차뿐.</para>
/// <para>🔴 <b>화면 값 불신</b>(병렬이슈 04): [예] 요청은 번호 하나만 받는다. 판·재료는 여기서 다시 계산하고,
/// 번호를 줄 때 본 재료와 다르면 거부한다(그 사이 자동 업데이트가 지나갔다).</para>
/// <para>L-2(사장님 · 늘 열림): 업데이트가 막혔는지(P-b)는 전제에서 뺐다 — 재료·메인PC·관리자·바쁨 아님이면 연다.</para>
/// </remarks>
public sealed class LocalRollbackService : ILocalRollbackService
{
    /// <summary>1회용 확인 번호 유효 시간(병렬이슈 03).</summary>
    public static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(10);

    private sealed record Issued(string UserId, string Kind, string Path, string To, DateTime IssuedUtc);

    private readonly ILocalSwapLauncher _launcher;
    private readonly ILocalSwapEnvironment _env;
    private readonly ILogger<LocalRollbackService> _logger;
    private readonly ConcurrentDictionary<string, Issued> _tickets = new(StringComparer.Ordinal);

    public LocalRollbackService(ILocalSwapLauncher launcher, ILocalSwapEnvironment env, ILogger<LocalRollbackService> logger)
    {
        _launcher = launcher;
        _env = env;
        _logger = logger;
    }

    // ── 20260930작1 1.3.50 확대 갈래 N2 — 세 번째 길(설계 §19-1 조각 B · §19-6 (ㄴ)) ─────────────────────────────
    //   기존 생성자·판정(재료 ①②)·prev/staging_zip 의 Launch 는 그대로 두고 덧붙이기만 했다(#1).
    //   이 생성자로 만들었을 때만(운영 DI) 세 번째 길이 선다 — 기존 생성자로 만든 시험은 전과 똑같이 돈다.
    private readonly IPreviousPackageFeed? _feed;
    private readonly IPackageFetcher? _fetcher;
    private readonly PreviousPackageFetch? _fetch;

    /// <summary>
    /// 운영(DI) — 기존 생성자 + 세 번째 길 재료(저장본 창구 · 받기·해시·공간 · 자동 업데이트 표식).
    /// <paramref name="reservationRenewInterval"/> 는 시험만 준다(DI 는 모르므로 기본값 = <see cref="ManualUpdateService.DefaultReservationRenewInterval"/>).
    /// </summary>
    public LocalRollbackService(
        ILocalSwapLauncher launcher,
        ILocalSwapEnvironment env,
        ILogger<LocalRollbackService> logger,
        IPreviousPackageFeed feed,
        IPackageFetcher fetcher,
        IAutoUpdateLockProbe autoLock,
        IHostApplicationLifetime? lifetime = null,
        TimeSpan? reservationRenewInterval = null)
        : this(launcher, env, logger)
    {
        _feed = feed;
        _fetcher = fetcher;
        _fetch = new PreviousPackageFetch(feed, fetcher, autoLock, launcher, env, logger,
            reservationRenewInterval ?? ManualUpdateService.DefaultReservationRenewInterval,
            lifetime?.ApplicationStopping ?? CancellationToken.None);
    }

    /// <summary>마지막으로 건 이전 판 받기 일의 끝(시험이 기다릴 때만 쓴다 · 세 번째 길이 없으면 끝난 일).</summary>
    public Task LastFetchRun => _fetch?.LastRun ?? Task.CompletedTask;

    public LocalRollbackStatus GetStatus(string userId)
    {
        PurgeExpired();
        var current = _env.CurrentVersion;
        var last = ToLast(_launcher.ReadLast());
        var (material, reason) = Judge();
        // N2 — 받은 파일 정리(X-5 · 19-6 보정: 판정이 선 P 의 zip 은 남긴다) · 받기 진행 칸(메모리)
        CleanupFetchedZips(material);
        var fetch = _fetch?.Current;
        if (material is null)
            return new LocalRollbackStatus(false, reason, current, null, null, null, (int)TicketLifetime.TotalMinutes, last, fetch);

        var ticket = LocalSwapLauncher.NewTicket();
        _tickets[ticket] = new Issued(userId, material.Kind, material.Path, material.Version, _env.UtcNow);
        return new LocalRollbackStatus(true, SwapReasons.Ok, current, material.Version, material.Kind, ticket,
            (int)TicketLifetime.TotalMinutes, last, fetch);
    }

    public SwapLaunchResult Start(string userId, string? ticket)
    {
        PurgeExpired();
        if (string.IsNullOrWhiteSpace(ticket) || !_tickets.TryRemove(ticket, out var issued))
            return SwapLaunchResult.Refuse(SwapReasons.TicketInvalid);
        if (!string.Equals(issued.UserId, userId, StringComparison.Ordinal) || _env.UtcNow - issued.IssuedUtc > TicketLifetime)
            return SwapLaunchResult.Refuse(SwapReasons.TicketInvalid);

        // 봉합 F-4(계약 §4) — 이 되돌리기 한 번의 예약 주인. 판정(CheckBusy(owner))·예약·Launch(Owner) 에 같은 값.
        var owner = ReservationOwner(ticket);
        var (material, reason) = Judge(owner);
        if (material is null) return SwapLaunchResult.Refuse(reason);
        if (material.Kind != issued.Kind || material.Version != issued.To ||
            !string.Equals(material.Path, issued.Path, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("[LocalRollback] 확인 번호를 준 뒤 재료가 바뀌었습니다({OldKind} {OldTo} → {Kind} {To}) — 거부.",
                issued.Kind, issued.To, material.Kind, material.Version);
            return SwapLaunchResult.Refuse(SwapReasons.TicketInvalid);
        }

        // N2 — 세 번째 길 재료(manual_zip)면 받기 일이 예약(TryReserve(owner))부터 넘기기까지 맡는다(설계 19-1). prev·staging_zip 은 아래 기존 줄 그대로.
        if (material.Kind == SwapMaterialKinds.ManualZip && _fetch is not null)
            return _fetch.Begin(material, _env.CurrentVersion, userId, ticket, owner);

        // 봉합 F-4 — CheckReady → CheckBusy(owner) → TryReserve(owner) 순서(계약 §4). 남이 쥐고 있으면(수동 업데이트 받기·백업 중) 진행 중.
        if (!_launcher.TryReserve(owner)) return SwapLaunchResult.Refuse(SwapReasons.SwapInProgress);
        SwapLaunchResult result;
        try
        {
            result = _launcher.Launch(new SwapLaunchInput(
                Mode: SwapModes.Rollback,
                From: _env.CurrentVersion,
                To: material.Version,
                Material: new SwapMaterial { Kind = material.Kind, Path = material.Path, Sha256 = null },
                RequestedBy: userId,
                Entry: SwapEntries.Menu,
                AutoState: null,
                Ticket: ticket,
                Owner: owner));
        }
        finally
        {
            // 걸렸으면 swap.lock 이 이어받고, 거부면 교체가 없다 ⇒ 어느 쪽이든 예약을 푼다.
            _launcher.Release(owner);
        }
        _logger.LogInformation("[LocalRollback] 되돌리기 요청 — {From} → {To} · 재료 {Kind} · 사용자 {User} · 결과 {Reason}",
            _env.CurrentVersion, material.Version, material.Kind, userId, result.Reason);
        return result;
    }

    /// <summary>봉합 F-4 — 되돌리기 예약 주인(확인 번호 하나 = 주인 하나).</summary>
    internal static string ReservationOwner(string ticket) => "rollback:" + ticket;

    /// <summary>전제 판정(설계 §1 P-c~P-f · P-a 는 컨트롤러 문). 순서: 사전 판정 → 바쁨 → 재료 → 저장 공간.</summary>
    /// <param name="owner">봉합 F-4 — 이 주인의 예약은 바쁨으로 보지 않는다. null(상태 조회) = 누구의 예약이든 바쁨.</param>
    private (RollbackMaterial? Material, string Reason) Judge(string? owner = null)
    {
        // ⬛ 봉합 전: 바쁨(CheckBusy())부터 — 슬롯·일꾼 원본·작업 폴더가 틀려도 번호를 발급했다(07).
        // 20260930작1 봉합 07(계약 §4) — 사전 판정을 바쁨 앞에서. 걸리면 번호 발급 0(G-U7).
        if (_launcher.CheckReady() is { } notReady) return (null, notReady);
        if (_launcher.CheckBusy(owner) is { } busy) return (null, busy);
        var app = _env.AppRoot;
        if (app is null) return (null, SwapReasons.RequestInvalid);
        try
        {
            var material = RollbackMaterialFinder.Find(app, _env.WatchdogStagingDir, _env.CurrentVersion, out var reason);
            // N2 — T1(①② 둘 다 없음 · 사유 no_previous_version)일 때만 세 번째 길을 본다. 연쇄 차단·형식 불량은 그 사유 그대로(아래 기존 줄).
            if (material is null && reason == SwapReasons.NoPreviousVersion && _feed is not null) return JudgeFetch(app);
            if (material is null) return (null, reason);

            var need = RollbackMaterialFinder.RequiredFreeBytes(material);
            var root = Path.GetPathRoot(app);
            if (root is not null && new DriveInfo(root).AvailableFreeSpace < need) return (null, SwapReasons.DiskLow);
            return (material, SwapReasons.Ok);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            _logger.LogWarning(ex, "[LocalRollback] 되돌리기 재료를 판정하지 못했습니다 — 되돌릴 이전 버전 없음으로 답합니다.");
            return (null, SwapReasons.NoPreviousVersion);
        }
    }

    /// <summary>
    /// N2 — 세 번째 길 판정(설계 19-1 조각 B · T2~T6). 하나라도 아니면 지금과 같은 사유(<c>no_previous_version</c> · 공간은 <c>disk_low</c>).
    /// 🔴 상태 확인 때 바깥 요청 0 — 저장본 읽기 + 서명 다시 확인 + 공간만. 파일 읽기 예외는 <see cref="Judge"/> 의 catch 가 같은 사유로 받는다.
    /// </summary>
    private (RollbackMaterial? Material, string Reason) JudgeFetch(string app)
    {
        if (!RollbackMaterialFinder.TryParse(_env.CurrentVersion, out var current)) return (null, SwapReasons.RequestInvalid);
        var work = Path.Combine(app, LocalSwapLauncher.WorkFolderName);

        // T2 묵은 prev 아님(재료 ② 와 같은 판정 재사용)
        if (RollbackMaterialFinder.IsStalePrev(Path.Combine(work, "prev"), current)) return (null, SwapReasons.NoPreviousVersion);
        // T3 판 이력이 직전 설치 판 P 를 안다 — 모르면 받지 않는다(두 판 뒤 원천 차단)
        if (!RollbackMaterialFinder.TryReadPreviousVersion(Path.Combine(work, LocalSwapLauncher.VersionsSeenFileName), current, out var previous)
            // N6(설계 §19-8 판정 4a) — 이력이 「막 태어난 상태」이고 저장본 중 지금 판 아래가 정확히 1개면 그 판. 그래도 모르면 지금대로.
            && !SignedManifestKeeper.TryFirstRunPrevious(app, current, out previous))
            return (null, SwapReasons.NoPreviousVersion);
        // T4 P < 지금 판
        if (previous >= current) return (null, SwapReasons.NoPreviousVersion);

        // T5 저장본 서명 다시 확인 + 안의 판 == P + 형식(3자리 판·sha256 64자·주소)
        var p = RollbackMaterialFinder.Format(previous);
        var pkg = _feed!.LoadVerified(p);
        if (pkg is null || !RollbackMaterialFinder.TryParse(pkg.Version, out var inside) || inside != previous
            || pkg.Sha256 is not { Length: 64 } || !pkg.Sha256.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(pkg.DownloadUrl))
            return (null, SwapReasons.NoPreviousVersion);

        // T6 받을 공간(수동 업데이트와 같은 기준)
        var staging = new ManualFolders(app).StagingDir;
        if (!_fetcher!.HasEnoughSpace(pkg.SizeBytes, staging)) return (null, SwapReasons.DiskLow);

        // 받을 자리 = 기존 받는 함수가 쓸 자리(UpdateClient: {받는 폴더}\hitpan-{판}.zip) — 주소 조립 아님(주소는 저장본 downloadUrl)
        return (new RollbackMaterial(SwapMaterialKinds.ManualZip, Path.Combine(staging, "hitpan-" + p + ".zip"), p), SwapReasons.Ok);
    }

    /// <summary>
    /// N2 — 받은 파일 정리(X-5 · 19-6 보정). 교체가 열려 있지 않을 때(<c>CheckBusy(null)</c> = null)만
    /// <c>{app}\manual\staging\hitpan-{v}.zip</c> 가운데 <b>v ≤ 지금 판</b>을 지운다. 판정이 선 세 번째 길 재료(P 의 zip)는 남긴다.
    /// 수동 업데이트는 지금보다 높은 판만 받으므로 겹치지 않는다. 내용 검사(해시)는 하지 않는다 — [예] 뒤 받기 일이 한다.
    /// </summary>
    private void CleanupFetchedZips(RollbackMaterial? judged)
    {
        if (_fetch is null) return;
        var app = _env.AppRoot;
        if (app is null || !RollbackMaterialFinder.TryParse(_env.CurrentVersion, out var current)) return;
        if (_launcher.CheckBusy(null) is not null) return;

        var keep = judged is { Kind: SwapMaterialKinds.ManualZip } ? Path.GetFullPath(judged.Path) : null;
        var staging = new ManualFolders(app).StagingDir;
        try
        {
            if (!Directory.Exists(staging)) return;
            foreach (var file in Directory.EnumerateFiles(staging, "hitpan-*.zip"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!string.Equals(Path.GetExtension(file), ".zip", StringComparison.OrdinalIgnoreCase)) continue; // 「*.zip」 이 「.zipx」 도 잡는 Windows 버릇
                if (!RollbackMaterialFinder.TryParse(name["hitpan-".Length..], out var v) || v > current) continue;
                if (keep is not null && string.Equals(Path.GetFullPath(file), keep, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    File.Delete(file);
                    _logger.LogInformation("[LocalRollback] 남은 이전 판 파일을 정리했습니다: {File}", Path.GetFileName(file));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "[LocalRollback] 남은 이전 판 파일을 지우지 못했습니다: {File}", Path.GetFileName(file));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[LocalRollback] 받는 폴더를 읽지 못해 남은 파일을 정리하지 않았습니다");
        }
    }

    /// <summary>request.json 한 줄 → 화면 칸. 수동 업데이트 확인(<c>ManualUpdateService.CheckAsync</c>)도 같은 것을 쓴다(한 벌).</summary>
    internal static LocalSwapLastResult? ToLast(SwapRequest? r) =>
        r is null ? null : new LocalSwapLastResult(r.Mode, r.State, r.Reason, r.From, r.To, r.LastTouchedUtc);

    private void PurgeExpired()
    {
        var now = _env.UtcNow;
        foreach (var kv in _tickets)
            if (now - kv.Value.IssuedUtc > TicketLifetime) _tickets.TryRemove(kv.Key, out _);
    }
}
