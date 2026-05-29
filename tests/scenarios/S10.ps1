# S10 — TunnelSecret 회전 (cred-file 손상 → WS-28-C 자동 재발급)
param([string]$HealthUrl)
$tunnelId = [Environment]::GetEnvironmentVariable("HITPAN_TUNNEL_ID","Machine")
if ([string]::IsNullOrEmpty($tunnelId)) {
    Write-Output "[S10] HITPAN_TUNNEL_ID env missing — skip"
    exit 0
}
$cred = "$env:USERPROFILE\.cloudflared\$tunnelId.json"
if (-not (Test-Path $cred)) {
    Write-Output "[S10] cred not found — skip"
    exit 0
}
Add-Content $cred "GARBAGE_$(Get-Date -Format yyyyMMddHHmmss)"
Restart-Service cloudflared -Force -ErrorAction SilentlyContinue
Write-Output "[S10] cred corrupted + cloudflared restarted, expecting WS-28-C recovery"
