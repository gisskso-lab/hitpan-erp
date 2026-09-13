<#
  test-brief.ps1 - dotnet test with short output: summary lines + failed tests + build errors only.
  Manual: docs (constitution folder) AI token manual, section 6 P7.

  usage: powershell -NoProfile -File scripts/dev/test-brief.ps1 [-Project <csproj>] [-Filter <expr>] [-MaxLines 40]
  note : DB gate tests skip silently unless HITPAN_REQUIRE_DB is set. Read the Skipped count.
#>
param(
  [string]$Project = "src/HitPan.Tests/HitPan.Tests.csproj",
  [string]$Filter = "",
  [int]$MaxLines = 40
)

$ErrorActionPreference = "Continue"
$env:DOTNET_CLI_UI_LANGUAGE = "en"
$log = Join-Path $env:TEMP ("test-brief-" + [guid]::NewGuid().ToString("N").Substring(0, 8) + ".log")

$testArgs = @("test", $Project, "-nologo", "-v", "q", "--logger", "console;verbosity=minimal")
if ($Filter) { $testArgs += @("--filter", $Filter) }

& dotnet @testArgs *> $log
$code = $LASTEXITCODE

$all = @(Get-Content -LiteralPath $log)
$summary = @($all | Where-Object { $_ -match '(Passed|Failed)!\s+-\s+Failed:' })
$failures = @($all | Where-Object { $_ -match '^\s*Failed\s+\S' -or $_ -match ': error [A-Za-z]+[0-9]+' } | Sort-Object -Unique)

"TEST exit=$code project=$Project filter=$Filter"
if (-not $env:HITPAN_REQUIRE_DB) { "note: HITPAN_REQUIRE_DB is not set - DB gate tests may be skipped (check Skipped)" }
$summary | Select-Object -Last 5
$failures | Select-Object -First $MaxLines

if ($failures.Count -gt $MaxLines) {
  "... +$($failures.Count - $MaxLines) more (full log: $log)"
} elseif ($code -ne 0 -and $failures.Count -eq 0) {
  $all | Select-Object -Last $MaxLines
  "(full log: $log)"
} else {
  Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue
}
exit $code
