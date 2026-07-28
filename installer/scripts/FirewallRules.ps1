# =================================================================
# FirewallRules.ps1 — 방화벽 규칙 4종
# UDP 7844 (cloudflared) / TCP 3306 (MariaDB) / TCP 5257 (API) / TCP 5234 (Web)
# =================================================================
$ErrorActionPreference = 'Continue'

function Set-HitPanFirewallRule {
    # PSUseApprovedVerbs 정합 — Ensure-Rule → Set-HitPanFirewallRule
    # PSAvoidAssignmentToAutomaticVariable 정합 — $profile → $netProfile
    param(
        [string]$Name,
        [string]$Direction,
        [string]$Protocol,
        [int]$Port,
        [string]$NetProfile = 'Private'
    )
    try {
        if (Get-NetFirewallRule -DisplayName $Name -ErrorAction SilentlyContinue) {
            Write-Output "[SKIP] $Name already exists"
            return
        }
        if ($Direction -eq 'Outbound') {
            New-NetFirewallRule -DisplayName $Name -Direction Outbound `
                -Protocol $Protocol -RemotePort $Port -Action Allow | Out-Null
        } else {
            New-NetFirewallRule -DisplayName $Name -Direction Inbound `
                -Protocol $Protocol -LocalPort $Port -Action Allow -Profile $NetProfile | Out-Null
        }
        Write-Output "[OK] $Name"
    } catch {
        Write-Output "[FAIL] $Name : $($_.Exception.Message)"
    }
}

Set-HitPanFirewallRule -Name 'HitPan cloudflared (UDP 7844)' -Direction 'Outbound' -Protocol 'UDP' -Port 7844
# 사고 #23 봉합 (WS-20260612-01 2026-06-12): UDP 443 폴백 영역 (ISP 7844 차단 환경 대응)
Set-HitPanFirewallRule -Name 'HitPan cloudflared (UDP 443 fallback)' -Direction 'Outbound' -Protocol 'UDP' -Port 443
Set-HitPanFirewallRule -Name 'HitPan MariaDB (TCP 3306)' -Direction 'Inbound' -Protocol 'TCP' -Port 3306 -NetProfile 'Private'
Set-HitPanFirewallRule -Name 'HitPan API (TCP 5257)' -Direction 'Inbound' -Protocol 'TCP' -Port 5257 -NetProfile 'Private'
Set-HitPanFirewallRule -Name 'HitPan Web (TCP 5234)' -Direction 'Inbound' -Protocol 'TCP' -Port 5234 -NetProfile 'Private'

# 사고 #20 봉합 (WS-20260612-01 2026-06-12): 멀티사업자 포트 영역 (사장님 결재 [[project_multi_business_per_pc]])
#   슬롯 2~5 영역 미리 박음 — 추가 사업자 영역 동적 등록 0건 사고 차단
#   포트: 5257 + 100*N → 5357, 5457, 5557, 5657
Set-HitPanFirewallRule -Name 'HitPan API Slot 2 (TCP 5357)' -Direction 'Inbound' -Protocol 'TCP' -Port 5357 -NetProfile 'Private'
Set-HitPanFirewallRule -Name 'HitPan API Slot 3 (TCP 5457)' -Direction 'Inbound' -Protocol 'TCP' -Port 5457 -NetProfile 'Private'
Set-HitPanFirewallRule -Name 'HitPan API Slot 4 (TCP 5557)' -Direction 'Inbound' -Protocol 'TCP' -Port 5557 -NetProfile 'Private'
Set-HitPanFirewallRule -Name 'HitPan API Slot 5 (TCP 5657)' -Direction 'Inbound' -Protocol 'TCP' -Port 5657 -NetProfile 'Private'

Write-Output "[DONE] Firewall rules applied (single + multi-tenant slots 2~5)"
exit 0
