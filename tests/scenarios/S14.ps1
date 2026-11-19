# S14 — 사용자 콘솔 종료 시도 (cloudflared Service라 콘솔 종료 불가 확인)
param([string]$HealthUrl)
# --- W-G1-53 (2026-11-19): old tool disabled. No bypass switch. Body below is kept as record (design s6). ---
Write-Host '[blocked] This old test tool is disabled (W-G1-53). It has no guard and can change a real PC.'
Write-Host '[blocked] Use tests/scenarios/comms-integrity/Run-RoutinePcScenarios.ps1 (guarded; without -Confirm it only judges).'
exit 1
# --- end of W-G1-53 block ---
$proc = Get-Process cloudflared -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $proc) {
    Write-Output "[S14] cloudflared not running — skip"
    exit 0
}
# CTRL_C는 Service에 안 먹힘 — Stop-Process 시뮬
try {
    Stop-Process -Id $proc.Id -Force -ErrorAction Stop
    Start-Sleep -Seconds 90  # sc failure restart 5s × 3 + 워치독 1분 주기
    $alive = Get-Process cloudflared -ErrorAction SilentlyContinue
    Write-Output "[S14] cloudflared revived: $([bool]$alive) (expected True)"
} catch {
    Write-Output "[S14] Stop-Process blocked (Service protected): $($_.Exception.Message)"
}
