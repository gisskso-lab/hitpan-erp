using System.Reflection;
using HitPan.Watchdog.AutoUpdate;
using HitPan.Watchdog.Stages;
using HitPan.Watchdog.Telemetry;
using Microsoft.Extensions.Options;

namespace HitPan.Watchdog;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly WatchdogOptions _options;
    private readonly WS28A_WindowsUpdate _a;
    private readonly WS28B_PostRebootCheck _b;
    private readonly WS28C_TunnelSecret _c;
    private readonly WS28D_ServiceReinstall _d;
    private readonly WS28E_ExternalHealthCheck _e;
    private readonly WS28F_CoolDown _f;
    private readonly WS28I_FourProcess _i;
    private readonly MetaPingClient _meta;
    private readonly UpdateOrchestrator _update;

    private string? _lastRecoveryStage;
    private DateTime? _lastRecoveryAt;

    // 봉합 (2026-06-29, 작1 고리1): 업데이트 확인을 게이트한다(매 루프 60초마다 manifest 조회 방지).
    //
    // ★ 20260807작2 N-10 (4안 혼합 · 사장님 결재 7건 2026-08-07) — 날짜에서 **시각**으로 바꿨다.
    //   ■ 종전: `DateOnly? _lastUpdateCheckDate` — "오늘 이미 했나"
    //   ■ 무엇을 겪고서 바꿨나
    //     2026-08-07 사장님 백지환경 실측. 게시한 1.2.55 를 워치독이 스스로 발견하지 못했고,
    //     사장님이 `sc stop`/`sc start` 를 직접 치신 뒤에야 잡혔다. 그날 이미 평가를 마쳤기
    //     때문에(19:30 에 1.2.54 까지) 그 뒤 올라온 버전은 다음날까지 조회 대상이 아니었다.
    //     그리고 재시작으로 풀린 것은 **봉합의 작동이 아니라 인메모리 소실의 우연한 이득**이다.
    //     고객 PC 에는 재시작할 사람이 없다 — 최대 24시간 못 받으면서 화면은 정상으로 보인다.
    //     ⚠️ 이 우연에 기대면 CS 매뉴얼에 "서비스를 재시작해 보세요" 가 들어가고,
    //        그건 헌법 #30(고객 손 0번)을 CS 절차로 위장해 위반하는 것이다.
    //   ■ 지금: 마지막 확인 '시각'(UTC)을 기억하고, N시간(WatchdogOptions.ResolvedUpdateCheckInterval,
    //     기본 60분)이 지났으면 어느 분기를 탔든 다시 확인한다 — 해제가 특정 사건이 아니라 시간에 걸린다.
    //     null = "확인한 적 없음" = 즉시 확인(fail-open).
    //   ■ 왜 `4cc5842` 로 안 끝났나 (같은 벽의 두 번째 얼굴)
    //     그 봉합은 "[나중에] 를 눌러 펜딩이 비워진 뒤" 라는 한 시나리오만 열었다(:306 블록 안).
    //     펜딩에 애초에 들어가지 않는 경로 — 평가했는데 새 버전이 없었고 그 뒤 게시된 경우 —
    //     는 그 코드에 도달조차 못 한다. 스위치를 여는 조건이 닫히는 조건보다 좁으면 틈이 남는다.
    private DateTime? _lastUpdateCheckUtc;

    // ★ 20260807작2 N-10 — 위 값을 디스크에도 남긴다(결재-2 파일, DB 아님).
    //   목적은 "게이트 유지"가 아니라 **"재시작 폭주 상한"** 이다. 워치독은 sc failure(5초)·
    //   Guardian(5분)이 되살리므로, 크래시 루프 PC 는 기동마다 manifest 를 1회씩 두드린다.
    //   인메모리만으로는 그 반복에 상한이 없다 — eaba641(디스크 100%)이 가르친 것은
    //   "1회당 비용이 작아도 회전이 없으면 반드시 죽는다" 였다.
    //   🔴 단, N시간이 지났으면 재시작 여부와 무관하게 확인한다. 이 파일이 확인을 막는 근거가 되면
    //      3안(영속화) 단독이 되어 개발 사이클이 24시간에 고정된다 — 그래서 반려된 안이다.
    private readonly UpdateCheckStampFile _updateCheckStamp;

    // ★ 20260807작2 N-10 (설계팀 합격조건 G-2) — "확인을 건너뛰었다"를 Information 으로 마지막에 남긴 시각.
    //   60초 루프라 매번 Information 을 남기면 하루 1,440줄이 쌓여 복구·터널 로그를 밀어낸다.
    //   반대로 Debug 로만 두면 고객 PC 기본 로그레벨(Information)에서 **또 아무것도 안 남는다** —
    //   이번 사고에서 채증 500건 중 업데이트 흔적이 0건이었던 것이 정확히 그 상태다.
    //   그래서 주기당 1줄만 Information 으로 올리고 나머지는 Debug 로 흘린다. 인메모리로 충분하다
    //   (재시작하면 다음 건너뜀에서 한 줄 나올 뿐이고, 그건 오히려 기동 사실이 남아 유익하다).
    private DateTime? _lastUpdateSkipNoticeUtc;

    // Normal 채널 새 버전을 낮에 발견하면, 야간 창(새벽 3시대)이 올 때까지 manifest 재조회 없이 들고 있다가 적용한다
    //   (feed 를 매 루프 두드리지 않으려는 보호 — 확인 주기 게이트 정신 유지).
    //   ※ 20260807작2 N-10: 종전 표현은 "하루 1회 게이트 정신" 이었다. 게이트가 N시간 주기(기본 60분)로
    //     바뀌어 사실과 달라졌다. 이 필드의 역할 자체는 그대로다 — 야간 창까지 manifest 를 들고 있는다.
    private UpdateManifest? _pendingNightUpdate;

    // 🔴 관측 (2026-08-09, 20260809작2 P0-3 · 사장님 결재 — **정책은 안 건드린다, 침묵만 깬다**)
    //   ■ 무엇이 문제인가
    //     야간 창은 새벽 3시대 **1시간뿐**이다(UpdateOrchestrator.IsNightWindow — Hour >= 3 && Hour < 4).
    //     그런데 **사무실 PC 는 밤에 끈다.** 그러면 그 창이 영영 오지 않아 Normal 채널 업데이트가
    //     그 고객에게 **구조적으로 도달하지 않는다.**
    //   ■ 더 나쁜 것은 침묵이다
    //     TrackUpdateStallAsync 는 "feed 조회가 성공했는가"만 본다. 낮에 조회는 잘 되므로
    //     카운터는 0 이고 7일 경보도 안 울린다. 화면·로그·경보 전부 정상으로 보이는데
    //     고객만 옛 버전에 고정된다 — **고객도 본사도 모른다.**
    //   ■ 그래서 이번에 하는 것 / 하지 않는 것
    //     한다   : 펜딩이 며칠째인지 세어 로그로 남긴다(아래). 정책과 무관하다.
    //     안 한다: 야간 창을 넓히거나, 낮에 적용하거나, 임계를 정하는 것 —
    //              전부 사장님 결재 사안이다(작2 §4-5). 낮에 적용하면 영업 중 재기동 위험이 있다.
    //     ⇒ **데이터가 있어야 사장님이 정책을 정하실 수 있다.** 그 데이터를 만드는 것이 이 필드다.
    private DateTime? _pendingNightSinceUtc;

    // 야간 대기 경과 안내를 하루 1회로 제한한다(60초 루프라 매번 남기면 로그가 오염된다).
    //   _lastUpdateSkipNoticeUtc 와 같은 빈도 조절 패턴이다(과설계 방지).
    private DateOnly? _lastNightPendingNoticeDate;

    // 봉합 (2026-06-29, 작1 고리2): Major(동의 필요) 새 버전을 발견하면 manifest 를 들고 있다가,
    //   다음 메타 ping 에 latest_version·update_channel·consent_message 를 실어 본사에 알린다.
    //   A안(2026-06-29 결재): ERP 로그인 동의(고리2 UI)는 본사를 거치지 않고 고객 PC 로컬에서 완결된다 —
    //   동의 결과를 고객 PC 로컬 ERP DB local_update_consents(DB-82)에 INSERT 하고 워치독이 로컬에서 SELECT 한다(헌법 #30).
    //   본사에는 latest_version·update_channel·consent_message 메타만 ping 으로 알린다(MetaPingPayload 확장, A안과 무관·유지).
    //   _pendingNightUpdate 와 동일한 공유 상태 패턴(과설계 방지) — 펜딩 없으면 null 이라 payload 필드도 null(역호환).
    private UpdateManifest? _pendingConsentUpdate;

    // 봉합 (2026-06-29, 작1 고리2 워치독 측): 이미 적용을 시도(ApplyUpdateAsync 호출)한 버전을 기억해
    //   같은 버전을 중복 적용하지 않는다(멱등). 적용 진입 시 _pendingConsentUpdate 를 비우는 것에 더해,
    //   거부 후 거부분이 다시 펜딩으로 돌아오는 케이스까지 막기 위해 적용 시도 버전 집합을 별도로 둔다.
    //   고리4(실 적용)가 붙으면 이 집합이 "이미 처리한 버전" 기준이 된다(재처리 차단).
    // ⬛ 20260929작3 절W4 — 위 설명의 「버전」은 **낡았다.** 키를 버전 → **동의 id**(local_update_consents.id)로 바꿨다
    //   (설계 §3 · 헌법 #1 수정 허용 · 이름은 grep 연속성을 위해 그대로 둔다).
    //   ■ 왜: 버전 키면 실패한 [예] 뒤에 사장님이 **새 [예]** 를 눌러도 같은 워치독 수명 안에서는 영영 무시된다(N-UPD2 · B-1).
    //   ■ 지금: 「[예] 한 번 = 시도 한 번」. 같은 동의 id 는 두 번 적용하지 않고, 새 동의 id 는 새 시도다.
    //     DB 의 결과행 consent_id(재기동 뒤에도 남는 기록)와 함께 이중 안전장치다.
    private readonly HashSet<long> _consentAppliedVersions = new();

    // ══════════════════════════════════════════════════════════════════════════════════
    // 20260929작3 — 규칙 C·기동 정리의 바깥 호출 자리(시험 대역 자리)
    //   Worker 의 구체 의존(동의 리더·상태 기록기·오케스트레이터)은 DB·프로세스·파일을 실제로 건드린다.
    //   게이트 G-W1~G-W7 이 그 자리에 대역을 끼워 「같은 인스턴스·새 인스턴스」 동작을 재도록
    //   호출을 대리자 한 겹으로 모았다. 운영에서는 생성자가 실제 메서드를 그대로 꽂는다(동작 불변).
    //   ⚠️ 운영 코드는 이 필드를 바꾸지 않는다 — 시험(InternalsVisibleTo)만 바꾼다.
    internal Func<string, CancellationToken, Task<ConsentUsage>> ReadConsentUsageSeam;
    // ⬛ 갈래 R — WriteApplyStatusSeam(apply_status 에 in_progress·consent_id)은 폐기 → 시도 표 열기·닫기 두 자리.
    internal Func<long, string, CancellationToken, Task<AttemptOpenResult>> OpenAttemptSeam;
    internal Func<string, string, string?, CancellationToken, Task<bool>> CloseAttemptSeam;

    // 20260929작3 갈래 R 규칙 Z-1(설계 §12-5 · PM 결재 3루프) — 시도 표 때문에 적용을 보류한 동의 id 와 연속 횟수.
    //   같은 동의 id 로 연속 3루프까지만 보류하고, 넘으면 종전 규칙(이 수명에서 처음 보는 동의 id → 적용)으로 내려앉는다.
    internal const int AttemptHoldLimit = 3;
    private long _attemptHoldConsentId;
    private int _attemptHoldCount;
    internal Func<UpdateManifest, CancellationToken, Task<bool>> ApplyUpdateSeam;
    internal Action<UpdateManifest> ReportConsentRejectedSeam;
    internal Func<bool, CancellationToken, Task<bool>> CloseInterruptedAttemptsSeam;
    internal Func<string> AppRootSeam;

    // 20260929작3 갈래 X · F-2 — 「이미 쓴 [예]」 안내를 Information 으로 남긴 마지막 동의 id(같은 id 반복은 Debug).
    private long _usedApproveNoticeConsentId;

    // 20260929작3 갈래 X 보완(PM 결재 9/30) — 이번 소비가 「이미 쓴 [예]」로 펜딩을 유지했는가.
    //   ConsumeConsentForMajorAsync 가 매번 먼저 false 로 두고 그 분기에서만 true. 호출부가 정규 확인 주기로 내려갈지 가른다.
    private bool _pendingHeldByUsedApprove;

    /// <summary>20260929작3 갈래 X 보완 · G-X3 시험용 — 마지막 정규 확인 시각(확인 게이트)을 읽고 쓴다.</summary>
    internal DateTime? LastUpdateCheckUtcForTest
    {
        get => _lastUpdateCheckUtc;
        set => _lastUpdateCheckUtc = value;
    }

    /// <summary>20260929작3 갈래 X · G-X2 시험용 — 한 루프의 업데이트 평가(운영 RunOneLoop 가 부르는 그 함수)를 그대로 부른다.</summary>
    internal Task EvaluateUpdateForTestAsync(CancellationToken ct) => EvaluateUpdateOncePerDayAsync(ct);

    /// <summary>20260929작3 갈래 X · G-X2 시험용 — 확인 게이트를 만료시킨다(앞선 시험이 남긴 stamp 로 게이트가 닫혀 대조군이 헛돌지 않게).</summary>
    internal void ExpireUpdateCheckGateForTest() => _lastUpdateCheckUtc = null;

    /// <summary>시험용 — 펜딩 Major 를 읽고 쓴다(운영 코드는 필드를 직접 쓴다).</summary>
    internal UpdateManifest? PendingConsentUpdateForTest
    {
        get => _pendingConsentUpdate;
        set => _pendingConsentUpdate = value;
    }

    private readonly WatchdogConsentReader _consent;

    // W4-6 정지공격 감지: manifest '조회'가 며칠(날짜) 연속 실패하면 feed 가 끊긴 것(장애·정지공격)이라
    //   고객이 영영 옛 버전에 고정된다. 조회 실패한 날짜를 세어 임계(7일) 연속이면 본사에 통지한다.
    //   같은 날 여러 번 세지 않도록 '마지막으로 실패를 집계한 날짜'를 기억한다.
    private int _updateFetchFailStreakDays;
    private DateOnly? _lastFetchFailCountedDate;
    private const int UpdateStallNotifyDays = 7;

    // 봉합 (2026-06-29, 작1 고리2 마지막 빈 칸): 발견한 Major 새버전을 로컬 DB(local_update_status, DB-83)에
    //   적재하는 라이터. ERP 가 로그인 시 그 행을 읽어 Y/N 동의 팝업을 노출한다(A안, 헌법 #30 본사 의존 0).
    private readonly WatchdogStatusWriter _statusWriter;
    // W4-1 (2026-07-16): 교체 구간 정지·복원 게이트 + 진행 표식. 기동 시 자가 점검(③ 보장)에 쓴다.
    private readonly UpdateProcessGate _updateGate;
    private readonly UpdateLockFile _updateLock;

    public Worker(
        ILogger<Worker> logger,
        IOptions<WatchdogOptions> options,
        WS28A_WindowsUpdate a,
        WS28B_PostRebootCheck b,
        WS28C_TunnelSecret c,
        WS28D_ServiceReinstall d,
        WS28E_ExternalHealthCheck e,
        WS28F_CoolDown f,
        WS28I_FourProcess i,
        MetaPingClient meta,
        UpdateOrchestrator update,
        WatchdogConsentReader consent,
        WatchdogStatusWriter statusWriter,
        UpdateProcessGate updateGate,
        UpdateLockFile updateLock,
        UpdateCheckStampFile updateCheckStamp)
    {
        _logger = logger;
        _options = options.Value;
        _a = a; _b = b; _c = c; _d = d; _e = e; _f = f; _i = i;
        _meta = meta;
        _update = update;
        _consent = consent;
        _statusWriter = statusWriter;
        _updateGate = updateGate;
        _updateLock = updateLock;
        _updateCheckStamp = updateCheckStamp;

        // 20260929작3 — 바깥 호출 자리에 실제 메서드를 꽂는다(운영 동작 = 종전 직접 호출과 같다).
        ReadConsentUsageSeam = _consent.ReadLatestWithUsageAsync;
        OpenAttemptSeam = _statusWriter.OpenAttemptAsync;
        CloseAttemptSeam = _statusWriter.CloseAttemptAsync;
        ApplyUpdateSeam = _update.ApplyUpdateAsync;
        ReportConsentRejectedSeam = _update.ReportConsentRejected;
        CloseInterruptedAttemptsSeam = _statusWriter.CloseInterruptedAttemptsAsync;
        AppRootSeam = UpdateOrchestrator.AppRoot;

        // ★ 20260807작2 N-10 — 재시작 폭주 상한 복원.
        //   디스크에 남은 마지막 확인 시각을 인메모리로 되살린다. 읽기 실패·손상·미래 시각이면
        //   null 이 돌아오고(fail-open) 첫 루프가 즉시 확인한다 — "덜 확인"이 아니라 "더 확인" 쪽.
        _lastUpdateCheckUtc = _updateCheckStamp.ReadLastCheckedUtc();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 봉합 (2026-07-16, 작1 W4-0): 종전 "v1.0.0" 하드코딩 — 로그가 늘 1.0.0 이라 CS 가 버전을 오판했다.
        _logger.LogInformation("HitPan Watchdog started v{Version}", VersionInfo.Current);

        // 20260929작3 절W6 — 교체 중 중단(전원 종료 등) 정리를 **가장 먼저** 한다(설계 §5-4 · §6).
        //   ① 폴더 복원(교체 표식이 있을 때만 R1~R3) ② 남은 in_progress 행을 failed/rolled_back 으로 닫기.
        //   설계는 「:195 기동 복원 블록 앞」을 지정했다 — 그보다 더 앞(keepalive 자가 점검 전)에 둔 이유:
        //   keepalive 가 되살아나 ERP 가 뜬 뒤에는 그 폴더가 잠겨 옮길 수 없다. 폴더를 먼저 제자리에 둔다.
        //   이 호출은 예외를 밖으로 던지지 않는다(취소 제외) — 실패해도 기동은 계속한다(헌법 #15·#20).
        if (OperatingSystem.IsWindows())
            await RecoverInterruptedUpdateAtStartupAsync(stoppingToken);

        // W4-1 ③ 자가 점검 (2026-07-16, 사장님 결재) — 재부팅 점검보다 먼저 한다.
        //   업데이트가 keepalive 를 끈 뒤 워치독이 죽으면 keepalive 가 꺼진 채 남아 ERP 가 영영 안 뜬다.
        //   워치독은 sc failure(5초)·Guardian(5분)이 되살리므로, 기동 즉시 이걸 풀면 재부팅을 기다리지
        //   않고 최대 5분 안에 ERP 가 복구된다. 다른 무엇보다 "ERP 가 안 떠 있는 상태"를 먼저 푼다.
        if (OperatingSystem.IsWindows())
            _updateGate.SelfHealKeepaliveIfAbandoned(_updateLock);

        // W4-3 ③ 자가 점검 (20260806작3, [3-V] 병렬검증 P1-2 봉합) — 위와 같은 이유로 기동 즉시 한다.
        //   자기교체가 예약만 되고 끝나지 못하면(2분 사이 재시작·정전) 부팅 복구 작업이 매 부팅 남고,
        //   Guardian 이 꺼진 채 남을 수도 있다. 둘 다 여기서 되돌린다.
        if (OperatingSystem.IsWindows())
            _update.CleanupStaleSelfReplaceArtifacts();

        // ★ 20260807작2 N-10 — 확인 주기를 기동 로그에 남긴다.
        //   이번 사고의 진짜 원인은 침묵이었다(채증 500건 중 업데이트 키워드 0건). 어떤 주기로 도는지가
        //   로그 어디에도 없으면, 다음에 "왜 안 받나" 를 물었을 때 또 코드를 열어야 한다.
        var checkInterval = _options.ResolvedUpdateCheckInterval;
        if (_options.IsUpdateCheckIntervalClamped)
        {
            // 설정값이 상·하한(30~3600초)에 걸려 잘렸다. 조용히 잘라내면 "설정했는데 왜 안 먹지" 가 된다.
            //   특히 상한 초과는 야간 창(새벽 3시대 1시간)을 건너뛰는 사고와 직결되므로 반드시 알린다.
            _logger.LogWarning("[Update] 확인 주기 설정 {Set}초가 허용 범위({Min}~{Max}초)를 벗어나 {Applied}초로 조정됐습니다. 상한 근거: {Max}초를 넘기면 야간 창(새벽 3시대 1시간)을 통째로 건너뛸 수 있습니다.",
                _options.UpdateCheckIntervalSeconds,
                WatchdogOptions.UpdateCheckIntervalMinSeconds,
                WatchdogOptions.UpdateCheckIntervalMaxSeconds,
                (int)checkInterval.TotalSeconds,
                WatchdogOptions.UpdateCheckIntervalMaxSeconds);
        }
        _logger.LogInformation("[Update] 새 버전 확인 주기 {N}초. 마지막 확인 기록: {Last} (기록 파일 {Path})",
            (int)checkInterval.TotalSeconds,
            _lastUpdateCheckUtc?.ToString("o") ?? "없음(첫 확인은 즉시)",
            _updateCheckStamp.Path);

        // W4-6 펜딩 소실 복원: _pendingConsentUpdate 는 인메모리라 워치독 재시작 시 Major 펜딩이 날아간다.
        //   확인 게이트 때문에 방금 평가했으면 다음 주기까지 동의 폴링이 죽는다. 로컬에 '발견해 둔'
        //   Major(local_update_status)가 있으면 게이트를 즉시 풀어(재조회) manifest 를 다시 받아
        //   펜딩을 '정상'(서명·Sha256 포함 full manifest)으로 복원한다 — 여기서 부분 manifest 를 만들지 않는다.
        // ※ 20260807작2 N-10: 종전 주석은 "하루 1회 게이트 … 다음날까지" 였다. 게이트가 N시간(기본 60분)
        //   주기로 바뀌었으므로 표현을 사실에 맞췄다. 이 해제 지점 자체는 존치한다(헌법 #1, 결재-4) —
        //   N시간을 기다리지 않고 **즉시 만료**시키는 경로로 여전히 유효하다.
        try
        {
            var pendingMajor = await _consent.TryGetPendingMajorVersionAsync(stoppingToken);
            if (!string.IsNullOrEmpty(pendingMajor))
            {
                _lastUpdateCheckUtc = null;   // 확인 게이트 즉시 만료 → 다음 루프가 즉시 재조회·재펜딩.
                _logger.LogInformation("[Update] 기동 시 로컬 Major 펜딩({V}) 발견 — 확인 게이트를 즉시 만료시켜 동의 폴링을 재개합니다(다음 주기를 기다리지 않음).", pendingMajor);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // 헌법 #15: 복원 조회 실패도 침묵 금지. 실패해도 정기 루프가 다음 확인 주기(N분)에 재평가하므로 치명 아님.
            //   ※ 20260807작2 N-10: 종전 "다음날 재평가" — 게이트가 N시간 주기로 바뀌어 사실과 달라졌다.
            _logger.LogWarning(ex, "[Update] 기동 시 Major 펜딩 복원 조회 실패 — 정기 루프에서 재평가");
        }

        if (OperatingSystem.IsWindows() && _b.ShouldRunPostRebootCheck())
        {
            // P0-1 봉합(2026-06-20): 종전엔 플래그만 확인하고 ClearFlag 후 끝나, Windows Update
            // 강제 재부팅으로 터널이 깨져도 워치독이 "점검"만 하고 봉합을 안 했다(5/15 demo 6시간
            // 다운 = 이 시나리오). 재부팅 직후엔 첫 정기 루프를 기다리지 않고 즉시 터널/서비스
            // 무결성을 강제 점검·복구한다(헌법 #28 5단계, #30 자가회복).
            _logger.LogWarning("WS-28-B: post-reboot check active — 통신 무결성 즉시 점검·복구 시작");
            await RunPostRebootRecoveryAsync(stoppingToken);
            _b.ClearFlag();
        }

        var tickCount = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOneLoopAsync(stoppingToken);
                tickCount++;
                if (tickCount % _options.MetaPingIntervalMinutes == 0)
                    await SendMetaPingAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watchdog loop failure");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(_options.LoopIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOneLoopAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogDebug("Non-Windows platform — skipping Windows-specific stages");
            await _e.PingAsync(ct);
            return;
        }

        var procStatus = await _i.CheckAllAsync(ct);
        foreach (var (name, ok) in procStatus)
        {
            if (!ok && _options.Processes.Services.Contains(name))
            {
                if (_f.AllowRecovery($"svc:{name}"))
                {
                    _i.TryRestartService(name);
                    MarkRecovery($"WS-28-I/{name}");
                }
            }
        }

        // 봉합 (2026-06-25, ERP 자가복구 A): HTTP 엔드포인트(ERP API)가 응답 없으면 재기동.
        //   종전엔 procStatus 에 HitPan.API=false 가 기록돼도 위 루프가 Services 목록만 보고 ERP 를
        //   재기동하지 않아, ERP 가 떠 있다 죽으면 영영 안 살아났다(2026-06-25 demo 502 사고 = 이 구멍).
        //   각 엔드포인트의 RestartTask(작업 스케줄러 작업명)를 schtasks /Run 으로 재실행한다.
        //   RestartTask 가 비면(LOCAL·미설정) 감지만 하던 종전 동작 유지.
        foreach (var ep in _options.Processes.HttpEndpoints)
        {
            if (procStatus.TryGetValue(ep.Name, out var epOk) && !epOk
                && !string.IsNullOrWhiteSpace(ep.RestartTask))
            {
                if (_f.AllowRecovery($"http:{ep.Name}"))
                {
                    if (_i.TryRestartTask(ep.RestartTask))
                        MarkRecovery($"WS-28-I/{ep.Name}");
                }
            }
        }

        var secretInvalid = _c.DetectInvalidSecret();
        if (secretInvalid)
        {
            if (_f.AllowRecovery("TunnelSecret"))
            {
                if (await _c.RegenerateAsync(ct))
                    MarkRecovery("WS-28-C");
            }
            else
            {
                await _meta.NotifyEmergencyAsync("cooldown_exceeded", "WS-28-C", ct);
            }
        }

        // 봉합 (2026-06-21, 7차 전수조사 D6-P0-02-FIX, 교차검증 설계팀장 P1):
        //   종전 D 게이트는 !ServiceExists 단독이라, 관리형 터널이 '서비스는 살아있고 secret 만 무효화'된
        //   대표 다운 모드(헌법 #28 5/15 demo 사고)에서 D 가 호출되지 않아 토큰 재설치가 발화하지 않았다.
        //   관리형 터널이면서 secret 무효화를 감지(secretInvalid)했을 때는 서비스 생존 여부와 무관하게 D 를
        //   강제한다 — C 는 관리형이라 스킵하므로 토큰 재설치(service install {token})만이 유일 복구 경로다.
        //   service install 은 멱등 + AllowRecovery(CoolDown) 게이트로 보호되어, 정상 터널에서 secretInvalid 가
        //   false 면 이 분기로 안 들어오고, 무효화 상태에서도 시간당 반복은 CoolDown 이 제한한다(자해 차단).
        var needsManagedReinstall = secretInvalid && _c.IsManagedTunnel();
        if (!_d.ServiceExists("cloudflared") || needsManagedReinstall)
        {
            if (_f.AllowRecovery("ServiceReinstall"))
            {
                if (await _d.ReinstallAsync(ct))
                    MarkRecovery("WS-28-D");
            }
        }

        var healthy = await _e.PingAsync(ct);
        if (!healthy)
        {
            var streak = _e.IncrementFailure();
            _logger.LogWarning("WS-28-E: health check fail streak {Streak}", streak);
            if (_e.ShouldTriggerRecovery() && _f.AllowRecovery("FullRecovery"))
            {
                // 봉합 (2026-06-23, 5차 전수조사 WD5-02 P2): 종전엔 'FullRecovery'가 본사 통지 +
                //   streak 리셋만 하고 실제 복구를 안 해, 외부 통신이 영원히 안 고쳐졌다(이름만 복구).
                //   헬스체크 실패 누적 = TunnelSecret/서비스가 로컬상 정상으로 보여도 외부에서 안 닿는 상태.
                //   실제 복구 시퀀스(자격증명 재생성 + 서비스 재설치)를 강제 1회 수행하고, 그래도 안 되면
                //   본사에 통지(헌법 #30: 본사는 통지만). streak 리셋은 복구를 시도한 뒤에만.
                _logger.LogWarning("WS-28-E: FullRecovery 발동 — 실제 복구 시퀀스 수행");
                var recovered = false;
                if (await _c.RegenerateAsync(ct)) { MarkRecovery("WS-28-E→C"); recovered = true; }
                // 봉합 (2026-06-21, D6-P0-02-FIX, 설계팀장 P1): 헬스 실패 누적 = 외부 미도달. 관리형 터널이면
                //   C 가 스킵하므로 토큰 재설치가 유일 복구다. 종전 !ServiceExists 단독 게이트는 서비스 생존 +
                //   터널 무효화 상태에서 D 를 건너뛰어 FullRecovery 가 이름만 복구였다(5차 WD5-02 자해 패턴 재현).
                //   관리형이면 서비스 생존 여부와 무관하게 D 강제(멱등 + CoolDown 보호).
                if ((!_d.ServiceExists("cloudflared") || _c.IsManagedTunnel()) && await _d.ReinstallAsync(ct))
                { MarkRecovery("WS-28-E→D"); recovered = true; }

                // 복구 후 재확인 — 여전히 다운이면 본사 통지(운영자 개입 경로).
                if (!await _e.PingAsync(ct))
                {
                    await _meta.NotifyEmergencyAsync("external_health_fail", "WS-28-E", ct);
                    _logger.LogError("WS-28-E: FullRecovery 후에도 외부 헬스체크 실패 — 본사 비상 통지");
                }
                if (recovered) MarkRecovery("WS-28-E");
                _e.ResetFailure();
            }
        }
        else
        {
            _e.ResetFailure();
        }

        if (_a.DetectImminentReboot())
        {
            _b.MarkPostRebootCheck();
            _logger.LogWarning("WS-28-A: reboot imminent, post-check flagged");
        }

        // 봉합 (2026-06-29, 작1 고리1): 버전 업데이트 평가. 통신 무결성 단계 뒤에 둔다
        //   — 통신이 안 되면 manifest 조회 자체가 무의미하기 때문(헌법 #27 통신 무결성 우선).
        // ※ 20260807작2 N-10: 종전 "하루 1회 게이트" → N시간 주기 게이트(기본 60분).
        await EvaluateUpdateOncePerDayAsync(ct);
    }

    /// <summary>
    /// 작1 고리1 — N시간 주기 게이트로 버전 업데이트를 평가하고 채널별로 분기한다.
    ///   Normal    = 야간 자동(새벽 3시대, IsNightWindow)에만 적용 진입(고리3 백업→차단까지).
    ///   Emergency = 안내 후 적용(시간 무관, 적용 진입).
    ///   Major     = ERP 로그인 시 동의 대기(고리2 담당) — 워치독은 적용을 시작하지 않고 로그만 남긴다.
    /// 실제 EXE 교체·재시작·검증·롤백(고리4 W4-2~6)은 ApplyUpdateAsync 가 수행한다(커밋 a2c249f). DB 마이그 자동적용은 고리5 범위.
    ///
    /// ⚠️ 20260807작2 N-10 — **이 함수 이름은 더 이상 사실이 아니다.**
    ///   게이트가 "하루 1회"에서 "N시간 주기"(기본 60분)로 바뀌었으므로 OncePerDay 는 거짓이다.
    ///   이번 작업지시서 §5 가 범위를 게이트 봉합으로 한정해 이름 정정은 하지 않았고,
    ///   설계서 A-5 가 "정정 필요성을 남길 것"으로 지시했다. 다음에 이 함수를 여는 사람은
    ///   이름이 아니라 이 주석과 아래 게이트 코드를 근거로 판단할 것.
    /// </summary>
    private async Task EvaluateUpdateOncePerDayAsync(CancellationToken ct)
    {
        // 20260929작3 갈래 X 보완 — 「이미 쓴 [예]」로 Major 펜딩을 든 채 정규 확인 주기로 내려가는 루프인가.
        var majorHeldForRegularCheck = false;

        // 봉합 (2026-06-29, 작1 고리2 워치독 측): Major(동의 필요) 펜딩이 있으면 매 루프 로컬 동의를 읽어
        //   적용 가부를 판단한다. 동의는 ERP 에서 아무 때나 들어올 수 있으므로 확인 주기 게이트와 분리해
        //   _pendingNightUpdate 와 동일한 매 루프 폴링 패턴으로 처리한다(feed 재조회 없음 — 로컬 DB 만 읽음).
        if (_pendingConsentUpdate is { } consentPending)
        {
            await ConsumeConsentForMajorAsync(consentPending, ct);
            // ★ 봉합 20260804작2 P0-1 ([3-V] 병렬검증 적발) — 사장님 오더 ② "바로 수정해서 다시 띄운다"
            //   ■ 무엇이 막혀 있었나
            //     여기서 무조건 return 하고, 아래 확인 게이트는 이미 방금 찍혀 있다. 그래서 사장님이
            //     [나중에] 를 눌러 펜딩이 비워져도 **다음 주기까지는 새 버전을 다시 발견하지 못한다.**
            //     당시엔 그 주기가 하루여서 수정 → 재게시 → 재확인 사이클이 하루 1회로 묶였다.
            //   ■ 봉합: 펜딩이 해소(거부 폐기·적용 진입)된 경우 확인 게이트를 즉시 만료시킨다.
            //     그러면 다음 루프에서 feed 를 다시 읽어 새로 올라온 버전을 발견한다.
            //     펜딩이 그대로면(영업시간 보류 등) 종전처럼 재조회 없이 반환한다 — 불필요한 폴링 0.
            //   ■ 20260807작2 N-10 — 이 봉합은 **존치한다**(헌법 #1, 결재-4). 다만 두 가지를 기록한다.
            //     ① 표현 정정: "하루 게이트 해제" → "확인 게이트 즉시 만료". N시간 주기로 바뀌어
            //        종전 표현이 사실과 달라졌다. 주석이 거짓말하면 다음 사람이 오판한다.
            //     ② 왜 이걸로 N-10 이 안 잡혔나: 이 코드는 `_pendingConsentUpdate` 가 **있었던** 경우에만
            //        도달한다. 평가했는데 새 버전이 없었고 그 뒤 게시된 이번 사례는 이 블록에 들어올 일이
            //        자체가 없다. 그래서 게이트 판정 자리(아래)를 시간 기반으로 바꾸는 것이 근본이다.
            if (_pendingConsentUpdate is null)
            {
                _lastUpdateCheckUtc = null;
                _logger.LogInformation("[Update] Major 펜딩 해소 — 확인 게이트 즉시 만료. 새 버전이 올라와 있으면 다음 루프에서 발견합니다.");
            }
            // 동의 처리(승인 적용 진입/거부 폐기) 후엔 펜딩이 비워졌을 수 있다. 다른 채널 평가와 섞지 않고 반환.
            // 20260929작3 갈래 X 보완(PM 결재 9/30) — 단 「이미 쓴 [예]」로 펜딩을 **유지**한 경우만은 반환하지 않고
            //   아래 **정규 확인 주기(N시간 게이트)** 로 내려간다. 강제 만료는 하지 않는다(F-2 — 2분 주기 재조회 0).
            //   ■ 왜: 실패한 1.3.50 을 고치려고 1.3.51 을 게시해도 고객이 답하기 전까지 1.3.50 에 묶이고,
            //     [예]를 누르면 다시 1.3.50 을 시도하게 된다(「업데이트를 고치는 업데이트가 못 들어오는」 길).
            //     정규 확인에서 더 새 버전이 오면 아래 RequireConsent 가 펜딩을 그 버전으로 바꾼다.
            //   ■ 미응답(None)·조회실패(Error)·기록실패 보류는 종전 그대로 반환한다(범위 밖 · 기존 성질).
            if (_pendingConsentUpdate is null || !_pendingHeldByUsedApprove)
                return;
            majorHeldForRegularCheck = true;
        }

        // 낮에 보류해 둔 Normal 업데이트가 있으면, 야간 창에 진입했을 때 feed 재조회 없이 적용한다.
        // 20260929작3 갈래 X 보완 — Major 펜딩을 든 채 내려온 루프에서는 종전처럼 이 적용을 하지 않는다
        //   (종전엔 Major 펜딩이 있으면 위에서 반환해 여기에 오지 않았다 — 그 성질 유지).
        if (!majorHeldForRegularCheck && _pendingNightUpdate is { } pending && _update.IsNightWindow(DateTime.Now))
        {
            var toApply = _pendingNightUpdate;
            _pendingNightUpdate = null;
            // 20260809작2 P0-3 관측: 야간 창이 실제로 왔다 — 대기 시계를 푼다.
            var waited = _pendingNightSinceUtc is { } since ? DateTime.UtcNow - since : (TimeSpan?)null;
            _pendingNightSinceUtc = null;
            _lastNightPendingNoticeDate = null;
            _logger.LogInformation("[Update] Normal 채널 — 야간 창 진입, 보류분 자동 적용: {V} (대기 {Waited})",
                toApply.Version, waited is { } w ? $"{w.TotalDays:F1}일" : "미상");
            try { await _update.ApplyUpdateAsync(toApply, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogWarning(ex, "[Update] Normal 보류분 적용 중 예외"); }
            return;
        }

        // 🔴 20260809작2 P0-3 관측 — 야간 창에 도달하지 못한 채 며칠이 흘렀는가 (사장님 결재 · 침묵 깨기)
        //   야간 창이 아직 안 왔는데 펜딩이 계속 남아 있으면, 그 사실을 **하루 1줄** 남긴다.
        //   ■ 왜 Information 인가
        //     Debug 로 두면 고객 PC 기본 로그레벨에서 0 줄이다. 종전 :497 이 정확히 그래서
        //     아무 흔적도 없었다. 이 관측의 목적은 CS 가 로그만 보고 알아채는 것이다.
        //   ■ 왜 하루 1줄인가
        //     60초 루프라 매번 남기면 하루 1,440줄로 로그가 오염된다(N-10 에서 배운 것).
        //   ■ 여기서 판정하지 않는다
        //     "며칠이면 이상인가"는 임계 정책이고 사장님 결재 사안이다(작2 §4-5).
        //     이 코드는 **세어서 남기기만** 한다. 경보·차단·정책 변경 어느 것도 하지 않는다.
        if (_pendingNightUpdate is { } stillPending && _pendingNightSinceUtc is { } pendingSince)
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (_lastNightPendingNoticeDate != today)
            {
                _lastNightPendingNoticeDate = today;
                var elapsed = DateTime.UtcNow - pendingSince;
                _logger.LogInformation(
                    "[Update] Normal 보류분 야간 대기 경과 — {V} 를 {Days:F1}일째 적용하지 못했다. " +
                    "야간 창(새벽 3시대 1시간)에 PC 가 켜져 있어야 적용된다. " +
                    "밤에 PC 를 끄는 환경이면 이 값이 계속 늘어난다(정책 미변경 · 관측 기록).",
                    stillPending.Version, elapsed.TotalDays);
            }
        }

        // ★★ 20260807작2 N-10 (4안 혼합 · 사장님 결재 7건) — 확인 게이트: 날짜 → 시각 ★★
        //   ■ 종전 (2026-06-29 ~ 2026-08-07)
        //       var today = DateOnly.FromDateTime(DateTime.Now);
        //       if (_lastUpdateCheckDate == today) return;   ← 로그 0줄
        //   ■ 무엇을 겪고서 바꿨나
        //     2026-08-07 사장님 백지환경 실측. 19:30 에 1.2.54 까지 평가를 마친 뒤 1.2.55 를 게시했는데
        //     워치독이 스스로 발견하지 못했고, 사장님이 `sc stop`/`sc start` 를 직접 치신 뒤에야 잡혔다.
        //     그 return 이 아무 흔적도 안 남겨서, 워치독 이벤트 500건을 뒤져도 무슨 일인지 알 수 없었다
        //     (문제 구간 15분 0건, `[Update|manifest|W4-|1.2.5` 0건). 못 찾은 게 아니라 찾으려 하지 않았다.
        //   ■ 지금
        //     "마지막 확인 후 N시간(기본 60분)이 지났나"로 판정한다. 어느 분기를 탔든, 재시작을 했든
        //     안 했든, 시간이 지나면 반드시 다시 확인한다 — 해제가 특정 사건이 아니라 시간에 걸린다.
        //     기록이 없으면(첫 기동·파일 손상·미래 시각) 즉시 확인한다(fail-open).
        var nowUtc = DateTime.UtcNow;
        var checkInterval = _options.ResolvedUpdateCheckInterval;
        if (_lastUpdateCheckUtc is { } lastChecked && nowUtc - lastChecked < checkInterval)
        {
            // 🔴 건너뛴 사실을 남긴다(설계팀 합격조건 G-2). 이번 사고의 진짜 원인이 침묵이었다.
            //   ■ 빈도 조절 판단 근거
            //     루프가 60초라 매번 남기면 하루 1,440줄이 쌓여, 정작 봐야 할 복구·터널 로그를 밀어낸다
            //     (eaba641 당시 access.log 폭증이 동반 증상이었다 — 로그량은 실재하는 비용이다).
            //     그렇다고 Debug 로만 두면 고객 PC 기본 로그레벨이 Information 이라 **또 아무것도 안 남는다.**
            //     그래서 두 층으로 나눈다:
            //       · 매 루프  → Debug. 개발·검증에서 레벨을 낮추면 전량 관측된다.
            //       · 주기당 1회 → Information. 확인 게이트가 닫힌 채로 흐르고 있다는 사실이
            //                      고객 PC 기본 설정에서도 최소 1줄은 남는다.
            //     판정 기준은 "다음에 같은 일이 나면 로그로 알 수 있는가" 였다. 주기당 1줄이면
            //     "언제 마지막으로 확인했고 언제 다시 하는지"를 항상 되짚을 수 있다.
            var due = lastChecked + checkInterval;
            if (!_lastUpdateSkipNoticeUtc.HasValue || nowUtc - _lastUpdateSkipNoticeUtc.Value >= checkInterval)
            {
                _lastUpdateSkipNoticeUtc = nowUtc;
                _logger.LogInformation("[Update] 확인 건너뜀 — 마지막 확인 {Last:o}(UTC), 다음 확인 예정 {Due:o}(UTC), 주기 {N}분. 새 버전 조회는 이 시각 이후에 다시 돕니다.",
                    lastChecked, due, (int)checkInterval.TotalMinutes);
            }
            else
            {
                _logger.LogDebug("[Update] 확인 건너뜀 — 다음 확인까지 {Remain:N1}분 남음(주기 {N}분).",
                    (due - nowUtc).TotalMinutes, (int)checkInterval.TotalMinutes);
            }
            return;
        }

        try
        {
            var currentVersion = GetCurrentVersion();
            _logger.LogInformation("[Update] 새 버전 확인 시작(current={V}, 주기 {N}분).", currentVersion, (int)checkInterval.TotalMinutes);
            var decision = await _update.EvaluateAsync(currentVersion, ct);

            // 평가했으면 다음 주기까지 닫는다(성공·새버전없음 모두). manifest 조회 실패도 다음 주기에 재시도.
            //   ★ 20260807작2 N-10: 인메모리와 디스크를 함께 갱신한다.
            //     디스크 기록은 재시작 폭주 상한 전용이다 — 쓰기에 실패해도 업데이트를 막지 않는다
            //     (UpdateCheckStampFile 이 로그만 남기고 삼킨다, 헌법 #15). 정상 PC 는 인메모리로 충분하고,
            //     크래시 루프 PC 만 이 기록의 보호를 받는다.
            _lastUpdateCheckUtc = nowUtc;
            _updateCheckStamp.WriteCheckedNow(nowUtc);

            // W4-6 정지공격 감지 — 조회 '실패'만 날짜 단위로 센다(새 버전 없음은 정상이라 제외).
            //   ⚠️ 20260807작2 N-10 · §6 (결재-5) — **이 계수의 전제가 이번 변경으로 무효화됐다.**
            //     TrackUpdateStallAsync 는 "조회를 하루 1번만 한다"는 전제 위에 서 있다. 주기가 60분이 되면
            //     하루 24번 중 **1번만 성공해도 카운터가 0으로 리셋**된다(그 함수 :485-492).
            //     그래서 하루 23시간 feed 가 죽고 1시간만 살아나는 간헐 장애에서는, 고객이 실제로 옛 버전에
            //     고정되면서도 카운터가 영원히 0 이라 7일 경보가 절대 발화하지 않는다.
            //     이것은 4안이 만든 새 결함이 아니라 4안이 **드러낸** 기존 전제의 붕괴다. 즉시 터지지는
            //     않으므로(카운터·임계·통지 자체는 동작) 이번 범위에서 손대지 않는다 — 계수 축을 "날짜"에서
            //     "연속 실패 횟수 + 최근 성공 시각"으로 바꾸는 것은 정지공격 감지 **정책의 재설계**이고
            //     임계 7일의 의미도 다시 정해야 하는 사장님 결재 사안이다.
            //     🔴 후속 과제: 작2 §9-1 "W4-6 계수 전제 재설계". 이 주석을 지우기 전에 그 과제부터 볼 것.
            var today = DateOnly.FromDateTime(DateTime.Now);
            await TrackUpdateStallAsync(today, ct);

            if (decision.Action == UpdateAction.None || decision.Manifest is null)
            {
                _logger.LogDebug("[Update] 새 버전 없음 또는 적용 대상 아님(current={V})", currentVersion);
                return;
            }

            var m = decision.Manifest;
            switch (decision.Action)
            {
                case UpdateAction.ApplyAtNight: // Normal
                    if (_update.IsNightWindow(DateTime.Now))
                    {
                        _logger.LogInformation("[Update] Normal 채널 — 야간 자동 적용 진입: {V}", m.Version);
                        await _update.ApplyUpdateAsync(m, ct);
                    }
                    else
                    {
                        // 야간 창이 아니면 manifest 를 들고만 있다가(_pendingNightUpdate) 야간 창에 적용한다.
                        //   확인 게이트는 그대로 둬서 feed 를 매 루프 재조회하지 않는다(주기 게이트 정신 유지).
                        //   ※ 20260807작2 N-10: 종전 "날짜 게이트 … 하루 1회 정신" → N시간 주기 게이트.
                        _pendingNightUpdate = m;
                        // 20260809작2 P0-3 관측: 이 펜딩이 **언제부터** 대기 중인지 기억한다.
                        //   같은 버전을 계속 대기 중이면 시작 시각을 유지한다(매 루프 리셋하면 경과를 못 센다).
                        _pendingNightSinceUtc ??= DateTime.UtcNow;
                        _logger.LogDebug("[Update] Normal 채널 새 버전({V}) — 야간(새벽 3시대) 자동 적용 대기(보류)", m.Version);
                    }
                    break;

                case UpdateAction.AnnounceThenApply: // Emergency
                    _logger.LogWarning("[Update] Emergency 채널 — 안내 후 적용 진입: {V} (안내 지연 {Delay})",
                        m.Version, decision.AnnounceDelay);
                    await _update.ApplyUpdateAsync(m, ct);
                    break;

                case UpdateAction.RequireConsent: // Major
                    // 고리2(ERP 로그인 Y/N 동의)가 동의 전달 다리를 통해 트리거한다. 워치독은 적용을 시작하지 않는다.
                    //   manifest 를 들고 있다가(_pendingConsentUpdate) 다음 메타 ping 에 버전·채널·안내 문구를 실어 본사에 알린다.
                    _pendingConsentUpdate = m;

                    // 봉합 (2026-06-29, 작1 고리2 마지막 빈 칸, A안 헌법 #30): 발견한 Major 새버전을 로컬 DB
                    //   (local_update_status, DB-83)에 적재한다. ERP 가 로그인 시 그 행을 읽어 UpdateAvailable=true
                    //   로 Y/N 동의 팝업을 노출한다(본사 의존 0). 로그인 팝업 동의 대상은 Major 위주이므로 Major 만 적재.
                    //   적재 실패(false)는 워치독 루프를 멈추지 않는다(라이터가 로그만 남기고 false 반환, 헌법 #15).
                    await _statusWriter.UpsertLatestAsync(m, ct);

                    _logger.LogInformation("[Update] Major 채널 새 버전({V}) — ERP 로그인 동의 대기(고리2 처리). 로컬 적재 완료. 워치독 자동 적용 안 함. 메타 ping 으로 본사 통지 예약.", m.Version);
                    break;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // 헌법 #15: 업데이트 평가 실패도 침묵 금지. 다음 루프에서 즉시 재평가하도록 게이트를 풀어둔다.
            //   ※ 20260807작2 N-10 — 이 해제 지점은 **존치한다**(헌법 #1, 결재-4). 표현만 사실에 맞췄다:
            //     종전 "다음날 재평가" → 이제는 N시간을 기다리지 않고 **즉시 만료**된다(다음 60초 루프에서 재시도).
            //   ⚠️ 디스크 기록(update-check.stamp)은 일부러 건드리지 않는다. 예외 상황에서 그것까지 지우면
            //     크래시 루프 PC 가 기동마다 조회를 되풀이해 재시작 폭주 상한이 무너진다 — 이 안의 존재 이유가
            //     그 상한이다. 인메모리만 풀어 "이 프로세스가 사는 동안 즉시 재시도" 로 한정한다.
            _lastUpdateCheckUtc = null;
            _logger.LogWarning(ex, "[Update] 업데이트 평가 중 예외 — 확인 게이트를 즉시 만료시켜 다음 루프에서 재시도합니다.");
        }
    }

    /// <summary>
    /// 작1 고리2 워치독 측 — 펜딩 Major 업데이트의 로컬 동의를 읽어 적용 가부를 판단한다(A안, 헌법 #30).
    ///   approve  → **즉시** ApplyUpdateAsync 진입(백업→차단→고리4 교체·재시작·검증·롤백).
    ///              ※ 20260806작1: 종전엔 영업시간이면 미뤘으나, 고객이 직접 [예] 를 누른 건을
    ///                아무 안내 없이 8시간 미루는 것은 "동작하지 않는 기능"이었다(사장님 지적).
    ///   reject   → 적용 안 함. 펜딩 폐기(다음 로그인 재제시는 ERP 몫 — 새 동의가 들어오면 manifest 재발견 시 재펜딩).
    ///   None     → 미응답. 펜딩 유지(다음 루프 재조회).
    ///   Error    → 조회 실패. 펜딩 유지(보수적, 다음 루프 재시도).
    /// 멱등: 이미 적용을 시도한 버전(_consentAppliedVersions)은 다시 적용하지 않는다.
    /// ⬛ 위 「멱등」 줄은 낡았다 — 20260929작3 규칙 C(설계 §3)로 바뀌었다:
    ///   「[예] 한 번 = 시도 한 번」. 최신 동의가 approve 이고 그 id 가 결과행이 이미 쓴 id 보다 크면 **새 [예]** —
    ///   ① in_progress 행(consent_id=그 id)을 **먼저** 기록 ② 기록 성공했을 때만 적용 ③ 기록 실패면 적용 안 함·펜딩 유지.
    ///   id 가 이미 쓴 id 이하(또는 이 수명에서 이미 쓴 id)면 **이미 쓴 [예]** — 적용 안 함 · 펜딩 해제 · 보고 0
    ///   (ERP 가 「업데이트가 정상적으로 이루어지지 않았습니다」로 다시 묻는다 · 사장님 Q-1 「응 다시 물어봐」).
    ///   Reject·None·Error 분기는 **종전 그대로**다(#43 [나중에]=적용 안 함 불변 · R-1 보고 수 불변).
    /// </summary>
    internal async Task ConsumeConsentForMajorAsync(UpdateManifest m, CancellationToken ct)
    {
        // ⬛ 20260929작3 절W4 — 종전 「이 버전을 이미 시도했으면 조회 없이 펜딩 해제」 멱등 블록은 뺐다(수정).
        //   버전 키로 막으면 새 [예](N-UPD2)가 조회조차 안 된다. 멱등은 이제 판독 뒤 **동의 id** 로 건다(아래).
        //   종전 조회 ReadLatestAsync 는 리더에 남아 있다(#1) — 규칙 C 는 id·used 까지 읽는 새 판독을 쓴다.
        _pendingHeldByUsedApprove = false;   // 갈래 X 보완 — 「이미 쓴 [예]」 분기에서만 true
        var usage = await ReadConsentUsageSeam(m.Version, ct);
        var decision = usage.Decision;

        // 20260929작3 갈래 R 규칙 Z-1 — 시도 표를 못 읽었다(동의 표만 읽힘). 같은 동의 id 로 3루프까지만 보류.
        var oldRuleFallback = false;
        if (decision == ConsentDecision.Approve && usage.AttemptsUnknown)
        {
            if (!AttemptHoldExceeded(usage.ConsentId, m.Version, "시도 표 판독 실패"))
                return;   // 보류 — 펜딩 유지 · 다음 루프 재판독
            oldRuleFallback = true;
        }

        // 규칙 C — 최신이 approve 인데 새 [예]가 아니면 「이미 쓴 [예]」: 적용 안 함 · 펜딩 해제 · 보고 0.
        //   Z-1 내려앉음(옛 규칙)에서는 인메모리 동의 id 집합만 본다.
        if (decision == ConsentDecision.Approve
            && ((!oldRuleFallback && !usage.IsFreshApprove) || _consentAppliedVersions.Contains(usage.ConsentId)))
        {
            // 20260929작3 갈래 X · [4] F-2 — 이 줄은 펜딩을 유지하므로 매 루프(60초) 온다. 같은 동의 id 는 처음 한 번만 Information,
            //   그 뒤는 Debug — 고객 PC 기본 레벨에서 하루 1,440줄이 쌓이지 않게 한다(N-10 교훈).
            if (_usedApproveNoticeConsentId != usage.ConsentId)
            {
                _usedApproveNoticeConsentId = usage.ConsentId;
                _logger.LogInformation("[Update] Major 버전 {V} — 최신 [예](동의 {Id})는 이미 한 번 시도한 동의입니다(이미 쓴 동의 {Used}). " +
                                       "다시 적용하지 않습니다 — ERP 가 다시 물어 새 [예]가 들어오면 그때 한 번 더 시도합니다(펜딩 유지 · 본사 feed 재조회 없음).",
                    m.Version, usage.ConsentId, usage.UsedConsentId);
            }
            else
            {
                _logger.LogDebug("[Update] Major 버전 {V} — 이미 쓴 [예](동의 {Id}) 그대로 · 새 [예] 대기(펜딩 유지).", m.Version, usage.ConsentId);
            }
            // ⬛ 20260929작3 갈래 X · [4] F-2 봉합(PM 결재) — 종전 이 자리의
            //     _pendingConsentUpdate = null;
            //   은 뺐다(수정). 펜딩을 풀면 호출부(EvaluateUpdateOncePerDayAsync)가 확인 게이트를 즉시 만료시켜
            //   다음 루프에 본사 feed 를 다시 받고 → 재펜딩 → 다시 「이미 쓴 [예]」… 고객이 답할 때까지 약 2분마다
            //   feed 조회가 무기한 이어졌다(NCP 부하 · N-10 상한 취지와 충돌).
            //   ⇒ 펜딩을 유지한다. 다음 루프는 로컬 DB 동의만 다시 읽고(feed 0), 새 [예]가 들어오면 그 루프에서
            //     아래 Approve 로 적용에 진입한다(규칙 C 그대로 — 새 [예] 한 번 = 시도 한 번).
            //   ⚠️ 대가: 펜딩이 선 동안엔 feed 를 안 읽으므로 그 사이 더 새 버전이 게시돼도 발견하지 않는다 —
            //     None(미응답) 분기와 같은 성질이다. 고객이 [예]·[나중에] 로 답하면 풀린다.
            //   ⬛ 위 「대가」는 PM 반려(9/30)로 **이 분기에서는 없앴다** — 아래 표식으로 호출부가 정규 확인 주기
            //     (N시간 게이트 · 기본 60분)는 그대로 돌리고, 더 새 버전이 오면 펜딩을 그 버전으로 바꾼다.
            _pendingHeldByUsedApprove = true;
            return;
        }

        switch (decision)
        {
            case ConsentDecision.Approve:
                // ★★ 봉합 20260806작1 (사장님 오더) — 영업시간 보류를 제거한다 ★★
                //   ■ 사장님 지적
                //     "팝업이 뜨는게 중요한게 아니라 뜬 팝업으로 옵션을 선택할때, 제대로 동작하느냐가 중요한거지."
                //   ■ 무엇이 문제였나 (2026-08-06 실측)
                //     종전엔 여기서 IsBusinessHour 면 return 했다. 실측: 목요일 10:57 에 [예] 를 눌렀더니
                //     동의는 기록됐는데(local_update_consents id=1, approve) **ERP 는 1.2.52 그대로**였다.
                //     화면에 진행 표시도 안내도 없다 — 팝업만 사라진다. 고객은 고장으로 인식한다.
                //     즉 이 기능은 "표시"였고 "동작"이 아니었다.
                //   ■ 왜 제거가 맞는가
                //     영업시간 보호는 **묻지 않고 바꾸는 자동 적용(Normal)** 에 필요한 규칙이다.
                //     Major 는 고객이 "지금 진행하시겠습니까?" 에 **직접 [예] 라고 답한 건**이다.
                //     본인이 명시적으로 선택한 행위를 8시간 미루면서 아무 고지도 하지 않는 것은
                //     헌법 #24(가르치지 않고 넘기는 건 거짓말)·#25(쉽게) 양쪽에 어긋난다.
                //     팝업이 "지금 진행" 이라고 물었으면 지금 해야 한다 — 문구와 동작이 일치해야 한다.
                //   ■ 범위: 이 분기는 ConsentDecision.Approve 전용이다. [나중에]는 Reject 로,
                //     자동 적용(Emergency/Normal)은 이 함수를 타지 않는다(RunOneLoop 의 별도 분기).
                //   ⚠️ IsBusinessHour 함수 자체는 **삭제하지 않는다**(헌법 #1 — 수정은 OK, 제거는 금지).
                //     ※ 정정(헌법 #32): 작성 중 "Normal 채널이 계속 쓴다"고 적었으나 **사실이 아니다.**
                //       grep 실측 결과 이 봉합 이후 IsBusinessHour 의 실제 호출처는 **0건**이고,
                //       Normal 은 IsNightWindow(:308·:344)를 쓴다. 그래도 함수를 남기는 이유는
                //       ① 향후 '업무시간 중 자동작업 자제' 판정에 재사용 여지가 있고
                //       ② 헌법 #1 이 제거를 금지하기 때문이다. 호출처 0건임을 알고 남긴다.

                // 20260929작3 규칙 C ① — 이 [예]를 「썼다」는 기록(in_progress · consent_id)을 **적용 전에** 남긴다.
                //   다운로드 실패·슬롯 실패·예외·전원 종료가 나도 「이 [예]는 썼다」가 DB 에 남아
                //   재기동 뒤 묻지 않고 옛 [예]로 다시 시도하는 일(B-3)이 원리적으로 사라진다(설계 §3).
                //   ③ 기록에 실패하면 적용하지 않고 펜딩을 유지한다(다음 루프에 다시) — 기록 없이 적용하면
                //     재기동 뒤 같은 [예]로 또 시도하게 된다.
                // ⬛ 갈래 R — 기록 자리 apply_status → 시도 표 INSERT(UPSERT 아님 · 설계 §12-1).
                //   AlreadyUsed(UNIQUE 위반) = 그 [예]는 이미 시도했다 → 적용 안 함 · 펜딩 해제(G-R7).
                //   Failed = 기록 실패 → 규칙 Z-1: 같은 동의 id 로 3루프까지 보류, 넘으면 옛 규칙으로 적용 + LogWarning.
                var opened = oldRuleFallback
                    ? AttemptOpenResult.Failed
                    : await OpenAttemptSeam(usage.ConsentId, m.Version, ct);
                if (opened == AttemptOpenResult.AlreadyUsed)
                {
                    _logger.LogInformation("[Update] Major 버전 {V} — 동의 {Id} 는 시도 표에 이미 있습니다. 다시 적용하지 않습니다.", m.Version, usage.ConsentId);
                    _consentAppliedVersions.Add(usage.ConsentId);
                    _pendingConsentUpdate = null;
                    break;
                }
                if (opened == AttemptOpenResult.Failed && !oldRuleFallback
                    && !AttemptHoldExceeded(usage.ConsentId, m.Version, "시도 행 기록 실패"))
                {
                    _logger.LogWarning("[Update] Major 버전 {V} — 적용 시작 기록(in_progress · 동의 {Id})에 실패해 이번 루프에는 적용하지 않습니다. 펜딩 유지 — 다음 루프에 다시 시도합니다.",
                        m.Version, usage.ConsentId);
                    break;
                }
                ResetAttemptHold();

                // 멱등 기록을 먼저 남긴 뒤 적용 — 적용 중 예외가 나도 같은 버전을 무한 재시도하지 않게 한다.
                // ⬛ 20260929작3 — 「같은 버전」 → 「같은 동의 id」(키 변경 · 위 필드 주석).
                _consentAppliedVersions.Add(usage.ConsentId);
                _pendingConsentUpdate = null;
                _logger.LogInformation("[Update] Major 버전 {V} 동의 확인 — 즉시 적용 진입(백업→차단→교체→재기동). 고객이 [예] 를 눌렀으므로 영업시간을 이유로 미루지 않는다.", m.Version);
                try { await ApplyUpdateSeam(m, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Update] Major 동의 적용 중 예외 — 버전 {V}", m.Version);
                    // 20260929작3 절W5 — 행 안 남던 종점(설계 §5-3). 예외면 in_progress 가 그대로 남아
                    //   ERP 가 30분 동안 「진행 중」으로 보였다. failed 로 닫는다(consent_id 는 안 건드림 — 쓴 [예] 유지).
                    //   detail 은 「업데이트 중 중단」으로 시작하지 않는다(설계 §7 「그 밖」 매핑).
                    //   ⬛ 갈래 R — 시도 행만 닫는다(진행 중 행만 · 오케스트레이터 종점이 이미 닫았으면 무변경 · [4] F-3).
                    await CloseAttemptSeam(m.Version, "failed", $"적용 중 예외 — {ex.GetType().Name}", ct);
                }
                break;

            case ConsentDecision.Reject:
                _logger.LogInformation("[Update] Major 버전 {V} 거부 — 적용 안 함, 펜딩 해제(재제시는 ERP 몫)", m.Version);
                // 20260806작4 (사장님 오더 ③ 3시점 중 ③) — 거부도 본사에 알린다.
                //   [3-V] 적발: 종전엔 이 경로가 빠져 CS 가 "왜 계속 구버전인가"를 알 수 없었다.
                //   기다리지 않는다(내부에서 fire-and-forget). 거부는 정상 동작이라 아무것도 강제하지 않는다.
                // 20260929작3 — 호출만 대리자 한 겹을 거친다(운영 = 같은 메서드 · 동작·빈도 불변 G-W7).
                ReportConsentRejectedSeam(m);
                _pendingConsentUpdate = null;
                break;

            case ConsentDecision.None:
                _logger.LogDebug("[Update] Major 버전 {V} 동의 미응답 — 펜딩 유지(다음 루프 재조회)", m.Version);
                break;

            case ConsentDecision.Error:
                _logger.LogDebug("[Update] Major 버전 {V} 동의 조회 실패 — 펜딩 유지(다음 루프 재시도)", m.Version);
                break;
        }
    }

    /// <summary>
    /// 20260929작3 갈래 R 규칙 Z-1 — 시도 표 때문에 보류할 때 부른다. 같은 동의 id 연속 횟수를 센다.
    ///   반환 false = 아직 보류(1~3루프) · true = 상한 초과 → 옛 규칙으로 내려앉는다(LogWarning).
    /// </summary>
    private bool AttemptHoldExceeded(long consentId, string version, string why)
    {
        if (_attemptHoldConsentId != consentId)
        {
            _attemptHoldConsentId = consentId;
            _attemptHoldCount = 0;
        }
        _attemptHoldCount++;
        if (_attemptHoldCount <= AttemptHoldLimit)
        {
            _logger.LogWarning("[Update] Major 버전 {V} — {Why}로 적용 보류 {N}/{Max}루프(동의 {Id}). 펜딩 유지.",
                version, why, _attemptHoldCount, AttemptHoldLimit, consentId);
            return false;
        }
        _logger.LogWarning("[Update] Major 버전 {V} — {Why}가 {Max}루프를 넘었습니다(동의 {Id}). 시도 표 없이 종전 규칙(이 수명에서 처음 보는 동의 id)으로 적용합니다(규칙 Z-1).",
            version, why, AttemptHoldLimit, consentId);
        return true;
    }

    private void ResetAttemptHold()
    {
        _attemptHoldConsentId = 0;
        _attemptHoldCount = 0;
    }

    /// <summary>
    /// 20260929작3 절W6 — 워치독 기동 시 교체 중 중단 정리(설계 §5-4 · §6).
    ///   ① 폴더 복원(UpdateFolderRecovery — 교체 표식이 있을 때만 R1~R3 · [3-V] 병렬이슈 01)
    ///   ② in_progress 행 닫기 — 폴더를 되돌렸으면 rolled_back 「업데이트 중 중단 — 이전 버전으로 되돌림」,
    ///      아니면 failed 「업데이트 중 중단」. 기동 시점엔 적용이 돌 수 없으므로 남은 in_progress 는 전부 중단된 시도다.
    ///   어느 쪽이 실패해도 로그만 남기고 기동을 계속한다(헌법 #15·#20). 취소만 위로 던진다.
    /// </summary>
    internal async Task RecoverInterruptedUpdateAtStartupAsync(CancellationToken ct)
    {
        var restored = false;
        try
        {
            var result = UpdateFolderRecovery.RecoverAtStartup(AppRootSeam(), _logger);
            restored = result.Restored;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Update] 기동 시 폴더 복원 호출 실패 — 워치독 기동은 계속합니다.");
        }

        try
        {
            await CloseInterruptedAttemptsSeam(restored, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Update] 기동 시 중단된 시도 정리 호출 실패 — 워치독 기동은 계속합니다.");
        }
    }

    /// <summary>
    /// 현재 설치 버전 문자열. 워치독 어셈블리 버전을 단일출처로 쓴다(별도 버전 파일·db.conf 키 없음 확인 완료).
    /// Major.Minor.Build 3자리로 manifest version 과 동일 형식(예: "1.2.34")을 맞춘다.
    ///
    /// 정리 (2026-07-16, 작1 W4-0): 동일 로직이 VersionInfo 로 승격됐다(진단·메타핑이 같은 출처를 보게 하려고).
    ///   여기 사본을 남겨두면 언젠가 두 값이 갈라져 "업데이트 판정 버전 ≠ 본사 보고 버전"이 된다 — 위임한다.
    /// </summary>
    private static string GetCurrentVersion() => VersionInfo.Current;

    /// <summary>
    /// W4-6 정지공격 감지 — manifest '조회'가 며칠 연속 실패하면(feed 끊김·정지공격) 본사에 통지한다.
    ///   feed 가 끊기면 고객이 영영 옛 버전에 고정되는데, 그건 조용히 진행돼 아무도 모른다(그래서 통지).
    ///   같은 날 중복 집계를 막고(하루 1건), 조회가 한 번이라도 성공하면 카운터를 즉시 0으로 리셋한다.
    ///
    /// 🔴 20260807작2 N-10 §6 (결재-5) — **이 로직의 전제가 무효화됐다. 코드는 그대로 두고 사실만 기록한다.**
    ///   이 함수는 "조회를 하루 1번만 한다"는 전제 위에 설계됐다. 그 전제가 60분 주기로 바뀌었다.
    ///   · 카운터 폭주는 없다 — :496 의 날짜 중복방지가 그대로 살아 있어 하루 최대 +1 이다.
    ///   · 임계 7일도 그대로 유효하다.
    ///   · 🔴 그러나 :485-492 가 **1회만 성공하면 즉시 리셋**한다. 하루 24번 중 1번만 성공해도 0이 된다.
    ///     하루 23시간 feed 가 죽고 1시간만 살아나는 간헐 장애에서는, 고객이 실제로 옛 버전에 고정되면서도
    ///     카운터가 영원히 0 이라 **7일 경보가 절대 발화하지 않는다.** W4-6 이 잡으려던 바로 그 상태다.
    ///   ■ 왜 지금 안 고치나: 이것은 4안이 만든 새 결함이 아니라 4안이 **드러낸** 기존 전제의 붕괴이며,
    ///     즉시 터지지 않는다. 계수 축을 "날짜"에서 "연속 실패 횟수 + 최근 성공 시각"으로 바꾸는 것은
    ///     정지공격 감지 **정책의 재설계**이고 임계 7일의 의미도 다시 정해야 하는 사장님 결재 사안이다.
    ///     한 판에 섞으면 N-10 봉합의 검증 표면이 커져 사장님 3단계가 그만큼 늦어진다(설계서 §8-4).
    ///   ■ 후속 과제: 작2 §9-1 "W4-6 계수 전제 재설계". **기록 없이 넘기면 `4cc5842` 와 같은 반쪽 봉합이 된다.**
    /// </summary>
    private async Task TrackUpdateStallAsync(DateOnly today, CancellationToken ct)
    {
        if (!_update.LastFetchFailed)
        {
            // 조회 성공 = feed 정상. 그동안의 연속 실패를 푼다.
            if (_updateFetchFailStreakDays > 0)
                _logger.LogInformation("[Update] feed 조회 정상 복구 — 정지 감지 카운터 리셋(직전 연속 {N}일).", _updateFetchFailStreakDays);
            _updateFetchFailStreakDays = 0;
            _lastFetchFailCountedDate = null;
            return;
        }

        // 조회 실패. 같은 날 이미 셌으면 중복 집계 안 함(하루 1 게이트와 정합).
        if (_lastFetchFailCountedDate == today) return;
        _lastFetchFailCountedDate = today;
        _updateFetchFailStreakDays++;

        _logger.LogWarning("[Update] manifest 조회 실패 연속 {N}일 — feed 도달 불가(장애·정지공격 의심).", _updateFetchFailStreakDays);

        if (_updateFetchFailStreakDays >= UpdateStallNotifyDays)
        {
            // 헌법 #30: 본사는 통지만 수신. 업무데이터 0(reason·stage 문자열뿐).
            await _meta.NotifyEmergencyAsync("update_feed_stalled", $"days={_updateFetchFailStreakDays}", ct);
            _logger.LogError("[Update] 🛑 manifest 조회 {N}일 연속 실패 — 본사 긴급 통지(고객이 옛 버전 고정 위험).", _updateFetchFailStreakDays);
        }
    }

    /// <summary>
    /// WS-28-B: 재부팅 직후 통신 무결성 즉시 점검·복구(헌법 #28). 정기 루프를 기다리지 않고
    /// ① TunnelSecret 무효화 ② cloudflared 서비스 부재 ③ 외부 헬스체크를 강제 1회 점검,
    /// 깨진 항목은 CoolDown 게이트를 존중하며 즉시 봉합한다. 5분 내 자가회복 보장(헌법 #27·#30).
    /// </summary>
    private async Task RunPostRebootRecoveryAsync(CancellationToken ct)
    {
        try
        {
            // ① TunnelSecret 무효화 감지·재생성
            var secretInvalid = _c.DetectInvalidSecret();
            if (secretInvalid && _f.AllowRecovery("PostReboot:TunnelSecret"))
            {
                if (await _c.RegenerateAsync(ct))
                {
                    MarkRecovery("WS-28-B→C");
                    _logger.LogWarning("WS-28-B: 재부팅 후 TunnelSecret 재생성 완료");
                }
            }

            // ② cloudflared 서비스 부재 감지·재설치
            //   봉합 (2026-06-21, D6-P0-02-FIX, 설계팀장 P1): 정기 루프와 동일 — 관리형 터널이면서 secret
            //   무효화 감지 시 서비스 생존 여부와 무관하게 D(토큰 재설치) 강제. 종전 !ServiceExists 단독 게이트는
            //   재부팅 후 secret 만 무효화되고 서비스는 살아난 관리형 케이스에서 토큰 재설치를 건너뛰었다.
            var needsManagedReinstall = secretInvalid && _c.IsManagedTunnel();
            if ((!_d.ServiceExists("cloudflared") || needsManagedReinstall) && _f.AllowRecovery("PostReboot:ServiceReinstall"))
            {
                if (await _d.ReinstallAsync(ct))
                {
                    MarkRecovery("WS-28-B→D");
                    _logger.LogWarning("WS-28-B: 재부팅 후 cloudflared 서비스 재설치 완료");
                }
            }

            // ③ 외부 헬스체크 — 여전히 다운이면 본사에 비상 통지(헌법 #30: 본사는 통지만 수신)
            var healthy = await _e.PingAsync(ct);
            if (!healthy)
            {
                _logger.LogWarning("WS-28-B: 재부팅 후에도 외부 헬스체크 실패 — 비상 통지");
                await _meta.NotifyEmergencyAsync("post_reboot_health_fail", "WS-28-B", ct);
            }
            else
            {
                _logger.LogInformation("WS-28-B: 재부팅 후 통신 무결성 정상 확인");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // 헌법 #15: 재부팅 복구 실패도 침묵 금지. 정기 루프가 이어서 재시도한다.
            _logger.LogError(ex, "WS-28-B: 재부팅 후 복구 중 예외 — 정기 루프에서 재시도");
        }
    }

    private async Task SendMetaPingAsync(CancellationToken ct)
    {
        var procStatus = OperatingSystem.IsWindows() ? await _i.CheckAllAsync(ct) : new();
        var recoveryCount = _f.RecentRecoveryCount;
        var status = procStatus.All(kv => kv.Value)
            ? "healthy"
            : recoveryCount > 0 ? "recovering" : "down";

        var payload = new MetaPingPayload
        {
            TenantIdHash = MetaPingClient.Sha256(MetaPingClient.GetTenantId()),
            Timestamp = DateTime.UtcNow,
            Status = status,
            RecentRecoveryCount = recoveryCount,
            // 봉합 (2026-07-16, 작1 W4-0 — 사장님 결재): 종전 "1.0.0" 하드코딩.
            //   본사가 보는 고객사 버전이 이 값인데, 설치본(1.2.xx)과 무관하게 늘 1.0.0 이라
            //   본사는 어느 고객사가 어느 버전인지 알 수 없었고 업데이트 성공률 집계가 불가능했다.
            WatchdogVersion = VersionInfo.Current,
            ProcessStatus = procStatus,
            LastRecovery = new LastRecoveryInfo
            {
                Stage = _lastRecoveryStage,
                Timestamp = _lastRecoveryAt
            }
        };

        // 봉합 (2026-06-29, 작1 고리2): 동의 필요(Major) 펜딩 업데이트가 있으면 본사에 함께 알린다.
        //   펜딩 없으면 세 필드 모두 null 로 남아 역호환(구버전 본사 API 는 무시).
        if (_pendingConsentUpdate is { } consent)
        {
            payload.LatestVersion = consent.Version;
            payload.UpdateChannel = consent.Channel.ToString();
            payload.ConsentMessage = consent.ConsentMessage;
        }

        await _meta.SendAsync(payload, ct);
    }

    private void MarkRecovery(string stage)
    {
        _lastRecoveryStage = stage;
        _lastRecoveryAt = DateTime.UtcNow;
    }
}
