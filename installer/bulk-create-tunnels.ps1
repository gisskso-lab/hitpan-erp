# 베타 고객용 Cloudflare Tunnel 일괄 발급 스크립트
# 사장님 또는 마커스 리(인프라 매니저) 본사 PC에서 1회 실행
# 결과: tenant-002 ~ tenant-010 토큰 9개 발급 + tunnels.csv 출력
#
# 사전 준비:
#   1. Cloudflare 계정 + Zero Trust 활성화 (사장님 가이드 STEP 1~2)
#   2. Cloudflare API Token 발급 (https://dash.cloudflare.com/profile/api-tokens)
#      - "Create Token" → "Custom token"
#      - Permissions: Account / Cloudflare Tunnel / Edit
#      - 토큰을 환경변수 CF_API_TOKEN 에 박을 것 (Git 절대 X)
#   3. Account ID 확인 (Cloudflare 대시보드 우측 사이드바)
#      - 환경변수 CF_ACCOUNT_ID 에 박음

param(
    [int]$StartIndex = 2,
    [int]$EndIndex = 10,
    [string]$Prefix = "tenant",
    [string]$OutputCsv = "tunnels.csv"
)

$ErrorActionPreference = "Stop"

# ─── 사전 검증 ────────────────────────────────────
if (-not $env:CF_API_TOKEN) {
    Write-Error "환경변수 CF_API_TOKEN 가 설정되지 않았습니다. Cloudflare API Token을 박으세요."
    exit 1
}
if (-not $env:CF_ACCOUNT_ID) {
    Write-Error "환경변수 CF_ACCOUNT_ID 가 설정되지 않았습니다. Cloudflare Account ID를 박으세요."
    exit 1
}

$apiToken = $env:CF_API_TOKEN
$accountId = $env:CF_ACCOUNT_ID
$apiBase = "https://api.cloudflare.com/client/v4/accounts/$accountId/cfd_tunnel"

$headers = @{
    "Authorization" = "Bearer $apiToken"
    "Content-Type" = "application/json"
}

# ─── 일괄 발급 ────────────────────────────────────
$results = @()

Write-Host ""
Write-Host "============================================"
Write-Host "  Cloudflare Tunnel 일괄 발급 ($StartIndex ~ $EndIndex)"
Write-Host "============================================"
Write-Host ""

for ($i = $StartIndex; $i -le $EndIndex; $i++) {
    $tunnelName = "$Prefix-{0:D3}" -f $i
    Write-Host "[$i/$EndIndex] $tunnelName 생성 중..." -NoNewline

    try {
        # tunnel_secret = 32 byte 랜덤 → base64
        $secretBytes = New-Object byte[] 32
        [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($secretBytes)
        $tunnelSecret = [Convert]::ToBase64String($secretBytes)

        $body = @{
            name = $tunnelName
            tunnel_secret = $tunnelSecret
            config_src = "cloudflare"
        } | ConvertTo-Json

        $response = Invoke-RestMethod -Uri $apiBase -Method Post -Headers $headers -Body $body

        if ($response.success) {
            $tunnelId = $response.result.id

            # 토큰 발급 (별도 엔드포인트)
            $tokenResp = Invoke-RestMethod -Uri "$apiBase/$tunnelId/token" -Method Get -Headers $headers
            $tunnelToken = $tokenResp.result

            $results += [PSCustomObject]@{
                Index = $i
                TunnelName = $tunnelName
                TunnelId = $tunnelId
                Token = $tunnelToken
                CreatedAt = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
            }

            Write-Host " OK" -ForegroundColor Green
        } else {
            Write-Host " 실패: $($response.errors | ConvertTo-Json -Compress)" -ForegroundColor Red
        }
    } catch {
        Write-Host " 에러: $_" -ForegroundColor Red
    }

    Start-Sleep -Milliseconds 500  # API rate limit 회피
}

# ─── CSV 출력 (안전) ──────────────────────────────
$results | Export-Csv -Path $OutputCsv -NoTypeInformation -Encoding UTF8

# 파일 권한 — Administrators만
$acl = Get-Acl $OutputCsv
$acl.SetAccessRuleProtection($true, $false)
$adminRule = New-Object System.Security.AccessControl.FileSystemAccessRule("Administrators","FullControl","Allow")
$systemRule = New-Object System.Security.AccessControl.FileSystemAccessRule("SYSTEM","FullControl","Allow")
$acl.AddAccessRule($adminRule)
$acl.AddAccessRule($systemRule)
Set-Acl -Path $OutputCsv -AclObject $acl

Write-Host ""
Write-Host "============================================"
Write-Host "  완료: $($results.Count)개 터널 발급" -ForegroundColor Green
Write-Host "  저장: $OutputCsv"
Write-Host "  권한: Administrators / SYSTEM only"
Write-Host ""
Write-Host "  ⚠ 이 파일을 Git에 절대 커밋하지 마세요!"
Write-Host "  ⚠ 토큰은 LastPass / Bitwarden에 옮겨 보관 권장."
Write-Host "============================================"
Write-Host ""
