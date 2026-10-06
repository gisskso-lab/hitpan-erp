<#
  test-brief.ps1 - dotnet test with short output: summary lines + failed tests + build errors only.
  Manual: docs (constitution folder) AI token manual, section 6 P7.

  usage: powershell -NoProfile -File scripts/dev/test-brief.ps1 [-Project <csproj>] [-Filter <expr>] [-MaxLines 40]
  note : DB gate tests skip silently unless HITPAN_REQUIRE_DB is set. Read the Skipped count.
  note : work10 backoffice gates (BackofficeRoleScopeGate / BackofficeRoleDowngradeGate) FAIL when no DB is
         reachable and no declaration is given - that is by design (20261007 work10 fix 4: no silent green).
         Local choices: (a) measure for real -> HITPAN_BO_GATE_DB=<test db name> + HITPAN_DB_HOST/USER/PASS
                        (b) skip on purpose -> HITPAN_GATE_SKIP_OK=1 ; that green is NOT evidence.
         [SKIP] lines go to stderr and are hidden at default verbosity. Do not judge by elapsed time:
         in CI the job WITHOUT a DB is SLOWER (connect timeout), so time inversion is real. Evidence = db-gate job.
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
if (-not $env:HITPAN_GATE_SKIP_OK -and -not $env:HITPAN_BO_GATE_DB) { "note: neither HITPAN_BO_GATE_DB nor HITPAN_GATE_SKIP_OK is set - work10 backoffice gates will FAIL (by design). Give one of them." }
if ($env:HITPAN_GATE_SKIP_OK) { "WARNING: HITPAN_GATE_SKIP_OK is set - work10 backoffice gates are being SKIPPED. This green is NOT evidence. (merge-gate M-2)" }
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
