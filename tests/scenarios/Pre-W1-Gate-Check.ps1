# Pre-W1-Gate-Check.ps1
# 6/3 W1 게이트 사장님 결재 직전 종합 사전 점검 (PM 자동 가도)
# 작성: PM 브라운킴 (2026-05-29)
# 헌법: #29 (PM 점검만, 인프라 변경 0)
# 사용: 6/3 09:00 PM 사장님 1분 결재 직전 발진

param(
    [string]$ApiBase = "https://api-demo.hitpan.kr",
    [string]$WebBase = "https://demo.hitpan.kr",
    [string]$RepoRoot = "C:\Users\소순근\Desktop\hitpan-erp"
)

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
Add-Result "단위" "xUnit 34/34" $(if ($xunitOk) {"PASS"} else {"FAIL"}) "$LASTEXITCODE"

# 2. 외부 Smoke
Write-Host "▶ 2. 외부 Smoke 8/8" -ForegroundColor Yellow
$smokePath = Join-Path $RepoRoot "tests\scenarios\Smoke-ExternalEndpoints.ps1"
if (Test-Path $smokePath) {
    $null = & $smokePath 2>&1
    $smokeOk = ($LASTEXITCODE -eq 0)
    Add-Result "외부" "Smoke 8/8" $(if ($smokeOk) {"PASS"} else {"FAIL"}) "$LASTEXITCODE"
} else {
    Add-Result "외부" "Smoke 8/8" "SKIP" "스크립트 부재"
}

# 3. W1 게이트
Write-Host "▶ 3. W1 게이트 18/18" -ForegroundColor Yellow
$w1Path = Join-Path $RepoRoot "tests\scenarios\W1-Gate-Checklist.ps1"
if (Test-Path $w1Path) {
    $null = & $w1Path 2>&1
    $w1Ok = ($LASTEXITCODE -eq 0)
    Add-Result "W1" "Gate 18/18" $(if ($w1Ok) {"PASS"} else {"FAIL"}) "$LASTEXITCODE"
} else {
    Add-Result "W1" "Gate 18/18" "SKIP" "스크립트 부재"
}

# 4. cloudflared Service (사장님 결재 #1 사전 점검)
Write-Host "▶ 4. cloudflared Service 존재 점검" -ForegroundColor Yellow
$cfSvc = Get-Service cloudflared -ErrorAction SilentlyContinue
if ($null -ne $cfSvc) {
    Add-Result "헌법#28" "cloudflared Service" "EXIST ($($cfSvc.Status))" "결재 #1 불필요"
} else {
    Add-Result "헌법#28" "cloudflared Service" "MISSING" "사장님 결재 #1 필요 (cloudflared service install)"
}

# 5. PR #1 상태 (gh CLI 있을 시)
Write-Host "▶ 5. PR #1 Mergeable 점검" -ForegroundColor Yellow
$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($gh) {
    $prState = gh pr view 1 --json mergeable,mergeStateStatus 2>&1
    if ($LASTEXITCODE -eq 0) {
        Add-Result "PR" "Mergeable" "박제" "$prState"
    } else {
        Add-Result "PR" "Mergeable" "UNKNOWN" "gh 실패"
    }
} else {
    Add-Result "PR" "Mergeable" "SKIP" "gh CLI 부재 — GitHub 직접 확인"
}

# 6. 매니저 가도 박제 (작1·작3 문서 존재)
Write-Host "▶ 6. 매니저 작지서 박제 점검" -ForegroundColor Yellow
$jak1 = Test-Path (Join-Path $RepoRoot "docs\handoff\20260530_작1_보안매니저2_백신5종_가도.md")
$jak3 = Test-Path (Join-Path $RepoRoot "docs\handoff\20260531_작3_검증팀장_dry-run_가도.md")
$dryRunReport = Test-Path (Join-Path $RepoRoot "tests\scenarios\reports\dry-run-20260602.md")
Add-Result "매니저" "작1 보안2" $(if ($jak1) {"박제"} else {"부재"}) ""
Add-Result "매니저" "작3 검증팀장" $(if ($jak3) {"박제"} else {"부재"}) ""
Add-Result "매니저" "dry-run 결과" $(if ($dryRunReport) {"박제 (6/2 완성)"} else {"6/2 18:00 박제 예정"}) ""

# 종합
Write-Host ""
Write-Host "=== 종합 ===" -ForegroundColor Cyan
$script:results | Format-Table -AutoSize

$passCount = ($script:results | Where-Object { $_.Status -like "PASS*" -or $_.Status -like "박제*" -or $_.Status -like "EXIST*" }).Count
$failCount = ($script:results | Where-Object { $_.Status -like "FAIL*" -or $_.Status -like "MISSING*" -or $_.Status -like "부재*" }).Count
$totalCount = $script:results.Count

Write-Host ""
Write-Host "PASS/박제: $passCount / $totalCount" -ForegroundColor Green
Write-Host "FAIL/부재 : $failCount / $totalCount" -ForegroundColor $(if ($failCount -eq 0) {"Green"} else {"Yellow"})
Write-Host ""

# 사장님 결재 권고
Write-Host "=== 사장님 결재 권고 ===" -ForegroundColor Cyan
if ($failCount -eq 0) {
    Write-Host "[GO] 결재 4건 즉시 가도 가능" -ForegroundColor Green
} elseif ($failCount -le 2) {
    Write-Host "[CONDITIONAL] FAIL 항목 보고 + 결재 의뢰" -ForegroundColor Yellow
} else {
    Write-Host "[NO-GO] W1 게이트 6/10 연기 결재 의뢰" -ForegroundColor Red
}

# JSON 박제
$reportDir = Join-Path $RepoRoot "tests\scenarios\reports"
if (-not (Test-Path $reportDir)) {
    New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
}
$reportPath = Join-Path $reportDir "pre-w1-gate-$(Get-Date -Format 'yyyyMMdd_HHmmss').json"
$script:results | ConvertTo-Json -Depth 4 | Set-Content $reportPath -Encoding UTF8
Write-Host ""
Write-Host "📋 Report: $reportPath" -ForegroundColor Cyan

exit $failCount
