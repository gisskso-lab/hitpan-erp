# S19 — XSS (저장 시 HTML escape 확인)
param([string]$HealthUrl)
$webHost = ([Uri]$HealthUrl).Host
$apiHost = if ($webHost -like 'api-*') { $webHost } else { "api-$webHost" }
$baseUrl = "https://$apiHost"
$xss = '<script>alert("xss")</script>'
try {
    $r = Invoke-WebRequest -Uri "$baseUrl/api/items?q=" + [Uri]::EscapeDataString($xss) `
        -TimeoutSec 10 -UseBasicParsing -ErrorAction SilentlyContinue
    $escaped = -not ($r.Content -match '<script>alert')
    Write-Output "[S19] response HTML-escaped: $escaped"
} catch {
    Write-Output "[S19] exception: $($_.Exception.Message)"
}
