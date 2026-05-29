# =================================================================
# InstallCloudflared.ps1 — cloudflared 자동 프로비저닝
# 본사 API에 LicenseKey 제출 → tunnel_id + credentials_json 수령
# 환경변수 박제 → 워치독이 이용
# =================================================================
param(
    [Parameter(Mandatory=$true)]
    [string]$LicenseKey
)

$ErrorActionPreference = 'Stop'
$ProvisioningEndpoint = 'https://api.hitpan.kr/provisioning/tunnel'

function Log-Info($m) { Write-Output "[INFO] $m" }
function Log-Err($m)  { Write-Output "[ERR ] $m" }

try {
    # 1. 본사 프로비저닝 요청
    Log-Info "Requesting tunnel from $ProvisioningEndpoint"
    $body = @{ license_key = $LicenseKey } | ConvertTo-Json
    $response = Invoke-RestMethod -Uri $ProvisioningEndpoint -Method Post `
        -Body $body -ContentType 'application/json' -TimeoutSec 30

    $tunnelId    = $response.tunnel_id
    $tenantId    = $response.tenant_id
    $subdomain   = $response.subdomain
    $credJson    = $response.credentials_json | ConvertTo-Json -Depth 10 -Compress

    if (-not $tunnelId) { throw "tunnel_id missing from response" }

    # 2. cred 파일 박제
    $credDir = "$env:USERPROFILE\.cloudflared"
    New-Item -ItemType Directory -Path $credDir -Force | Out-Null
    $credPath = Join-Path $credDir "$tunnelId.json"
    $credJson | Out-File $credPath -Encoding utf8 -NoNewline

    # 3. config.yml 작성
    $configPath = Join-Path $credDir "config.yml"
    @"
tunnel: $tunnelId
credentials-file: $credPath
ingress:
  - hostname: $subdomain.hitpan.kr
    service: http://localhost:5234
  - hostname: api-$subdomain.hitpan.kr
    service: http://localhost:5257
  - service: http_status:404
"@ | Out-File $configPath -Encoding utf8

    # 4. cloudflared Service install
    $cloudflaredExe = "C:\Program Files\HitPan\payload\cloudflared.exe"
    if (-not (Test-Path $cloudflaredExe)) {
        $cloudflaredExe = "$env:ProgramFiles\HitPan\payload\cloudflared.exe"
    }
    if (-not (Test-Path $cloudflaredExe)) {
        throw "cloudflared.exe not found in payload"
    }

    # service 등록 (이미 있으면 skip)
    $existing = Get-Service -Name cloudflared -ErrorAction SilentlyContinue
    if ($null -eq $existing) {
        & $cloudflaredExe service install
        Log-Info "cloudflared service installed"
    } else {
        Log-Info "cloudflared service already exists"
    }

    Start-Service cloudflared -ErrorAction SilentlyContinue

    # 5. 환경변수 박제 (Machine scope, 워치독이 읽음)
    [Environment]::SetEnvironmentVariable('HITPAN_TUNNEL_ID',   $tunnelId,   'Machine')
    [Environment]::SetEnvironmentVariable('HITPAN_TENANT_ID',   $tenantId,   'Machine')
    [Environment]::SetEnvironmentVariable('HITPAN_LICENSE_KEY', $LicenseKey, 'Machine')
    [Environment]::SetEnvironmentVariable('HITPAN_SUBDOMAIN',   $subdomain,  'Machine')

    Log-Info "Cloudflared provisioning completed: $subdomain.hitpan.kr"
    exit 0
} catch {
    Log-Err "Provisioning failure: $($_.Exception.Message)"
    # 본사 실패 알림 (best-effort)
    try {
        Invoke-RestMethod -Uri 'https://api.hitpan.kr/install/failure' -Method Post `
            -Body (@{ reason = 'cloudflared_provisioning'; error = $_.Exception.Message } | ConvertTo-Json) `
            -ContentType 'application/json' -TimeoutSec 10 -ErrorAction SilentlyContinue
    } catch { }
    exit 1
}
