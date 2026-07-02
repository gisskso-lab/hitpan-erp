# 히트판 ERP — 정적 파일 웹 서버 + API 프록시 (포트 5234)
# WS-20260428-03 작3: /api/* 요청을 localhost:5257로 프록시 (외부 터널링 단일 도메인 라우팅)
$webRoot = Join-Path $PSScriptRoot "web\wwwroot"
$port = 5234

# 봉합 (2026-06-25, 배포 전수조사 P0-8): API 프록시 포트를 db.conf API_PORT 에서 읽는다.
#   종전엔 5257 하드코딩이라 멀티슬롯(슬롯2=5357 …)에서 Web 프록시가 엉뚱한 포트로 가 503.
#   db.conf 미발견·키 부재면 5257 폴백(슬롯1·LOCAL 안전). ERP 본체·워치독과 동일한 단일출처.
$apiPort = 5257
# 도메인 일원화 (작1 2차봉합 2026-07-02, 사장님 결재): canonical 리다이렉트 대상.
#   [진범] 클라이언트가 localhost/127.0.0.1/{id}.hitpan.kr 서로 다른 origin 으로 접속하면
#   기기 지문(localStorage, origin별 격리)이 갈려 같은 PC도 매번 새 기기로 잡힌다(실측 확정).
#   [봉합] 정식 도메인이 있으면, 엉뚱한 host 로 들어온 접속을 정식 도메인으로 1회 301 → origin 단일화.
#   ⚠️ 접속 차단·무한 리다이렉트 방지: PRIMARY_DOMAIN 이 있고 유효할 때만, 그리고 loopback(메인PC 본인)
#   ·LOCAL(도메인 없음)·이미 정식 도메인인 접속은 절대 리다이렉트하지 않는다(아래 겹겹 가드).
$primaryDomain = ""
$confPath = Join-Path $PSScriptRoot "db.conf"
if (Test-Path $confPath) {
    foreach ($line in Get-Content $confPath) {
        $t = $line.Trim()
        if ($t -like "API_PORT=*") {
            $v = $t.Substring(9).Trim()
            $parsed = 0
            if ([int]::TryParse($v, [ref]$parsed) -and $parsed -gt 0) { $apiPort = $parsed }
        }
        elseif ($t -like "PRIMARY_DOMAIN=*") {
            $primaryDomain = $t.Substring(15).Trim()
        }
    }
}
# LOCAL 모드 표식(localhost:5234) 또는 빈 값이면 리다이렉트 비활성 — 정식 도메인만 대상.
$canonicalEnabled = ($primaryDomain -ne "") -and ($primaryDomain -notlike "localhost*") -and ($primaryDomain -notlike "127.0.0.1*")
$apiBase = "http://localhost:$apiPort"

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

    # ─── 도메인 일원화 canonical 리다이렉트 (작1 2차봉합 2026-07-02) ────────────
    #   목적: 클라이언트가 정식 도메인이 아닌 host(예: IP 직접·다른 별칭)로 들어오면 정식
    #   도메인으로 1회 301 → origin 단일화(기기 지문 origin 격리 진범 봉합).
    #   ⚠️ 접속 차단·무한 리다이렉트 방지 겹겹 가드 (하나라도 걸리면 리다이렉트 안 함):
    #     ① canonicalEnabled(정식 도메인 존재·LOCAL 아님)일 때만
    #     ② 들어온 host 가 loopback(메인PC 본인 localhost/127.0.0.1)이면 스킵
    #     ③ 들어온 host 가 이미 정식 도메인이면 스킵(무한 루프 차단)
    #     ④ host 가 비어있거나 파싱 실패면 스킵(안전)
    if ($canonicalEnabled) {
        $reqHost = $ctx.Request.Url.Host
        $isLoopback = ($reqHost -eq "localhost") -or ($reqHost -eq "127.0.0.1") -or ($reqHost -eq "::1")
        $isAlreadyCanonical = ($reqHost -eq $primaryDomain)
        if (($reqHost -ne "") -and (-not $isLoopback) -and (-not $isAlreadyCanonical)) {
            $target = "https://$primaryDomain$($ctx.Request.Url.PathAndQuery)"
            $ctx.Response.StatusCode = 301
            $ctx.Response.RedirectLocation = $target
            $ctx.Response.Close()
            continue
        }
    }

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
