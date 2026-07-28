# =================================================================
# Run-All-Scenarios.ps1 — 20개 강제 시나리오 일괄 실행
# 헌법 #27. 5분 이내 자동 복구 = PASS. 20/20 PASS만 베타 발진.
# 사장님 PC 또는 검증 PC에서 관리자 권한으로 실행.
# =================================================================
[CmdletBinding()]
param(
    [string]$HealthUrl = "https://demo.hitpan.kr/health",
    [string]$ReportDir = "$PSScriptRoot\reports",
    [switch]$SkipReboot,           # S01 재부팅 시나리오 생략
    [switch]$SkipDestructive,      # S02·S03·S04 강제 종료 생략 (운영 PC 보호)
    [string[]]$Only                # 특정 시나리오만 (예: "S07","S10")
)

$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Path $ReportDir -Force | Out-Null
$ReportPath = Join-Path $ReportDir "scenario-report-$(Get-Date -Format yyyyMMdd_HHmmss).json"

$Scenarios = @(
    @{ Id="S01"; Area="인프라"; Name="Windows Update 강제 재부팅"; Destructive=$true;  Reboot=$true  }
    @{ Id="S02"; Area="인프라"; Name="정전 후 복전 (수동)";          Destructive=$true;  Reboot=$false }
    @{ Id="S03"; Area="인프라"; Name="UPS 없는 정전 (수동)";          Destructive=$true;  Reboot=$false }
    @{ Id="S04"; Area="네트워크"; Name="KT 회선 다운";                 Destructive=$true;  Reboot=$false }
    @{ Id="S05"; Area="네트워크"; Name="공유기 재부팅 (수동)";         Destructive=$true;  Reboot=$false }
    @{ Id="S06"; Area="네트워크"; Name="DNS 사고";                     Destructive=$true;  Reboot=$false }
    @{ Id="S07"; Area="보안SW"; Name="Defender 격리 (EICAR)";          Destructive=$false; Reboot=$false }
    @{ Id="S08"; Area="보안SW"; Name="V3 격리 (본사 PC 실측)";          Destructive=$false; Reboot=$false }
    @{ Id="S09"; Area="보안SW"; Name="알약 격리 (본사 PC 실측)";        Destructive=$false; Reboot=$false }
    @{ Id="S10"; Area="자격증명"; Name="TunnelSecret 회전";              Destructive=$true;  Reboot=$false }
    @{ Id="S11"; Area="자격증명"; Name="cert.pem 손상 (수동)";           Destructive=$true;  Reboot=$false }
    @{ Id="S12"; Area="물리"; Name="SSD 비트 플립 (DR 백업, 수동)";       Destructive=$false; Reboot=$false }
    @{ Id="S13"; Area="물리"; Name="RAM 오류 (자동 재부팅, 수동)";        Destructive=$false; Reboot=$false }
    @{ Id="S14"; Area="인적"; Name="사용자 Ctrl+C cloudflared 종료";     Destructive=$false; Reboot=$false }
    @{ Id="S15"; Area="인적"; Name="사용자 EXE 삭제 → 자동 재설치";       Destructive=$true;  Reboot=$false }
    @{ Id="S16"; Area="응용"; Name="cloudflared 비정상 종료";             Destructive=$true;  Reboot=$false }
    @{ Id="S17"; Area="외부공격"; Name="DDoS (Cloudflare 의존)";          Destructive=$false; Reboot=$false }
    @{ Id="S18"; Area="외부공격"; Name="SQL Injection";                   Destructive=$false; Reboot=$false }
    @{ Id="S19"; Area="외부공격"; Name="XSS";                              Destructive=$false; Reboot=$false }
    @{ Id="S20"; Area="외부공격"; Name="Brute force 로그인";                Destructive=$false; Reboot=$false }
)

function Test-Health {
    param([int]$TimeoutSec = 5)
    try {
        $r = Invoke-WebRequest -Uri $HealthUrl -TimeoutSec $TimeoutSec -UseBasicParsing -ErrorAction Stop
        return $r.StatusCode -eq 200
    } catch { return $false }
}

function Wait-Health {
    param([int]$MaxMinutes = 5)
    $deadline = (Get-Date).AddMinutes($MaxMinutes)
    while ((Get-Date) -lt $deadline) {
        if (Test-Health) { return $true }
        Start-Sleep -Seconds 10
    }
    return $false
}

function Invoke-Scenario {
    param($Scenario)
    $sid = $Scenario.Id
    $impl = Join-Path $PSScriptRoot "$sid.ps1"
    if (-not (Test-Path $impl)) {
        return @{ Id=$sid; Pass=$false; Reason="impl not found ($impl)"; ElapsedSec=0 }
    }
    $start = Get-Date
    try {
        & $impl -HealthUrl $HealthUrl | Out-Null
        $pass = Wait-Health -MaxMinutes 5
    } catch {
        $pass = $false
    }
    $elapsed = [int]((Get-Date) - $start).TotalSeconds
    return @{ Id=$sid; Pass=$pass; Reason=if ($pass) { "OK" } else { "no recovery in 5min" }; ElapsedSec=$elapsed }
}

$targetSet = if ($Only) { $Only } else { $Scenarios.Id }
$results = @()
$skipped = @()

foreach ($s in $Scenarios) {
    if ($s.Id -notin $targetSet) { continue }
    if ($SkipReboot -and $s.Reboot) { $skipped += $s.Id; continue }
    if ($SkipDestructive -and $s.Destructive) { $skipped += $s.Id; continue }

    Write-Host "▶ $($s.Id) — $($s.Name) [$($s.Area)]" -ForegroundColor Cyan
    $r = Invoke-Scenario -Scenario $s
    $results += $r
    $color = if ($r.Pass) { "Green" } else { "Red" }
    Write-Host "  → $(if ($r.Pass) { 'PASS' } else { 'FAIL' }) ($($r.ElapsedSec)s) - $($r.Reason)" -ForegroundColor $color
}

$summary = @{
    timestamp = (Get-Date).ToUniversalTime().ToString("o")
    health_url = $HealthUrl
    total = $results.Count
    passed = ($results | Where-Object { $_.Pass }).Count
    failed = ($results | Where-Object { -not $_.Pass }).Count
    skipped = $skipped
    results = $results
}

$summary | ConvertTo-Json -Depth 5 | Out-File $ReportPath -Encoding utf8
Write-Host "`n📋 Report: $ReportPath" -ForegroundColor Yellow
Write-Host "✅ PASS: $($summary.passed) / $($summary.total) (FAIL: $($summary.failed), SKIP: $($skipped.Count))" -ForegroundColor Yellow

if ($summary.failed -gt 0) { exit 1 } else { exit 0 }
