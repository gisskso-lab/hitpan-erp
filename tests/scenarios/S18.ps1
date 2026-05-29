# S18 — SQL Injection (Dapper 파라미터 바인딩 정합 검증)
param([string]$HealthUrl)
$baseUrl = ([Uri]$HealthUrl).GetLeftPart([UriPartial]::Authority)
$payloads = @(
    "';DROP TABLE items;--",
    "' OR '1'='1",
    "1; UPDATE users SET role='admin'--",
    "%27%20OR%201%3D1--"
)
$results = @()
foreach ($p in $payloads) {
    try {
        $u = "$baseUrl/api/items?q=" + [Uri]::EscapeDataString($p)
        $r = Invoke-WebRequest -Uri $u -TimeoutSec 10 -UseBasicParsing -ErrorAction SilentlyContinue
        $results += @{ payload=$p; status=$r.StatusCode }
    } catch {
        $results += @{ payload=$p; status="exception" }
    }
}
Write-Output "[S18] SQLi attempts: $(($results | ConvertTo-Json -Compress))"
# items 테이블 살아있는지 검증 (정상 GET)
try {
    $check = Invoke-WebRequest -Uri "$baseUrl/api/items" -TimeoutSec 10 -UseBasicParsing -ErrorAction SilentlyContinue
    Write-Output "[S18] items endpoint alive: $($check.StatusCode)"
} catch { Write-Output "[S18] items endpoint check fail" }
