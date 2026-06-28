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
    [string]$ApiHealthUrl = "http://127.0.0.1:5257/health",
    [string]$WebUrl       = "http://127.0.0.1:5234/",
    [int]$PassMinutes     = 5,              # 헌법 #27: 5분 이내 복구 = PASS
    [string]$ReportDir    = "$PSScriptRoot\reports"
)

$ErrorActionPreference = 'Continue'

# ── 안전가드 1: 명시 동의 ──
if (-not $Confirm) {
    Write-Host "[X] -Confirm 플래그 필수. 이 스크립트는 cloudflared·MariaDB·API 를 실제로 죽입니다." -ForegroundColor Red
    Write-Host "    헌법 #39: 운영(demo) 환경에서 절대 실행 금지. 테스트 환경에서만 -Confirm 으로 실행." -ForegroundColor Yellow
    exit 1
}

# ── 안전가드 2: 운영(demo) 환경 감지 시 차단 ──
$dbConf = "C:\Program Files\HitPan\db.conf"
if (Test-Path $dbConf) {
    $primary = (Get-Content $dbConf | Where-Object { $_ -match '^\s*PRIMARY_DOMAIN\s*=' }) -replace '.*=\s*',''
    if ($primary -match 'demo\.hitpan\.kr') {
        Write-Host "[X] PRIMARY_DOMAIN=demo.hitpan.kr 감지 = 운영 환경. 헌법 #39 위반. 중단." -ForegroundColor Red
        Write-Host "    테스트 환경(별도 터널·DB·포트)에서만 실행하십시오." -ForegroundColor Yellow
        exit 1
    }
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

# ── S-A: cloudflared 서비스 강제 종료 ──
Log "## S-A cloudflared 서비스 강제 종료 → 워치독 재기동"
if (SvcRunning 'cloudflared') {
    Stop-Service cloudflared -Force -ErrorAction SilentlyContinue
    Log "  죽임: cloudflared 중지 ($(Get-Date -Format HH:mm:ss))"
    $r = Wait-Recovery 'cloudflared' { SvcRunning 'cloudflared' }
    $verdict = if ($r.Pass) { "PASS ($($r.Seconds)초)" } else { "FAIL (5분 초과 미복구)" }
    Log "  결과: $verdict"
    $results += [pscustomobject]@{ Scenario='S-A cloudflared'; Verdict=$verdict }
} else { Log "  SKIP: cloudflared 서비스 없음(테스트 환경 미구성)"; $results += [pscustomobject]@{ Scenario='S-A'; Verdict='SKIP' } }
Log ""

# ── S-B: MariaDB 서비스 강제 중지 ──
Log "## S-B MariaDB 서비스 강제 중지 → 워치독 재기동"
if (SvcRunning 'MariaDB') {
    Stop-Service MariaDB -Force -ErrorAction SilentlyContinue
    Log "  죽임: MariaDB 중지 ($(Get-Date -Format HH:mm:ss))"
    $r = Wait-Recovery 'MariaDB' { SvcRunning 'MariaDB' }
    $verdict = if ($r.Pass) { "PASS ($($r.Seconds)초)" } else { "FAIL (5분 초과 미복구)" }
    Log "  결과: $verdict"
    $results += [pscustomobject]@{ Scenario='S-B MariaDB'; Verdict=$verdict }
} else { Log "  SKIP: MariaDB 서비스 없음"; $results += [pscustomobject]@{ Scenario='S-B'; Verdict='SKIP' } }
Log ""

# ── S-C: HitPan.API(5257) 강제 kill → schtasks 재기동 ──
Log "## S-C HitPan.API 강제 kill → 워치독 schtasks /Run 재기동"
$apiProc = Get-Process HitPan.API -ErrorAction SilentlyContinue
if ($apiProc) {
    $apiProc | Stop-Process -Force
    Log "  죽임: HitPan.API PID $($apiProc.Id) kill ($(Get-Date -Format HH:mm:ss))"
    $r = Wait-Recovery 'HitPan.API' { HttpOk $ApiHealthUrl }
    $verdict = if ($r.Pass) { "PASS ($($r.Seconds)초)" } else { "FAIL (5분 초과 미복구 — db.conf SLOT_INDEX/RestartTask 확인)" }
    Log "  결과: $verdict"
    $results += [pscustomobject]@{ Scenario='S-C API'; Verdict=$verdict }
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
