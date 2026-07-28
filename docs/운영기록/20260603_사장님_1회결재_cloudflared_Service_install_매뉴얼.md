# 🔐 6/3 W1 게이트 — cloudflared Service install 사장님 1회 결재 매뉴얼

> 작성일 : 2026-05-29 (선제 박제, 실행 시점 6/3)
> 헌법 정합 : #29 인프라 조작 사전 승인 (PM 자동 실행 절대 금지)
> 사장님 손 : 정확히 **1회 클릭** (관리자 PowerShell)
> 소요 : 약 1분
> 효과 : 5/27 사고 RC1·RC3 영구 봉합

---

## 0. 본 작업이 필요한 이유

5/27 모바일 외부 접속 사고의 **근본 원인 3건 중 2건** (RC1·RC3)이 이 단계에서 영구 봉합됩니다.

| RC | 내용 | 본 단계 봉합 |
|---|---|---|
| RC1 | cloudflared 콘솔 모드 운영 | ✅ Service 등록으로 봉합 |
| RC3 | 워치독 부재 | ✅ HitPanWatchdog Service 등록 |
| RC2 | PM appsettings 위반 | (5/29 PM 자수 처리 완료) |

워치독 `--health` 5/29 실측:
- `cloudflared service: false` ← **RC1 그대로 남아있음**
- `WatchdogService: false` ← **RC3 그대로 남아있음**

---

## 1. 사전 점검 (사장님 손 0회, PM 자동 박제 가능)

다음 명령어 실행으로 사전 점검:
```powershell
# 워치독 진단 (read-only, 5초 소요)
& "C:\Users\소순근\Desktop\hitpan-erp\src\HitPan.Watchdog\bin\Release\net8.0-windows\win-x64\HitPan.Watchdog.exe" --health
```

예상 출력 (현 상태):
```json
{
  "OverallStatus": "recovering",
  "ExternalHealthOk": true,
  "CloudflaredServiceExists": false,   ← 이 줄을 봉합합니다
  "WatchdogServiceExists": false,      ← 이 줄도 봉합합니다
  "ProcessStatus": { "MariaDB": true, "cloudflared": false, ... }
}
```

---

## 2. 사장님 결재 1회 실행 (관리자 PowerShell)

### 2-A. cloudflared Service install

**❶ 관리자 PowerShell 열기**
- 시작 → "powershell" 입력 → 우클릭 → **관리자 권한으로 실행**

**❷ 다음 명령 한 줄 실행**
```powershell
& "$env:USERPROFILE\.cloudflared\cloudflared.exe" service install
```

또는 cloudflared가 다른 위치인 경우:
```powershell
& "C:\Program Files\HitPan\payload\cloudflared.exe" service install
```

**❸ 예상 출력**
```
2026-06-03T... INF Using Service Configuration FilePath: C:\Users\소순근\.cloudflared\config.yml
2026-06-03T... INF Installed Cloudflare Tunnel Windows service
```

**❹ Service 시작**
```powershell
Start-Service cloudflared
```

### 2-B. (선택) HitPanWatchdog Service install

워치독은 사장님 PC가 베타 1주차 발진(6/15) 시점에 등록 권장. 6/3에는 cloudflared만 등록해도 충분.

만약 6/3에 워치독도 미리 등록하려면:
```powershell
# 워치독 EXE를 안정 위치로 박제
$exe = "C:\Program Files\HitPan\Watchdog\HitPan.Watchdog.exe"
New-Item -ItemType Directory -Path (Split-Path $exe) -Force | Out-Null
Copy-Item "C:\Users\소순근\Desktop\hitpan-erp\src\HitPan.Watchdog\bin\Release\net8.0-windows\win-x64\HitPan.Watchdog.exe" $exe -Force

# Service 등록 (5초 / 5초 / 60초 자동 재시작)
sc.exe create HitPanWatchdog binPath= "`"$exe`"" start= auto DisplayName= "HitPan Watchdog"
sc.exe failure HitPanWatchdog reset= 60 actions= restart/5000/restart/5000/restart/60000
Start-Service HitPanWatchdog
```

---

## 3. 봉합 후 검증 (사장님 또는 PM 자동)

```powershell
# 워치독 진단 재실행
& "C:\Users\소순근\Desktop\hitpan-erp\src\HitPan.Watchdog\bin\Release\net8.0-windows\win-x64\HitPan.Watchdog.exe" --health
```

기대 출력:
```json
{
  "OverallStatus": "healthy",          ← recovering → healthy
  "CloudflaredServiceExists": true,    ← false → true
  "WatchdogServiceExists": true        ← (워치독도 등록한 경우만)
}
```

Smoke 재실행:
```powershell
& "C:\Users\소순근\Desktop\hitpan-erp\tests\scenarios\Smoke-ExternalEndpoints.ps1"
```

기대 = **8/8 PASS** 유지.

---

## 4. 롤백 (예상치 못한 사고 시)

`cloudflared service install`이 잘못 적용된 경우 즉시 원복:
```powershell
# Service 중단·삭제
Stop-Service cloudflared -Force
sc.exe delete cloudflared

# 콘솔 모드 복귀 (5/27 사고 직전 상태)
& "$env:USERPROFILE\.cloudflared\cloudflared.exe" tunnel run
```

롤백 후 워치독 `--health` 다시 실행해 `CloudflaredServiceExists: false` 박제.

---

## 5. 본 결재의 영구 효과

| 시점 | 효과 |
|---|---|
| 6/3 결재 직후 | Windows 부팅 시 cloudflared 자동 시작 |
| Windows Update 강제 재부팅 | cloudflared 자동 복귀 (5/15·5/27 사고 재발 0) |
| 사장님 출장 중 PC 무인 가동 | 안정 |
| 베타 1주차 (6/15) | 진짜 SLA 99.99% 산정 시작 |

---

## 6. 결재 양식

```
[6/3 W1 게이트 — cloudflared Service install]

승인 시각: __________
실행자: 사장님 (관리자 PowerShell)
실행 명령:
  & "$env:USERPROFILE\.cloudflared\cloudflared.exe" service install
  Start-Service cloudflared

봉합 후 검증:
  [ ] HitPan.Watchdog.exe --health → CloudflaredServiceExists: true
  [ ] Smoke-ExternalEndpoints.ps1 → 8/8 PASS
  [ ] sc query cloudflared → STATE: RUNNING

[ ] 결재 승인
[ ] 결재 반려 (사유: __________)
```

---

## 7. 헌법 정합

- **#27** 통신 무결성 99.99% — Service 등록으로 영구 자가 회복 박제
- **#28** cloudflared Windows Update 자동 봉합 — WS-28-A·B 동작 조건 충족
- **#29** 인프라 조작 사전 승인 — PM 자동 실행 0, 사장님 1회 결재 박제
- **#30** 고객 PC 자가 회복 — 본사 의존 0
- **#31** OS 보안 도구 호환성 — Service 등록이 백신 화이트리스트 트리거

---

**문서 끝.** 6/3 W1 게이트 통과 후 즉시 베타 1주차 가도 발진.
