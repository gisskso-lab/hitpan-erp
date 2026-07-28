# S07 — Windows Defender 격리 시뮬 (EICAR 표준 테스트 파일)
param([string]$HealthUrl)
$dir = "C:\Program Files\HitPan\test"
New-Item -ItemType Directory -Path $dir -Force | Out-Null
$path = Join-Path $dir "eicar.com"
# EICAR 표준 테스트 문자열 (백신 트리거)
$eicar = 'X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*'
$eicar | Out-File $path -Encoding ascii -NoNewline
Start-Sleep -Seconds 30  # Defender 스캔 대기
# 격리 확인 + 워치독 알림 박제 트리거
$exists = Test-Path $path
Write-Output "[S07] eicar file still exists: $exists (expected False = Defender quarantined)"
if (Test-Path $path) { Remove-Item $path -Force -ErrorAction SilentlyContinue }
