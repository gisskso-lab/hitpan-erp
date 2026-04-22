# 다운로드 토큰 E2E 테스트
# 1) 로그인 → access 토큰
# 2) POST /api/documents/{type}/{id}/download-token → 다운로드 토큰 발급 (2h, doc_id 바인딩)
# 3) GET /api/documents/{type}/{id}/excel?token=<download-token> → 실제 다운로드
# 4) 다른 문서 id에 토큰 재사용 시도 → 거부 확인
# 5) token_type=access 원본 토큰 사용 시도 → 거부 확인

$ErrorActionPreference = 'Stop'
$ApiBase = if ($env:API_BASE) { $env:API_BASE } else { 'http://localhost:5257' }
$Email = 'tenant@hitpan.kr'
$Password = 'Admin1234!'

Write-Host '=== Step 1: 로그인 ==='
$loginBody = @{ email = $Email; password = $Password } | ConvertTo-Json
$loginRes = Invoke-RestMethod -Uri "$ApiBase/api/auth/login" -Method Post -Body $loginBody -ContentType 'application/json'
$accessToken = $loginRes.accessToken
if (-not $accessToken) { throw 'Login failed' }
Write-Host "  ✓ accessToken issued (len=$($accessToken.Length))"

Write-Host '=== Step 2: 임의 매출 전표 ID 조회 ==='
$hdr = @{ Authorization = "Bearer $accessToken" }
$deliveries = Invoke-RestMethod -Uri "$ApiBase/api/sales/deliveries?from=2026-01-01&to=2026-12-31" -Method Get -Headers $hdr
$docId = $deliveries[0].deliveryId
Write-Host "  ✓ 대상 전표 id=$docId"

Write-Host '=== Step 3: 다운로드 토큰 발급 ==='
$tokenRes = Invoke-RestMethod -Uri "$ApiBase/api/documents/delivery/$docId/download-token" -Method Post -Headers $hdr
$downloadToken = $tokenRes.token
$expiresIn = $tokenRes.expires_in
Write-Host "  ✓ download token issued, expires_in=$expiresIn (기대: 7200)"
if ($expiresIn -ne 7200) { throw "expires_in should be 7200, got $expiresIn" }

Write-Host '=== Step 4: 정상 다운로드 (바인딩된 doc_id) ==='
$resp = Invoke-WebRequest -Uri "$ApiBase/api/documents/delivery/$docId/excel?token=$downloadToken" -Method Get
if ($resp.StatusCode -eq 200 -and $resp.Headers.'Content-Type' -match 'spreadsheet') {
    Write-Host "  ✓ 다운로드 성공 (sz=$($resp.Content.Length) bytes)"
} else {
    throw "정상 다운로드 실패: $($resp.StatusCode)"
}

Write-Host '=== Step 5: 다른 문서 id에 토큰 재사용 시도 (거부 확인) ==='
$otherId = $deliveries[1].deliveryId
try {
    $r2 = Invoke-WebRequest -Uri "$ApiBase/api/documents/delivery/$otherId/excel?token=$downloadToken" -Method Get -ErrorAction SilentlyContinue
    throw "WARN: 다른 doc_id에 토큰이 허용됨 (보안 실패)"
} catch {
    if ($_.Exception.Response.StatusCode.value__ -eq 401) {
        Write-Host '  ✓ 401 Unauthorized (정상 거부)'
    } else {
        throw "예상 외 오류: $_"
    }
}

Write-Host '=== Step 6: token_type=access 원본 토큰 사용 시도 (거부 확인) ==='
try {
    $r3 = Invoke-WebRequest -Uri "$ApiBase/api/documents/delivery/$docId/excel?token=$accessToken" -Method Get -ErrorAction SilentlyContinue
    throw "WARN: 원본 access 토큰이 다운로드에 허용됨 (보안 실패)"
} catch {
    if ($_.Exception.Response.StatusCode.value__ -eq 401) {
        Write-Host '  ✓ 401 Unauthorized (정상 거부 — token_type=download 강제)'
    } else {
        throw "예상 외 오류: $_"
    }
}

Write-Host ''
Write-Host '========================================'
Write-Host '✅ 다운로드 토큰 E2E 6/6 통과'
Write-Host '========================================'
