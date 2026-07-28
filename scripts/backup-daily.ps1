# 히트판 ERP — 일일 DB 백업 (Windows)
# 작업스케줄러 등록 권장 (매일 03:00)
$ErrorActionPreference = 'Stop'

$DbName    = if ($env:DB_NAME) { $env:DB_NAME } else { 'hitpan_erp' }
$DbUser    = if ($env:DB_USER) { $env:DB_USER } else { 'hitpan' }
$DbPass    = if ($env:DB_PASS) { $env:DB_PASS } else { 'Hitpan2025!' }
$BackupDir = if ($env:BACKUP_DIR) { $env:BACKUP_DIR } else { 'C:\hitpan\backups' }
$Retain    = if ($env:RETAIN_DAYS) { [int]$env:RETAIN_DAYS } else { 14 }

if (-not (Test-Path $BackupDir)) { New-Item -Path $BackupDir -ItemType Directory -Force | Out-Null }

$Stamp = Get-Date -Format 'yyyyMMdd_HHmm'
$Out   = Join-Path $BackupDir "hitpan_erp_$Stamp.sql"

Write-Host "[backup] START $DbName -> $Out"
& mysqldump -u $DbUser "-p$DbPass" `
  --single-transaction --quick --routines --triggers --events `
  --column-statistics=0 --set-gtid-purged=OFF `
  --default-character-set=utf8mb4 `
  $DbName | Out-File -FilePath $Out -Encoding utf8

Compress-Archive -Path $Out -DestinationPath "$Out.zip" -Force
Remove-Item $Out -Force

$Size = (Get-Item "$Out.zip").Length / 1MB
Write-Host "[backup] DONE size=$([math]::Round($Size,1))MB"

Write-Host "[backup] prune >$Retain days"
Get-ChildItem $BackupDir -Filter 'hitpan_erp_*.sql.zip' |
  Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-$Retain) } |
  Remove-Item -Force

Write-Host "[backup] END"
