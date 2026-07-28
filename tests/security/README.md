# OWASP ZAP DAST 1차 실행 가이드

> 작업지시서: `docs/work-orders/WS-20260601-09_OWASP_ZAP_DAST_1차.md`
> 마감: 2026-06-15
> 담당: 보안 매니저 1 (헌법·앱·인증 영역)
> 헌법: #22(데이터 최소주의) · #23(5중 검증 ④ DAST) · #25(안전하게) · #29(인프라 무허가 조작 금지)

---

## 0. 사전 점검 (반드시 실행 전)

- [ ] **헌법 #29**: ZAP 실행은 로컬·내부망 한정. Cloudflare·DNS·방화벽 설정 변경 금지.
- [ ] **사장님 사전 결재**: 운영(prod) 도메인 스캔은 사전 결재 + 트래픽 시간대 조율 필수. 기본은 로컬·스테이징.
- [ ] **본사 데이터 0건 노출 확인**: ZAP report·세션 파일에 고객 업무 데이터 포함 금지 (헌법 #22).
- [ ] **WAF·Rate Limit 우회 금지**: 우회용 헤더·토큰 주입 시 사전 결재.
- [ ] **결과물 저장 위치**: `tests/security/reports/{YYYYMMDD}_{target}_{baseline|full}.html`

---

## 1. 대상 URL 4종

| # | 시스템 | 대상 URL (로컬) | 대상 URL (스테이징) | 인증 방식 | Context |
|---|---|---|---|---|---|
| 1 | 랜딩페이지 | `http://localhost:5234/` | `https://landing-stg.hitpan.kr/` | anonymous | `landing` |
| 2 | ERP Web (Blazor) | `http://localhost:5234/erp` | `https://erp-stg.hitpan.kr/` | JWT (tenant_admin) | `erp` |
| 3 | API | `http://localhost:5257/` | `https://api-stg.hitpan.kr/` | JWT Bearer | `api` |
| 4 | 백오피스 | `http://localhost:5300/admin` | `https://admin-stg.hitpan.kr/` | JWT (PlatformOnly) | `backoffice` |

> 운영(prod) `*.hitpan.kr` 스캔은 사장님 사전 결재 필수.

---

## 2. ZAP 실행 명령

### 2.1 Baseline (passive only, 약 2~5분)

수동·자동 빠른 1차 점검용. 실제 공격 페이로드 미발사.

```powershell
# Docker (권장)
docker run --rm -v ${PWD}/tests/security:/zap/wrk/:rw `
  -t ghcr.io/zaproxy/zaproxy:stable `
  zap-baseline.py `
  -t http://host.docker.internal:5234/ `
  -c /zap/wrk/zap-config.yaml `
  -r reports/$(Get-Date -Format yyyyMMdd)_landing_baseline.html `
  -J reports/$(Get-Date -Format yyyyMMdd)_landing_baseline.json
```

### 2.2 Full Scan (active, 약 30분~수시간)

active scan 포함. **반드시 로컬·스테이징 대상으로만 실행**.

```powershell
docker run --rm -v ${PWD}/tests/security:/zap/wrk/:rw `
  -t ghcr.io/zaproxy/zaproxy:stable `
  zap-full-scan.py `
  -t http://host.docker.internal:5257/ `
  -c /zap/wrk/zap-config.yaml `
  -r reports/$(Get-Date -Format yyyyMMdd)_api_full.html `
  -J reports/$(Get-Date -Format yyyyMMdd)_api_full.json `
  -z "-config api.disablekey=true"
```

### 2.3 인증 후 스캔 (ERP·백오피스·API)

`zap-config.yaml`의 `authentication` 블록에 JWT 발급 스크립트 경로 지정 후 실행.

```powershell
docker run --rm -v ${PWD}/tests/security:/zap/wrk/:rw `
  -t ghcr.io/zaproxy/zaproxy:stable `
  zap-full-scan.py `
  -t http://host.docker.internal:5257/api/employees `
  -c /zap/wrk/zap-config.yaml `
  -r reports/$(Get-Date -Format yyyyMMdd)_api_authed_full.html
```

---

## 3. OWASP Top 10 (2021) 매핑

| ID | 항목 | ZAP Rule 예시 | 봉합 책임 매니저 |
|---|---|---|---|
| A01 | Broken Access Control | 6, 40012, 40018, 40019, 40020 | 보안 매니저 1 |
| A02 | Cryptographic Failures | 10038, 10202, 10063 | 보안 매니저 2 |
| A03 | Injection (SQLi/XSS) | 40018, 40019, 40020, 40012, 40014 | 백엔드 + 보안 매니저 1 |
| A04 | Insecure Design | 수동 검토 | 어벤져스 5중 검증 |
| A05 | Security Misconfiguration | 10015, 10020, 10021, 10035, 10036 | 보안 매니저 2 |
| A06 | Vulnerable Components | dependency-check (별도) | 백엔드 매니저 |
| A07 | Identification & Auth | 10202, 10105, 10202 | 보안 매니저 1 |
| A08 | Software & Data Integrity | 90003, 10202 | 보안 매니저 1 |
| A09 | Logging & Monitoring | 수동 검토 | 보안 매니저 2 |
| A10 | SSRF | 40046 | 백엔드 + 보안 매니저 1 |

---

## 4. Finding 등록 절차

1. ZAP report HTML/JSON → `tests/security/reports/` 저장
2. Finding별 `docs/security/DAST_FINDINGS_TEMPLATE.md` 사본 작성
3. P0/P1/P2 분류 (템플릿 기준 준수)
4. 봉합 PR 번호 매핑 후 PM 결재 요청
5. **본사 데이터 최소주의(#22) 위반 발견 시 즉시 P0 + 사장님 긴급 보고**

---

## 5. 금지 사항 (헌법 정합)

- 운영(prod) Cloudflare·DNS·방화벽 룰 변경 (#29)
- ZAP report에 고객 평문 데이터 노출 (#22)
- 빈 catch 블록으로 ZAP 알람 silent swallow (#15)
- 외부(인터넷) 대상 무허가 스캔 (#29, 법령 위반 가능)
- WAF/Rate Limit 우회 헤더 주입 (사전 결재 필수)
