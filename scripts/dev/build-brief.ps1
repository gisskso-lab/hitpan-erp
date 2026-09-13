<#
  build-brief.ps1 - dotnet build with short output: errors/warnings (deduplicated) + one summary line.
  Keeps shell output small so it does not pile up in the AI session context.
  Manual: docs (constitution folder) AI token manual, section 6 P7.

  usage: powershell -NoProfile -File scripts/dev/build-brief.ps1 [-Project <csproj>] [-Configuration Debug] [-MaxLines 40]
#>
param(
  [string]$Project = "src/HitPan.API/HitPan.API.csproj",
  [string]$Configuration = "Debug",
  [int]$MaxLines = 40
)

$ErrorActionPreference = "Continue"
$env:DOTNET_CLI_UI_LANGUAGE = "en"
$log = Join-Path $env:TEMP ("build-brief-" + [guid]::NewGuid().ToString("N").Substring(0, 8) + ".log")

& dotnet build $Project -c $Configuration -nologo -v q "-clp:ErrorsOnly;WarningsOnly;NoSummary" *> $log
$code = $LASTEXITCODE

$all = @(Get-Content -LiteralPath $log)
$issues = @($all | Where-Object { $_ -match ': (error|warning) [A-Za-z]+[0-9]+' } | Sort-Object -Unique)
$errorCount = @($issues | Where-Object { $_ -match ': error ' }).Count
$warningCount = @($issues | Where-Object { $_ -match ': warning ' }).Count

"BUILD exit=$code errors=$errorCount warnings=$warningCount project=$Project"
$issues | Select-Object -First $MaxLines

if ($issues.Count -gt $MaxLines) {
  "... +$($issues.Count - $MaxLines) more (full log: $log)"
} elseif ($code -ne 0 -and $issues.Count -eq 0) {
  $all | Select-Object -Last $MaxLines
  "(full log: $log)"
} else {
  Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue
}
exit $code
