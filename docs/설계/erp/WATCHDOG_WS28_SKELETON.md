# 워치독 WS-28-A~I 9단계 — C# Windows Service 의사코드 골격

> 헌법 #27·#28·#30 정합. 본 문서는 **설계 골격**이며, 실제 구현은 사장님 코드 동결 해제 + 인프라 사전 승인(헌법 #29) 후 발진.

---

## 0. 프로젝트 구조 (제안)

```
src/HitPan.Watchdog/                  # 새 .NET 8 Worker Service 프로젝트
├── HitPan.Watchdog.csproj           # <OutputType>Exe</OutputType> + Microsoft.Extensions.Hosting.WindowsServices
├── Program.cs                        # Host 부트스트랩 (Service 등록)
├── Worker.cs                         # 1분 주기 메인 루프
├── Stages/
│   ├── WS28A_WindowsUpdate.cs       # TrustedInstaller 1074 감지
│   ├── WS28B_PostRebootCheck.cs     # 재부팅 후 5분 점검
│   ├── WS28C_TunnelSecret.cs        # Invalid tunnel secret 감지 + 재발급
│   ├── WS28D_ServiceReinstall.cs    # cloudflared Service 자동 재설치
│   ├── WS28E_ExternalHealthCheck.cs # /health 1분×3회 봉합
│   ├── WS28F_CoolDown.cs            # 5회/시간 제한 + 본사 알림
│   ├── WS28G_EventLog.cs            # Windows Event Log + Telemetry
│   ├── WS28H_Guardian.cs            # 2층 (작업스케줄러 5분 주기)
│   └── WS28I_FourProcess.cs         # MariaDB·API·Web·cloudflared 4개 감시
├── Telemetry/
│   ├── MetaPingClient.cs            # 본사 메타 ping (TLS 1.3, 업무 데이터 0)
│   └── MetaPingPayload.cs           # JSON 스키마
└── appsettings.json                  # 헬스 URL·주기·cool down 임계값
```

---

## 1. Program.cs — Worker Service 부트

```csharp
using HitPan.Watchdog;
using HitPan.Watchdog.Stages;
using HitPan.Watchdog.Telemetry;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(opts =>
{
    opts.ServiceName = "HitPanWatchdog";
});

// Polly Resilience
builder.Services.AddSingleton<IAsyncPolicy>(_ =>
    Policy.Handle<Exception>()
        .WaitAndRetryAsync(3, retry => TimeSpan.FromSeconds(Math.Pow(2, retry))));

// Stage 등록
builder.Services.AddSingleton<WS28A_WindowsUpdate>();
builder.Services.AddSingleton<WS28B_PostRebootCheck>();
builder.Services.AddSingleton<WS28C_TunnelSecret>();
builder.Services.AddSingleton<WS28D_ServiceReinstall>();
builder.Services.AddSingleton<WS28E_ExternalHealthCheck>();
builder.Services.AddSingleton<WS28F_CoolDown>();
builder.Services.AddSingleton<WS28G_EventLog>();
builder.Services.AddSingleton<WS28I_FourProcess>();

builder.Services.AddSingleton<MetaPingClient>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
```

---

## 2. Worker.cs — 1분 메인 루프

```csharp
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly WS28A_WindowsUpdate _a;
    private readonly WS28B_PostRebootCheck _b;
    private readonly WS28C_TunnelSecret _c;
    private readonly WS28D_ServiceReinstall _d;
    private readonly WS28E_ExternalHealthCheck _e;
    private readonly WS28F_CoolDown _f;
    private readonly WS28I_FourProcess _i;
    private readonly MetaPingClient _meta;

    public Worker(ILogger<Worker> logger, /* DI 9개 */ ...)
    {
        _logger = logger;
        // ...
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. WS-28-I: 4 프로세스 헬스
                var procStatus = await _i.CheckAllAsync();

                // 2. WS-28-C: TunnelSecret 무효화 감지 (cloudflared 로그 tail)
                if (_c.DetectInvalidSecret())
                {
                    if (_f.AllowRecovery("TunnelSecret"))
                        await _c.RegenerateAsync();
                    else
                        await _meta.NotifyHQAsync("TunnelSecret cooldown exceeded");
                }

                // 3. WS-28-D: cloudflared Service 사라짐 감지
                if (!_d.ServiceExists())
                {
                    if (_f.AllowRecovery("ServiceReinstall"))
                        await _d.ReinstallAsync();
                }

                // 4. WS-28-E: 외부 헬스체크 (1분 × 3회 실패 → 봉합)
                var healthy = await _e.PingAsync("https://demo.hitpan.kr/health");
                if (!healthy)
                {
                    var failStreak = _e.IncrementFailure();
                    if (failStreak >= 3 && _f.AllowRecovery("FullRecovery"))
                        await _e.TriggerFullRecoveryAsync();
                }
                else
                {
                    _e.ResetFailure();
                }

                // 5. WS-28-A: TrustedInstaller 1074 감지 (재부팅 예고)
                if (_a.DetectImminentReboot())
                {
                    _b.MarkPostRebootCheck(); // 다음 부팅 시 자동 점검 플래그
                }

                // 6. 메타 ping (5분 주기 = 5루프마다)
                if (DateTime.UtcNow.Minute % 5 == 0)
                    await _meta.SendAsync(procStatus, _f.RecentRecoveryCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watchdog loop failure");
                // 빈 catch 금지 (헌법 #15)
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
```

---

## 3. WS-28-A — TrustedInstaller 1074 감지

```csharp
public class WS28A_WindowsUpdate
{
    public bool DetectImminentReboot()
    {
        // PowerShell 호출 또는 EventLog API 직접
        using var log = new EventLog("System");
        var recent = log.Entries
            .Cast<EventLogEntry>()
            .Where(e => e.InstanceId == 1074
                && e.Message.Contains("TrustedInstaller")
                && e.TimeGenerated > DateTime.Now.AddMinutes(-10))
            .Any();
        return recent;
    }
}
```

---

## 4. WS-28-C — TunnelSecret 자동 재발급

```csharp
public class WS28C_TunnelSecret
{
    private readonly string _logPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     ".cloudflared", "cloudflared.log");

    public bool DetectInvalidSecret()
    {
        if (!File.Exists(_logPath)) return false;
        // 최근 100줄 tail
        var tail = File.ReadLines(_logPath).TakeLast(100);
        return tail.Any(l => l.Contains("Invalid tunnel secret"));
    }

    public async Task RegenerateAsync()
    {
        var tunnelId = Environment.GetEnvironmentVariable("HITPAN_TUNNEL_ID")
                       ?? throw new InvalidOperationException("HITPAN_TUNNEL_ID missing");
        var credDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cloudflared");
        var credFile = Path.Combine(credDir, $"{tunnelId}.json");
        var backup = $"{credFile}.bak_{DateTime.Now:yyyyMMddHHmmss}";

        File.Move(credFile, backup, overwrite: true);

        var psi = new ProcessStartInfo("cloudflared", $"tunnel token --cred-file \"{credFile}\" {tunnelId}")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        using var p = Process.Start(psi)!;
        await p.WaitForExitAsync();

        // Restart-Service cloudflared
        using var sc = new ServiceController("cloudflared");
        if (sc.Status == ServiceControllerStatus.Running) sc.Stop();
        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
    }
}
```

---

## 5. WS-28-F — Cool Down + 본사 알림

```csharp
public class WS28F_CoolDown
{
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _history = new();
    private const int MaxPerHour = 5;
    public int RecentRecoveryCount => _history.Values.Sum(q => q.Count);

    public bool AllowRecovery(string key)
    {
        var q = _history.GetOrAdd(key, _ => new Queue<DateTime>());
        var cutoff = DateTime.UtcNow.AddHours(-1);
        while (q.Count > 0 && q.Peek() < cutoff) q.Dequeue();
        if (q.Count >= MaxPerHour) return false;
        q.Enqueue(DateTime.UtcNow);
        return true;
    }
}
```

---

## 6. WS-28-I — 4 프로세스 감시

```csharp
public class WS28I_FourProcess
{
    private static readonly string[] Services = { "MariaDB", "cloudflared" };
    private static readonly (string Name, int Port)[] HttpEndpoints =
    {
        ("HitPan.API", 5257),
        ("HitPan.Web", 5234),
    };

    public async Task<Dictionary<string, bool>> CheckAllAsync()
    {
        var result = new Dictionary<string, bool>();

        foreach (var svc in Services)
        {
            try
            {
                using var sc = new ServiceController(svc);
                result[svc] = sc.Status == ServiceControllerStatus.Running;
            }
            catch (Exception ex)
            {
                result[svc] = false;
                // log only — 헌법 #15
            }
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        foreach (var (name, port) in HttpEndpoints)
        {
            try
            {
                var r = await http.GetAsync($"http://127.0.0.1:{port}/health");
                result[name] = r.IsSuccessStatusCode;
            }
            catch { result[name] = false; }
        }

        return result;
    }
}
```

---

## 7. Telemetry — 본사 메타 ping (헌법 #22 정합)

```csharp
public class MetaPingPayload
{
    public string TenantIdHash { get; set; } = "";   // sha256
    public DateTime Timestamp { get; set; }
    public string Status { get; set; } = "healthy";  // healthy|recovering|down
    public int RecentRecoveryCount { get; set; }
    public string WatchdogVersion { get; set; } = "1.0.0";
    // 금지: tenant_id 원본, 직원명, 거래데이터, IP, 매출
}

public class MetaPingClient
{
    private readonly HttpClient _http;
    private const string Endpoint = "https://api.hitpan.kr/watchdog/ping";

    public MetaPingClient()
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls13
            }
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task SendAsync(Dictionary<string, bool> processStatus, int recoveryCount)
    {
        var payload = new MetaPingPayload
        {
            TenantIdHash = Sha256(GetTenantId()),
            Timestamp = DateTime.UtcNow,
            Status = processStatus.All(kv => kv.Value) ? "healthy"
                   : recoveryCount > 0 ? "recovering" : "down",
            RecentRecoveryCount = recoveryCount,
        };
        await _http.PostAsJsonAsync(Endpoint, payload);
    }

    public Task NotifyHQAsync(string reason) => /* 즉시 CS 알림 채널 */ Task.CompletedTask;

    private static string Sha256(string s)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return "sha256:" + Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s)));
    }

    private static string GetTenantId() =>
        Environment.GetEnvironmentVariable("HITPAN_TENANT_ID") ?? "unknown";
}
```

---

## 8. WS-28-H — 2층 Guardian (작업 스케줄러 XML)

`HitPanWatchdogGuardian.xml` — 5분 주기로 `HitPanWatchdog` 서비스 생사 확인. 죽었으면 `sc start HitPanWatchdog`. Service 자체 죽음 대비.

```xml
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <Triggers>
    <TimeTrigger>
      <Repetition><Interval>PT5M</Interval></Repetition>
      <StartBoundary>2026-05-30T00:00:00</StartBoundary>
    </TimeTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author"><RunLevel>HighestAvailable</RunLevel></Principal>
  </Principals>
  <Actions>
    <Exec>
      <Command>powershell.exe</Command>
      <Arguments>-NoProfile -ExecutionPolicy Bypass -File "C:\Program Files\HitPan\Watchdog\Guardian.ps1"</Arguments>
    </Exec>
  </Actions>
</Task>
```

`Guardian.ps1`:
```powershell
$svc = Get-Service -Name HitPanWatchdog -ErrorAction SilentlyContinue
if ($null -eq $svc -or $svc.Status -ne 'Running') {
    Start-Service HitPanWatchdog -ErrorAction SilentlyContinue
    Write-EventLog -LogName Application -Source HitPanWatchdog `
        -EntryType Warning -EventId 28008 -Message "Guardian restarted HitPanWatchdog"
}
```

---

## 9. 빌드·배포

- `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`
- Inno Setup에서 `HitPan.Watchdog.exe` Service 등록:
  ```
  Filename: "{app}\Watchdog\HitPan.Watchdog.exe"; Parameters: "install"; Flags: runhidden
  ```

---

## 10. 검증 (전수 시뮬레이션 — 헌법 #27 통신 무결성 게이트)

| 단계 | 시뮬레이션 | 자동 봉합 확인 |
|---|---|---|
| WS-28-A | `shutdown /r /t 60` | 재부팅 후 5분 안 4 서비스 기동 |
| WS-28-C | `tunnelId.json` 파일 변조 | 1분 안 cred 재발급 |
| WS-28-D | `sc delete cloudflared` | 2분 안 자동 재설치 |
| WS-28-E | hosts에 `demo.hitpan.kr → 0.0.0.0` | 3분 안 본사 알림 |
| WS-28-F | 6회 강제 실패 유발 | 5회 후 cool down + 본사 알림 |
| WS-28-H | `sc stop HitPanWatchdog` | 5분 안 Guardian 재기동 |
| WS-28-I | 4 서비스 각각 Stop | 각 1분 안 재기동 |

20개 시나리오 검증 스크립트는 `docs/설계/erp/SCENARIO_20_VERIFY_SKELETON.md` 별도 산출.

---

**문서 끝.** 다음: 시나리오 20 검증 스크립트 골격.
