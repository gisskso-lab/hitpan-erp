# =================================================================
# InstallWatchdog.ps1 — HitPan.Watchdog Windows Service 등록 + Guardian
# =================================================================
param(
    [Parameter(Mandatory=$true)]
    [string]$InstallPath
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'HitPanWatchdog'
$ExePath     = Join-Path $InstallPath 'payload\HitPan.Watchdog\HitPan.Watchdog.exe'

if (-not (Test-Path $ExePath)) {
    Write-Output "[ERR] $ExePath not found"
    exit 1
}

# 기존 서비스 정리
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $existing) {
    Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# 서비스 등록
& sc.exe create $ServiceName binPath= "`"$ExePath`"" start= auto DisplayName= "HitPan Watchdog" | Out-Null
& sc.exe description $ServiceName "HitPan ERP 워치독 - 통신 무결성 자가 복구 (헌법 #28)" | Out-Null

# 실패 시 자동 재시작 (5초 / 5초 / 60초)
& sc.exe failure $ServiceName reset= 60 actions= restart/5000/restart/5000/restart/60000 | Out-Null

# 시작
Start-Service $ServiceName
Write-Output "[OK] $ServiceName installed and started"

# Guardian 작업 스케줄러 등록 (2층 워치독, 5분 주기)
$GuardianTask = 'HitPanWatchdogGuardian'
$GuardianPs1  = Join-Path $InstallPath 'scripts\Guardian.ps1'

# Guardian.ps1 작성
@'
$svc = Get-Service -Name HitPanWatchdog -ErrorAction SilentlyContinue
if ($null -eq $svc -or $svc.Status -ne 'Running') {
    try {
        Start-Service HitPanWatchdog -ErrorAction Stop
        Write-EventLog -LogName Application -Source HitPanSetup `
            -EntryType Warning -EventId 28008 `
            -Message "Guardian restarted HitPanWatchdog" -ErrorAction SilentlyContinue
    } catch { }
}
'@ | Out-File $GuardianPs1 -Encoding utf8

# 작업 스케줄러 등록 (5분 주기, 무한)
$action  = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$GuardianPs1`""
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) `
    -RepetitionInterval (New-TimeSpan -Minutes 5)
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest

Register-ScheduledTask -TaskName $GuardianTask -Action $action -Trigger $trigger `
    -Principal $principal -Force | Out-Null

Write-Output "[OK] $GuardianTask scheduled (5min interval)"
exit 0
