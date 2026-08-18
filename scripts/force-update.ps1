<#
.SYNOPSIS
  히트판 수동 강제 업데이트 — 팝업이 못 뜨는 상황에서 새 버전을 받는 길.

.DESCRIPTION
  🔴 이 도구가 왜 필요한가 (2026-08-18 실제 사고)

  1.2.84 에서 로그인 화면이 무한 반복에 빠졌다. 봉합은 1.2.85 에 실려 게시됐는데,
  **그 봉합을 받으려면 팝업이 떠야 하고, 팝업은 화면이 정상이어야 뜬다.**
  ⇒ 화면이 죽으면 고치는 것을 보낼 길이 통째로 막힌다.

  자동 업데이트의 정본 흐름(Major 발견 → 로그인 시 Y/N 팝업)은 그대로 둔다(헌법 #43).
  이 도구는 **그 흐름이 불가능할 때의 우회로**다. 대체가 아니라 예비다.

  [무엇을 하나]
  팝업이 [예] 를 눌렀을 때 하는 일과 **똑같은 한 줄**을 표에 넣는다.
  워치독이 그 줄을 읽고 종전과 **완전히 같은 절차**로 진행한다 —
  백업 → 교체 → 재기동 → 검증 → 실패 시 되돌리기. 아무것도 건너뛰지 않는다.

  🔴 이 도구는 **업데이트를 대신 하지 않는다.** 동의만 대신 넣는다.

.PARAMETER Version
  받을 버전 (예: 1.2.85). 생략하면 게시원에서 최신을 읽어 온다.

.PARAMETER Reject
  거부를 넣는다 (되돌릴 때 쓴다).

.PARAMETER WhatIf
  실제로 넣지 않고 무엇을 할지만 보여 준다.

.EXAMPLE
  .\force-update.ps1
  게시된 최신 버전으로 업데이트를 승인한다.

.EXAMPLE
  .\force-update.ps1 -Version 1.2.85
  버전을 직접 지정한다.

.EXAMPLE
  .\force-update.ps1 -RestartWatchdog
  🔴 한 번 시도했다가 실패한 버전을 다시 받을 때. 워치독을 다시 시작해
  "새 버전 발견" 상태를 되살린다 — 이것 없이는 멱등 장치에 걸려 무시된다.

.NOTES
  관리자 권한으로 실행할 것. 실행 사실은 표에 남는다(user_id='manual-force').
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Version,
    [switch]$Reject,

    # 🔴 워치독을 다시 시작해 "새 버전 발견" 상태를 되살린다.
    #   한 번 시도했다가 실패한 버전은 이것 없이는 무시된다(멱등 장치, Worker.cs:602).
    [switch]$RestartWatchdog
)

$ErrorActionPreference = 'Stop'

function Write-Step($msg)  { Write-Host "  $msg" }
function Write-Ok($msg)    { Write-Host "  [됨] $msg"    -ForegroundColor Green }
function Write-Warn2($msg) { Write-Host "  [주의] $msg"  -ForegroundColor Yellow }
function Write-Bad($msg)   { Write-Host "  [안됨] $msg"  -ForegroundColor Red }

Write-Host ""
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " 히트판 수동 업데이트 — 팝업이 못 뜰 때 쓰는 길" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# ── ① db.conf 찾기 ───────────────────────────────────────────────
#   🔴 워치독(DbConfReader.cs)과 **같은 자리**를 본다. 다른 데를 보면
#     내가 쓴 값과 워치독이 읽는 값이 갈린다.
Write-Host "[1/5] 설정 파일을 찾습니다"

$candidates = @(
    "C:\HitPan\db.conf",
    "C:\Program Files\HitPan\db.conf",
    "C:\Program Files (x86)\HitPan\db.conf",
    (Join-Path $PSScriptRoot "..\db.conf"),
    (Join-Path $PSScriptRoot "db.conf")
)

$dbConf = $null
foreach ($c in $candidates) {
    if (Test-Path $c) { $dbConf = (Resolve-Path $c).Path; break }
}

if (-not $dbConf) {
    # 설치 위치가 다를 수 있다 — 서비스 등록 경로에서 역추적한다.
    try {
        $svc = Get-CimInstance Win32_Service -Filter "Name='HitPanWatchdog'" -ErrorAction Stop
        # 서비스 등록 경로: "...\HitPan\watchdog\HitPan.Watchdog.exe"
        #   ⇒ watchdog 폴더의 **부모**가 설치 폴더이고 거기에 db.conf 가 있다.
        $exePath = $svc.PathName.Trim('"')
        $wdDir = Split-Path $exePath -Parent
        if ($wdDir) {
            $guess = Join-Path (Split-Path $wdDir -Parent) "db.conf"
            if (Test-Path $guess) { $dbConf = $guess }
        }
    } catch {
        Write-Verbose "서비스에서 경로를 못 찾았다: $_"
    }
}

if (-not $dbConf) {
    Write-Bad "db.conf 를 못 찾았습니다."
    Write-Host ""
    Write-Host "  히트판이 설치된 폴더의 db.conf 경로를 알려 주세요:" -ForegroundColor Yellow
    Write-Host "    .\force-update.ps1 -Verbose   (자세히 보기)"
    exit 1
}
Write-Ok "설정: $dbConf"

# ── ② 자격증명 읽기 ──────────────────────────────────────────────
Write-Host ""
Write-Host "[2/5] 데이터베이스 정보를 읽습니다"

$conf = @{}
Get-Content $dbConf -Encoding UTF8 | ForEach-Object {
    if ($_ -match '^\s*([A-Za-z_]+)\s*=\s*(.*?)\s*$') { $conf[$Matches[1]] = $Matches[2] }
}

$dbHost = if ($conf.DB_HOST) { $conf.DB_HOST } else { 'localhost' }
$dbPort = if ($conf.DB_PORT) { $conf.DB_PORT } else { '3306' }
$dbName = $conf.DB_NAME
$dbUser = $conf.DB_USER
$dbPass = $conf.DB_PASSWORD

if (-not $dbName) {
    Write-Bad "db.conf 에 DB_NAME 이 없습니다 — 어느 데이터베이스인지 알 수 없습니다."
    exit 1
}

# DB_USER 가 없는 db.conf 를 만난 경우.
#   ⚠️ **정식 설치본에는 반드시 있다** — 설치 마법사가 쓴다(`HitPan-Universal.iss:2104`, 실측).
#     없다면 그 PC 는 설치 경로를 거치지 않은 것이다(개발 조각·수동 구성).
#   ⇒ 히트판이 쓰는 기본 계정으로 갈음해 일단 되게 한다.
#     표에 한 줄 넣는 것뿐이고, 그 줄을 읽는 것은 워치독이다.
if (-not $dbUser) {
    $dbUser = 'hitpan'
    if (-not $dbPass) { $dbPass = 'Hitpan2025!' }
    Write-Warn2 "db.conf 에 DB_USER 가 없어 기본 계정으로 붙습니다."
    Write-Warn2 "이 PC 는 설치 마법사를 거치지 않은 것으로 보입니다(정식 설치본에는 이 값이 있습니다)."
}
Write-Ok ("데이터베이스: {0} ({1}:{2})" -f $dbName, $dbHost, $dbPort)

# mysql.exe 찾기
$mysqlCandidates = @(
    "C:\Program Files\MariaDB 11.4\bin\mysql.exe",
    "C:\Program Files\MariaDB 11.3\bin\mysql.exe",
    "C:\Program Files\MariaDB 10.11\bin\mysql.exe"
)
$mysql = $mysqlCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $mysql) {
    $mysql = (Get-Command mysql.exe -ErrorAction SilentlyContinue).Source
}
if (-not $mysql) {
    Write-Bad "mysql.exe 를 못 찾았습니다. MariaDB 설치를 확인해 주세요."
    exit 1
}

function Invoke-Sql([string]$sql) {
    # ⚠️ $args 는 PowerShell 예약 변수다 — 다른 이름을 쓴다.
    $cliArgs = @("--host=$dbHost", "--port=$dbPort", "-u$dbUser")
    if ($dbPass) { $cliArgs += "-p$dbPass" }
    $cliArgs += @("--batch", "--skip-column-names", $dbName, "-e", $sql)
    $out = & $mysql @cliArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "SQL 실패: $out" }
    return $out
}

# ── ③ 버전 정하기 ────────────────────────────────────────────────
Write-Host ""
Write-Host "[3/5] 받을 버전을 정합니다"

# 🔴 지금 버전은 **DB 가 아니라 EXE 파일**에 있다(local_company 에 그런 칸이 없다 — 실측).
#   워치독이 보는 것과 같은 값을 보려고 실행 파일의 FileVersion 을 읽는다.
$current = $null
$appDir = Split-Path $dbConf -Parent
foreach ($exe in @("HitPan.API.exe", "watchdog\HitPan.Watchdog.exe")) {
    $p = Join-Path $appDir $exe
    if (Test-Path $p) {
        $fv = (Get-Item $p).VersionInfo.FileVersion
        if ($fv) { $current = ($fv -split '\.')[0..2] -join '.'; break }
    }
}

if (-not $Version) {
    Write-Step "게시원에서 최신 버전을 확인합니다..."
    try {
        $manifest = Invoke-RestMethod -Uri "https://updates.hitpan.kr/manifest.json" -TimeoutSec 20
        $Version = $manifest.version
        Write-Ok "게시된 최신: $Version"
    } catch {
        Write-Bad "게시원에 못 닿았습니다 — 버전을 직접 넣어 주세요: -Version 1.2.85"
        exit 1
    }
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Bad "버전 형식이 아닙니다: '$Version' (예: 1.2.85)"
    exit 1
}

if ($current) { Write-Step "지금 이 PC: $current" }
Write-Step "받을 버전 : $Version"

if ($current -and $current -eq $Version) {
    Write-Warn2 "이미 같은 버전입니다 — 워치독은 아무것도 하지 않습니다."
    Write-Host ""
    exit 0
}

# ── ④ 동의 넣기 ──────────────────────────────────────────────────
Write-Host ""
Write-Host "[4/5] 업데이트 동의를 넣습니다"

$action = if ($Reject) { 'reject' } else { 'approve' }

# 🔴 워치독은 update_version 만 보고 최신 1행을 읽는다(WatchdogConsentReader).
#   tenant_id 는 표의 NOT NULL 을 채우려고 local_company 에서 가져온다.
$sql = @"
INSERT INTO local_update_consents (tenant_id, user_id, update_version, action, consented_at)
SELECT tenant_id, 'manual-force', '$Version', '$action', NOW(3)
FROM local_company LIMIT 1;
"@

if ($PSCmdlet.ShouldProcess("$dbName.local_update_consents", "$action $Version")) {
    Invoke-Sql $sql | Out-Null
    Write-Ok "$Version 을(를) '$action' 으로 넣었습니다."
} else {
    Write-Warn2 "실제로는 넣지 않았습니다(WhatIf)."
    Write-Host ""
    Write-Host $sql
    exit 0
}

# ── ⑤ 안내 ───────────────────────────────────────────────────────
Write-Host ""
Write-Host "[5/5] 이제 워치독이 받습니다"

if ($Reject) {
    Write-Ok "거부를 넣었습니다 — 이 버전은 적용되지 않습니다."
    Write-Host ""
    exit 0
}

# ══════════════════════════════════════════════════════════════
# 🔴 동의를 넣는 것만으로는 부족한 경우가 있다 (설계팀 실측, 2026-08-18)
#
#   워치독은 **두 겹의 문**을 지나야 동의를 읽는다(`Worker.cs`):
#     ① `_pendingConsentUpdate` 가 있어야 읽는다(:388)      — 새 버전을 "발견"한 상태
#     ② 이미 시도한 버전이면 읽지도 않고 끝낸다(:602)        — 멱등 장치
#
#   🔴 둘 다 **인메모리**다(:102, :563). 서비스를 다시 시작하면 초기화되고,
#     워치독이 게시원을 다시 보고 새 버전을 **다시 발견**하면서 ①이 선다.
#
#   ⇒ 오늘처럼 [예] 를 눌렀는데 적용이 완주하지 못한 버전은
#     **동의만 넣으면 ②에 걸려 조용히 무시된다.** 서비스를 다시 시작해야 한다.
# ══════════════════════════════════════════════════════════════
Write-Host ""
if ($RestartWatchdog) {
    Write-Step "워치독을 다시 시작합니다..."
    try {
        Restart-Service HitPanWatchdog -Force -ErrorAction Stop
        Write-Ok "다시 시작했습니다 — 새 버전을 다시 발견하고 방금 넣은 동의를 읽습니다."
    } catch {
        Write-Warn2 "다시 시작하지 못했습니다: $($_.Exception.Message)"
        Write-Warn2 "관리자 권한으로 직접 실행해 주세요: Restart-Service HitPanWatchdog"
    }
} else {
    Write-Host "  ▸ 이 동의는 워치독이 '새 버전을 발견한 상태' 일 때 읽힙니다." -ForegroundColor Cyan
    Write-Host "    한 번 시도했다가 실패한 버전이면 그대로는 무시됩니다(중복 적용 방지 장치)."
    Write-Host ""
    Write-Host "  ▸ 확실하게 하려면 이렇게 다시 돌리십시오:" -ForegroundColor Yellow
    Write-Host "      .\force-update.ps1 -RestartWatchdog"
    Write-Host "    또는 직접:  Restart-Service HitPanWatchdog"
}
Write-Host ""
Write-Host "  그 뒤는 종전 자동 업데이트와 **같은 절차**입니다 —" -ForegroundColor Cyan
Write-Host "  자료를 먼저 백업하고, 교체하고, 히트판이 잠시 꺼졌다 켜지고,"
Write-Host "  실패하면 스스로 되돌립니다. 건너뛰는 것은 없습니다."
Write-Host ""
# 🔴 로그 자리를 **찾아서** 알려 준다 — 추측한 경로를 적으면 고객이 헛다리를 짚는다.
$logHint = $null
foreach ($d in @((Join-Path $appDir 'watchdog\logs'), (Join-Path $appDir 'logs'))) {
    if (Test-Path $d) {
        $newest = Get-ChildItem $d -Filter *.log -ErrorAction SilentlyContinue |
                  Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($newest) { $logHint = $newest.FullName; break }
    }
}

Write-Host "  진행 상황 보기:" -ForegroundColor Cyan
if ($logHint) {
    Write-Host "    Get-Content '$logHint' -Tail 30 -Wait"
} else {
    Write-Host "    (로그 파일을 못 찾았습니다 — 이벤트 뷰어에서 HitPanWatchdog 서비스를 보십시오)"
}
Write-Host ""
Write-Host "  적용 확인 — 이 명령을 다시 돌리면 '지금 이 PC' 줄에 새 버전이 보입니다:" -ForegroundColor Cyan
Write-Host "    .\force-update.ps1 -WhatIf"
Write-Host ""
