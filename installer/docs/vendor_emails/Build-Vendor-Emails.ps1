# Build-Vendor-Emails.ps1 (v2 - ASCII safe for PowerShell 5.1)
# Builds 5 vendor email send-versions by replacing placeholders.
# Source files contain Korean placeholders; this script reads them as UTF-8.
# Author: PM Brownkim (2026-05-29, v2 2026-05-31)
# Constitution: #29 (PM no external send, owner approval required first)
# Usage: Run after owner provides company info (5 lines)

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

    [string]$VendorDir = "",
    [string]$OutputSuffix = "_send"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($VendorDir)) {
    if ($PSScriptRoot) {
        $VendorDir = $PSScriptRoot
    } else {
        $VendorDir = (Get-Location).Path
    }
}
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "=== Vendor Email Builder (5 AV vendors) ===" -ForegroundColor Cyan
Write-Host "Company    : $CompanyName"
Write-Host "Biz Reg No : $BusinessRegNo"
Write-Host "Address    : $HeadOfficeAddress"
Write-Host "Main Phone : $MainPhone"
Write-Host "Sec2 Phone : $Security2DirectPhone"
Write-Host "Reply Mail : $ReplyEmail"
Write-Host ""

# Korean placeholders defined via byte sequences to avoid PS 5.1 source encoding issues
# {{회사명}} {{사업자번호}} {{본사주소}} {{대표전화}} {{보안2직통}} {{회신메일}} {{일반메일}}
$enc = [System.Text.Encoding]::UTF8
$placeholders = @{
    ($enc.GetString([byte[]](0x7B,0x7B,0xED,0x9A,0x8C,0xEC,0x82,0xAC,0xEB,0xAA,0x85,0x7D,0x7D))) = $CompanyName
    ($enc.GetString([byte[]](0x7B,0x7B,0xEC,0x82,0xAC,0xEC,0x97,0x85,0xEC,0x9E,0x90,0xEB,0xB2,0x88,0xED,0x98,0xB8,0x7D,0x7D))) = $BusinessRegNo
    ($enc.GetString([byte[]](0x7B,0x7B,0xEB,0xB3,0xB8,0xEC,0x82,0xAC,0xEC,0xA3,0xBC,0xEC,0x86,0x8C,0x7D,0x7D))) = $HeadOfficeAddress
    ($enc.GetString([byte[]](0x7B,0x7B,0xEB,0x8C,0x80,0xED,0x91,0x9C,0xEC,0xA0,0x84,0xED,0x99,0x94,0x7D,0x7D))) = $MainPhone
    ($enc.GetString([byte[]](0x7B,0x7B,0xEB,0xB3,0xB4,0xEC,0x95,0x88,0x32,0xEC,0xA7,0x81,0xED,0x86,0xB5,0x7D,0x7D))) = $Security2DirectPhone
    ($enc.GetString([byte[]](0x7B,0x7B,0xED,0x9A,0x8C,0xEC,0x8B,0xA0,0xEB,0xA9,0x94,0xEC,0x9D,0xBC,0x7D,0x7D))) = $ReplyEmail
    ($enc.GetString([byte[]](0x7B,0x7B,0xEC,0x9D,0xBC,0xEB,0xB0,0x98,0xEB,0xA9,0x94,0xEC,0x9D,0xBC,0x7D,0x7D))) = $GeneralEmail
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
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        Write-Warning "Source missing: $sourcePath"
        continue
    }

    $content = Get-Content -LiteralPath $sourcePath -Raw -Encoding UTF8

    $unreplaced = @()
    foreach ($key in $placeholders.Keys) {
        if ($content -notmatch [regex]::Escape($key)) {
            $unreplaced += $key
        }
        $content = $content -replace [regex]::Escape($key), $placeholders[$key]
    }

    $outName = $file -replace "\.md$", "$OutputSuffix.md"
    $outPath = Join-Path $VendorDir $outName
    $content | Out-File -LiteralPath $outPath -Encoding utf8 -NoNewline

    $result += [pscustomobject]@{
        Source        = $file
        Output        = $outName
        BytesOriginal = (Get-Item -LiteralPath $sourcePath).Length
        BytesOutput   = (Get-Item -LiteralPath $outPath).Length
        Unreplaced    = ($unreplaced -join ", ")
    }

    Write-Host "[OK] $file -> $outName" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== Result ===" -ForegroundColor Cyan
$result | Format-Table -AutoSize

if ($result.Count -ne $sourceFiles.Count) {
    Write-Warning "Some source files missing. Build incomplete."
    exit 1
}

Write-Host ""
Write-Host "[OK] 5 vendor emails built successfully." -ForegroundColor Green
Write-Host "Next - send the emails:" -ForegroundColor Yellow
Write-Host "  1. 01_AhnLab_V3${OutputSuffix}.md -> v3sos@ahnlab.com"
Write-Host "  2. 02_ESTsecurity_AlYac${OutputSuffix}.md -> esrc@estsecurity.com"
Write-Host "  3. 03_Naver_Vaccine${OutputSuffix}.md -> antivirus_help@naver.com"
Write-Host "  4. 04_Norton_Symantec${OutputSuffix}.md -> submit.symantec.com/false_positive/"
Write-Host "  5. 05_McAfee${OutputSuffix}.md -> mcafee.com web form"

exit 0
