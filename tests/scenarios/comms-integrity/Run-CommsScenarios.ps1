# =====================================================================
#  통신 무결성 17 시나리오 — 자동화 가능 4건 실측 계측 스크립트
#  헌법 #27: 죽인 뒤 5분 이내 자동 복구 = PASS / 초과·미복구 = FAIL
#  헌법 #39: 운영(demo) 금지 — 반드시 별도 테스트 환경에서만. 안전가드 내장.
#
#  대상 4건 (워치독 자동복구 검증):
#   S-A  cloudflared 서비스 강제 종료  → 워치독 sc.Start() 재기동
#   S-B  MariaDB 서비스 강제 중지       → 워치독 sc.Start() 재기동
#   S-C  HitPan.API(5257) 강제 kill     → 워치독 schtasks /Run 재기동
#   S-D  cloudflared TunnelSecret 무효화 → 워치독 WS28C 재생성
#
#  실행: 테스트 환경 관리자 PowerShell 에서
#    powershell -ExecutionPolicy Bypass -File Run-CommsScenarios.ps1 -Confirm
# =====================================================================
param(
    [switch]$Confirm,                       # 명시적 동의 없으면 실행 거부(오발사 방지)
    # 봉합② (작2, 2026-06-28): 죽이는 대상을 test 서비스명/경로로만 한정. demo 공통명(cloudflared·MariaDB) 죽이기 차단.
    # 기본값은 -test 접미사. 마커 파일이 슬롯의 실제 서비스명을 명세하면 그 값으로 덮어씀(마커=단일 진실원).
    [string]$CloudflaredSvc = 'cloudflared-test',
    [string]$MariaDbSvc     = 'MariaDB-test',
    [string]$ApiSlotPath    = 'C:\HitPanTest',     # API kill 은 ProcessPath 가 이 경로 하위일 때만(MUST, CTO 6-1 #2)
    [string]$ApiHealthUrl = "http://127.0.0.1:15257/health",  # 봉합②: test 포트 기본
    [string]$WebUrl       = "http://127.0.0.1:15234/",
    [int]$PassMinutes     = 5,              # 헌법 #27: 5분 이내 복구 = PASS
    [string]$ReportDir    = "$PSScriptRoot\reports",
    [string]$MarkerPath   = "C:\HitPanTest\HITPAN_TEST_ENV.marker"  # 봉합① 화이트리스트 마커
)

$ErrorActionPreference = 'Continue'

# ── 안전가드 1: 명시 동의 ──
if (-not $Confirm) {
    Write-Host "[X] -Confirm 플래그 필수. 이 스크립트는 cloudflared·MariaDB·API 를 실제로 죽입니다." -ForegroundColor Red
    Write-Host "    헌법 #39: 운영(demo) 환경에서 절대 실행 금지. 테스트 환경에서만 -Confirm 으로 실행." -ForegroundColor Yellow
    exit 1
}

# ── 안전가드 2 (봉합① 작2 2026-06-28): 화이트리스트 fail-safe ──
#   기존 블랙리스트(db.conf demo면 차단)는 db.conf 없으면 fail-open 으로 무력화됐다(5-1 백업 중 실측 발견).
#   → fail-closed 로 전환: "test 환경임이 명시 증명될 때만 실행". demo 엔 마커가 절대 없으므로 100% 차단.
# 1) test 마커 파일 존재 + 내용 검증 (없으면 무조건 차단)
if (-not (Test-Path $MarkerPath)) {
    Write-Host "[X] test 마커 없음: $MarkerPath" -ForegroundColor Red
    Write-Host "    화이트리스트 fail-safe(헌법 #39): test 환경 증명 안 됨 = 실행 거부. demo 보호." -ForegroundColor Yellow
    exit 1
}
$marker = Get-Content $MarkerPath -ErrorAction SilentlyContinue
if (-not ($marker | Where-Object { $_ -match '^\s*ENV\s*=\s*TEST\s*$' })) {
    Write-Host "[X] 마커 내용 무효: 'ENV=TEST' 줄 없음. 실행 거부." -ForegroundColor Red
    exit 1
}
# 마커가 서비스명을 명세하면 채택(마커=단일 진실원, 봉합②와 정합)
$mSvcCf = ($marker | Where-Object { $_ -match '^\s*CLOUDFLARED_SVC\s*=' }) -replace '.*=\s*',''
$mSvcDb = ($marker | Where-Object { $_ -match '^\s*MARIADB_SVC\s*=' })     -replace '.*=\s*',''
if ($mSvcCf) { $CloudflaredSvc = $mSvcCf.Trim() }
if ($mSvcDb) { $MariaDbSvc     = $mSvcDb.Trim() }

# 2) 마커 잔존 방어 (CTO 6-1 #1): test 마커 + demo 서비스(접미사 없는 cloudflared) Running 동시 = 위험상태 → 차단
$demoCf = Get-Service 'cloudflared' -ErrorAction SilentlyContinue
if ($demoCf -and $demoCf.Status -eq 'Running') {
    Write-Host "[X] demo 서비스(cloudflared, 접미사없음)가 Running 인데 test 마커도 존재 = 위험상태." -ForegroundColor Red
    Write-Host "    같은 PC에서 demo 가 살아있는 동안 실측 금지(헌법 #39). 마커 오잔존 의심." -ForegroundColor Yellow
    exit 1
}

# 3) 2차 방어 유지 (CTO 6-1 #3, db.conf 있으면 demo 차단 — 지우지 말 것) ──
$dbConf = "C:\Program Files\HitPan\db.conf"
if (Test-Path $dbConf) {
    $primary = (Get-Content $dbConf | Where-Object { $_ -match '^\s*PRIMARY_DOMAIN\s*=' }) -replace '.*=\s*',''
    if ($primary -match 'demo\.hitpan\.kr') {
        Write-Host "[X] PRIMARY_DOMAIN=demo.hitpan.kr 감지 = 운영 환경. 헌법 #39 위반. 중단." -ForegroundColor Red
        Write-Host "    테스트 환경(별도 터널·DB·포트)에서만 실행하십시오." -ForegroundColor Yellow
        exit 1
    }
}

# ── 봉합② 가드 헬퍼: 서비스명이 test(-test 접미사 또는 마커 명세)인지 검증 ──
function Assert-TestSvc($name) {
    if ($name -notmatch '-test$') {
        Log "  [GUARD] 서비스명 '$name' 에 '-test' 접미사 없음 → demo 서비스 보호 위해 SKIP."
        return $false
    }
    return $true
}

if (-not (Test-Path $ReportDir)) { New-Item -ItemType Directory -Path $ReportDir -Force | Out-Null }
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$report = "$ReportDir\comms-scenarios-$stamp.md"
$results = @()

function Log($m) { Write-Host $m; Add-Content -Path $report -Value $m }

# ── 공통: 대상이 5분 내 복구되는지 폴링 ──
function Wait-Recovery {
    param([string]$Name, [scriptblock]$IsHealthy)
    $start = Get-Date
    $deadline = $start.AddMinutes($PassMinutes)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 10
        if (& $IsHealthy) {
            $elapsed = [math]::Round(((Get-Date) - $start).TotalSeconds, 0)
            return @{ Pass = $true; Seconds = $elapsed }
        }
    }
    return @{ Pass = $false; Seconds = ($PassMinutes * 60) }
}

function HttpOk($url) {
    try { (Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 8).StatusCode -eq 200 } catch { $false }
}
function SvcRunning($name) {
    try { (Get-Service $name -ErrorAction Stop).Status -eq 'Running' } catch { $false }
}

Log "# 통신 무결성 시나리오 실측 — $stamp"
Log "헌법 #27: 5분 이내 자동 복구 = PASS / 루프주기 60초·임계 3회 고려"
Log ""

# ── S-A: cloudflared(test) 서비스 강제 종료 ── 봉합②: $CloudflaredSvc 만 죽임(demo 'cloudflared' 보호)
Log "## S-A cloudflared(test) 서비스 강제 종료 → 워치독 재기동  [대상: $CloudflaredSvc]"
if (-not (Assert-TestSvc $CloudflaredSvc)) {
    $results += [pscustomobject]@{ Scenario='S-A cloudflared'; Verdict='SKIP(가드)' }
} elseif (SvcRunning $CloudflaredSvc) {
    Stop-Service $CloudflaredSvc -Force -ErrorAction SilentlyContinue
    Log "  죽임: $CloudflaredSvc 중지 ($(Get-Date -Format HH:mm:ss))"
    $r = Wait-Recovery $CloudflaredSvc { SvcRunning $CloudflaredSvc }
    $verdict = if ($r.Pass) { "PASS ($($r.Seconds)초)" } else { "FAIL (5분 초과 미복구)" }
    Log "  결과: $verdict"
    $results += [pscustomobject]@{ Scenario='S-A cloudflared'; Verdict=$verdict }
} else { Log "  SKIP: $CloudflaredSvc 서비스 없음(테스트 환경 미구성)"; $results += [pscustomobject]@{ Scenario='S-A'; Verdict='SKIP' } }
Log ""

# ── S-B: MariaDB(test) 서비스 강제 중지 ── 봉합②: $MariaDbSvc 만 죽임(demo 'MariaDB' 보호)
Log "## S-B MariaDB(test) 서비스 강제 중지 → 워치독 재기동  [대상: $MariaDbSvc]"
if (-not (Assert-TestSvc $MariaDbSvc)) {
    $results += [pscustomobject]@{ Scenario='S-B MariaDB'; Verdict='SKIP(가드)' }
} elseif (SvcRunning $MariaDbSvc) {
    Stop-Service $MariaDbSvc -Force -ErrorAction SilentlyContinue
    Log "  죽임: $MariaDbSvc 중지 ($(Get-Date -Format HH:mm:ss))"
    $r = Wait-Recovery $MariaDbSvc { SvcRunning $MariaDbSvc }
    $verdict = if ($r.Pass) { "PASS ($($r.Seconds)초)" } else { "FAIL (5분 초과 미복구)" }
    Log "  결과: $verdict"
    $results += [pscustomobject]@{ Scenario='S-B MariaDB'; Verdict=$verdict }
} else { Log "  SKIP: $MariaDbSvc 서비스 없음"; $results += [pscustomobject]@{ Scenario='S-B'; Verdict='SKIP' } }
Log ""

# ── S-C: HitPan.API(test) 강제 kill → schtasks 재기동 ──
#   봉합② MUST(CTO 6-1 #2): demo API 와 test API 둘 다 'HitPan.API.exe' 라 ProcessPath 가 $ApiSlotPath(C:\HitPanTest) 하위일 때만 kill.
#   경로 가드 없으면 demo API 가 죽는다(헌법 #39). 경로 미확인 프로세스는 절대 죽이지 않는다.
Log "## S-C HitPan.API(test) 강제 kill → 워치독 schtasks /Run 재기동  [경로가드: $ApiSlotPath]"
$apiProcs = Get-Process HitPan.API -ErrorAction SilentlyContinue
$testApi = $apiProcs | Where-Object {
    $p = $null; try { $p = $_.Path } catch { $p = $null }
    $p -and $p.StartsWith($ApiSlotPath, [System.StringComparison]::OrdinalIgnoreCase)
}
if ($testApi) {
    $testApi | ForEach-Object { Log "  죽임 대상: PID $($_.Id) Path=$($_.Path)" }
    $testApi | Stop-Process -Force
    Log "  죽임: HitPan.API(test) kill ($(Get-Date -Format HH:mm:ss))"
    $r = Wait-Recovery 'HitPan.API' { HttpOk $ApiHealthUrl }
    $verdict = if ($r.Pass) { "PASS ($($r.Seconds)초)" } else { "FAIL (5분 초과 미복구 — db.conf SLOT_INDEX/RestartTask 확인)" }
    Log "  결과: $verdict"
    $results += [pscustomobject]@{ Scenario='S-C API'; Verdict=$verdict }
} elseif ($apiProcs) {
    Log "  SKIP(경로가드): HitPan.API 프로세스는 있으나 $ApiSlotPath 하위가 아님 → demo API 보호 위해 kill 안 함."
    $results += [pscustomobject]@{ Scenario='S-C API'; Verdict='SKIP(경로가드)' }
} else { Log "  SKIP: HitPan.API 프로세스 없음"; $results += [pscustomobject]@{ Scenario='S-C'; Verdict='SKIP' } }
Log ""

# ── S-D: TunnelSecret 무효화 → WS28C 재생성 ──
#   실제 무효화는 자격증명 파일 손상으로 시뮬(테스트 터널만). 워치독 로그 'Invalid tunnel secret' 감지·재생성 확인.
Log "## S-D TunnelSecret 무효화 시뮬 → 워치독 WS28C 재생성"
Log "  주의: 테스트 터널 자격증명만 대상. 워치독 EventLog(HitPanWatchdog)에서 재생성 로그 확인 필요."
Log "  자동계측 한계: 자격증명 재생성은 외부 헬스체크(api 도메인 200 복귀)로 간접 PASS 판정."
Log "  → 수동 보강: Get-EventLog Application -Source HitPanWatchdog 에서 WS28C RegenerateAsync 성공 확인."
$results += [pscustomobject]@{ Scenario='S-D TunnelSecret'; Verdict='반자동(EventLog 병행)' }
Log ""

# ── 종합 ──
Log "## 종합"
$pass = ($results | Where-Object { $_.Verdict -like 'PASS*' }).Count
$fail = ($results | Where-Object { $_.Verdict -like 'FAIL*' }).Count
$skip = ($results | Where-Object { $_.Verdict -eq 'SKIP' }).Count
Log "  PASS=$pass  FAIL=$fail  SKIP=$skip"
$results | ForEach-Object { Log "  - $($_.Scenario): $($_.Verdict)" }
Log ""
Log "리포트: $report"
Write-Host "`n[완료] 리포트 저장: $report" -ForegroundColor Green
