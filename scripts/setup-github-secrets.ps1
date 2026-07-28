# 히트판 ERP — GitHub Actions Secrets 일괄 등록 스크립트
# 사용: 사장님이 PowerShell에서 1회 실행
#   1) gh CLI 설치: winget install GitHub.cli
#   2) gh auth login (브라우저 인증 1회)
#   3) .\scripts\setup-github-secrets.ps1
#
# 사전 조건:
#   - secrets/.generated-secrets-20260611.txt 존재
#   - 현재 디렉토리가 hitpan-erp 루트
#
# 헌법 #29: PM은 gh secret set 직접 실행 불가. 사장님 영역.

$ErrorActionPreference = "Stop"

$secretFile = "secrets/.generated-secrets-20260611.txt"
if (-not (Test-Path $secretFile)) {
    Write-Error "$secretFile 파일이 없습니다. 먼저 시크릿 생성 필요."
    exit 1
}

# gh CLI 확인
$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    Write-Error "gh CLI 미설치. winget install GitHub.cli 후 재시도."
    exit 1
}

# 인증 확인
gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error "gh 미인증. 'gh auth login' 먼저 실행하세요."
    exit 1
}

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  GitHub Actions Secrets 등록" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

$secrets = @{}
Get-Content $secretFile | ForEach-Object {
    if ($_ -match "^([A-Z_][A-Z0-9_]*)\s*=\s*(.+)$") {
        $secrets[$matches[1]] = $matches[2]
    }
}

Write-Host ""
Write-Host "  등록 대상 ($($secrets.Count)건):" -ForegroundColor Yellow
foreach ($key in $secrets.Keys) {
    Write-Host "    - $key"
}

Write-Host ""
$confirm = Read-Host "GitHub Repo Secrets로 등록하시겠습니까? (y/N)"
if ($confirm -ne "y") {
    Write-Host "중단됨." -ForegroundColor Yellow
    exit 0
}

foreach ($key in $secrets.Keys) {
    Write-Host "  > $key 등록..." -NoNewline
    $secrets[$key] | gh secret set $key
    if ($LASTEXITCODE -eq 0) {
        Write-Host " OK" -ForegroundColor Green
    } else {
        Write-Host " 실패" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  완료. gh secret list 로 확인 가능" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
