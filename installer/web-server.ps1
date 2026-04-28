# 히트판 ERP — 정적 파일 웹 서버 + API 프록시 (포트 5234)
# WS-20260428-03 작3: /api/* 요청을 localhost:5257로 프록시 (외부 터널링 단일 도메인 라우팅)
$webRoot = Join-Path $PSScriptRoot "web\wwwroot"
$port = 5234
$apiBase = "http://localhost:5257"

$mimeTypes = @{
    ".html" = "text/html; charset=utf-8"
    ".js"   = "application/javascript"
    ".css"  = "text/css"
    ".json" = "application/json"
    ".wasm" = "application/wasm"
    ".dll"  = "application/octet-stream"
    ".dat"  = "application/octet-stream"
    ".blat" = "application/octet-stream"
    ".png"  = "image/png"
    ".jpg"  = "image/jpeg"
    ".svg"  = "image/svg+xml"
    ".ico"  = "image/x-icon"
    ".woff" = "font/woff"
    ".woff2"= "font/woff2"
    ".ttf"  = "font/ttf"
}

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://+:$port/")
try { $listener.Start() } catch {
    $listener.Prefixes.Clear()
    $listener.Prefixes.Add("http://localhost:$port/")
    $listener.Start()
}

Write-Host "Web server running on http://localhost:$port (API proxy → $apiBase)"

while ($listener.IsListening) {
    $ctx = $listener.GetContext()
    $localPath = $ctx.Request.Url.LocalPath

    # ─── API 프록시 — /api/* → localhost:5257/api/* ─────────────
    if ($localPath -like "/api/*" -or $localPath -eq "/api") {
        try {
            $targetUrl = "$apiBase$($ctx.Request.Url.PathAndQuery)"
            $req = [System.Net.HttpWebRequest]::Create($targetUrl)
            $req.Method = $ctx.Request.HttpMethod
            $req.AllowAutoRedirect = $false

            # 요청 헤더 복사 (Host 제외)
            foreach ($h in $ctx.Request.Headers.AllKeys) {
                if ($h -ieq "Host" -or $h -ieq "Connection" -or $h -ieq "Content-Length") { continue }
                if ($h -ieq "Content-Type") { $req.ContentType = $ctx.Request.Headers[$h]; continue }
                if ($h -ieq "User-Agent") { $req.UserAgent = $ctx.Request.Headers[$h]; continue }
                if ($h -ieq "Accept") { $req.Accept = $ctx.Request.Headers[$h]; continue }
                try { $req.Headers.Add($h, $ctx.Request.Headers[$h]) } catch { }
            }

            # 요청 본문 복사
            if ($ctx.Request.HasEntityBody) {
                $reqStream = $req.GetRequestStream()
                $ctx.Request.InputStream.CopyTo($reqStream)
                $reqStream.Close()
            }

            try {
                $resp = $req.GetResponse()
            } catch [System.Net.WebException] {
                $resp = $_.Exception.Response
                if ($null -eq $resp) { throw }
            }

            $ctx.Response.StatusCode = [int]$resp.StatusCode
            $ctx.Response.ContentType = $resp.ContentType

            # 응답 헤더 복사 (Transfer-Encoding 제외)
            foreach ($h in $resp.Headers.AllKeys) {
                if ($h -ieq "Transfer-Encoding" -or $h -ieq "Content-Length" -or $h -ieq "Content-Type") { continue }
                try { $ctx.Response.Headers.Add($h, $resp.Headers[$h]) } catch { }
            }

            $respStream = $resp.GetResponseStream()
            $respStream.CopyTo($ctx.Response.OutputStream)
            $respStream.Close()
            $resp.Close()
        } catch {
            $ctx.Response.StatusCode = 502
            $errBytes = [System.Text.Encoding]::UTF8.GetBytes("API proxy error: $_")
            $ctx.Response.OutputStream.Write($errBytes, 0, $errBytes.Length)
        }
        $ctx.Response.Close()
        continue
    }

    # ─── 정적 파일 서빙 ──────────────────────────────────────
    if ($localPath -eq "/") { $localPath = "/index.html" }
    $filePath = Join-Path $webRoot $localPath.Replace("/", "\")

    if (Test-Path $filePath -PathType Leaf) {
        $ext = [System.IO.Path]::GetExtension($filePath).ToLower()
        $contentType = if ($mimeTypes.ContainsKey($ext)) { $mimeTypes[$ext] } else { "application/octet-stream" }
        $ctx.Response.ContentType = $contentType
        $bytes = [System.IO.File]::ReadAllBytes($filePath)
        $ctx.Response.ContentLength64 = $bytes.Length
        $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
    } else {
        # SPA fallback — index.html 반환
        $indexPath = Join-Path $webRoot "index.html"
        if (Test-Path $indexPath) {
            $ctx.Response.ContentType = "text/html; charset=utf-8"
            $bytes = [System.IO.File]::ReadAllBytes($indexPath)
            $ctx.Response.ContentLength64 = $bytes.Length
            $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        } else {
            $ctx.Response.StatusCode = 404
        }
    }
    $ctx.Response.Close()
}
