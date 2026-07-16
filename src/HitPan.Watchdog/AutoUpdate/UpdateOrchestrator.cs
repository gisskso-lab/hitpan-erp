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
    private readonly UpdateProcessGate _gate;
    private readonly UpdateLockFile _lock;
    private readonly string _stagingDir;

    // 봉합 (2026-06-29, 작1 고리3): 백업 실행기를 주입받는다(없으면 컴파일 깨지지 않게 신규 인자 추가만 — 헌법 #1).
    // 봉합 (2026-07-16, 작1 W4-1): 교체 구간 정지·복원 게이트와 진행 표식을 주입받는다(추가만).
    public UpdateOrchestrator(
        IUpdateClient client,
        ILogger<UpdateOrchestrator> logger,
        WatchdogBackupRunner backup,
        UpdateProcessGate gate,
        UpdateLockFile lockFile)
    {
        _client = client;
        _logger = logger;
        _backup = backup;
        _gate = gate;
        _lock = lockFile;
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

        // 4) ===== W4-1 (2026-07-16, 사장님 결재): 교체 구간 정지·복원 =====
        //   여기부터 ERP 를 멈추고 파일을 바꾼다. 이 구간의 최대 위험은 "keepalive 가 꺼진 채 남아
        //   ERP 가 영영 안 뜨는 것"이라, 어떤 경로로 끝나든 반드시 복원되게 감싼다(① 보장).
        var slot = _gate.ResolveSlot();
        if (slot is null)
        {
            _logger.LogError("[Update] 🛑 슬롯을 판정하지 못해 적용을 중단합니다 — 구버전 그대로 유지({V})", manifest.Version);
            return false;
        }

        _lock.Acquire(manifest.Version);
        try
        {
            if (!await _gate.StopForSwapAsync(slot.Value, ct))
            {
                // 사유는 StopForSwapAsync 가 이미 기록했다. 아직 아무것도 안 바꿨으므로 구버전 무손상.
                _logger.LogError("[Update] 🛑 교체 준비 실패 — 적용을 중단합니다. 구버전 그대로 유지({V})", manifest.Version);
                return false;
            }

            // ===== W4-2~W4-5 자리 (다음 작업 범위) =====
            //   여기서 파일 교체(web→api 순) → 재기동 → 헬스폴링 2중 판정 → 실패 시 롤백.
            //   지금은 정지·복원 골격만 세운 상태다. 교체 코드가 붙기 전까지는 아무것도 바꾸지 않고
            //   그대로 되돌린다 — 반쯤 적용된 상태를 만들지 않는다(헌법 #20).
            _logger.LogWarning("[Update] W4-2(파일 교체)~W4-5(롤백) 미구현 — 정지까지만 수행하고 원상 복구합니다({V})", manifest.Version);

            return true;
        }
        finally
        {
            // ① 무조건 복원 — 정상·예외·중단 어느 경로든 ERP 는 살아나야 한다.
            //   ② 부팅 안전망과 ③ 자가 점검이 이 뒤를 받치지만, 정상 경로는 여기서 끝내는 게 맞다.
            //
            //   봉합 (2026-07-16, 검증팀 R-3 적발): 종전엔 복원 성공 여부와 무관하게 안전망을 지웠다.
            //     복원이 실패했는데 부팅 복원망까지 지우면 ①·② 가 동시에 사라져 3중이 1중이 된다.
            //     복원에 성공했을 때만 치운다 — 실패 시엔 남겨야 재부팅 1회로 ERP 가 살아난다
            //     (남아도 /ENABLE 은 멱등이라 무해하다).
            var restored = _gate.RestoreKeepalive(slot.Value);
            if (restored) _gate.RemoveRestoreSafetyNet();
            _lock.Release();

            // 교체 후 ERP 를 다시 띄우는 건 W4-4 다. 그때까지는 keepalive 가 1분 내에 되살린다
            //   (교체를 안 했으므로 구버전이 그대로 뜬다).
        }
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
