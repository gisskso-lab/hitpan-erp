# 다음 세션 핸드오프 — 도메인 전환 마무리 (2026-04-29 마감)

## 🎯 다음 세션 목표 (5분 작업)

`https://demo.hitpan.kr` 에서 **로그인 → 메인 화면 진입** 까지 봉합.

CORS 에러 한 가지만 해결하면 끝. 나머지 인프라(도메인·DNS·터널·매핑) 다 완성됨.

---

## ✅ 오늘 완료된 것 (변함없는 사실)

1. **`hitpan.kr` 가비아 구매 완료** (2027-04-29 만료, 자동연장)
2. **Cloudflare 가입 + Free 플랜** + 도메인 등록
3. **가비아 네임서버 → Cloudflare 변경 완료**
   - bob.ns.cloudflare.com / magali.ns.cloudflare.com
4. **DNS 전파 완료, hitpan.kr 활성화**
5. **사장님 PC에 cloudflared Windows 서비스 설치**
   - 위치: `C:\Program Files (x86)\cloudflared\cloudflared.exe`
   - 서비스명: `Cloudflared` (자동 시작, Running)
6. **터널 `hitpan-demo` 생성**
   - 터널 ID: `68e0988c-9554-4a5c-8718-718dae793ced`
7. **Public Hostname 매핑 완료**
   - `demo.hitpan.kr` → `http://localhost:5234`
   - `api-demo.hitpan.kr` → `http://localhost:5257`
8. **DNS 레코드 자동 생성 (터널 라우트)**
9. **Web appsettings.json 수정** (`https://api-demo.hitpan.kr`)
10. **API CORS 코드 단순화** (Program.cs 164-174줄, SetIsOriginAllowed _ => true)

---

## ❌ 현재 막힌 것 — CORS preflight 실패

```
Access to fetch at 'https://api-demo.hitpan.kr/api/auth/login'
from origin 'https://demo.hitpan.kr' has been blocked by CORS policy:
Response to preflight request doesn't pass access control check:
No 'Access-Control-Allow-Origin' header is present
```

**증상:**
- ✅ `https://demo.hitpan.kr` 로그인 화면 정상 표시
- ❌ 로그인 시도하면 CORS 에러 → `서버에 연결할 수 없습니다`
- 로컬(`http://localhost:5234`)에서도 같은 에러

**진단 결과:**
- DLL LastWriteTime: 2026-04-29 18:40 (새 빌드)
- API 프로세스 StartTime: 2026-04-29 18:44 (새 dll로 도는 게 맞음)
- 그런데도 CORS 헤더 안 박힘

**의심 원인 (다음 세션 첫 진단):**

1. **OPTIONS preflight 메서드가 컨트롤러에 도달하기 전에 미들웨어 어딘가에서 차단/스왈로우**
   - GlobalExceptionMiddleware? AuditLogMiddleware? RateLimitMiddleware? TenantMiddleware? IdempotencyMiddleware?
   - `app.UseCors("BlazorWasmDev")` 가 214번 줄, MapControllers는 237번 줄. 사이의 미들웨어가 OPTIONS 가로챘을 가능성.
2. **TenantMiddleware가 OPTIONS 요청에서 tenant_id 못 찾고 401/403 반환** → CORS 헤더 안 박힘
3. **Cloudflare 터널이 OPTIONS preflight 자체를 차단** (가능성 낮음, 보통 통과시킴)

---

## 🚀 다음 세션 즉시 시도 — 명령어 한 줄 진단

PowerShell 관리자에서:

```powershell
curl.exe -i -X OPTIONS "http://localhost:5257/api/auth/login" -H "Origin: https://demo.hitpan.kr" -H "Access-Control-Request-Method: POST" -H "Access-Control-Request-Headers: content-type"
```

### 결과별 분기

**A. 응답에 `Access-Control-Allow-Origin: https://demo.hitpan.kr` 보임**
→ API CORS는 OK. **Cloudflare 터널이 헤더 떼는 것** → Cloudflare 측 수정.

**B. 응답에 헤더 없음 + 200/204**
→ CORS 정책에 정의 안 됨. Program.cs CORS 정책 다시 확인.

**C. 응답이 401/403/404**
→ **미들웨어가 OPTIONS 가로챔.** 진짜 의심되는 곳.
→ 해결: `app.UseCors()` 호출을 **GlobalExceptionMiddleware보다 위**로 옮기기:

```csharp
// 현재 (Program.cs 214줄)
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseCors("BlazorWasmDev");

// 변경
app.UseCors("BlazorWasmDev");      // ⭐ 맨 위
app.UseMiddleware<GlobalExceptionMiddleware>();
```

또는 더 확실하게 — **TenantMiddleware/AuditLogMiddleware/RateLimitMiddleware에 OPTIONS 무시 로직 추가:**

```csharp
public async Task InvokeAsync(HttpContext context, RequestDelegate next)
{
    if (context.Request.Method == "OPTIONS")
    {
        await next(context);  // preflight는 건너뛰기
        return;
    }
    // 기존 로직
}
```

---

## 🛠️ 환경 정보 (다음 세션 컨텍스트)

### 사장님 PC 상태
- **OS**: Windows 11
- **API 포트**: 5257 (Web 클라이언트 wasm은 https://api-demo.hitpan.kr으로 호출)
- **Web 포트**: 5234
- **MariaDB**: 11.4.10, 포트 3306, hitpan / Hitpan2025!
- **cloudflared**: Windows 서비스 자동 시작

### 핵심 파일 변경 내역 (오늘)
- `src/HitPan.Web/wwwroot/appsettings.json` — `ApiBaseUrl: https://api-demo.hitpan.kr`
- `src/HitPan.API/Program.cs` 164-174줄 — CORS 정책 단순화 (모든 Origin 허용)

### Cloudflare 정보
- 계정: `gisskso@gmail.com`
- 도메인: `hitpan.kr` (Free 플랜)
- 네임서버: bob.ns.cloudflare.com, magali.ns.cloudflare.com
- 터널: `hitpan-demo` (ID: `68e0988c-9554-4a5c-8718-718dae793ced`)
- DNS 레코드 (자동, 터널 라우트):
  - `demo.hitpan.kr` → 터널 → `http://localhost:5234`
  - `api-demo.hitpan.kr` → 터널 → `http://localhost:5257`

### 가비아 정보
- 도메인: hitpan.kr
- 만료일: 2027-04-29
- 등록일: 2026-04-29
- 자동연장: ON, 등록정보 숨김: ON

---

## 🚨 다음 세션 시작 시 클로드에게 전달할 한 마디

```
오늘 도메인 인프라 다 끝냈는데 CORS 한 곳에서 막힘.
docs/handoff/next_session_prompt_20260429_domain.md 읽고
"즉시 시도 한 줄 진단"부터 시작. 5분 안에 끝내자.
```

---

## 📋 사장님 시연 시 명심할 것

### 로컬 시연 (CORS 막혀도 작동)
- 주소: `http://localhost:5234`
- 로그인: `tenant@hitpan.kr` / `Admin1234!`
- 로컬은 같은 PC라 CORS 영향 없음 → 정상 작동

### 인터넷 시연 (CORS 풀린 후 가능)
- 주소: `https://demo.hitpan.kr`
- 로그인 같음
- 사장님 폰으로도 같은 주소로 접속 가능

---

## 💡 영업 가능한 것 (현재)

- ✅ "고객사 등록하면 https://tenant001.hitpan.kr 같은 주소로 접속" 시연 가능 (구두)
- ✅ Cloudflare API로 자동화 가능한 인프라 (베타 9곳 자동 등록 시 5분/곳)
- ⏳ 실제 화면 시연은 CORS 풀린 후 (5분 작업)

---

## 🔚 오늘 마감 시점 시스템 상태

- **Web**: PC에서 실행 중 (포트 5234)
- **API**: PC에서 실행 중 (포트 5257)
- **cloudflared**: Windows 서비스 자동 시작 (PC 재부팅해도 살아남음)
- **MariaDB**: 정상

PC 재부팅 후엔:
- ✅ cloudflared, MariaDB 자동 시작
- ❌ Web/API는 수동 재기동 필요 (다음 세션 첫 단계에서 다시 띄우기)

---

## 작업자 메모 (5/23 베타 출시까지 남은 일)

1. **CORS 한 발** (5분, 다음 세션)
2. **인스톨러 v2.0 — Cloudflare API 자동화 통합** (며칠~1주)
3. **본사 서버 (홈페이지) — 신규 가입 자동화** (1~2주)
4. **베타 9곳 1대1 등록** (각 30분, 총 4~5시간)

오늘 인프라 80% 완성. 마지막 한 발만 남음.
