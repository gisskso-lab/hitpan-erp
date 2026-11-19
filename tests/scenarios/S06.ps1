# S06 — DNS 사고 (hosts 변조로 demo.hitpan.kr → 0.0.0.0)
param([string]$HealthUrl)
# --- W-G1-53 (2026-11-19): old tool disabled. No bypass switch. Body below is kept as record (design s6). ---
Write-Host '[blocked] This old test tool is disabled (W-G1-53). It has no guard and can change a real PC.'
Write-Host '[blocked] Use tests/scenarios/comms-integrity/Run-RoutinePcScenarios.ps1 (guarded; without -Confirm it only judges).'
exit 1
# --- end of W-G1-53 block ---
$hostsPath = "C:\Windows\System32\drivers\etc\hosts"
$backup    = "$hostsPath.bak_$(Get-Date -Format yyyyMMddHHmmss)"
$host = ([Uri]$HealthUrl).Host

Copy-Item $hostsPath $backup -Force
Add-Content $hostsPath "`n0.0.0.0 $host"
Start-Sleep -Seconds 180   # 워치독 3회 실패 → 본사 알림 박제 트리거

# 봉합 (관리자 매뉴얼 시뮬): 원복
(Get-Content $hostsPath) | Where-Object { $_ -notmatch [regex]::Escape("0.0.0.0 $host") } | Set-Content $hostsPath
ipconfig /flushdns | Out-Null
Write-Output "[S06] hosts reverted"
