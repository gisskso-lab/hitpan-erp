# 마커스 리 인계 문서 — 4/29 도메인+터널 작업

> 작성: CTO / 사장님 결재: 2026-04-29 / 우선순위: P0
> 목적: 베타 10곳 영구 URL 매핑 + Cloudflare 인증 lockout 해제

---

## 🎯 사장님 결재 사항 (2026-04-29)

1. **도메인**: `hitpan.kr` 단일 (22,000원/년) — 4/25 `.app` 정책 폐기
2. **분배 모델**: `tenantNNN.hitpan.kr` 서브도메인 1고객당 1개
3. **목표**: 베타 10곳 EXE의 임의 URL → 영구 서브도메인으로 매핑

---

## ⚠️ 현재 차단 요인 2개

### #1. 사장님 hitpan.kr 미구매
- 사장님이 가비아·후이즈·카페24 중 한 곳에서 구매 진행 (4/29 오전 안)
- 결제 후 **등록 완료 화면 캡처** 또는 도메인 관리 콘솔 접근 정보 마커스에게 전달

### #2. Cloudflare 인증 lockout (4/28 발생)
- 계정: `Gisskso@gmail.com` (Account ID: `62b2856d779a0eb151fe0637cbb84161`)
- 증상: 로그인 시도 락아웃, 4/28 9곳 터널 토큰 발급 중단됨
- 해제 절차: Cloudflare 고객센터 (지원 티켓) → 신원 확인 → 24시간 내 해제 통상

---

## 📋 작업 시퀀스 (마커스 리)

### Phase A — Cloudflare lockout 해제 (오전)
1. Cloudflare 지원 티켓 제출 (영문) — 사유: "Multiple failed login attempts triggered account lock during emergency tunnel deployment"
2. 2FA 백업 코드 / 가입 시 결제 카드 마지막 4자리 준비
3. 해제 완료 후 즉시 Account API Token 신규 발급 (권한: Zone DNS Edit, Tunnel Edit)

### Phase B — hitpan.kr → Cloudflare 등록
1. 사장님이 산 도메인의 등록처(가비아 등) 콘솔에서 **네임서버 변경**
   - 변경 전: 가비아 기본 NS
   - 변경 후: Cloudflare 발급 NS 2개 (예: `aaa.ns.cloudflare.com`, `bbb.ns.cloudflare.com`)
2. DNS 전파 대기 (5분~24시간, 보통 30분 안)
3. Cloudflare 대시보드에서 SSL/TLS = Full (strict) 설정
4. Always Use HTTPS = ON

### Phase C — 베타 10곳 서브도메인 매핑
`installer-build/tunnels.csv`에 박힌 토큰 10개 + tenant-001(본사) 1개 = 총 11개 매핑.

각 행에 대해:
```
tenant001.hitpan.kr → 토큰_1 (cloudflared Named Tunnel)
tenant002.hitpan.kr → 토큰_2
...
tenant010.hitpan.kr → 토큰_10
```

방법:
- Cloudflare API로 자동화 (권장):
```powershell
foreach ($t in (Import-Csv installer-build/tunnels.csv)) {
    $body = @{
        type = "CNAME"
        name = $t.TunnelName  # tenant001 등
        content = "$($t.TunnelId).cfargotunnel.com"
        proxied = $true
    } | ConvertTo-Json
    Invoke-RestMethod `
      -Uri "https://api.cloudflare.com/client/v4/zones/{ZoneId}/dns_records" `
      -Method POST `
      -Headers @{ Authorization = "Bearer $token" } `
      -ContentType "application/json" `
      -Body $body
}
```

### Phase D — 인스톨러 v1.0.8 (자동화)
- `installer/HitPan.iss` 또는 PS1에 **신규 고객 가입 시 자동 DNS 레코드 생성** 코드 추가
- 본사 SaaS 백오피스가 신규 고객 가입 처리 시:
  1. cloudflared Named Tunnel 신규 생성 → 토큰 발급
  2. 그 토큰의 TunnelId로 `tenantNNN.hitpan.kr` CNAME 레코드 자동 INSERT
  3. EXE 빌드 시 토큰 + 서브도메인 둘 다 박아넣기
- 결과: 영업이 EXE 1개 보내기만 하면 고객은 그 도메인으로 영구 접속

### Phase E — 검증 (오후)
1. 사장님 PC에 tenant001.hitpan.kr 매핑 → `https://tenant001.hitpan.kr` 접속 → ERP 로그인 통과 확인
2. 베타 9곳 EXE 중 1개 실제 PC에서 테스트 → 영구 URL 접속 확인
3. 재부팅 후 재접속 → 같은 URL 유지 확인 (Named Tunnel 핵심)

---

## 🔑 환경 정보

```
Cloudflare 계정: Gisskso@gmail.com
Account ID: 62b2856d779a0eb151fe0637cbb84161
도메인: hitpan.kr (사장님 4/29 구매 예정)
터널 토큰 CSV: installer-build/tunnels.csv (10건, 4/28 발급분)
EXE 빌드: dist/HitPan-Setup-tenant-001~010.exe (현재 임시 trycloudflare URL 박힌 상태)
ISCC: C:\Users\소순근\AppData\Local\Programs\Inno Setup 6\ISCC.exe
```

---

## ⚠️ §절대원칙 준수

- **#1 덮어쓰기 X**: tunnels.csv 수정 시 백업 → ALTER 형태
- **#7 SaaS ↔ ERP 권한 분리**: DNS 레코드 관리는 SaaS 계층 (백오피스)
- **#18 본사 ↔ 고객사 데이터 경계**: 도메인 매핑 정보는 SaaS 운영 데이터 (OK)
- **#19 errors 0 + warnings 0**: 자동화 스크립트도 동일 기준

---

## 📌 마커스 리 시작 프롬프트

> 사장님 / CTO: "마커스 리 출근. 도메인+터널 건 인계 받아. `docs/handoff/marcus_lee_20260429_domain_tunnel.md` 봐."
>
> 마커스 리: "넵. Phase A부터 즉시 진행하겠습니다. 사장님 hitpan.kr 구매 완료되셨는지 먼저 확인하겠습니다."

---

## 🔄 CTO ↔ 마커스 리 분담

| 영역 | 담당 |
|---|---|
| ERP 워크플로우 P0 (자동 사슬·수주서·UX) | CTO |
| 도메인·터널·DNS 인프라 | 마커스 리 |
| 인스톨러 v1.0.8 자동화 | 마커스 리 |
| 베타 9곳 EXE 재빌드 (영구 URL 박기) | 마커스 리 |
| 사장님 시연 검증 | CTO |
