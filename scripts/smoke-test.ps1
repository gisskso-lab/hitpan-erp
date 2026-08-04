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
# ★ 봉합 20260803작2 P1-1 (병렬검증 적발 · 사장님 지시 "고쳐"):
#   종전 이 검사는 `-match 'api-demo.hitpan.kr'` 이었다. 즉 **오늘 P0 의 원인 문자열이
#   있어야 PASS** 하도록 굳어 있었다. 그대로 두면 두 결말 모두 나쁘다:
#     (a) 격리 봉합 후 이 스모크가 FAIL 로 뒤집힌다 → 통과시키려 봉합을 되돌린다 = P0 재발
#     (b) 상시 적색 → 무시 습관화
#   오진 3회의 공통 패턴 — "검증기가 틀린 걸 정답으로 알고 있다" — 와 동일 계통이다.
#
#   ■ 왜 단순 반전(-notmatch)이 아닌가
#     이 스크립트의 기본 대상은 **demo.hitpan.kr**(param 기본값)이고, demo 는 Web(5234)과
#     API(api-demo:5257)가 **서로 다른 도메인**이라 명시값이 정답이다(7/30 실측: 비웠더니 405).
#     반대로 고객 설치본은 Web·API 가 같은 출처라 **빈 값이 정답**이다(HostEnvironment.BaseAddress 폴백).
#     ⇒ 하나의 고정 문자열로 양쪽을 판정할 수 없다. **대상에 따라 기대값이 갈린다.**
#   ■ 채택: 대상이 demo 면 $ApiUrl 과 일치해야 하고, 고객 도메인이면 빈 값이어야 한다.
#     '어느 쪽도 아닌 값'(= 남의 회사 주소)이면 무조건 FAIL — 이것이 격리의 본질이다.
Check "appsettings.json ApiBaseUrl 격리 정합" {
    $r = Invoke-WebRequest -Uri "$BaseUrl/appsettings.json" -TimeoutSec 8 -UseBasicParsing
    $api = ($r.Content | ConvertFrom-Json).ApiBaseUrl
    if ($BaseUrl -like '*demo.hitpan.kr*') {
        # demo 배치: Web·API 가 다른 도메인 → 명시값이 정답
        $api -eq $ApiUrl
    } else {
        # 고객 설치본: Web·API 가 같은 출처 → 빈 값이 정답(자기 출처 폴백)
        #   여기에 주소가 박혀 있으면 그 고객은 남의 서버를 본다 = 테넌트 격리 붕괴
        [string]::IsNullOrWhiteSpace($api)
    }
}
Check "appsettings.Development.json 존재" {
    $r = Invoke-WebRequest -Uri "$BaseUrl/appsettings.Development.json" -TimeoutSec 8 -UseBasicParsing
    $r.StatusCode -eq 200
}
Check "appsettings.Development.json ApiBaseUrl 정상" {
    # Development 는 개발 PC(demo) 전용이라 api-demo 명시값이 정답이다.
    #   ※ 고객 PC 에도 이 파일이 출하되지만 Blazor 는 blazor-environment 헤더가 없으면
    #     Production 이라 **로드하지 않는다**(20260803작2 §4 실측). 그래서 여기선 demo 기준으로 본다.
    $r = Invoke-WebRequest -Uri "$BaseUrl/appsettings.Development.json" -TimeoutSec 8 -UseBasicParsing
    ($r.Content | ConvertFrom-Json).ApiBaseUrl -eq $ApiUrl
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
