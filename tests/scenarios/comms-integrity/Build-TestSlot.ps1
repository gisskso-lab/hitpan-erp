# =====================================================================
#  Build-TestSlot.ps1  —  통신/업데이트 검증용 test 슬롯 구축 스크립트
#  작성: 풀스택 매니저 존 (MS 설계팀장) / 2026-06-29 / 문서번호 20260629작2
# ---------------------------------------------------------------------
#  ##  이 스크립트 실행 = 헌법 #29 인프라 조작입니다  ##
#  - DB 생성 / 서비스(cloudflared-test, MariaDB-test) 등록이 포함됩니다.
#  - 반드시 사장님 입회 + 사전 승인 후에만 실행하십시오(헌법 #29, #33).
#  - 작성자(존)는 파일만 작성했고 실행하지 않았습니다(A갈래=준비물).
# ---------------------------------------------------------------------
#  헌법 정합:
#   #15 빈 catch 금지 — 모든 예외는 로그를 남긴다.
#   #27 통신 무결성 — 이 슬롯이 17시나리오 실측의 격리 환경.
#   #36 출하 DDL 단일 진실원 — installer/hitpan_db_clean.sql 만 import.
#   #39 운영(demo) 절대 미터치 — DB명/도메인/서비스명 demo면 즉시 abort.
#        ASCII 격리 경로(C:\HitPanTest\), 한글 경로 차단.
#
#  단일 진실원 설계서:
#   tests/scenarios/comms-integrity/README_테스트환경구축.md
#  소비 스크립트(이 슬롯을 사용):
#   tests/scenarios/comms-integrity/Run-CommsScenarios.ps1
#
#  실행(사장님 입회 하에, 관리자 PowerShell):
#    powershell -ExecutionPolicy Bypass -File Build-TestSlot.ps1 -Confirm
#
#  ※ 인코딩: 본 파일은 UTF-8 BOM 으로 저장됨.
#    Windows PowerShell 5.1 이 BOM 없는 UTF-8 한글을 CP949 로 오독해
#    가짜 구문오류를 내므로 BOM 유지 필수(편집 시 재저장).
# =====================================================================

[CmdletBinding()]
param(
    # 안전장치 (1) : 명시적 동의 없으면 실행 거부(오발사 방지, README 안전망 #1)
    [switch]$Confirm,

    # ASCII 격리 루트(헌법 #39 — 한글 경로 깨짐 차단). 변경 비권장.
    [string]$TestRoot = 'C:\HitPanTest',

    # test 슬롯 포트(demo 5234/5257 충돌 회피 — README 격리표)
    [int]$ApiPort = 15257,
    [int]$WebPort = 15234,

    # test 슬롯 식별자 / 도메인(README, 마커 단일 진실원)
    [string]$Slot          = 'e2e-comms',
    [string]$PrimaryDomain = 'e2e-comms.hitpan.kr',

    # test 전용 서비스명(반드시 -test 접미사 — 안전망 가드 정합)
    [string]$CloudflaredSvc = 'cloudflared-test',
    [string]$MariaDbSvc     = 'MariaDB-test',

    # 테스트 DB(절대 운영 미터치 — 하드코딩 가드 대상)
    [string]$DbName = 'hitpan_e2e',
    [string]$DbUser = 'hitpan',

    # ## 항목3 봉합(2026-06-30, M4 백로그): DB 인스턴스 접속을 host/port 로 명시 고정한다.
    #    전원 하브루타 결론 — probe(운영DB 가드)와 CREATE 가 같은 인스턴스를 본다는 보장을 코드에 박는다.
    #    M4 가 별도 포트 인스턴스(예: 3307/3308)일 때, 이 값을 호출 시 주지 않으면 가드는 테스트 인스턴스,
    #    CREATE 는 기본 포트(운영 3306)를 가리키는 실버그가 된다. 기본값은 로컬 단일 인스턴스 환경용.
    [string]$DbHost = '127.0.0.1',
    [int]$DbPort    = 3306,

    # 출하 DDL 단일 진실원(헌법 #36) — 레포 루트 기준 상대경로
    [string]$CleanDdlRelPath = 'installer\hitpan_db_clean.sql',

    # db.conf 가 놓일 위치 — 격리 경로(C:\HitPanTest)에 둔다.
    # ## M2 봉합(2026-06-30, 작3): 과거 기본값이 운영 공유경로('C:\Program Files\HitPan\db.conf')라
    #    이 스크립트가 demo 운영 db.conf 를 테스트값으로 덮어 demo 재시작 시 인증깨짐 잠복 사고 발생.
    #    → 격리 루트 하위로 변경. 운영 경로 지정 시 [4]단계에서 abort(아래 봉합 가드).
    [string]$DbConfPath = 'C:\HitPanTest\db.conf',

    # 실제 변경 없이 가드/경로/파일 존재만 점검
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------
#  로그 (헌법 #15 — 빈 catch 금지, 모든 흐름/예외 기록)
# ---------------------------------------------------------------------
$script:LogLines = New-Object System.Collections.Generic.List[string]
function Write-Log {
    param([string]$Level, [string]$Msg)
    $ts = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    $line = "[$ts] [$Level] $Msg"
    $script:LogLines.Add($line)
    $color = switch ($Level) {
        'OK'    { 'Green' }
        'STEP'  { 'Cyan' }
        'WARN'  { 'Yellow' }
        'ABORT' { 'Red' }
        'FAIL'  { 'Red' }
        default { 'Gray' }
    }
    Write-Host $line -ForegroundColor $color
}
function Step($n, $msg)  { Write-Log 'STEP'  "[$n] $msg" }
function Ok($msg)        { Write-Log 'OK'    $msg }
function Warn($msg)      { Write-Log 'WARN'  $msg }
function Save-Log {
    try {
        $logDir = Join-Path $TestRoot 'logs'
        if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Force $logDir | Out-Null }
        $logFile = Join-Path $logDir ("build-testslot-{0}.log" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
        # UTF-8 (BOM) 로 기록 — PS5.1 한글 정합
        [System.IO.File]::WriteAllLines($logFile, $script:LogLines, (New-Object System.Text.UTF8Encoding($true)))
        Write-Host "    로그 저장: $logFile" -ForegroundColor DarkGray
    } catch {
        # 헌법 #15: 로그 저장 실패도 침묵 금지(콘솔에라도 남긴다)
        Write-Host "    [WARN] 로그 파일 저장 실패: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}
function Abort($msg) {
    Write-Log 'ABORT' $msg
    Save-Log
    exit 1
}

# =====================================================================
#  ## 실행 경고 배너 — 헌법 #29
# =====================================================================
Write-Host ""
Write-Host "=====================================================================" -ForegroundColor Red
Write-Host "  ## 이 스크립트 실행 = 헌법 #29 인프라 조작 (DB 생성/서비스 등록)" -ForegroundColor Red
Write-Host "     반드시 사장님 입회 + 사전 승인 후에만 실행하십시오." -ForegroundColor Red
Write-Host "     운영(demo)은 절대 건드리지 않습니다(헌법 #39)." -ForegroundColor Red
Write-Host "=====================================================================" -ForegroundColor Red
Write-Host ""

# ---------------------------------------------------------------------
#  안전가드 (1) : -Confirm 없으면 거부 (오발사 방지, README 안전망 #1)
# ---------------------------------------------------------------------
if (-not $Confirm) {
    Write-Host "[X] -Confirm 미지정. 안전을 위해 실행 거부." -ForegroundColor Red
    Write-Host "    실행하려면(사장님 입회 하): -Confirm 를 명시하십시오." -ForegroundColor Yellow
    exit 1
}

# ---------------------------------------------------------------------
#  안전가드 (2) : 운영(demo/hitpan_erp) 식별자 하드 차단 (헌법 #39)
#   - DB명/도메인/서비스명에 운영 흔적이 보이면 즉시 abort.
#   - "운영을 실수로라도 만들지 않는다" 의 1차 방어선.
# ---------------------------------------------------------------------
Step 'G' '운영(demo) 식별자 차단 가드 (헌법 #39)'

# DB명: hitpan_e2e 가 아니거나 demo/hitpan_erp 면 즉시 차단
if ($DbName -ne 'hitpan_e2e') {
    Abort "DbName 은 반드시 'hitpan_e2e' 여야 합니다. 입력값='$DbName' → 운영 보호 차단."
}
if ($DbName -match '(?i)(demo|hitpan_erp)') {
    Abort "DbName 에 운영 흔적(demo/hitpan_erp) 감지: '$DbName' → 차단."
}
Ok "DbName='$DbName' (운영 미터치 확인)"

# 도메인: demo 면 차단
if ($PrimaryDomain -match '(?i)demo') {
    Abort "PrimaryDomain 에 demo 감지: '$PrimaryDomain' → 운영 도메인 차단."
}
Ok "PrimaryDomain='$PrimaryDomain' (demo 아님)"

# 서비스명: -test 접미사 강제(데모 공통명 cloudflared/MariaDB 보호)
if ($CloudflaredSvc -notmatch '(?i)-test$') {
    Abort "CloudflaredSvc 는 '-test' 접미사 필수(demo 'cloudflared' 보호): '$CloudflaredSvc'"
}
if ($MariaDbSvc -notmatch '(?i)-test$') {
    Abort "MariaDbSvc 는 '-test' 접미사 필수(demo 'MariaDB' 보호): '$MariaDbSvc'"
}
Ok "서비스명 test 접미사 확인: $CloudflaredSvc / $MariaDbSvc"

# 포트: demo 5234/5257 충돌 차단
if ($ApiPort -eq 5257 -or $WebPort -eq 5234) {
    Abort "포트가 demo(5234/5257)와 충돌: Api=$ApiPort Web=$WebPort → 차단."
}
Ok "포트 격리 확인: API=$ApiPort / Web=$WebPort"

# ASCII 경로 강제(한글 경로 깨짐 차단 — 헌법 #39)
if ($TestRoot -match '[^\x00-\x7F]') {
    Abort "TestRoot 에 비ASCII 문자 감지(한글 경로 금지): '$TestRoot'"
}
if ($TestRoot -notmatch '(?i)^[A-Z]:\\HitPanTest') {
    # 강제는 아니되 격리 루트 관례를 벗어나면 경고만(사장님 결재 시 조정 여지)
    Warn "TestRoot 가 권장 격리 루트(C:\HitPanTest)와 다릅니다: '$TestRoot'"
}
Ok "ASCII 격리 경로 확인: $TestRoot"

# ---------------------------------------------------------------------
#  안전가드 (3) : demo 서비스가 Running 이면 경고 (위험상태 인지)
#   - 죽이지는 않는다(이 스크립트는 구축용). 사장님께 경고만.
# ---------------------------------------------------------------------
function Test-DemoServiceRunning {
    # demo 공통 서비스명(접미사 없음) — Run-CommsScenarios.ps1 안전망과 동일 철학
    foreach ($name in @('cloudflared', 'MariaDB')) {
        try {
            $svc = Get-Service -Name $name -ErrorAction Stop
            if ($svc.Status -eq 'Running') {
                Warn "demo 의심 서비스 '$name' 가 Running 입니다. 이 PC가 운영(demo) PC가 아닌지 사장님 확인 요망(헌법 #39)."
            }
        } catch {
            # 헌법 #15: 서비스 없음은 정상(테스트 PC). 침묵 말고 디버그 로그.
            Write-Log 'INFO' "서비스 '$name' 조회 불가/없음(테스트 PC면 정상): $($_.Exception.Message)"
        }
    }
}
Test-DemoServiceRunning

# ---------------------------------------------------------------------
#  안전가드 (3-M3) : 운영 DB(hitpan_erp) 존재 시 ABORT  ## M3 봉합(2026-06-30, 작3)
#   - 사고 원인: 가드(3)이 '서비스명'만 봤다. demo 는 MariaDB(3306) 인스턴스의 hitpan_erp DB 로
#     살아있는데(129만행), 서비스명 가드는 "테스트 PC면 정상"으로 통과시켜 운영 인스턴스 안에
#     hitpan_e2e 를 만들었다(2026-06-30 실측 사고).
#   - 봉합: 슬롯을 만들 대상 인스턴스에 운영 DB(hitpan_erp)가 존재하면 = 운영과 공유 인스턴스 =
#     테스트 슬롯 생성 ABORT. WARN 이 아니라 물리 차단(헌법 #39).
#   - 별도 인스턴스/포트(M4)로 분리되면 hitpan_erp 가 없어 통과한다.
function Assert-NoLiveOperationalDb {
    # mysql 클라이언트로 대상 인스턴스의 사용자 DB 존재 여부만 조회(읽기, 데이터 미접근).
    # [작3v2 R2 fail-closed] mysql 부재면 확인 불가 → 안전하게 abort(과거: SKIP=fail-open).
    $mysqlProbe = (Get-Command mysql -ErrorAction SilentlyContinue)
    if (-not $mysqlProbe) {
        Abort "mysql 클라이언트가 없어 대상 인스턴스의 운영 DB 존재 여부를 확인할 수 없습니다 → 안전하게 중단(R2 fail-closed, 헌법 #39). mysql 클라이언트를 PATH 에 두고 재시도하십시오."
    }
    # [작3v2 보정] 비번은 MYSQL_PWD 환경변수로 전달한다(--password= 명령행은 stderr 경고를
    #   내뿜어 아래 실패판정을 오발시킴 — 2026-06-30 WhatIfOnly 실측에서 R2 과민발동 확인).
    $prevPwd = $env:MYSQL_PWD
    $dbPw2 = $env:HITPAN_TEST_DB_PW
    if (-not [string]::IsNullOrWhiteSpace($dbPw2)) { $env:MYSQL_PWD = $dbPw2 }
    try {
        # [작3v2 R3 화이트리스트] hitpan_erp 단일 리터럴이 아니라, 시스템 DB·슬롯 자신(hitpan_e2e)을
        #   제외한 '사용자 DB'가 하나라도 있으면 = 운영/타 사용자와 공유 인스턴스 = abort.
        #   demo 가 hitpan_erp·demo·hitpan_t001(멀티사업자 #35) 무엇이든 이름 무관 차단.
        $probeSql = "SELECT IFNULL(GROUP_CONCAT(SCHEMA_NAME),'') FROM information_schema.SCHEMATA " +
                    "WHERE SCHEMA_NAME NOT IN ('hitpan_e2e','information_schema','mysql','performance_schema','sys','test');"
        $userDbs = (& $mysqlProbe.Source "--host=$DbHost" "--port=$DbPort" "--user=$DbUser" --batch --skip-column-names "--execute=$probeSql" 2>$null | Out-String).Trim()
        # [작3v2 R2 fail-closed] 종료코드로 실패 판정(경고는 exit 0, 진짜 실패만 exit≠0).
        if ($LASTEXITCODE -ne 0) {
            Abort "운영 DB 존재 여부를 확인하지 못했습니다(mysql 종료코드 $LASTEXITCODE) → 안전하게 중단(R2 fail-closed, 헌법 #39). 운영 인스턴스 여부가 불확실하면 슬롯을 생성하지 않습니다."
        }
        if (-not [string]::IsNullOrWhiteSpace($userDbs)) {
            Abort "이 DB 인스턴스에 사용자 DB가 존재합니다: '$userDbs' → 운영/타 사용자와 공유 인스턴스(R3 화이트리스트, 헌법 #39). 테스트 슬롯은 별도 인스턴스/포트(시스템 DB만 있는 곳)에서만 생성하십시오. 운영 인스턴스 옆에 hitpan_e2e 를 만들면 운영 데이터 옆에 두는 사고가 됩니다."
        }
        Ok "사용자 DB 미존재 확인 — 이 인스턴스는 테스트 전용(R3 가드 통과)."
    } catch {
        # [작3v2 R2 fail-closed] 예외 시에도 침묵 통과 금지. 운영 여부 불확실 = 안전하게 중단.
        Abort "운영 DB 존재 여부 조회 중 예외: $($_.Exception.Message) → 안전하게 중단(R2 fail-closed, 헌법 #39). 운영 인스턴스 여부가 불확실하면 슬롯을 생성하지 않습니다."
    } finally {
        # MYSQL_PWD 원복(누수 방지)
        if ($null -eq $prevPwd) { Remove-Item Env:\MYSQL_PWD -ErrorAction SilentlyContinue }
        else { $env:MYSQL_PWD = $prevPwd }
    }
}
Assert-NoLiveOperationalDb

if ($WhatIfOnly) {
    Ok '--WhatIfOnly: 가드 점검만 수행하고 변경 없이 종료.'
    Save-Log
    return
}

# ---------------------------------------------------------------------
#  관리자 권한 확인 (서비스 등록/DB 생성 전제)
# ---------------------------------------------------------------------
function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p  = New-Object Security.Principal.WindowsPrincipal($id)
    return $p.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
}
if (-not (Test-Admin)) {
    Abort '관리자 권한 필요(서비스 등록/DB 생성). 관리자 PowerShell 로 재실행하십시오.'
}
Ok '관리자 권한 확인'

# =====================================================================
#  [1] 격리 디렉토리 생성 (이미 있으면 유지)
# =====================================================================
Step 1 "격리 디렉토리 준비: $TestRoot"
try {
    foreach ($d in @($TestRoot,
                     (Join-Path $TestRoot 'api'),
                     (Join-Path $TestRoot 'web'),
                     (Join-Path $TestRoot 'logs'),
                     (Join-Path $TestRoot 'backup'),
                     (Join-Path $TestRoot 'manifest'))) {
        if (-not (Test-Path $d)) {
            New-Item -ItemType Directory -Force $d | Out-Null
            Ok "생성: $d"
        } else {
            Ok "유지(이미 있음): $d"
        }
    }
} catch {
    Abort "격리 디렉토리 생성 실패: $($_.Exception.Message)"
}

# =====================================================================
#  [2] hitpan_e2e DB 생성 + clean DDL import (121테이블)
#   - 헌법 #36: installer/hitpan_db_clean.sql 단일 진실원만 import.
#   - 운영 hitpan_erp/demo 절대 미터치(가드 (2)에서 이미 차단됨).
#   - 이 스크립트는 어떤 DB도 DROP/DELETE 하지 않는다(운영 보호 핵심).
# =====================================================================
Step 2 "테스트 DB '$DbName' 생성 + clean DDL import"

# clean DDL 경로 해석(레포 루트 = 이 스크립트의 ..\..\.. )
$repoRoot = $null
try {
    # tests/scenarios/comms-integrity/Build-TestSlot.ps1 → 3단계 상위가 레포 루트
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
} catch {
    Warn "레포 루트 자동 해석 실패, 현재 위치 기준 시도: $($_.Exception.Message)"
    $repoRoot = (Get-Location).Path
}
$cleanDdl = Join-Path $repoRoot $CleanDdlRelPath
if (-not (Test-Path $cleanDdl)) {
    Abort "출하 DDL 단일 진실원을 찾을 수 없음(헌법 #36): $cleanDdl"
}
Ok "clean DDL 확인: $cleanDdl"

# DB 비밀번호는 평문 금지 — 환경변수에서 (deploy-demo.ps1 패턴)
$dbPw = $env:HITPAN_TEST_DB_PW
if ([string]::IsNullOrWhiteSpace($dbPw)) {
    Warn "환경변수 HITPAN_TEST_DB_PW 미설정. mysql 클라이언트가 대화형 암호를 물을 수 있습니다."
    Warn "  (평문 커밋 금지 — 실행 직전 사장님이 환경변수로 주입 권장)"
}

# mysql 클라이언트 확인
$mysqlCmd = (Get-Command mysql -ErrorAction SilentlyContinue)
if (-not $mysqlCmd) {
    Abort "mysql 클라이언트를 PATH 에서 찾을 수 없음. MariaDB 클라이언트 경로 확인 필요."
}
$mysqlExe = $mysqlCmd.Source
Ok "mysql 클라이언트: $mysqlExe"

# ── DB 생성 (운영 보호: DROP 절대 없음, CREATE DATABASE IF NOT EXISTS 만) ──
$pwArg = @()
if (-not [string]::IsNullOrWhiteSpace($dbPw)) { $pwArg = @("--password=$dbPw") }
$createSql = "CREATE DATABASE IF NOT EXISTS ``$DbName`` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
try {
    Write-Log 'INFO' "DB 생성 실행(IF NOT EXISTS, DROP 없음): $DbName @ $DbHost`:$DbPort"
    # ## 항목3 봉합: probe(가드)와 동일한 host/port 로 생성 → 같은 인스턴스 보장.
    & $mysqlExe "--host=$DbHost" "--port=$DbPort" "--user=$DbUser" @pwArg "--execute=$createSql"
    if ($LASTEXITCODE -ne 0) { throw "mysql CREATE DATABASE 종료코드 $LASTEXITCODE" }
    Ok "DB 생성/확인 완료: $DbName"
} catch {
    Abort "DB 생성 실패: $($_.Exception.Message)"
}

# ── clean DDL import ──
try {
    Write-Log 'INFO' "clean DDL import 시작 → $DbName"
    # cmd 리다이렉션으로 SQL 파일 주입(mysql 표준 패턴). 운영 DB명 아님은 가드 (2)에서 보장.
    # ## 항목3 봉합: host/port 명시 — 생성과 동일 인스턴스에 import.
    $pwInline = ($pwArg -join ' ')
    & cmd /c "`"$mysqlExe`" --host=$DbHost --port=$DbPort --user=$DbUser $pwInline $DbName < `"$cleanDdl`""
    if ($LASTEXITCODE -ne 0) { throw "mysql import 종료코드 $LASTEXITCODE" }
    Ok "clean DDL import 완료"
} catch {
    Abort "clean DDL import 실패: $($_.Exception.Message)"
}

# ── 121테이블 검증(ddl-smoke 1차) ──
try {
    $cntSql = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='$DbName' AND table_type='BASE TABLE';"
    # ## 항목3 봉합: host/port 명시 — 생성과 동일 인스턴스에서 검증.
    $raw = & $mysqlExe "--host=$DbHost" "--port=$DbPort" "--user=$DbUser" @pwArg --batch --skip-column-names "--execute=$cntSql"
    $tableCount = 0
    [int]::TryParse(("$raw").Trim(), [ref]$tableCount) | Out-Null
    if ($tableCount -lt 124) {   # 2026-06-29 schema_migrations 편입 123→124 (고리4 ①)
        Abort "테이블 수 검증 실패: 기대 124, 실제 $tableCount → import 불완전(헌법 #36)."
    }
    Ok "테이블 수 검증 통과: $tableCount (>= 124)"
} catch {
    Abort "테이블 수 검증 실패: $($_.Exception.Message)"
}

# =====================================================================
#  [3] API(15257)/Web(15234) test 슬롯 기동 준비
#   - 실제 산출물 배치/빌드는 deploy 단계(별도) 또는 사장님 결재 후.
#   - 여기서는 포트 충돌 점검 + 디렉토리 표식만(준비물 A갈래).
# =====================================================================
Step 3 "test 슬롯 포트 점검(API=$ApiPort / Web=$WebPort)"
function Test-PortFree {
    param([int]$Port, [string]$Label)
    try {
        $busy = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue
        if ($busy) {
            $pids = (($busy.OwningProcess | Select-Object -Unique) -join ',')
            Warn "$Label 포트 $Port 가 이미 점유 중입니다(점유 PID: $pids)."
            Warn "  demo 포트(5234/5257)가 아닌지 확인하고, test 슬롯과 충돌하면 정리 후 기동하십시오."
        } else {
            Ok "$Label 포트 $Port 비어있음"
        }
    } catch {
        # 헌법 #15
        Write-Log 'INFO' "$Label 포트 $Port 점검 중 예외(무시 가능): $($_.Exception.Message)"
    }
}
Test-PortFree -Port $ApiPort -Label 'API'
Test-PortFree -Port $WebPort -Label 'Web'
Ok "기동 산출물 배치는 별도 deploy 단계/사장님 결재 후 진행(이 스크립트는 베이스만 구축)"

# =====================================================================
#  [4] db.conf 생성 (PRIMARY_DOMAIN / API_PORT / SLOT)
#   - Run-CommsScenarios.ps1 의 2차 방어가 읽는 KEY=VALUE 형식.
#   - demo 도메인이면 abort(가드 (2)에서 이미 차단되지만 직전 재확인).
# =====================================================================
Step 4 "db.conf 생성: $DbConfPath"
if ($PrimaryDomain -match '(?i)demo') {
    Abort "db.conf 생성 직전 demo 도메인 재감지: '$PrimaryDomain' → 차단(헌법 #39)."
}
# ## M2 봉합(2026-06-30, 작3) + R1 재봉합(작3v2, 하브루타 합의):
#    db.conf 를 격리 루트($TestRoot) 하위에만 쓴다(덮어쓰기 물리 차단).
#    [작3v2 R1] 과거 블랙리스트 정규식('Program Files\HitPan')은 8.3단축(PROGRA~1)·다른드라이브(D:\HitPan)·
#    심볼릭링크로 우회됐다(고객 설치경로 가변: iss DisableDirPage 미설정). → 화이트리스트로 역전:
#    GetFullPath 로 정규화한 db.conf 경로가 격리 루트 하위가 아니면 abort. 우회 경로 전부 차단.
$fullConf = [System.IO.Path]::GetFullPath($DbConfPath)
# [작3v2 R1 경계버그 보정] 루트 끝에 구분자를 붙여 형제경로 오통과 차단.
#   (보정 전: StartsWith('C:\HitPanTest') 가 'C:\HitPanTest2\db.conf' 도 통과시킴 — 데이비드박 재검증 P1)
$fullRoot = [System.IO.Path]::GetFullPath($TestRoot).TrimEnd('\') + '\'
if (-not $fullConf.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    Abort "db.conf 경로가 격리 루트($fullRoot) 하위가 아닙니다: '$fullConf' → 운영 db.conf 덮어쓰기 차단(R1 화이트리스트, 헌법 #39). 격리 경로(C:\HitPanTest\db.conf)만 허용."
}
try {
    $dbConfDir = Split-Path $DbConfPath -Parent
    if (-not (Test-Path $dbConfDir)) {
        New-Item -ItemType Directory -Force $dbConfDir | Out-Null
        Ok "db.conf 디렉토리 생성: $dbConfDir"
    }
    # KEY=VALUE 형식(hitpan-start.bat / Run-CommsScenarios.ps1 파서 정합). 평문 비번 미기록.
    $dbConfLines = @(
        "# ===== HitPan TEST 슬롯 db.conf (Build-TestSlot.ps1 생성) =====",
        "# 운영(demo)에는 절대 이 파일을 두지 않는다(헌법 #39).",
        "PRIMARY_DOMAIN=$PrimaryDomain",
        "API_PORT=$ApiPort",
        "SLOT=$Slot",
        "DB_NAME=$DbName"
    )
    [System.IO.File]::WriteAllLines($DbConfPath, $dbConfLines, (New-Object System.Text.UTF8Encoding($false)))
    Ok "db.conf 작성 완료(PRIMARY_DOMAIN=$PrimaryDomain / API_PORT=$ApiPort / SLOT=$Slot)"
} catch {
    Abort "db.conf 작성 실패: $($_.Exception.Message)"
}

# =====================================================================
#  [5] HITPAN_TEST_ENV.marker 생성 (README 스펙 정확히)
#   - fail-closed 안전망의 화이트리스트 키. demo 엔 절대 없음.
#   - Run-CommsScenarios.ps1 이 ENV=TEST / 서비스명을 이 파일에서만 읽는다.
# =====================================================================
$markerPath = Join-Path $TestRoot 'HITPAN_TEST_ENV.marker'
Step 5 "test 마커 생성: $markerPath"
try {
    # README 스펙 그대로(키 순서/이름 정확히). ENV=TEST 줄 없으면 소비 스크립트가 차단함.
    $markerLines = @(
        "ENV=TEST",
        "SLOT=$Slot",
        "CLOUDFLARED_SVC=$CloudflaredSvc",
        "MARIADB_SVC=$MariaDbSvc"
    )
    [System.IO.File]::WriteAllLines($markerPath, $markerLines, (New-Object System.Text.UTF8Encoding($false)))
    Ok "마커 작성 완료(ENV=TEST / SLOT=$Slot / CLOUDFLARED_SVC=$CloudflaredSvc / MARIADB_SVC=$MariaDbSvc)"
} catch {
    Abort "마커 작성 실패: $($_.Exception.Message)"
}

# =====================================================================
#  [6] test 전용 서비스 등록 (cloudflared-test / MariaDB-test)
# ---------------------------------------------------------------------
#  ## 헌법 #29 영역 — sc/net 으로 Windows 서비스를 등록합니다.
#       실행 = 인프라 조작. 반드시 사장님 입회/승인 하에서만.
#  ## 안전: -test 접미사 없는 서비스명은 가드 (2)에서 이미 abort 됨.
#       demo 공통 서비스(cloudflared/MariaDB)는 이 코드가 절대 못 만진다.
#  ## 실제 바이너리 경로(cloudflared.exe / mariadbd.exe), 터널 자격증명은
#     B갈래(터널 토큰 주입, IssueTunnelAsync)에서 확정되므로,
#     여기서는 자격증명/경로가 없으면 등록을 SKIP 한다(미결 의존 명시).
# =====================================================================
Step 6 "test 전용 서비스 등록 (헌법 #29 — 사장님 입회 필요)"
Warn "이 단계는 헌법 #29 인프라 조작입니다. 사장님 입회/승인이 없으면 여기서 멈추십시오."

# --- MariaDB-test 서비스 ---
# 운영 MariaDB 와 다른 datadir/포트로 격리된 인스턴스를 가리켜야 함.
# datadir/my.ini 경로는 사장님 결재 시 확정(B갈래). 미확정이면 등록 SKIP.
$mariadbExe = $env:HITPAN_TEST_MARIADBD      # 예: C:\HitPanTest\mariadb\bin\mariadbd.exe
$mariadbIni = $env:HITPAN_TEST_MARIADB_INI   # 예: C:\HitPanTest\mariadb\my.ini
if ([string]::IsNullOrWhiteSpace($mariadbExe) -or [string]::IsNullOrWhiteSpace($mariadbIni)) {
    Warn "MariaDB-test 서비스: 격리 인스턴스 경로(HITPAN_TEST_MARIADBD/INI) 미설정 → 등록 SKIP(B갈래 의존)."
} else {
    try {
        $existing = Get-Service -Name $MariaDbSvc -ErrorAction SilentlyContinue
        if ($existing) {
            Ok "서비스 '$MariaDbSvc' 이미 존재 — 등록 생략(유지)."
        } else {
            Write-Log 'INFO' "서비스 등록(헌법 #29): $MariaDbSvc"
            # mariadbd 표준 등록: --install <svcname> --defaults-file=<ini>
            & "$mariadbExe" --install $MariaDbSvc --defaults-file="$mariadbIni"
            if ($LASTEXITCODE -ne 0) { throw "mariadbd --install 종료코드 $LASTEXITCODE" }
            Ok "MariaDB-test 서비스 등록 완료: $MariaDbSvc"
        }
    } catch {
        Abort "MariaDB-test 서비스 등록 실패: $($_.Exception.Message)"
    }
}

# --- cloudflared-test 서비스 ---
# 터널 토큰/자격증명은 B갈래(IssueTunnelAsync) 산출물. 없으면 등록 SKIP.
$cfExe   = $env:HITPAN_TEST_CLOUDFLARED      # 예: C:\HitPanTest\cloudflared\cloudflared.exe
$cfToken = $env:HITPAN_TEST_TUNNEL_TOKEN     # B갈래: test-tunnel 토큰(평문 커밋 금지)
if ([string]::IsNullOrWhiteSpace($cfExe) -or [string]::IsNullOrWhiteSpace($cfToken)) {
    Warn "cloudflared-test 서비스: 바이너리/터널 토큰(HITPAN_TEST_CLOUDFLARED/TOKEN) 미설정 → 등록 SKIP(B갈래 의존)."
    Warn "  ## 터널 토큰 주입 = B갈래(작2 3-2, IssueTunnelAsync). 사장님 #29 결재 후 발급."
} else {
    try {
        $existing = Get-Service -Name $CloudflaredSvc -ErrorAction SilentlyContinue
        if ($existing) {
            Ok "서비스 '$CloudflaredSvc' 이미 존재 — 등록 생략(유지)."
        } else {
            Write-Log 'INFO' "서비스 등록(헌법 #29): $CloudflaredSvc"
            # cloudflared 는 기본 서비스명이 'cloudflared' 로 고정되므로, test 격리를 위해
            # sc.exe 로 별도 이름(cloudflared-test)으로 등록한다(결재 시 binPath 확정).
            & sc.exe create $CloudflaredSvc binPath= "`"$cfExe`" tunnel run --token $cfToken" start= auto
            if ($LASTEXITCODE -ne 0) { throw "sc create 종료코드 $LASTEXITCODE" }
            Ok "cloudflared-test 서비스 등록 완료: $CloudflaredSvc"
        }
    } catch {
        Abort "cloudflared-test 서비스 등록 실패: $($_.Exception.Message)"
    }
}

# =====================================================================
#  완료 요약
# =====================================================================
Step 'DONE' 'test 슬롯 구축 베이스 완료'
Ok "격리 루트   : $TestRoot"
Ok "테스트 DB   : $DbName (clean DDL import 완료, 테이블 검증 통과)"
Ok "포트        : API=$ApiPort / Web=$WebPort (demo 5234/5257 격리)"
Ok "db.conf     : $DbConfPath (PRIMARY_DOMAIN=$PrimaryDomain)"
Ok "마커        : $markerPath (ENV=TEST)"
Warn "남은 단계(B갈래/별도):"
Warn "  - test-tunnel 발급 + 터널 토큰 주입(IssueTunnelAsync, 헌법 #29 사장님)"
Warn "  - API/Web 산출물 배치/기동(deploy 단계)"
Warn "  - 반증 테스트 4건(데이비드 박) 통과 후에만 Run-CommsScenarios.ps1 -Confirm 실측"

Save-Log
Write-Host ""
Write-Host "[완료] Build-TestSlot — 17시나리오/업데이트 검증용 베이스 준비됨." -ForegroundColor Green
