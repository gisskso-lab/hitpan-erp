# =============================================================================
#  deploy-demo.ps1  —  demo 정합 배포 스크립트 (초안 / DRAFT)
# =============================================================================
#  ⚠️⚠️⚠️  실행 금지 (DO NOT RUN)  ⚠️⚠️⚠️
#
#  본 파일은 헌법 #39(운영 직접수술 금지) 대책 2·3·4의 *설계 초안*이다.
#  - demo(사장님 PC 운영 환경)에 절대 실행하지 않는다.
#  - 실행은 SOP대로 [4]CTO결재 → [5]사장님 승인 → 테스트환경 dry-run 통과 후에만.
#  - 경로·포트·커밋 핀은 결재 시 확정한다(아래 CONFIG는 인수인계서 실측 기본값).
#
#  설계서: docs/handoff/20260626작4_deploy-demo-ps1_정합배포_설계서_작업지시서.md
#  목적: Web/API/DB/키를 따로 손대다 정합 깨지는 어제 사고를 단일 정합 배포로 차단.
#        한쪽만 옛 버전 = 캘린더 사고 원천 차단(같은 커밋 동시 빌드).
#        실패 시 백업 자동 롤백 = "망가진 채 방치" 차단.
# =============================================================================

[CmdletBinding()]
param(
    # 안전장치: 실제 실행하려면 명시적으로 -Confirm 줘야 함. 기본은 dry-run.
    [switch]$Execute,
    # 같은 커밋에서 Web/API 동시 빌드 강제(버전 정합 ★). 미지정 시 현재 HEAD.
    [string]$Commit = ""
)

$ErrorActionPreference = "Stop"

# -----------------------------------------------------------------------------
# CONFIG (인수인계서 실측값 — 결재 시 확정)
# -----------------------------------------------------------------------------
$CFG = @{
    # ASCII 경로 강제(헌법 #39 요건 4 — 한글 경로 깨짐 차단)
    DeployRoot   = "C:\HitPanDeploy"
    WebDir       = "C:\HitPanDeploy\web\wwwroot"
    ApiDir       = "C:\HitPanDeploy\api"
    BackupRoot   = "C:\HitPanDeploy\backup"

    WebPort      = 5234
    ApiPort      = 5257

    DbName       = "hitpan_erp"
    DbUser       = "hitpan"          # 비밀번호는 환경변수/키파일에서 — 스크립트 평문 금지

    # 키 단일 진실원(헌법 #39 요건 3 — db.conf 즉석생성 금지)
    KeyConf      = "C:\Program Files\HitPan\hitpan-keys.conf"   # ERP_ENCRYPTION_KEY

    # 소스 프로젝트 경로
    WebProj      = "src\HitPan.Web\HitPan.Web.csproj"
    ApiProj      = "src\HitPan.API\HitPan.API.csproj"

    # 스모크 대상(약관 동의된 검증 계정 — 테스트 환경에서 주입)
    SmokeBaseUrl = "http://localhost:5257"
}

function Step($n, $msg) { Write-Host "`n[$n] $msg" -ForegroundColor Cyan }
function Ok($msg)       { Write-Host "    OK  $msg" -ForegroundColor Green }
function Fail($msg)     { Write-Host "    FAIL $msg" -ForegroundColor Red }

# =============================================================================
# [0] 사전 가드
# =============================================================================
function Invoke-Preflight {
    Step 0 "사전 가드"

    # 관리자 권한(서비스 정지/기동 — 헌법 #29 인프라 조작, 사장님 승인 전제)
    $isAdmin = ([Security.Principal.WindowsPrincipal] `
        [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
    if (-not $isAdmin) { throw "관리자 권한 필요" }
    Ok "관리자 권한"

    # ASCII 경로 확인(한글 깨짐 차단)
    foreach ($p in @($CFG.DeployRoot, $CFG.WebDir, $CFG.ApiDir, $CFG.BackupRoot)) {
        if ($p -match '[^\x00-\x7F]') { throw "비ASCII 경로 금지: $p" }
    }
    Ok "ASCII 경로"

    # 키 진실원 존재 확인(즉석 db.conf 생성 금지)
    if (-not (Test-Path $CFG.KeyConf)) { throw "키 진실원 없음: $($CFG.KeyConf)" }
    Ok "키 진실원 존재"

    # 같은 커밋 빌드 강제(버전 정합 ★)
    if ($Commit) {
        git checkout $Commit 2>&1 | Out-Null
        Ok "커밋 핀: $Commit"
    } else {
        $head = (git rev-parse --short HEAD).Trim()
        Ok "현재 HEAD: $head (Web/API 동일 커밋 보장)"
    }
}

# =============================================================================
# [1] 백업 (헌법 #39 요건 2 — 손대기 전 자동 백업, 실패 시 복원 원천)
# =============================================================================
function Invoke-Backup {
    param([string]$Stamp)
    Step 1 "백업"
    $dir = Join-Path $CFG.BackupRoot $Stamp
    New-Item -ItemType Directory -Force $dir | Out-Null

    # DB 덤프
    & mysqldump --user=$($CFG.DbUser) $CFG.DbName > (Join-Path $dir "db.sql")
    Ok "DB 덤프"

    # 현재 Web/API 폴더 백업
    if (Test-Path $CFG.WebDir) { Copy-Item $CFG.WebDir (Join-Path $dir "web") -Recurse -Force }
    if (Test-Path $CFG.ApiDir) { Copy-Item $CFG.ApiDir (Join-Path $dir "api") -Recurse -Force }
    Ok "Web/API 폴더 백업 → $dir"
    return $dir
}

# =============================================================================
# [2] 빌드 — Web + API 같은 커밋 동시 publish (버전 정합 ★)
# =============================================================================
function Invoke-Build {
    Step 2 "빌드(같은 커밋 Web+API 동시 publish)"
    # 헌법 #19: errors 0 + warnings 0
    & dotnet publish $CFG.ApiProj -c Release -o "$($CFG.DeployRoot)\_stage\api"  /warnaserror
    Ok "API publish"
    & dotnet publish $CFG.WebProj -c Release -o "$($CFG.DeployRoot)\_stage\web"  /warnaserror
    Ok "Web publish"
}

# =============================================================================
# [3] 정지  (헌법 #29 서비스 조작 — 사장님 승인 전제)
# =============================================================================
function Invoke-Stop {
    Step 3 "기존 프로세스 정지"
    # 포트 점유 프로세스 graceful stop (구현 시 PID 추적으로 정밀 종료)
    Ok "(초안) Web 5234 / API 5257 graceful stop 자리"
}

# =============================================================================
# [4] 교체 (ASCII 경로, 백업 보존하며 신규 배치)
# =============================================================================
function Invoke-Swap {
    Step 4 "산출물 교체"
    Copy-Item "$($CFG.DeployRoot)\_stage\api\*" $CFG.ApiDir -Recurse -Force
    Copy-Item "$($CFG.DeployRoot)\_stage\web\wwwroot\*" $CFG.WebDir -Recurse -Force
    Ok "교체 완료"
}

# =============================================================================
# [5] 키 정합 (단일 진실원 주입 — db.conf 즉석생성 금지)
# =============================================================================
function Invoke-KeyAlign {
    Step 5 "키 정합(ERP_ENCRYPTION_KEY 주입)"
    # hitpan-keys.conf 의 ERP_ENCRYPTION_KEY 를 API 환경변수로 주입.
    # PM 환경변수 키와 섞이지 않게 이 파일 하나만 진실원으로 사용.
    Ok "(초안) keys.conf → API 환경변수 주입 자리"
}

# =============================================================================
# [6] 기동 (API → Web 순서 + 헬스 대기)
# =============================================================================
function Invoke-Start {
    Step 6 "기동(API→Web)"
    Ok "(초안) API 5257 기동 → 헬스 대기 → Web 5234 기동 자리"
}

# =============================================================================
# [7] 정합 게이트 — 스모크 5종 (하나라도 실패 시 throw → 자동 롤백)
# =============================================================================
function Invoke-Smoke {
    Step 7 "정합 게이트(스모크 5종)"
    $base = $CFG.SmokeBaseUrl

    # 1) 로그인 → 토큰
    $login = Invoke-RestMethod -Method Post -Uri "$base/api/auth/login" `
        -ContentType "application/json" `
        -Body (@{ email = "<smoke-account>"; password = "<smoke-pw>" } | ConvertTo-Json)
    if (-not $login.token) { throw "스모크1 로그인 실패" }
    Ok "1. 로그인 200"
    $h = @{ Authorization = "Bearer $($login.token)" }

    # 2) /me 복호화(키 정합 증명)
    $null = Invoke-RestMethod -Uri "$base/api/auth/me" -Headers $h
    Ok "2. /me 200 (복호화 성공 = 키 정합)"

    # 3) 캘린더 JSON(버전 정합 증명 — HTML 폴백이면 실패)
    $cal = Invoke-WebRequest -Uri "$base/api/dashboard/unified-calendar" -Headers $h
    if ($cal.Content.TrimStart().StartsWith("<")) { throw "스모크3 캘린더 HTML 폴백 = API 옛버전" }
    Ok "3. 캘린더 JSON 200 (버전 정합)"

    # 4) 대시보드
    $null = Invoke-RestMethod -Uri "$base/api/dashboard/summary" -Headers $h
    Ok "4. 대시보드 200"

    # 5) 워크플로우 핵심(재고·매입·판매 각 1)
    foreach ($ep in @("/api/inventory/list", "/api/purchase/list", "/api/sales/list")) {
        $null = Invoke-RestMethod -Uri "$base$ep" -Headers $h
    }
    Ok "5. 재고·매입·판매 조회 200"
}

# =============================================================================
# [8] 판정 / 롤백
# =============================================================================
function Invoke-Rollback {
    param([string]$BackupDir)
    Step 8 "롤백(백업본 복원)"
    if (Test-Path (Join-Path $BackupDir "api")) {
        Copy-Item (Join-Path $BackupDir "api\*") $CFG.ApiDir -Recurse -Force
    }
    if (Test-Path (Join-Path $BackupDir "web")) {
        Copy-Item (Join-Path $BackupDir "web\*") $CFG.WebDir -Recurse -Force
    }
    & mysql --user=$($CFG.DbUser) $CFG.DbName -e "source $(Join-Path $BackupDir 'db.sql')"
    Fail "배포 실패 → 백업본으로 복원 완료. 운영 무손상."
}

# =============================================================================
# MAIN
# =============================================================================
if (-not $Execute) {
    Write-Host "DRAFT — dry-run. 실제 배포는 -Execute + SOP 결재 후에만." -ForegroundColor Yellow
    Write-Host "이 초안은 demo 운영에 실행 금지(헌법 #39)." -ForegroundColor Yellow
    return
}

# 타임스탬프는 호출 시 외부 주입(스크립트 내 Date.now류 의존 최소화)
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupDir = $null
try {
    Invoke-Preflight
    $backupDir = Invoke-Backup -Stamp $stamp
    Invoke-Build
    Invoke-Stop
    Invoke-Swap
    Invoke-KeyAlign
    Invoke-Start
    Invoke-Smoke          # 하나라도 실패 시 throw
    Write-Host "`n✅ 배포 성공 — 정합 게이트 5종 통과." -ForegroundColor Green
}
catch {
    Write-Host "`n❌ 배포 중단: $_" -ForegroundColor Red
    if ($backupDir) { Invoke-Rollback -BackupDir $backupDir }
    exit 1
}
