# Device fingerprint origin-isolation probe server (ASCII only - avoid PS 5.1 encoding issues)
# - Serves tests/device-fp-probe/index.html over http (file:// gives origin=null, unusable)
# - No DB / no API contact (constitution #39 safe - static HTML only)
# Usage (PowerShell):
#   powershell -ExecutionPolicy Bypass -File serve.ps1
# Then open in browser:
#   http://localhost:5599   -> fingerprint A
#   http://127.0.0.1:5599   -> fingerprint B  (address only differs, same server)
# Stop: Ctrl+C in this window

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$port = 5599

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:$port/")
$listener.Prefixes.Add("http://127.0.0.1:$port/")
$listener.Start()

Write-Host ""
Write-Host "  Fingerprint probe server STARTED" -ForegroundColor Green
Write-Host "  ---------------------------------------------"
Write-Host "  Open BOTH in the same browser:" -ForegroundColor Yellow
Write-Host "    http://localhost:5599    -> fingerprint A" -ForegroundColor Cyan
Write-Host "    http://127.0.0.1:5599    -> fingerprint B" -ForegroundColor Cyan
Write-Host "  ---------------------------------------------"
Write-Host "  Stop: Ctrl+C"
Write-Host ""

try {
    while ($listener.IsListening) {
        $ctx = $listener.GetContext()
        $path = $ctx.Request.Url.LocalPath.TrimStart('/')
        if ([string]::IsNullOrWhiteSpace($path)) { $path = 'index.html' }

        if ($path -like '*device-fingerprint.js') {
            $file = Join-Path $root '..\..\src\HitPan.Web\wwwroot\js\device-fingerprint.js'
        } else {
            $file = Join-Path $root $path
        }

        if (Test-Path $file) {
            $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $file))
            if ($file -like '*.js') { $ctx.Response.ContentType = 'application/javascript; charset=utf-8' }
            elseif ($file -like '*.html') { $ctx.Response.ContentType = 'text/html; charset=utf-8' }
            $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
            $host_ = $ctx.Request.Url.Authority
            Write-Host ("  200  " + $path + "  (" + $host_ + ")")
        } else {
            $ctx.Response.StatusCode = 404
            Write-Host ("  404  " + $path) -ForegroundColor Red
        }
        $ctx.Response.Close()
    }
} finally {
    $listener.Stop()
}
