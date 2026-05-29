# =================================================================
# AntivirusExceptions.ps1 — 백신 5종 자동 예외 (헌법 #31)
# 자동: Windows Defender + V3 Lite + 알약 + Naver Vaccine
# 매뉴얼: Norton + McAfee (docs PDF 안내)
# =================================================================
param(
    [Parameter(Mandatory=$true)]
    [string]$InstallPath
)

$ErrorActionPreference = 'Continue'
$EventSource = 'HitPanSetup'

# Event Log 소스 등록 (없으면)
try {
    if (-not [System.Diagnostics.EventLog]::SourceExists($EventSource)) {
        New-EventLog -LogName Application -Source $EventSource -ErrorAction SilentlyContinue
    }
} catch { }

function Log-Info($msg) {
    Write-EventLog -LogName Application -Source $EventSource `
        -EntryType Information -EventId 31001 -Message $msg -ErrorAction SilentlyContinue
    Write-Output "[INFO] $msg"
}

function Log-Warn($msg) {
    Write-EventLog -LogName Application -Source $EventSource `
        -EntryType Warning -EventId 31002 -Message $msg -ErrorAction SilentlyContinue
    Write-Output "[WARN] $msg"
}

# --- 1. Windows Defender ---
try {
    Add-MpPreference -ExclusionPath $InstallPath -ErrorAction Stop
    Add-MpPreference -ExclusionProcess "$InstallPath\payload\cloudflared.exe" -ErrorAction Stop
    Add-MpPreference -ExclusionProcess "$InstallPath\payload\HitPan.Watchdog\HitPan.Watchdog.exe" -ErrorAction Stop
    Add-MpPreference -ExclusionProcess "C:\Program Files\MariaDB 11.4\bin\mysqld.exe" -ErrorAction SilentlyContinue
    Log-Info "Defender exceptions registered"
} catch {
    Log-Warn "Defender exception failure: $($_.Exception.Message)"
}

# --- 2. AhnLab V3 Lite ---
try {
    $v3Key = 'HKLM:\SOFTWARE\AhnLab\V3Lite\Exclusions'
    if (Test-Path 'HKLM:\SOFTWARE\AhnLab\V3Lite') {
        if (-not (Test-Path $v3Key)) { New-Item -Path $v3Key -Force | Out-Null }
        New-ItemProperty -Path $v3Key -Name 'HitPan' -Value $InstallPath -PropertyType String -Force | Out-Null
        Log-Info "V3 Lite exception registered"
    } else {
        Log-Info "V3 Lite not installed — skipped"
    }
} catch {
    Log-Warn "V3 Lite exception failure: $($_.Exception.Message)"
}

# --- 3. ALYac (알약) ---
try {
    $alyacKey = 'HKLM:\SOFTWARE\ESTsoft\ALYac\Exclusions'
    if (Test-Path 'HKLM:\SOFTWARE\ESTsoft\ALYac') {
        if (-not (Test-Path $alyacKey)) { New-Item -Path $alyacKey -Force | Out-Null }
        New-ItemProperty -Path $alyacKey -Name 'HitPan' -Value $InstallPath -PropertyType String -Force | Out-Null
        Log-Info "ALYac exception registered"
    } else {
        Log-Info "ALYac not installed — skipped"
    }
} catch {
    Log-Warn "ALYac exception failure: $($_.Exception.Message)"
}

# --- 4. Naver Vaccine ---
try {
    $naverKey = 'HKLM:\SOFTWARE\NAVER\Vaccine\Exclusions'
    if (Test-Path 'HKLM:\SOFTWARE\NAVER\Vaccine') {
        if (-not (Test-Path $naverKey)) { New-Item -Path $naverKey -Force | Out-Null }
        New-ItemProperty -Path $naverKey -Name 'HitPan' -Value $InstallPath -PropertyType String -Force | Out-Null
        Log-Info "Naver Vaccine exception registered"
    } else {
        Log-Info "Naver Vaccine not installed — skipped"
    }
} catch {
    Log-Warn "Naver Vaccine exception failure: $($_.Exception.Message)"
}

# --- 5. Norton / McAfee — 매뉴얼 안내만 ---
$nortonInstalled = Test-Path 'HKLM:\SOFTWARE\Symantec\Norton'
$mcafeeInstalled = Test-Path 'HKLM:\SOFTWARE\McAfee'
if ($nortonInstalled -or $mcafeeInstalled) {
    Log-Warn "Norton/McAfee detected — manual exception required (see docs\백신_매뉴얼)"
}

Log-Info "Antivirus exception registration completed"
exit 0
