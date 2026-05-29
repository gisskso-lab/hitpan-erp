# S06 — DNS 사고 (hosts 변조로 demo.hitpan.kr → 0.0.0.0)
param([string]$HealthUrl)
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
