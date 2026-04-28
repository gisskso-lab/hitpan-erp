# ============================================================
# 의존성 번들 다운로드 스크립트
# WS-20260428-02 — 캐시 디렉토리에 1회만 받음 (재실행 시 재사용)
#
# 사용법:
#   .\download-bundle.ps1 -BundleDir "installer-build/bundle"
#
# 다운로드 항목:
#   1. .NET 8 ASP.NET Core Hosting Bundle (~80MB)
#   2. MariaDB 11.4 MSI (~200MB)
#   3. Visual C++ Redistributable x64 (~25MB)
#   4. cloudflared.exe (~15MB)
#
# 총합 ~320MB (1회 다운로드 후 캐시)
# ============================================================

param(
    [string]$BundleDir = "installer-build/bundle",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $BundleDir)) {
    New-Item -ItemType Directory -Path $BundleDir -Force | Out-Null
}

Write-Host ""
Write-Host "  의존성 번들 다운로드 → $BundleDir" -ForegroundColor Yellow
Write-Host ""

# TLS 1.2 강제 (구형 .NET 호환)
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# 다운로드 함수
function Download-IfMissing {
    param(
        [string]$Url,
        [string]$Path,
        [string]$Label,
        [int]$ExpectedMinMB
    )

    if ((Test-Path $Path) -and (-not $Force)) {
        $sizeMB = [Math]::Round((Get-Item $Path).Length / 1MB, 1)
        if ($sizeMB -ge $ExpectedMinMB) {
            Write-Host "  ✅ $Label : 캐시 사용 (${sizeMB} MB)" -ForegroundColor Green
            return
        }
        Write-Host "  ⚠ $Label : 크기 이상 (${sizeMB} MB < ${ExpectedMinMB} MB), 재다운로드"
    }

    Write-Host "  → $Label 다운로드 중..." -NoNewline
    try {
        Invoke-WebRequest -Uri $Url -OutFile $Path -UseBasicParsing
        $sizeMB = [Math]::Round((Get-Item $Path).Length / 1MB, 1)
        Write-Host " OK (${sizeMB} MB)" -ForegroundColor Green
    } catch {
        Write-Host " 실패" -ForegroundColor Red
        Write-Error "다운로드 실패: $Url`n  $_"
        exit 1
    }
}

# 1. .NET 8 ASP.NET Core Hosting Bundle
# ※ 정확한 URL은 https://dotnet.microsoft.com/download/dotnet/8.0 에서 확인
#   여기는 안정적인 redirect URL 사용 (마커스 리 약관 재확인 필요)
Download-IfMissing `
    -Url "https://aka.ms/dotnet/8.0/dotnet-hosting-win.exe" `
    -Path "$BundleDir/dotnet-hosting.exe" `
    -Label ".NET 8 Hosting Bundle" `
    -ExpectedMinMB 60

# 2. Visual C++ Redistributable 2015~2022 x64
Download-IfMissing `
    -Url "https://aka.ms/vs/17/release/vc_redist.x64.exe" `
    -Path "$BundleDir/vc_redist.x64.exe" `
    -Label "VC++ Redist x64" `
    -ExpectedMinMB 20

# 3. MariaDB 11.4 MSI (장기 안정 미러)
Download-IfMissing `
    -Url "https://archive.mariadb.org/mariadb-11.4.4/winx64-packages/mariadb-11.4.4-winx64.msi" `
    -Path "$BundleDir/mariadb.msi" `
    -Label "MariaDB 11.4.4 MSI" `
    -ExpectedMinMB 150

# 4. cloudflared.exe (latest from GitHub)
Download-IfMissing `
    -Url "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe" `
    -Path "$BundleDir/cloudflared.exe" `
    -Label "Cloudflare Tunnel" `
    -ExpectedMinMB 10

Write-Host ""
Write-Host "  ✅ 의존성 번들 준비 완료" -ForegroundColor Green
Write-Host ""

# 캐시 폴더 안내
$totalMB = [Math]::Round((Get-ChildItem $BundleDir -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "  번들 위치: $BundleDir"
Write-Host "  총 크기:   ${totalMB} MB"
Write-Host ""
Write-Host "  ※ 재빌드 시 -SkipBundleDownload 옵션 사용 가능" -ForegroundColor Cyan
Write-Host ""
