using System.Net;
using HitPan.API.Services.LocalSwap;
using HitPan.API.Services.ManualUpdate;

namespace HitPan.API.Services.LocalRollback;

/// <summary>
/// 20260930작1 1.3.50 확대 갈래 N2 — 되돌리기 「세 번째 길」의 받기 일(설계 §19-1 조각 B · §19-6 (ㄴ) · 작업지시서 §18).
/// 재료 ①·② 가 둘 다 없고 서는 조건 T1~T6 이 선 뒤 [예] 를 받았을 때만 돈다(판정은 <see cref="LocalRollbackService"/>).
/// </summary>
/// <remarks>
/// <para>순서는 <c>ManualUpdateService.RunAsync</c> 를 따르되 코드는 나누지 않는다(설계 19-1):
/// ① 받는 폴더 검사 → ② 자동 업데이트 표식 → ③ 저장본 다시 확인 → (19-6 ㄴ) 받을 자리에 맞는 파일이 이미 있으면 받지 않음
/// → ④ 받기(주소 = 저장본 <c>downloadUrl</c> 그대로 · 이름 조립 0) → ⑤ 위치·연결 파일 → ⑥ 길이·sha256
/// → ⑦ 풀 공간 → ⑧ 자동 업데이트 표식 한 번 더 → ⑨ 교체 일꾼에 넘김(<c>manual_zip</c> · sha256 소문자).</para>
/// <para>🔴 받는 함수·해시 함수·공간 판정·폴더 검사·예약·런처는 전부 기존 것(새 암호 코드 0 · 새 상수 0 · 새 사유 0).</para>
/// <para>🔴 받기 크기 상한(병렬이슈 23) = 서명된 크기. 받는 동안 파일이 그보다 커지면 받기를 끊고 지운다(길이 다름 = <c>hash_mismatch</c>).
/// 받는 함수(기존)를 바꾸지 않으므로 「받는 도중」은 파일 크기 바뀜 알림으로 재고, 끝난 뒤 길이 대조는 언제나 한다.</para>
/// <para>받은 파일 정리: 이 일이 받은 파일은 넘기기 전에 멈추면 그 자리에서 지운다. 받기 전부터 있던 맞는 파일(본사·대리점이 넣어 둔 것)은
/// 남긴다 — 맞는지는 서명된 sha256·크기로 이미 확인했고, 남은 것은 다음 상태 확인의 정리(X-5 · 19-6 보정)가 다룬다.</para>
/// </remarks>
public sealed class PreviousPackageFetch
{
    private readonly IPreviousPackageFeed _feed;
    private readonly IPackageFetcher _fetcher;
    private readonly IAutoUpdateLockProbe _autoLock;
    private readonly ILocalSwapLauncher _launcher;
    private readonly ILocalSwapEnvironment _env;
    private readonly ILogger _logger;
    private readonly TimeSpan _renewInterval;
    private readonly CancellationToken _stopping;
    private readonly object _gate = new();
    private Job? _job;

    /// <param name="renewInterval">예약 갱신 주기 — 운영은 <see cref="ManualUpdateService.DefaultReservationRenewInterval"/>(같은 값 재사용).
    /// 0 이하이면 단계 문턱에서만 갱신한다.</param>
    /// <param name="stopping">API 종료 신호(받기를 멈춘다).</param>
    public PreviousPackageFetch(
        IPreviousPackageFeed feed,
        IPackageFetcher fetcher,
        IAutoUpdateLockProbe autoLock,
        ILocalSwapLauncher launcher,
        ILocalSwapEnvironment env,
        ILogger logger,
        TimeSpan renewInterval,
        CancellationToken stopping)
    {
        _feed = feed;
        _fetcher = fetcher;
        _autoLock = autoLock;
        _launcher = launcher;
        _env = env;
        _logger = logger;
        _renewInterval = renewInterval;
        _stopping = stopping;
    }

    /// <summary>마지막으로 건 받기 일의 끝(시험이 기다릴 때만 쓴다).</summary>
    public Task LastRun { get; private set; } = Task.CompletedTask;

    /// <summary>지금(또는 마지막) 받기 진행 — 건 적이 없으면 null. 메모리만(설계 19-3).</summary>
    public LocalRollbackFetchStatus? Current
    {
        get
        {
            lock (_gate) return _job?.Snapshot();
        }
    }

    /// <summary>
    /// [예] 뒤 — 예약(<paramref name="owner"/>)을 쥐고 받기 일을 뒤에서 시작한다. 남이 쥐고 있으면 <c>swap_in_progress</c>.
    /// 받기를 건 뒤는 <c>Started=true · ok</c>(받기가 첫 단계 · 설계 19-1) — 그 뒤 결과는 <see cref="Current"/> 가 알린다.
    /// </summary>
    /// <param name="material">판정이 선 재료(<c>manual_zip</c> · <c>{app}\manual\staging\hitpan-{P}.zip</c> · P).</param>
    /// <param name="from">지금 판.</param>
    public SwapLaunchResult Begin(RollbackMaterial material, string from, string userId, string ticket, string owner)
    {
        if (!_launcher.TryReserve(owner)) return SwapLaunchResult.Refuse(SwapReasons.SwapInProgress);
        var started = false;
        try
        {
            // 첫 단계 = 받을 자리에 파일이 이미 있으면 「확인 중」, 없으면 「받는 중」(19-6 ㄴ · G-NP8 은 downloading 없이 verifying 부터).
            var first = File.Exists(material.Path) ? ManualUpdateStages.Verifying : ManualUpdateStages.Downloading;
            var job = new Job(material, from, userId, ticket, owner, first, _env.UtcNow);
            lock (_gate) _job = job;
            LastRun = Task.Run(() => RunAsync(job), CancellationToken.None);
            started = true;
            _logger.LogInformation("[LocalRollback] 이전 판 받기를 시작합니다 — {From} → {To} · 첫 단계 {Stage} · 사용자 {User}",
                from, material.Version, first, userId);
            return new SwapLaunchResult(true, SwapReasons.Ok, ticket);
        }
        finally
        {
            if (!started) _launcher.Release(owner);
        }
    }

    private async Task RunAsync(Job job)
    {
        var ct = _stopping;
        using var renewStop = new CancellationTokenSource();
        var renewLoop = _renewInterval > TimeSpan.Zero
            ? Task.Run(() => RenewLoopAsync(job, renewStop.Token), CancellationToken.None)
            : Task.CompletedTask;
        var target = job.Material.Path;
        var weDownloaded = false;
        try
        {
            var app = _env.AppRoot;
            if (app is null)
            {
                Refuse(job, SwapReasons.RequestInvalid);
                return;
            }
            var staging = new ManualFolders(app).StagingDir;
            var stagingRoot = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;

            // ① 받는 폴더 검사(수동 업데이트와 같은 입구)
            try
            {
                ManualFolders.EnsureRestricted(staging);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "[LocalRollback] 받는 폴더 검사 실패 — 이전 판을 받지 않습니다");
                Refuse(job, ManualUpdateReasons.DownloadFailed);
                return;
            }

            // ② 자동 업데이트 표식
            if (_autoLock.IsAutoUpdateInProgress())
            {
                Refuse(job, SwapReasons.UpdateInProgress);
                return;
            }

            // ③ 저장본 다시 확인(상태 확인 뒤 바뀌었으면 여기서 멈춘다 · 서명은 기존 검증기)
            var pkg = _feed.LoadVerified(job.To);
            if (pkg is null || !RollbackMaterialFinder.TryParse(pkg.Version, out var inside)
                || !RollbackMaterialFinder.TryParse(job.To, out var wanted) || inside != wanted)
            {
                Refuse(job, ManualUpdateReasons.SignatureInvalid);
                return;
            }

            // (19-6 ㄴ) 받을 자리에 파일이 이미 있으면 — 위치·연결 → 길이 → sha. 맞으면 받지 않는다 / 하나라도 틀리면 지우고 받는다.
            var ready = false;
            if (File.Exists(target))
            {
                if (!MoveRenewed(job, ManualUpdateStages.Verifying)) return;
                if (IsInside(target, stagingRoot) && !ManualFolders.IsReparsePoint(target)
                    && await MatchesAsync(target, pkg, ct).ConfigureAwait(false))
                {
                    ready = true;
                    _logger.LogInformation("[LocalRollback] 받을 자리에 맞는 이전 판 파일이 이미 있어 받지 않습니다 — {To}", job.To);
                }
                else
                {
                    _logger.LogWarning("[LocalRollback] 받을 자리의 파일이 서명된 크기·해시와 맞지 않아 쓰지 않고 지운 뒤 받습니다 — {To}", job.To);
                    TryDelete(target);
                    if (File.Exists(target))
                    {
                        Refuse(job, ManualUpdateReasons.DownloadFailed);
                        return;
                    }
                }
            }

            if (!ready)
            {
                // ④ 받기 — 주소는 저장본 downloadUrl 그대로(기존 DownloadAsync · 이름 조립 0)
                if (!MoveRenewed(job, ManualUpdateStages.Downloading)) return;
                if (!_fetcher.HasEnoughSpace(pkg.SizeBytes, staging))
                {
                    Refuse(job, SwapReasons.DiskLow);
                    return;
                }

                string zipPath;
                weDownloaded = true;
                try
                {
                    zipPath = await DownloadCappedAsync(pkg, staging, target, ct).ConfigureAwait(false);
                }
                catch (SizeCapExceededException)
                {
                    _logger.LogWarning("[LocalRollback] 받는 파일이 서명된 크기({Size})를 넘어 받기를 끊었습니다 — {To}", pkg.SizeBytes, job.To);
                    TryDelete(target);
                    Refuse(job, SwapReasons.HashMismatch);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _logger.LogWarning("[LocalRollback] API 종료로 이전 판 받기를 멈췄습니다 — {To}", job.To);
                    TryDelete(target);
                    Refuse(job, ManualUpdateReasons.DownloadFailed);
                    return;
                }
                catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    // X-6 — 본사 보관본 없음은 새 사유 없이 「이전 판 없음」(화면이 받기 전용 문구로 가른다)
                    _logger.LogWarning(ex, "[LocalRollback] 본사에 {To} 판 파일이 더 이상 없습니다(HTTP {Code})", job.To, (int)ex.StatusCode!.Value);
                    TryDelete(target);
                    Refuse(job, SwapReasons.NoPreviousVersion);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[LocalRollback] 이전 판 받기 실패 — {To}", job.To);
                    TryDelete(target);
                    Refuse(job, ManualUpdateReasons.DownloadFailed);
                    return;
                }

                // ⑤ 받은 파일이 받을 자리가 아니거나 연결 파일이면 믿지 않는다(ManualUpdateService 와 같은 검사 + 판정한 경로와 같아야)
                if (!MoveRenewed(job, ManualUpdateStages.Verifying)) { TryDelete(target); return; }
                var full = Path.GetFullPath(zipPath);
                if (!IsInside(full, stagingRoot) || ManualFolders.IsReparsePoint(full)
                    || !string.Equals(full, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("[LocalRollback] 받은 파일 위치가 받을 자리가 아니거나 연결 파일입니다 — 쓰지 않습니다");
                    TryDelete(target);
                    Refuse(job, ManualUpdateReasons.DownloadFailed);
                    return;
                }

                // ⑥ 길이 == 서명된 크기 · sha256 == 서명된 해시(기존 VerifySha256Async)
                if (!await MatchesAsync(full, pkg, ct).ConfigureAwait(false))
                {
                    TryDelete(target);
                    Refuse(job, SwapReasons.HashMismatch);
                    return;
                }
            }

            // ⑦ 풀 공간 — 기존 ② 기준과 같은 식·상수(풀린 크기 × 2 + PrevHeadroomBytes)를 그대로 쓴다
            var need = RollbackMaterialFinder.RequiredFreeBytes(job.Material with { Kind = SwapMaterialKinds.StagingZip });
            var root = Path.GetPathRoot(app);
            if (root is not null && new DriveInfo(root).AvailableFreeSpace < need)
            {
                if (weDownloaded) TryDelete(target);
                Refuse(job, SwapReasons.DiskLow);
                return;
            }

            // ⑧ 넘기기 직전 — 자동 업데이트 표식 한 번 더
            if (_autoLock.IsAutoUpdateInProgress())
            {
                if (weDownloaded) TryDelete(target);
                Refuse(job, SwapReasons.UpdateInProgress);
                return;
            }

            // ⑨ 교체 일꾼에 넘김 — 같은 번호·같은 예약 주인 · 재료 manual_zip · sha256 소문자(설계 19-3 · 일꾼 p1·p2 가 다시 잰다)
            if (!MoveRenewed(job, ManualUpdateStages.HandingOff)) { if (weDownloaded) TryDelete(target); return; }
            var result = _launcher.Launch(new SwapLaunchInput(
                Mode: SwapModes.Rollback,
                From: job.From,
                To: job.To,
                Material: new SwapMaterial { Kind = SwapMaterialKinds.ManualZip, Path = target, Sha256 = pkg.Sha256.ToLowerInvariant() },
                RequestedBy: job.UserId,
                Entry: SwapEntries.Menu,
                AutoState: null,
                Ticket: job.Ticket,
                Owner: job.Owner));
            if (result.Started)
            {
                job.Move(ManualUpdateStages.HandedOff, _env.UtcNow);
                _logger.LogInformation("[LocalRollback] 받은 이전 판으로 되돌리기를 넘겼습니다 — {From} → {To} · 사용자 {User}",
                    job.From, job.To, job.UserId);
                return;
            }
            if (weDownloaded) TryDelete(target);
            Refuse(job, result.Reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LocalRollback] 이전 판 받기 중 예상 못 한 오류 — 넘기지 않았습니다");
            if (weDownloaded) TryDelete(target);
            Refuse(job, ManualUpdateReasons.DownloadFailed);
        }
        finally
        {
            renewStop.Cancel();
            await renewLoop.ConfigureAwait(false);
            // 넘겼으면 swap.lock 이 이어받고, 멈췄으면 교체가 없다 ⇒ 어느 끝이든 예약을 푼다.
            _launcher.Release(job.Owner);
        }
    }

    /// <summary>
    /// ④ 기존 <see cref="IPackageFetcher.DownloadAsync"/> 로 받되, 받는 동안 파일이 서명된 크기를 넘으면 끊는다(병렬이슈 23 · 상한 = 서명된 크기).
    /// 받는 함수는 그대로 두고 받을 자리의 크기 바뀜 알림으로 잰다 — 알림을 못 걸면 끝난 뒤 길이 대조(⑥)만 남는다(경고 한 줄).
    /// </summary>
    private async Task<string> DownloadCappedAsync(FeedPackage pkg, string staging, string target, CancellationToken ct)
    {
        using var cap = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cap.Token);
        FileSystemWatcher? watcher = null;
        try
        {
            try
            {
                watcher = new FileSystemWatcher(staging, Path.GetFileName(target))
                {
                    NotifyFilter = NotifyFilters.Size | NotifyFilters.LastWrite,
                };
                watcher.Changed += (_, _) => CutIfOversize(target, pkg.SizeBytes, cap);
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or PlatformNotSupportedException)
            {
                _logger.LogWarning(ex, "[LocalRollback] 받는 동안 크기를 재지 못합니다 — 받은 뒤 길이 대조만 합니다");
                watcher?.Dispose();
                watcher = null;
            }

            try
            {
                var path = await _fetcher.DownloadAsync(pkg, staging, linked.Token).ConfigureAwait(false);
                if (cap.IsCancellationRequested) throw new SizeCapExceededException();
                return path;
            }
            catch (OperationCanceledException) when (cap.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new SizeCapExceededException();
            }
        }
        finally
        {
            // 알림을 먼저 끊는다(끊은 뒤 cap 을 건드리지 않게)
            watcher?.Dispose();
        }
    }

    private void CutIfOversize(string target, long signedSize, CancellationTokenSource cap)
    {
        try
        {
            if (new FileInfo(target).Length > signedSize) cap.Cancel();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            _logger.LogWarning(ex, "[LocalRollback] 받는 중 파일 크기를 재지 못했습니다 — 받은 뒤 길이 대조로 갑니다");
        }
    }

    /// <summary>⑥ 길이 == 서명된 크기 그리고 sha256 == 서명된 해시(기존 해시 함수).</summary>
    private async Task<bool> MatchesAsync(string path, FeedPackage pkg, CancellationToken ct)
    {
        var length = new FileInfo(path).Length;
        if (length != pkg.SizeBytes)
        {
            _logger.LogWarning("[LocalRollback] 파일 길이 {Length} ≠ 서명된 크기 {Size} — 쓰지 않습니다", length, pkg.SizeBytes);
            return false;
        }
        return await _fetcher.VerifySha256Async(path, pkg.Sha256, ct).ConfigureAwait(false);
    }

    private static bool IsInside(string path, string stagingRoot)
        => Path.GetFullPath(path).StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase);

    private bool MoveRenewed(Job job, string stage)
    {
        if (RenewReservation(job))
        {
            job.Move(stage, _env.UtcNow);
            return true;
        }
        Refuse(job, SwapReasons.SwapInProgress);
        return false;
    }

    /// <summary>예약 갱신 = 같은 주인으로 다시 쥔다(수동 업데이트와 같은 규칙 · 한 번 잃으면 끝까지 잃은 것).</summary>
    private bool RenewReservation(Job job)
    {
        if (job.ReservationLost) return false;
        if (_launcher.TryReserve(job.Owner)) return true;
        job.MarkReservationLost();
        _logger.LogWarning("[LocalRollback] 이전 판 받기 중 예약을 잃었습니다(다른 업데이트·되돌리기가 쥐었다) — {To}", job.To);
        return false;
    }

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
                _logger.LogDebug("[LocalRollback] 이전 판 받기 예약 갱신을 멈췄습니다 — {To}", job.To);
                return;
            }

            if (stop.IsCancellationRequested) return;
            try
            {
                if (!RenewReservation(job)) return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[LocalRollback] 이전 판 받기 예약 갱신 중 오류 — 잃은 것으로 봅니다 · {To}", job.To);
                job.MarkReservationLost();
                return;
            }
        }
    }

    private void Refuse(Job job, string reason)
    {
        job.Refuse(reason, _env.UtcNow);
        _logger.LogWarning("[LocalRollback] 이전 판 받기를 넘기기 전에 멈췄습니다 — 사유 {Reason} · {From} → {To}", reason, job.From, job.To);
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[LocalRollback] 받은 이전 판 파일을 지우지 못했습니다 — 다음 상태 확인 때 다시 정리합니다: {Path}", path);
        }
    }

    /// <summary>받는 동안 서명된 크기를 넘었다(이 파일 안에서만 쓴다).</summary>
    private sealed class SizeCapExceededException : Exception
    {
    }

    private sealed class Job
    {
        private readonly object _lock = new();
        private string _stage;
        private string? _reason;
        private DateTime _at;
        private bool _reservationLost;

        public Job(RollbackMaterial material, string from, string userId, string ticket, string owner, string firstStage, DateTime atUtc)
        {
            Material = material;
            From = from;
            UserId = userId;
            Ticket = ticket;
            Owner = owner;
            _stage = firstStage;
            _at = atUtc;
        }

        public RollbackMaterial Material { get; }
        public string To => Material.Version;
        public string From { get; }
        public string UserId { get; }
        public string Ticket { get; }
        public string Owner { get; }

        public bool ReservationLost
        {
            get { lock (_lock) return _reservationLost; }
        }

        public void MarkReservationLost()
        {
            lock (_lock) _reservationLost = true;
        }

        public void Move(string stage, DateTime atUtc)
        {
            lock (_lock)
            {
                _stage = stage;
                _at = atUtc;
            }
        }

        public void Refuse(string reason, DateTime atUtc)
        {
            lock (_lock)
            {
                _stage = ManualUpdateStages.Refused;
                _reason = reason;
                _at = atUtc;
            }
        }

        public LocalRollbackFetchStatus Snapshot()
        {
            lock (_lock) return new LocalRollbackFetchStatus(_stage, _reason, To, _at);
        }
    }
}
