param(
    [string]$InputDir = "docs\erp-handover",
    [string]$OutputDir = "docs\erp-handover\word"
)

$ErrorActionPreference = "Stop"
$projectRoot = (Get-Location).Path
$absInputDir = Join-Path $projectRoot $InputDir
$absOutputDir = Join-Path $projectRoot $OutputDir

if (-not (Test-Path $absOutputDir)) {
    New-Item -ItemType Directory -Force -Path $absOutputDir | Out-Null
}

$mdFiles = Get-ChildItem -Path $absInputDir -Filter "*.md" | Sort-Object Name
Write-Output ("MD files found: " + $mdFiles.Count)

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0

try {
    foreach ($md in $mdFiles) {
        $stem = $md.BaseName
        $htmlPath = Join-Path $env:TEMP ($stem + ".html")
        $docxPath = Join-Path $absOutputDir ($stem + ".docx")

        $mdText = Get-Content -Path $md.FullName -Raw -Encoding UTF8
        $html = $mdText

        $codeBlocks = New-Object System.Collections.ArrayList
        $html = [regex]::Replace($html, '(?ms)```[a-zA-Z]*\r?\n(.*?)\r?\n```', {
            param($m)
            $code = $m.Groups[1].Value -replace '&', '&amp;' -replace '<', '&lt;' -replace '>', '&gt;'
            $idx = $codeBlocks.Add("<pre style='background:#f4f4f4;padding:8px;font-family:Consolas;font-size:9pt;border:1px solid #ddd;'>$code</pre>")
            return "###CB_${idx}###"
        })

        $inlineCodes = New-Object System.Collections.ArrayList
        $html = [regex]::Replace($html, '`([^`]+)`', {
            param($m)
            $c = $m.Groups[1].Value -replace '&', '&amp;' -replace '<', '&lt;' -replace '>', '&gt;'
            $idx = $inlineCodes.Add("<code style='background:#eee;font-family:Consolas;font-size:9pt;padding:1px 4px;'>$c</code>")
            return "###IC_${idx}###"
        })

        $html = [regex]::Replace($html, '(?m)^# (.+)$', '<h1 style="border-bottom:2px solid #333;padding-bottom:5px;color:#1976D2;">$1</h1>')
        $html = [regex]::Replace($html, '(?m)^## (.+)$', '<h2 style="border-bottom:1px solid #999;padding-bottom:3px;color:#1976D2;">$1</h2>')
        $html = [regex]::Replace($html, '(?m)^### (.+)$', '<h3 style="color:#333;">$1</h3>')
        $html = [regex]::Replace($html, '(?m)^#### (.+)$', '<h4>$1</h4>')

        $html = [regex]::Replace($html, '(?m)^> (.+)$', '<blockquote style="border-left:4px solid #1976D2;padding-left:10px;color:#555;margin:10px 0;">$1</blockquote>')

        $html = [regex]::Replace($html, '\*\*([^*]+)\*\*', '<strong>$1</strong>')
        $html = [regex]::Replace($html, '(?<![a-zA-Z\*])\*([^*\n]+)\*(?![a-zA-Z\*])', '<em>$1</em>')

        $html = [regex]::Replace($html, '(?ms)(\|[^\r\n]+\|\r?\n)+', {
            param($m)
            $lines = $m.Value.Trim() -split "`r?`n"
            if ($lines.Count -lt 2) { return $m.Value }
            $headerCells = ($lines[0].Trim('|').Split('|') | ForEach-Object { "<th style='border:1px solid #aaa;padding:4px 8px;background:#e3f2fd;'>$($_.Trim())</th>" }) -join ""
            $bodyRows = @()
            for ($i = 2; $i -lt $lines.Count; $i++) {
                $cells = ($lines[$i].Trim('|').Split('|') | ForEach-Object { "<td style='border:1px solid #aaa;padding:4px 8px;'>$($_.Trim())</td>" }) -join ""
                $bodyRows += "<tr>$cells</tr>"
            }
            return "<table style='border-collapse:collapse;margin:10px 0;width:100%;'><thead><tr>$headerCells</tr></thead><tbody>$($bodyRows -join "`n")</tbody></table>"
        })

        $html = [regex]::Replace($html, '(?m)^- (.+)$', '<li>$1</li>')
        $html = [regex]::Replace($html, '(?ms)(<li>.*?</li>\s*)+', '<ul>$0</ul>')
        $html = [regex]::Replace($html, '(?m)^---+$', '<hr style="border:none;border-top:1px solid #ccc;">')

        $html = $html -replace "`r?`n`r?`n", '</p><p>'

        for ($i = 0; $i -lt $codeBlocks.Count; $i++) {
            $html = $html -replace "###CB_${i}###", $codeBlocks[$i]
        }
        for ($i = 0; $i -lt $inlineCodes.Count; $i++) {
            $html = $html -replace "###IC_${i}###", $inlineCodes[$i]
        }

        $fullHtml = "<!DOCTYPE html><html><head><meta charset='UTF-8'></head><body style=`"font-family:'Malgun Gothic',sans-serif;font-size:11pt;line-height:1.5;`"><p>$html</p></body></html>"

        $utf8 = New-Object System.Text.UTF8Encoding $true
        [System.IO.File]::WriteAllText($htmlPath, $fullHtml, $utf8)

        $doc = $word.Documents.Open($htmlPath, $false, $true)
        $doc.SaveAs2($docxPath, 16)
        $doc.Close($false)
        Remove-Item $htmlPath -Force -ErrorAction SilentlyContinue
        Write-Output ("OK: " + $stem + ".docx")
    }
} finally {
    $word.Quit()
    [System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null
    [GC]::Collect()
}

Write-Output ("Done: " + $absOutputDir)
