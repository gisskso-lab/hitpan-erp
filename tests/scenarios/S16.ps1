# S16 — cloudflared 비정상 종료 (sc failure actions로 자동 재시작 확인)
param([string]$HealthUrl)
# --- W-G1-53 (2026-11-19): old tool disabled. No bypass switch. Body below is kept as record (design s6). ---
Write-Host '[blocked] This old test tool is disabled (W-G1-53). It has no guard and can change a real PC.'
Write-Host '[blocked] Use tests/scenarios/comms-integrity/Run-RoutinePcScenarios.ps1 (guarded; without -Confirm it only judges).'
exit 1
# --- end of W-G1-53 block ---
$proc = Get-Process cloudflared -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $proc) {
    Write-Output "[S16] cloudflared not running — skip"
    exit 0
}
Stop-Process -Id $proc.Id -Force
Start-Sleep -Seconds 90
$svc = Get-Service cloudflared -ErrorAction SilentlyContinue
Write-Output "[S16] cloudflared service status: $($svc.Status)"
