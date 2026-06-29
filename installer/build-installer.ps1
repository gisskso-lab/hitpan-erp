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

# Inno Setup 6 설치 확인 (관리자/사용자 양쪽 경로 탐색)
$isccCandidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$isccPath = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $isccPath) {
    Write-Error "Inno Setup 6 가 설치되지 않았습니다.`n  https://jrsoftware.org/isdl.php 에서 설치 후 재시도."
    exit 1
}
Write-Host "  ISCC:       $isccPath"

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

# 사장님 원칙: 개발한 ERP가 설치본에서 그대로 작동해야 함.
#   → 출시 빌드는 항상 최신 소스를 publish (옛 DLL 재사용 금지).
#   → -SkipErpBuild 는 개발 중 빠른 반복용일 뿐, 출시 빌드에선 경고.
if ($SkipErpBuild) {
    Write-Warning "  ⚠ -SkipErpBuild: 기존 번들 재사용. 출시용이면 절대 사용 금지(옛 DLL 고착). 개발 반복용만."
} else {
    # API publish
    Write-Host "  → HitPan.API publish (최신 소스)..."
    dotnet publish src/HitPan.API/HitPan.API.csproj `
        -c Release `
        -o "$BundleDir/api" `
        --nologo 2>&1 | Out-Null

    if ($LASTEXITCODE -ne 0) {
        Write-Error "HitPan.API publish 실패."
        exit 1
    }

    # Web publish
    Write-Host "  → HitPan.Web publish (최신 소스)..."
    dotnet publish src/HitPan.Web/HitPan.Web.csproj `
        -c Release `
        -o "$BundleDir/web" `
        --nologo 2>&1 | Out-Null

    if ($LASTEXITCODE -ne 0) {
        Write-Error "HitPan.Web publish 실패."
        exit 1
    }

    Write-Host "  ✅ ERP 산출물 빌드 완료" -ForegroundColor Green
}

# ── DLL 버전 게이트: 번들 DLL이 현재 git HEAD로 빌드됐는지 (옛 DLL 고착 차단) ──
# HitPan.API.dll ProductVersion 에 git 커밋 해시가 박힘. HEAD 단축해시와 대조.
$apiDll = "$BundleDir/api/HitPan.API.dll"
if (-not (Test-Path $apiDll)) {
    Write-Error "  ❌ 번들 DLL 없음: $apiDll. ERP 빌드부터 하라(-SkipErpBuild 제거)."
    exit 1
}
$headHash = (& git rev-parse --short HEAD 2>$null)
$dllVer = (Get-Item $apiDll).VersionInfo.ProductVersion
if ($headHash -and $dllVer -and ($dllVer -notmatch [regex]::Escape($headHash))) {
    Write-Error "  ❌ 게이트 실패: 번들 DLL이 현재 코드와 불일치.`n      DLL=$dllVer / HEAD=$headHash`n      옛 DLL이 동봉됨('개발한 그대로' 위반). -SkipErpBuild 없이 재빌드하라. 출시 차단."
    exit 1
}
Write-Host "  ✅ DLL 버전 게이트 통과 — 번들 DLL = 현재 코드(HEAD $headHash)" -ForegroundColor Green

# ─── 3. DB 빈 스키마 준비 + 무결성 게이트 ────────
# 사장님 원칙(2026-06-18): 코드·데이터 구조 100% 그대로. 빼고 넣고 없음.
#   · ERP 로컬 배포본 구조(124 테이블 + 3 트리거)를 그대로 가져간다 (본사 16테이블·5트리거 제거 후, 구조 보존 / 2026-06-29 121→122→123→124).
#   · 데이터만 0 (고객은 빈 DB로 시작) / 백도어 테스트계정만 차단.
#   · common_codes 코드성 시드만 유지 / DEFINER만 제거(회사별DB 이식용).
Write-Host ""
Write-Host "[3/4] DB 빈 스키마 준비 + 무결성 게이트..." -ForegroundColor Yellow

$dbDumpSrc = "installer/hitpan_db_clean.sql"   # 124테이블 구조 그대로 + 데이터0 + common_codes 시드 (게이트 변수 ExpectedTables=124 정합, 2026-06-29 schema_migrations 편입 123→124 고리4 ①)
$dbDumpDst = "$BundleDir/hitpan_db.sql"

if (-not (Test-Path $dbDumpSrc)) {
    Write-Error "  ❌ installer/hitpan_db_clean.sql 없음. 빈 스키마부터 생성하라 (mariadb-dump --no-data --triggers)."
    exit 1
}

$sql = Get-Content $dbDumpSrc -Raw -Encoding UTF8

# ── 게이트 1: 백도어 테스트계정 0건 (DB-03_test_accounts 시드 유입 차단) ──
$backdoorHits = ([regex]::Matches($sql, 'admin@hitpan\.kr|reseller@hitpan\.kr|tenant@hitpan\.kr|Admin1234')).Count
if ($backdoorHits -gt 0) {
    Write-Error "  ❌ 게이트 실패: 백도어 테스트계정 $backdoorHits 건 검출. 출시 차단."
    exit 1
}

# ── 게이트 2: 금지 구문(USE/CREATE DATABASE/DEFINER) 0건 — 회사별 DB import 깨짐 방지 ──
# 주석(-- ...) 라인은 검사 대상에서 제외 (실제 SQL 구문만 차단).
$sqlNoComments = ($sql -split "`n" | Where-Object { $_ -notmatch '^\s*--' }) -join "`n"
$banHits = ([regex]::Matches($sqlNoComments, '(?im)^\s*USE\s|CREATE\s+DATABASE|DROP\s+DATABASE|ALTER\s+DATABASE|DEFINER\s*=`')).Count
if ($banHits -gt 0) {
    Write-Error "  ❌ 게이트 실패: 금지 구문(USE/CREATE DATABASE/DEFINER) $banHits 건. 회사별 DB import 깨짐. 출시 차단."
    exit 1
}

# ── 게이트 3: 구조 보존 — 테이블 수 + 트리거 존재 (구조 100% 그대로) ──
# ERP 로컬 배포본 구조(124 테이블 + 3 트리거)를 그대로 가져왔는지 확인. 빼거나 더하면 차단.
# 빌드 PC에 개발 hitpan_erp가 있으면 실측 교차검증, 없으면 고정 기대값으로 검사.
$tableCount   = ([regex]::Matches($sql, 'CREATE TABLE')).Count
$triggerCount = ([regex]::Matches($sql, '(?im)CREATE.*TRIGGER|50003 .*TRIGGER')).Count
$ExpectedTables   = 124   # ERP 로컬 배포본 BASE TABLE 수 (본사 16테이블 제거 후, 2026-06-19 실측 / 2026-06-29 schema_migrations 편입 123→124 고리4 ①)
$ExpectedTriggers = 3     # ERP 필수 트리거 수 (본사 5트리거 동반 제거 후: psp비율2 + 세금계산서잠금1)
if ($tableCount -ne $ExpectedTables) {
    Write-Error "  ❌ 게이트 실패: 구조 불일치 — 기대 $ExpectedTables 테이블 ≠ 빈 스키마 $tableCount. '구조 100% 그대로' 위반. 출시 차단."
    exit 1
}
if ($triggerCount -lt $ExpectedTriggers) {
    Write-Error "  ❌ 게이트 실패: 트리거 누락 — 기대 $ExpectedTriggers ≠ 빈 스키마 $triggerCount. 구조 보존 위반. 출시 차단."
    exit 1
}
# 빌드 PC에 개발 DB가 있으면 실측 교차검증 (있을 때만, 없으면 건너뜀)
$mariaExe = "$env:ProgramFiles\MariaDB 11.4\bin\mariadb.exe"
if (Test-Path $mariaExe) {
    try {
        $q = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='hitpan_erp' AND table_type='BASE TABLE';"
        $devCnt = (& "$mariaExe" -u hitpan "-pHitpan2025!" -N -e $q 2>$null | Select-Object -First 1)
        if ($devCnt -and ([int]$devCnt -ne $tableCount)) {
            Write-Error "  ❌ 게이트 실패: 개발 DB $devCnt 테이블 ≠ 빈 스키마 $tableCount. 개발 DB와 불일치. 출시 차단."
            exit 1
        }
    } catch { }  # 개발 DB 접근 실패 시 고정 기대값 검사로 충분
}

# ── 게이트 4: 데이터 0 — 개발 데이터 미혼입 (common_codes 코드성 시드만 허용) ──
$insertCount = ([regex]::Matches($sql, '(?im)^\s*INSERT\s+INTO')).Count
$ccInsert    = ([regex]::Matches($sql, '(?im)INSERT\s+INTO\s+`?common_codes')).Count
if (($insertCount - $ccInsert) -gt 0) {
    Write-Error "  ❌ 게이트 실패: common_codes 외 데이터 INSERT $($insertCount - $ccInsert) 건 혼입(개발 데이터 의심). 출시 차단."
    exit 1
}

Copy-Item $dbDumpSrc $dbDumpDst -Force
$size = (Get-Item $dbDumpDst).Length
$devInfo = if ($devTableCount) { "개발 $devTableCount = 빈스키마 $tableCount" } else { "테이블 $tableCount" }
Write-Host "  ✅ 빈 스키마 게이트 4/4 통과 — 구조보존($devInfo), 데이터0, 백도어0 ($([Math]::Round($size/1KB, 0)) KB)" -ForegroundColor Green

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
