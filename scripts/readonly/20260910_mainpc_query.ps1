# =====================================================================
#  히트판 메인PC 식별·등록 — 읽기 전용 조회 (2026-09-10)
#  검증팀장 데이비드 박 작성 · 게시 전 필수(하브루타 결재 4·6 판정 근거)
#
#  [사용법 — 3줄]
#   1) 시작 단추 > "Windows PowerShell" 에서 마우스 우클릭 > "관리자 권한으로 실행"
#   2) 이 파일을 그 창에 끌어다 놓고 Enter (또는:  powershell -ExecutionPolicy Bypass -File "<이 파일 경로>")
#   3) 끝나면 바탕화면에 생기는  hitpan_mainpc_query_날짜시각.txt  파일만 PM 에게 주시면 됩니다.
#
#  이 스크립트는 데이터를 조회(읽기)만 합니다 — 무엇도 바꾸거나 지우지 않습니다.
#  비밀번호는 화면·파일 어디에도 찍지 않습니다(MYSQL_PWD 환경변수로만 전달, 끝나면 지움).
#  PowerShell 5.1 호환.
# =====================================================================

# 🔴 R-1: 'Stop' 로 두지 않는다. PowerShell 5.1 에서 네이티브 stderr 를 2>&1 로 합치면
#   그 한 줄이 ErrorRecord 가 되고, Stop 이면 문항 하나만 실패해도 그 자리에서 멈춰
#   뒤 문항이 전부 안 돈다(사장님이 다시 돌리셔야 함). 조회 루프는 계속 돌게 둔다.
$ErrorActionPreference = 'Continue'

function Read-ConfFile {
    # db.conf 를 히트판 본체(TenantConfigReader)와 같은 방식으로 읽는다:
    #   빈 줄·'#' 로 시작하는 줄은 건너뛰고, 첫 '=' 를 기준으로 key/value 를 나눈다.
    param([string]$Path)
    $map = @{}
    if (-not (Test-Path -LiteralPath $Path)) { return $map }
    foreach ($line in Get-Content -LiteralPath $Path) {
        $t = $line.Trim()
        if ([string]::IsNullOrEmpty($t)) { continue }
        if ($t.StartsWith('#')) { continue }
        $i = $t.IndexOf('=')
        if ($i -le 0) { continue }
        $k = $t.Substring(0, $i).Trim()
        $v = $t.Substring($i + 1).Trim()
        $map[$k] = $v
    }
    return $map
}

function Find-MysqlExe {
    # 설치본이 쓰는 경로 우선(HitPan-Universal.iss:272·2101 · BackupService fallback), 그다음 PATH.
    $candidates = @(
        'C:\Program Files\MariaDB 11.4\bin\mysql.exe',
        'C:\Program Files\MariaDB 10.11\bin\mysql.exe',
        'C:\Program Files\MariaDB 11.7\bin\mysql.exe',
        'C:\Program Files\MariaDB 11.4\bin\mariadb.exe'
    )
    foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { return $c } }
    $cmd = Get-Command mysql.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $cmd2 = Get-Command mariadb.exe -ErrorAction SilentlyContinue
    if ($cmd2) { return $cmd2.Source }
    return $null
}

# ── 1) 접속 정보 확보 ───────────────────────────────────────────────
$confPath = 'C:\Program Files\HitPan\db.conf'
if (-not (Test-Path -LiteralPath $confPath)) {
    Write-Host "[중단] db.conf 를 찾을 수 없습니다: $confPath"
    Write-Host "       이 창이 '관리자 권한'으로 열렸는지 확인해 주세요."
    return
}
$conf = Read-ConfFile -Path $confPath

$dbHost = $conf['DB_HOST']; if ([string]::IsNullOrEmpty($dbHost)) { $dbHost = 'localhost' }
$dbPort = $conf['DB_PORT']; if ([string]::IsNullOrEmpty($dbPort)) { $dbPort = '3306' }
$dbName = $conf['DB_NAME']
$dbUser = $conf['DB_USER']
$dbPass = $conf['DB_PASSWORD']

# DB 이름 교차 확인 (registry.json — 하드코딩하지 않는다)
$regPath = 'C:\ProgramData\HitPan\registry.json'
$regDbName = $null
if (Test-Path -LiteralPath $regPath) {
    try {
        $reg = Get-Content -LiteralPath $regPath -Raw | ConvertFrom-Json
        if ($reg.tenants -and $reg.tenants.Count -ge 1) { $regDbName = $reg.tenants[0].dbName }
    } catch { $regDbName = $null }
}
if ([string]::IsNullOrEmpty($dbName)) { $dbName = $regDbName }

if ([string]::IsNullOrEmpty($dbName) -or [string]::IsNullOrEmpty($dbUser) -or [string]::IsNullOrEmpty($dbPass)) {
    Write-Host "[중단] db.conf 에서 DB_NAME/DB_USER/DB_PASSWORD 를 읽지 못했습니다."
    return
}

$mysql = Find-MysqlExe
if ($null -eq $mysql) {
    Write-Host "[중단] mysql 실행파일을 찾지 못했습니다 (MariaDB 설치 경로 확인)."
    return
}

# ── 2) 출력 파일 ────────────────────────────────────────────────────
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$desktop = [Environment]::GetFolderPath('Desktop')
$outFile = Join-Path $desktop ("hitpan_mainpc_query_{0}.txt" -f $stamp)

# ── 3) 조회 문항 (전부 SELECT · 읽기 전용) ──────────────────────────
$queries = @(
  @{ title = 'Q1) 기기 줄 전부 - 몇 줄인가 / 메인PC 표식 / 신분(1=서버줄 MAINPC- / 2=브라우저줄 HFPv2-)';
     sql   = "SELECT device_id, LEFT(fingerprint,14) AS fp, device_type, is_main_pc, status, user_id, (auth_key_hash IS NOT NULL) AS has_key, device_name, registered_at, approved_at, last_seen_at, revoked_at, revoked_reason FROM tenant_devices ORDER BY registered_at;" },

  @{ title = 'Q1b) 메인PC 표식 줄 수 (0/1/2+ 판정 - 게시 전 필수)';
     sql   = "SELECT is_main_pc, status, COUNT(*) AS n FROM tenant_devices GROUP BY is_main_pc, status ORDER BY is_main_pc DESC, status;" },

  @{ title = 'Q2) 슬롯 계수 (히트판 본체와 같은 축)';
     sql   = "SELECT device_type, status, COUNT(*) AS n FROM tenant_devices GROUP BY device_type, status;" },

  # 정정(2026-09-10 병렬이슈20): 옛 제목 「표식 이동 감사 기록 (한 번이라도 옮겨졌나 / 합류했나)」는 오해를 부른다 —
  #   로그인 안에서 쓰는 감사는 실물에서 0행이라(AuditService.cs:37) 표식 이동이 돌았어도 여기 안 남는다. 판정은 Q1 줄 상태로.
  @{ title = 'Q3) 기기 감사 기록 — 참고용 · 로그인 중 일어난 일(표식 이동 등)은 여기 안 남음 · 판정은 Q1';
     sql   = "SELECT created_at, action_type, entity_id, reason FROM audit_trail WHERE entity_type = 'device' AND action_type LIKE 'device_%' ORDER BY created_at;" },

  @{ title = 'Q4) 이관 이력 - 실물이 이미 이관된 DB 인가 (빈 DB 면 전부 0)';
     sql   = "SELECT (SELECT COUNT(*) FROM migration_jobs) AS jobs, (SELECT COUNT(*) FROM tax_invoices WHERE source_type = 'migration') AS mig_invoices, (SELECT COUNT(*) FROM hr_reports) AS hr_reports;" },

  @{ title = 'Q5) 대표 계정 / is_parent (헌법 #40 - 파괴급 권한 결재 근거)';
     sql   = "SELECT account_type, is_parent, COUNT(*) AS n FROM users WHERE is_deleted = 0 GROUP BY account_type, is_parent ORDER BY account_type, is_parent;" },

  @{ title = 'Q6) 마이그 기록표 - DB-89 행의 checksum 이 비었나(무결성 미산출 여부)';
     sql   = "SELECT migration_id, app_version, checksum, success, applied_at FROM schema_migrations WHERE migration_id IN ('DB-86','DB-89','DB-103','DB-104','DB-119') ORDER BY id;" },

  @{ title = 'Q7) DB-89 보호(메인PC 1줄 보장) 구조가 실물에 있나 - main_pc_key 컬럼';
     sql   = "SELECT column_name FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'tenant_devices' AND column_name = 'main_pc_key';" },

  @{ title = 'Q8) DB-89 보호 - uq_tenant_main_pc 유일키 존재 여부';
     sql   = "SELECT index_name FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = 'tenant_devices' AND index_name = 'uq_tenant_main_pc';" }
)

# ── 4) 실행 ─────────────────────────────────────────────────────────
$header = @()
$header += "히트판 메인PC 식별.등록 조회 결과"
$header += ("생성 시각 : {0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
$header += ("DB 이름   : {0}" -f $dbName)
if (-not [string]::IsNullOrEmpty($regDbName) -and ($regDbName -ne $dbName)) {
    $header += ("  (registry.json 의 dbName = {0} - 값이 다르니 PM 에게 알려 주세요)" -f $regDbName)
}
$header += ("mysql     : {0}" -f $mysql)
$header += ("컴퓨터    : {0}" -f $env:COMPUTERNAME)
$header += "----------------------------------------------------------------------"
$header | Out-File -LiteralPath $outFile -Encoding utf8

$env:MYSQL_PWD = $dbPass    # 비밀번호는 명령줄 인자로 넘기지 않는다(프로세스 목록 노출 방지)
try {
    foreach ($q in $queries) {
        ("`r`n==== " + $q.title + " ====") | Out-File -LiteralPath $outFile -Encoding utf8 -Append
        # 🔴 R-2: $args 는 PowerShell 자동 변수라 이름을 바꾼다.
        $mysqlArgs = @('-h', $dbHost, '-P', $dbPort, '-u', $dbUser, '--default-character-set=utf8mb4', '-t', $dbName, '-e', $q.sql)
        $result = & $mysql @mysqlArgs 2>&1 | Out-String
        $code = $LASTEXITCODE   # 이 문항이 실패했는지 파일에 남긴다(다음 문항은 계속 돈다 · R-1)
        if ($code -ne 0) {
            ("[이 문항 실패 exit=" + $code + " — 아래 메시지 확인 · 나머지 문항은 계속 진행됩니다]") | Out-File -LiteralPath $outFile -Encoding utf8 -Append
        }
        $result | Out-File -LiteralPath $outFile -Encoding utf8 -Append
    }
}
finally {
    Remove-Item Env:\MYSQL_PWD -ErrorAction SilentlyContinue   # 환경변수 즉시 제거
}

Write-Host ""
Write-Host "완료. 결과 파일:"
Write-Host "  $outFile"
Write-Host "이 파일만 PM 에게 전달해 주세요. (비밀번호는 파일에 들어 있지 않습니다.)"
