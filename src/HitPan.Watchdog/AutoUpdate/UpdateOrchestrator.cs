using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using HitPan.Watchdog.Stages;
using HitPan.Watchdog.Telemetry;
using Microsoft.Extensions.Options;

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
    private readonly WS28I_FourProcess _fourProcess;
    private readonly IHttpClientFactory _httpFactory;
    private readonly WatchdogOptions _options;
    private readonly WatchdogStatusWriter _statusWriter;
    private readonly MetaPingClient _meta;
    private readonly string _stagingDir;

    // W4-6: 이번 적용이 "마이그 교차검증 게이트"에 걸려 중단됐는지 표식. TrySwapFilesAsync 실패를
    //   apply_status 에 blocked(게이트 차단) vs rolled_back(일반 스왑 실패)로 구분해 기록하기 위한 것.
    //   ApplyUpdateAsync 1회 실행 안에서만 의미 있다(진입 시 false 로 리셋).
    private bool _lastSwapBlockedByMigrationGate;

    // 봉합 (2026-06-29, 작1 고리3): 백업 실행기를 주입받는다(없으면 컴파일 깨지지 않게 신규 인자 추가만 — 헌법 #1).
    // 봉합 (2026-07-16, 작1 W4-1): 교체 구간 정지·복원 게이트와 진행 표식을 주입받는다(추가만).
    // 봉합 (2026-07-16, 작1 W4-4): 재시작기(WS28I_FourProcess)를 주입받는다 — schtasks /Run 재기동을
    //   재사용한다(신규 구현 0). 기존 인자 뒤에 추가만 하며, 이미 DI 싱글턴으로 등록돼 있어 순환참조 없다.
    // 봉합 (2026-07-16, 작1 W4-5): 검증 게이트가 로컬 API /health 를 폴링하려고 HttpClientFactory·옵션을
    //   주입받는다(추가만). 둘 다 Program.cs 에 이미 등록(AddHttpClient·AddOptions<WatchdogOptions>)됐고
    //   WS28I 가 같은 조합을 쓰고 있어 순환참조·미등록 위험이 없다.
    // 봉합 (2026-07-16, 작1 W4-6): 적용 결과를 local_update_apply_status 에 기록(WatchdogStatusWriter)하고,
    //   롤백까지 실패한 종점은 본사에 긴급 통지(MetaPingClient — 본사는 통지만, 헌법 #30)한다. 둘 다 이미
    //   DI 싱글턴이고 UpdateOrchestrator 에 의존하지 않아 순환참조가 없다(추가만 — 헌법 #1).
    public UpdateOrchestrator(
        IUpdateClient client,
        ILogger<UpdateOrchestrator> logger,
        WatchdogBackupRunner backup,
        UpdateProcessGate gate,
        UpdateLockFile lockFile,
        WS28I_FourProcess fourProcess,
        IHttpClientFactory httpFactory,
        IOptions<WatchdogOptions> options,
        WatchdogStatusWriter statusWriter,
        MetaPingClient meta)
    {
        _client = client;
        _logger = logger;
        _backup = backup;
        _gate = gate;
        _lock = lockFile;
        _fourProcess = fourProcess;
        _httpFactory = httpFactory;
        _options = options.Value;
        _statusWriter = statusWriter;
        _meta = meta;
        _stagingDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HitPan", "Updates", "staging");
    }

    /// <summary>
    /// W4-6 정지공격 감지용 — 직전 EvaluateAsync 의 manifest '조회'가 feed 예외로 실패했는지 노출한다.
    ///   None 반환만으로는 '조회 실패'와 '새 버전 없음'을 구분 못 하므로, Worker 가 이 값으로 조회 실패만 센다.
    /// </summary>
    public bool LastFetchFailed => _client.LastFetchFailed;

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
    /// 작1 고리3+고리4 — 자동 업데이트 안전 적용 흐름 전체.
    /// 다운로드+검증 → "반드시 백업 먼저 → 백업 실패 시 물리적 차단"(고리3, 헌법 #20) →
    /// W4-1 정지 → W4-2 파일 교체(web→api 스왑) → W4-4 재시작(api→web) →
    /// W4-5 검증(‑/health 200+버전 AND 교체 EXE FileVersion 2중 판정) → 실패 시 롤백(.old 복원) →
    /// W4-6 상태 기록(local_update_apply_status)·롤백 실패 시 본사 메타핑 통지.
    ///
    /// 반환 = 신버전이 실제로 200+정확한 버전으로 살아났는지(true)/차단·롤백됐는지(false).
    ///   ※ 종전 이 자리에는 "고리4는 범위 밖, 백업까지만 수행한다"는 주석이 있었으나(20260720작1 W4-2~6로 구현됨),
    ///     실제 교체·재시작·검증·롤백을 수행하도록 커밋 a2c249f 에서 완결됐다 — 낡은 주석 정정(CTO B-1).
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
            _logger.LogError("[Update] 🛑 적용 전 백업 실패 — 업데이트 전 과정 물리적 차단({V}). 데이터 무결성 우선.", manifest.Version);
            // W4-6: 아무것도 안 바꿨고 구버전 그대로다(blocked). 본사에도 통지(헌법 #30 — 본사는 통지만).
            await RecordApplyStatusAsync(manifest, "blocked", "적용 전 백업 실패 — 업데이트 차단(데이터 무결성 우선)", ct);
            await _meta.NotifyEmergencyAsync("update_backup_failed", $"pre-apply:{manifest.Version}", ct);
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
        _lastSwapBlockedByMigrationGate = false;   // W4-6: 이번 적용 시작 시 게이트 표식 초기화.
        try
        {
            if (!await _gate.StopForSwapAsync(slot.Value, ct))
            {
                // 사유는 StopForSwapAsync 가 이미 기록했다. 아직 아무것도 안 바꿨으므로 구버전 무손상.
                _logger.LogError("[Update] 🛑 교체 준비 실패 — 적용을 중단합니다. 구버전 그대로 유지({V})", manifest.Version);
                // 아무것도 안 바꿨고 구버전 그대로라 rolled_back 으로 기록(신버전 미적용, 구버전 유지).
                await RecordApplyStatusAsync(manifest, "rolled_back", "교체 준비(정지) 실패 — 구버전 유지", ct);
                return false;
            }

            // ===== W4-2 (2026-07-16, 사장님 결재): 파일 교체 (best-effort 스왑) =====
            //   여기부터 실제 파일을 바꾼다. 스왑 자체가 실패하면 TrySwapFilesAsync 안에서
            //   부분 성공까지 전부 역복원하고 false 로 나온다(반쯤 적용 금지 — 헌법 #20).
            if (!await TrySwapFilesAsync(manifest, ct))
            {
                // 스왑 실패는 TrySwapFilesAsync 가 이미 역복원까지 마쳤다(구버전 무손상). 사유도 기록됐다.
                //   W4-6: 마이그 교차검증 게이트에 걸린 것이면 'blocked', 그 외 스왑 실패면 'rolled_back'.
                if (_lastSwapBlockedByMigrationGate)
                {
                    _logger.LogError("[Update] 🛑 마이그 교차검증 게이트 차단 — 적용을 중단합니다(구버전 유지)({V})", manifest.Version);
                    await RecordApplyStatusAsync(manifest, "blocked", "마이그 교차검증 게이트 차단(CS 확인 필요)", ct);
                }
                else
                {
                    _logger.LogError("[Update] 🛑 파일 교체 실패 — 구버전으로 되돌렸습니다({V})", manifest.Version);
                    await RecordApplyStatusAsync(manifest, "rolled_back", "파일 교체 실패 — 구버전 유지", ct);
                }
                return false;
            }

            // 여기부터는 신버전 파일이 이미 {app}\api·web 에 들어가 있다(.old 는 롤백 자산으로 남아 있다).
            //   따라서 이 아래에서 실패하면 "그냥 return false" 로 끝내면 안 된다 — 신버전이 뜬 채(혹은 안 뜬 채)
            //   방치된다. 재시작·검증 어느 쪽이 실패하든 반드시 RollbackToOldAsync 로 구버전을 복원한다(헌법 #20).

            // ===== W4-4 (2026-07-16, 사장님 결재): 재시작 =====
            //   스왑된 신버전을 띄운다. 기동 순서는 api → web(스왑 순서 web→api 와 의도된 비대칭 —
            //   "API 가 서야 ERP 가 산다"). 종료코드 성공까지만 보고, 실제 "신버전인가"는 아래 검증 게이트가 판정한다.
            if (!RestartErp(slot.Value, manifest))
            {
                _logger.LogError("[Update] 🛑 재시작 실패 — 신버전 파일은 교체됐으나 ERP 기동에 실패했습니다. 롤백합니다({V}).", manifest.Version);
                await FinishWithRollbackAsync(slot.Value, manifest, "재시작 실패", ct);
                return false;
            }

            // ===== W4-5 (2026-07-16, 사장님 결재): 정밀 검증 게이트 =====
            //   재시작 종료코드만으로는 "신버전이 정말 살아서 도는가"를 알 수 없다(구버전도 종료코드 0 으로 뜬다).
            //   ① API /health 2초 간격 × 최대 60초 폴링: HTTP 200 AND 본문 checks.version == 신버전.
            //      200 만으론 불통과(구버전도 200) — 반드시 버전까지 같아야 한다.
            //   ② 교차 판정: 교체된 {app}\api\HitPan.API.dll FileVersion == 신버전.
            //      /health 는 미들웨어 설정 하나에 묶인 단일 실패점이라, 실제 파일 버전으로 교차한다.
            //   둘 다 통과해야만 성공. 하나라도 어긋나면 = 거짓성공(신버전 죽었는데 true) 위험 → 롤백.
            if (!await VerifyNewVersionAsync(slot.Value, manifest, ct))
            {
                _logger.LogError("[Update] 🛑 신버전 검증 실패 — 구버전으로 롤백합니다({V}).", manifest.Version);
                await FinishWithRollbackAsync(slot.Value, manifest, "신버전 검증 실패", ct);
                return false;
            }

            _logger.LogInformation("[Update] ✅ 업데이트 적용·검증 성공(api→web, /health+FileVersion 2중 통과)({V})", manifest.Version);
            // W4-6: 성공 기록 → 성공 정리(staging·.old·watchdog.new·안전망·TTL). .old 정리는 성공 확정 후에만.
            await RecordApplyStatusAsync(manifest, "success", null, ct);
            CleanupAfterSuccess(slot.Value);
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

    /// <summary>
    /// W4-2 — staging 의 zip 을 풀어 web→api 순으로 교체한다. "원자적"이 아니라 best-effort 스왑이다
    /// (Windows 폴더 리네임은 핸들 하나만 열려 있어도 실패한다 — 정직한 이름).
    ///
    /// 흐름:
    ///   0) staging\hitpan-{V}.zip → staging\extract\{api,web,watchdog} 해제
    ///   1) B안 교차검증 게이트 — Migrations/SQL 에 .sql 이 있는데 manifest.RequiresMigration==false 면 강제 중단.
    ///      추가로 그 .sql 내용에 'local_update_' 가 있으면(구버전 워치독을 깨뜨리는 스키마) 중단.
    ///   2) 스왑: web 먼저 → api 나중. 어느 Move 든 실패하면 이미 옮긴 것을 역순 복원하고 false.
    ///   3) watchdog 은 스왑하지 않는다(W4-3 제외) — extract\watchdog 을 {app}\watchdog.new 에 두기만 한다.
    ///
    /// ★ 스왑 순서를 web→api 로 둔 근거(작지서 CTO 처방 5): web 실패 시 api 는 아직 기동 전이라
    ///   "신버전 api 가 처리한 거래를 구버전 api 가 이어받는 창"이 아예 생기지 않는다(노출창 0).
    /// ★ 이 구간 전체가 W4-1 정지 상태 안이다 — api·web 어느 것도 실행 중이 아니다.
    /// ★ 게이트 위치는 작지서상 "다운로드·백업 후 / W4-1 전"이나, 본 구현은 정지 성공 직후에 둔다.
    ///   게이트가 걸려 false 로 나가도 호출부 finally 가 keepalive 를 복원해 구버전 ERP 를 되살리므로
    ///   반쯤 적용된 상태는 만들어지지 않는다(헌법 #20).
    /// ★ 성공해도 .old 는 아직 지우지 않는다 — W4-5 롤백이 그대로 쓴다(다음 단계에서 정리).
    /// </summary>
    /// <summary>
    /// {app} = 워치독 EXE 상위 폴더. 워치독은 {app}\watchdog\ 에 설치되므로 BaseDirectory 의 상위가 {app} 다
    ///   (DbConfReader.ResolveDbConfPath 와 동일한 '..' 규칙 — 설치 구조 단일 출처). W4-2 스왑과 W4-5
    ///   FileVersion 검증·롤백이 같은 값을 써야 하므로 한 곳으로 모은다(경로가 어긋나면 롤백이 딴 폴더를 건드린다).
    /// </summary>
    private static string AppRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));

    private async Task<bool> TrySwapFilesAsync(UpdateManifest manifest, CancellationToken ct)
    {
        var appRoot = AppRoot();
        var zipPath = Path.Combine(_stagingDir, $"hitpan-{manifest.Version}.zip");
        var extractDir = Path.Combine(_stagingDir, "extract");

        // ── 0) 해제 ── 이전 시도의 잔재가 있으면 지우고 새로 푼다(부분 잔재로 오염되지 않게).
        try
        {
            if (!File.Exists(zipPath))
            {
                _logger.LogError("[Update] 🛑 교체용 zip 을 찾지 못했습니다: {Zip}", zipPath);
                return false;
            }
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);
            ZipFile.ExtractToDirectory(zipPath, extractDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Update] 🛑 zip 해제 실패 — 교체를 중단합니다({Zip})", zipPath);
            return false;
        }

        var extractApi = Path.Combine(extractDir, "api");
        var extractWeb = Path.Combine(extractDir, "web");
        var extractWatchdog = Path.Combine(extractDir, "watchdog");

        // zip 최상위에 api/web 이 있어야 스왑이 성립한다(build-manifest.ps1 이 이 구조로 압축).
        if (!Directory.Exists(extractApi) || !Directory.Exists(extractWeb))
        {
            _logger.LogError("[Update] 🛑 해제 결과에 api/web 폴더가 없습니다(api={A}, web={W}) — 교체를 중단합니다.",
                Directory.Exists(extractApi), Directory.Exists(extractWeb));
            return false;
        }

        // ── 1) B안 교차검증 게이트 (스왑 前) ── 사람 오타를 코드가 잡는다.
        if (!PassesMigrationCrossCheck(extractApi, manifest))
            return false;   // 사유는 PassesMigrationCrossCheck 가 이미 기록했다.

        // ── 2) best-effort 스왑 (web 먼저 → api 나중) ──
        var appWeb = Path.Combine(appRoot, "web");
        var appWebOld = Path.Combine(appRoot, "web.old");
        var appApi = Path.Combine(appRoot, "api");
        var appApiOld = Path.Combine(appRoot, "api.old");

        // 이전 실패로 남은 .old 가 있으면 리네임이 막힌다 — 스왑 시작 전 치운다(멱등).
        TryDeleteDir(appWebOld);
        TryDeleteDir(appApiOld);

        // 어디까지 옮겼는지 추적해 실패 시 역복원한다(부분 성공도 전부 되돌림 — 헌법 #20).
        var webRenamedOut = false;   // web → web.old 완료?
        var webReplaced = false;     // extract\web → web 완료?
        try
        {
            // web: 기존을 web.old 로 밀어내고 신버전을 web 자리에 넣는다.
            Directory.Move(appWeb, appWebOld);
            webRenamedOut = true;
            Directory.Move(extractWeb, appWeb);
            webReplaced = true;

            // api: 동일. 여기서 실패하면 아래 catch 가 web 까지 통째로 되돌린다.
            Directory.Move(appApi, appApiOld);
            Directory.Move(extractApi, appApi);

            // ── 3) watchdog 은 스왑하지 않는다(W4-3 제외). 신버전 워치독을 곁에 두기만 한다. ──
            //   자기 자신(실행 중 EXE)을 교체하는 것은 고리5 별건이라 여기서 하지 않는다.
            if (Directory.Exists(extractWatchdog))
            {
                var appWatchdogNew = Path.Combine(appRoot, "watchdog.new");
                TryDeleteDir(appWatchdogNew);
                try
                {
                    Directory.Move(extractWatchdog, appWatchdogNew);
                }
                catch (Exception ex)
                {
                    // watchdog.new 배치 실패는 스왑 성패를 좌우하지 않는다(고리5에서 쓸 예비물일 뿐).
                    //   침묵은 금지(헌법 #15) — 경고만 남기고 진행한다.
                    _logger.LogWarning(ex, "[Update] watchdog.new 배치 실패 — api/web 교체는 유효합니다(고리5 예비물만 누락).");
                }
            }

            await Task.CompletedTask;   // 현재 스왑 자체는 동기지만, 다음 단계 재시작이 async 로 이어붙는다.
            _logger.LogInformation("[Update] 파일 교체 완료(web→api) — .old 는 롤백용으로 남겨둡니다({V})", manifest.Version);
            return true;
        }
        catch (Exception ex)
        {
            // ── 열린 핸들 진단 (헌법 #15 침묵 금지) ── HRESULT + 잔존 프로세스 + keepalive 상태를 함께.
            LogSwapFailureDiagnostics(ex);

            // ── 즉시 역복원 (부분 성공도 전부 되돌림) ── 옮긴 역순으로 되돌린다.
            //   web 을 원위치로: (신버전이 들어갔으면 빼고) web.old 를 web 으로.
            try
            {
                if (webReplaced && Directory.Exists(appWeb))
                {
                    // web 자리에 든 신버전을 extract 쪽으로 도로 밀어낸다(다음 시도/정리를 위해).
                    if (Directory.Exists(extractWeb)) TryDeleteDir(extractWeb);
                    Directory.Move(appWeb, extractWeb);
                }
                if (webRenamedOut && Directory.Exists(appWebOld))
                    Directory.Move(appWebOld, appWeb);
            }
            catch (Exception restoreEx)
            {
                _logger.LogError(restoreEx, "[Update] ⚠️ web 역복원 실패 — 부팅 복원 안전망(②)과 자가 점검(③)이 뒤를 받칩니다.");
            }
            // api 는 web.old→web 밀어내기 전에 실패했을 수 있다. api.old 가 있으면 되돌린다.
            try
            {
                if (Directory.Exists(appApiOld))
                {
                    if (Directory.Exists(appApi)) TryDeleteDir(appApi);
                    Directory.Move(appApiOld, appApi);
                }
            }
            catch (Exception restoreEx)
            {
                _logger.LogError(restoreEx, "[Update] ⚠️ api 역복원 실패 — 부팅 복원 안전망(②)과 자가 점검(③)이 뒤를 받칩니다.");
            }

            _logger.LogError("[Update] 🛑 파일 교체 실패 — 구버전으로 역복원했습니다({V})", manifest.Version);
            return false;
        }
    }

    /// <summary>워치독이 직접 SQL 로 읽고 쓰는 상태 테이블 — 이 스키마 변경은 구버전 워치독을 깬다(B-2 게이트 대상).
    /// apply_status 는 워치독 상태기록기(WatchdogStatusWriter)가 직접 CREATE·INSERT 한다(W4-6) — F-1 반영으로 포함.</summary>
    private static readonly string[] GuardedUpdateTables =
        { "local_update_status", "local_update_consents", "local_update_apply_status" };

    /// <summary>
    /// SQL 텍스트에 '<verb> TABLE [IF ...] <table>' 문이 있는지(대소문자·백틱·연속공백 무관). 정밀 파서가 아니라
    /// ALTER/DROP 대상 테이블만 잡는 실용 판정이다. verb=ALTER|DROP. table 은 리터럴로만 쓴다(정규식 이스케이프 불요).
    /// </summary>
    private static bool MatchesTableStatement(string sql, string verb, string table)
    {
        // 예: ALTER  TABLE `local_update_status`  /  DROP TABLE IF EXISTS local_update_consents
        var pattern = $@"\b{verb}\s+TABLE\s+(?:IF\s+(?:NOT\s+)?EXISTS\s+)?`?{table}`?\b";
        return System.Text.RegularExpressions.Regex.IsMatch(
            sql, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// B안 교차검증 — 해제된 api 폴더의 Migrations\SQL 에 .sql 이 있는데 manifest.RequiresMigration==false 면
    /// 사람 오타로 보고 강제 중단한다(①, 유지). 추가로 그 .sql 이 local_update_status/consents 의 스키마를
    /// '변경'하면(구버전 워치독의 상태 테이블을 깸 → 업데이트 주체가 죽음) 역시 중단한다(②, 호환성 게이트).
    ///
    /// ── 두 게이트 통일 판정 정의 (빌드타임=build-manifest.ps1 §3 / 런타임=여기 ②) ──
    ///   막을 것은 'local_update_' 문자열의 **존재**가 아니라 워치독 상태 테이블의 스키마 **변경**이다.
    ///   (존재만으로 막으면 그 테이블을 처음 만든 DB-82/83 이 모든 릴리스에 항상 실려 전 릴리스 불통과 = 오탐.)
    ///   워치독은 clean DDL 대조가 무거우므로(CTO 지침) "이번 zip 이 실어온 local_update_ SQL 이 신규/변경인가"
    ///   기준으로 좁힌다: 가드 3테이블(status·consents=SELECT, apply_status=CREATE·INSERT/W4-6)에 대한
    ///   ALTER/DROP 이 있으면 변경 = 중단(apply_status 포함은 F-1 반영).
    ///   CREATE TABLE IF NOT EXISTS(이미 있는 테이블 재현 = no-op)는 변경이 아니므로 통과 — 이게 DB-82/83 이다.
    ///   clean DDL 과 다른 CREATE 까지 잡는 정밀 대조는 빌드타임 게이트가 담당한다(2중 방어의 역할 분담).
    /// </summary>
    /// <returns>통과하면 true. 하나라도 걸리면 false(사유 기록 후).</returns>
    private bool PassesMigrationCrossCheck(string extractApiDir, UpdateManifest manifest)
    {
        var sqlDir = Path.Combine(extractApiDir, "Migrations", "SQL");
        if (!Directory.Exists(sqlDir))
            return true;   // 마이그 폴더 자체가 없으면 검사할 대상이 없다 = 통과.

        string[] sqlFiles;
        try
        {
            sqlFiles = Directory.GetFiles(sqlDir, "*.sql", SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            // 목록조차 못 읽으면 안전측(중단)으로 기운다 — 검증 없이 교체하지 않는다(헌법 #20).
            _logger.LogError(ex, "[Update] 🛑 Migrations\\SQL 목록 조회 실패 — 교차검증 불가로 교체를 중단합니다.");
            return false;
        }

        // ① manifest.RequiresMigration==false 인데 .sql 이 존재 → 손입력 오타. 강제 중단.
        if (sqlFiles.Length > 0 && !manifest.RequiresMigration)
        {
            _logger.LogError("[Update] 🛑 교차검증 실패 — Migrations\\SQL 에 .sql {N}개가 있는데 " +
                             "manifest.RequiresMigration=false 입니다(릴리스 손입력 오타 의심). " +
                             "구 스키마 위에서 신버전이 500 나면 되돌려도 안 살아납니다 — 교체를 중단합니다({V}).",
                             sqlFiles.Length, manifest.Version);
            _lastSwapBlockedByMigrationGate = true;   // W4-6: apply_status 에 'blocked' 로 기록되게 표식.
            return false;
        }

        // ② local_update_status/consents 의 스키마를 '변경'(ALTER/DROP)하는 릴리스면 구버전 워치독이 깨진다.
        //    구버전 워치독(자기교체 W4-3 제외로 정상 상태)이 깨지면 그 PC 는 영구 고립 = 재설치 외 복구 불가.
        //    build 쪽에 정밀 게이트가 있고(clean DDL 대조, 작지서 B-1), 워치독은 ALTER/DROP 만 교차한다(2중).
        //    존재만으로 막지 않는다 — CREATE TABLE IF NOT EXISTS(DB-82/83 재현=no-op)는 통과다(오탐 봉합, B-2).
        foreach (var file in sqlFiles)
        {
            string content;
            try
            {
                content = File.ReadAllText(file);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Update] 🛑 마이그 파일 읽기 실패({File}) — 교차검증 불가로 교체를 중단합니다.", file);
                return false;
            }

            if (!content.Contains("local_update_", StringComparison.OrdinalIgnoreCase))
                continue;   // 이 테이블을 언급조차 안 하면 검사 대상 아님.

            foreach (var table in GuardedUpdateTables)
            {
                // ALTER TABLE <t> 또는 DROP TABLE <t> = 스키마 변경. (CREATE IF NOT EXISTS 재현은 변경 아님 → 통과.)
                if (MatchesTableStatement(content, "ALTER", table) ||
                    MatchesTableStatement(content, "DROP", table))
                {
                    _logger.LogError("[Update] 🛑 교차검증 실패 — 마이그 '{File}' 이 '{Table}' 스키마를 변경(ALTER/DROP)합니다. " +
                                     "구버전 워치독이 깨져 업데이트 주체가 죽습니다(영구 고립 위험) — 교체를 중단합니다({V}).",
                                     Path.GetFileName(file), table, manifest.Version);
                    _lastSwapBlockedByMigrationGate = true;   // W4-6: apply_status 에 'blocked' 로 기록되게 표식.
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 스왑 실패 진단(헌법 #15) — IOException HRESULT(32=공유위반 / 5=액세스거부) 구분 +
    /// tasklist 로 HitPan.API.exe·powershell.exe 잔존 확인 + keepalive 작업 상태를 함께 남긴다.
    /// 원인 대부분(keepalive 부활로 파일이 잠김)이 이 세 줄로 특정된다. 이 메서드는 예외를 던지지 않는다.
    /// </summary>
    private void LogSwapFailureDiagnostics(Exception ex)
    {
        // HRESULT 하위 16비트가 Win32 에러코드다(32=ERROR_SHARING_VIOLATION, 5=ERROR_ACCESS_DENIED).
        var win32 = ex.HResult & 0xFFFF;
        var meaning = win32 switch
        {
            32 => "공유 위반(파일을 다른 프로세스가 잡고 있음 — keepalive 부활 의심)",
            5 => "액세스 거부(권한 부족 또는 폴더가 사용 중)",
            _ => "기타"
        };
        _logger.LogError(ex, "[Update] 파일 교체 예외 진단 — Win32={Code}({Meaning})", win32, meaning);

        // 잔존 프로세스 확인 — End/taskkill 후에도 살아 있으면 keepalive 가 되살린 것이다.
        _logger.LogError("[Update] 잔존 프로세스: HitPan.API.exe={Api}, powershell.exe={Ps}",
            IsProcessRunning("HitPan.API"), IsProcessRunning("powershell"));
    }

    /// <summary>프로세스 이름(확장자 제외)이 실행 중인지. 조회 실패 시 판정 불가로 false 대신 문자열로 남긴다.</summary>
    private string IsProcessRunning(string processName)
    {
        try
        {
            return System.Diagnostics.Process.GetProcessesByName(processName).Length > 0 ? "있음" : "없음";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Update] 프로세스 조회 실패({Name}) — 판정 불가.", processName);
            return "조회불가";
        }
    }

    /// <summary>폴더를 best-effort 로 지운다(없으면 no-op). 실패해도 흐름을 막지 않되 침묵하지 않는다(헌법 #15).</summary>
    private void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Update] 임시 폴더 정리 실패({Dir}) — 다음 시도에서 재정리됩니다.", dir);
        }
    }

    /// <summary>
    /// W4-5 검증 게이트 — 재시작 후 "정말 신버전이 살아 도는가"를 2중으로 판정한다.
    ///
    ///   ① API /health 폴링(2초 간격 × 최대 60초): HTTP 200 AND 본문 checks.version == 신버전.
    ///      200 만으론 불통과다 — 구버전도 200 을 준다. 반드시 버전 문자열까지 같아야 통과.
    ///   ② 교차 판정: {app}\api\HitPan.API.dll 의 FileVersion == 신버전.
    ///      /health 응답은 미들웨어·라우팅 설정 하나에 묶인 단일 실패점이라, 실제 교체된 파일 버전으로 교차한다.
    ///
    /// 둘 다 통과해야만 true. 하나라도 어긋나면 false(호출부가 롤백). "모르면 통과" 는 절대 없다 — 거짓성공 차단.
    /// 버전 동일성은 UpdateClient.IsSameVersion(3자리 정규화)로, "1.2.34" 와 "1.2.34.0" 표기 차이가
    /// 버전 차이로 둔갑하지 않게 한다(W4-0 F-4 함정 재사용 방지).
    /// </summary>
    private async Task<bool> VerifyNewVersionAsync(int slot, UpdateManifest manifest, CancellationToken ct)
    {
        // ── ② 먼저: 교체된 EXE(dll) FileVersion 교차 ── 파일은 이미 자리에 있으니 즉시 판정 가능(빠른 실패).
        var appApiDll = Path.Combine(AppRoot(), "api", "HitPan.API.dll");
        var fileVersion = ReadApiFileVersion(appApiDll);
        if (fileVersion is null || !UpdateClient.IsSameVersion(fileVersion, manifest.Version))
        {
            _logger.LogError("[Update] 🛑 검증 실패(FileVersion 교차) — 교체된 파일 버전='{File}', 기대='{V}'. " +
                             "스왑이 어긋났거나 잘못된 zip 입니다.", fileVersion ?? "(읽기실패)", manifest.Version);
            return false;
        }

        // ── ① API /health 폴링: 2초 간격 × 최대 60초, 200 AND checks.version == 신버전 ──
        var healthUrl = ResolveLocalApiHealthUrl();
        if (string.IsNullOrWhiteSpace(healthUrl))
        {
            // 로컬 API 헬스 URL 을 못 구하면 신버전 생존을 확인할 길이 없다 → 통과시키지 않는다(거짓성공 차단).
            _logger.LogError("[Update] 🛑 검증 실패 — 로컬 API 헬스 URL 을 구성하지 못했습니다(db.conf API_PORT 확인). " +
                             "신버전 생존을 확인할 수 없어 롤백합니다.");
            return false;
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        var attempt = 0;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            attempt++;

            var (http200, healthVersion) = await ReadApiHealthVersionAsync(healthUrl, ct);
            if (http200 && UpdateClient.IsSameVersion(healthVersion, manifest.Version))
            {
                _logger.LogInformation("[Update] 검증 통과 — /health 200 + 버전 일치('{V}') + FileVersion 교차(시도 {N}회, 슬롯 {Slot}).",
                    manifest.Version, attempt, slot);
                return true;
            }

            // 아직이면 로그는 시끄럽지 않게 요약만(폴링 성격상 초반 몇 회는 기동 중이라 정상 미달).
            _logger.LogInformation("[Update] /health 대기 중(시도 {N}) — 200={Ok}, 버전='{HV}'(기대 '{V}')",
                attempt, http200, healthVersion ?? "(없음)", manifest.Version);

            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }

        _logger.LogError("[Update] 🛑 검증 실패 — 60초 안에 API /health 가 신버전('{V}')으로 200 을 주지 않았습니다(시도 {N}회). " +
                         "신버전이 뜨지 못했거나 여전히 구버전입니다.", manifest.Version, attempt);
        return false;
    }

    /// <summary>
    /// 로컬 API /health URL 진실원 — WatchdogOptions.Processes.HttpEndpoints 의 'HitPan.API' 엔드포인트 Url.
    ///   DbConfReader.ApplyToOptions 가 db.conf 의 API_PORT 로 'http://127.0.0.1:{포트}/health' 로 덮어쓴다.
    ///   ※ WatchdogOptions.HealthCheckUrl 은 '외부 터널'(https://{도메인}/health) 이고 localhost 면 비활성이라,
    ///     방금 재시작한 '로컬 프로세스' 생존 확인에는 부적합하다. 그래서 로컬 엔드포인트 Url 을 쓴다.
    /// </summary>
    private string? ResolveLocalApiHealthUrl()
    {
        var ep = _options.Processes.HttpEndpoints
            .FirstOrDefault(e => string.Equals(e.Name, "HitPan.API", StringComparison.OrdinalIgnoreCase));
        return ep?.Url;
    }

    /// <summary>
    /// API /health 를 1회 호출해 (HTTP 200 여부, 본문 checks.version)을 돌려준다. 예외는 삼키지 않되
    /// (헌법 #15) 폴링 1회 실패는 치명이 아니므로 Warning 으로만 남기고 (false, null)로 반환한다.
    /// </summary>
    private async Task<(bool http200, string? version)> ReadApiHealthVersionAsync(string url, CancellationToken ct)
    {
        try
        {
            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);

            using var resp = await http.GetAsync(url, ct);
            if (resp.StatusCode != System.Net.HttpStatusCode.OK)
                return (false, null);   // unhealthy(503) 등 — 200 아니면 불통과. 버전 파싱 불필요.

            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("checks", out var checks) &&
                checks.TryGetProperty("version", out var ver) &&
                ver.ValueKind == JsonValueKind.String)
            {
                return (true, ver.GetString());
            }
            // 200 이지만 버전 필드가 없다 = 판정 불가. 통과시키지 않는다.
            return (true, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // 취소는 폴링 루프가 처리한다.
        }
        catch (Exception ex)
        {
            // 재시작 직후엔 아직 포트가 안 열려 연결거부가 정상적으로 난다 — 폴링이라 Warning 으로만.
            _logger.LogWarning(ex, "[Update] /health 조회 실패(폴링 재시도됨): {Url}", url);
            return (false, null);
        }
    }

    /// <summary>
    /// 교체된 {app}\api\HitPan.API.dll 의 FileVersion 을 읽는다. 없거나 읽기 실패면 null(호출부가 실패 처리).
    /// </summary>
    private string? ReadApiFileVersion(string dllPath)
    {
        try
        {
            if (!File.Exists(dllPath))
            {
                _logger.LogError("[Update] 🛑 교체된 API 파일을 찾지 못했습니다: {Path}", dllPath);
                return null;
            }
            return FileVersionInfo.GetVersionInfo(dllPath).FileVersion;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Update] 🛑 FileVersion 읽기 실패: {Path}", dllPath);
            return null;
        }
    }

    /// <summary>
    /// W4-5 롤백 — 검증 실패 시 구버전으로 되돌린다. 이게 실패하면 ERP 가 영구 정지하므로 가장 신중히 다룬다.
    ///
    /// 순서(작지서 §W4-5 그대로):
    ///   1) 신버전 정지 — keepalive DISABLE + 신버전 프로세스 종료. StopForSwapAsync 를 그대로 재사용
    ///      (이미 정지 로직이고, 재실행해도 멱등 — 이미 꺼진 keepalive DISABLE·이미 죽은 프로세스 kill 은 무해).
    ///   2) 파일 복원 — {app}\api.old → {app}\api, {app}\web.old → {app}\web (신버전을 밀어내고 구버전 복귀).
    ///   3) keepalive ENABLE + 재기동(api→web) — RestoreKeepalive + TryRestartTask 재사용.
    ///   4) 구버전 /health 폴링 통과 = 롤백 성공.
    ///   5) 롤백까지 실패 = 최악(구버전 복원조차 안 됨). 치명 LogError + finally 의 keepalive 복원·부팅
    ///      안전망(②)·기동 시 자가치유(③)에 최종 위임. 절대 조용히 넘기지 않는다.
    ///
    /// ⚠️ DB 자동복원은 하지 않는다(사장님 결재 #3). 1차는 마이그 없는 릴리스라 스키마가 그대로이고,
    ///    DB 를 되돌리면 업데이트 중 들어온 정상 거래가 삭제된다. 백업은 보험으로 존재만 한다.
    /// </summary>
    /// <returns>구버전으로 정상 복원됐으면 true, 복원조차 실패(최악)면 false.</returns>
    private async Task<bool> RollbackToOldAsync(int slot, UpdateManifest manifest, CancellationToken ct)
    {
        _logger.LogError("[Update] ⏪ 롤백 시작 — 구버전으로 되돌립니다(슬롯 {Slot}, {V}).", slot, manifest.Version);
        var appRoot = AppRoot();
        var appApi = Path.Combine(appRoot, "api");
        var appApiOld = Path.Combine(appRoot, "api.old");
        var appWeb = Path.Combine(appRoot, "web");
        var appWebOld = Path.Combine(appRoot, "web.old");

        // .old 가 없으면 되돌릴 구버전 자체가 없다 = 복원 불가. 이 상태는 명확히 치명으로 남긴다.
        if (!Directory.Exists(appApiOld) && !Directory.Exists(appWebOld))
        {
            _logger.LogError("[Update] 🛑🛑 롤백 불가 — 구버전 백업(api.old·web.old)이 없습니다. " +
                             "finally 의 keepalive 복원·부팅 안전망·자가치유에 최종 위임합니다(슬롯 {Slot}).", slot);
            return false;   // 호출부가 rollback_failed 기록 + 긴급 통지.
        }

        // ── 1) 신버전 정지 (StopForSwapAsync 재사용 — keepalive DISABLE + 프로세스 종료) ──
        //   실패해도 계속 진행한다: 파일 복원을 시도해야 그나마 구버전이 살아날 여지가 생긴다.
        //   (프로세스가 안 죽어 파일이 잠기면 아래 Move 가 실패하고, 그건 아래에서 치명으로 잡힌다.)
        if (!await _gate.StopForSwapAsync(slot, ct))
            _logger.LogError("[Update] ⚠️ 롤백: 신버전 정지에 실패했습니다 — 파일 복원을 시도하나 잠겨 있으면 실패할 수 있습니다(슬롯 {Slot}).", slot);

        // ── 2) 파일 복원: api.old → api, web.old → web ──
        //   현재 api/web 에는 신버전이 들어 있다. 먼저 신버전을 옆으로 치우고(.failed) old 를 제자리로.
        var restoredApi = RestoreOneFromOld(appApi, appApiOld, "api");
        var restoredWeb = RestoreOneFromOld(appWeb, appWebOld, "web");

        if (!restoredApi || !restoredWeb)
        {
            // 파일 복원조차 실패 = 최악. 구버전이 제자리에 없을 수 있다.
            _logger.LogError("[Update] 🛑🛑 롤백 파일 복원 실패(api복원={A}, web복원={W}, 슬롯 {Slot}) — " +
                             "finally 의 keepalive 복원·부팅 안전망·자가치유에 최종 위임합니다.", restoredApi, restoredWeb, slot);
            return false;   // 호출부가 rollback_failed 기록 + 긴급 통지.
        }

        // ── 3) keepalive ENABLE + 재기동(api→web) ──
        _gate.RestoreKeepalive(slot);
        var apiUp = _fourProcess.TryRestartTask(UpdateProcessGate.ApiTask(slot));
        var webUp = _fourProcess.TryRestartTask(UpdateProcessGate.WebTask(slot));
        if (!apiUp || !webUp)
            _logger.LogError("[Update] ⚠️ 롤백: 구버전 재기동 명령 실패(api={A}, web={W}) — keepalive 가 1분 내 되살립니다(슬롯 {Slot}).", apiUp, webUp, slot);

        // ── 4) 구버전 /health 폴링 통과 확인 ── (구버전이므로 신버전이 아닌 '200 + 살아있음'까지만 본다) ──
        //   여기서 '신버전과 같은지'는 보지 않는다 — 롤백은 구버전으로 돌아가는 것이라 당연히 버전이 다르다.
        var healthUrl = ResolveLocalApiHealthUrl();
        var alive = await WaitApiAliveAsync(healthUrl, TimeSpan.FromSeconds(60), ct);
        if (alive)
        {
            _logger.LogInformation("[Update] ⏪ 롤백 성공 — 구버전 ERP 가 다시 응답합니다(슬롯 {Slot}).", slot);
            return true;
        }

        _logger.LogError("[Update] 🛑🛑 롤백 후에도 구버전 API 가 60초 안에 응답하지 않습니다(슬롯 {Slot}) — " +
                         "keepalive·부팅 안전망·자가치유가 뒤를 받칩니다. 최악의 경우 CS 개입 필요.", slot);
        return false;   // 파일은 되돌렸으나 생존 미확인 = 최악 취급(rollback_failed + 긴급 통지).
    }

    /// <summary>
    /// W4-6 — 적용 결과를 local_update_apply_status 에 기록한다(성공·롤백·롤백실패·차단 각 종점).
    ///   기록 실패는 침묵하지 않되(라이터가 로그) 업데이트 흐름을 멈추지 않는다. 취소는 rethrow.
    /// </summary>
    private async Task RecordApplyStatusAsync(UpdateManifest manifest, string result, string? detail, CancellationToken ct)
    {
        try
        {
            await _statusWriter.WriteApplyStatusAsync(manifest.Version, result, detail, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // 헌법 #15: 기록 실패도 침묵 금지. 적용 자체의 성패와 별개라 여기서 흐름을 끊지 않는다.
            _logger.LogError(ex, "[Update] 적용결과 기록 호출 실패 — 버전 {V}, 결과 {R}", manifest.Version, result);
        }
    }

    /// <summary>
    /// W4-6 — 롤백까지 실패한 최악 종점에서 본사에 긴급 통지한다(헌법 #30 — 본사는 통지만, 업무데이터 0).
    ///   검증팀이 지적한 "롤백실패가 본사에 안 보인다" 구멍을 닫는다. 메타핑 경로는 이미 있어 재사용한다.
    /// </summary>
    private async Task NotifyRollbackFailedAsync(UpdateManifest manifest, CancellationToken ct)
    {
        try
        {
            await _meta.NotifyEmergencyAsync("update_rollback_failed", $"W4-5:{manifest.Version}", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Update] 롤백실패 긴급 통지 호출 실패 — 버전 {V}", manifest.Version);
        }
    }

    /// <summary>
    /// W4-6 — 재시작·검증 실패 종점 공통 마무리: 롤백 실행 → 결과에 따라 apply_status 기록 + 통지.
    ///   롤백 성공 = rolled_back 기록(구버전 정상 복귀). 롤백 실패 = rollback_failed 기록 + 본사 긴급 통지
    ///   (검증팀이 지적한 "롤백실패가 본사에 안 보인다" 구멍 봉합, 헌법 #30 본사는 통지만).
    /// </summary>
    private async Task FinishWithRollbackAsync(int slot, UpdateManifest manifest, string reason, CancellationToken ct)
    {
        var rolledBack = await RollbackToOldAsync(slot, manifest, ct);
        if (rolledBack)
        {
            await RecordApplyStatusAsync(manifest, "rolled_back", $"{reason} → 구버전 복원 성공", ct);
        }
        else
        {
            await RecordApplyStatusAsync(manifest, "rollback_failed", $"{reason} → 롤백까지 실패(CS 개입 필요)", ct);
            await NotifyRollbackFailedAsync(manifest, ct);   // 최악 종점만 본사 긴급 통지.
        }
    }

    /// <summary>
    /// W4-6 — 성공 확정 후 정리. .old(롤백 자산)는 성공 확정 후에만 지운다. 부팅 안전망·TTL 표식은 finally 가
    ///   RestoreKeepalive 성공 시 이미 정리하지만, staging·.old·watchdog.new 는 여기서만 치운다(정상 종점).
    /// 모든 삭제는 best-effort(TryDeleteDir) — 정리 실패가 "적용 성공"을 뒤집지 않는다(로그만).
    /// </summary>
    private void CleanupAfterSuccess(int slot)
    {
        var appRoot = AppRoot();
        // .old = 롤백 자산. 성공했으니 더는 필요 없다.
        TryDeleteDir(Path.Combine(appRoot, "api.old"));
        TryDeleteDir(Path.Combine(appRoot, "web.old"));
        // 롤백 조사용으로 남았을 수 있는 .failed 잔재도 정리.
        TryDeleteDir(Path.Combine(appRoot, "api.failed"));
        TryDeleteDir(Path.Combine(appRoot, "web.failed"));
        // 신버전 워치독 예비물(W4-3 미교체). 고리5 전까지는 쓰지 않으므로 성공 시 치운다.
        TryDeleteDir(Path.Combine(appRoot, "watchdog.new"));
        // 다운로드·해제 잔재.
        TryDeleteDir(Path.Combine(_stagingDir, "extract"));
        _logger.LogInformation("[Update] 성공 정리 완료 — .old·.failed·watchdog.new·staging\\extract 제거(슬롯 {Slot}).", slot);
    }

    /// <summary>
    /// 롤백 파일 복원 1건 — 현재 자리(신버전)를 .failed 로 치우고 .old(구버전)를 제자리로 되돌린다.
    /// .old 가 없으면(한쪽만 스왑됐던 경우) 현재 자리가 이미 구버전이라 손대지 않는다(true).
    /// </summary>
    private bool RestoreOneFromOld(string current, string old, string label)
    {
        try
        {
            if (!Directory.Exists(old))
            {
                // 이 컴포넌트는 .old 가 없다 = 스왑 안 됐다 = 현재 자리가 이미 구버전. 정상.
                _logger.LogInformation("[Update] 롤백: {Label}.old 없음 — 이 컴포넌트는 교체되지 않았습니다(현재가 구버전).", label);
                return true;
            }

            // 현재 자리(신버전)를 옆으로 치운다. 롤백 조사·재시도용으로 .failed 에 남긴다.
            if (Directory.Exists(current))
            {
                var failed = current + ".failed";
                TryDeleteDir(failed);
                Directory.Move(current, failed);
            }
            // 구버전을 제자리로.
            Directory.Move(old, current);
            _logger.LogInformation("[Update] 롤백: {Label} 구버전 복원 완료.", label);
            return true;
        }
        catch (Exception ex)
        {
            // 헌법 #15: 여기가 조용히 실패하면 구버전이 안 살아난다. 진단과 함께 명확히 남긴다.
            LogSwapFailureDiagnostics(ex);
            _logger.LogError(ex, "[Update] 🛑 롤백: {Label} 구버전 복원 실패.", label);
            return false;
        }
    }

    /// <summary>구버전 롤백 후 API 가 '200 으로 살아났는지'만 폴링한다(버전 일치는 보지 않는다 — 구버전이니까).</summary>
    private async Task<bool> WaitApiAliveAsync(string? healthUrl, TimeSpan timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(healthUrl))
        {
            _logger.LogWarning("[Update] 롤백 생존 확인 생략 — 로컬 API 헬스 URL 이 없습니다(keepalive 가 뒤를 받칩니다).");
            return false;
        }
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var (http200, _) = await ReadApiHealthVersionAsync(healthUrl, ct);
            if (http200) return true;
            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }
        return false;
    }

    /// <summary>
    /// W4-4 — 스왑된 신버전 ERP 를 다시 띄운다. 인스톨러가 ONSTART 로 등록한 작업을 schtasks /Run 으로
    /// 재기동하는 기존 자산(WS28I_FourProcess.TryRestartTask)을 그대로 재사용한다(신규 구현 0).
    ///
    /// ★ 기동 순서 = api → web (스왑 순서 web→api 와 의도된 비대칭 — "API 가 서야 ERP 가 산다", 작지서 명시).
    /// ★ 정밀 헬스판정(HTTP 200 + 버전/FileVersion 2중)은 W4-5 범위다. 여기선 TryRestartTask 종료코드까지만.
    /// </summary>
    /// <returns>api·web 두 작업의 재기동이 모두 성공하면 true.</returns>
    private bool RestartErp(int slot, UpdateManifest manifest)
    {
        // 1) API 먼저 — ERP 의 심장. 여기부터 살아야 web 이 붙을 API 가 존재한다.
        var apiOk = _fourProcess.TryRestartTask(UpdateProcessGate.ApiTask(slot));
        if (!apiOk)
        {
            // 사유는 TryRestartTask 가 이미 기록했다. api 가 안 뜨면 web 을 띄워도 ERP 는 못 산다.
            _logger.LogError("[Update] 🛑 API 재기동 실패(슬롯 {Slot}) — Web 기동을 진행하지 않습니다({V}).", slot, manifest.Version);
            return false;
        }

        // 2) Web 나중.
        var webOk = _fourProcess.TryRestartTask(UpdateProcessGate.WebTask(slot));
        if (!webOk)
        {
            _logger.LogError("[Update] 🛑 Web 재기동 실패(슬롯 {Slot}, API 는 기동됨)({V}).", slot, manifest.Version);
            return false;
        }

        // 3) keepalive 복원 — 기동 직후 명시적으로 한 번 켠다(작지서 의도: 기동 직후 복원).
        //   finally 가 어차피 무조건 복원하므로 이는 중복 호출이나, /ENABLE 은 멱등이라 무해하다.
        //   여기서 미리 켜두면, 재기동한 인스턴스가 혹시 꺼져도 keepalive 가 즉시 되살린다.
        _gate.RestoreKeepalive(slot);

        _logger.LogInformation("[Update] ERP 재기동 성공(api→web, 슬롯 {Slot})({V})", slot, manifest.Version);
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
