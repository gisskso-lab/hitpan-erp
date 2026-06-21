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

    private string? _lastRecoveryStage;
    private DateTime? _lastRecoveryAt;

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
        MetaPingClient meta)
    {
        _logger = logger;
        _options = options.Value;
        _a = a; _b = b; _c = c; _d = d; _e = e; _f = f; _i = i;
        _meta = meta;
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
            WatchdogVersion = "1.0.0",
            ProcessStatus = procStatus,
            LastRecovery = new LastRecoveryInfo
            {
                Stage = _lastRecoveryStage,
                Timestamp = _lastRecoveryAt
            }
        };
        await _meta.SendAsync(payload, ct);
    }

    private void MarkRecovery(string stage)
    {
        _lastRecoveryStage = stage;
        _lastRecoveryAt = DateTime.UtcNow;
    }
}
