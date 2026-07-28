# =================================================================
# Smoke-ExternalEndpoints.ps1 — 외부 endpoint 통합 smoke 테스트
# Q+R 봉합 후 정합성 영구 검증. 5/27 사고(잘못된 URL 사용) 재발 방지.
#
# 검증 항목:
#   1. Web hostname (`demo.hitpan.kr`) — GET 200 (Blazor WASM)
#   2. Web /health — GET 200
#   3. API hostname (`api-demo.hitpan.kr`) — auth 없이 401
#   4. API /api/auth/login POST — wrong credential 시 401
#   5. cloudflared config.yml 2개 hostname 박제
# =================================================================
[CmdletBinding()]
param(
    [string]$WebHost = "demo.hitpan.kr",
    [string]$ApiHost = "api-demo.hitpan.kr"
)

$ErrorActionPreference = 'Continue'
$results = @()

function Test-Endpoint {
    param([string]$Name, [string]$Url, [int[]]$ExpectedCodes, [string]$Method = 'GET', $Body = $null)
    try {
        $args = @{
            Uri = $Url; Method = $Method; TimeoutSec = 10
            UseBasicParsing = $true; ErrorAction = 'Stop'
        }
        if ($Body) {
            $args.Body = ($Body | ConvertTo-Json)
            $args.ContentType = 'application/json'
        }
        $r = Invoke-WebRequest @args
        $code = $r.StatusCode
    } catch {
        $code = $_.Exception.Response.StatusCode.Value__
    }
    $pass = $ExpectedCodes -contains $code
    $color = if ($pass) { 'Green' } else { 'Red' }
    Write-Host ("[{0,-7}] {1,-35} HTTP {2}  (expected {3})" -f `
        $(if ($pass) { 'PASS' } else { 'FAIL' }), $Name, $code, ($ExpectedCodes -join '/')) -ForegroundColor $color
    return @{ Name = $Name; Code = $code; Pass = $pass }
}

Write-Host "`n=== 외부 endpoint smoke 테스트 ===" -ForegroundColor Cyan
$results += Test-Endpoint "Web home"        "https://$WebHost/"               200
$results += Test-Endpoint "Web /health"     "https://$WebHost/health"         @(200, 404)
$results += Test-Endpoint "API root"        "https://$ApiHost/"               @(200, 404)
$results += Test-Endpoint "API /health"     "https://$ApiHost/health"         @(200, 403)  # IP whitelist 적용 시 403
$results += Test-Endpoint "API /api/items"  "https://$ApiHost/api/items"      401          # auth required
$results += Test-Endpoint "API auth/login"  "https://$ApiHost/api/auth/login" 401  POST @{ email='wrong@t.kr'; password='wrong' }

Write-Host "`n=== cloudflared config.yml 박제 검증 ===" -ForegroundColor Cyan
$configPath = "$env:USERPROFILE\.cloudflared\config.yml"
if (Test-Path $configPath) {
    $config = Get-Content $configPath -Raw
    $hasWeb = $config -match "hostname:\s*$WebHost"
    $hasApi = $config -match "hostname:\s*$ApiHost"
    Write-Host "  WebHost ingress ($WebHost): $hasWeb" -ForegroundColor $(if ($hasWeb) { 'Green' } else { 'Red' })
    Write-Host "  ApiHost ingress ($ApiHost): $hasApi" -ForegroundColor $(if ($hasApi) { 'Green' } else { 'Red' })
    $results += @{ Name='config.yml WebHost'; Pass=$hasWeb }
    $results += @{ Name='config.yml ApiHost'; Pass=$hasApi }
} else {
    Write-Host "  config.yml not found at $configPath" -ForegroundColor Yellow
}

$passed = ($results | Where-Object { $_.Pass }).Count
$total  = $results.Count
Write-Host "`n📋 종합: $passed / $total PASS" -ForegroundColor Yellow

if ($passed -lt $total) { exit 1 } else { exit 0 }
