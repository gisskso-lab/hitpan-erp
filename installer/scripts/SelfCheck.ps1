# =================================================================
# SelfCheck.ps1 — 설치 직후 통신 무결성 자가 점검
# 5분 안 https://{subdomain}.hitpan.kr/health 200 → PASS
# 실패 → 본사 자동 알림 + EventLog 박제
# =================================================================
$ErrorActionPreference = 'Continue'

$subdomain = [Environment]::GetEnvironmentVariable('HITPAN_SUBDOMAIN', 'Machine')
if ([string]::IsNullOrEmpty($subdomain)) {
    Write-Output "[ERR] HITPAN_SUBDOMAIN not set"
    exit 1
}

$healthUrl = "https://$subdomain.hitpan.kr/health"
$maxAttempts = 30   # 30 × 10s = 5분
$attempt = 0
$pass = $false

while ($attempt -lt $maxAttempts) {
    $attempt++
    Start-Sleep -Seconds 10
    try {
        $r = Invoke-WebRequest -Uri $healthUrl -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop
        if ($r.StatusCode -eq 200) {
            $pass = $true
            break
        }
    } catch {
        # silent retry
    }
}

if ($pass) {
    Write-EventLog -LogName Application -Source HitPanSetup `
        -EntryType Information -EventId 31010 `
        -Message "Self-check PASS after $attempt attempts ($healthUrl)" -ErrorAction SilentlyContinue
    Write-Output "[OK] Self-check PASS after $attempt attempts"
    exit 0
} else {
    Write-EventLog -LogName Application -Source HitPanSetup `
        -EntryType Error -EventId 31011 `
        -Message "Self-check FAILED after 5min ($healthUrl)" -ErrorAction SilentlyContinue
    try {
        Invoke-RestMethod -Uri 'https://api.hitpan.kr/install/failure' -Method Post `
            -Body (@{ reason = 'self_check_timeout'; url = $healthUrl } | ConvertTo-Json) `
            -ContentType 'application/json' -TimeoutSec 10 -ErrorAction SilentlyContinue
    } catch { }
    Write-Output "[FAIL] Self-check timeout — HQ notified"
    exit 1
}
