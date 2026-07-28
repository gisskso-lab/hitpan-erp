# S16 — cloudflared 비정상 종료 (sc failure actions로 자동 재시작 확인)
param([string]$HealthUrl)
$proc = Get-Process cloudflared -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $proc) {
    Write-Output "[S16] cloudflared not running — skip"
    exit 0
}
Stop-Process -Id $proc.Id -Force
Start-Sleep -Seconds 90
$svc = Get-Service cloudflared -ErrorAction SilentlyContinue
Write-Output "[S16] cloudflared service status: $($svc.Status)"
