# S15 — 사용자 EXE 삭제 → WS-28-D 자동 재설치
param([string]$HealthUrl)
$exe = "C:\Program Files\HitPan\payload\cloudflared.exe"
$backup = "$exe.bak_$(Get-Date -Format yyyyMMddHHmmss)"
if (-not (Test-Path $exe)) {
    Write-Output "[S15] cloudflared.exe not found — skip"
    exit 0
}
Stop-Service cloudflared -Force -ErrorAction SilentlyContinue
Move-Item $exe $backup -Force
Start-Sleep -Seconds 180  # 워치독 WS-28-D 트리거 (cool down 허용)
# 자동 재설치 안 되면 봉합 (테스트 환경 복구)
if (-not (Test-Path $exe)) { Move-Item $backup $exe -Force }
$restored = Test-Path $exe
Write-Output "[S15] cloudflared.exe restored: $restored"
