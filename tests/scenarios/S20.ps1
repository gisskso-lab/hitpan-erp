# S20 — Brute force 로그인 (5회 실패 → 5분 lockout 박제)
param([string]$HealthUrl)
$baseUrl = ([Uri]$HealthUrl).GetLeftPart([UriPartial]::Authority)
$loginUrl = "$baseUrl/api/auth/login"
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
$locked = ($results | Where-Object { $_ -eq 429 -or $_ -eq 423 }).Count
Write-Output "[S20] codes: $($results -join ',') / locked (429/423): $locked (expected >= 1)"
