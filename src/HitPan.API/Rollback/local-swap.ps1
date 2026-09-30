# local-swap.ps1 - HitPan local program swap worker (-Mode update | rollback)
#
# ASCII ONLY (PowerShell 5.1 breaks non-ASCII scripts). Do not add non-ASCII characters.
#
# Contract : docs/.../erp/20260930_*_local-swap_request.md  (request.json schema 1)
# Design   : docs/.../erp/20260930_*_PC*.md  sections 2, 3, 4, 13-2  (work order 20260930 no.1, lane A)
#
# What it does: swaps ONLY the three program folders {app}\api, {app}\web, {app}\watchdog
#   and restarts the HitPan scheduled tasks and the watchdog service. On failure it puts the
#   original three folders back (S7).
# What it never does: it has no database access of any kind and never opens any configuration
#   file with credentials. Customer data is never touched. (gate G-D1)
# Who runs it: a one-time SYSTEM scheduled task "HitPan-LocalSwap" that the ERP API registers
#   after the administrator pressed [Yes] on the main PC. The task deletes itself at the end.
# Hand run is refused: -Ticket must equal request.json ticket and state must be "requested".
#
# -TestRoot <dir> : test mode. Every external action (scheduled tasks, service, process kill,
#   health check, event log) is replaced by a stand-in under <dir>. app_root and material paths
#   must be under <dir>. Failure injection: <dir>\fail-at.txt containing S1|S2|S3|S4|S6|S7.
#   Seal round (20260930 no.1, lane K1): S7R = Invoke-Revert throws on its first line,
#   S7R2 = Invoke-Revert throws after the first folder is back, OVL = another update shows up while
#   we stop (foreign update.lock body + web.old), OVLK = foreign update.lock body only.
#   Seal round lane K2: S6W = the new version writes one attachment in S6 (use with S6),
#   S7C = the S7 carry of customer data out of api fails.
#   Seal round 2 lane A1 (design 15-0, 15-1 N-1 d/e, N-2): LOSKIP = the S0 leftover cleanup is skipped,
#   RBKHC = while the S0 leftover cleanup runs, one file under api.rbk\chat-files is held open
#   (FileShare.None - a real lock).
#   Seal round 2 lane A2 (design 15-1 N-1 a/b, N-3 a): S4C = the S4 carry of customer data into the new api
#   is skipped (it stays in api.rbk), RBKH = while the success cleanup runs, one program file at the top of
#   api.rbk is held open (FileShare.None), RBKHC = the same for one file under api.rbk\chat-files (when no
#   leftover was there at S0; the S0 side of RBKHC is lane A1).
#   Seal round 3 lane C (design 16-1 15/16): PVH = one file in web.rbk is held open (FileShare.None) during the
#   first prev saving of an update and let go before the success retries, PVHH = the same file held to the end of the
#   success cleanup, LOTHROW = the S0 leftover cleanup throws after its list (before the first delete).

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('update', 'rollback')][string]$Mode,
    [Parameter(Mandatory = $true)][string]$Ticket,
    [string]$TestRoot = ''
)

$ErrorActionPreference = 'Stop'

# ---- names (must equal UpdateProcessGate / WatchdogHandoffBootstrap constants - gate G-S1) ----
$SwapTaskName    = 'HitPan-LocalSwap'
$RestoreTaskName = 'HitPan-ERP-keepalive-restore'
$GuardianTask    = 'HitPanWatchdogGuardian'
$WatchdogService = 'HitPanWatchdog'
$SelfReplaceTask = 'HitPanWatchdogSelfReplace'
$SelfReplaceRecoverTask = 'HitPanWatchdogSelfReplaceRecover'
$Parts = @('api', 'web', 'watchdog')
$LockTtlMinutes = 15
$VerifySeconds = 180

# seal 06a: a bad ticket no longer exits here (outside everything, the one-time task stayed forever);
# the main part ends it with Exit-Early 2. The log name never carries a bad ticket into a path.
$TicketOk = ($Ticket -match '^[0-9a-f]{32}$')

$IsTest = -not [string]::IsNullOrEmpty($TestRoot)
# The work folder is {app}\rollback (Program Files: only administrators can write - parallel issue 01).
# Real run: this script is the copy at {app}\rollback\run\local-swap.ps1, so the work folder is its parent.
if ($IsTest) {
    $TestRoot = [System.IO.Path]::GetFullPath($TestRoot)
    $WorkDir = Join-Path $TestRoot 'app\rollback'
    $WdStagingDir = Join-Path $TestRoot 'wdstaging'
} else {
    $WorkDir = Split-Path -Parent $PSScriptRoot
    $WdStagingDir = Join-Path $env:SystemRoot 'System32\config\systemprofile\AppData\Local\HitPan\Updates\staging'
}
$ExpectedAppRoot = Split-Path -Parent $WorkDir
$ManualDir = Join-Path $ExpectedAppRoot 'manual'
$SwapLockPath = Join-Path $WorkDir 'swap.lock'
$ChainMarkPath = Join-Path $WorkDir 'rolled-back.txt'
$TicketMinutes = 10
$RequestPath = Join-Path $WorkDir 'request.json'
$LogName = 'swap-invalid-ticket.log'
if ($TicketOk) { $LogName = "swap-" + $Ticket + ".log" }
$LogPath = Join-Path (Join-Path $WorkDir 'logs') $LogName
$EventBase = 28060
if ($Mode -eq 'rollback') { $EventBase = 28040 }
$Utf8 = New-Object System.Text.UTF8Encoding($false)

$script:Req = $null
$script:AppRoot = $null
$script:HoldLock = $false
$script:LockBody = $null
$script:CreatedNet = $false
$script:StageDir = $null
$script:SrcRoot = $null
$script:Swapped = New-Object System.Collections.ArrayList

# ======================================================================================
# helpers
# ======================================================================================
function Write-Log([string]$msg) {
    $line = (Get-Date).ToUniversalTime().ToString('o') + ' ' + $msg
    try {
        $dir = Split-Path -Parent $LogPath
        if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        [System.IO.File]::AppendAllText($LogPath, $line + "`r`n", $Utf8)
    } catch {
        # the log itself failed; nothing else can record it, write to host so the task history keeps it
        Write-Host ('log write failed: ' + $_.Exception.Message + ' / ' + $line)
    }
}

function Test-FailAt([string]$step) {
    if (-not $IsTest) { return $false }
    $f = Join-Path $TestRoot 'fail-at.txt'
    if (-not (Test-Path -LiteralPath $f)) { return $false }
    return (@((Get-Content -LiteralPath $f -Raw).Trim().Split(',')) -contains $step)
}

function ConvertTo-AsciiJson([string]$json) {
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $json.ToCharArray()) {
        if ([int]$ch -gt 127) { [void]$sb.AppendFormat('\u{0:x4}', [int]$ch) } else { [void]$sb.Append($ch) }
    }
    return $sb.ToString()
}

function Save-Request {
    $json = ConvertTo-AsciiJson ($script:Req | ConvertTo-Json -Depth 6)
    $tmp = $RequestPath + '.tmp'
    [System.IO.File]::WriteAllText($tmp, $json, $Utf8)
    Move-Item -LiteralPath $tmp -Destination $RequestPath -Force
}

function Set-ReqField([string]$name, $value) {
    if ($script:Req.PSObject.Properties.Name -contains $name) { $script:Req.$name = $value }
    else { $script:Req | Add-Member -NotePropertyName $name -NotePropertyValue $value }
}

function Set-State([string]$state, [string]$reason, [string]$step) {
    Set-ReqField 'state' $state
    Set-ReqField 'reason' $reason
    Set-ReqField 'step' $step
    Set-ReqField 'updated_at' ((Get-Date).ToUniversalTime().ToString('o'))
    Set-ReqField 'log' $LogPath
    Save-Request
    Write-Log ("state=" + $state + " reason=" + $reason + " step=" + $step)
}

function ConvertTo-NormVersion([string]$v) {
    if ([string]::IsNullOrWhiteSpace($v)) { return $null }
    $p = $v.Trim().Split('.')
    if ($p.Length -lt 3) { return $null }
    return ($p[0] + '.' + $p[1] + '.' + $p[2])
}

function Compare-Version([string]$a, [string]$b) {
    return ([version]$a).CompareTo([version]$b)
}

function Test-Under([string]$path, [string]$root) {
    $full = [System.IO.Path]::GetFullPath($path).TrimEnd('\')
    $r = [System.IO.Path]::GetFullPath($root).TrimEnd('\')
    return ($full.Equals($r, [System.StringComparison]::OrdinalIgnoreCase) -or
            $full.StartsWith($r + '\', [System.StringComparison]::OrdinalIgnoreCase))
}

# Run an external program without the shell. Returns the exit code. In test mode: stand-in.
function Invoke-Exe([string]$file, [string]$arguments) {
    if ($IsTest) {
        [System.IO.File]::AppendAllText((Join-Path $TestRoot 'calls.log'), ($file + ' ' + $arguments + "`r`n"), $Utf8)
        $tasks = Join-Path $TestRoot 'tasks'
        if (-not (Test-Path -LiteralPath $tasks)) { New-Item -ItemType Directory -Path $tasks -Force | Out-Null }
        $m = [regex]::Match($arguments, '/TN\s+"?([^"\s]+)"?')
        $tn = $null
        if ($m.Success) { $tn = Join-Path $tasks $m.Groups[1].Value }
        if ($file -eq 'schtasks.exe' -and $tn) {
            if ($arguments.StartsWith('/Create')) { Set-Content -LiteralPath $tn -Value 'task'; return 0 }
            if ($arguments.StartsWith('/Delete')) { if (Test-Path -LiteralPath $tn) { Remove-Item -LiteralPath $tn -Force }; return 0 }
            if ($arguments.StartsWith('/Query'))  { if (Test-Path -LiteralPath $tn) { return 0 } else { return 1 } }
        }
        return 0
    }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $file
    $psi.Arguments = $arguments
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEnd()
    $err = $p.StandardError.ReadToEnd()
    $p.WaitForExit()
    Write-Log ("exe " + $file + " " + $arguments + " -> " + $p.ExitCode + " " + ($out + $err).Trim())
    return $p.ExitCode
}

function Invoke-Schtasks([string]$arguments) { return (Invoke-Exe 'schtasks.exe' $arguments) }

function Test-Task([string]$name) { return ((Invoke-Schtasks ('/Query /TN "' + $name + '"')) -eq 0) }

function Get-ServiceState {
    if ($IsTest) {
        $f = Join-Path $TestRoot ('services\' + $WatchdogService)
        if (-not (Test-Path -LiteralPath $f)) { return 'Missing' }
        return (Get-Content -LiteralPath $f -Raw).Trim()
    }
    $s = Get-Service -Name $WatchdogService -ErrorAction SilentlyContinue
    if ($null -eq $s) { return 'Missing' }
    return [string]$s.Status
}

function Set-TestService([string]$state) {
    $dir = Join-Path $TestRoot 'services'
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    Set-Content -LiteralPath (Join-Path $dir $WatchdogService) -Value $state
}

# FileVersion of a part folder, normalized to M.m.b. web has no version stamp of its own.
function Get-PartVersion([string]$dir, [string]$part) {
    if ($IsTest) {
        $f = Join-Path $dir '.testversion'
        if (-not (Test-Path -LiteralPath $f)) { return $null }
        return (ConvertTo-NormVersion (Get-Content -LiteralPath $f -Raw))
    }
    $exe = $null
    if ($part -eq 'api') { $exe = Join-Path $dir 'HitPan.API.exe' }
    if ($part -eq 'watchdog') { $exe = Join-Path $dir 'HitPan.Watchdog.exe' }
    if ($null -eq $exe -or -not (Test-Path -LiteralPath $exe)) { return $null }
    return (ConvertTo-NormVersion ([System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion))
}

function Get-HealthVersion {
    if ($IsTest) { return (Get-PartVersion (Join-Path $script:AppRoot 'api') 'api') }
    $url = 'http://127.0.0.1:' + [string]$script:Req.api_port + '/health'
    try {
        $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -ne 200) { return $null }
        $j = $r.Content | ConvertFrom-Json
        return (ConvertTo-NormVersion ([string]$j.checks.version))
    } catch {
        Write-Log ("health not ready: " + $_.Exception.Message)
        return $null
    }
}

function Write-Event([int]$offset, [string]$type, [string]$msg) {
    $id = $EventBase + $offset
    if ($IsTest) {
        [System.IO.File]::AppendAllText((Join-Path $TestRoot 'events.log'), ([string]$id + ' ' + $type + ' ' + $msg + "`r`n"), $Utf8)
        return
    }
    try {
        Write-EventLog -LogName Application -Source $WatchdogService -EventId $id -EntryType $type -Message $msg
    } catch {
        Write-Log ("event log write failed (" + $id + "): " + $_.Exception.Message)
    }
}

function Add-Usage([string]$result, [string]$reason) {
    try {
        $o = [ordered]@{
            at = (Get-Date).ToUniversalTime().ToString('o'); entry = [string]$script:Req.entry; mode = $Mode
            from = [string]$script:Req.from; to = [string]$script:Req.to; result = $result; reason = $reason
            requested_by = [string]$script:Req.requested_by; auto_state = [string]$script:Req.auto_state; ticket = $Ticket
        }
        $line = ConvertTo-AsciiJson ((New-Object PSObject -Property $o) | ConvertTo-Json -Compress)
        [System.IO.File]::AppendAllText((Join-Path $WorkDir 'usage.jsonl'), $line + "`n", $Utf8)
    } catch {
        Write-Log ("usage line write failed: " + $_.Exception.Message)
    }
}

function Remove-DirSafe([string]$dir) {
    if ([string]::IsNullOrEmpty($dir)) { return }
    try { if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force } }
    catch { Write-Log ("remove failed " + $dir + ": " + $_.Exception.Message) }
}

# ---- seal F-2: customer data that lives INSIDE the program folder {app}\api ----
# chat-files (ChatFileStore default root) and HitpanBackup (BackupService last-resort folder) sit in
# {app}\api, and the swap unit is the whole api folder. They are carried along, never deleted.
# Only these two names are carried (owner approval S-4). A PC that keeps them elsewhere has nothing here.
$CarryDirs = @('chat-files', 'HitpanBackup')

# Move the carry folders from one api folder to another. No overwrite ever: when the target has no
# such folder the whole folder is renamed (same volume); otherwise file by file, and a name that is
# already taken gets ".dup-{ticket}" so both files stay. Only source folders left empty are removed.
# Returns $false when anything could not be moved (what is left stays where it was).
function Move-CarryData([string]$fromApi, [string]$toApi) {
    $ok = $true
    foreach ($c in $CarryDirs) {
        $src = Join-Path $fromApi $c
        if (-not (Test-Path -LiteralPath $src)) { continue }
        $dst = Join-Path $toApi $c
        try {
            if (-not (Test-Path -LiteralPath $toApi)) { throw ('no target folder ' + $toApi) }
            if (-not (Test-Path -LiteralPath $dst)) {
                Move-Item -LiteralPath $src -Destination $dst
                Write-Log ('carried ' + $src + ' -> ' + $dst + ' (folder)')
                continue
            }
            $base = (Get-Item -LiteralPath $src -Force).FullName.TrimEnd('\')
            $n = 0; $dup = 0
            foreach ($f in @(Get-ChildItem -LiteralPath $src -Recurse -File -Force)) {
                $rel = $f.FullName.Substring($base.Length).TrimStart('\')
                $target = Join-Path $dst $rel
                if (Test-Path -LiteralPath $target) {
                    $cand = $target + '.dup-' + $Ticket
                    $k = 1
                    while (Test-Path -LiteralPath $cand) { $cand = $target + '.dup-' + $Ticket + '-' + $k; $k++ }
                    $target = $cand
                    $dup++
                }
                $parent = Split-Path -Parent $target
                if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
                Move-Item -LiteralPath $f.FullName -Destination $target
                $n++
            }
            foreach ($d in @(Get-ChildItem -LiteralPath $src -Recurse -Directory -Force | Sort-Object { $_.FullName.Length } -Descending)) {
                if ($null -eq (Get-ChildItem -LiteralPath $d.FullName -Force | Select-Object -First 1)) { Remove-Item -LiteralPath $d.FullName -Force }
            }
            if ($null -eq (Get-ChildItem -LiteralPath $src -Force | Select-Object -First 1)) { Remove-Item -LiteralPath $src -Force }
            Write-Log ('carried ' + $src + ' -> ' + $dst + ' files=' + $n + ' renamed-dup=' + $dup)
        } catch {
            Write-Log ('carry failed ' + $src + ' -> ' + $dst + ': ' + $_.Exception.Message)
            $ok = $false
        }
    }
    return $ok
}

# true when an api folder still holds at least one file under a carry folder
function Test-HasCarryData([string]$apiDir) {
    foreach ($c in $CarryDirs) {
        $d = Join-Path $apiDir $c
        if (-not (Test-Path -LiteralPath $d)) { continue }
        if ($null -ne (Get-ChildItem -LiteralPath $d -Recurse -File -Force | Select-Object -First 1)) { return $true }
    }
    return $false
}

# the api folders a program folder may hold: itself (api / api.rbk), <dir>\api (prev, stage\<v>),
# <dir>\<any>\api (rollback\stage)
function Get-CarryApiDirs([string]$dir) {
    $list = New-Object System.Collections.ArrayList
    $leaf = Split-Path -Leaf $dir
    if ($leaf -eq 'api' -or $leaf -eq 'api.rbk') { [void]$list.Add($dir) }
    $a = Join-Path $dir 'api'
    if (Test-Path -LiteralPath $a) { [void]$list.Add($a) }
    foreach ($d in @(Get-ChildItem -LiteralPath $dir -Directory -Force)) {
        $a2 = Join-Path $d.FullName 'api'
        if (Test-Path -LiteralPath $a2) { [void]$list.Add($a2) }
    }
    return @($list.ToArray())
}

# seal F-2: the delete gatekeeper for program folders (api.rbk, rollback\prev, rollback\stage, a swapped-out api).
# Customer data still inside is carried to the live {app}\api first; if that cannot be done the folder is
# NOT deleted. Returns $true when the folder is gone (or was never there).
function Remove-AppDirSafe([string]$dir) {
    if ([string]::IsNullOrEmpty($dir)) { return $true }
    if (-not (Test-Path -LiteralPath $dir)) { return $true }
    $live = Join-Path $script:AppRoot 'api'
    $liveFull = [System.IO.Path]::GetFullPath($live).TrimEnd('\')
    foreach ($a in @(Get-CarryApiDirs $dir)) {
        if (-not (Test-HasCarryData $a)) { continue }
        $isLive = [System.IO.Path]::GetFullPath($a).TrimEnd('\').Equals($liveFull, [System.StringComparison]::OrdinalIgnoreCase)
        if (-not $isLive -and (Test-Path -LiteralPath $live)) { [void](Move-CarryData $a $live) }
        if (Test-HasCarryData $a) {
            Write-Log ('kept ' + $dir + ': customer data inside ' + $a + ' could not be carried to ' + $live)
            return $false
        }
    }
    Remove-DirSafe $dir
    return (-not (Test-Path -LiteralPath $dir))
}

# test only (S6W): the new version writes one attachment while it runs (S5..S6)
function Invoke-TestNewCarryFile {
    $d = Join-Path $script:AppRoot 'api\chat-files\T1\202609'
    if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
    [System.IO.File]::WriteAllText((Join-Path $d ('s6w-' + $Ticket + '.bin')), ('s6w-' + $Ticket), $Utf8)
}

# ---- lock: same file and body format as the watchdog UpdateLockFile ({app}\update.lock = "UTC|version") ----
function Test-ForeignLock {
    $f = Join-Path $script:AppRoot 'update.lock'
    if (-not (Test-Path -LiteralPath $f)) { return $false }
    try {
        $stamp = (Get-Content -LiteralPath $f -Raw).Split('|')[0]
        $t = [DateTime]::Parse($stamp, [System.Globalization.CultureInfo]::InvariantCulture,
                               [System.Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
        $age = (Get-Date).ToUniversalTime() - $t
        if ($age.TotalMinutes -lt 0) { return $false }
        return ($age.TotalMinutes -le $LockTtlMinutes)
    } catch {
        Write-Log ("update.lock unreadable, treated as stale: " + $_.Exception.Message)
        return $false
    }
}

function Set-Lock {
    $body = (Get-Date).ToUniversalTime().ToString('o') + '|' + [string]$script:Req.to
    [System.IO.File]::WriteAllText((Join-Path $script:AppRoot 'update.lock'), $body, $Utf8)
    $script:LockBody = $body
    $script:HoldLock = $true
}

# seal 08: update.lock exists and its body is not the one this run wrote last (someone else took it)
function Test-LockNotMine {
    $f = Join-Path $script:AppRoot 'update.lock'
    if (-not (Test-Path -LiteralPath $f)) { return $false }
    try { $body = [System.IO.File]::ReadAllText($f, $Utf8) }
    catch { Write-Log ('update.lock unreadable after stop, treated as not ours: ' + $_.Exception.Message); return $true }
    return ($body -cne $script:LockBody)
}

# seal 08: the watchdog download/backup stretch (before it takes update.lock) is invisible at S0.
# After the watchdog is stopped (S3) look once more. Any of these = another update got in between.
function Test-OverlapAfterStop {
    $why = New-Object System.Collections.ArrayList
    if (Test-Path -LiteralPath (Join-Path $script:AppRoot 'update-swap.marker')) { [void]$why.Add('update-swap.marker') }
    if (Test-Path -LiteralPath (Join-Path $script:AppRoot 'watchdog.new')) { [void]$why.Add('watchdog.new') }
    foreach ($p in $Parts) { if (Test-Path -LiteralPath (Join-Path $script:AppRoot ($p + '.old'))) { [void]$why.Add($p + '.old') } }
    if (Test-Task $SelfReplaceTask) { [void]$why.Add($SelfReplaceTask) }
    if (Test-Task $SelfReplaceRecoverTask) { [void]$why.Add($SelfReplaceRecoverTask) }
    if (Test-LockNotMine) { [void]$why.Add('update.lock') }
    if ($why.Count -eq 0) { return $false }
    Write-Log ('S3 another update showed up while stopping: ' + ($why -join ','))
    return $true
}

# test only (OVL / OVLK): another updater shows up - its update.lock body, and with OVL its web.old folder
function Invoke-TestOverlap([bool]$withOld) {
    [System.IO.File]::WriteAllText((Join-Path $script:AppRoot 'update.lock'), ((Get-Date).ToUniversalTime().ToString('o') + '|9.9.9'), $Utf8)
    if ($withOld) { New-Item -ItemType Directory -Path (Join-Path $script:AppRoot 'web.old') -Force | Out-Null }
}

function Unlock-UpdateLock {
    if (-not $script:HoldLock) { return }
    try { Remove-Item -LiteralPath (Join-Path $script:AppRoot 'update.lock') -Force -ErrorAction Stop; $script:HoldLock = $false }
    catch { Write-Log ("update.lock release failed (expires in " + $LockTtlMinutes + " min): " + $_.Exception.Message) }
}

# ======================================================================================
# S3 stop / S5 start / S6 verify
# ======================================================================================
function Stop-All {
    $slot = [int]$script:Req.slot
    Invoke-Schtasks ('/Change /TN "' + $GuardianTask + '" /DISABLE') | Out-Null
    if ($IsTest) { Set-TestService 'Stopped' }
    else {
        $s = Get-Service -Name $WatchdogService -ErrorAction SilentlyContinue
        if ($null -ne $s -and $s.Status -ne 'Stopped') {
            Stop-Service -Name $WatchdogService -Force
            $s.WaitForStatus('Stopped', (New-TimeSpan -Seconds 60))
        }
        Get-Process -Name 'HitPan.Watchdog' -ErrorAction SilentlyContinue | Stop-Process -Force
    }
    $k1 = Invoke-Schtasks ('/Change /TN "HitPan-ERP-API-keepalive-' + $slot + '" /DISABLE')
    $k2 = Invoke-Schtasks ('/Change /TN "HitPan-ERP-WEB-keepalive-' + $slot + '" /DISABLE')
    if ($k1 -ne 0 -or $k2 -ne 0) { throw ('keepalive disable failed api=' + $k1 + ' web=' + $k2) }
    Invoke-Schtasks ('/End /TN "HitPan-ERP-API-tenant-' + $slot + '"') | Out-Null
    Invoke-Schtasks ('/End /TN "HitPan-ERP-WEB-tenant-' + $slot + '"') | Out-Null
    Invoke-Exe 'taskkill.exe' '/F /IM HitPan.API.exe' | Out-Null
    Invoke-Exe 'taskkill.exe' '/F /IM HitPan.Web.exe' | Out-Null
    if (-not $IsTest) {
        $deadline = (Get-Date).AddSeconds(30)
        while ((Get-Date) -lt $deadline) {
            $left = Get-Process -Name 'HitPan.API', 'HitPan.Web', 'HitPan.Watchdog' -ErrorAction SilentlyContinue
            if ($null -eq $left) { break }
            Start-Sleep -Seconds 1
        }
        $left = Get-Process -Name 'HitPan.API', 'HitPan.Web', 'HitPan.Watchdog' -ErrorAction SilentlyContinue
        if ($null -ne $left) { throw 'HitPan processes still running after 30s' }
    }
    if (Test-FailAt 'S3') { throw 'injected S3 failure' }
    Write-Log 'S3 stopped'
}

# One line of Start-All. A failure (exit code or exception) only turns the result false;
# it never skips the next line (seal F-1: one broken line must not leave the rest switched off).
function Invoke-StartStep([string]$what, [scriptblock]$action) {
    try { return [bool](& $action) }
    catch { Write-Log ('S5 ' + $what + ' failed: ' + $_.Exception.Message); return $false }
}

function Start-All {
    $slot = [int]$script:Req.slot
    $ok = $true
    if (-not (Invoke-StartStep 'api keepalive enable' { (Invoke-Schtasks ('/Change /TN "HitPan-ERP-API-keepalive-' + $slot + '" /ENABLE')) -eq 0 })) { $ok = $false }
    if (-not (Invoke-StartStep 'web keepalive enable' { (Invoke-Schtasks ('/Change /TN "HitPan-ERP-WEB-keepalive-' + $slot + '" /ENABLE')) -eq 0 })) { $ok = $false }
    # the exit codes of /Run and the guardian line were never part of the result; only an exception is
    if (-not (Invoke-StartStep 'api run' { Invoke-Schtasks ('/Run /TN "HitPan-ERP-API-tenant-' + $slot + '"') | Out-Null; $true })) { $ok = $false }
    if (-not (Invoke-StartStep 'web run' { Invoke-Schtasks ('/Run /TN "HitPan-ERP-WEB-tenant-' + $slot + '"') | Out-Null; $true })) { $ok = $false }
    if ($IsTest) {
        if (-not (Invoke-StartStep 'watchdog service start' { Set-TestService 'Running'; $true })) { $ok = $false }
    } else {
        if (-not (Invoke-StartStep 'watchdog service start' { Start-Service -Name $WatchdogService; $true })) { $ok = $false }
    }
    if (-not (Invoke-StartStep 'guardian enable' { Invoke-Schtasks ('/Change /TN "' + $GuardianTask + '" /ENABLE') | Out-Null; $true })) { $ok = $false }
    Write-Log ("S5 started ok=" + $ok)
    return $ok
}

function Test-Running([string]$want, [string]$failStep) {
    if (Test-FailAt $failStep) { Write-Log ('injected ' + $failStep + ' verify failure'); return $false }
    $deadline = (Get-Date).AddSeconds($VerifySeconds)
    while ($true) {
        $h = Get-HealthVersion
        $svc = Get-ServiceState
        $api = Get-PartVersion (Join-Path $script:AppRoot 'api') 'api'
        $wd = Get-PartVersion (Join-Path $script:AppRoot 'watchdog') 'watchdog'
        if ($h -eq $want -and $svc -eq 'Running' -and $api -eq $want -and $wd -eq $want) {
            Write-Log ('verify ok ' + $want); return $true
        }
        if ((Get-Date) -ge $deadline) {
            Write-Log ('verify timeout want=' + $want + ' health=' + $h + ' svc=' + $svc + ' api=' + $api + ' wd=' + $wd)
            return $false
        }
        Start-Sleep -Seconds 3
    }
}

# ======================================================================================
# S4 swap / S7 revert
# ======================================================================================
function Move-Part([string]$from, [string]$to) {
    # destination is cleared FIRST: Move-Item into an existing folder nests it (A-1 precedent)
    if (Test-Path -LiteralPath $to) { Remove-Item -LiteralPath $to -Recurse -Force }
    Move-Item -LiteralPath $from -Destination $to
}

function Invoke-Swap {
    # seal2 N-1 (e) safety line: Move-Part clears its destination with a raw delete (no gatekeeper). A .rbk that is
    # already there (a leftover the S0 cleanup did not take) may hold customer data - stop before anything moves (S7).
    foreach ($p in $Parts) { if (Test-Path -LiteralPath (Join-Path $script:AppRoot ($p + '.rbk'))) { throw ('S4 ' + $p + '.rbk is already there - not swapping over it') } }
    foreach ($p in $Parts) {
        $dst = Join-Path $script:AppRoot $p
        $rbk = $dst + '.rbk'
        Move-Part $dst $rbk
        [void]$script:Swapped.Add($p)
        if (Test-FailAt 'S4') { throw 'injected S4 failure' }
        # test only: a folder reappears at the destination (what nested api\api in the A-1 accident)
        if (Test-FailAt 'S4X') { New-Item -ItemType Directory -Path $dst -Force | Out-Null }
        Move-Part (Join-Path $script:SrcRoot $p) $dst
        Write-Log ('S4 swapped ' + $p)
        # test only (S4C): the carry below is skipped - customer data stays in api.rbk (what a carry that could not finish leaves)
        if ($p -eq 'api' -and (Test-FailAt 'S4C')) { Write-Log 'injected S4C: S4 customer data carry skipped'; continue }
        # seal F-2 (a): customer data rides along into the new api at once (services are stopped).
        # If anything is left in api.rbk the delete gatekeeper keeps that folder later.
        if ($p -eq 'api' -and -not (Move-CarryData $rbk $dst)) { Write-Log 'S4 customer data carry incomplete - rest stays in api.rbk' }
    }
}

function Invoke-Revert {
    if (Test-FailAt 'S7R') { throw 'injected S7R failure (revert, first line)' }
    $list = @($script:Swapped.ToArray())
    [array]::Reverse($list)
    foreach ($p in $list) {
        $dst = Join-Path $script:AppRoot $p
        $rbk = $dst + '.rbk'
        if (-not (Test-Path -LiteralPath $rbk)) { continue }
        # seal F-2 (b): before api goes back, what the new version kept (attachments written in S5..S6 too)
        # rides back into api.rbk. If it cannot, api is not touched (the S7 catch ends broken).
        if ($p -eq 'api' -and (Test-Path -LiteralPath $dst)) {
            if (Test-FailAt 'S7C') { Write-Log 'injected S7C carry failure' }
            else { [void](Move-CarryData $dst $rbk) }
            if (Test-HasCarryData $dst) { throw ('S7 customer data could not be carried out of ' + $dst + ' - api left as it is') }
        }
        if (Test-Path -LiteralPath $dst) {
            $back = Join-Path $script:SrcRoot $p
            if ($script:Req.material.kind -eq 'prev' -and -not (Test-Path -LiteralPath $back)) { Move-Item -LiteralPath $dst -Destination $back }
            elseif (-not (Remove-AppDirSafe $dst)) { throw ('S7 could not remove ' + $dst) }
        }
        Move-Part $rbk $dst
        Write-Log ('S7 restored ' + $p)
        if (Test-FailAt 'S7R2') { throw ('injected S7R2 failure (revert, after ' + $p + ')') }
    }
}

# ======================================================================================
# S1 material
# ======================================================================================
function Test-PartsReady([string]$root) {
    foreach ($p in $Parts) {
        $d = Join-Path $root $p
        if (-not (Test-Path -LiteralPath $d)) { Write-Log ('material missing ' + $d); return $false }
        if ($null -eq (Get-ChildItem -LiteralPath $d -Force | Select-Object -First 1)) { Write-Log ('material empty ' + $d); return $false }
    }
    $to = [string]$script:Req.to
    foreach ($p in @('api', 'watchdog')) {
        $v = Get-PartVersion (Join-Path $root $p) $p
        if ($v -ne $to) { Write-Log ('material version ' + $p + '=' + $v + ' want ' + $to); return $false }
    }
    return $true
}

function Test-PrevHashes([string]$prev) {
    $list = Join-Path $prev 'sha256.txt'
    if (-not (Test-Path -LiteralPath $list)) { Write-Log 'prev sha256.txt missing'; return $false }
    $n = 0
    foreach ($line in [System.IO.File]::ReadAllLines($list, $Utf8)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $m = [regex]::Match($line, '^([0-9a-fA-F]{64})\s+(.+)$')
        if (-not $m.Success) { Write-Log ('prev sha256 bad line ' + $line); return $false }
        $f = Join-Path $prev $m.Groups[2].Value.Trim()
        if (-not (Test-Under $f $prev) -or -not (Test-Path -LiteralPath $f)) { Write-Log ('prev file missing ' + $f); return $false }
        $h = (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash
        if (-not $h.Equals($m.Groups[1].Value, [System.StringComparison]::OrdinalIgnoreCase)) { Write-Log ('prev hash mismatch ' + $f); return $false }
        $n++
    }
    return ($n -gt 0)
}

function Expand-Material([string]$zip) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Remove-DirSafe $script:StageDir
    New-Item -ItemType Directory -Path $script:StageDir -Force | Out-Null
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $script:StageDir)
}

# seal 05 (worker re-check - the API decided first, things may have changed since):
# the staging zip must be the version installed right before this one. Returns $null when allowed,
# otherwise why not.
#  (a) a stale prev: {app}\rollback\prev exists and was replaced by another version than the current one
#      (a manual update or the setup EXE came in between) -> refuse
#  (b) version history {app}\rollback\versions-seen.txt ("M.m.b|UTC" per line, appended by the API at start):
#      when the last line is the current version, the nearest line above with another version is the one
#      installed right before -> the zip must be exactly that one. Unknown history -> only (a).
function Get-StagingZipRefusal {
    $now = ConvertTo-NormVersion ([string]$script:Req.from)
    $to = ConvertTo-NormVersion ([string]$script:Req.to)
    $prev = Join-Path $script:AppRoot 'rollback\prev'
    if (Test-Path -LiteralPath $prev) {
        $by = $null
        $byFile = Join-Path $prev 'replaced-by.txt'
        if (Test-Path -LiteralPath $byFile) { $by = ConvertTo-NormVersion (Get-Content -LiteralPath $byFile -Raw) }
        if ($by -ne $now) { return ('stale prev replaced-by=' + $by + ' now=' + $now) }
    }
    $seen = Join-Path $script:AppRoot 'rollback\versions-seen.txt'
    if (-not (Test-Path -LiteralPath $seen)) { return $null }
    $vers = New-Object System.Collections.ArrayList
    try {
        foreach ($line in [System.IO.File]::ReadAllLines($seen, $Utf8)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $v = ConvertTo-NormVersion ($line.Split('|')[0])
            if ($null -ne $v) { [void]$vers.Add($v) }
        }
    } catch {
        Write-Log ('versions-seen.txt unreadable, history unknown: ' + $_.Exception.Message)
        return $null
    }
    if ($vers.Count -eq 0 -or $vers[$vers.Count - 1] -ne $now) { return $null }
    $before = $null
    for ($i = $vers.Count - 2; $i -ge 0; $i--) { if ($vers[$i] -ne $now) { $before = $vers[$i]; break } }
    if ($null -ne $before -and $before -ne $to) { return ('zip ' + $to + ' is not the version installed right before ' + $now + ' (' + $before + ')') }
    return $null
}

# returns $null when ready, otherwise a reason code
function Initialize-Material {
    $mat = $script:Req.material
    $to = [string]$script:Req.to
    $script:StageDir = Join-Path $script:AppRoot ('rollback\stage\' + $to)
    if (Test-FailAt 'S1') { return 'material_invalid' }
    if ($mat.kind -eq 'prev') {
        $prev = [string]$mat.path
        $ver = $null; $by = $null
        if (Test-Path -LiteralPath (Join-Path $prev 'version.txt')) { $ver = ConvertTo-NormVersion (Get-Content -LiteralPath (Join-Path $prev 'version.txt') -Raw) }
        if (Test-Path -LiteralPath (Join-Path $prev 'replaced-by.txt')) { $by = ConvertTo-NormVersion (Get-Content -LiteralPath (Join-Path $prev 'replaced-by.txt') -Raw) }
        if ($ver -ne $to -or $by -ne [string]$script:Req.from) { Write-Log ('prev is not the previous version ver=' + $ver + ' by=' + $by); return 'material_invalid' }
        if (-not (Test-PrevHashes $prev)) { return 'material_invalid' }
        $script:SrcRoot = $prev
        $script:StageDir = $null
    } else {
        $zip = [string]$mat.path
        if (-not (Test-Path -LiteralPath $zip)) { Write-Log ('zip missing ' + $zip); return 'material_invalid' }
        if ($Mode -eq 'rollback') {
            $skip = Get-StagingZipRefusal
            if ($null -ne $skip) { Write-Log ('S1 staging zip refused: ' + $skip); return 'material_invalid' }
        }
        if ($Mode -eq 'update') {
            $h = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
            if (-not $h.Equals([string]$mat.sha256, [System.StringComparison]::OrdinalIgnoreCase)) {
                Write-Log ('zip hash mismatch ' + $h); return 'hash_mismatch'
            }
        }
        try { Expand-Material $zip }
        catch { Write-Log ('zip extract failed: ' + $_.Exception.Message); Remove-DirSafe $script:StageDir; return 'material_invalid' }
        $script:SrcRoot = $script:StageDir
    }
    if (-not (Test-PartsReady $script:SrcRoot)) { Remove-DirSafe $script:StageDir; return 'material_invalid' }
    Write-Log ('S1 material ready ' + $script:SrcRoot)
    return $null
}

# ======================================================================================
# S0 request check
# ======================================================================================
function Test-RequestValid {
    $r = $script:Req
    foreach ($n in @('from', 'to', 'material', 'app_root', 'slot', 'api_port', 'requested_by', 'entry')) {
        if ($null -eq $r.$n -or [string]::IsNullOrWhiteSpace([string]$r.$n)) { Write-Log ('request missing ' + $n); return $false }
    }
    $from = ConvertTo-NormVersion ([string]$r.from); $to = ConvertTo-NormVersion ([string]$r.to)
    if ($null -eq $from -or $null -eq $to) { return $false }
    if ($Mode -eq 'rollback' -and (Compare-Version $to $from) -ge 0) { Write-Log 'rollback target is not lower'; return $false }
    if ($Mode -eq 'update' -and (Compare-Version $to $from) -le 0) { Write-Log 'update target is not higher'; return $false }
    if ([int]$r.slot -lt 1) { return $false }
    $app = [System.IO.Path]::GetFullPath([string]$r.app_root).TrimEnd('\')
    if (-not $app.Equals([System.IO.Path]::GetFullPath($ExpectedAppRoot).TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Log 'app_root is not the parent of the work folder'; return $false
    }
    try {
        $at = [DateTime]::Parse([string]$r.requested_at, [System.Globalization.CultureInfo]::InvariantCulture,
                                [System.Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
        $age = ((Get-Date).ToUniversalTime() - $at).TotalMinutes
        if ($age -lt -1 -or $age -gt $TicketMinutes) { Write-Log ('request expired age=' + $age); return $false }
    } catch { Write-Log ('requested_at unreadable: ' + $_.Exception.Message); return $false }
    if (-not (Test-Path -LiteralPath (Join-Path $app 'api')) -or -not (Test-Path -LiteralPath (Join-Path $app 'watchdog'))) {
        # seal2 15-0 M2: a program folder is gone but its .rbk is there = a swap cut off in the middle of S4.
        # That is not a bad request; S0 refuses it as swap_interrupted and touches no folder.
        $anyRbk = $false
        foreach ($p in $Parts) { if (Test-Path -LiteralPath (Join-Path $app ($p + '.rbk'))) { $anyRbk = $true } }
        if (-not $anyRbk) { Write-Log 'app_root has no api/watchdog'; return $false }
        Write-Log 'app_root is missing a program folder but a .rbk is there - judged at S0 (swap_interrupted)'
    }
    $script:AppRoot = $app
    $kind = [string]$r.material.kind; $path = [string]$r.material.path
    if ([string]::IsNullOrWhiteSpace($path)) { return $false }
    $allowed = $false
    if ($Mode -eq 'rollback' -and $kind -eq 'prev') {
        $allowed = [System.IO.Path]::GetFullPath($path).TrimEnd('\').Equals((Join-Path $app 'rollback\prev'), [System.StringComparison]::OrdinalIgnoreCase)
    }
    if ($Mode -eq 'rollback' -and $kind -eq 'staging_zip') {
        $allowed = (Test-Under $path $WdStagingDir) -and ([System.IO.Path]::GetFileName($path) -eq ('hitpan-' + $to + '.zip'))
    }
    if ($Mode -eq 'update' -and $kind -eq 'manual_zip') {
        $allowed = (Test-Under $path (Join-Path $ManualDir 'staging')) -and ([string]$r.material.sha256 -match '^[0-9a-fA-F]{64}$')
    }
    if (-not $allowed) { Write-Log ('material not allowed kind=' + $kind + ' path=' + $path) }
    return $allowed
}

# The S0 check before seal round 2 (every .rbk = busy, .old not looked at). Kept as it was (rule 1: add only);
# the main part now calls Get-S0Refusal. The gate controls put this line back to show the old behaviour.
function Test-UpdateBusy {
    if (Test-Path -LiteralPath (Join-Path $script:AppRoot 'update-swap.marker')) { return $true }
    if (Test-Path -LiteralPath (Join-Path $script:AppRoot 'watchdog.new')) { return $true }
    foreach ($p in $Parts) { if (Test-Path -LiteralPath (Join-Path $script:AppRoot ($p + '.rbk'))) { return $true } }
    if (Test-ForeignLock) { return $true }
    if (Test-Task $SelfReplaceTask) { return $true }
    if (Test-Task $SelfReplaceRecoverTask) { return $true }
    return $false
}

# ---- seal2 (design 15-0): a {p}.rbk left at S0 is judged by its SHAPE, not as "in progress" ----
# Only this worker makes .rbk folders and swaps run one at a time (swap.lock), so a .rbk seen at S0 belongs
# to an EARLIER swap that ended or was cut off.
#   M1 no .rbk | M2 a .rbk and a live program folder is missing | M3 live api/watchdog versions differ or
#   are unreadable | M6 (seal3) the old generation whose saving into prev did not finish (Clear-Leftover finishes it) |
#   M5 anything else = a leftover (not busy) that Clear-Leftover takes away.
# M4 (last request broken and nothing changed since) is judged by the launcher only (design 15-4).
function Get-LeftoverShape {
    $any = $false
    foreach ($p in $Parts) { if (Test-Path -LiteralPath (Join-Path $script:AppRoot ($p + '.rbk'))) { $any = $true } }
    if (-not $any) { return 'M1' }
    foreach ($p in $Parts) { if (-not (Test-Path -LiteralPath (Join-Path $script:AppRoot $p))) { return 'M2' } }
    $a = Get-PartVersion (Join-Path $script:AppRoot 'api') 'api'
    $w = Get-PartVersion (Join-Path $script:AppRoot 'watchdog') 'watchdog'
    if ($null -eq $a -or $null -eq $w -or $a -ne $w) { return 'M3' }
    # seal3 15 (design 16-0): M6 = the .rbk folders are the old generation whose saving into prev did not finish
    # (the mark written before the move is still there, it names the live version and prev has no sha256.txt).
    # Not a leftover: Clear-Leftover finishes the saving instead of deleting them. Not busy either (like M5).
    $mark = Get-PrevSavingMark
    if ($null -ne $mark -and -not $mark.stale) { return 'M6' }
    return 'M5'
}

# S0 refusal (design 15-0 order). Returns $null when the swap may go on, otherwise the reason code.
#  1 watchdog progress marks -> update_in_progress  2 M2/M3 -> swap_interrupted
#  3 {p}.old (the watchdog cleans it at its next update; we only look) -> update_cleanup_pending
# Nothing here stops anything or touches a folder.
function Get-S0Refusal {
    if (Test-Path -LiteralPath (Join-Path $script:AppRoot 'update-swap.marker')) { return 'update_in_progress' }
    if (Test-Path -LiteralPath (Join-Path $script:AppRoot 'watchdog.new')) { return 'update_in_progress' }
    if (Test-ForeignLock) { return 'update_in_progress' }
    if (Test-Task $SelfReplaceTask) { return 'update_in_progress' }
    if (Test-Task $SelfReplaceRecoverTask) { return 'update_in_progress' }
    $shape = Get-LeftoverShape
    if ($shape -eq 'M2' -or $shape -eq 'M3') { Write-Log ('S0 leftover shape ' + $shape); return 'swap_interrupted' }
    foreach ($p in $Parts) {
        if (Test-Path -LiteralPath (Join-Path $script:AppRoot ($p + '.old'))) { Write-Log ('S0 ' + $p + '.old is there (watchdog cleanup not done)'); return 'update_cleanup_pending' }
    }
    return $null
}

# true when every file under $dir can be opened exclusively right now (nothing holds it)
function Test-DirFree([string]$dir) {
    foreach ($f in @(Get-ChildItem -LiteralPath $dir -Recurse -File -Force)) {
        try { $s = [System.IO.File]::Open($f.FullName, 'Open', 'Read', 'None'); $s.Dispose() }
        catch { Write-Log ('S0 leftover file in use ' + $f.FullName + ': ' + $_.Exception.Message); return $false }
    }
    return $true
}

# test only (RBKHC): hold the first file under $dir open with FileShare.None. Returns the stream or $null.
function Open-TestHold([string]$dir) {
    if (-not (Test-Path -LiteralPath $dir)) { return $null }
    $f = Get-ChildItem -LiteralPath $dir -Recurse -File -Force | Select-Object -First 1
    if ($null -eq $f) { return $null }
    Write-Log ('test hold ' + $f.FullName)
    return [System.IO.File]::Open($f.FullName, 'Open', 'Read', 'None')
}

# seal2 N-1 (d): take away the M5 leftover .rbk folders at S0 - after Set-Lock, before S1, with nothing stopped.
# Every delete goes through the gatekeeper (customer data is carried to the live {app}\api first). A .rbk with a
# file in use is not touched at all (no half carry). Returns $null when nothing is left, otherwise the honest
# reason: carry_pending (customer data still inside api.rbk) or cleanup_pending (program files only).
#
# seal3 16 (a): the whole body runs in its own try/catch. A throw here (a folder listing, a file check) used to reach
# the main catch and end broken/revert_failed at S0 although nothing was stopped or swapped. Now it is refused at S0
# with the honest reason (Get-RbkLeftReason; cleanup_pending when that throws too or nothing is left to name).
# seal3 15 (d)(e): the prev-saving mark is looked at first. A live mark (M6) = the .rbk folders are the old generation:
# the saving is finished (they are not deleted); if it cannot be finished the swap is refused here (nothing stopped).
# A stale mark is removed alone - the .rbk folders then go the M5 way and prev is not touched.
function Clear-Leftover {
    try {
        $mark = Get-PrevSavingMark
        # seal4 18 (b): a mark that cannot be read - the mark and the .rbk folders are not touched, refused here (nothing stopped)
        if ($null -ne $mark -and $mark.unread) { Write-Log 'S0 prev-saving mark unreadable - mark and old folders kept'; return (Get-RbkLeftReason) }
        if ($null -ne $mark -and $mark.stale) {
            Write-Log ('S0 prev-saving mark is stale (from=' + $mark.from + ' to=' + $mark.to + ') - mark removed, prev untouched')
            try { Remove-Item -LiteralPath $mark.path -Force }
            catch { Write-Log ('S0 stale prev-saving mark delete failed (judged stale again next time): ' + $_.Exception.Message) }
        }
        if ($null -ne $mark -and -not $mark.stale) { if (-not (Complete-PrevGeneration $mark.from $mark.to)) { Write-Log 'S0 previous generation saving not finished - old folders kept'; return (Get-RbkLeftReason) } }
        $left = @($Parts | Where-Object { Test-Path -LiteralPath (Join-Path $script:AppRoot ($_ + '.rbk')) })
        if ($left.Count -eq 0) { return $null }
        if (Test-FailAt 'LOSKIP') { Write-Log 'injected LOSKIP: S0 leftover cleanup skipped'; return $null }
        if (Test-FailAt 'LOTHROW') { throw 'injected LOTHROW: S0 leftover cleanup threw' }
        $hold = $null
        if (Test-FailAt 'RBKHC') { $hold = Open-TestHold (Join-Path $script:AppRoot 'api.rbk\chat-files') }
        try {
            foreach ($p in $left) {
                $rbk = Join-Path $script:AppRoot ($p + '.rbk')
                if (-not (Test-DirFree $rbk)) { Write-Log ('S0 leftover ' + $rbk + ' kept: a file in it is in use'); continue }
                if (Remove-AppDirSafe $rbk) { Write-Log ('S0 leftover ' + $rbk + ' cleared') }
            }
        } finally {
            if ($null -ne $hold) { $hold.Dispose() }
        }
        $still = @($Parts | Where-Object { Test-Path -LiteralPath (Join-Path $script:AppRoot ($_ + '.rbk')) })
        if ($still.Count -eq 0) { return $null }
        $apiRbk = Join-Path $script:AppRoot 'api.rbk'
        if (Test-Path -LiteralPath $apiRbk) {
            foreach ($a in @(Get-CarryApiDirs $apiRbk)) { if (Test-HasCarryData $a) { Write-Log 'S0 leftover not cleared: customer data still in api.rbk'; return 'carry_pending' } }
        }
        Write-Log ('S0 leftover not cleared: ' + ($still -join ','))
        return 'cleanup_pending'
    } catch {
        Write-Log ('S0 leftover cleanup threw: ' + $_.Exception.Message + ' - refused before anything is stopped')
        $r = $null
        try { $r = Get-RbkLeftReason } catch { Write-Log ('S0 leftover reason check threw too: ' + $_.Exception.Message) }
        if ($null -eq $r) { $r = 'cleanup_pending' }
        return $r
    }
}

# ======================================================================================
# finish
# ======================================================================================
# seal2 N-3 (a): the api and watchdog versions that are live when the run ends ({"api":"M.m.b","watchdog":"M.m.b"};
# null when a folder is gone or its version cannot be read). The launcher uses it to tell "nothing changed since a
# broken end" from "the watchdog has put the versions right since". Reading only; never throws.
function Get-PartsAfter {
    $o = [ordered]@{ api = $null; watchdog = $null }
    try {
        $root = $script:AppRoot
        if ([string]::IsNullOrEmpty($root)) { $root = $ExpectedAppRoot }
        $o.api = Get-PartVersion (Join-Path $root 'api') 'api'
        $o.watchdog = Get-PartVersion (Join-Path $root 'watchdog') 'watchdog'
    } catch {
        Write-Log ('parts_after read failed: ' + $_.Exception.Message)
    }
    return (New-Object PSObject -Property $o)
}

function Complete-Swap([string]$state, [string]$reason, [string]$step) {
    Set-ReqField 'parts_after' (Get-PartsAfter)
    Set-State $state $reason $step
    $offset = @{ 'success' = 1; 'refused' = 2; 'reverted' = 3; 'broken' = 4 }[$state]
    $type = 'Information'
    if ($state -eq 'reverted' -or $state -eq 'refused') { $type = 'Warning' }
    if ($state -eq 'broken') { $type = 'Error' }
    Write-Event $offset $type ('HitPan local-swap ' + $Mode + ' ' + [string]$script:Req.from + ' -> ' + [string]$script:Req.to + ' : ' + $state + ' ' + $reason + ' (ticket ' + $Ticket + ')')
    Add-Usage $state $reason
}

function Save-PrevGeneration {
    # update mode: the old three folders (.rbk) become the one and only previous generation
    try {
        $prev = Join-Path $script:AppRoot 'rollback\prev'
        # seal F-2 (c): the old generation goes through the gatekeeper. If customer data in it could not be
        # carried out, it stays as it is and no new generation is saved (the old program folders go instead).
        if (-not (Remove-AppDirSafe $prev)) {
            Write-Log 'previous generation not saved: the old one still holds customer data that could not be carried'
            foreach ($p in $Parts) { [void](Remove-AppDirSafe (Join-Path $script:AppRoot ($p + '.rbk'))) }
            return
        }
        New-Item -ItemType Directory -Path $prev -Force | Out-Null
        # seal3 15 (a): the mark is written BEFORE anything moves and removed only after sha256.txt is written, so a throw
        # or a power cut half way leaves it behind - the .rbk folders are then known to be the old generation (M6).
        [System.IO.File]::WriteAllText((Get-PrevSavingPath), ([string]$script:Req.from + '|' + [string]$script:Req.to + '|' + $Ticket), $Utf8)
        # test only (PVH): one file in web.rbk is held during this first try and let go before the retries
        $hold = $null
        if (Test-FailAt 'PVH') { $hold = Open-TestHold (Join-Path $script:AppRoot 'web.rbk') }
        try { [void](Complete-PrevGeneration ([string]$script:Req.from) ([string]$script:Req.to)) }
        finally { if ($null -ne $hold) { $hold.Dispose() } }
    } catch {
        # preservation never flips a successful swap (best effort, same as the watchdog)
        Write-Log ('previous generation save failed: ' + $_.Exception.Message)
    }
}

# ---- seal3 15 (design 16-0): the prev-saving mark {app}\rollback\prev-saving.txt = "from|to|ticket" (one line) ----
function Get-PrevSavingPath { return (Join-Path $script:AppRoot 'rollback\prev-saving.txt') }

# Reads the mark. $null = no mark. Otherwise from, to, ticket, path and stale: $true when the mark no longer names the
# live generation - unreadable, its "to" is not the live api version (a watchdog update moved on since) or
# prev\sha256.txt is there (someone finished a prev already; it is not ours to overwrite). Never throws.
# seal4 18 (design 17-0): a mark that is there but cannot be read is NOT stale - unread = $true (stale = $false, from/to
# null). Not knowing whose folders they are, nobody deletes the .rbk folders or the mark (the retry and S0 both keep
# them). No time limit: while the mark cannot be read they stay (owner decision V-3 - customer centre).
function Get-PrevSavingMark {
    try {
        $path = Get-PrevSavingPath
        if (-not (Test-Path -LiteralPath $path)) { return $null }
        $o = [ordered]@{ from = $null; to = $null; ticket = $null; path = $path; stale = $true; unread = $false }
        try {
            $f = ([System.IO.File]::ReadAllText($path, $Utf8)).Trim().Split('|')
            if ($f.Length -ge 3) { $o.from = ConvertTo-NormVersion $f[0]; $o.to = ConvertTo-NormVersion $f[1]; $o.ticket = $f[2] }
            $live = Get-PartVersion (Join-Path $script:AppRoot 'api') 'api'
            $stale = ($null -eq $o.from -or $null -eq $o.to -or $null -eq $live)
            if ($o.to -ne $live) { $stale = $true }
            if (Test-Path -LiteralPath (Join-Path $script:AppRoot 'rollback\prev\sha256.txt')) { $stale = $true }
            $o.stale = $stale
        } catch {
            $o.from = $null; $o.to = $null; $o.stale = $false; $o.unread = $true
            Write-Log ('prev-saving mark unreadable (kept, old folders kept): ' + $_.Exception.Message)
        }
        return (New-Object PSObject -Property $o)
    } catch {
        Write-Log ('prev-saving mark check failed (judged unreadable, old folders kept): ' + $_.Exception.Message)
        return (New-Object PSObject -Property ([ordered]@{ from = $null; to = $null; ticket = $null; path = $null; stale = $false; unread = $true }))
    }
}

# seal3 15 (b): finish saving the old generation (the .rbk folders) into {app}\rollback\prev. Idempotent - it can be
# called again after any half way stop - and never throws ($false + a log line when it could not finish).
#  - a part whose prev\{p} is missing: {p}.rbk is moved as a whole, only when no file in it is in use (C-0: Move-Item
#    of a folder with a held file throws AFTER moving the other files - it would split the part in two)
#  - a part that is in both places (a move cut off half way): the files left in {p}.rbk are moved in one by one, never
#    over a file that is already in prev\{p}; {p}.rbk goes when no file is left in it
#  - all three parts in prev: version.txt (from) / replaced-by.txt (to) / sha256.txt, then the mark is removed LAST.
function Complete-PrevGeneration([string]$from, [string]$to) {
    try {
        $prev = Join-Path $script:AppRoot 'rollback\prev'
        if (-not (Test-Path -LiteralPath $prev)) { New-Item -ItemType Directory -Path $prev -Force | Out-Null }
        foreach ($p in $Parts) {
            $rbk = Join-Path $script:AppRoot ($p + '.rbk')
            $dst = Join-Path $prev $p
            if (-not (Test-Path -LiteralPath $rbk)) { continue }
            if (-not (Test-DirFree $rbk)) { Write-Log ('previous generation not finished: a file in ' + $rbk + ' is in use'); return $false }
            if (-not (Test-Path -LiteralPath $dst)) { Move-Item -LiteralPath $rbk -Destination $dst; continue }
            $base = (Get-Item -LiteralPath $rbk -Force).FullName.TrimEnd('\')
            foreach ($f in @(Get-ChildItem -LiteralPath $rbk -Recurse -File -Force)) {
                $target = Join-Path $dst ($f.FullName.Substring($base.Length).TrimStart('\'))
                if (Test-Path -LiteralPath $target) { Write-Log ('previous generation not finished: ' + $target + ' is already there'); return $false }
                $parent = Split-Path -Parent $target
                if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
                Move-Item -LiteralPath $f.FullName -Destination $target
            }
            if ($null -ne (Get-ChildItem -LiteralPath $rbk -Recurse -File -Force | Select-Object -First 1)) { Write-Log ('previous generation not finished: files left in ' + $rbk); return $false }
            Remove-Item -LiteralPath $rbk -Recurse -Force
            Write-Log ('previous generation: the rest of ' + $rbk + ' moved into ' + $dst)
        }
        foreach ($p in $Parts) {
            if (-not (Test-Path -LiteralPath (Join-Path $prev $p))) { Write-Log ('previous generation not finished: prev\' + $p + ' missing'); return $false }
        }
        [System.IO.File]::WriteAllText((Join-Path $prev 'version.txt'), $from, $Utf8)
        [System.IO.File]::WriteAllText((Join-Path $prev 'replaced-by.txt'), $to, $Utf8)
        $sb = New-Object System.Text.StringBuilder
        foreach ($p in $Parts) {
            foreach ($f in (Get-ChildItem -LiteralPath (Join-Path $prev $p) -Recurse -File -Force)) {
                $rel = $f.FullName.Substring($prev.Length).TrimStart('\')
                [void]$sb.Append((Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $rel + "`n")
            }
        }
        [System.IO.File]::WriteAllText((Join-Path $prev 'sha256.txt'), $sb.ToString(), $Utf8)
        $mk = Get-PrevSavingPath
        if (Test-Path -LiteralPath $mk) { Remove-Item -LiteralPath $mk -Force }
        Write-Log 'previous generation saved'
        return $true
    } catch {
        Write-Log ('previous generation save not finished: ' + $_.Exception.Message)
        return $false
    }
}

function Clear-AfterSuccess {
    if ($Mode -eq 'update') {
        Save-PrevGeneration
        try { Remove-Item -LiteralPath ([string]$script:Req.material.path) -Force } catch { Write-Log ('zip delete failed: ' + $_.Exception.Message) }
        # a successful update opens rollback again (parallel issue 02: no chained rollbacks)
        try { if (Test-Path -LiteralPath $ChainMarkPath) { Remove-Item -LiteralPath $ChainMarkPath -Force } }
        catch { Write-Log ('rolled-back mark delete failed: ' + $_.Exception.Message) }
    } else {
        # seal F-2 (c): every program folder delete goes through the gatekeeper (customer data is carried first)
        foreach ($p in $Parts) { [void](Remove-AppDirSafe (Join-Path $script:AppRoot ($p + '.rbk'))) }
        if ($script:Req.material.kind -eq 'prev') { [void](Remove-AppDirSafe ([string]$script:Req.material.path)) }
        # "rolled back to <to>": the API refuses another rollback while the installed version is <to>
        try {
            $mark = [string]$script:Req.to + '|' + [string]$script:Req.from + '|' + (Get-Date).ToUniversalTime().ToString('o')
            [System.IO.File]::WriteAllText($ChainMarkPath, $mark, $Utf8)
        } catch { Write-Log ('rolled-back mark write failed: ' + $_.Exception.Message) }
    }
    [void](Remove-AppDirSafe $script:StageDir)
    [void](Remove-AppDirSafe (Join-Path $script:AppRoot 'rollback\stage'))
    if ($script:CreatedNet) { Invoke-Schtasks ('/Delete /TN "' + $RestoreTaskName + '" /F') | Out-Null }
}

# seal2 N-1 (b): the honest reason for .rbk folders that are still there. $null when none is left,
# carry_pending when customer data is still inside api.rbk, otherwise cleanup_pending (program files only).
function Get-RbkLeftReason {
    $still = @($Parts | Where-Object { Test-Path -LiteralPath (Join-Path $script:AppRoot ($_ + '.rbk')) })
    if ($still.Count -eq 0) { return $null }
    $apiRbk = Join-Path $script:AppRoot 'api.rbk'
    if (Test-Path -LiteralPath $apiRbk) {
        foreach ($a in @(Get-CarryApiDirs $apiRbk)) {
            if (Test-HasCarryData $a) { Write-Log 'leftover not cleared: customer data still in api.rbk'; return 'carry_pending' }
        }
    }
    Write-Log ('leftover not cleared: ' + ($still -join ','))
    return 'cleanup_pending'
}

# seal2 N-1 (a): the success cleanup of the .rbk folders is tried again in the same run - up to 3 more times,
# 10 seconds apart (0 in test mode) - always through the gatekeeper (customer data is carried first, never lost).
$SuccessRetryCount = 3
$SuccessRetrySeconds = 10
if ($IsTest) { $SuccessRetrySeconds = 0 }
function Get-SuccessLeftover {
    for ($i = 1; $i -le $SuccessRetryCount; $i++) {
        $left = @($Parts | Where-Object { Test-Path -LiteralPath (Join-Path $script:AppRoot ($_ + '.rbk')) })
        if ($left.Count -eq 0) { return $null }
        Write-Log ('success cleanup retry ' + $i + '/' + $SuccessRetryCount + ': ' + ($left -join ','))
        if ($SuccessRetrySeconds -gt 0) { Start-Sleep -Seconds $SuccessRetrySeconds }
        # seal3 15 (c): in update mode the .rbk folders under a live prev-saving mark (M6) are the only copy of the old
        # generation - the retry finishes the saving, it never deletes them. Not finished = they stay as they are.
        if ($Mode -eq 'update') {
            $pm = Get-PrevSavingMark
            # seal4 18 (a): a mark that cannot be read - whose folders they are is unknown, they stay as they are
            if ($null -ne $pm -and $pm.unread) { Write-Log 'success cleanup: prev-saving mark unreadable - old folders kept'; continue }
            if ($null -ne $pm -and -not $pm.stale) { [void](Complete-PrevGeneration $pm.from $pm.to); continue }
        }
        foreach ($p in $left) { [void](Remove-AppDirSafe (Join-Path $script:AppRoot ($p + '.rbk'))) }
    }
    return (Get-RbkLeftReason)
}

# test only (RBKH): hold one program file at the top of $dir open with FileShare.None. Returns the stream or $null.
function Open-TestHoldTop([string]$dir) {
    if (-not (Test-Path -LiteralPath $dir)) { return $null }
    $f = Get-ChildItem -LiteralPath $dir -File -Force | Where-Object { -not $_.Name.StartsWith('.') } | Select-Object -First 1
    if ($null -eq $f) { return $null }
    Write-Log ('test hold ' + $f.FullName)
    return [System.IO.File]::Open($f.FullName, 'Open', 'Read', 'None')
}

# seal2 N-1 (a)(b): the success cleanup, then the retries. Returns the reason that goes next to success
# ($null = nothing left). Success stays success: the swap itself is done and verified.
function Invoke-SuccessCleanup {
    $hold = $null
    if (Test-FailAt 'RBKH') { $hold = Open-TestHoldTop (Join-Path $script:AppRoot 'api.rbk') }
    elseif (Test-FailAt 'RBKHC') { $hold = Open-TestHold (Join-Path $script:AppRoot 'api.rbk\chat-files') }
    elseif (Test-FailAt 'PVHH') { $hold = Open-TestHold (Join-Path $script:AppRoot 'web.rbk') } # seal3: held to the end
    $markHold = $null
    try {
        Clear-AfterSuccess | Out-Null
        # test only (PVMR, seal4): the prev-saving mark is held from after the first saving to the end (it cannot be read)
        if ((Test-FailAt 'PVMR') -and (Test-Path -LiteralPath (Get-PrevSavingPath))) { Write-Log 'test hold mark'; $markHold = [System.IO.File]::Open((Get-PrevSavingPath), 'Open', 'Read', 'None') }
        return (Get-SuccessLeftover)
    } finally {
        if ($null -ne $markHold) { $markHold.Dispose() }
        if ($null -ne $hold) { $hold.Dispose() }
    }
}

# Early exit (seal 06a): the request is missing, unreadable or not this run's. Remove ONLY the
# one-time task (a leftover task makes the API report "in progress" forever) and stop.
# swap.lock and request.json are never touched here: they may belong to another ticket.
function Exit-Early([int]$code) {
    Write-Log ('early exit ' + $code + ' (request not for this run) - one-time task removed, lock and request untouched')
    try { Invoke-Schtasks ('/Delete /TN "' + $SwapTaskName + '" /F') | Out-Null }
    catch { Write-Log ('early exit ' + $code + ': one-time task delete failed: ' + $_.Exception.Message) }
    exit $code
}

# ======================================================================================
# main
# ======================================================================================
if (-not $TicketOk) { Exit-Early 2 }
if (-not (Test-Path -LiteralPath $RequestPath)) { Exit-Early 3 }
try { $script:Req = [System.IO.File]::ReadAllText($RequestPath, $Utf8) | ConvertFrom-Json } catch { Exit-Early 4 }
if ([int]$script:Req.schema -ne 1) { Exit-Early 5 }
if ([string]$script:Req.ticket -ne $Ticket) { Exit-Early 6 }
if ([string]$script:Req.mode -ne $Mode) { Exit-Early 7 }
if ([string]$script:Req.state -ne 'requested') { Exit-Early 8 }

$stopped = $false
try {
    Set-State 'running' $null 'S0'
    Write-Event 0 'Information' ('HitPan local-swap ' + $Mode + ' start ' + [string]$script:Req.from + ' -> ' + [string]$script:Req.to + ' (ticket ' + $Ticket + ')')
    if (-not (Test-RequestValid)) { Complete-Swap 'refused' 'request_invalid' 'S0' }
    elseif ($null -ne ($s0Why = Get-S0Refusal)) { Complete-Swap 'refused' $s0Why 'S0' }
    else {
        Set-Lock
        # seal2 N-1 (d): a leftover .rbk is taken away here, nothing stopped yet. Not all gone = refused at S0.
        $why = Clear-Leftover
        $whyStep = 'S0'
        if ($null -eq $why) {
            Set-State 'running' $null 'S1'
            $why = Initialize-Material
            $whyStep = 'S1'
        }
        if ($null -ne $why) { Complete-Swap 'refused' $why $whyStep }
        else {
            Set-State 'running' $null 'S2'
            $netOk = $true
            if (-not (Test-Task $RestoreTaskName)) {
                $slot = [int]$script:Req.slot
                $inner = 'cmd /c schtasks /Change /TN HitPan-ERP-API-keepalive-' + $slot + ' /ENABLE & schtasks /Change /TN HitPan-ERP-WEB-keepalive-' + $slot + ' /ENABLE'
                # boot-time safety net, same name and same action as UpdateProcessGate.RegisterRestoreSafetyNet;
                # removed again after success when this run created it
                $code = Invoke-Schtasks ('/Create /F /TN "' + $RestoreTaskName + '" /TR "' + $inner + '" /SC ONSTART /RU SYSTEM /RL HIGHEST')
                $script:CreatedNet = ($code -eq 0)
                $netOk = ($code -eq 0) -and (Test-Task $RestoreTaskName)
            }
            if (Test-FailAt 'S2') { $netOk = $false }
            if (-not $netOk) {
                if ($script:CreatedNet) { Invoke-Schtasks ('/Delete /TN "' + $RestoreTaskName + '" /F') | Out-Null }
                Remove-DirSafe $script:StageDir
                Complete-Swap 'refused' 'safety_net_failed' 'S2'
            } else {
                $failReason = $null
                $overlap = $false
                if (Test-FailAt 'OVL') { Invoke-TestOverlap $true }
                try {
                    Set-State 'running' $null 'S3'; Set-Lock
                    if (Test-FailAt 'OVLK') { Invoke-TestOverlap $false }
                    $stopped = $true
                    Stop-All
                } catch { Write-Log ('S3 failed: ' + $_.Exception.Message); $failReason = 'stop_failed' }
                # seal 08: look again after the watchdog is stopped - folders stay untouched if anything shows up
                if ($null -eq $failReason) { $overlap = Test-OverlapAfterStop }
                if ($null -eq $failReason -and -not $overlap) {
                    try { Set-State 'running' $null 'S4'; Set-Lock; Invoke-Swap }
                    catch { Write-Log ('S4 failed: ' + $_.Exception.Message); $failReason = 'swap_failed' }
                }
                if ($null -eq $failReason -and -not $overlap) {
                    Set-State 'running' $null 'S5'; Set-Lock
                    [void](Start-All)
                    Set-State 'running' $null 'S6'
                    if (Test-FailAt 'S6W') { Invoke-TestNewCarryFile }
                    if (-not (Test-Running ([string]$script:Req.to) 'S6')) { $failReason = 'verify_failed' }
                }
                if ($overlap) {
                    # nothing was swapped: start everything again and step aside for the other update
                    if (Test-LockNotMine) { $script:HoldLock = $false } # never delete another updater's lock
                    [void](Start-All)
                    $stopped = $false
                    Remove-DirSafe $script:StageDir
                    if ($script:CreatedNet) { Invoke-Schtasks ('/Delete /TN "' + $RestoreTaskName + '" /F') | Out-Null }
                    Complete-Swap 'refused' 'update_in_progress' 'S3'
                } elseif ($null -eq $failReason) {
                    # seal2 N-1 (a)(b): the .rbk cleanup is retried; what is still left is told next to success
                    $okWhy = Invoke-SuccessCleanup
                    Complete-Swap 'success' $okWhy 'S6'
                } else {
                    Set-State 'running' $failReason 'S7'; Set-Lock
                    $back = $false
                    $revertOk = $false
                    # seal F-1: start again whether or not the folders went back (a thrown revert used to
                    # skip Start-All and leave the keepalive tasks and the watchdog switched off)
                    try {
                        try { Stop-All } catch { Write-Log ('S7 stop warning: ' + $_.Exception.Message) }
                        Invoke-Revert
                        $revertOk = $true
                    } catch { Write-Log ('S7 revert failed: ' + $_.Exception.Message) }
                    finally { [void](Start-All) }
                    if ($revertOk) {
                        try { $back = Test-Running ([string]$script:Req.from) 'S7' }
                        catch { Write-Log ('S7 verify failed: ' + $_.Exception.Message) }
                    }
                    if ($back) {
                        [void](Remove-AppDirSafe $script:StageDir)
                        if ($script:CreatedNet) { Invoke-Schtasks ('/Delete /TN "' + $RestoreTaskName + '" /F') | Out-Null }
                        Complete-Swap 'reverted' $failReason 'S7'
                    } else {
                        # keep the boot-time safety net: a reboot brings the keepalive tasks back
                        Complete-Swap 'broken' 'revert_failed' 'S7'
                    }
                }
            }
        }
    }
} catch {
    Write-Log ('unexpected: ' + $_.Exception.Message)
    try {
        if ($stopped) { [void](Start-All) }
        Complete-Swap 'broken' 'revert_failed' ([string]$script:Req.step)
    } catch { Write-Log ('final state write failed: ' + $_.Exception.Message) }
} finally {
    Unlock-UpdateLock
    # the one-at-a-time lock the API created exclusively for this ticket (parallel issue 03)
    try {
        if ((Test-Path -LiteralPath $SwapLockPath) -and ((Get-Content -LiteralPath $SwapLockPath -Raw).StartsWith($Ticket))) {
            Remove-Item -LiteralPath $SwapLockPath -Force
        }
    } catch { Write-Log ('swap.lock release failed: ' + $_.Exception.Message) }
    # the one-time task removes itself (gate G-SV)
    Invoke-Schtasks ('/Delete /TN "' + $SwapTaskName + '" /F') | Out-Null
}
exit 0
