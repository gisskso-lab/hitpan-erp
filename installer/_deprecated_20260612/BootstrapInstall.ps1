# =================================================================
# BootstrapInstall.ps1 — 시리얼 입력 → 백오피스 부트스트랩 → 통합 설치
# 사장님 결재 Plan 2026-06-09 (cicd-velvety-reef)
# 멀티사업자 지원 추가 (사장님 결재 2026-06-09)
#
# 흐름:
#   1) 마법사가 시리얼 입력 받아 본 스크립트 호출
#   2) POST https://back.hitpan.kr/api/installer/bootstrap
#   3) 응답: 회사정보 + 도메인 + tunnelToken
#   4) 기존 설치 여부 확인 → 첫 설치 / 추가 설치 분기
#   5) MariaDB·.NET·cloudflared 사일런트 설치 + 자동 시작
#   6) ERP 자동 시작 후 브라우저 열림
#
# 멀티사업자 설계:
#   - 첫 설치:   C:\HitPan\tenant-1\ (포트 5257)
#   - 두 번째:   C:\HitPan\tenant-2\ (포트 5357)
#   - 세 번째:   C:\HitPan\tenant-3\ (포트 5457)
#   - MariaDB는 공유 (포트 3306), 데이터베이스만 hitpan_erp_{tenantCode} 분리
#   - cloudflared 터널은 회사별 따로 등록
#   - 워치독은 하나 (모든 회사 ERP 자가 진단)
#
# 헌법 정합:
#   #18·#22 — 본사 인프라 토큰 EXE에 포함하지 않음, 시리얼만 입력
#   #28·#30 — 고객 손 0번 자동 봉합
#   #31 — 백신 5종 호환 (예외 등록 + 방화벽 규칙)
#   #34 — 베타부터 정식 완성도
#   #35 — 시리얼 = 백오피스↔ERP 외래키
# =================================================================
param(
    [Parameter(Mandatory=$true)]
    [string]$LicenseKey,

    [Parameter(Mandatory=$false)]
    [string]$BackofficeApi = 'https://back.hitpan.kr',

    [Parameter(Mandatory=$false)]
    [string]$RootDir = 'C:\HitPan',

    [Parameter(Mandatory=$false)]
    [string]$InstallerVersion = '1.1.0'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Log-Info($m) { Write-Host "[INFO] $m" -ForegroundColor Cyan }
function Log-OK($m)   { Write-Host "[ OK ] $m" -ForegroundColor Green }
function Log-Warn($m) { Write-Host "[WARN] $m" -ForegroundColor Yellow }
function Log-Err($m)  { Write-Host "[ERR ] $m" -ForegroundColor Red }

# 0. 관리자 권한 확인
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $isAdmin) {
    Log-Err "관리자 권한이 필요합니다. 설치마법사를 관리자 권한으로 다시 실행해주세요."
    exit 1
}

# 1. 머신 핑거프린트 생성 (CPU + 디스크 + MAC)
function Get-MachineFingerprint {
    $cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).ProcessorId
    $disk = (Get-CimInstance Win32_DiskDrive | Where-Object { $_.MediaType -like '*Fixed*' } | Select-Object -First 1).SerialNumber
    $mac = (Get-CimInstance Win32_NetworkAdapter | Where-Object { $_.MACAddress -and $_.PhysicalAdapter } | Select-Object -First 1).MACAddress
    return ("$cpu|$disk|$mac").Trim()
}

# 2. 부트스트랩 API 호출
Log-Info "백오피스 부트스트랩 호출 중... ($BackofficeApi)"
$fingerprint = Get-MachineFingerprint
$hostname = $env:COMPUTERNAME
$osVersion = (Get-CimInstance Win32_OperatingSystem).Caption

$body = @{
    licenseKey = $LicenseKey
    machineFingerprint = $fingerprint
    hostname = $hostname
    osVersion = $osVersion
    installerVersion = $InstallerVersion
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri "$BackofficeApi/api/installer/bootstrap" `
        -Method Post -Body $body -ContentType 'application/json; charset=utf-8' -TimeoutSec 30
} catch {
    Log-Err "부트스트랩 실패: $($_.Exception.Message)"
    Log-Err "시리얼 키가 올바른지, 인터넷 연결이 정상인지 확인해주세요."
    exit 2
}

if (-not $response.success) {
    Log-Err "부트스트랩 거부: $($response.message)"
    exit 3
}

$tenantCode = $response.tenant.tenantCode
$companyName = $response.tenant.companyName
$primaryDomain = $response.domain.primary
$apiDomain = $response.domain.api
$tunnelToken = $response.domain.tunnelToken
$bootstrapToken = $response.bootstrap.token

Log-OK "회사: $companyName / 도메인: $primaryDomain"

# 3. 기존 설치 확인 (멀티사업자 지원)
Log-Info "기존 설치 여부 확인 중..."
$registryPath = "$RootDir\registry.json"
$registry = @{
    tenants = @()
    nextSlotIndex = 1
}

if (Test-Path $registryPath) {
    try {
        $registry = Get-Content $registryPath -Raw | ConvertFrom-Json -AsHashtable
        if (-not $registry.tenants) { $registry.tenants = @() }
        if (-not $registry.nextSlotIndex) { $registry.nextSlotIndex = ($registry.tenants.Count + 1) }
    } catch {
        Log-Warn "기존 레지스트리 파일 읽기 실패, 새로 생성합니다."
        $registry = @{ tenants = @(); nextSlotIndex = 1 }
    }
}

# 같은 시리얼로 이미 설치되어 있는지 확인 (중복 방지)
$existing = $registry.tenants | Where-Object { $_.tenantCode -eq $tenantCode }
if ($existing) {
    Log-Warn "이미 이 시리얼 키로 설치된 회사가 있습니다: $($existing.companyName)"
    Log-Warn "기존 설치 폴더: $($existing.installDir)"
    Log-Err "동일 시리얼 재설치는 지원하지 않습니다. 다른 시리얼로 시도하거나 본사에 문의해주세요."
    exit 4
}

# 새 슬롯 번호 결정
$slotIndex = [int]$registry.nextSlotIndex
$tenantInstallDir = "$RootDir\tenant-$slotIndex"
$apiPort = 5257 + (($slotIndex - 1) * 100)   # 5257, 5357, 5457, ...
$dbName = "hitpan_erp_" + ($tenantCode -replace '[^a-zA-Z0-9]', '_').ToLower()

if ($registry.tenants.Count -eq 0) {
    Log-OK "첫 회사 설치 — 슬롯 1번 (포트 $apiPort)"
} else {
    Log-OK "추가 회사 설치 — 슬롯 $slotIndex 번 (포트 $apiPort, 기존 회사 $($registry.tenants.Count)개 유지)"
}

# 4. 회사별 설치 디렉토리 준비
Log-Info "회사별 설치 디렉토리 준비: $tenantInstallDir"
New-Item -ItemType Directory -Force -Path "$tenantInstallDir\api" | Out-Null
New-Item -ItemType Directory -Force -Path "$tenantInstallDir\web" | Out-Null
New-Item -ItemType Directory -Force -Path "$tenantInstallDir\logs" | Out-Null
New-Item -ItemType Directory -Force -Path "$tenantInstallDir\config" | Out-Null

# 5. 회사별 부트스트랩 정보 저장
$config = @{
    tenantCode = $tenantCode
    companyName = $companyName
    primaryDomain = $primaryDomain
    apiDomain = $apiDomain
    backofficeApi = $BackofficeApi
    bootstrapToken = $bootstrapToken
    apiPort = $apiPort
    dbName = $dbName
    slotIndex = $slotIndex
    installedAt = (Get-Date).ToString('o')
    installerVersion = $InstallerVersion
} | ConvertTo-Json -Depth 4
Set-Content -Path "$tenantInstallDir\config\bootstrap.json" -Value $config -Encoding UTF8
Log-OK "회사별 부트스트랩 정보 저장 완료"

# 6. 전체 레지스트리 업데이트 (한 PC에 설치된 모든 회사 목록)
$registry.tenants += @{
    slotIndex = $slotIndex
    tenantCode = $tenantCode
    companyName = $companyName
    primaryDomain = $primaryDomain
    apiPort = $apiPort
    dbName = $dbName
    installDir = $tenantInstallDir
    installedAt = (Get-Date).ToString('o')
}
$registry.nextSlotIndex = $slotIndex + 1
New-Item -ItemType Directory -Force -Path $RootDir | Out-Null
$registry | ConvertTo-Json -Depth 5 | Set-Content -Path $registryPath -Encoding UTF8
Log-OK "전체 회사 목록 업데이트 완료 (현재 $($registry.tenants.Count)개 회사)"

# 7. 백신 예외 + 방화벽 규칙 (헌법 #31) — 첫 설치 시만
if ($registry.tenants.Count -eq 1) {
    Log-Info "백신 예외 + 방화벽 규칙 등록 중 (첫 설치)..."
    try {
        & "$PSScriptRoot\AntivirusExceptions.ps1" -InstallDir $RootDir
        & "$PSScriptRoot\FirewallRules.ps1"
        Log-OK "백신 예외 + 방화벽 등록 완료"
    } catch {
        Log-Warn "백신/방화벽 등록 일부 실패 (수동 등록 필요): $($_.Exception.Message)"
    }
} else {
    Log-Info "백신 예외 + 방화벽 규칙은 이미 등록되어 있습니다 (첫 설치 시 등록 완료)"
}

# 추가 슬롯 포트도 방화벽에 등록 (5357, 5457, ...)
if ($registry.tenants.Count -gt 1) {
    try {
        New-NetFirewallRule -DisplayName "HitPan-API-Port-$apiPort" `
            -Direction Inbound -Protocol TCP -LocalPort $apiPort -Action Allow `
            -ErrorAction SilentlyContinue | Out-Null
        Log-OK "방화벽 포트 $apiPort 추가 등록 완료"
    } catch {
        Log-Warn "방화벽 포트 $apiPort 등록 실패: $($_.Exception.Message)"
    }
}

# 8. .NET 8 Runtime 설치 (첫 설치 시만, 이후엔 공유)
Log-Info ".NET 8 Runtime 확인 중..."
$dotnetVersion = $null
try {
    $dotnetVersion = & dotnet --list-runtimes 2>$null | Select-String 'Microsoft.AspNetCore.App 8\.' | Select-Object -First 1
} catch {}

if (-not $dotnetVersion) {
    Log-Info ".NET 8 Runtime 설치 중 (사일런트)..."
    $dotnetUrl = 'https://download.visualstudio.microsoft.com/download/pr/dotnet-runtime-8.0.10-win-x64.exe'
    $dotnetExe = "$env:TEMP\dotnet-runtime-8.exe"
    Invoke-WebRequest -Uri $dotnetUrl -OutFile $dotnetExe -UseBasicParsing
    Start-Process -FilePath $dotnetExe -ArgumentList '/quiet','/install' -Wait
    Log-OK ".NET 8 Runtime 설치 완료"
} else {
    Log-OK ".NET 8 Runtime 이미 설치됨"
}

# 9. MariaDB 11.4 사일런트 설치 (첫 설치 시만, 이후엔 공유)
Log-Info "MariaDB 11.4 확인 중..."
$mariaService = Get-Service -Name 'MariaDB' -ErrorAction SilentlyContinue
$mariaRootPwdPath = "$RootDir\config\.maria-root"
$rootPwd = $null

if (-not $mariaService) {
    Log-Info "MariaDB 11.4 다운로드 + 설치 중 (사일런트)..."
    $mariaUrl = 'https://archive.mariadb.org/mariadb-11.4.10/winx64-packages/mariadb-11.4.10-winx64.msi'
    $mariaMsi = "$env:TEMP\mariadb-11.4.10.msi"
    Invoke-WebRequest -Uri $mariaUrl -OutFile $mariaMsi -UseBasicParsing

    # 사일런트 옵션: root 비번 자동 생성 (헌법 #22 — 본사가 모름)
    $rootPwd = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 24 | ForEach-Object { [char]$_ })
    $rootPwd += '!A1'
    New-Item -ItemType Directory -Force -Path "$RootDir\config" | Out-Null
    Set-Content -Path $mariaRootPwdPath -Value $rootPwd -Encoding UTF8

    $mariaArgs = @(
        '/i', $mariaMsi,
        '/quiet',
        "PASSWORD=$rootPwd",
        'PORT=3306',
        'SERVICENAME=MariaDB',
        "DATADIR=$RootDir\db",
        'INSTALLDIR=C:\Program Files\MariaDB 11.4'
    )
    Start-Process -FilePath 'msiexec.exe' -ArgumentList $mariaArgs -Wait
    Log-OK "MariaDB 11.4 설치 완료"
} else {
    Log-OK "MariaDB 이미 설치됨 (서비스 상태: $($mariaService.Status))"
    # 기존 root 비번 읽기 (이미 첫 설치 시 저장됨)
    if (Test-Path $mariaRootPwdPath) {
        $rootPwd = Get-Content $mariaRootPwdPath -Raw
    } else {
        Log-Warn "MariaDB는 설치되어 있으나 root 비번 파일이 없습니다. 데이터베이스 생성을 수동으로 진행해야 합니다."
    }
}

# 10. 회사별 데이터베이스 생성 (멀티사업자 핵심)
if ($rootPwd) {
    Log-Info "회사별 데이터베이스 생성 중: $dbName"
    try {
        $mysqlExe = 'C:\Program Files\MariaDB 11.4\bin\mysql.exe'
        if (Test-Path $mysqlExe) {
            $backtick = [char]96
            $createDbCmd = "CREATE DATABASE IF NOT EXISTS {0}{1}{0} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;" -f $backtick, $dbName
            $pwdArg = "-p" + $rootPwd
            & $mysqlExe -u root $pwdArg -e $createDbCmd 2>&1 | Out-Null
            Log-OK "데이터베이스 생성 완료: $dbName"
        } else {
            Log-Warn "mysql.exe를 찾을 수 없습니다. 데이터베이스 수동 생성 필요."
        }
    } catch {
        Log-Warn "데이터베이스 생성 실패: $($_.Exception.Message)"
    }
}

# 11. cloudflared 터널 등록 (회사별 — 응답에 토큰이 있으면 자동 등록)
if ($tunnelToken) {
    Log-Info "cloudflared 자동 등록 중 (회사별 터널)..."
    try {
        # cloudflared 다운로드 (첫 설치 시만)
        $cfPath = 'C:\Program Files\cloudflared\cloudflared.exe'
        if (-not (Test-Path $cfPath)) {
            $cfUrl = 'https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe'
            New-Item -ItemType Directory -Force -Path 'C:\Program Files\cloudflared' | Out-Null
            Invoke-WebRequest -Uri $cfUrl -OutFile $cfPath -UseBasicParsing
        }

        # 회사별 서비스 이름으로 등록 (cloudflared-tenant-1, cloudflared-tenant-2, ...)
        $cfServiceName = "cloudflared-tenant-$slotIndex"
        if ($slotIndex -eq 1) {
            # 첫 설치: 기본 cloudflared 서비스로 등록
            & $cfPath service install $tunnelToken 2>&1 | Out-Null
            Start-Service -Name 'cloudflared' -ErrorAction SilentlyContinue
            Log-OK "cloudflared 기본 서비스 등록 + 시작 완료"
        } else {
            # 추가 설치: 별도 서비스 이름으로 cloudflared 실행 (NSSM 또는 sc.exe 사용)
            # 간단히 작업 스케줄러로 자동 시작 등록
            $cfArgs = "tunnel run --token $tunnelToken"
            schtasks /Create /F /TN $cfServiceName `
                /TR "`"$cfPath`" $cfArgs" `
                /SC ONSTART /RU SYSTEM /RL HIGHEST | Out-Null
            Start-Process -FilePath $cfPath -ArgumentList "tunnel","run","--token",$tunnelToken -WindowStyle Hidden
            Log-OK "cloudflared 추가 터널 등록 + 시작 완료 ($cfServiceName)"
        }
    } catch {
        Log-Warn "cloudflared 자동 등록 일부 실패: $($_.Exception.Message)"
        Log-Warn "본사 고객센터로 문의해주세요. (수동 등록 가능)"
    }
} else {
    Log-Warn "터널 토큰이 응답에 포함되어 있지 않습니다 — 본사 측에서 수동 발급 후 재시도 필요"
}

# 12. 워치독 서비스 등록 (첫 설치 시만, 워치독 1개가 모든 회사 ERP 자가 진단)
if ($registry.tenants.Count -eq 1) {
    Log-Info "워치독 Windows 서비스 등록 중 (첫 설치)..."
    try {
        & "$PSScriptRoot\InstallWatchdog.ps1" -InstallDir $RootDir
        Log-OK "워치독 등록 완료"
    } catch {
        Log-Warn "워치독 등록 실패: $($_.Exception.Message)"
    }
} else {
    Log-Info "워치독은 이미 등록되어 있습니다 (모든 회사 ERP 통합 자가 진단)"
}

# 13. 환경변수 설정 (전체 공통 + 회사별 분리)
# 공통 환경변수 (첫 설치 시만)
if ($registry.tenants.Count -eq 1) {
    [Environment]::SetEnvironmentVariable('HITPAN_ROOT_DIR', $RootDir, 'Machine')
    [Environment]::SetEnvironmentVariable('HITPAN_BACKOFFICE_API', $BackofficeApi, 'Machine')
    [Environment]::SetEnvironmentVariable('HITPAN_UPDATE_FEED', 'https://updates.hitpan.kr', 'Machine')
    [Environment]::SetEnvironmentVariable('HITPAN_AUTO_UPDATE_ENABLED', 'true', 'Machine')
    Log-OK "공통 환경변수 설정 완료"
}

# 회사별 환경변수는 bootstrap.json에 저장됨 (환경변수에 다 넣으면 충돌)
Log-OK "회사별 설정은 $tenantInstallDir\config\bootstrap.json 에 저장됨"

# 14. ERP API 자동 시작 등록 (회사별 작업 스케줄러)
Log-Info "ERP 자동 시작 등록 중 (회사별)..."
try {
    $taskName = "HitPan-API-tenant-$slotIndex"
    $exePath = "$tenantInstallDir\api\HitPan.API.exe"
    # 환경변수로 포트와 DB 정보 전달
    $envArgs = "--urls http://127.0.0.1:$apiPort"
    schtasks /Create /F /TN $taskName `
        /TR "`"$exePath`" $envArgs" `
        /SC ONSTART /RU SYSTEM /RL HIGHEST | Out-Null

    # 즉시 시작
    Start-Process -FilePath $exePath -ArgumentList "--urls","http://127.0.0.1:$apiPort" -WindowStyle Hidden
    Log-OK "ERP API 자동 시작 등록 + 시작 완료 (포트 $apiPort)"
} catch {
    Log-Warn "ERP 자동 시작 등록 실패: $($_.Exception.Message)"
}

# 15. 설치 완료 + 브라우저 자동 열기
Start-Sleep -Seconds 3
Log-OK "========================================"
Log-OK "설치 완료!"
Log-OK "회사: $companyName"
Log-OK "주소: https://$primaryDomain"
Log-OK "슬롯: $slotIndex 번 / 포트: $apiPort"
Log-OK "현재 이 PC에 설치된 회사: $($registry.tenants.Count)개"
Log-OK "========================================"
Log-Info "브라우저로 이동합니다..."
Start-Process "https://$primaryDomain"

exit 0
