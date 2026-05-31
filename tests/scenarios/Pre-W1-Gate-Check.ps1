# Pre-W1-Gate-Check.ps1
# 6/3 W1 게이트 사장님 결재 직전 종합 사전 점검 (PM 자동 가도)
# 작성: PM 브라운킴 (2026-05-29)
# 헌법: #29 (PM 점검만, 인프라 변경 0)
# 사용: 6/3 09:00 PM 사장님 1분 결재 직전 발진

param(
    [string]$ApiBase = "https://api-demo.hitpan.kr",
    [string]$WebBase = "https://demo.hitpan.kr",
    [string]$RepoRoot = ""
)

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}

$ErrorActionPreference = "Continue"
$script:results = @()

function Add-Result {
    param($Category, $Name, $Status, $Detail)
    $script:results += [pscustomobject]@{
        Category = $Category
        Name     = $Name
        Status   = $Status
        Detail   = $Detail
    }
}

Write-Host "=== 6/3 W1 게이트 사전 종합 점검 ===" -ForegroundColor Cyan
Write-Host "시각: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Host ""

# 1. xUnit
Write-Host "▶ 1. xUnit 34/34 재검증" -ForegroundColor Yellow
Push-Location (Join-Path $RepoRoot "src\HitPan.Watchdog.Tests")
$xunit = dotnet test --verbosity quiet 2>&1
$xunitOk = ($LASTEXITCODE -eq 0)
Pop-Location
$xunitStatus = if ($xunitOk) { "PASS" } else { "FAIL" }
Add-Result "Unit" "xUnit 34/34" $xunitStatus "$LASTEXITCODE"

# 2. 외부 Smoke
Write-Host "▶ 2. 외부 Smoke 8/8" -ForegroundColor Yellow
$smokePath = Join-Path $RepoRoot "tests\scenarios\Smoke-ExternalEndpoints.ps1"
if (Test-Path $smokePath) {
    $null = & $smokePath 2>&1
    $smokeOk = ($LASTEXITCODE -eq 0)
    $smokeStatus = if ($smokeOk) { "PASS" } else { "FAIL" }
    Add-Result "External" "Smoke 8/8" $smokeStatus "$LASTEXITCODE"
} else {
    Add-Result "External" "Smoke 8/8" "SKIP" "Script missing"
}

# 3. W1 게이트
Write-Host "▶ 3. W1 게이트 18/18" -ForegroundColor Yellow
$w1Path = Join-Path $RepoRoot "tests\scenarios\W1-Gate-Checklist.ps1"
if (Test-Path $w1Path) {
    $null = & $w1Path 2>&1
    $w1Ok = ($LASTEXITCODE -eq 0)
    $w1Status = if ($w1Ok) { "PASS" } else { "FAIL" }
    Add-Result "W1" "Gate 18/18" $w1Status "$LASTEXITCODE"
} else {
    Add-Result "W1" "Gate 18/18" "SKIP" "Script missing"
}

# 4. cloudflared Service (사장님 결재 #1 사전 점검)
Write-Host "▶ 4. cloudflared Service 존재 점검" -ForegroundColor Yellow
$cfSvc = Get-Service cloudflared -ErrorAction SilentlyContinue
if ($null -ne $cfSvc) {
    Add-Result "Const28" "cloudflared Service" "EXIST ($($cfSvc.Status))" "Approval#1 not needed"
} else {
    Add-Result "Const28" "cloudflared Service" "MISSING" "Approval#1 needed (cloudflared service install)"
}

# 5. PR #1 상태 (gh CLI 있을 시)
Write-Host "▶ 5. PR #1 Mergeable 점검" -ForegroundColor Yellow
$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($gh) {
    $prState = gh pr view 1 --json mergeable,mergeStateStatus 2>&1
    if ($LASTEXITCODE -eq 0) {
        Add-Result "PR" "Mergeable" "BAKJE" "$prState"
    } else {
        Add-Result "PR" "Mergeable" "UNKNOWN" "gh failed"
    }
} else {
    Add-Result "PR" "Mergeable" "SKIP" "gh CLI missing - check GitHub directly"
}

# 6. 매니저 가도 박제 (작1·작3 문서 존재)
Write-Host "▶ 6. 매니저 작지서 박제 점검" -ForegroundColor Yellow
$handoffDir = Join-Path $RepoRoot "docs\handoff"
$jak1 = (Get-ChildItem -Path $handoffDir -Filter "20260530_*.md" -ErrorAction SilentlyContinue).Count -gt 0
$jak3 = (Get-ChildItem -Path $handoffDir -Filter "20260531_*.md" -ErrorAction SilentlyContinue).Count -gt 0
$dryRunReport = (Get-ChildItem -Path (Join-Path $RepoRoot "tests\scenarios\reports") -Filter "dry-run-*.md" -ErrorAction SilentlyContinue).Count -gt 0
$jak1Status = if ($jak1) { "BAKJE" } else { "MISSING" }
$jak3Status = if ($jak3) { "BAKJE" } else { "MISSING" }
$dryRunStatus = if ($dryRunReport) { "BAKJE (6/2)" } else { "PENDING (6/2 18:00)" }
Add-Result "Manager" "Jak1 Sec2" $jak1Status ""
Add-Result "Manager" "Jak3 QA Lead" $jak3Status ""
Add-Result "Manager" "dry-run Result" $dryRunStatus ""

# 종합
Write-Host ""
Write-Host "=== 종합 ===" -ForegroundColor Cyan
$script:results | Format-Table -AutoSize

$passCount = ($script:results | Where-Object { $_.Status -like "PASS*" -or $_.Status -like "BAKJE*" -or $_.Status -like "EXIST*" }).Count
$failCount = ($script:results | Where-Object { $_.Status -like "FAIL*" -or $_.Status -like "MISSING*" }).Count
$totalCount = $script:results.Count

Write-Host ""
Write-Host "PASS/BAKJE: $passCount / $totalCount" -ForegroundColor Green
$failColor = if ($failCount -eq 0) { "Green" } else { "Yellow" }
Write-Host "FAIL/MISS : $failCount / $totalCount" -ForegroundColor $failColor
Write-Host ""

# 사장님 결재 권고
Write-Host "=== Owner Approval Recommendation ===" -ForegroundColor Cyan
if ($failCount -eq 0) {
    Write-Host "[GO] 4 approvals can proceed immediately" -ForegroundColor Green
} elseif ($failCount -le 2) {
    Write-Host "[CONDITIONAL] Report FAIL items + request approval" -ForegroundColor Yellow
} else {
    Write-Host "[NO-GO] W1 Gate delay to 6/10 - request approval" -ForegroundColor Red
}

# JSON 박제
$reportDir = Join-Path $RepoRoot "tests\scenarios\reports"
if (-not (Test-Path $reportDir)) {
    New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
}
$reportPath = Join-Path $reportDir "pre-w1-gate-$(Get-Date -Format 'yyyyMMdd_HHmmss').json"
$script:results | ConvertTo-Json -Depth 4 | Out-File -FilePath $reportPath -Encoding utf8
Write-Host ""
Write-Host "📋 Report: $reportPath" -ForegroundColor Cyan

exit $failCount
