# ===============================================================
#  HitPan ERP Install Post-Processor (called from Inno Setup [Run])
#  v1.0.7  — S1/S2/S3 scenarios + WinForms root password prompt
#
#  CHANGES v1.0.6 -> v1.0.7
#  - Detects 3 install scenarios (PRD §5):
#      S1 = Fresh install (no MariaDB)
#      S2 = Existing MariaDB, root password unknown
#           -> WinForms dialog asks user for root pwd ONCE
#      S3 = Reinstall / upgrade (hitpan account already exists)
#           -> Skip root step entirely, preserve .env/keys/logs
#  - root login is NOT a fatal error anymore — falls back to:
#      a) hitpan account check (S3 detection)
#      b) WinForms dialog (S2)
#  - All sensitive values cleared from memory after use
#  - Cloudflare Tunnel placeholder (token/cloudflared install) — wired in v1.0.8
#
#  NOTE: All log messages are in English on purpose.
#  Korean text in a BOM-less PS1 file is mangled under PS 5.1 / cp949.
# ===============================================================

param(
    [Parameter(Mandatory=$true)][string]$AppDir,
    [string]$MariaBin = "C:\Program Files\MariaDB 11.4\bin"
)

$ErrorActionPreference = 'Continue'
$LogPath = Join-Path $env:TEMP 'hitpan-install.log'

function Log($msg) {
    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $msg
    [System.IO.File]::AppendAllText($LogPath, $line + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
    Write-Host $line
}

function Fail($msg) {
    Log "!! FATAL: $msg"
    Log "Install log path: $LogPath"
    try { Start-Process -FilePath 'notepad.exe' -ArgumentList $LogPath -ErrorAction SilentlyContinue } catch {}
    exit 1
}

# -- WinForms password prompt (S2 scenario) --------------------
# Inno Setup runs us with /runhidden but Add-Type + Form.ShowDialog
# still surfaces a top-level window. Verified on Win10/11.
function Prompt-RootPassword {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing

    $form = New-Object System.Windows.Forms.Form
    $form.Text = 'HitPan ERP - MariaDB root password required'
    $form.Size = New-Object System.Drawing.Size(440, 200)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $form.TopMost = $true

    $label = New-Object System.Windows.Forms.Label
    $label.Location = New-Object System.Drawing.Point(15, 15)
    $label.Size = New-Object System.Drawing.Size(400, 60)
    $label.Text = "An existing MariaDB was detected, but the default password did not work.`r`nPlease enter the MariaDB 'root' password to continue setup.`r`nThis value is used once and not stored to disk."
    $form.Controls.Add($label)

    $textBox = New-Object System.Windows.Forms.TextBox
    $textBox.Location = New-Object System.Drawing.Point(15, 80)
    $textBox.Size = New-Object System.Drawing.Size(400, 25)
    $textBox.UseSystemPasswordChar = $true
    $form.Controls.Add($textBox)

    $okButton = New-Object System.Windows.Forms.Button
    $okButton.Location = New-Object System.Drawing.Point(245, 120)
    $okButton.Size = New-Object System.Drawing.Size(80, 28)
    $okButton.Text = 'OK'
    $okButton.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $form.Controls.Add($okButton)
    $form.AcceptButton = $okButton

    $cancelButton = New-Object System.Windows.Forms.Button
    $cancelButton.Location = New-Object System.Drawing.Point(335, 120)
    $cancelButton.Size = New-Object System.Drawing.Size(80, 28)
    $cancelButton.Text = 'Cancel'
    $cancelButton.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $form.Controls.Add($cancelButton)
    $form.CancelButton = $cancelButton

    $textBox.Select()
    $result = $form.ShowDialog()

    if ($result -ne [System.Windows.Forms.DialogResult]::OK) {
        return $null
    }
    $pwd = $textBox.Text
    # Clear text box memory (best-effort; managed string is GC'd)
    $textBox.Text = ''
    $form.Dispose()
    return $pwd
}

# -- Scenario detection helpers --------------------------------
function Test-MariaDBService {
    $svc = Get-Service -Name MariaDB -ErrorAction SilentlyContinue
    return ($svc -and $svc.Status -eq 'Running')
}

function Test-MariaDBLogin([string]$user, [string]$pwd, [string]$mariaExe) {
    $tmp = Join-Path $env:TEMP "hitpan-login-test-$([guid]::NewGuid().ToString('N')).sql"
    [System.IO.File]::WriteAllText($tmp, "SELECT 1;", [System.Text.UTF8Encoding]::new($false))
    try {
        $null = & $mariaExe "-u$user" "-p$pwd" -e "source $tmp" 2>&1
        return ($LASTEXITCODE -eq 0)
    }
    finally {
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    }
}

# ===============================================================
Log "========== HitPan ERP install-setup START (v1.0.7) =========="
Log "AppDir=$AppDir"
Log "MariaBin=$MariaBin"

$HitpanDir = Join-Path $AppDir 'hitpan'
$EnvFile   = Join-Path $HitpanDir '.env'
$SqlFile   = Join-Path $AppDir 'hitpan_db.sql'
$MariaExe  = Join-Path $MariaBin 'mariadb.exe'
if (-not (Test-Path $MariaExe)) { $MariaExe = Join-Path $MariaBin 'mysql.exe' }
if (-not (Test-Path $MariaExe)) { Fail "MariaDB client not found under $MariaBin" }

# -- 1. MariaDB service up -------------------------------------
Log '[1/7] Ensuring MariaDB service is running...'
Start-Service -Name MariaDB -ErrorAction SilentlyContinue
$svcOk = $false
for ($i = 0; $i -lt 30; $i++) {
    if (Test-MariaDBService) { $svcOk = $true; break }
    Start-Sleep -Seconds 2
}
if (-not $svcOk) { Fail 'MariaDB service did not start within 60s.' }
Log 'MariaDB service: Running'

# -- 2. Scenario detection (S1/S2/S3) -------------------------
Log '[2/7] Detecting install scenario...'
$Scenario = 'S1-Fresh'
$rootPwd = 'Hitpan2025!'

# S3 check first: if hitpan account already works, this is a reinstall
if (Test-MariaDBLogin -user 'hitpan' -pwd 'Hitpan2025!' -mariaExe $MariaExe) {
    $Scenario = 'S3-Reinstall'
    Log 'Scenario: S3 (existing hitpan account detected — root step skipped)'
}
elseif (Test-MariaDBLogin -user 'root' -pwd 'Hitpan2025!' -mariaExe $MariaExe) {
    $Scenario = 'S1-Fresh'
    Log 'Scenario: S1 (root login OK with default password)'
}
else {
    # Could be S1 first-run with stale process or S2 (unknown root pwd)
    # Distinguish: was MariaDB just installed in this run? If service was started by us — assume S1.
    # Heuristic: hitpan_db doesn't exist yet → likely S2 because S1's earlier MSI step would have set the password.
    Log 'Default root login failed — assuming S2 (existing MariaDB, unknown root password)'
    $Scenario = 'S2-ExistingMariaDB'
    $userPwd = Prompt-RootPassword
    if ([string]::IsNullOrEmpty($userPwd)) {
        Fail 'User cancelled root password entry. Cannot continue.'
    }
    if (-not (Test-MariaDBLogin -user 'root' -pwd $userPwd -mariaExe $MariaExe)) {
        # Clear from memory before exit
        $userPwd = $null
        Fail 'Provided root password did not authenticate.'
    }
    $rootPwd = $userPwd
    $userPwd = $null
}

# -- 3. DB + hitpan account (skip if S3) ----------------------
if ($Scenario -eq 'S3-Reinstall') {
    Log '[3/7] Skipping DB/account creation (S3 reinstall).'
}
else {
    Log "[3/7] Creating database and hitpan account (scenario=$Scenario)..."
    $setupSql = @'
CREATE DATABASE IF NOT EXISTS hitpan_erp CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS 'hitpan'@'localhost' IDENTIFIED BY 'Hitpan2025!';
GRANT ALL PRIVILEGES ON hitpan_erp.* TO 'hitpan'@'localhost';
FLUSH PRIVILEGES;
'@
    $tmpSql = Join-Path $env:TEMP 'hitpan-setup.sql'
    [System.IO.File]::WriteAllText($tmpSql, $setupSql, [System.Text.UTF8Encoding]::new($false))

    & $MariaExe -uroot "-p$rootPwd" -e "source $tmpSql" 2>&1 | ForEach-Object { Log "  (root) $_" }
    $rootExit = $LASTEXITCODE
    $rootPwd = $null  # Clear from memory immediately
    Remove-Item $tmpSql -Force -ErrorAction SilentlyContinue
    if ($rootExit -ne 0) { Fail 'root SQL execution failed.' }
    Log 'DB + hitpan account ready.'
}

# -- 4. Import schema and samples (skip if S3, DB already there) ----
if ($Scenario -eq 'S3-Reinstall') {
    Log '[4/7] Skipping schema import (S3 — DB preserved).'
}
elseif (Test-Path $SqlFile) {
    Log '[4/7] Importing schema and sample data (~59MB, 6 tenants)...'
    $cmd = "`"$MariaExe`" -uhitpan -pHitpan2025! hitpan_erp < `"$SqlFile`""
    cmd /c $cmd 2>&1 | ForEach-Object { Log "  (import) $_" }
    if ($LASTEXITCODE -ne 0) { Fail 'DB import failed.' }
    Log 'DB import OK.'
}
else {
    Fail "$SqlFile not found."
}

# -- 5. Security keys (skip if S3 .env exists) -----------------
$preserveEnv = ($Scenario -eq 'S3-Reinstall' -and (Test-Path $EnvFile))
if ($preserveEnv) {
    Log '[5/7] Preserving existing .env (S3 reinstall — keys retained for data continuity).'
}
else {
    Log '[5/7] Generating security keys...'
    function New-RandomBase64([int]$bytes) {
        $buf = New-Object byte[] $bytes
        [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buf)
        [Convert]::ToBase64String($buf)
    }
    $jwtSecret = New-RandomBase64 64
    # AES key MUST be exactly 32 bytes raw -> 32-char ASCII string
    $aesBuf = New-Object byte[] 24
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($aesBuf)
    $aesKey = ([Convert]::ToBase64String($aesBuf) + '================').Substring(0, 32)
    Log 'Keys generated (64B JWT + 32B AES).'
}

# -- 6. Write .env (skip if preserving) ------------------------
if ($preserveEnv) {
    Log '[6/7] .env preserved (S3).'
}
else {
    Log '[6/7] Writing .env (UTF-8, no BOM)...'
    # PROV_BASE_URL placeholder — wired by Cloudflare Tunnel in v1.0.8 (작3)
    #   beta:  https://hitpan-prov.workers.dev
    #   prod:  https://prov.hitpan.app
    $envContent = @"
ASPNETCORE_ENVIRONMENT=Production
DB_HOST=localhost
DB_PORT=3306
DB_NAME=hitpan_erp
DB_USER=hitpan
DB_PASSWORD=Hitpan2025!
JWT_SECRET=$jwtSecret
JWT_ISSUER=hitpan-erp
JWT_AUDIENCE=hitpan-client
ERP_ENCRYPTION_KEY=$aesKey
HITPAN_LOG_DIR=$HitpanDir\logs
ASPNETCORE_URLS=http://127.0.0.1:5234
PROV_BASE_URL=https://hitpan-prov.workers.dev
"@

    if (-not (Test-Path $HitpanDir)) { New-Item -ItemType Directory -Path $HitpanDir -Force | Out-Null }
    [System.IO.File]::WriteAllText($EnvFile, $envContent, [System.Text.UTF8Encoding]::new($false))

    # Verify BOM absence (DotNetEnv breaks on BOM)
    $firstBytes = [System.IO.File]::ReadAllBytes($EnvFile) | Select-Object -First 3
    if ($firstBytes.Count -ge 3 -and $firstBytes[0] -eq 0xEF -and $firstBytes[1] -eq 0xBB -and $firstBytes[2] -eq 0xBF) {
        Fail '.env was written with BOM. DotNetEnv will misparse the first key.'
    }
    Log ".env written to $EnvFile (no BOM verified)."
}

# -- 7. Smoke test ---------------------------------------------
Log '[7/7] Smoke test: SELECT COUNT(*) FROM tenants...'
$smokeOut = & $MariaExe -uhitpan -pHitpan2025! hitpan_erp -N -B -e "SELECT COUNT(*) FROM tenants;" 2>&1
$smokeOut | ForEach-Object { Log "  (smoke) $_" }
if ($LASTEXITCODE -ne 0) { Fail 'Smoke test failed.' }

$count = 0
[int]::TryParse(($smokeOut | Select-Object -First 1), [ref]$count) | Out-Null
if ($count -lt 1) { Fail "Smoke test returned tenant count=$count. Expected >=1." }
Log "Smoke test OK. tenant_count=$count."

Log "========== HitPan ERP install-setup DONE (scenario=$Scenario) =========="
exit 0
