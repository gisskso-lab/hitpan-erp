<#
=============================================================================
 업데이트 manifest 본문 생성 (CI/로컬 겸용) — 작업지시서 20260715작1 §7 파이프라인
 검증팀 SoD 반증 반영 · CTO 처방 C-1/C-2 코드 배선

 ■ 이 스크립트가 하는 일 (CI 는 여기까지만 — 서명 없음)
   1) 산출 zip 을 만든다 (api/web/watchdog publish 결과 → hitpan-{ver}.zip)
   2) zip 의 sha256·크기를 계산한다
   3) 버전을 '단방향'으로 결정한다 — 산출 api EXE 의 FileVersion 을 읽어서 쓴다.
      사람이 버전을 두 번 입력하면(인스톨러·manifest) 어긋난다. EXE 가 진실원이다(W4-0).
   4) 호환성 코드 게이트 — local_update_status/consents 의 스키마를 '변경'하는 릴리스면 빌드 실패.
      막는 것은 문자열 '존재'가 아니라 '변경'이다(존재로 막으면 그 테이블을 처음 만든 DB-82/83 이
      항상 실려 있어 전 릴리스 불통과 — 오탐). CREATE 는 clean DDL 컬럼집합과 같으면 통과, 다르거나
      ALTER 면 실패. 구버전 워치독이 그 테이블을 직접 SQL 로 읽고 쓰므로, 변경 시 워치독이 깨져
      업데이트 주체가 죽는다 = 그 PC 영구 고립(CTO C-2 / 20260720작2 B-1).
   5) manifest 본문(JSON)을 출력한다. signature·kid 는 넣지 않는다 — 서명은 NCP 전용(C-1).

 ■ 서명은 여기서 하지 않는다 (CTO C-1 — 착수 차단 사유였음)
   개인키는 NCP /var/hitpan/update-keys/ (600 root) 에만 있다. CI(GitHub Actions)가 서명하려면
   개인키를 Secrets 에 둬야 하고, 그건 "전 고객 SYSTEM 코드 실행 권한을 제3자에 두는 것"이다.
   시리얼 키 유출 = 가짜 라이선스 / 업데이트 키 유출 = 전 고객 PC 임의 코드 실행 — 위력이 다르다.
   그래서: CI 는 본문까지 → NCP 에서 sign-manifest.sh 로 서명 → signature·kid 추가 → 배포.

 ■ requiresMigration 자동 산출은 아직 못박지 않았다 (사장님·CTO 결재 대기)
   작업지시서는 "Migrations/SQL 신규 파일이 있으면 true"라 했으나 '무엇 대비 신규'인지(기준선)를
   명시하지 않았다. 파이프라인 산출 방식이 워치독 교차검증(W4-2) 방식을 결정하므로 함께 정해야 한다.
   → 지금은 -RequiresMigration 스위치로 '명시 입력'만 받고, 자동 산출은 W4-2 착수 시 결재 후 배선한다.
   sql 파일 목록은 사이드카(migrations.txt)로 함께 산출해 둔다(워치독 비교의 재료 = 결재 후 사용).

 헌법 정합: #15(침묵 금지) #19(0/0) #20(워크플로우 불변) #22(개인키 NCP) #23 #29 #34
=============================================================================
#>
[CmdletBinding()]
param(
    # api/web/watchdog publish 산출물이 들어있는 폴더 (각각 하위 api\ web\ watchdog\).
    [Parameter(Mandatory = $true)] [string]$PayloadDir,
    # zip·manifest 를 낼 폴더.
    [Parameter(Mandatory = $true)] [string]$OutDir,
    # 업데이트 채널: Emergency | Normal | Major
    [Parameter(Mandatory = $true)] [ValidateSet('Emergency', 'Normal', 'Major')] [string]$Channel,
    # 다운로드 URL 접두 (예: https://updates.hitpan.kr/packages). 파일명은 자동으로 붙인다.
    [Parameter(Mandatory = $true)] [string]$DownloadUrlBase,
    # DB 스키마 변경 릴리스 여부. 자동 산출 배선 전까지 명시 입력(결재 대기).
    [switch]$RequiresMigration,
    [string]$ReleaseNotes = '',
    [string]$ConsentMessage = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Fail([string]$msg) { Write-Error "[manifest] $msg"; exit 1 }

# ── 1) 산출물 검증 ───────────────────────────────────────────────────────────
$apiDir = Join-Path $PayloadDir 'api'
$webDir = Join-Path $PayloadDir 'web'
$wdDir  = Join-Path $PayloadDir 'watchdog'
foreach ($d in @($apiDir, $webDir, $wdDir)) {
    if (-not (Test-Path $d)) { Fail "산출물 폴더가 없습니다: $d (api/web/watchdog 3개 모두 필요)" }
}

# ── 2) 버전 단방향 — api 산출물의 FileVersion 이 진실원 (W4-0) ────────────────
# ★ 봉합 (2026-07-21, CI 첫 완주 시도에서 적발):
#   종전엔 HitPan.API.dll 만 봤는데, 릴리스 publish 는 -p:PublishSingleFile=true 라
#   모든 관리 DLL 이 EXE 하나로 합쳐져 HitPan.API.dll 이 개별 파일로 존재하지 않는다.
#   → CI 에서 "api 산출물에 HitPan.API.dll 이 없습니다" 로 항상 실패했다.
#   (로컬 빌드는 단일파일이 아니라 dll 이 남아 통과 — 그래서 여태 안 드러났다.
#    개발PC 통과는 검증이 아니다: feedback_dev_pc_proves_nothing.)
#   dll 우선(비단일파일 빌드 호환), 없으면 exe 폴백. 둘 다 없으면 실패.
$apiExe = Join-Path $apiDir 'HitPan.API.dll'
if (-not (Test-Path $apiExe)) {
    $apiExeAlt = Join-Path $apiDir 'HitPan.API.exe'
    if (Test-Path $apiExeAlt) {
        $apiExe = $apiExeAlt
        Write-Host "[manifest] 단일파일 publish 감지 — HitPan.API.exe 에서 버전을 읽습니다."
    }
}
if (-not (Test-Path $apiExe)) { Fail "api 산출물에 HitPan.API.dll/.exe 가 없습니다: $apiDir" }
$fileVer = (Get-Item $apiExe).VersionInfo.FileVersion
if ([string]::IsNullOrWhiteSpace($fileVer)) { Fail "HitPan.API.dll 의 FileVersion 을 읽지 못했습니다(버전 스탬핑 확인 — W4-0)." }
# FileVersion 은 x.y.z.0 형태 → Major.Minor.Build 3자리로 정규화(검증기 TryParseThreePart 와 정합).
$parts = $fileVer.Split('.')
if ($parts.Count -lt 3) { Fail "FileVersion 형식이 예상과 다릅니다: '$fileVer'" }
$version = "$($parts[0]).$($parts[1]).$($parts[2])"
Write-Host "[manifest] 버전(api EXE FileVersion 기준) = $version"

# watchdog·web 버전이 api 와 어긋나면 갈라진 산출물 — 즉시 실패(W4-0 정합 게이트).
# ★ 봉합 (2026-07-21): api 와 동일 사유로 exe 폴백을 둔다. 종전엔 dll 이 없으면
#   if(Test-Path) 가 통째로 스킵돼 "갈라짐 검사를 안 하고 통과"했다(침묵 미검증).
#   단일파일 publish 로 바뀌는 순간 이 게이트가 조용히 무력화된다 — 헌법 #15 정신에 어긋난다.
$wdDll = Join-Path $wdDir 'HitPan.Watchdog.dll'
if (-not (Test-Path $wdDll)) {
    $wdExe = Join-Path $wdDir 'HitPan.Watchdog.exe'
    if (Test-Path $wdExe) { $wdDll = $wdExe }
}
if (-not (Test-Path $wdDll)) {
    Fail "워치독 산출물에 HitPan.Watchdog.dll/.exe 가 없습니다: $wdDir (버전 갈라짐 검사를 건너뛸 수 없다)"
}
if (Test-Path $wdDll) {
    $wdVer = (Get-Item $wdDll).VersionInfo.FileVersion
    $wdParts = $wdVer.Split('.')
    $wdShort = if ($wdParts.Count -ge 3) { "$($wdParts[0]).$($wdParts[1]).$($wdParts[2])" } else { $wdVer }
    if ($wdShort -ne $version) {
        Fail "워치독 버전($wdShort)이 api 버전($version)과 다릅니다 — 같은 커밋에서 빌드했는지 확인(W4-0 갈라짐 차단)."
    }
}

# ── 3) 호환성 코드 게이트 — local_update_ 스키마 "변경"만 차단 (CTO C-2 / 20260720작2 B-1) ─
# ┌─ 두 게이트 통일 판정 정의 (빌드타임=여기 / 런타임=UpdateOrchestrator.PassesMigrationCrossCheck ②) ─┐
# │ 막을 것은 'local_update_' 문자열의 **존재**가 아니라 워치독 상태 테이블의 스키마 **변경**이다.       │
# │ (존재만으로 막으면 그 테이블을 처음 만든 DB-82/83 이 모든 릴리스에 항상 실려 있어 전 릴리스 불통과.) │
# │ 가드 대상 3테이블 = local_update_status·consents(워치독 SELECT) + apply_status(워치독 CREATE·INSERT). │
# │ 판정: DB-*.sql 이 위 3테이블에 대해                                                                │
# │   (a) CREATE TABLE  → 그 컬럼집합이 clean DDL(단일 진실원, 헌법 #36)의 같은 테이블과 **같으면 통과**, │
# │                       다르면(컬럼 추가/삭제/다른 정의) 실패.                                          │
# │   (b) ALTER TABLE   → 무조건 스키마 변경이므로 실패.                                                 │
# │ 근거: 스키마가 바뀌면 그 테이블을 직접 SQL 로 읽고 쓰는 구버전 워치독이 깨져 업데이트 주체가 죽는다 = │
# │       그 고객 PC 영구 고립(재설치 외 복구 불가). 재현일 뿐인 CREATE(=변경 아님)는 무해하므로 통과.   │
# └───────────────────────────────────────────────────────────────────────────────────────────────────┘
$cleanDdlPath = Join-Path $PSScriptRoot '..\hitpan_db_clean.sql'
if (-not (Test-Path $cleanDdlPath)) { Fail "clean DDL(단일 진실원)을 찾지 못했습니다: $cleanDdlPath (헌법 #36)" }
$cleanDdlText = Get-Content -Path $cleanDdlPath -Raw

# clean DDL 의 CREATE TABLE `<name>` (...) 본문에서 백틱 컬럼명 집합을 뽑는다(소문자 정렬).
function Get-CleanDdlColumns([string]$ddlText, [string]$table) {
    # `CREATE TABLE `t` ( ... ) ENGINE` 블록을 잡는다(비탐욕).
    $m = [regex]::Match($ddlText, "CREATE\s+TABLE\s+``$table``\s*\((?<body>.*?)\)\s*ENGINE",
        [System.Text.RegularExpressions.RegexOptions]::Singleline -bor
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $m.Success) { return $null }
    $cols = @()
    foreach ($line in ($m.Groups['body'].Value -split "`n")) {
        $t = $line.Trim()
        # 컬럼 정의 줄만(백틱으로 시작). PRIMARY KEY / KEY / INDEX / UNIQUE 등 제약절은 백틱으로 시작 안 함.
        $cm = [regex]::Match($t, '^`(?<c>[^`]+)`')
        if ($cm.Success -and $t -notmatch '^`?(PRIMARY|UNIQUE|KEY|INDEX|CONSTRAINT|FULLTEXT|FOREIGN)\b') {
            $cols += $cm.Groups['c'].Value.ToLowerInvariant()
        }
    }
    return ($cols | Sort-Object -Unique)
}

# DB-*.sql 의 CREATE TABLE [IF NOT EXISTS] <name> (...) 본문에서 컬럼명 집합을 뽑는다(백틱 유무 무관).
function Get-MigrationCreateColumns([string]$sqlText, [string]$table) {
    $m = [regex]::Match($sqlText,
        "CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?``?$table``?\s*\((?<body>.*?)\)\s*ENGINE",
        [System.Text.RegularExpressions.RegexOptions]::Singleline -bor
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $m.Success) { return $null }
    $cols = @()
    foreach ($line in ($m.Groups['body'].Value -split "`n")) {
        $t = $line.Trim()
        $cm = [regex]::Match($t, '^`?(?<c>[A-Za-z_][A-Za-z0-9_]*)`?\s')
        if ($cm.Success -and $t -notmatch '^`?(PRIMARY|UNIQUE|KEY|INDEX|CONSTRAINT|FULLTEXT|FOREIGN)\b') {
            $cols += $cm.Groups['c'].Value.ToLowerInvariant()
        }
    }
    return ($cols | Sort-Object -Unique)
}

$guardedTables = @('local_update_status', 'local_update_consents', 'local_update_apply_status')
$sqlDir = Join-Path $apiDir 'Migrations\SQL'
$migrationFiles = @()
if (Test-Path $sqlDir) {
    $migrationFiles = @(Get-ChildItem -Path $sqlDir -Filter 'DB-*.sql' -File | Sort-Object Name)
    foreach ($f in $migrationFiles) {
        $sqlText = Get-Content -Path $f.FullName -Raw
        if ($sqlText -notmatch 'local_update_') { continue }   # 이 테이블을 언급조차 안 하면 검사 대상 아님.
        foreach ($table in $guardedTables) {
            # (b) ALTER TABLE <table> → 무조건 스키마 변경 = 실패.
            if ($sqlText -match "ALTER\s+TABLE\s+``?$table``?\b") {
                Fail @"
호환성 게이트 위반 — $($f.Name) 이 '$table' 을 ALTER 합니다(스키마 변경).
  이 테이블은 구버전 워치독이 직접 SQL 로 읽고 쓰므로, 변경 시 구버전 워치독이 깨져
  업데이트 주체가 죽습니다 = 그 고객 PC 는 재설치 외 복구 불가(사장님이 없앤 경로).
  이 테이블을 바꾸려면 워치독 자기교체(고리5)를 먼저 설계하고 사장님 결재를 받으십시오.
"@
            }
            # (a) CREATE TABLE <table> → clean DDL 컬럼집합과 비교. 다르면 실패, 같으면(재현) 통과.
            $migCols = Get-MigrationCreateColumns $sqlText $table
            if ($null -ne $migCols) {
                $cleanCols = Get-CleanDdlColumns $cleanDdlText $table
                if ($null -eq $cleanCols) {
                    Fail "clean DDL 에 '$table' 정의가 없습니다 — 대조 기준을 세울 수 없어 안전측(실패)으로 막습니다(헌법 #36)."
                }
                $diff = Compare-Object -ReferenceObject $cleanCols -DifferenceObject $migCols
                if ($diff) {
                    $migStr = ($migCols -join ', ')
                    $cleanStr = ($cleanCols -join ', ')
                    Fail @"
호환성 게이트 위반 — $($f.Name) 의 '$table' CREATE 가 clean DDL 과 컬럼집합이 다릅니다(스키마 변경).
  clean DDL($cleanStr)
  이 릴리스($migStr)
  구버전 워치독이 직접 쓰는 스키마를 바꾸면 그 워치독이 깨져 고객 PC 가 영구 고립됩니다.
  clean DDL(단일 진실원, 헌법 #36)과 일치시키거나, 워치독 자기교체(고리5) 설계 후 사장님 결재를 받으십시오.
"@
                }
            }
        }
    }
}

# ── 4) zip 생성 + sha256 ─────────────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$zipName = "hitpan-$version.zip"
$zipPath = Join-Path $OutDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# api/web/watchdog 3개를 각각 최상위 폴더로 유지해 압축(워치독 스왑이 이 구조를 전제).
Push-Location $PayloadDir
try {
    Compress-Archive -Path 'api', 'web', 'watchdog' -DestinationPath $zipPath -CompressionLevel Optimal
}
finally { Pop-Location }

$sha = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item $zipPath).Length
Write-Host "[manifest] zip = $zipPath ($([math]::Round($size/1MB,1)) MB)  sha256 = $sha"

# ── 5) migrations 사이드카 (워치독 교차검증 재료 — 결재 후 사용) ──────────────
$sidecar = Join-Path $OutDir "migrations-$version.txt"
if ($migrationFiles.Count -gt 0) {
    $migrationFiles | ForEach-Object { $_.Name } | Set-Content -Path $sidecar -Encoding UTF8
} else {
    Set-Content -Path $sidecar -Value '' -Encoding UTF8
}
Write-Host "[manifest] Migrations/SQL 파일 $($migrationFiles.Count)건 → 사이드카 $sidecar"

# ── 6) manifest 본문 (서명 없음 — NCP 에서 서명 후 signature·kid 추가) ────────
#   필드명은 UpdateManifest record 의 JSON 매핑(camelCase, JsonSerializerDefaults.Web)과 일치해야 한다.
$releasedAt = if ($env:HITPAN_RELEASED_AT) { $env:HITPAN_RELEASED_AT } else {
    # CI 는 이 값을 주입한다. 없으면 명시 실패(Date.Now 를 숨겨 쓰면 재현 불가·서명 payload 흔들림).
    Fail "HITPAN_RELEASED_AT(UTC ISO-8601, 예: 2026-07-16T00:00:00Z) 를 환경변수로 주십시오."
}

$manifest = [ordered]@{
    version           = $version
    channel           = $Channel
    downloadUrl       = "$($DownloadUrlBase.TrimEnd('/'))/$zipName"
    sha256            = $sha
    sizeBytes         = $size
    releasedAt        = $releasedAt
    releaseNotes      = if ($ReleaseNotes)   { $ReleaseNotes }   else { $null }
    requiresMigration = [bool]$RequiresMigration
    consentMessage    = if ($ConsentMessage) { $ConsentMessage } else { $null }
    # signature·kid 는 여기서 넣지 않는다 — NCP sign-manifest.sh 가 채운다(C-1).
}

$manifestPath = Join-Path $OutDir 'manifest.json'
# BOM 없는 UTF-8 로 출력한다 (20260720작3 CTO B-2 — manifest 는 워치독이 파싱하는 JSON, BOM 없어야 함).
#   PowerShell 5.1 의 `Set-Content -Encoding UTF8` 은 BOM(EF BB BF)을 붙인다. 워치독 System.Text.Json 은
#   BOM 을 관용 처리하지만, publish-update.sh 의 jq·자기검증 등 다운스트림이 BOM 을 만나 흔들릴 여지를
#   산출 시점에 제거한다. WriteAllText + UTF8Encoding($false) 는 5.1·7.x 양쪽에서 BOM 없는 UTF-8 을 보장.
$manifestJson = $manifest | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText($manifestPath, $manifestJson, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "[manifest] 본문 출력(서명 전, BOM 없음) = $manifestPath"
Write-Host ""
Write-Host "다음 단계(NCP, 사장님 결재 1회):"
Write-Host "  bash installer/updates/sign-manifest.sh --private /var/hitpan/update-keys/update_private.pem \"
Write-Host "    --version $version --channel $($Channel.ToLowerInvariant()) \"
Write-Host "    --url $($manifest.downloadUrl) --sha256 $sha --size $size --migration $(([bool]$RequiresMigration).ToString().ToLowerInvariant())"
