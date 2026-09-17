# 읽기 전용 MDB 조회 (20260917작1 [4] 검증 · 검증팀장)
# - SELECT 문만 허용 (INSERT/UPDATE/DELETE/DROP/ALTER/CREATE/INTO 포함 시 거절)
# - Mode=Read 로 연다 · 원본 MDB 대신 사본 경로를 넘길 것
# - 결과: -OutCsv 지정 시 UTF-8 CSV, 아니면 콘솔 표
param(
  [Parameter(Mandatory=$true)][string]$Mdb,
  [Parameter(Mandatory=$true)][string]$Sql,
  [string]$Password = '',
  [string]$OutCsv = ''
)
$ErrorActionPreference = 'Stop'
if ($Sql -notmatch '^\s*SELECT\s' -or $Sql -match '\b(INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|INTO)\b') {
  throw 'SELECT 전용 스크립트입니다.'
}
$cs = "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=$Mdb;Mode=Read;"
if ($Password) { $cs += "Jet OLEDB:Database Password=$Password;" }
$cn = New-Object System.Data.OleDb.OleDbConnection $cs
$cn.Open()
try {
  $cmd = $cn.CreateCommand(); $cmd.CommandText = $Sql; $cmd.CommandTimeout = 600
  $da = New-Object System.Data.OleDb.OleDbDataAdapter $cmd
  $dt = New-Object System.Data.DataTable
  [void]$da.Fill($dt)
  if ($OutCsv) { $dt | Export-Csv -Path $OutCsv -NoTypeInformation -Encoding UTF8; "rows=$($dt.Rows.Count) -> $OutCsv" }
  else { $dt | Format-Table -AutoSize | Out-String -Width 250 }
} finally { $cn.Close() }
