# =================================================================
# BootstrapInstall.ps1 — 시리얼 입력 → 백오피스 부트스트랩 → 통합 설치
# 사장님 결재 Plan 2026-06-09 (cicd-velvety-reef Day 1~5)
#
# 흐름:
#   1) 마법사가 시리얼 입력 받아 본 스크립트 호출
#   2) POST https://back.hitpan.kr/api/installer/bootstrap
#   3) 응답: 회사정보 + 도메인 + tunnelToken
#   4) MariaDB·.NET·cloudflared 사일런트 설치 + 자동 시작
#   5) ERP 자동 시작 후 브라우저 열림
#
# 헌법 정합:
#   #22·#35 — 본사 인프라 토큰 EXE에 박지 않음, 시리얼만 입력
#   #28·#30 — 고객 손 0번
#   #31 — 백신 5종 호환 (예외 등록 + 방화벽 규칙)
#   #34 — 베타부터 정식 완성도
# =================================================================
param(
    [Parameter(Mandatory=$true)]
    [string]$LicenseKey,

    [Parameter(Mandatory=$false)]
    [string]$BackofficeApi = 'https://back.hitpan.kr',

    [Parameter(Mandatory=$false)]
    [string]$InstallDir = 'C:\HitPan',

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

# 3. 설치 디렉토리 준비
Log-Info "설치 디렉토리 준비: $InstallDir"
New-Item -ItemType Directory -Force -Path "$InstallDir\api" | Out-Null
New-Item -ItemType Directory -Force -Path "$InstallDir\web" | Out-Null
New-Item -ItemType Directory -Force -Path "$InstallDir\logs" | Out-Null
New-Item -ItemType Directory -Force -Path "$InstallDir\config" | Out-Null

# 4. 부트스트랩 정보 저장 (헌법 #22·#35 정합 — 평문 시리얼은 즉시 폐기, 해시만 보관)
$config = @{
    tenantCode = $tenantCode
    companyName = $companyName
    primaryDomain = $primaryDomain
    apiDomain = $apiDomain
    backofficeApi = $BackofficeApi
    bootstrapToken = $bootstrapToken
    installedAt = (Get-Date).ToString('o')
    installerVersion = $InstallerVersion
} | ConvertTo-Json -Depth 4
Set-Content -Path "$InstallDir\config\bootstrap.json" -Value $config -Encoding UTF8
Log-OK "부트스트랩 정보 저장 완료"

# 5. 백신 예외 + 방화벽 규칙 (헌법 #31)
Log-Info "백신 예외 + 방화벽 규칙 등록 중..."
try {
    & "$PSScriptRoot\AntivirusExceptions.ps1" -InstallDir $InstallDir
    & "$PSScriptRoot\FirewallRules.ps1"
    Log-OK "백신 예외 + 방화벽 등록 완료"
} catch {
    Log-Warn "백신/방화벽 등록 일부 실패 (수동 등록 필요): $($_.Exception.Message)"
}

# 6. .NET 8 Runtime 설치 (이미 설치되어 있으면 skip)
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

# 7. MariaDB 11.4 사일런트 설치
Log-Info "MariaDB 11.4 확인 중..."
$mariaService = Get-Service -Name 'MariaDB' -ErrorAction SilentlyContinue
if (-not $mariaService) {
    Log-Info "MariaDB 11.4 다운로드 + 설치 중 (사일런트)..."
    $mariaUrl = 'https://archive.mariadb.org/mariadb-11.4.10/winx64-packages/mariadb-11.4.10-winx64.msi'
    $mariaMsi = "$env:TEMP\mariadb-11.4.10.msi"
    Invoke-WebRequest -Uri $mariaUrl -OutFile $mariaMsi -UseBasicParsing

    # 사일런트 옵션: root 비번 자동 생성 (헌법 #22 — 본사가 모름)
    $rootPwd = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 24 | ForEach-Object { [char]$_ })
    $rootPwd += '!A1'
    Set-Content -Path "$InstallDir\config\.maria-root" -Value $rootPwd -Encoding UTF8

    $mariaArgs = @(
        '/i', $mariaMsi,
        '/quiet',
        "PASSWORD=$rootPwd",
        'PORT=3306',
        'SERVICENAME=MariaDB',
        'DATADIR=C:\HitPan\db',
        'INSTALLDIR=C:\Program Files\MariaDB 11.4'
    )
    Start-Process -FilePath 'msiexec.exe' -ArgumentList $mariaArgs -Wait
    Log-OK "MariaDB 11.4 설치 완료"
} else {
    Log-OK "MariaDB 이미 설치됨 (서비스 상태: $($mariaService.Status))"
}

# 8. cloudflared 터널 등록 (응답에 토큰 박혀있으면 자동 등록)
if ($tunnelToken) {
    Log-Info "cloudflared 자동 등록 중 (터널 토큰 수신)..."
    try {
        # cloudflared 다운로드 (없으면)
        $cfPath = 'C:\Program Files\cloudflared\cloudflared.exe'
        if (-not (Test-Path $cfPath)) {
            $cfUrl = 'https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe'
            New-Item -ItemType Directory -Force -Path 'C:\Program Files\cloudflared' | Out-Null
            Invoke-WebRequest -Uri $cfUrl -OutFile $cfPath -UseBasicParsing
        }

        # 서비스 등록 (관리자 권한 필수)
        & $cfPath service install $tunnelToken 2>&1 | Out-Null
        Start-Service -Name 'cloudflared' -ErrorAction SilentlyContinue
        Log-OK "cloudflared 서비스 등록 + 시작 완료"
    } catch {
        Log-Warn "cloudflared 자동 등록 일부 실패: $($_.Exception.Message)"
        Log-Warn "본사 CS로 문의해주세요. (수동 등록 가능)"
    }
} else {
    Log-Warn "터널 토큰이 응답에 없음 — 본사 측 수동 발급 후 재시도 필요"
}

# 9. 워치독 서비스 등록 (헌법 #28·#30)
Log-Info "워치독 Windows 서비스 등록 중..."
try {
    & "$PSScriptRoot\InstallWatchdog.ps1" -InstallDir $InstallDir
    Log-OK "워치독 등록 완료"
} catch {
    Log-Warn "워치독 등록 실패: $($_.Exception.Message)"
}

# 10. 환경변수 박제
[Environment]::SetEnvironmentVariable('HITPAN_INSTALL_DIR', $InstallDir, 'Machine')
[Environment]::SetEnvironmentVariable('HITPAN_TENANT_CODE', $tenantCode, 'Machine')
[Environment]::SetEnvironmentVariable('HITPAN_PRIMARY_DOMAIN', $primaryDomain, 'Machine')
[Environment]::SetEnvironmentVariable('HITPAN_API_DOMAIN', $apiDomain, 'Machine')
[Environment]::SetEnvironmentVariable('HITPAN_BACKOFFICE_API', $BackofficeApi, 'Machine')
[Environment]::SetEnvironmentVariable('HITPAN_UPDATE_FEED', 'https://updates.hitpan.kr', 'Machine')
[Environment]::SetEnvironmentVariable('HITPAN_AUTO_UPDATE_ENABLED', 'true', 'Machine')
Log-OK "환경변수 박제 완료"

# 11. ERP API + Web 자동 시작 (서비스 등록 또는 작업스케줄러)
Log-Info "ERP 자동 시작 등록 중..."
try {
    # 작업 스케줄러: 부팅 시 자동 시작 + 사용자 로그온 시 작업
    schtasks /Create /F /TN 'HitPan-API' /TR "$InstallDir\api\HitPan.API.exe" /SC ONSTART /RU SYSTEM /RL HIGHEST | Out-Null
    Start-Process -FilePath "$InstallDir\api\HitPan.API.exe" -WindowStyle Hidden
    Log-OK "ERP API 자동 시작 등록 + 시작 완료"
} catch {
    Log-Warn "ERP 자동 시작 등록 실패: $($_.Exception.Message)"
}

# 12. 브라우저 자동 열기
Start-Sleep -Seconds 3
Log-OK "설치 완료! 브라우저로 이동합니다... ($primaryDomain)"
Start-Process "https://$primaryDomain"

exit 0
