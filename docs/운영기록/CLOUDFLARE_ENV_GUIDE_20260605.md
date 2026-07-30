# Cloudflare 환경변수 가이드 — 사장님 직접 작업

> 발행: 2026-06-05 PM (브라운킴)
> 헌법 #29 정합 — 본 PM 손 못 댐, 사장님 직접 영역

---

## 1. 작업 개요

W14 (Cloudflare 도메인 자동 발급 골격) 활성화에 필요한 환경변수 3개를 Windows Machine scope에 등록합니다. 등록 전까지는 `/admin/tenant-domains` 화면이 "환경변수 미설정" 경고를 노출하고 발급 API 호출은 503 응답으로 차단됩니다.

---

## 2. 사전 준비 (Cloudflare 대시보드에서 수집)

### 2.1 CLOUDFLARE_API_TOKEN
- Cloudflare 대시보드 → My Profile → API Tokens → Create Token
- 권한:
  - `Zone : DNS : Edit`
  - `Account : Cloudflare Tunnel : Edit` (선택, 본 골격은 DNS만)
- Zone Resources: `Include — Specific zone — hitpan.kr`

### 2.2 CLOUDFLARE_ZONE_ID
- Cloudflare 대시보드 → hitpan.kr 도메인 클릭 → 우측 사이드 "API" 박스
- "Zone ID" 32자리 hex 값 복사

### 2.3 CLOUDFLARE_ACCOUNT_ID
- 같은 사이드 박스 "Account ID" 32자리 hex 값 복사

---

## 3. Windows 환경변수 등록 (관리자 PowerShell)

```powershell
# 관리자 권한 PowerShell 실행 후
[Environment]::SetEnvironmentVariable("CLOUDFLARE_API_TOKEN",   "<2.1 토큰>",      "Machine")
[Environment]::SetEnvironmentVariable("CLOUDFLARE_ZONE_ID",     "<2.2 Zone ID>",   "Machine")
[Environment]::SetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID",  "<2.3 Account ID>","Machine")

# 등록 확인
[Environment]::GetEnvironmentVariable("CLOUDFLARE_API_TOKEN",   "Machine")
[Environment]::GetEnvironmentVariable("CLOUDFLARE_ZONE_ID",     "Machine")
[Environment]::GetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID",  "Machine")
```

---

## 4. 백오피스 API 재기동 (환경변수 적용)

환경변수는 새 프로세스에서만 읽힘 — 기존 백오피스 API (port 5258) 종료 후 재기동.

```powershell
# 가동 중인 백오피스 API 종료
Get-Process -Name "HitPan.Backoffice.API" -ErrorAction SilentlyContinue | Stop-Process -Force

# 새 셸에서 재기동 (환경변수 자동 적용)
cd C:\Users\소순근\Desktop\hitpan-erp\src\HitPan.Backoffice.API
dotnet run --no-launch-profile --urls http://localhost:5258
```

---

## 5. 통과 확인

### 5.1 화면 점검
- 사장님 백오피스 로그인 (`http://localhost:5291/backoffice/login`)
- 사이드바 → Owner 전용 → "도메인 자동 발급"
- 상단 알림: 녹색 "Cloudflare 자격증명 정상 — 발급 가능 상태" 표시

### 5.2 API 점검
```powershell
curl http://localhost:5258/api/backoffice/tenant-domains
# 응답에 "configured":true 포함되면 통과
```

---

## 6. 헌법 정합

| 헌법 | 정합 |
|---|---|
| #18·#22 | 토큰은 환경변수에만 존재, DB·로그·코드 0건 |
| #29 | 본 PM 사전 결재 없이 호출 0건, 환경변수 설정은 사장님 직접 |
| #34 | 베타부터 정식 완성도 — www.{tenant_code}.hitpan.kr 발급 |

---

## 7. 잔여 후속 차수 (별도 결재 영역)

- cloudflared 터널 자동 발급 흐름 (현재 골격에서는 null, 본사 사전 발급한 터널로 CNAME만 연결)
- DNS 발급 후 자동 ERP 부트스트랩 토큰 발급·전송
- 발급 실패 시 자동 재시도 + Owner 알림

---

**적용 후 결과 알려주시면**, `/admin/tenant-domains` 화면에서 실제 발급 테스트 진행 가능합니다 (W14 통합 실측).
