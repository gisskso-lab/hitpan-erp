# ============================================================
# HitPan ERP 인스톨러 빌드 자동화 스크립트
# WS-20260428-02 (Gard 3) — 옵션 X2 + X1 갈아타기 디딤돌
#
# 사용법:
#   .\build-installer.ps1 -TenantId tenant-001 -Token "eyJh..."
#
# 동작:
#   1. 의존성 번들 캐시 확인 (없으면 다운로드)
#   2. ERP 산출물 빌드 (.NET publish)
#   3. DB 덤프 복사
#   4. Inno Setup 컴파일 (토큰 사전 주입)
#   5. dist/HitPan-Setup-{TenantId}.exe 출력
# ============================================================

param(
    [Parameter(Mandatory)]
    [string]$TenantId,

    [Parameter(Mandatory)]
    [string]$Token,

    [string]$Version = "1.0.7",
    [string]$OutputDir = "dist",
    [string]$BundleDir = "installer-build/bundle",
    [switch]$SkipBundleDownload,
    [switch]$SkipErpBuild
)

$ErrorActionPreference = "Stop"
$startTime = Get-Date

# ─── 사전 검증 ────────────────────────────────────
Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  HitPan ERP 인스톨러 빌드 ($TenantId)" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Token 형식 검증 (eyJ로 시작)
if (-not $Token.StartsWith("eyJ")) {
    Write-Error "Token 형식이 잘못되었습니다. 'eyJ'로 시작해야 합니다."
    exit 1
}

# TenantId 형식 검증
if ($TenantId -notmatch '^[a-z0-9\-]+$') {
    Write-Error "TenantId는 소문자·숫자·하이픈만 허용. 입력: $TenantId"
    exit 1
}

# Inno Setup 6 설치 확인
$isccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $isccPath)) {
    Write-Error "Inno Setup 6 가 설치되지 않았습니다.`n  https://jrsoftware.org/isdl.php 에서 설치 후 재시도."
    exit 1
}

# 작업 디렉토리 = 프로젝트 루트
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $projectRoot

Write-Host "  TenantId:   $TenantId"
Write-Host "  Version:    $Version"
Write-Host "  BundleDir:  $BundleDir"
Write-Host "  OutputDir:  $OutputDir"
Write-Host "  Token:      $($Token.Substring(0, [Math]::Min(10, $Token.Length)))... (길이: $($Token.Length))"
Write-Host ""

# ─── 1. 번들 디렉토리 준비 ────────────────────────
Write-Host "[1/4] 번들 디렉토리 확인..." -ForegroundColor Yellow

if (-not (Test-Path $BundleDir)) {
    New-Item -ItemType Directory -Path $BundleDir -Force | Out-Null
}

if (-not $SkipBundleDownload) {
    $bundleScript = Join-Path $PSScriptRoot "download-bundle.ps1"
    if (Test-Path $bundleScript) {
        Write-Host "  → download-bundle.ps1 실행 (의존성 캐시)..."
        & $bundleScript -BundleDir $BundleDir
        if ($LASTEXITCODE -ne 0) {
            Write-Error "번들 다운로드 실패."
            exit 1
        }
    } else {
        Write-Warning "download-bundle.ps1 없음. -SkipBundleDownload로 우회 가능."
        Write-Host "  → 수동으로 번들 디렉토리에 다음 파일이 있어야 합니다:"
        Write-Host "    · dotnet-hosting.exe"
        Write-Host "    · mariadb.msi"
        Write-Host "    · vc_redist.x64.exe"
        Write-Host "    · cloudflared.exe"
    }
}

# 필수 파일 검증
$requiredBundleFiles = @(
    "dotnet-hosting.exe",
    "mariadb.msi",
    "vc_redist.x64.exe",
    "cloudflared.exe"
)
$missing = @()
foreach ($f in $requiredBundleFiles) {
    if (-not (Test-Path "$BundleDir/$f")) {
        $missing += $f
    }
}
if ($missing.Count -gt 0) {
    Write-Error "번들 디렉토리에 누락 파일:`n  $($missing -join "`n  ")"
    exit 1
}
Write-Host "  ✅ 의존성 번들 OK (4개 파일)" -ForegroundColor Green

# ─── 2. ERP 산출물 빌드 ─────────────────────────
Write-Host ""
Write-Host "[2/4] ERP 산출물 빌드..." -ForegroundColor Yellow

if (-not $SkipErpBuild) {
    # API publish
    Write-Host "  → HitPan.API publish..."
    dotnet publish src/HitPan.API/HitPan.API.csproj `
        -c Release `
        -o "$BundleDir/api" `
        --nologo 2>&1 | Out-Null

    if ($LASTEXITCODE -ne 0) {
        Write-Error "HitPan.API publish 실패."
        exit 1
    }

    # Web publish
    Write-Host "  → HitPan.Web publish..."
    dotnet publish src/HitPan.Web/HitPan.Web.csproj `
        -c Release `
        -o "$BundleDir/web" `
        --nologo 2>&1 | Out-Null

    if ($LASTEXITCODE -ne 0) {
        Write-Error "HitPan.Web publish 실패."
        exit 1
    }

    Write-Host "  ✅ ERP 산출물 빌드 완료" -ForegroundColor Green
} else {
    Write-Host "  ⏭ ERP 빌드 건너뜀 (-SkipErpBuild)"
}

# ─── 3. DB 덤프 준비 ─────────────────────────────
Write-Host ""
Write-Host "[3/4] DB 덤프 준비..." -ForegroundColor Yellow

$dbDumpSrc = "installer/hitpan_db.sql"
$dbDumpDst = "$BundleDir/hitpan_db.sql"

if (Test-Path $dbDumpSrc) {
    Copy-Item $dbDumpSrc $dbDumpDst -Force
    $size = (Get-Item $dbDumpDst).Length
    Write-Host "  ✅ DB 덤프 복사 ($([Math]::Round($size/1MB, 2)) MB)" -ForegroundColor Green
} else {
    Write-Warning "  ⚠ installer/hitpan_db.sql 없음. 빈 DB로 설치됨."
    Set-Content -Path $dbDumpDst -Value "-- empty db dump (placeholder)" -Encoding UTF8
}

# ─── 4. Inno Setup 컴파일 ────────────────────────
Write-Host ""
Write-Host "[4/4] Inno Setup 컴파일..." -ForegroundColor Yellow

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$outputName = "HitPan-Setup-$TenantId"
$bundleAbsPath = (Resolve-Path $BundleDir).Path
$outputAbsPath = (Resolve-Path $OutputDir).Path

# ISCC 인자 구성 (Gard 1: 토큰 명령행 주입)
$isccArgs = @(
    "installer/HitPan.iss",
    "/DAppVersion=$Version",
    "/DTunnelToken=$Token",
    "/DTenantId=$TenantId",
    "/DOutputName=$outputName",
    "/DOutputDir=`"$outputAbsPath`"",
    "/DBundleDir=`"$bundleAbsPath`"",
    "/Q"
)

Write-Host "  → ISCC.exe 호출..."
& $isccPath $isccArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup 컴파일 실패."
    exit 1
}

$outputExe = Join-Path $outputAbsPath "$outputName.exe"
if (-not (Test-Path $outputExe)) {
    Write-Error "출력 EXE를 찾을 수 없음: $outputExe"
    exit 1
}

$exeSize = [Math]::Round((Get-Item $outputExe).Length / 1MB, 1)
$elapsed = ((Get-Date) - $startTime).TotalSeconds

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  ✅ 빌드 완료 ($($elapsed.ToString('F1'))초)" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "  EXE:    $outputExe"
Write-Host "  크기:   $exeSize MB"
Write-Host "  Tenant: $TenantId"
Write-Host ""
Write-Host "  ⚠ 다음 단계:"
Write-Host "  1. 클린 PC에서 EXE 테스트"
Write-Host "  2. Cloudflare R2 업로드 (또는 이메일 직접 발송)"
Write-Host "  3. 베타 고객에게 다운로드 링크 전달"
Write-Host ""
Write-Host "  ⚠ EXE를 Git에 커밋하지 마세요 (.gitignore 박힘)"
Write-Host ""
