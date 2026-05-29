# Build-Vendor-Emails.ps1
# 백신 5종 메일 본문 회사 정보 일괄 치환 (5/30 09:00 발송 직전 실행)
# 작성: PM 브라운킴 (2026-05-29)
# 헌법: #29 (외부 발송 사장님 결재 후만 가도)
# 사용: 보안 매니저 2 PowerShell 관리자 권한

param(
    [Parameter(Mandatory = $true)]
    [string]$CompanyName,

    [Parameter(Mandatory = $true)]
    [string]$BusinessRegNo,

    [Parameter(Mandatory = $true)]
    [string]$HeadOfficeAddress,

    [Parameter(Mandatory = $true)]
    [string]$MainPhone,

    [Parameter(Mandatory = $true)]
    [string]$Security2DirectPhone,

    [string]$ReplyEmail = "security@hitpan.kr",
    [string]$GeneralEmail = "support@hitpan.kr",

    [string]$VendorDir = "$PSScriptRoot",
    [string]$OutputSuffix = "_발송본"
)

$ErrorActionPreference = "Stop"

Write-Host "=== 백신 5종 메일 발송본 박제 ===" -ForegroundColor Cyan
Write-Host "회사명     : $CompanyName"
Write-Host "사업자번호 : $BusinessRegNo"
Write-Host "본사주소   : $HeadOfficeAddress"
Write-Host "대표전화   : $MainPhone"
Write-Host "보안2 직통 : $Security2DirectPhone"
Write-Host "회신 메일  : $ReplyEmail"
Write-Host ""

$placeholders = @{
    "{{회사명}}"     = $CompanyName
    "{{사업자번호}}" = $BusinessRegNo
    "{{본사주소}}"   = $HeadOfficeAddress
    "{{대표전화}}"   = $MainPhone
    "{{보안2직통}}"  = $Security2DirectPhone
    "{{회신메일}}"   = $ReplyEmail
    "{{일반메일}}"   = $GeneralEmail
}

$sourceFiles = @(
    "01_AhnLab_V3.md",
    "02_ESTsecurity_AlYac.md",
    "03_Naver_Vaccine.md",
    "04_Norton_Symantec.md",
    "05_McAfee.md"
)

$result = @()

foreach ($file in $sourceFiles) {
    $sourcePath = Join-Path $VendorDir $file
    if (-not (Test-Path $sourcePath)) {
        Write-Warning "원본 부재: $sourcePath"
        continue
    }

    $content = Get-Content $sourcePath -Raw -Encoding UTF8

    $unreplaced = @()
    foreach ($key in $placeholders.Keys) {
        if ($content -notmatch [regex]::Escape($key)) {
            $unreplaced += $key
        }
        $content = $content -replace [regex]::Escape($key), $placeholders[$key]
    }

    $outName = $file -replace "\.md$", "$OutputSuffix.md"
    $outPath = Join-Path $VendorDir $outName
    $content | Set-Content $outPath -Encoding UTF8 -NoNewline

    $result += [pscustomobject]@{
        Source        = $file
        Output        = $outName
        BytesOriginal = (Get-Item $sourcePath).Length
        BytesOutput   = (Get-Item $outPath).Length
        Unreplaced    = ($unreplaced -join ", ")
    }

    Write-Host "[박제] $file → $outName" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== 박제 결과 ===" -ForegroundColor Cyan
$result | Format-Table -AutoSize

if ($result.Count -ne $sourceFiles.Count) {
    Write-Warning "원본 일부 부재. 박제 미완료."
    exit 1
}

Write-Host ""
Write-Host "[성공] 5종 발송본 박제 완료." -ForegroundColor Green
Write-Host "다음 단계 — 5/30 09:00 발송:" -ForegroundColor Yellow
Write-Host "  1. 01_AhnLab_V3_발송본.md → v3sos@ahnlab.com"
Write-Host "  2. 02_ESTsecurity_AlYac_발송본.md → esrc@estsecurity.com"
Write-Host "  3. 03_Naver_Vaccine_발송본.md → antivirus_help@naver.com"
Write-Host "  4. 04_Norton_Symantec_발송본.md → submit.symantec.com/false_positive/"
Write-Host "  5. 05_McAfee_발송본.md → mcafee.com 웹 폼"
Write-Host ""
Write-Host "Ticket ID / Submission ID 박제 → docs/handoff/20260530_작1_*.md §5.1" -ForegroundColor Cyan

exit 0
