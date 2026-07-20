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
    # 봉합 (2026-07-06, 종단설치 P0 — setup 화면 실종 진범): 종전엔 publish 출력을 `| Out-Null`
    #   로 버려 $LASTEXITCODE 가 파이프 마지막(Out-Null=항상0)을 봤다. Web publish 가 중간에
    #   깨져도(오늘 아침 PC 크래시) 게이트를 통과해 LicenseSetupPage 빠진 불완전 WASM 이 출하됐고,
    #   /setup/license 가 NotFound → 부모계정 생성 불가 → 로그인 401(헌법 #20 워크플로우 차단).
    #   → Out-Null 제거(출력 보존)하고 publish 직후 exit code 를 명시 변수로 잡는다.
    # ── W2 (2026-07-07): publish 전 bundle/api 청소 (작업지시서 20260706작-v2, CTO 승인) ──
    #   `dotnet publish -o` 는 출력 폴더를 완전 청소하지 않아, 과거 산출물(옛 wwwroot 5/18~20 잔재)이
    #   새 dll 과 섞여 그대로 출하됐다(진범). publish 전 폴더를 비워 잔재 재유입을 원천 차단한다.
    #   (소스트리 src/HitPan.API/wwwroot 옛 잔재도 별도 삭제+.gitignore 등재 — 재오염원 제거, CTO 지목.)
    if (Test-Path "$BundleDir/api") { Remove-Item -Recurse -Force "$BundleDir/api" }
    # ── W4-0 (2026-07-16, 검증팀 F-1 적발): publish 에 -p:HitPanVersion 주입 ──
    #   종전엔 -Version 이 /DAppVersion(인스톨러)에만 갔고 어셈블리엔 안 갔다. 그래서 EXE 이름은
    #   1.2.33 인데 그 안의 dll 은 Directory.Build.props 폴백(그 이전엔 아예 없어서 1.0.0)이었다.
    #   업데이트 판정·/health·본사 메타핑이 전부 어셈블리 버전을 읽으므로, 이 주입이 빠지면
    #   "인스톨러는 새 버전 / 프로그램은 옛 버전" 으로 갈라져 자동 업데이트가 무한 재적용에 빠진다.
    Write-Host "  → HitPan.API publish (v$Version)..."
    dotnet publish src/HitPan.API/HitPan.API.csproj -c Release -o "$BundleDir/api" -p:HitPanVersion=$Version --nologo
    if ($LASTEXITCODE -ne 0) { Write-Error "HitPan.API publish 실패 (exit $LASTEXITCODE)."; exit 1 }

    Write-Host "  → HitPan.Web publish (v$Version)..."
    dotnet publish src/HitPan.Web/HitPan.Web.csproj -c Release -o "$BundleDir/web" -p:HitPanVersion=$Version --nologo
    if ($LASTEXITCODE -ne 0) { Write-Error "HitPan.Web publish 실패 (exit $LASTEXITCODE)."; exit 1 }

    # ── Web 산출물 완전성 게이트 (2026-07-06 신설, 헌법 #36 실측 스모크의 Web판) ──
    #   빌드 성공(exit 0)이어도 산출물이 완전하다는 보장이 아니다. setup 화면(부모계정 생성 =
    #   신규/재설치 필수 관문)이 WASM 에 실제 담겼는지 라우트 문자열로 실측한다. 없으면 출하 차단.
    $webWasm = "$BundleDir/web/wwwroot/_framework/HitPan.Web.wasm"
    if (-not (Test-Path $webWasm)) {
        Write-Error "  ❌ Web게이트 실패: HitPan.Web.wasm 미생성 ($webWasm). 출시 차단."; exit 1
    }
    $wasmText = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($webWasm))
    foreach ($needle in @('setup/license', 'LicenseSetup')) {
        if ($wasmText -notmatch [regex]::Escape($needle)) {
            Write-Error "  ❌ Web게이트 실패: WASM 에 '$needle' 라우트/컴포넌트 실종 — setup 화면 빠진 불완전 빌드. 출시 차단 (종단 P0 재발)."; exit 1
        }
    }
    Write-Host "  ✅ Web 완전성 게이트 통과 — setup/license 라우트 담김" -ForegroundColor Green

    # ── W1 (2026-07-07, 종단 P0 진범 봉합 — 작업지시서 20260706작-v2, 4인 만장일치+CTO 조건부승인) ──
    #   진범: 터널(test1.hitpan.kr)은 ingress origin=http://localhost:5257(API) 직결이라(3중 실측:
    #   /health 200=API전용·ingress 코드값 5257·WASM바이트=API옛것 일치), 터널로 오는 실고객은
    #   오직 api\wwwroot 의 WASM 으로 화면을 받는다. 그런데 빌드는 web\wwwroot 만 최신 publish 하고
    #   api\wwwroot 는 소스트리 옛 잔재(5/18~20, setup 라우트 없음)를 그대로 실어 → 터널 setup NotFound
    #   → 부모계정 생성 불가 → 로그인 401(헌법 #20 차단). web-server.ps1(5234)은 로컬 전용이라 로컬만 정상.
    #   봉합: API 서빙 제거(v1)는 터널 화면 백지 크래시 P0 오답 — 제거가 아니라 web publish 최신 WASM 을
    #   api\wwwroot 에 복사(갱신)한다. 터널·ingress·포트는 손대지 않는다(헌법 #29 통신 무결성 무손상).
    Write-Host "  → API wwwroot 최신화 (web publish → api\wwwroot 복사)..."
    $apiWwwroot = "$BundleDir/api/wwwroot"
    if (Test-Path $apiWwwroot) { Remove-Item -Recurse -Force $apiWwwroot }
    Copy-Item -Recurse -Force "$BundleDir/web/wwwroot" $apiWwwroot

    # ── W3: api\wwwroot == web\wwwroot 정합 강제 게이트 (CTO 조건, 2벌 드리프트 차단) ──
    #   설치본은 api\wwwroot(5257 서빙)+web\wwwroot(5234 서빙) 2벌 WASM 을 함께 출하(.iss L88·89).
    #   한쪽만 갱신되면 오늘 사고 재발. 두 WASM 의 SHA256 이 다르면 빌드 실패시켜 드리프트를 원천 차단.
    #   (근본 제거=web-server 5234 폐기+5257 단일서빙 one-served-copy 는 P1 백로그 별건.)
    $apiWasm = "$apiWwwroot/_framework/HitPan.Web.wasm"
    if (-not (Test-Path $apiWasm)) {
        Write-Error "  ❌ api정합게이트 실패: api\wwwroot 복사 후 HitPan.Web.wasm 없음. 출시 차단."; exit 1
    }
    $hashWeb = (Get-FileHash $webWasm -Algorithm SHA256).Hash
    $hashApi = (Get-FileHash $apiWasm -Algorithm SHA256).Hash
    if ($hashWeb -ne $hashApi) {
        Write-Error "  ❌ api정합게이트 실패: web\wwwroot($hashWeb) ≠ api\wwwroot($hashApi) — 2벌 드리프트. 출시 차단 (종단 P0 재발)."; exit 1
    }
    Write-Host "  ✅ api\wwwroot 정합 게이트 통과 — web=api WASM 동일($($hashApi.Substring(0,12)))" -ForegroundColor Green

    Write-Host "  ✅ ERP 산출물 빌드 완료" -ForegroundColor Green
} else {
    Write-Host "  ⏭ ERP 빌드 건너뜀 (-SkipErpBuild)"
}

# ─── 3. 워치독 빌드 (Day 7에 추가될 영역, 미존재 시 건너뜀) ─────
Write-Host ""
Write-Host "[3/5] 워치독 빌드..." -ForegroundColor Yellow

if (-not $SkipWatchdog -and (Test-Path "src/HitPan.Watchdog/HitPan.Watchdog.csproj")) {
    Write-Host "  → HitPan.Watchdog publish (v$Version)..."
    # 봉합 (2026-07-06): Out-Null 제거 — $LASTEXITCODE 가 publish 실제 결과를 보게 한다(위 API/Web 동일 사유).
    # 봉합 (2026-07-16, W4-0 검증팀 F-1): -p:HitPanVersion 주입. 워치독 버전은 본사 메타핑이 보는 값이자
    #   업데이트 판정의 "현재 버전"이라, 여기가 빠지면 워치독이 자기를 1.0.0 으로 알고
    #   모든 manifest 를 "새 버전"으로 받아들인다(다운그레이드 방어까지 통째로 무력화).
    dotnet publish src/HitPan.Watchdog/HitPan.Watchdog.csproj `
        -c Release -o "$BundleDir/watchdog" -r win-x64 --self-contained true `
        -p:HitPanVersion=$Version `
        --nologo
    if ($LASTEXITCODE -ne 0) { Write-Error "HitPan.Watchdog publish 실패 (exit $LASTEXITCODE)."; exit 1 }
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
$dbDumpSrc = "installer/hitpan_db_clean.sql"   # 124 구조 + 데이터0 + common_codes 시드 (git 추적 정본, 2026-06-29 schema_migrations 편입 123→124 고리4 ①)
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

# ── 게이트 3: 구조 보존 — 테이블 수(125) + 트리거(3) ── (2026-06-29 schema_migrations 편입 123→124 고리4 ① / 2026-07-20 local_update_apply_status 편입 124→125 고리4 §W4-6)
$tableCount   = ([regex]::Matches($sql, 'CREATE TABLE')).Count
$triggerCount = ([regex]::Matches($sql, '(?im)CREATE.*TRIGGER|50003 .*TRIGGER')).Count
if ($tableCount -ne 125) { throw "  ❌ 게이트3 실패: 구조 불일치 — 기대 125 테이블 ≠ $tableCount. 출시 차단." }
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

# ── 버전 정합 게이트 (2026-07-16, 작1 W4-0 — 검증팀 F-1·F-2 적발) ────────────────
#   왜 필요한가: 종전엔 "인스톨러 버전(EXE 이름·AppVersion)"과 "어셈블리 버전(프로그램이 실제로
#   자기를 뭐라고 말하는지)"이 각각 따로 정해졌다. 그래서 EXE 이름은 1.2.33 인데 그 안의 dll 은
#   1.0.0 인 채로 출하됐고, 본사 메타핑·/health·업데이트 판정이 전부 거짓을 말했다.
#   주석으로 "같아야 한다"고 적는 건 사람의 약속이라 언젠가 갈라진다 — 코드가 막는다.
#   구워진 산출물을 실제로 열어보고 다르면 여기서 세운다(헌법 #19 "그냥 되어야 한다").
Write-Host ""
Write-Host "  → 버전 정합 게이트 (산출물 실측)..." -ForegroundColor Yellow
$expectedVer = "$Version.0"   # AssemblyVersion 은 Directory.Build.props 에서 x.y.z.0 으로 스탬핑된다
$versionTargets = @(
    @{ Name = "API";      Path = "$BundleDir/api/HitPan.API.dll" },
    @{ Name = "Watchdog"; Path = "$BundleDir/watchdog/HitPan.Watchdog.dll" }
)
foreach ($t in $versionTargets) {
    if (-not (Test-Path $t.Path)) {
        # 봉합 (2026-07-16, 검증팀 재검증 Q2 적발): 종전엔 조용히 continue 했다.
        #   그런데 -SkipErpBuild 로 돌리면 옛 산출물이 그대로 재사용되는데, 그게 바로 이 게이트가
        #   잡아야 할 상황이다. 산출물이 없으면 "검사했다"가 아니라 "검사 못 했다"이므로 경고로 올린다.
        Write-Warning "$($t.Name): 산출물이 없어 버전 정합을 검사하지 못했습니다 — $($t.Path)`n" +
                      "  (-SkipErpBuild/-SkipWatchdog 로 돌렸다면, 담기는 파일이 v$Version 이 맞는지 직접 확인하세요.)"
        continue
    }
    $actual = (Get-Item $t.Path).VersionInfo.FileVersion
    if ($actual -ne $expectedVer) {
        Write-Error @"
버전 정합 게이트 실패 — $($t.Name) 어셈블리가 요청한 버전과 다릅니다.
  요청(-Version):  $Version  (기대 FileVersion: $expectedVer)
  실제 산출물:     $actual
  파일:            $($t.Path)

원인 후보: publish 단계에 -p:HitPanVersion=`$Version 주입이 빠졌거나, 옛 산출물이 남아 있습니다.
이대로 출하하면 EXE 이름은 $Version 인데 프로그램은 자기를 $actual 이라 말해,
본사 모니터링·자동 업데이트 판정이 전부 어긋납니다(무한 재적용 위험). 출하를 차단합니다.
"@
        exit 1
    }
    Write-Host "    ✅ $($t.Name): $actual" -ForegroundColor Green
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
