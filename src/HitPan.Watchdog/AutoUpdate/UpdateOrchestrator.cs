namespace HitPan.Watchdog.AutoUpdate;

// 사장님 결재 2026-06-09 (Plan cicd-velvety-reef Day 11~12)
//
// 채널별 적용 정책 (결재 4 — 메이저 업데이트 A안):
//   Emergency: 5분 안내 → 즉시 다운로드 + 적용
//   Normal:    매일 새벽 3시 자동 다운로드 + 적용 (서비스 재기동)
//   Major:     ERP 화면에 동의 요청 → 동의 후 영업시간 외 예약
//              동의 무응답 시 90일 옛 버전 유지 → 30일 추가 알림 → CS 직접 연락
//
// 헌법 정합:
//   #25 — 쉽게·정확하게·안전하게
//   #28·#30 — 고객 손 0번
//   #34 — 베타부터 정식 완성도
public sealed class UpdateOrchestrator
{
    private readonly IUpdateClient _client;
    private readonly ILogger<UpdateOrchestrator> _logger;
    private readonly WatchdogBackupRunner _backup;
    private readonly string _stagingDir;

    // 봉합 (2026-06-29, 작1 고리3): 백업 실행기를 주입받는다(없으면 컴파일 깨지지 않게 신규 인자 추가만 — 헌법 #1).
    public UpdateOrchestrator(IUpdateClient client, ILogger<UpdateOrchestrator> logger, WatchdogBackupRunner backup)
    {
        _client = client;
        _logger = logger;
        _backup = backup;
        _stagingDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HitPan", "Updates", "staging");
    }

    public async Task<UpdateDecision> EvaluateAsync(string currentVersion, CancellationToken ct)
    {
        var manifest = await _client.GetLatestManifestAsync(currentVersion, ct);
        if (manifest is null)
            return new UpdateDecision(UpdateAction.None, null, null);

        return manifest.Channel switch
        {
            UpdateChannel.Emergency => new UpdateDecision(UpdateAction.AnnounceThenApply, manifest, TimeSpan.FromMinutes(5)),
            UpdateChannel.Normal => new UpdateDecision(UpdateAction.ApplyAtNight, manifest, null),
            UpdateChannel.Major => new UpdateDecision(UpdateAction.RequireConsent, manifest, null),
            _ => new UpdateDecision(UpdateAction.None, null, null)
        };
    }

    public async Task<bool> DownloadAndVerifyAsync(UpdateManifest manifest, CancellationToken ct)
    {
        try
        {
            var path = await _client.DownloadAsync(manifest, _stagingDir, ct);
            var ok = await _client.VerifySha256Async(path, manifest.Sha256, ct);
            if (!ok)
            {
                File.Delete(path);
                _logger.LogError("[Update] 검증 실패 — 다운로드 파일 폐기 ({V})", manifest.Version);
                return false;
            }
            _logger.LogInformation("[Update] 다운로드+검증 완료: {V}", manifest.Version);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Update] 다운로드 실패: {V}", manifest.Version);
            return false;
        }
    }

    /// <summary>
    /// 작1 고리3 — 안전 적용 흐름. 다운로드+검증 성공 후 "반드시 백업 먼저 → 백업 실패 시 물리적 차단 →
    /// 백업 성공(파일 존재·크기>0) 검증 후에만 적용 단계 진입" 을 강제한다(헌법 #20 데이터 무결성).
    ///
    /// 반환 = 적용 단계까지 안전하게 도달했는지(true)/차단됐는지(false).
    /// ⚠️ 고리4(Velopack 실제 EXE 교체·재시작 + DB 마이그·실패 롤백)는 본 작업 범위 밖이다.
    ///    백업 성공 직후 실제 ApplyUpdatesAndRestart 는 호출하지 않는다(VelopackUpdaterStub 그대로).
    /// </summary>
    public async Task<bool> ApplyUpdateAsync(UpdateManifest manifest, CancellationToken ct)
    {
        // 1) 다운로드 + sha256 검증 (실패 시 적용 진입 금지)
        var verified = await DownloadAndVerifyAsync(manifest, ct);
        if (!verified)
        {
            _logger.LogError("[Update] 다운로드/검증 실패 — 적용 차단 ({V})", manifest.Version);
            return false;
        }

        // 2) ★ 적용 전 반드시 백업 먼저 (헌법 #20 데이터 무결성)
        var (backupOk, backupFile) = await _backup.RunPreUpdateBackupAsync(ct);

        // 3) ★ 백업 실패 = 업데이트 전 과정 물리적 차단
        if (!backupOk || string.IsNullOrWhiteSpace(backupFile))
        {
            // TODO(메타 통지 자리): 백업 실패로 업데이트를 차단했음을 본사 메타에 통지(헌법 #30 본사는 통지만 수신).
            //   Worker 가 MetaPingClient.NotifyEmergencyAsync("update_backup_failed", ...) 로 올린다(고리3 후속 배선).
            _logger.LogError("[Update] 🛑 적용 전 백업 실패 — 업데이트 전 과정 물리적 차단({V}). 데이터 무결성 우선.", manifest.Version);
            return false;
        }

        _logger.LogInformation("[Update] 백업 성공 확인({Backup}) — 적용 단계 진입 가능: {V}", backupFile, manifest.Version);

        // 4) ===== 고리4 자리표시 (본 작업 범위 밖) =====
        //   여기서 Velopack 실제 적용(원자적 EXE 교체 → 재시작) + DB ALTER 마이그 실행.
        //   마이그 실패 시 위 backupFile 로 DB+EXE 자동 복원(롤백) → 구버전 무손상 복구.
        //   현재는 VelopackUpdaterStub(no-op) 유지 — 실제 ApplyUpdatesAndRestart 호출 안 함.
        //   고리4 설계 완료 후 이 자리에 실 적용 코드를 추가한다(헌법 #1 추가만, #34 정식 완성도).
        _logger.LogWarning("[Update] 고리4(Velopack 실 적용·마이그 롤백) 미구현 — 백업까지만 수행하고 적용 보류({V})", manifest.Version);

        return true;
    }

    public bool IsNightWindow(DateTime now)
        => now.Hour >= 3 && now.Hour < 4;

    public bool IsBusinessHour(DateTime now)
        => now.Hour >= 9 && now.Hour < 18 && now.DayOfWeek != DayOfWeek.Saturday && now.DayOfWeek != DayOfWeek.Sunday;
}

public enum UpdateAction
{
    None,
    AnnounceThenApply,   // Emergency
    ApplyAtNight,        // Normal
    RequireConsent       // Major
}

public sealed record UpdateDecision(UpdateAction Action, UpdateManifest? Manifest, TimeSpan? AnnounceDelay);
