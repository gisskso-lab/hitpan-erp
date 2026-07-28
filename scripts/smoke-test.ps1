# 히트판 배포 후 스모크 테스트 (2026-05-06)
# 사용법: .\scripts\smoke-test.ps1
# 모든 체크 통과해야 배포 완료로 인정. 하나라도 실패하면 즉시 중단.

param(
    [string]$BaseUrl = "https://demo.hitpan.kr",
    [string]$ApiUrl  = "https://api-demo.hitpan.kr"
)

$ErrorCount = 0

function Check {
    param([string]$Name, [scriptblock]$Test)
    Write-Host -NoNewline "  [$Name] ... "
    try {
        $result = & $Test
        if ($result) {
            Write-Host "OK" -ForegroundColor Green
        } else {
            Write-Host "FAIL" -ForegroundColor Red
            $script:ErrorCount++
        }
    } catch {
        Write-Host "FAIL ($_)" -ForegroundColor Red
        $script:ErrorCount++
    }
}

Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host "  히트판 배포 스모크 테스트" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# ── 1. API 서버 생존 확인
Write-Host "[1] API 서버" -ForegroundColor Yellow
Check "health 응답" {
    $r = Invoke-WebRequest -Uri "$ApiUrl/health" -TimeoutSec 8 -UseBasicParsing
    $r.StatusCode -eq 200
}
Check "DB 연결 정상" {
    $r = Invoke-WebRequest -Uri "$ApiUrl/health" -TimeoutSec 8 -UseBasicParsing
    $r.Content -match '"database":"ok"'
}

# ── 2. Blazor 프론트 생존 확인
Write-Host ""
Write-Host "[2] Blazor 프론트" -ForegroundColor Yellow
Check "index.html 응답" {
    $r = Invoke-WebRequest -Uri "$BaseUrl" -TimeoutSec 8 -UseBasicParsing
    $r.StatusCode -eq 200
}
Check "appsettings.json 존재" {
    $r = Invoke-WebRequest -Uri "$BaseUrl/appsettings.json" -TimeoutSec 8 -UseBasicParsing
    $r.StatusCode -eq 200
}
Check "appsettings.json ApiBaseUrl 정상" {
    $r = Invoke-WebRequest -Uri "$BaseUrl/appsettings.json" -TimeoutSec 8 -UseBasicParsing
    $r.Content -match 'api-demo.hitpan.kr'
}
Check "appsettings.Development.json 존재" {
    $r = Invoke-WebRequest -Uri "$BaseUrl/appsettings.Development.json" -TimeoutSec 8 -UseBasicParsing
    $r.StatusCode -eq 200
}
Check "appsettings.Development.json ApiBaseUrl 정상" {
    $r = Invoke-WebRequest -Uri "$BaseUrl/appsettings.Development.json" -TimeoutSec 8 -UseBasicParsing
    $r.Content -match 'api-demo.hitpan.kr'
}
Check "blazor.webassembly.js 존재" {
    $r = Invoke-WebRequest -Uri "$BaseUrl/_framework/blazor.webassembly.js" -TimeoutSec 8 -UseBasicParsing
    $r.StatusCode -eq 200
}
Check "blazor.boot.json 존재 (localhost 없음)" {
    $r = Invoke-WebRequest -Uri "$BaseUrl/_framework/blazor.boot.json" -TimeoutSec 8 -UseBasicParsing
    $r.StatusCode -eq 200 -and -not ($r.Content -match 'localhost')
}

# ── 3. 터널 생존 확인
Write-Host ""
Write-Host "[3] Cloudflare 터널" -ForegroundColor Yellow
Check "demo.hitpan.kr 외부 응답" {
    $r = Invoke-WebRequest -Uri "$BaseUrl" -TimeoutSec 8 -UseBasicParsing
    $r.StatusCode -eq 200
}
Check "api-demo.hitpan.kr 외부 응답" {
    $r = Invoke-WebRequest -Uri "$ApiUrl/health" -TimeoutSec 8 -UseBasicParsing
    $r.StatusCode -eq 200
}

# ── 4. 로그인 API 확인
Write-Host ""
Write-Host "[4] 로그인 API" -ForegroundColor Yellow
Check "로그인 엔드포인트 응답" {
    try {
        $body = '{"email":"tenant@hitpan.kr","password":"Admin1234!","deviceId":"smoke-test"}'
        $r = Invoke-WebRequest -Uri "$ApiUrl/api/auth/login" -Method POST -Body $body -ContentType "application/json" -TimeoutSec 8 -UseBasicParsing
        $r.StatusCode -eq 200 -and $r.Content -match 'accessToken'
    } catch {
        $_.Exception.Response.StatusCode.value__ -ne 0
    }
}

# ── 5. 워크플로우 핵심 API 생존 확인
Write-Host ""
Write-Host "[5] 핵심 워크플로우 API" -ForegroundColor Yellow
Check "재고 API 응답" {
    try {
        $r = Invoke-WebRequest -Uri "$ApiUrl/api/stock" -TimeoutSec 8 -UseBasicParsing
        $r.StatusCode -in @(200, 401)
    } catch {
        # 401 Unauthorized = 인증 필요 = API 살아있음
        $_.Exception.Response.StatusCode.value__ -eq 401
    }
}
Check "매입 API 응답" {
    try {
        $r = Invoke-WebRequest -Uri "$ApiUrl/api/purchase" -TimeoutSec 8 -UseBasicParsing
        $r.StatusCode -in @(200, 401)
    } catch {
        $_.Exception.Response.StatusCode.value__ -eq 401
    }
}
Check "판매 API 응답" {
    try {
        $r = Invoke-WebRequest -Uri "$ApiUrl/api/sales" -TimeoutSec 8 -UseBasicParsing
        $r.StatusCode -in @(200, 401)
    } catch {
        $_.Exception.Response.StatusCode.value__ -eq 401
    }
}
Check "세금계산서 API 응답" {
    try {
        $r = Invoke-WebRequest -Uri "$ApiUrl/api/sales/tax-invoices" -TimeoutSec 8 -UseBasicParsing
        $r.StatusCode -in @(200, 401)
    } catch {
        $_.Exception.Response.StatusCode.value__ -eq 401
    }
}

# ── 결과
Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
if ($ErrorCount -eq 0) {
    Write-Host "  결과: 전체 통과 ✅ 배포 완료" -ForegroundColor Green
} else {
    Write-Host "  결과: $ErrorCount 개 실패 ❌ 배포 중단!!" -ForegroundColor Red
    Write-Host "  실패 항목 확인 후 수정하고 다시 실행하세요." -ForegroundColor Red
}
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

exit $ErrorCount
