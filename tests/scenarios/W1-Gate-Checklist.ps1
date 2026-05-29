# =================================================================
# W1-Gate-Checklist.ps1 — 6/3 W1 게이트 자동 통과 검증
# 헌법 #27 통신 무결성 + #28 cloudflared 자동 봉합 + #31 백신 호환성
# 베타 1주차(6/15) 발진 전 마지막 자동 게이트
# =================================================================
[CmdletBinding()]
param(
    [string]$WatchdogExe = "$PSScriptRoot\..\..\src\HitPan.Watchdog\bin\Release\net8.0-windows\win-x64\HitPan.Watchdog.exe",
    [string]$WebHost = "demo.hitpan.kr",
    [string]$ApiHost = "api-demo.hitpan.kr",
    [switch]$VerboseOutput
)

$ErrorActionPreference = 'Continue'
$results = @()
$start = Get-Date

function Add-Check {
    param([string]$Category, [string]$Name, [bool]$Pass, [string]$Detail = '')
    $results += @{
        Category = $Category
        Name = $Name
        Pass = $Pass
        Detail = $Detail
        Timestamp = Get-Date
    }
    $color = if ($Pass) { 'Green' } else { 'Red' }
    $mark  = if ($Pass) { '[ PASS ]' } else { '[ FAIL ]' }
    Write-Host "$mark $Category :: $Name $(if ($Detail) { "→ $Detail" })" -ForegroundColor $color
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  W1 게이트 자동 검증 — $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# ───────────── 1. 워치독 빌드 & --health ─────────────
Write-Host "── 1. 워치독 빌드 + --health ──" -ForegroundColor Yellow
if (Test-Path $WatchdogExe) {
    Add-Check '워치독' 'EXE 존재' $true $WatchdogExe
    try {
        $json = & $WatchdogExe --health 2>&1 | Out-String
        $health = $json | ConvertFrom-Json
        Add-Check '워치독' '--health 응답' $true "OverallStatus: $($health.OverallStatus)"
        Add-Check '워치독' 'ExternalHealthOk' $health.ExternalHealthOk "demo /health 200"
        Add-Check '워치독' 'CloudflaredServiceExists' $health.CloudflaredServiceExists "cloudflared Service 등록"
        Add-Check '워치독' 'WatchdogServiceExists' $health.WatchdogServiceExists "HitPanWatchdog Service 등록"
        Add-Check '워치독' 'MariaDB 정상' $health.ProcessStatus.MariaDB
        Add-Check '워치독' 'HitPan.API 정상' $health.ProcessStatus.'HitPan.API'
        Add-Check '워치독' 'HitPan.Web 정상' $health.ProcessStatus.'HitPan.Web'
    } catch {
        Add-Check '워치독' '--health 응답' $false $_.Exception.Message
    }
} else {
    Add-Check '워치독' 'EXE 존재' $false "$WatchdogExe not found — dotnet build 필요"
}

# ───────────── 2. xUnit 테스트 ─────────────
Write-Host "`n── 2. xUnit 테스트 ──" -ForegroundColor Yellow
$testProj = Join-Path $PSScriptRoot "..\..\src\HitPan.Watchdog.Tests\HitPan.Watchdog.Tests.csproj"
if (Test-Path $testProj) {
    $testOut = & dotnet test $testProj -c Release --nologo --verbosity quiet 2>&1 | Out-String
    $pass = $testOut -match '통과!|Passed!'
    $count = if ($testOut -match '통과:\s*(\d+)') { $matches[1] } elseif ($testOut -match 'Passed:\s*(\d+)') { $matches[1] } else { '?' }
    Add-Check 'xUnit' "전체 PASS" $pass "$count 건 통과"
} else {
    Add-Check 'xUnit' "프로젝트 존재" $false $testProj
}

# ───────────── 3. 외부 endpoint smoke ─────────────
Write-Host "`n── 3. 외부 endpoint smoke ──" -ForegroundColor Yellow
$smokeScript = Join-Path $PSScriptRoot 'Smoke-ExternalEndpoints.ps1'
if (Test-Path $smokeScript) {
    & $smokeScript -WebHost $WebHost -ApiHost $ApiHost | Out-Null
    $smokeExitOk = ($LASTEXITCODE -eq 0)
    Add-Check 'Smoke' '8/8 PASS' $smokeExitOk "exit code $LASTEXITCODE"
} else {
    Add-Check 'Smoke' '스크립트 존재' $false $smokeScript
}

# ───────────── 4. 자동 시나리오 비파괴 3건 ─────────────
Write-Host "`n── 4. 자동 시나리오 비파괴 (S18·S19·S20) ──" -ForegroundColor Yellow
foreach ($sid in 'S18','S19','S20') {
    $script = Join-Path $PSScriptRoot "$sid.ps1"
    if (Test-Path $script) {
        $out = & $script -HealthUrl "https://$WebHost/health" 2>&1 | Out-String
        $hasBypass = $out -match 'bypassed: [1-9]|fail.*=[1-9]'
        Add-Check '시나리오' $sid (-not $hasBypass) $out.Trim().Split("`n")[0]
    } else {
        Add-Check '시나리오' $sid $false "스크립트 없음"
    }
}

# ───────────── 5. cloudflared config.yml 박제 ─────────────
Write-Host "`n── 5. cloudflared config.yml ──" -ForegroundColor Yellow
$configPath = "$env:USERPROFILE\.cloudflared\config.yml"
if (Test-Path $configPath) {
    $cfg = Get-Content $configPath -Raw
    Add-Check 'cloudflared' "$WebHost ingress" ($cfg -match "hostname:\s*$WebHost")
    Add-Check 'cloudflared' "$ApiHost ingress" ($cfg -match "hostname:\s*$ApiHost")
    Add-Check 'cloudflared' '404 fallback' ($cfg -match 'http_status:404')
} else {
    Add-Check 'cloudflared' 'config.yml 존재' $false $configPath
}

# ───────────── 6. 백신 4종 자동 예외 (보안 매니저 2 영역) ─────────────
Write-Host "`n── 6. 백신 4종 자동 예외 ──" -ForegroundColor Yellow
try {
    $defExclusions = (Get-MpPreference -ErrorAction SilentlyContinue).ExclusionPath
    $hasHitPan = $defExclusions -match 'HitPan'
    Add-Check '백신' 'Windows Defender HitPan 예외' ($null -ne $hasHitPan)
} catch {
    Add-Check '백신' 'Windows Defender 점검' $false $_.Exception.Message
}
$v3Key  = Test-Path 'HKLM:\SOFTWARE\AhnLab\V3Lite'
$alyKey = Test-Path 'HKLM:\SOFTWARE\ESTsoft\ALYac'
$nvKey  = Test-Path 'HKLM:\SOFTWARE\NAVER\Vaccine'
Add-Check '백신' 'V3 Lite 설치 감지'      $v3Key  "(설치 안 됐으면 매뉴얼 스킵 정합)"
Add-Check '백신' 'ALYac 설치 감지'         $alyKey "(설치 안 됐으면 매뉴얼 스킵 정합)"
Add-Check '백신' 'Naver Vaccine 설치 감지' $nvKey  "(설치 안 됐으면 매뉴얼 스킵 정합)"

# ───────────── 7. 빌드 영구 검증 (API + Web) ─────────────
Write-Host "`n── 7. 빌드 영구 검증 ──" -ForegroundColor Yellow
foreach ($proj in 'HitPan.API','HitPan.Web','HitPan.Watchdog') {
    $csproj = Join-Path $PSScriptRoot "..\..\src\$proj\$proj.csproj"
    if (Test-Path $csproj) {
        $buildOut = & dotnet build $csproj -c Release --nologo --verbosity quiet 2>&1 | Out-String
        $errors0 = $buildOut -match '오류 0개|0 Error'
        Add-Check '빌드' "$proj errors 0" $errors0
    }
}

# ───────────── 종합 ─────────────
$total  = $results.Count
$passed = ($results | Where-Object { $_.Pass }).Count
$failed = $total - $passed
$elapsed = ((Get-Date) - $start).TotalSeconds

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  종합: $passed / $total PASS  ($([int]$elapsed)초)" -ForegroundColor $(if ($failed -eq 0) { 'Green' } else { 'Yellow' })
Write-Host "========================================" -ForegroundColor Cyan

# JSON 리포트 박제
$reportDir = Join-Path $PSScriptRoot 'reports'
New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
$reportPath = Join-Path $reportDir "w1-gate-$(Get-Date -Format yyyyMMdd_HHmmss).json"
@{
    timestamp = (Get-Date).ToUniversalTime().ToString('o')
    total = $total
    passed = $passed
    failed = $failed
    elapsed_sec = [int]$elapsed
    results = $results
} | ConvertTo-Json -Depth 5 | Out-File $reportPath -Encoding utf8
Write-Host "`n📋 Report: $reportPath" -ForegroundColor Yellow

# W1 게이트 통과 기준: 빌드/xUnit/smoke 100% + 워치독·시나리오 ≥ 80%
if ($failed -eq 0) {
    Write-Host "`n✅ W1 게이트 통과 — 베타 1주차 발진 가능" -ForegroundColor Green
    exit 0
} else {
    Write-Host "`n⚠️ W1 게이트 미통과 — 실패 $failed 건 봉합 후 재실행" -ForegroundColor Red
    exit 1
}
