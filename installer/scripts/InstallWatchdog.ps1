# =================================================================
# InstallWatchdog.ps1 — HitPan.Watchdog Windows Service 등록 + Guardian
# =================================================================
param(
    [Parameter(Mandatory=$true)]
    [string]$InstallPath
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'HitPanWatchdog'
# 사고 #5 봉합 (WS-20260612-01 2026-06-12): 워치독 EXE 경로 영역 정정
#   기존: $InstallPath\payload\HitPan.Watchdog\HitPan.Watchdog.exe (옛 빌드 구조)
#   정정: $InstallPath\watchdog\HitPan.Watchdog.exe (현재 빌드 정합 — HitPan-Universal.iss:93 정합)
$ExePath     = Join-Path $InstallPath 'watchdog\HitPan.Watchdog.exe'

# ★★ 봉합 20260804작1 — 종료코드 3분화 ★★
#   0 = 서비스 등록 + Guardian 등록 + 기동 전부 성공
#   1 = 서비스가 아예 없다(EXE 부재 / sc create 실패) = 치명. 워치독 기능 0.
#   2 = 서비스·Guardian 은 만들어졌고 **기동만** 실패 = 자가복구 가능(5분 뒤 Guardian, 재부팅 시 auto).
#   종전엔 1/2 구분이 없어 .iss 가 "재부팅하면 시작됩니다" 라는 같은 안내를 냈다.
#   그러나 sc create 자체가 실패한 경우 재부팅해도 안 뜬다 — 고객에게 틀린 안내였다.
#   3 = 예상 못 한 오류(미처리 예외) — 아래 trap 이 낸다.
#     ★ 봉합 20260804작1 P1 ([3-V] 병렬검증 적발): PowerShell 은 **미처리 종료예외에도 exit 1** 을 낸다.
#       그대로 두면 EXIT_FATAL(서비스 생성 실패)과 구분이 안 돼 위 규약이 깨진다
#       (Out-File·Get-Service 등이 죽어도 1). trap 으로 3 을 분리해 규약을 지킨다.
$EXIT_OK = 0; $EXIT_FATAL = 1; $EXIT_START_FAILED = 2; $EXIT_UNEXPECTED = 3

trap {
    Write-Output "[FAIL] 예상 못 한 오류: $($_.Exception.Message)"
    exit $EXIT_UNEXPECTED
}

if (-not (Test-Path $ExePath)) {
    # 봉합 (2026-07-14, Sandbox 실측 적발): 종전 exit 0 = "설치 성공인데 워치독 없음" 침묵 실패.
    #   워치독은 필수 인프라(헌법 #28·#30·자동업데이트)라 부재는 실패로 승격 — .iss 가 경고 가시화.
    Write-Output "[FAIL] $ExePath not found — watchdog service NOT installed"
    exit $EXIT_FATAL
}

# 기존 서비스 정리
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $existing) {
    Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# 서비스 등록
# ★ 봉합 20260804작1 (②): 종전 `| Out-Null` 로 결과를 버려 **생성 실패가 조용히 통과**했다.
#   그러면 다음 줄 Start-Service 가 '없는 서비스'를 시작하려다 실패하고, .iss 는 그걸
#   "자동 시작 등록 실패"로 보고한다 — **진짜 원인(생성 실패)이 가려진다.**
#   ⚠️ 재설치 정합: 위 sc delete 직후 서비스가 'marked for deletion' 이면 create 가 1072 로 실패한다
#     (이 프로젝트가 cloudflared 에서 이미 당한 계통 — 20260803작1). Sleep 2초로 부족할 수 있으므로
#     **실패 시 3초 간격 3회 재시도** 후에도 안 되면 치명으로 승격한다.
$createOk = $false
for ($i = 1; $i -le 3; $i++) {
    & sc.exe create $ServiceName binPath= "`"$ExePath`"" start= auto DisplayName= "HitPan Watchdog" | Out-Null
    if ($LASTEXITCODE -eq 0) { $createOk = $true; break }
    # 1073 = 이미 존재. 정리 실패했더라도 서비스는 있으니 성공으로 본다(멱등).
    if ($LASTEXITCODE -eq 1073) {
        Write-Output "[WARN] service already exists (1073) — reusing"
        $createOk = $true; break
    }
    Write-Output "[WARN] sc create attempt $i failed (exit=$LASTEXITCODE) — retry in 3s"
    Start-Sleep -Seconds 3
}
if (-not $createOk) {
    Write-Output "[FAIL] sc create $ServiceName failed — watchdog service NOT installed"
    exit $EXIT_FATAL
}

# 부가 설정 — 실패해도 서비스 동작 자체엔 무해하므로 경고만 남긴다(헌법 #15: 침묵 금지).
& sc.exe description $ServiceName "HitPan ERP 워치독 - 통신 무결성 자가 복구 (헌법 #28)" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Output "[WARN] sc description failed (exit=$LASTEXITCODE) — 무해, 계속" }

# 실패 시 자동 재시작 (5초 / 5초 / 60초)
& sc.exe failure $ServiceName reset= 60 actions= restart/5000/restart/5000/restart/60000 | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Output "[WARN] sc failure 설정 실패 (exit=$LASTEXITCODE) — 무해, 계속" }

# =================================================================
# Guardian 등록 (2층 워치독, 5분 주기)
#
# ★★ 봉합 20260804작1 (①) — 이 블록을 Start-Service **앞으로** 옮겼다 ★★
#   ■ 무엇이 문제였나
#     종전 순서는  Start-Service(:39) → Guardian 등록(:66) 이었다.
#     :9 의 $ErrorActionPreference='Stop' 때문에 Start-Service 가 실패하면
#     **스크립트가 그 자리에서 즉사**해 Guardian 등록에 도달하지 못했다.
#       Start-Service 실패 → 즉사 → Guardian 미등록 → 아무도 안 살림
#     Guardian 은 '워치독이 죽어 있으면 5분마다 살리는' 2층 안전망인데,
#     **살릴 사람이 필요한 바로 그 상황에서 살릴 사람이 안 깔렸다.**
#     Start-Service 는 기본 30초 타임아웃이라 느린 고객 PC·백신 첫 검사에서 충분히 발생한다.
#   ■ 봉합: Guardian 을 먼저 등록한다. 그러면 기동이 실패해도 5분 뒤 Guardian 이 살린다.
#
# ★★ 봉합 20260804작1 (③) — Register-ScheduledTask → schtasks.exe ★★
#   ■ 진범 가능성이 높은 지점이다(2026-08-04 Sandbox 실측 기반)
#     Register-ScheduledTask 는 ScheduledTasks 모듈 = **CIM 기반**이다.
#     같은 모듈의 Get-ScheduledTask 가 Sandbox 에서 실패했다(실측):
#       "Cannot connect to CIM server. 액세스가 거부되었습니다."
#     그런데 사장님 실측은 **워치독 RUNNING + 팝업 발생** 이었다.
#     Start-Service 가 성공했는데도 exit≠0 이었다는 뜻이고,
#     그 뒤 남은 단계는 Register-ScheduledTask 뿐이다 ⇒ **여기서 죽었을 가능성이 높다.**
#     (미확정 — 설치 로그로 최종 확인 필요. 다만 schtasks 전환은 어느 쪽이든 이득이다.)
#   ■ schtasks.exe 는 CIM 을 타지 않는다. 이 레포의 지배적 관용구이기도 하다:
#     HitPan-Universal.iss 에 schtasks 사용 45건 vs Register-ScheduledTask 1건(이 줄뿐).
#     선례 없는 기법을 검증된 패턴이라 부르지 않는다(작10 교훈).
# =================================================================
$GuardianTask = 'HitPanWatchdogGuardian'
$GuardianPs1  = Join-Path $InstallPath 'scripts\Guardian.ps1'

# Guardian.ps1 작성 — 반드시 작업 등록보다 먼저(등록 즉시 실행될 수 있으므로 파일이 있어야 한다)
@'
$svc = Get-Service -Name HitPanWatchdog -ErrorAction SilentlyContinue
if ($null -eq $svc -or $svc.Status -ne 'Running') {
    try {
        Start-Service HitPanWatchdog -ErrorAction Stop
        Write-EventLog -LogName Application -Source HitPanSetup `
            -EntryType Warning -EventId 28008 `
            -Message "Guardian restarted HitPanWatchdog" -ErrorAction SilentlyContinue
    } catch { }
}
'@ | Out-File $GuardianPs1 -Encoding utf8

# 작업 스케줄러 등록 (5분 주기, 무한)
#   /SC MINUTE /MO 5 = 5분마다 무한 반복 (종전 -Once + RepetitionInterval 과 동등)
#   /RU SYSTEM /RL HIGHEST = 종전 New-ScheduledTaskPrincipal 과 동등
#   /F = 이미 있으면 덮어씀(멱등, 재설치 정합) — 종전 -Force 와 동등
#   ⚠️ Guardian 등록 실패는 **치명이 아니다**(2층 안전망 부재일 뿐 워치독 본체는 동작).
#     그래서 여기서 스크립트를 죽이지 않는다 — 경고만 남기고 기동으로 진행한다(헌법 #15).
#   ★★ 봉합 20260804작1 P0 ([3-V] 병렬검증 적발) — /TR 따옴표 파손 ★★
#     PS 5.1 은 네이티브 exe 를 부를 때 안쪽 `" 를 **벗겨서** 넘긴다. 그래서
#       /TR "powershell.exe ... -File `"$GuardianPs1`""
#     는 공백에서 쪼개진다(실측):
#       ARG[5] = <powershell.exe ... -File C:\Program>
#       ARG[6] = <Files\HitPan\scripts\Guardian.ps1>
#     .iss:58 DefaultDirName={autopf}\HitPan = "C:\Program Files\HitPan" — **공백이 항상 있다.**
#     ⇒ 기본 설치 전건에서 schtasks 가 'Verify' 인수 오류로 실패하고 Guardian 이 안 깔린다.
#     게다가 아래 분기가 경고만 남기고 exit 0 으로 통과시켜, 이 작지서가 없애려던
#     **침묵실패를 형태만 바꿔 재생산**한다.
#     봉합: \" 로 이스케이프한 문자열을 **변수에 담아** 넘긴다(실측: 한 인자로 온전히 전달).
#   ⚠️ 검증은 반드시 공백 포함 경로에서 하라 — 공백 없는 경로로 테스트하면 통과해버려 못 잡는다.
$guardianOk = $false
$guardianTr = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"' + $GuardianPs1 + '\"'
& schtasks.exe /Create /F /TN $GuardianTask /TR $guardianTr `
    /SC MINUTE /MO 5 /RU SYSTEM /RL HIGHEST | Out-Null
if ($LASTEXITCODE -eq 0) {
    $guardianOk = $true
    Write-Output "[OK] $GuardianTask scheduled (5min interval)"
} else {
    Write-Output "[WARN] $GuardianTask 등록 실패 (exit=$LASTEXITCODE) — 2층 안전망 없이 계속"
}

# =================================================================
# 서비스 기동 — 반드시 Guardian 등록 **뒤**에 온다(위 봉합 ① 참조)
# =================================================================
# ★ 봉합 20260804작1: try/catch 로 감싼다.
#   $ErrorActionPreference='Stop' 하에서 Start-Service 실패는 종료 예외가 되어 스크립트를 죽인다.
#   기동 실패는 **자가복구 가능한 실패**다 — 서비스는 start=auto 라 재부팅 시 뜨고,
#   Guardian 이 5분마다 살린다. 그러니 설치를 죽이지 말고 exit 2 로 구분해 알린다.
try {
    Start-Service $ServiceName
    Write-Output "[OK] $ServiceName installed and started"
} catch {
    # 헌법 #15 — 빈 catch 금지. 원인을 남긴다.
    Write-Output "[WARN] Start-Service 실패: $($_.Exception.Message)"
    if ($guardianOk) {
        Write-Output "[INFO] Guardian 등록됨 — 5분 내 자동 기동 시도 예정"
    } else {
        Write-Output "[INFO] Guardian 미등록 — 재부팅 시 자동 시작(start=auto)"
    }
    exit $EXIT_START_FAILED
}

exit $EXIT_OK
