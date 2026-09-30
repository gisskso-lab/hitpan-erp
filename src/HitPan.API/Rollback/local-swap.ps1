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

if ($Ticket -notmatch '^[0-9a-f]{32}$') { exit 2 }

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
$LogPath = Join-Path (Join-Path $WorkDir 'logs') ("swap-" + $Ticket + ".log")
$EventBase = 28060
if ($Mode -eq 'rollback') { $EventBase = 28040 }
$Utf8 = New-Object System.Text.UTF8Encoding($false)

$script:Req = $null
$script:AppRoot = $null
$script:HoldLock = $false
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
    $script:HoldLock = $true
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
        if (Test-Path -LiteralPath $dst) {
            $back = Join-Path $script:SrcRoot $p
            if ($script:Req.material.kind -eq 'prev' -and -not (Test-Path -LiteralPath $back)) { Move-Item -LiteralPath $dst -Destination $back }
            else { Remove-Item -LiteralPath $dst -Recurse -Force }
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
    if (-not (Test-Path -LiteralPath (Join-Path $app 'api')) -or -not (Test-Path -LiteralPath (Join-Path $app 'watchdog'))) { Write-Log 'app_root has no api/watchdog'; return $false }
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

function Test-UpdateBusy {
    if (Test-Path -LiteralPath (Join-Path $script:AppRoot 'update-swap.marker')) { return $true }
    if (Test-Path -LiteralPath (Join-Path $script:AppRoot 'watchdog.new')) { return $true }
    foreach ($p in $Parts) { if (Test-Path -LiteralPath (Join-Path $script:AppRoot ($p + '.rbk'))) { return $true } }
    if (Test-ForeignLock) { return $true }
    if (Test-Task $SelfReplaceTask) { return $true }
    if (Test-Task $SelfReplaceRecoverTask) { return $true }
    return $false
}

# ======================================================================================
# finish
# ======================================================================================
function Complete-Swap([string]$state, [string]$reason, [string]$step) {
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
        Remove-DirSafe $prev
        New-Item -ItemType Directory -Path $prev -Force | Out-Null
        foreach ($p in $Parts) { Move-Part (Join-Path $script:AppRoot ($p + '.rbk')) (Join-Path $prev $p) }
        [System.IO.File]::WriteAllText((Join-Path $prev 'version.txt'), [string]$script:Req.from, $Utf8)
        [System.IO.File]::WriteAllText((Join-Path $prev 'replaced-by.txt'), [string]$script:Req.to, $Utf8)
        $sb = New-Object System.Text.StringBuilder
        foreach ($p in $Parts) {
            foreach ($f in (Get-ChildItem -LiteralPath (Join-Path $prev $p) -Recurse -File -Force)) {
                $rel = $f.FullName.Substring($prev.Length).TrimStart('\')
                [void]$sb.Append((Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $rel + "`n")
            }
        }
        [System.IO.File]::WriteAllText((Join-Path $prev 'sha256.txt'), $sb.ToString(), $Utf8)
        Write-Log 'previous generation saved'
    } catch {
        # preservation never flips a successful swap (best effort, same as the watchdog)
        Write-Log ('previous generation save failed: ' + $_.Exception.Message)
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
        foreach ($p in $Parts) { Remove-DirSafe (Join-Path $script:AppRoot ($p + '.rbk')) }
        if ($script:Req.material.kind -eq 'prev') { Remove-DirSafe ([string]$script:Req.material.path) }
        # "rolled back to <to>": the API refuses another rollback while the installed version is <to>
        try {
            $mark = [string]$script:Req.to + '|' + [string]$script:Req.from + '|' + (Get-Date).ToUniversalTime().ToString('o')
            [System.IO.File]::WriteAllText($ChainMarkPath, $mark, $Utf8)
        } catch { Write-Log ('rolled-back mark write failed: ' + $_.Exception.Message) }
    }
    Remove-DirSafe $script:StageDir
    Remove-DirSafe (Join-Path $script:AppRoot 'rollback\stage')
    if ($script:CreatedNet) { Invoke-Schtasks ('/Delete /TN "' + $RestoreTaskName + '" /F') | Out-Null }
}

# ======================================================================================
# main
# ======================================================================================
if (-not (Test-Path -LiteralPath $RequestPath)) { exit 3 }
try { $script:Req = [System.IO.File]::ReadAllText($RequestPath, $Utf8) | ConvertFrom-Json } catch { exit 4 }
if ([int]$script:Req.schema -ne 1) { exit 5 }
if ([string]$script:Req.ticket -ne $Ticket) { exit 6 }
if ([string]$script:Req.mode -ne $Mode) { exit 7 }
if ([string]$script:Req.state -ne 'requested') { exit 8 }

$stopped = $false
try {
    Set-State 'running' $null 'S0'
    Write-Event 0 'Information' ('HitPan local-swap ' + $Mode + ' start ' + [string]$script:Req.from + ' -> ' + [string]$script:Req.to + ' (ticket ' + $Ticket + ')')
    if (-not (Test-RequestValid)) { Complete-Swap 'refused' 'request_invalid' 'S0' }
    elseif (Test-UpdateBusy) { Complete-Swap 'refused' 'update_in_progress' 'S0' }
    else {
        Set-Lock
        Set-State 'running' $null 'S1'
        $why = Initialize-Material
        if ($null -ne $why) { Complete-Swap 'refused' $why 'S1' }
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
                try {
                    Set-State 'running' $null 'S3'; Set-Lock
                    $stopped = $true
                    Stop-All
                } catch { Write-Log ('S3 failed: ' + $_.Exception.Message); $failReason = 'stop_failed' }
                if ($null -eq $failReason) {
                    try { Set-State 'running' $null 'S4'; Set-Lock; Invoke-Swap }
                    catch { Write-Log ('S4 failed: ' + $_.Exception.Message); $failReason = 'swap_failed' }
                }
                if ($null -eq $failReason) {
                    Set-State 'running' $null 'S5'; Set-Lock
                    [void](Start-All)
                    Set-State 'running' $null 'S6'
                    if (-not (Test-Running ([string]$script:Req.to) 'S6')) { $failReason = 'verify_failed' }
                }
                if ($null -eq $failReason) {
                    Clear-AfterSuccess
                    Complete-Swap 'success' $null 'S6'
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
                        Remove-DirSafe $script:StageDir
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
