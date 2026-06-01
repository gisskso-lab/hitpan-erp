# WS-20260601-09 — OWASP ZAP DAST 1차

> 발행: 2026-06-01 / 결재: 사장님 모두결재 / 담당: 보안 매니저 1 (헌법·앱·인증 영역) / 마감: 2026-06-15

---

## 1. 목적

베타 출시 절대 게이트 (EVF §12 + 헌법 #23 5중 검증 ④번) 통과를 위한 1차 DAST(동적 분석) 실행. 5중 중 1개라도 실패 시 머지 금지.

---

## 2. 대상 시스템

| 시스템 | URL | 비고 |
|---|---|---|
| 랜딩페이지 | https://demo.hitpan.kr/landing 외 7개 | Anonymous |
| ERP (인증 후) | https://demo.hitpan.kr | JWT 인증 + tenant@hitpan.kr |
| API | https://api-demo.hitpan.kr | OpenAPI 스펙 import |
| 백오피스 (Platform) | /admin/* | PlatformOnly 정책 |

---

## 3. 검증 항목 (OWASP Top 10 2021)

1. A01 Broken Access Control — tenant_id JWT 클레임만 (헌법 #2)
2. A02 Cryptographic Failures — AES-256 컬럼 미적용 PR 차단 (헌법 #5)
3. A03 Injection — Dapper 파라미터 바인딩 / SQL 인젝션 0건
4. A04 Insecure Design — 6단계 워크플로우 순서 (헌법 #8)
5. A05 Security Misconfiguration — HSTS·CSP·X-Frame-Options
6. A06 Vulnerable Components — NuGet 취약 0건 유지
7. A07 Auth Failures — JWT Refresh 회전·Rate Limit
8. A08 Software/Data Integrity — 5중 검증 (헌법 #23)
9. A09 Logging Failures — 빈 catch 0건 (헌법 #15)
10. A10 SSRF — Cloudflare Worker 화이트리스트

---

## 4. 산출물

- `tests/security/zap-baseline-20260615.html` — Baseline 스캔 리포트
- `tests/security/zap-full-20260615.html` — Full 스캔 리포트
- `docs/security/DAST_FINDINGS_20260615.md` — Finding별 P0/P1/P2 분류 + 봉합 PR 매핑

---

## 5. 절대 원칙

- P0 (High) 1건이라도 발견 시 베타 출시 게이트 차단
- 인프라(Cloudflare·DNS·방화벽) 조작은 사장님 사전 결재 (헌법 #29)
- 본사 데이터 최소주의 (#22) 위반 발견 시 즉시 P0
