using System.Text.RegularExpressions;
using HitPan.API.Services.LocalRollback;
using HitPan.API.Services.LocalSwap;
using HitPan.Application.Interfaces;

namespace HitPan.API.Services.ManualUpdate;

/// <summary>수동 업데이트 사유 코드(계약 `docs/설계/erp/20260930_계약_local-swap_request.md` §6 · U 예약분 포함).</summary>
public static class ManualUpdateReasons
{
    public const string Ok = "ok";
    public const string NotWindows = "not_windows";
    public const string UpdateInProgress = "update_in_progress";
    public const string SwapInProgress = "swap_in_progress";
    public const string DiskLow = "disk_low";
    public const string HashMismatch = "hash_mismatch";
    public const string FeedUnreachable = "feed_unreachable";
    public const string SignatureInvalid = "signature_invalid";
    public const string NoNewerVersion = "no_newer_version";
    public const string BackupFailed = "backup_failed";
    public const string DownloadFailed = "download_failed";

    // ⬛ LauncherNotWired("launcher_not_wired") — 20260930작1 I-API 가 런처를 이어 없앴다(개발명세서 I-API §1).
    //    넘기기 거부 사유는 이제 런처(LocalSwap.SwapReasons — 계약 §6)의 코드가 그대로 나간다.
}

/// <summary>수동 업데이트 한 번의 진행 단계.</summary>
public static class ManualUpdateStages
{
    public const string Checking = "checking";
    public const string Downloading = "downloading";
    public const string Verifying = "verifying";
    public const string BackingUp = "backing_up";
    public const string HandingOff = "handing_off";
    /// <summary>일꾼에 넘겼다(작업 등록됨) — 곧 API 가 멈춘다. 결과는 다음 로그인 뒤 request.json 으로 알린다(계약 §7).</summary>
    public const string HandedOff = "handed_off";
    /// <summary>넘기기 전에 멈췄다(아무것도 안 멈춤) — <see cref="ManualUpdateJobStatus.Reason"/> 가 사유.</summary>
    public const string Refused = "refused";
}

/// <summary>화면이 읽는 진행 상태.</summary>
public sealed record ManualUpdateJobStatus(
    string JobId,
    string Stage,
    string? Reason,
    string From,
    string? To,
    DateTime StartedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>「최신 버전 확인」 결과(읽기만 · 다운로드 0).</summary>
/// <remarks><see cref="Last"/> = 마지막 교체 한 번의 끝 상태(request.json · 두 모드 공통 · 되돌리기 GET 과 같은 모양).
/// 계약 §7 「결과는 다음 로그인 뒤 GET 이 알린다」 — 20260930작1 I-API(I-WEB 발견 §5-1).</remarks>
public sealed record ManualUpdateCheckResult(
    string CurrentVersion,
    string? LatestVersion,
    bool UpdateAvailable,
    string Reason,
    long? PackageSizeBytes,
    string? ReleaseNotes,
    bool Busy,
    LocalSwapLastResult? Last = null);

/// <summary>[예] 를 받았을 때의 즉답. <see cref="Accepted"/> 면 202 · 아니면 사유.</summary>
public sealed record ManualUpdateStartOutcome(bool Accepted, string Reason, ManualUpdateJobStatus? Job);

/// <summary>
/// 교체 일꾼에 넘길 주문(계약 §3 의 '호출자' 칸만). 런처가 schema·ticket·app_root·slot·api_port·requested_at 을 채운다.
/// ⬛ 「합칠 때 옮겨 담는다」 — I-API 가 HandOff 에서 <see cref="SwapLaunchInput"/> 로 옮겨 담는다.
/// </summary>
public sealed record ManualSwapOrder(
    string Mode,
    string From,
    string To,
    string MaterialKind,
    string MaterialPath,
    string MaterialSha256,
    string RequestedBy,
    string Entry,
    string? AutoState);

/// <summary>시험이 바꿔 끼울 수 있는 환경 값(운영 = 윈도 여부 · API 판).</summary>
public sealed record ManualUpdateEnvironment(Func<bool> IsSupportedPlatform, Func<string> CurrentVersion)
{
    public static ManualUpdateEnvironment Default { get; } =
        new(OperatingSystem.IsWindows, () => HitPan.API.VersionInfo.Current);
}

/// <summary>
/// 2026-09-30 작1 갈래 U — 수동 업데이트(설계 §13-2 · 작업지시서 §9·§10). 워치독과 독립된 모듈.
///
/// ■ 흐름(한 번에 하나 · 단일 인스턴스)
///   [예] → ① 판 확인(피드 · 서명 · 다운그레이드 0) → ② 받기(디스크 · 폴더 검사) → ③ 해시 대조
///        → ④ 자료 백업(API 기존 <see cref="IBackupService"/> · 실패 = 넘기기 0 · G-U4) → ⑤ 교체 일꾼에 넘기기
///   받기·백업은 수 분이 걸린다 ⇒ 요청은 202 로 곧장 돌려주고(터널 응답 시간 한도), 화면은 상태를 다시 묻는다.
///
/// ■ 막는 문(G-U1 은 컨트롤러 · 여기서는 요청 전 판정)
///   · 윈도 아님 → not_windows · 이미 진행 중 → swap_in_progress · 워치독 자동 업데이트 표식 잡힘 → update_in_progress(G-U5)
///   · 받기 직전·넘기기 직전에 워치독 표식을 한 번씩 더 본다(그 사이 자동 업데이트가 시작하는 틈).
///
/// ■ 하지 않는 것
///   · 워치독 프로세스 호출 0 · 동의 표 0(G-U3) · DB 쓰기 0(백업 이력 한 줄은 기존 백업 기능이 스스로 적는다) · 본사 전송 0(G-U6)
///   · 요청 본문에서 판·경로를 받지 않는다 — 판은 피드·설치 판으로 서버가 계산한다(병렬이슈 [3-V] 04).
/// </summary>
public sealed class ManualUpdateService
{
    private static readonly Regex ThreePartVersion = new(@"^\d{1,5}\.\d{1,5}\.\d{1,6}$", RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Hex = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

    private readonly IUpdateFeed _feed;
    private readonly IPackageFetcher _fetcher;
    private readonly IAutoUpdateLockProbe _autoLock;
    private readonly IServiceScopeFactory _scopes;
    private readonly ManualFolders _folders;
    private readonly ManualUsageLog _usage;
    private readonly ILocalSwapLauncher _launcher;
    private readonly ManualUpdateEnvironment _env;
    private readonly ILogger<ManualUpdateService> _logger;
    private readonly CancellationToken _stopping;

    /// <summary>
    /// 20260930작1 봉합2 10ⓑ(설계 15-1 · PM 결재 T-3) — 한 단계 <b>안</b> 예약 갱신 주기. 운영 5분(런처 만료 30분의 1/6).
    /// 0 이하면 끈다(시험이 「단계마다 갱신」만 따로 잴 때).
    /// </summary>
    public static readonly TimeSpan DefaultReservationRenewInterval = TimeSpan.FromMinutes(5);

    private readonly TimeSpan _renewInterval;

    private readonly object _gate = new();
    private Job? _job;

    public ManualUpdateService(
        IUpdateFeed feed,
        IPackageFetcher fetcher,
        IAutoUpdateLockProbe autoLock,
        IServiceScopeFactory scopes,
        ManualFolders folders,
        ManualUsageLog usage,
        ILocalSwapLauncher launcher,
        ManualUpdateEnvironment env,
        ILogger<ManualUpdateService> logger,
        IHostApplicationLifetime? lifetime = null,
        TimeSpan? reservationRenewInterval = null)
    {
        // 봉합2 10ⓑ — 생성자 주입(시험 10ms). DI 는 이 인자를 모르므로 기본값(null → 5분)을 쓴다.
        _renewInterval = reservationRenewInterval ?? DefaultReservationRenewInterval;
        _feed = feed;
        _fetcher = fetcher;
        _autoLock = autoLock;
        _scopes = scopes;
        _folders = folders;
        _usage = usage;
        _launcher = launcher;
        _env = env;
        _logger = logger;
        _stopping = lifetime?.ApplicationStopping ?? CancellationToken.None;
    }

    /// <summary>마지막으로 시작한 작업의 끝(시험이 기다릴 때만 쓴다).</summary>
    public Task LastRun { get; private set; } = Task.CompletedTask;

    /// <summary>「최신 버전 확인」 — 피드·서명·판 비교만. 다운로드·백업·기록 0.</summary>
    public async Task<ManualUpdateCheckResult> CheckAsync(CancellationToken ct)
    {
        var current = _env.CurrentVersion();
        // ⬛ 봉합 전: var busy = IsBusy() || _autoLock.IsAutoUpdateInProgress()
        //            || _launcher.CheckBusy() is SwapReasons.SwapInProgress or SwapReasons.UpdateInProgress;
        // 20260930작1 봉합 07(계약 §4) — 사전 판정(CheckReady) 을 바쁨(CheckBusy) 앞에서 본다. 한 번만 부른다(폴더 문지기 1회).
        var notReady = _env.IsSupportedPlatform() ? _launcher.CheckReady() : null;
        var launcherBusy = notReady is null ? _launcher.CheckBusy() : null;
        var busy = IsBusy() || _autoLock.IsAutoUpdateInProgress()
                   || launcherBusy is SwapReasons.SwapInProgress or SwapReasons.UpdateInProgress;
        var last = LocalRollbackService.ToLast(_launcher.ReadLast());

        if (!_env.IsSupportedPlatform())
            return new ManualUpdateCheckResult(current, null, false, ManualUpdateReasons.NotWindows, null, null, busy, last);

        // 봉합 07 — 넘길 수 없는 설치(슬롯·루트·일꾼 원본 없음 · 작업 폴더 문지기 실패)면 피드도 안 묻고 그 사유(「가능」 거짓 0).
        if (notReady is not null)
            return new ManualUpdateCheckResult(current, null, false, notReady, null, null, busy, last);

        // 봉합 06ⓑ(L 개발명세서 §5 ⚠️) — 끝나지 않은 교체(swap_interrupted)는 「진행 중」도 「가능」도 아니다. 그 사유를 그대로 보인다.
        if (launcherBusy == SwapReasons.SwapInterrupted)
            return new ManualUpdateCheckResult(current, null, false, launcherBusy, null, null, busy, last);

        // 20260930작1 봉합2 N-2(설계 15-1) — 워치독 .old 가 남아 있으면(update_cleanup_pending) 「가능」이 아니다.
        //   [예] 는 런처 CheckBusy(owner) 가 받기·백업 전에 같은 사유로 거부한다 ⇒ 확인 화면도 같은 사유를 보인다(피드 안 묻는다).
        if (launcherBusy == SwapReasons.UpdateCleanupPending)
            return new ManualUpdateCheckResult(current, null, false, launcherBusy, null, null, busy, last);

        var (reason, package) = await CheckFeedAsync(current, ct).ConfigureAwait(false);
        return new ManualUpdateCheckResult(
            current,
            package?.Version,
            package is not null,
            reason,
            package?.SizeBytes,
            package?.ReleaseNotes,
            busy,
            last);
    }

    /// <summary>tenant 의 진행 상태(없으면 null). 다른 tenant 의 작업은 보여 주지 않는다.</summary>
    public ManualUpdateJobStatus? GetStatus(string tenantId)
    {
        lock (_gate)
        {
            return _job is not null && string.Equals(_job.TenantId, tenantId, StringComparison.Ordinal)
                ? _job.Snapshot()
                : null;
        }
    }

    /// <summary>[예] — 요청 전 판정 후 뒤에서 ①~⑤ 를 돈다. tenantId·userId 는 JWT 에서만(#2).</summary>
    public ManualUpdateStartOutcome Start(string tenantId, string userId, string entry)
    {
        var from = _env.CurrentVersion();
        var safeEntry = entry == "login" ? "login" : "menu";

        if (!_env.IsSupportedPlatform())
            return RefuseBeforeStart(from, safeEntry, userId, ManualUpdateReasons.NotWindows);

        Job job;
        lock (_gate)
        {
            if (_job is { IsRunning: true })
                return RefuseBeforeStart(from, safeEntry, userId, ManualUpdateReasons.SwapInProgress);

            if (_autoLock.IsAutoUpdateInProgress())
                return RefuseBeforeStart(from, safeEntry, userId, ManualUpdateReasons.UpdateInProgress);

            // 20260930작1 I-API — 되돌리기와 한 틀(같은 런처·같은 요청서): 교체 진행 중 · 쿨다운(10분 · 두 모드 합쳐) ·
            // 워치독 표식이면 받기·백업 전에 거부한다(병렬이슈 03 순서 「자물쇠 → 전제 재판정 → 받기 → 백업」).
            // ⬛ 봉합 전: if (_launcher.CheckBusy() is { } launcherBusy) return RefuseBeforeStart(…, launcherBusy);
            // 20260930작1 봉합 07·F-4(계약 §4) — CheckReady → CheckBusy(owner) → TryReserve(owner) 순서. 받기·백업 **전**.
            //   07: 넘길 수 없는 설치면 받기 0 · 백업 0(백업 30개 밀림 = 복원 지점 소실 방지 · G-U7).
            //   F-4: 받기·백업 동안 되돌리기가 끼어들지 못하게 예약한다(G-U5d). 넘기기 뒤·거부·실패 때 RunAsync 가 푼다.
            if (_launcher.CheckReady() is { } notReady)
                return RefuseBeforeStart(from, safeEntry, userId, notReady);

            var jobId = Guid.NewGuid().ToString("N");
            var owner = ReservationOwner(jobId);
            if (_launcher.CheckBusy(owner) is { } launcherBusy)
                return RefuseBeforeStart(from, safeEntry, userId, launcherBusy);
            if (!_launcher.TryReserve(owner))
                return RefuseBeforeStart(from, safeEntry, userId, ManualUpdateReasons.SwapInProgress);

            job = new Job(jobId, tenantId, userId, safeEntry, from);
            _job = job;
        }

        _logger.LogWarning("[ManualUpdate] 수동 업데이트 시작 — 입구 {Entry} · 현재 {From} · 요청자 {User} (자동 경로 결함 신호일 수 있다)",
            safeEntry, from, userId);
        LastRun = Task.Run(() => RunAsync(job), CancellationToken.None);
        return new ManualUpdateStartOutcome(true, ManualUpdateReasons.Ok, job.Snapshot());
    }

    /// <summary>봉합 F-4 — 런처 예약 주인(한 작업 = 한 주인). <see cref="SwapLaunchInput.Owner"/> 에도 같은 값을 넣는다(계약 §4).</summary>
    internal static string ReservationOwner(string jobId) => "manual-update:" + jobId;

    private bool IsBusy()
    {
        lock (_gate) return _job is { IsRunning: true };
    }

    private ManualUpdateStartOutcome RefuseBeforeStart(string from, string entry, string userId, string reason)
    {
        _logger.LogWarning("[ManualUpdate] 수동 업데이트 거부(시작 전) — 사유 {Reason}", reason);
        _usage.RecordBeforeHandOff(new ManualUsageEntry(
            DateTime.UtcNow, entry, "update", from, null, ManualUpdateStages.Refused, reason, userId, null, null));
        return new ManualUpdateStartOutcome(false, reason, null);
    }

    private async Task<(string Reason, FeedPackage? Package)> CheckFeedAsync(string current, CancellationToken ct)
    {
        var result = await _feed.CheckAsync(current, ct).ConfigureAwait(false);
        switch (result.Status)
        {
            case FeedCheckStatus.Unreachable:
                return (ManualUpdateReasons.FeedUnreachable, null);
            case FeedCheckStatus.Invalid:
                return (ManualUpdateReasons.SignatureInvalid, null);
            case FeedCheckStatus.NotNewer:
                return (ManualUpdateReasons.NoNewerVersion, null);
        }

        var pkg = result.Package;
        if (pkg is null || !ThreePartVersion.IsMatch(pkg.Version) || !Sha256Hex.IsMatch(pkg.Sha256)
            || string.IsNullOrWhiteSpace(pkg.DownloadUrl))
        {
            _logger.LogWarning("[ManualUpdate] 피드 값 형식 불량 — 판·해시·주소를 믿지 않는다");
            return (ManualUpdateReasons.SignatureInvalid, null);
        }

        // 한 번 더(방어 겹): 창구가 무엇을 돌려주든 피드 판 ≤ 현재 판이면 거부 — 다운그레이드 0(G-U2).
        if (!WatchdogUpdateCoreAdapter.IsNewerVersion(pkg.Version, current, out var why))
        {
            _logger.LogWarning("[ManualUpdate] 새 판 아님(서비스 재확인) — {Why}", why);
            return (ManualUpdateReasons.NoNewerVersion, null);
        }

        return (ManualUpdateReasons.Ok, pkg);
    }

    private async Task RunAsync(Job job)
    {
        var ct = _stopping;
        // 20260930작1 봉합2 10ⓑ — 한 단계 안 주기 갱신(받기·백업이 30분을 넘어도 예약이 만료되지 않게). 끝나면 멈춘다(finally).
        using var renewStop = new CancellationTokenSource();
        var renewLoop = _renewInterval > TimeSpan.Zero
            ? Task.Run(() => RenewLoopAsync(job, renewStop.Token), CancellationToken.None)
            : Task.CompletedTask;
        try
        {
            // ⬛ 봉합2 전: job.Move(…) 다섯 곳(Checking·Downloading·Verifying·BackingUp·HandingOff) — 예약은 Start 때 한 번만 찍혔다.
            // 20260930작1 봉합2 10ⓐⓒ — 단계를 옮길 때마다 예약 갱신 · 실패면 그 자리에서 swap_in_progress 로 끝(백업·넘기기 0).
            if (!MoveRenewed(job, ManualUpdateStages.Checking)) return;
            var (reason, pkg) = await CheckFeedAsync(job.From, ct).ConfigureAwait(false);
            if (pkg is null)
            {
                Finish(job, reason);
                return;
            }
            job.SetTo(pkg.Version);

            // ② 받기
            if (!MoveRenewed(job, ManualUpdateStages.Downloading)) return;
            try
            {
                ManualFolders.EnsureRestricted(_folders.StagingDir);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "[ManualUpdate] 받는 폴더 검사 실패 — 받지 않는다");
                Finish(job, ManualUpdateReasons.DownloadFailed);
                return;
            }

            if (!_fetcher.HasEnoughSpace(pkg.SizeBytes, _folders.StagingDir))
            {
                Finish(job, ManualUpdateReasons.DiskLow);
                return;
            }

            if (_autoLock.IsAutoUpdateInProgress())
            {
                Finish(job, ManualUpdateReasons.UpdateInProgress);
                return;
            }

            string zipPath;
            try
            {
                zipPath = await _fetcher.DownloadAsync(pkg, _folders.StagingDir, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning("[ManualUpdate] API 종료로 받기를 멈췄다");
                Finish(job, ManualUpdateReasons.DownloadFailed);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ManualUpdate] 패키지 받기 실패");
                Finish(job, ManualUpdateReasons.DownloadFailed);
                return;
            }

            // ③ 해시 — 받은 파일이 연결(재분석 지점)이거나 받는 폴더 밖이면 믿지 않는다.
            if (!MoveRenewed(job, ManualUpdateStages.Verifying)) return;
            var fullZip = Path.GetFullPath(zipPath);
            var stagingRoot = Path.GetFullPath(_folders.StagingDir) + Path.DirectorySeparatorChar;
            if (!fullZip.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase) || ManualFolders.IsReparsePoint(fullZip))
            {
                _logger.LogWarning("[ManualUpdate] 받은 파일 위치가 받는 폴더가 아니거나 연결 파일이다 — 쓰지 않는다");
                Finish(job, ManualUpdateReasons.DownloadFailed);
                return;
            }

            if (!await _fetcher.VerifySha256Async(fullZip, pkg.Sha256, ct).ConfigureAwait(false))
            {
                TryDelete(fullZip);
                Finish(job, ManualUpdateReasons.HashMismatch);
                return;
            }

            // ④ 자료 백업 — 실패하면 넘기지 않는다(G-U4 · 업데이트 백업 원칙 「백업 실패 시 업데이트 차단」).
            if (!MoveRenewed(job, ManualUpdateStages.BackingUp)) return;
            if (!await RunBackupAsync(job.TenantId, ct).ConfigureAwait(false))
            {
                Finish(job, ManualUpdateReasons.BackupFailed);
                return;
            }

            // ⑤ 넘기기 직전 — 자동 업데이트 표식을 한 번 더(G-U5).
            if (_autoLock.IsAutoUpdateInProgress())
            {
                Finish(job, ManualUpdateReasons.UpdateInProgress);
                return;
            }

            if (!MoveRenewed(job, ManualUpdateStages.HandingOff)) return;
            var order = new ManualSwapOrder(
                Mode: SwapModes.Update,
                From: job.From,
                To: pkg.Version,
                MaterialKind: SwapMaterialKinds.ManualZip,
                MaterialPath: fullZip,
                MaterialSha256: pkg.Sha256.ToLowerInvariant(),
                RequestedBy: job.UserId,
                Entry: job.Entry,
                AutoState: null);
            var handOffReason = HandOff(order, ReservationOwner(job.Id)); // 봉합 F-4 — 자기 예약에 막히지 않게 같은 owner
            if (handOffReason == ManualUpdateReasons.Ok)
            {
                job.Move(ManualUpdateStages.HandedOff);
                _logger.LogWarning("[ManualUpdate] 교체 일꾼에 넘겼다 — {From} → {To}", order.From, order.To);
                return;
            }
            Finish(job, handOffReason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ManualUpdate] 수동 업데이트 중 예상 못 한 오류 — 넘기지 않았다");
            Finish(job, ManualUpdateReasons.DownloadFailed);
        }
        finally
        {
            // 봉합2 10ⓑ — 주기 갱신을 먼저 멈춘다(풀어 놓은 예약을 주기 갱신이 다시 쥐지 않게).
            renewStop.Cancel();
            await renewLoop.ConfigureAwait(false);
            // 봉합 F-4 — 넘기기 성공 뒤는 swap.lock 이 이어받고, 거부·실패면 교체가 없다 ⇒ 어느 끝이든 예약을 푼다.
            _launcher.Release(ReservationOwner(job.Id));
        }
    }

    /// <summary>
    /// 20260930작1 봉합2 10ⓐ(설계 15-1) — 예약 갱신 = 같은 주인으로 <see cref="ILocalSwapLauncher.TryReserve"/> 재호출(시각을 새로 찍는다).
    /// <c>false</c> = 만료 뒤 남이 쥐었다. 한 번 잃으면 끝까지 잃은 것으로 본다(남이 풀어도 다시 쥐지 않는다 — 그 사이 교체가 있었을 수 있다).
    /// </summary>
    private bool RenewReservation(Job job)
    {
        if (job.ReservationLost) return false;
        if (_launcher.TryReserve(ReservationOwner(job.Id))) return true;
        job.MarkReservationLost();
        _logger.LogWarning("[ManualUpdate] 예약을 갱신하지 못했다(다른 업데이트·되돌리기가 쥐었다) — 단계 {Stage} · {From} → {To}",
            job.Snapshot().Stage, job.From, job.To ?? "-");
        return false;
    }

    /// <summary>봉합2 10ⓐⓒ — 단계를 옮기고 예약을 갱신한다. 갱신 실패면 그 자리에서 <c>swap_in_progress</c> 로 끝(백업·넘기기 0).</summary>
    private bool MoveRenewed(Job job, string stage)
    {
        if (RenewReservation(job))
        {
            job.Move(stage);
            return true;
        }
        Finish(job, ManualUpdateReasons.SwapInProgress);
        return false;
    }

    /// <summary>
    /// 봉합2 10ⓑ(PM 결재 T-3) — 한 단계 안 주기 갱신. 실패하면 잃었다는 표시만 남기고 멈춘다 —
    /// 도는 받기·백업은 끊지 않고(백업을 반쯤에서 끊지 않는다) 다음 단계 문턱(<see cref="MoveRenewed"/>)이 끝낸다.
    /// </summary>
    private async Task RenewLoopAsync(Job job, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_renewInterval, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("[ManualUpdate] 예약 주기 갱신을 멈췄다 — 작업 {Job}", job.Id);
                return;
            }

            if (stop.IsCancellationRequested) return;
            try
            {
                if (!RenewReservation(job)) return;
            }
            catch (Exception ex)
            {
                // finally 의 await 가 던지지 않게(던지면 예약 해제를 건너뛴다). 못 쟀으면 잃은 것으로 본다 — 다음 단계 문턱이 끝낸다.
                _logger.LogWarning(ex, "[ManualUpdate] 예약 주기 갱신 중 오류 — 잃은 것으로 본다 · 작업 {Job}", job.Id);
                job.MarkReservationLost();
                return;
            }
        }
    }

    /// <summary>
    /// ⑤ 교체 일꾼에 넘기기 — 갈래 A 의 <see cref="ILocalSwapLauncher"/>(바쁨 판정 · 원자 잠금 <c>swap.lock</c> · 쿨다운 ·
    /// <c>request.json</c> · 1회용 SYSTEM 작업 등록)에 넘긴다(20260930작1 I-API).
    /// 판·재료·해시는 전부 서버가 피드·받은 파일에서 계산한 값이다(요청 본문 유래 0 · 병렬이슈 04).
    /// 거부면 런처 사유 코드(계약 §6)를 그대로 돌려준다 — 작업 등록 0 · 잠금 풀림(런처가 보장).
    /// ⬛ U 초판의 「launcher_not_wired 로 멈춘다」 자리는 이것으로 대체됐다.
    /// </summary>
    private string HandOff(ManualSwapOrder order, string? owner = null)
    {
        var result = _launcher.Launch(new SwapLaunchInput(
            Mode: order.Mode,
            From: order.From,
            To: order.To,
            Material: new SwapMaterial { Kind = order.MaterialKind, Path = order.MaterialPath, Sha256 = order.MaterialSha256 },
            RequestedBy: order.RequestedBy,
            Entry: order.Entry,
            AutoState: order.AutoState,
            Ticket: null,
            Owner: owner)); // 봉합 F-4(계약 §4) — 예약한 주인 그대로
        if (result.Started) return ManualUpdateReasons.Ok;
        _logger.LogWarning("[ManualUpdate] 교체 일꾼이 받지 않았다 — 사유 {Reason} · {From} → {To}", result.Reason, order.From, order.To);
        return result.Reason;
    }

    private async Task<bool> RunBackupAsync(string tenantId, CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var backup = scope.ServiceProvider.GetRequiredService<IBackupService>();
            var result = await backup.RunBackupAsync(tenantId, "manual_update", ct).ConfigureAwait(false);
            if (result is { Success: true })
                return true;

            _logger.LogWarning("[ManualUpdate] 업데이트 전 자료 백업 실패 — 넘기지 않는다: {Error}", result?.Error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ManualUpdate] 업데이트 전 자료 백업 중 오류 — 넘기지 않는다");
            return false;
        }
    }

    private void Finish(Job job, string reason)
    {
        job.Refuse(reason);
        _logger.LogWarning("[ManualUpdate] 수동 업데이트를 넘기기 전에 멈췄다 — 사유 {Reason} · {From} → {To}", reason, job.From, job.To ?? "-");
        _usage.RecordBeforeHandOff(new ManualUsageEntry(
            DateTime.UtcNow, job.Entry, "update", job.From, job.To, ManualUpdateStages.Refused, reason, job.UserId, null, null));
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[ManualUpdate] 해시가 틀린 받은 파일을 지우지 못했다");
        }
    }

    private sealed class Job
    {
        private readonly object _lock = new();
        private string _stage = ManualUpdateStages.Checking;
        private string? _reason;
        private string? _to;
        private DateTime _updated;

        public Job(string id, string tenantId, string userId, string entry, string from)
        {
            Id = id;
            TenantId = tenantId;
            UserId = userId;
            Entry = entry;
            From = from;
            Started = DateTime.UtcNow;
            _updated = Started;
        }

        public string Id { get; }
        public string TenantId { get; }
        public string UserId { get; }
        public string Entry { get; }
        public string From { get; }
        public DateTime Started { get; }

        public string? To
        {
            get { lock (_lock) return _to; }
        }

        public bool IsRunning
        {
            get
            {
                lock (_lock)
                    return _stage != ManualUpdateStages.Refused && _stage != ManualUpdateStages.HandedOff;
            }
        }

        public void SetTo(string to)
        {
            lock (_lock) _to = to;
        }

        // 봉합2 10ⓒ — 예약을 한 번 잃었나(주기 갱신·단계 갱신 공통 · 한 번 잃으면 되돌리지 않는다)
        private bool _reservationLost;

        public bool ReservationLost
        {
            get { lock (_lock) return _reservationLost; }
        }

        public void MarkReservationLost()
        {
            lock (_lock) _reservationLost = true;
        }

        public void Move(string stage)
        {
            lock (_lock)
            {
                _stage = stage;
                _updated = DateTime.UtcNow;
            }
        }

        public void Refuse(string reason)
        {
            lock (_lock)
            {
                _stage = ManualUpdateStages.Refused;
                _reason = reason;
                _updated = DateTime.UtcNow;
            }
        }

        public ManualUpdateJobStatus Snapshot()
        {
            lock (_lock)
                return new ManualUpdateJobStatus(Id, _stage, _reason, From, _to, Started, _updated);
        }
    }
}
