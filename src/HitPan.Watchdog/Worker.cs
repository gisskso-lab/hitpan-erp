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

    // 봉합 (2026-06-29, 작1 고리1): 업데이트 확인은 하루 1회로 게이트한다(매 루프 60초마다 manifest 조회 방지).
    //   마지막 확인 날짜(로컬 날짜)를 기억해, 날짜가 바뀐 첫 루프에서만 manifest 를 조회한다.
    private DateOnly? _lastUpdateCheckDate;

    // Normal 채널 새 버전을 낮에 발견하면, 야간 창(새벽 3시대)이 올 때까지 manifest 재조회 없이 들고 있다가 적용한다
    //   (feed 를 매 루프 두드리지 않으려는 보호 — 하루 1회 게이트 정신 유지).
    private UpdateManifest? _pendingNightUpdate;

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
    private readonly HashSet<string> _consentAppliedVersions = new(StringComparer.OrdinalIgnoreCase);

    private readonly WatchdogConsentReader _consent;

    // 봉합 (2026-06-29, 작1 고리2 마지막 빈 칸): 발견한 Major 새버전을 로컬 DB(local_update_status, DB-83)에
    //   적재하는 라이터. ERP 가 로그인 시 그 행을 읽어 Y/N 동의 팝업을 노출한다(A안, 헌법 #30 본사 의존 0).
    private readonly WatchdogStatusWriter _statusWriter;

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
        WatchdogStatusWriter statusWriter)
    {
        _logger = logger;
        _options = options.Value;
        _a = a; _b = b; _c = c; _d = d; _e = e; _f = f; _i = i;
        _meta = meta;
        _update = update;
        _consent = consent;
        _statusWriter = statusWriter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HitPan Watchdog started v1.0.0");

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

        // 봉합 (2026-06-29, 작1 고리1): 버전 업데이트 평가(하루 1회 게이트). 통신 무결성 단계 뒤에 둔다
        //   — 통신이 안 되면 manifest 조회 자체가 무의미하기 때문(헌법 #27 통신 무결성 우선).
        await EvaluateUpdateOncePerDayAsync(ct);
    }

    /// <summary>
    /// 작1 고리1 — 하루 1회 게이트로 버전 업데이트를 평가하고 채널별로 분기한다.
    ///   Normal    = 야간 자동(새벽 3시대, IsNightWindow)에만 적용 진입(고리3 백업→차단까지).
    ///   Emergency = 안내 후 적용(시간 무관, 적용 진입).
    ///   Major     = ERP 로그인 시 동의 대기(고리2 담당) — 워치독은 적용을 시작하지 않고 로그만 남긴다.
    /// 실제 EXE 교체·재시작·마이그(고리4)는 미구현 — ApplyUpdateAsync 가 백업까지만 수행한다.
    /// </summary>
    private async Task EvaluateUpdateOncePerDayAsync(CancellationToken ct)
    {
        // 봉합 (2026-06-29, 작1 고리2 워치독 측): Major(동의 필요) 펜딩이 있으면 매 루프 로컬 동의를 읽어
        //   적용 가부를 판단한다. 동의는 ERP 에서 아무 때나 들어올 수 있으므로 하루 1회 게이트와 분리해
        //   _pendingNightUpdate 와 동일한 매 루프 폴링 패턴으로 처리한다(feed 재조회 없음 — 로컬 DB 만 읽음).
        if (_pendingConsentUpdate is { } consentPending)
        {
            await ConsumeConsentForMajorAsync(consentPending, ct);
            // 동의 처리(승인 적용 진입/거부 폐기) 후엔 펜딩이 비워졌을 수 있다. 다른 채널 평가와 섞지 않고 반환.
            return;
        }

        // 낮에 보류해 둔 Normal 업데이트가 있으면, 야간 창에 진입했을 때 feed 재조회 없이 적용한다.
        if (_pendingNightUpdate is { } pending && _update.IsNightWindow(DateTime.Now))
        {
            var toApply = _pendingNightUpdate;
            _pendingNightUpdate = null;
            _logger.LogInformation("[Update] Normal 채널 — 야간 창 진입, 보류분 자동 적용: {V}", toApply.Version);
            try { await _update.ApplyUpdateAsync(toApply, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogWarning(ex, "[Update] Normal 보류분 적용 중 예외"); }
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_lastUpdateCheckDate == today)
            return;

        try
        {
            var currentVersion = GetCurrentVersion();
            var decision = await _update.EvaluateAsync(currentVersion, ct);

            // 평가 자체는 하루 1회만(성공·새버전없음 모두). manifest 조회 실패는 다음날 재시도.
            _lastUpdateCheckDate = today;

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
                        //   날짜 게이트는 그대로 둬서 feed 를 매 루프 재조회하지 않는다(하루 1회 정신 유지).
                        _pendingNightUpdate = m;
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
            // 헌법 #15: 업데이트 평가 실패도 침묵 금지. 다음날 재평가하도록 게이트를 풀어둔다.
            _lastUpdateCheckDate = null;
            _logger.LogWarning(ex, "[Update] 업데이트 평가 중 예외 — 다음 주기 재시도");
        }
    }

    /// <summary>
    /// 작1 고리2 워치독 측 — 펜딩 Major 업데이트의 로컬 동의를 읽어 적용 가부를 판단한다(A안, 헌법 #30).
    ///   approve  → 영업시간 외에 ApplyUpdateAsync 진입(백업→차단→고리4 자리). 영업시간이면 미루기(다음 루프 재판단).
    ///   reject   → 적용 안 함. 펜딩 폐기(다음 로그인 재제시는 ERP 몫 — 새 동의가 들어오면 manifest 재발견 시 재펜딩).
    ///   None     → 미응답. 펜딩 유지(다음 루프 재조회).
    ///   Error    → 조회 실패. 펜딩 유지(보수적, 다음 루프 재시도).
    /// 멱등: 이미 적용을 시도한 버전(_consentAppliedVersions)은 다시 적용하지 않는다.
    /// </summary>
    private async Task ConsumeConsentForMajorAsync(UpdateManifest m, CancellationToken ct)
    {
        // 멱등 — 이미 이 버전 적용을 시도했으면 펜딩만 비우고 끝(중복 백업·적용 차단).
        if (_consentAppliedVersions.Contains(m.Version))
        {
            _logger.LogDebug("[Update] Major 버전 {V} 은 이미 적용 시도됨 — 펜딩 해제(멱등)", m.Version);
            _pendingConsentUpdate = null;
            return;
        }

        var decision = await _consent.ReadLatestAsync(m.Version, ct);
        switch (decision)
        {
            case ConsentDecision.Approve:
                // Major 는 동의했어도 영업시간엔 미룬다(기존 IsBusinessHour 게이트 활용 — 업무 방해 0).
                if (_update.IsBusinessHour(DateTime.Now))
                {
                    _logger.LogInformation("[Update] Major 버전 {V} 동의 확인 — 영업시간이라 적용 보류(영업시간 외 자동 적용)", m.Version);
                    return; // 펜딩 유지, 다음 루프 영업시간 외에 재판단
                }

                // 멱등 기록을 먼저 남긴 뒤 적용 — 적용 중 예외가 나도 같은 버전을 무한 재시도하지 않게 한다.
                _consentAppliedVersions.Add(m.Version);
                _pendingConsentUpdate = null;
                _logger.LogInformation("[Update] Major 버전 {V} 동의 확인(영업시간 외) — 적용 진입(백업→...)", m.Version);
                try { await _update.ApplyUpdateAsync(m, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _logger.LogWarning(ex, "[Update] Major 동의 적용 중 예외 — 버전 {V}", m.Version); }
                break;

            case ConsentDecision.Reject:
                _logger.LogInformation("[Update] Major 버전 {V} 거부 — 적용 안 함, 펜딩 해제(재제시는 ERP 몫)", m.Version);
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
    /// 현재 설치 버전 문자열. 워치독 어셈블리 버전을 단일출처로 쓴다(별도 버전 파일·db.conf 키 없음 확인 완료).
    /// Major.Minor.Build 3자리로 manifest version 과 동일 형식(예: "1.2.34")을 맞춘다.
    ///
    /// 정리 (2026-07-16, 작1 W4-0): 동일 로직이 VersionInfo 로 승격됐다(진단·메타핑이 같은 출처를 보게 하려고).
    ///   여기 사본을 남겨두면 언젠가 두 값이 갈라져 "업데이트 판정 버전 ≠ 본사 보고 버전"이 된다 — 위임한다.
    /// </summary>
    private static string GetCurrentVersion() => VersionInfo.Current;

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
