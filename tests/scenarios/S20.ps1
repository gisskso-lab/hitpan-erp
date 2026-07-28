# S20 — Brute force 로그인 (5회 실패 → 5분 lockout 박제)
param([string]$HealthUrl)
$webHost = ([Uri]$HealthUrl).Host
$apiHost = if ($webHost -like 'api-*') { $webHost } else { "api-$webHost" }
$loginUrl = "https://$apiHost/api/auth/login"
$attempts = 10
$results = @()
1..$attempts | ForEach-Object {
    $body = @{ email="bruteforce-test@hitpan.kr"; password="wrong_$_" } | ConvertTo-Json
    try {
        $r = Invoke-WebRequest -Uri $loginUrl -Method Post -Body $body `
            -ContentType 'application/json' -TimeoutSec 10 -UseBasicParsing -ErrorAction SilentlyContinue
        $results += $r.StatusCode
    } catch {
        $code = $_.Exception.Response.StatusCode.Value__
        $results += $code
    }
}
# PASS: 모든 시도가 401/429로 reject되면 OK (200 = 자격증명 우회 = FAIL)
$rejected = ($results | Where-Object { $_ -eq 401 -or $_ -eq 429 -or $_ -eq 423 }).Count
$bypassed = ($results | Where-Object { $_ -eq 200 }).Count
Write-Output "[S20] codes: $($results -join ',') / rejected: $rejected / bypassed: $bypassed (expected bypassed=0)"
