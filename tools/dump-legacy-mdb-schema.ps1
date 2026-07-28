# 레거시 히트판 MDB 스키마 덤프 (사장님 결재 2026-04-29)
# 사장님 본인 ERP 백업의 빈 데이터 MDB로 매핑 작업용 스키마 추출.

$ErrorActionPreference = 'Stop'
$folder = 'C:\Users\소순근\Desktop\새 폴더'
$outFile = 'C:\Users\소순근\Desktop\hitpan-erp\docs\mdb-schema-dump.md'

if (-not (Test-Path -LiteralPath $folder)) {
    Write-Error "폴더 없음: $folder"
    exit 1
}

$files = Get-ChildItem -LiteralPath $folder -Force | Where-Object { $_.Extension -ieq '.mdb' } | Sort-Object Name
"발견 MDB: $($files.Count)"
foreach ($f in $files) { "  $($f.Name) ($([Math]::Round($f.Length/1024,1)) KB)" }

$sb = New-Object Text.StringBuilder
[void]$sb.AppendLine("# 레거시 히트판 MDB 스키마 덤프 (2026-04-29)")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("출처: ``C:\Users\소순근\Desktop\새 폴더\``")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("이 문서는 매핑 설계의 단일 진실 소스(Single Source of Truth)이다.")
[void]$sb.AppendLine("새 히트판 ERP의 어떤 테이블·컬럼으로 이관할지 결정하는 근거.")
[void]$sb.AppendLine("")

foreach ($f in $files) {
    $cs = "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=$($f.FullName);Jet OLEDB:Database Password=;"
    $conn = New-Object System.Data.OleDb.OleDbConnection($cs)
    try {
        $conn.Open()
    } catch {
        [void]$sb.AppendLine("## $($f.Name) — OPEN 실패")
        [void]$sb.AppendLine("``$($_.Exception.Message)``")
        [void]$sb.AppendLine("")
        continue
    }

    $tablesAll = $conn.GetSchema('Tables')
    $tables = $tablesAll | Where-Object { $_.TABLE_TYPE -eq 'TABLE' } | Sort-Object TABLE_NAME
    [void]$sb.AppendLine("---")
    [void]$sb.AppendLine("## $($f.Name)")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("**테이블 수: $($tables.Count)**")
    [void]$sb.AppendLine("")

    # 테이블 인덱스
    [void]$sb.AppendLine("### 테이블 인덱스")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| # | 테이블명 | 행 수 |")
    [void]$sb.AppendLine("|---|---|---:|")
    $idx = 0
    $rowCounts = @{}
    foreach ($t in $tables) {
        $idx++
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = "SELECT COUNT(*) FROM [$($t.TABLE_NAME)]"
        try { $rc = [int]$cmd.ExecuteScalar() } catch { $rc = -1 }
        $rowCounts[$t.TABLE_NAME] = $rc
        $rcText = if ($rc -ge 0) { "{0:N0}" -f $rc } else { "?" }
        [void]$sb.AppendLine("| $idx | $($t.TABLE_NAME) | $rcText |")
    }
    [void]$sb.AppendLine("")

    # 각 테이블 컬럼
    foreach ($t in $tables) {
        $tname = $t.TABLE_NAME
        $cols = $conn.GetSchema('Columns') | Where-Object { $_.TABLE_NAME -eq $tname } | Sort-Object ORDINAL_POSITION
        $rc = $rowCounts[$tname]
        $rcText = if ($rc -ge 0) { "{0:N0}" -f $rc } else { "?" }
        [void]$sb.AppendLine("### $tname  ($rcText rows, $($cols.Count) cols)")
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("| 순 | 컬럼 | 타입 | 길이 | NULL |")
        [void]$sb.AppendLine("|---:|---|---|---:|:---:|")
        foreach ($c in $cols) {
            $type = switch ([int]$c.DATA_TYPE) {
                2 {'SmallInt'}
                3 {'Long'}
                4 {'Single'}
                5 {'Double'}
                6 {'Currency'}
                7 {'Date'}
                11 {'Boolean'}
                17 {'Byte'}
                72 {'GUID'}
                128 {'OLE'}
                130 {'Text(Wide)'}
                131 {'Decimal'}
                202 {'Text'}
                203 {'Memo'}
                default { "T$($c.DATA_TYPE)" }
            }
            $len = if ($c.CHARACTER_MAXIMUM_LENGTH) { $c.CHARACTER_MAXIMUM_LENGTH } else { '' }
            $nullable = if ($c.IS_NULLABLE) { 'Y' } else { 'N' }
            [void]$sb.AppendLine("| $($c.ORDINAL_POSITION) | $($c.COLUMN_NAME) | $type | $len | $nullable |")
        }
        [void]$sb.AppendLine("")
    }
    $conn.Close()
}

# UTF-8 (BOM 없이) 로 저장
[IO.File]::WriteAllText($outFile, $sb.ToString(), (New-Object Text.UTF8Encoding($false)))
"==> $outFile"
"size: $([Math]::Round((Get-Item $outFile).Length/1024,1)) KB"
