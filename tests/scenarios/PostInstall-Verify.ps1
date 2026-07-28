# =================================================================
# PostInstall-Verify.ps1 — 설치 후 자가 진단
# 사장님 결재 Plan 2026-06-09 (Day 15~17 다른 PC 시험 도구)
#
# 사용:
#   .\PostInstall-Verify.ps1 -ExpectedDomain 'mycompany.hitpan.kr'
# =================================================================
param(
    [Parameter(Mandatory=$true)]
    [string]$ExpectedDomain
)

$ErrorActionPreference = 'Continue'
$passed = 0
$failed = 0
$report = @()

function Test-Item($name, $check) {
    try {
        $result = & $check
        if ($result) {
            Write-Host "[ OK ] $name" -ForegroundColor Green
            $script:passed++
            $script:report += @{ name = $name; status = 'pass' }
        } else {
            Write-Host "[FAIL] $name" -ForegroundColor Red
            $script:failed++
            $script:report += @{ name = $name; status = 'fail' }
        }
    } catch {
        Write-Host "[ERR ] $name : $($_.Exception.Message)" -ForegroundColor Red
        $script:failed++
        $script:report += @{ name = $name; status = 'error'; message = $_.Exception.Message }
    }
}

Write-Host "`n=== HitPan ERP 설치 후 자가 진단 ===`n" -ForegroundColor Cyan

# 1. 환경변수
Test-Item "환경변수 HITPAN_TENANT_CODE 박힘" { [Environment]::GetEnvironmentVariable('HITPAN_TENANT_CODE','Machine') -ne $null }
Test-Item "환경변수 HITPAN_PRIMARY_DOMAIN = $ExpectedDomain" { [Environment]::GetEnvironmentVariable('HITPAN_PRIMARY_DOMAIN','Machine') -eq $ExpectedDomain }
Test-Item "환경변수 HITPAN_INSTALL_DIR 박힘" { Test-Path ([Environment]::GetEnvironmentVariable('HITPAN_INSTALL_DIR','Machine')) }

# 2. Windows 서비스
Test-Item "MariaDB 서비스 Running" { (Get-Service -Name 'MariaDB' -ErrorAction SilentlyContinue).Status -eq 'Running' }
Test-Item "cloudflared 서비스 Running" { (Get-Service -Name 'cloudflared' -ErrorAction SilentlyContinue).Status -eq 'Running' }
Test-Item "HitPan-Watchdog 서비스 Running" { (Get-Service -Name 'HitPan-Watchdog' -ErrorAction SilentlyContinue).Status -eq 'Running' }

# 3. 포트
Test-Item "MariaDB 3306 LISTEN" { (Get-NetTCPConnection -LocalPort 3306 -State Listen -ErrorAction SilentlyContinue) -ne $null }
Test-Item "ERP API 5257 LISTEN" { (Get-NetTCPConnection -LocalPort 5257 -State Listen -ErrorAction SilentlyContinue) -ne $null }

# 4. 방화벽 규칙
Test-Item "방화벽: cloudflared UDP 7844" { (Get-NetFirewallRule -DisplayName '*cloudflared*' -ErrorAction SilentlyContinue) -ne $null }
Test-Item "방화벽: ERP API TCP 5257" { (Get-NetFirewallRule -DisplayName '*HitPan*API*' -ErrorAction SilentlyContinue) -ne $null }

# 5. 통신
Test-Item "외부 도메인 HTTP 200: $ExpectedDomain" {
    try {
        $r = Invoke-WebRequest -Uri "https://$ExpectedDomain" -UseBasicParsing -TimeoutSec 10
        $r.StatusCode -eq 200
    } catch { $false }
}
Test-Item "로컬 /health 200" {
    try {
        $r = Invoke-WebRequest -Uri 'http://localhost:5257/health' -UseBasicParsing -TimeoutSec 5
        $r.StatusCode -eq 200
    } catch { $false }
}

# 6. 백신 예외
Test-Item "Defender 예외: cloudflared" {
    $excl = Get-MpPreference -ErrorAction SilentlyContinue
    if ($excl) { $excl.ExclusionProcess -contains 'cloudflared.exe' } else { $true }
}

# 7. 워치독 로그
$installDir = [Environment]::GetEnvironmentVariable('HITPAN_INSTALL_DIR','Machine')
Test-Item "워치독 로그 파일 박힘" {
    Test-Path "$installDir\logs\watchdog.log"
}

# 결과 출력
Write-Host "`n=== 결과 ===" -ForegroundColor Cyan
Write-Host "통과: $passed" -ForegroundColor Green
Write-Host "실패: $failed" -ForegroundColor $(if ($failed -eq 0) { 'Green' } else { 'Red' })

# JSON 보고서
$reportPath = "$PSScriptRoot\reports\post-install-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
$reportDir = Split-Path $reportPath -Parent
if (-not (Test-Path $reportDir)) { New-Item -ItemType Directory -Path $reportDir -Force | Out-Null }
@{
    timestamp = (Get-Date).ToString('o')
    expectedDomain = $ExpectedDomain
    passed = $passed
    failed = $failed
    items = $report
} | ConvertTo-Json -Depth 4 | Set-Content -Path $reportPath -Encoding UTF8
Write-Host "보고서: $reportPath`n" -ForegroundColor Cyan

exit $failed
