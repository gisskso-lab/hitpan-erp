using System.Text.RegularExpressions;
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

    /// <summary>
    /// ⚠️ 임시 — 갈래 A 의 교체 일꾼 런처(<c>ILocalSwapLauncher</c>)가 아직 이 갈래에 없다. 합칠 때 이 코드는 사라진다
    /// (개발명세서 U 「못 한 것」 1번). 계약 §6 표에 없는 코드라 화면(F)은 「고객센터로 연락 주세요」로 다룬다.
    /// </summary>
    public const string LauncherNotWired = "launcher_not_wired";
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
public sealed record ManualUpdateCheckResult(
    string CurrentVersion,
    string? LatestVersion,
    bool UpdateAvailable,
    string Reason,
    long? PackageSizeBytes,
    string? ReleaseNotes,
    bool Busy);

/// <summary>[예] 를 받았을 때의 즉답. <see cref="Accepted"/> 면 202 · 아니면 사유.</summary>
public sealed record ManualUpdateStartOutcome(bool Accepted, string Reason, ManualUpdateJobStatus? Job);

/// <summary>
/// 교체 일꾼에 넘길 주문(계약 §3 의 '호출자' 칸만). 런처가 schema·ticket·app_root·slot·api_port·requested_at 을 채운다.
/// ⚠️ 갈래 A 의 <c>SwapRequest</c> 로 옮겨 담는 것은 합칠 때(개발명세서 U 「못 한 것」 1번).
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
    private readonly ManualUpdateEnvironment _env;
    private readonly ILogger<ManualUpdateService> _logger;
    private readonly CancellationToken _stopping;

    private readonly object _gate = new();
    private Job? _job;

    public ManualUpdateService(
        IUpdateFeed feed,
        IPackageFetcher fetcher,
        IAutoUpdateLockProbe autoLock,
        IServiceScopeFactory scopes,
        ManualFolders folders,
        ManualUsageLog usage,
        ManualUpdateEnvironment env,
        ILogger<ManualUpdateService> logger,
        IHostApplicationLifetime? lifetime = null)
    {
        _feed = feed;
        _fetcher = fetcher;
        _autoLock = autoLock;
        _scopes = scopes;
        _folders = folders;
        _usage = usage;
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
        var busy = IsBusy() || _autoLock.IsAutoUpdateInProgress();

        if (!_env.IsSupportedPlatform())
            return new ManualUpdateCheckResult(current, null, false, ManualUpdateReasons.NotWindows, null, null, busy);

        var (reason, package) = await CheckFeedAsync(current, ct).ConfigureAwait(false);
        return new ManualUpdateCheckResult(
            current,
            package?.Version,
            package is not null,
            reason,
            package?.SizeBytes,
            package?.ReleaseNotes,
            busy);
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

            job = new Job(Guid.NewGuid().ToString("N"), tenantId, userId, safeEntry, from);
            _job = job;
        }

        _logger.LogWarning("[ManualUpdate] 수동 업데이트 시작 — 입구 {Entry} · 현재 {From} · 요청자 {User} (자동 경로 결함 신호일 수 있다)",
            safeEntry, from, userId);
        LastRun = Task.Run(() => RunAsync(job), CancellationToken.None);
        return new ManualUpdateStartOutcome(true, ManualUpdateReasons.Ok, job.Snapshot());
    }

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
        try
        {
            job.Move(ManualUpdateStages.Checking);
            var (reason, pkg) = await CheckFeedAsync(job.From, ct).ConfigureAwait(false);
            if (pkg is null)
            {
                Finish(job, reason);
                return;
            }
            job.SetTo(pkg.Version);

            // ② 받기
            job.Move(ManualUpdateStages.Downloading);
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
            job.Move(ManualUpdateStages.Verifying);
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
            job.Move(ManualUpdateStages.BackingUp);
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

            job.Move(ManualUpdateStages.HandingOff);
            var order = new ManualSwapOrder(
                Mode: "update",
                From: job.From,
                To: pkg.Version,
                MaterialKind: "manual_zip",
                MaterialPath: fullZip,
                MaterialSha256: pkg.Sha256.ToLowerInvariant(),
                RequestedBy: job.UserId,
                Entry: job.Entry,
                AutoState: null);
            var handOffReason = HandOff(order);
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
    }

    /// <summary>
    /// ⑤ 교체 일꾼에 넘기기. ⚠️ 미완 — 갈래 A 의 <c>ILocalSwapLauncher</c>(request.json 쓰기 · 바쁨 판정 · 1회용 번호 ·
    /// 원자 잠금 · 쿨다운 · 1회용 SYSTEM 작업 등록)가 이 갈래에 아직 없다. 인터페이스를 이쪽에 겹쳐 만들지 않는다(PM 지시).
    /// 합칠 때 이 한 곳에서 <paramref name="order"/> 를 A 의 SwapRequest 로 옮겨 Launch 를 부르고, 그 사유 코드를 그대로 돌려준다.
    /// 그 전까지는 아무것도 등록하지 않고 <see cref="ManualUpdateReasons.LauncherNotWired"/> 로 멈춘다(받은 zip 은 staging 에 남는다).
    /// </summary>
    private string HandOff(ManualSwapOrder order)
    {
        _logger.LogWarning("[ManualUpdate] 교체 일꾼 런처가 아직 연결되지 않아 넘기지 않았다 — {From} → {To}", order.From, order.To);
        return ManualUpdateReasons.LauncherNotWired;
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
