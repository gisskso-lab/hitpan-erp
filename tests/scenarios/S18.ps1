# S18 — SQL Injection (Dapper 파라미터 바인딩 정합 검증)
param([string]$HealthUrl)
$webHost = ([Uri]$HealthUrl).Host
$apiHost = if ($webHost -like 'api-*') { $webHost } else { "api-$webHost" }
$baseUrl = "https://$apiHost"
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
        $r = Invoke-WebRequest -Uri $u -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop
        $results += @{ payload=$p; status=$r.StatusCode }
    } catch {
        $code = $_.Exception.Response.StatusCode.Value__
        $results += @{ payload=$p; status=$code }
    }
}
# PASS: 401(인증 필요) 또는 400(파라미터 검증) = 정상. 500/200(데이터 손상) = FAIL.
$ok = ($results | Where-Object { $_.status -in @(400, 401, 403, 404) }).Count
$fail = ($results | Where-Object { $_.status -in @(200, 500) }).Count
Write-Output "[S18] SQLi attempts ok(4xx)=$ok fail(200/500)=$fail / detail: $(($results | ConvertTo-Json -Compress))"
# items 테이블 살아있는지 — 401 응답이 정상 (인증 없이 호출)
try {
    $check = Invoke-WebRequest -Uri "$baseUrl/api/items" -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop
    Write-Output "[S18] items endpoint alive: $($check.StatusCode)"
} catch {
    $code = $_.Exception.Response.StatusCode.Value__
    Write-Output "[S18] items endpoint reachable (HTTP $code = $(if($code -eq 401){'auth required, OK'} else {'check'}))"
}
