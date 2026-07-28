# WS-20260515-02 — cloudflared 하트비트 + 자동 복구 (헌법 #28 정공법)

> **발행:** 2026-05-15 PM 브라운킴 (자체 작성)
> **수행:** 5/16 백엔드 매니저 + 보안 매니저 2 (Defender 호환)
> **마감:** 5/19 23:59 (강제 시나리오 17건 검증 데드라인)
> **선행:** WS-20260515-01 워치독 데몬 코어 (HitPanWatchdog.exe Worker Service)
> **참조:** [헌법 #28 Windows Update 후 cloudflared 자동 복구](../../CLAUDE.md) + [project_communication_incident_20260515](../../memory/...)

---

## 1. 목적

5/15 새벽 사고 재발 방지의 정공법. Windows Update 강제 재부팅 → cloudflared 비정상 종료 → TunnelSecret 무효화 → 통신 다운 6시간 = 본 워치독으로 **5분 이내 자동 복구**.

## 2. 사고 박제 (2026-05-15)

| 시각 | 사건 |
|---|---|
| 02:30 | Windows Update 1차 강제 재부팅 (TrustedInstaller event 1074) |
| 02:34 | Windows Update 2차 강제 재부팅 |
| 02:35 | cloudflared 서비스 비정상 종료, TunnelSecret invalidate |
| 08:00 | 사장님 기상 → demo.hitpan.kr 502 확인 |
| 08:00~11:50 | 사장님 직접 봉합 (Cloudflare 대시보드 + 토큰 갱신 + 서비스 재설치) |
| 11:50 | demo 200 OK 복구 |

**다운타임 6시간** = SLA 99.99% (연 52분) 환산 시 7년치 소진. 본 워치독 정공법으로 다운타임 5분 이내 제약.

## 3. 자동 복구 5단계 (헌법 #28)

```
① TrustedInstaller 1074 이벤트 감지 (Windows Update 재부팅 예고)
   ↓
② 재부팅 후 5분 자동 점검 (워치독 부팅 직후 자가 진단)
   ↓
③ TunnelSecret 무효화 감지 (cloudflared.log "Invalid tunnel secret" 또는 헬스체크 502)
   ↓
④ `cloudflared tunnel token --cred-file <path>` 자동 재생성 + 서비스 재설치
   ↓
⑤ 외부 헬스체크 (https://demo.hitpan.kr/api/health) 200 OK → 정상화 완료
```

## 4. 구현 명세

### 4-1. EventLog 모니터링 (단계 ①)

```csharp
// HitPanWatchdog.exe — Worker Service 내부
// System EventLog 구독, EventID=1074 (TrustedInstaller 재부팅) 감지

using System.Diagnostics.Eventing.Reader;

private static EventLogWatcher? _watcher;

public override Task StartAsync(CancellationToken ct) {
    var query = new EventLogQuery("System", PathType.LogName,
        "*[System[Provider[@Name='User32'] and EventID=1074]]");

    _watcher = new EventLogWatcher(query);
    _watcher.EventRecordWritten += (s, e) => {
        if (e.EventRecord == null) return;
        _logger.LogWarning("[Watchdog] Windows 재부팅 예고 감지 — TrustedInstaller 1074");
        WriteRebootMarker();  // C:\ProgramData\HitPanWatchdog\reboot.flag 생성
    };
    _watcher.Enabled = true;
    return base.StartAsync(ct);
}
```

### 4-2. 재부팅 후 자가 진단 (단계 ②)

```csharp
// 워치독 서비스 시작 시 마커 파일 확인
public override async Task ExecuteAsync(CancellationToken ct) {
    var marker = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.CommonApplicationData),
        "HitPanWatchdog", "reboot.flag");

    if (File.Exists(marker)) {
        _logger.LogInformation("[Watchdog] 재부팅 후 첫 가동 — 자가 진단 시작");
        await Task.Delay(TimeSpan.FromMinutes(1), ct);  // 부팅 안정화 대기
        await SelfDiagnoseAsync(ct);
        File.Delete(marker);
    }

    // 정기 점검 (1분 주기) — 헌법 #30 자가 회복
    while (!ct.IsCancellationRequested) {
        await SelfDiagnoseAsync(ct);
        await Task.Delay(TimeSpan.FromMinutes(1), ct);
    }
}
```

### 4-3. TunnelSecret 무효화 감지 (단계 ③)

```csharp
private async Task<bool> IsCloudflaredHealthyAsync(CancellationToken ct) {
    // 1. cloudflared 서비스 상태
    using var sc = new ServiceController("cloudflared");
    if (sc.Status != ServiceControllerStatus.Running) {
        _logger.LogWarning("[Watchdog] cloudflared 서비스 중단 감지");
        return false;
    }

    // 2. 외부 헬스체크
    try {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var res = await http.GetAsync("https://demo.hitpan.kr/api/health", ct);
        if (!res.IsSuccessStatusCode) {
            _logger.LogWarning("[Watchdog] 외부 헬스체크 실패: {Status}", res.StatusCode);
            return false;
        }
    } catch (Exception ex) {
        _logger.LogWarning(ex, "[Watchdog] 외부 헬스체크 예외");
        return false;
    }

    // 3. cloudflared.log 최근 "Invalid tunnel secret" 검색 (선택)
    var logPath = @"C:\cloudflared\cloudflared.log";
    if (File.Exists(logPath)) {
        var lines = await File.ReadAllLinesAsync(logPath, ct);
        var recent = lines.TakeLast(200);
        if (recent.Any(l => l.Contains("Invalid tunnel secret", StringComparison.OrdinalIgnoreCase))) {
            _logger.LogError("[Watchdog] TunnelSecret 무효화 감지!");
            return false;
        }
    }

    return true;
}
```

### 4-4. 자동 봉합 (단계 ④) — ⚠️ 헌법 #29 정합

```csharp
private async Task RecoverCloudflaredAsync(CancellationToken ct) {
    // ⚠️ 헌법 #29 — 워치독 EXE는 사장님 설치 시 자동 허가됨 (1회 결재)
    // 매 봉합마다 추가 결재 불요. 단, 봉합 액션은 PII 0 + 데이터 0 + 텔레메트리만.

    _logger.LogWarning("[Watchdog] cloudflared 자동 봉합 시작");

    // 1. 서비스 중단
    using (var sc = new ServiceController("cloudflared")) {
        if (sc.Status == ServiceControllerStatus.Running) {
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }
    }

    // 2. TunnelSecret 재생성 (사장님 사전 등록 토큰 사용, 외부 호출 0)
    var configPath = @"C:\cloudflared\config.yml";
    var tunnelId = ExtractTunnelIdFromConfig(configPath);
    var credPath = $@"C:\cloudflared\{tunnelId}.json";

    var psi = new ProcessStartInfo("cloudflared",
        $"tunnel token --cred-file \"{credPath}\" {tunnelId}") {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    using var proc = Process.Start(psi)!;
    await proc.WaitForExitAsync(ct);
    if (proc.ExitCode != 0) {
        _logger.LogError("[Watchdog] cloudflared tunnel token 재생성 실패: {Code}", proc.ExitCode);
        await NotifyMetaPingAsync("cloudflared_recovery_failed", ct);
        return;
    }

    // 3. 서비스 재시작
    using (var sc = new ServiceController("cloudflared")) {
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
    }

    // 4. 외부 헬스체크 재검증
    await Task.Delay(TimeSpan.FromSeconds(15), ct);  // 터널 안정화
    if (await IsCloudflaredHealthyAsync(ct)) {
        _logger.LogInformation("[Watchdog] cloudflared 자동 봉합 완료");
        await NotifyMetaPingAsync("cloudflared_recovered", ct);
    } else {
        _logger.LogError("[Watchdog] 봉합 후에도 헬스체크 실패 — 사장님 알림");
        await NotifyMetaPingAsync("cloudflared_recovery_partial_fail", ct);
    }
}
```

### 4-5. 본사 메타 ping (단계 ⑤) — 헌법 #22 정합

```csharp
private async Task NotifyMetaPingAsync(string eventType, CancellationToken ct) {
    // 본사로 전송하는 것 = 이벤트 타입 + 타임스탬프 + 머신 ID(해시)만
    // PII 0, 비즈니스 데이터 0, 로그 본문 0
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    var payload = new {
        eventType,
        ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        machineHash = ComputeMachineHash(),  // SHA256(machineGuid)
    };
    try {
        await http.PostAsJsonAsync(
            "https://watchdog-ingest.hitpan.app/v1/event",
            payload, ct);
    } catch {
        // 본사 ping 실패는 워치독 코어 동작에 영향 없음
    }
}
```

## 5. 보안 (헌법 #29 정합)

| 액션 | 사전 결재 |
|---|---|
| 워치독 EXE 설치 | ✅ 사장님 1회 결재 (설치 시점) |
| 자동 ServiceController.Start/Stop | ✅ 워치독 권한 범위 (설치 시 명시) |
| cloudflared tunnel token (--cred-file 옵션, 로컬 자격증명) | ✅ 사장님 설치 시 토큰 사전 등록 |
| Cloudflare API 호출 | ❌ 금지 (외부 클라우드 = 헌법 #29) |
| DNS / 방화벽 / 환경변수 변경 | ❌ 금지 |

워치독은 **로컬 봉합 + 메타 ping**만. 외부 인프라 조작 0.

## 6. 검증 (5/19 강제 시나리오)

```powershell
# 시나리오 1: Windows Update 1074 시뮬
$src = "User32"; $id = 1074
Write-EventLog -LogName System -Source $src -EventId $id -Message "User initiated restart"
# 워치독이 reboot.flag 생성하는지 확인

# 시나리오 2: cloudflared 강제 종료
Stop-Service cloudflared
# 1분 이내 워치독이 재시작하는지 확인

# 시나리오 3: 외부 헬스체크 실패
# (네트워크 차단 5분 → 복구) 워치독 로그에 cloudflared_recovery_failed/recovered 기록 확인

# 시나리오 4: TunnelSecret 인위적 손상
# cred 파일 백업 후 무효 데이터로 덮어쓰기
# 워치독이 자동 재생성하는지 확인 (요건: 설치 시 토큰 사전 등록)
```

## 7. 검증 게이트

| 게이트 | 통과 조건 |
|---|---|
| 시나리오 1 (1074 감지) | 30초 이내 reboot.flag 생성 |
| 시나리오 2 (서비스 중단) | 1분 이내 자동 재시작 |
| 시나리오 3 (헬스체크 실패) | 5분 이내 복구 시도 또는 메타 ping |
| 시나리오 4 (TunnelSecret 손상) | 5분 이내 재생성 + 서비스 정상화 |
| 백신 호환성 | Defender + V3 Lite + 알약 + 네이버 + Norton + McAfee 격리 0 |
| 헌법 #29 | 외부 API 호출 0 (메타 ping 제외, 그것도 본사 자체 호스트) |

## 8. SLA 산정

- 현재 (5/15 사고): 6시간 다운 → 연간 SLA 환산 99.93% (목표 99.99% 미달)
- 본 워치독 적용: 5분 다운 한계 → 연간 다운타임 52분 이내 가능 → SLA 99.99% 달성

---

**작성: PM 브라운킴 2026-05-15 15:35**
**문서 ID: WS-20260515-02**
**다음:** WS-20260515-03 (코드 서명 + DPAPI 자격증명)
