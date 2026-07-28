# ============================================================================
# M4 샌드박스 검증 셋업 (작5, 사장님 결재 2026-06-30) — 샌드박스 *안*에서 실행
# ----------------------------------------------------------------------------
# ① 고리4 P1 멱등 검증 (빈 DB → MigrationRunner 2회 → 멱등 확인)
# ② 종단 빌드테스트는 사장님이 화면 보며 단계 진행(캡처 목적) — 이 스크립트는 ① 자동화 담당.
#
# 전제: 종단 EXE(C:\dist\HitPan-ERP-Setup-1.2.15.exe) 설치가 먼저 끝나 MariaDB 가 깔려 있어야 한다.
#       (EXE 가 MariaDB 11.4 MSI 동봉 — 헌법 #31). 설치 전이면 이 스크립트가 안내 후 멈춘다.
#
# 운영 무접촉: 샌드박스는 호스트 3306 에 못 닿는다(물리 격리). 여기 MariaDB 는 샌드박스 안 신규 인스턴스.
# 헌법 #39 검증=테스트환경 / #15 실패 로그 / #32 부풀림 금지(멱등 깨지면 FAIL 그대로).
# ============================================================================

$ErrorActionPreference = 'Stop'
$ok = $true
function Say($m)  { Write-Host "[M4] $m" -ForegroundColor Cyan }
function Good($m) { Write-Host "[OK] $m" -ForegroundColor Green }
function Bad($m)  { Write-Host "[FAIL] $m" -ForegroundColor Red; $script:ok = $false }

Say "==================================================================="
Say " M4 샌드박스 검증 — ① 고리4 P1 멱등"
Say "==================================================================="

# ── [전제 확인] MariaDB 클라이언트가 PATH 에 있나 (EXE 설치가 깔았어야) ────────────
$mysqlExe = $null
foreach ($p in @(
    "C:\Program Files\MariaDB 11.4\bin\mysql.exe",
    "C:\Program Files\HitPan\MariaDB\bin\mysql.exe")) {
    if (Test-Path $p) { $mysqlExe = $p; break }
}
if (-not $mysqlExe) {
    $cmd = Get-Command mysql.exe -ErrorAction SilentlyContinue
    if ($cmd) { $mysqlExe = $cmd.Source }
}
if (-not $mysqlExe) {
    Bad "MariaDB(mysql.exe) 를 못 찾음. 먼저 C:\dist\HitPan-ERP-Setup-1.2.15.exe 를 설치해 MariaDB 를 깔고 다시 실행하십시오."
    Say "종단 빌드테스트(②)도 그 EXE 설치부터 시작합니다 — 읽어주세요_M4검증순서.txt 참조."
    exit 1
}
Good "MariaDB 클라이언트: $mysqlExe"

# ── [DB 설정] 샌드박스 안 빈 테스트 DB (운영 이름 금지 — 멱등도구 가드와 정합) ──────────
$dbHost = '127.0.0.1'
$dbPort = '3306'           # 샌드박스 안 MariaDB(호스트 3306 아님 — 격리)
$dbUser = 'hitpan'
$dbPw   = 'Hitpan2025!'    # 설치본 기본값(install.bat 실측) — 샌드박스 내부 전용, 운영 아님
$dbName = 'hitpan_m4'      # ★ 테스트 전용. 멱등도구가 hitpan_erp/backoffice/demo 면 ABORT.
$env:MYSQL_PWD = $dbPw

# ── [1] 빈 테스트 DB 생성 ────────────────────────────────────────────────────
Say "[1] 빈 테스트 DB 생성: $dbName"
$createSql = "DROP DATABASE IF EXISTS $dbName; CREATE DATABASE $dbName CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
& $mysqlExe "--host=$dbHost" "--port=$dbPort" "--user=$dbUser" "--execute=$createSql"
if ($LASTEXITCODE -ne 0) { Bad "DB 생성 실패(MariaDB 비번·기동 확인)"; exit 1 }
Good "빈 DB 생성: $dbName"

# ── [2] clean DDL import (124테이블 — 출하 단일 진실원, 헌법 #36) ────────────────
$cleanDdl = "C:\m4\hitpan_db_clean.sql"
if (-not (Test-Path $cleanDdl)) { Bad "clean DDL 없음: $cleanDdl (payload 누락)"; exit 1 }
Say "[2] clean DDL import (124테이블) ..."
# PowerShell 은 입력 리다이렉션('<')을 예약어로 막으므로, cmd 명령줄을 문자열로 조립해 cmd /c 로 넘긴다.
# (단일 변수 인자라 PowerShell 파서가 '<' 를 해석하지 않음 — cmd 가 리다이렉션 처리)
$importCmd = '"' + $mysqlExe + '" --host=' + $dbHost + ' --port=' + $dbPort +
             ' --user=' + $dbUser + ' ' + $dbName + ' < "' + $cleanDdl + '"'
cmd /c $importCmd
if ($LASTEXITCODE -ne 0) { Bad "clean DDL import 실패"; exit 1 }
$countSql = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='$dbName' AND table_type='BASE TABLE';"
$tblCount = (& $mysqlExe "--host=$dbHost" "--port=$dbPort" "--user=$dbUser" "--batch" "--skip-column-names" "--execute=$countSql").Trim()
Say "    import 후 테이블 수: $tblCount"
if ([int]$tblCount -lt 124) { Bad "테이블 수 124 미만($tblCount) — clean DDL 불완전"; exit 1 }
Good "clean DDL import: $tblCount 테이블"

# ── [3] 멱등 도구 실행 (MigrationRunner 2회 → 멱등) ────────────────────────────
$tool = "C:\m4\app\MigrationIdempotencyCheck.exe"
if (-not (Test-Path $tool)) { Bad "멱등 도구 없음: $tool (payload 누락)"; exit 1 }
Say "[3] 고리4 P1 멱등 검증 실행 ..."
$env:DB_HOST = $dbHost; $env:DB_PORT = $dbPort
$env:DB_USER = $dbUser; $env:DB_PASSWORD = $dbPw; $env:DB_NAME = $dbName
& $tool
$rc = $LASTEXITCODE
if ($rc -eq 0) { Good "① P1 멱등 검증 PASS (위 'PASS' 출력 확인)" }
else           { Bad "① P1 멱등 검증 FAIL (종료코드 $rc) — 위 빨간 줄 캡처해 PM 에게" }

# ── [결과] ───────────────────────────────────────────────────────────────────
Write-Host ""
if ($ok) {
    Write-Host "============================================" -ForegroundColor Green
    Write-Host " ① 고리4 P1 멱등 = PASS" -ForegroundColor Green
    Write-Host " 다음: ② 종단 빌드테스트 — 읽어주세요_M4검증순서.txt 참조" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
} else {
    Write-Host "============================================" -ForegroundColor Red
    Write-Host " 실패 발생 — 위 빨간 [FAIL] 줄 전체를 캡처해 PM 에게 보여주십시오(헌법 #39 방치 금지)." -ForegroundColor Red
    Write-Host "============================================" -ForegroundColor Red
}
$env:MYSQL_PWD = $null
