# ============================================================
# HitPan ERP 범용 인스톨러 빌드 스크립트
# 사장님 결재 Plan 2026-06-09
#
# 기존 build-installer.ps1과의 차이:
#   - 기존  : 고객 1명당 EXE 1개 (TenantId·Token 빌드 시점 주입)
#   - 범용  : 모든 고객 동일 EXE, 시리얼 입력으로 자동 설정 ⭐ Plan 정합
#
# 사용법:
#   .\build-installer-universal.ps1
#   .\build-installer-universal.ps1 -Version 1.1.1 -BackofficeApi https://back.hitpan.kr
#
# CI/CD 정합:
#   GitHub Actions에서 호출: build-installer-universal.ps1 -Version $env:GITHUB_REF_NAME
# ============================================================

param(
    [string]$Version = "1.1.0",
    [string]$BackofficeApi = "https://back.hitpan.kr",
    [string]$OutputDir = "dist",
    [string]$BundleDir = "installer-build/bundle",
    [switch]$SkipBundleDownload,
    [switch]$SkipErpBuild,
    [switch]$SkipWatchdog
)

$ErrorActionPreference = "Stop"
$startTime = Get-Date

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  HitPan ERP 범용 인스톨러 빌드 v$Version" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Inno Setup 6 탐색
$isccCandidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$isccPath = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $isccPath) {
    Write-Error "Inno Setup 6가 설치되어 있지 않습니다.`n  https://jrsoftware.org/isdl.php 에서 설치 후 재시도."
    exit 1
}
Write-Host "  ISCC:           $isccPath"
Write-Host "  Version:        $Version"
Write-Host "  BackofficeApi:  $BackofficeApi"
Write-Host "  BundleDir:      $BundleDir"
Write-Host "  OutputDir:      $OutputDir"
Write-Host ""

$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $projectRoot

# ─── 1. 의존성 번들 ───────────────────────────────
Write-Host "[1/5] 의존성 번들 확인..." -ForegroundColor Yellow

if (-not (Test-Path $BundleDir)) {
    New-Item -ItemType Directory -Path $BundleDir -Force | Out-Null
}

if (-not $SkipBundleDownload) {
    $bundleScript = Join-Path $PSScriptRoot "download-bundle.ps1"
    if (Test-Path $bundleScript) {
        Write-Host "  → download-bundle.ps1 실행 중..."
        & $bundleScript -BundleDir $BundleDir
        if ($LASTEXITCODE -ne 0) {
            Write-Error "번들 다운로드 실패."
            exit 1
        }
    } else {
        Write-Warning "  download-bundle.ps1 존재하지 않음. -SkipBundleDownload로 우회."
    }
}

$requiredBundle = @("dotnet-hosting.exe", "mariadb.msi", "vc_redist.x64.exe", "cloudflared.exe")
$missing = $requiredBundle | Where-Object { -not (Test-Path "$BundleDir/$_") }
if ($missing.Count -gt 0) {
    Write-Error "번들에 존재하지 않는 파일:`n  $($missing -join "`n  ")"
    exit 1
}
Write-Host "  ✅ 의존성 번들 OK (4개 파일)" -ForegroundColor Green

# ─── 2. ERP 산출물 빌드 ──────────────────────────
Write-Host ""
Write-Host "[2/5] ERP 산출물 빌드..." -ForegroundColor Yellow

if (-not $SkipErpBuild) {
    Write-Host "  → HitPan.API publish..."
    dotnet publish src/HitPan.API/HitPan.API.csproj -c Release -o "$BundleDir/api" --nologo 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Error "HitPan.API publish 실패."; exit 1 }

    Write-Host "  → HitPan.Web publish..."
    dotnet publish src/HitPan.Web/HitPan.Web.csproj -c Release -o "$BundleDir/web" --nologo 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Error "HitPan.Web publish 실패."; exit 1 }

    Write-Host "  ✅ ERP 산출물 빌드 완료" -ForegroundColor Green
} else {
    Write-Host "  ⏭ ERP 빌드 건너뜀 (-SkipErpBuild)"
}

# ─── 3. 워치독 빌드 (Day 7에 추가될 영역, 미존재 시 건너뜀) ─────
Write-Host ""
Write-Host "[3/5] 워치독 빌드..." -ForegroundColor Yellow

if (-not $SkipWatchdog -and (Test-Path "src/HitPan.Watchdog/HitPan.Watchdog.csproj")) {
    Write-Host "  → HitPan.Watchdog publish..."
    dotnet publish src/HitPan.Watchdog/HitPan.Watchdog.csproj `
        -c Release -o "$BundleDir/watchdog" -r win-x64 --self-contained true `
        --nologo 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Error "HitPan.Watchdog publish 실패."; exit 1 }
    Write-Host "  ✅ 워치독 빌드 완료" -ForegroundColor Green
} else {
    Write-Host "  ⏭ 워치독 프로젝트 없음 (Day 7에 추가될 영역)"
    # placeholder 생성 (Inno Setup이 Source 라인을 건너뜀)
    if (-not (Test-Path "$BundleDir/watchdog")) {
        New-Item -ItemType Directory -Path "$BundleDir/watchdog" -Force | Out-Null
    }
}

# ─── 4. DB 덤프 ──────────────────────────────────
Write-Host ""
Write-Host "[4/5] DB 덤프..." -ForegroundColor Yellow

# 봉합 (2026-06-23, 5차 전수조사 SHIP-DDL-01 P1→P0급): 종전엔 universal 본류 빌드가 소스로
#   installer/hitpan_db.sql(38MB 실데이터 덤프 — 재고원장·users·본사 bo_permissions·거래명세서 등
#   헌법 #18/#22 출하 금지 데이터)을 쓰고 게이트는 금지구문(DB명/DEFINER) 1개뿐이라, 본사·실데이터가
#   고객 PC로 출하될 수 있었다. build-installer.ps1(구세대)에만 있던 무결성 게이트 4단을 본류로 이식하고,
#   소스를 검증된 빈 스키마 정본(hitpan_db_clean.sql)으로 통일한다(단일 진실원, 헌법 #19/#35).
$dbDumpSrc = "installer/hitpan_db_clean.sql"   # 123 구조 + 데이터0 + common_codes 시드 (git 추적 정본, 2026-06-29 local_update_status 편입 122→123)
$dbDumpDst = "$BundleDir/hitpan_db.sql"

if (-not (Test-Path $dbDumpSrc)) {
    throw "  ❌ installer/hitpan_db_clean.sql 없음. 빈 스키마 정본부터 준비하라 (mariadb-dump --no-data --triggers)."
}

$sql = Get-Content $dbDumpSrc -Raw -Encoding UTF8

# ── 게이트 1: 백도어 테스트계정 0건 ──
$backdoorHits = ([regex]::Matches($sql, 'admin@hitpan\.kr|reseller@hitpan\.kr|tenant@hitpan\.kr|Admin1234')).Count
if ($backdoorHits -gt 0) { throw "  ❌ 게이트1 실패: 백도어 테스트계정 $backdoorHits 건. 출시 차단." }

# ── 게이트 2: 금지 구문(USE/CREATE DATABASE/DEFINER) 0건 (회사별 DB import 깨짐 방지, 로그인 500 진범) ──
$sqlNoComments = ($sql -split "`n" | Where-Object { $_ -notmatch '^\s*--' }) -join "`n"
$banHits = ([regex]::Matches($sqlNoComments, '(?im)^\s*USE\s|CREATE\s+DATABASE|DROP\s+DATABASE|ALTER\s+DATABASE|DEFINER\s*=')).Count
if ($banHits -gt 0) { throw "  ❌ 게이트2 실패: 금지 구문(USE/CREATE DATABASE/DEFINER) $banHits 건. 출시 차단." }

# ── 게이트 3: 구조 보존 — 테이블 수(123) + 트리거(3) ── (2026-06-29 local_update_status 편입 122→123)
$tableCount   = ([regex]::Matches($sql, 'CREATE TABLE')).Count
$triggerCount = ([regex]::Matches($sql, '(?im)CREATE.*TRIGGER|50003 .*TRIGGER')).Count
if ($tableCount -ne 123) { throw "  ❌ 게이트3 실패: 구조 불일치 — 기대 123 테이블 ≠ $tableCount. 출시 차단." }
if ($triggerCount -lt 3) { throw "  ❌ 게이트3 실패: 트리거 누락 — 기대 3 ≠ $triggerCount. 출시 차단." }

# ── 게이트 4: 데이터 0 — 개발/실데이터 미혼입 (common_codes 코드성 시드만 허용) ──
$insertCount = ([regex]::Matches($sql, '(?im)^\s*INSERT\s+INTO')).Count
$ccInsert    = ([regex]::Matches($sql, '(?im)INSERT\s+INTO\s+`?common_codes')).Count
if (($insertCount - $ccInsert) -gt 0) { throw "  ❌ 게이트4 실패: common_codes 외 데이터 INSERT $($insertCount - $ccInsert) 건 혼입(실데이터 의심). 출시 차단." }

Copy-Item $dbDumpSrc $dbDumpDst -Force
$size = (Get-Item $dbDumpDst).Length
Write-Host "  ✅ DB 빈 스키마 게이트 4/4 통과 — 구조보존(테이블 $tableCount·트리거 $triggerCount), 데이터0, 백도어0 ($([Math]::Round($size/1KB, 0)) KB)" -ForegroundColor Green

# ─── 5. Inno Setup 컴파일 ────────────────────────
Write-Host ""
Write-Host "[5/5] Inno Setup 컴파일..." -ForegroundColor Yellow

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$outputName = "HitPan-ERP-Setup-$Version"
$bundleAbsPath = (Resolve-Path $BundleDir).Path
$outputAbsPath = (Resolve-Path $OutputDir).Path

$isccArgs = @(
    "installer/HitPan-Universal.iss",
    "/DAppVersion=$Version",
    "/DBackofficeApi=$BackofficeApi",
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
    Write-Error "출력 EXE 존재하지 않음: $outputExe"
    exit 1
}

$exeSize = [Math]::Round((Get-Item $outputExe).Length / 1MB, 1)
$elapsed = ((Get-Date) - $startTime).TotalSeconds

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  ✅ 빌드 완료 ($($elapsed.ToString('F1'))초)" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "  EXE:        $outputExe"
Write-Host "  크기:       $exeSize MB"
Write-Host "  Version:    $Version"
Write-Host "  Type:       범용 (모든 고객 사용 가능)"
Write-Host ""
Write-Host "  ⚠ 다음 단계:"
Write-Host "  1. 클린 PC에서 EXE 시험"
Write-Host "  2. 시리얼 입력 → 자동 설정 확인"
Write-Host "  3. CI/CD = GitHub Releases 업로드"
Write-Host ""
Write-Host "  ⚠ EXE를 Git에 커밋하지 마세요"
Write-Host ""
