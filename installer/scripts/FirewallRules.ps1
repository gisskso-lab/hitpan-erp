# =================================================================
# FirewallRules.ps1 — 방화벽 규칙 4종
# UDP 7844 (cloudflared) / TCP 3306 (MariaDB) / TCP 5257 (API) / TCP 5234 (Web)
# =================================================================
$ErrorActionPreference = 'Continue'

function Ensure-Rule($name, $direction, $protocol, $port, $profile = 'Private') {
    try {
        if (Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue) {
            Write-Output "[SKIP] $name already exists"
            return
        }
        if ($direction -eq 'Outbound') {
            New-NetFirewallRule -DisplayName $name -Direction Outbound `
                -Protocol $protocol -RemotePort $port -Action Allow | Out-Null
        } else {
            New-NetFirewallRule -DisplayName $name -Direction Inbound `
                -Protocol $protocol -LocalPort $port -Action Allow -Profile $profile | Out-Null
        }
        Write-Output "[OK] $name"
    } catch {
        Write-Output "[FAIL] $name : $($_.Exception.Message)"
    }
}

Ensure-Rule 'HitPan cloudflared (UDP 7844)' 'Outbound' 'UDP' 7844
Ensure-Rule 'HitPan MariaDB (TCP 3306)'    'Inbound'  'TCP' 3306 'Private'
Ensure-Rule 'HitPan API (TCP 5257)'         'Inbound'  'TCP' 5257 'Private'
Ensure-Rule 'HitPan Web (TCP 5234)'         'Inbound'  'TCP' 5234 'Private'

Write-Output "[DONE] Firewall rules applied"
exit 0
