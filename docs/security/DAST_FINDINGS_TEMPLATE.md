# DAST Finding 템플릿 (OWASP ZAP 1차)

> 작업지시서: `docs/work-orders/WS-20260601-09_OWASP_ZAP_DAST_1차.md`
> Finding 1건당 본 템플릿을 복사하여 `docs/security/findings/{YYYYMMDD}_{NN}_{title}.md`로 저장.
> 헌법 정합: #22 데이터 최소주의 / #23 5중 검증 ④ DAST / #25 안전하게

---

## 1. 기본 정보

| 항목 | 내용 |
|---|---|
| Finding ID | DAST-{YYYYMMDD}-{NN} |
| 발견 일자 | YYYY-MM-DD |
| 발견자 | 보안 매니저 1 / 보안 개발자 |
| 스캔 대상 | landing / erp / api / backoffice |
| 대상 URL | http://localhost:.../... |
| ZAP Rule ID | 예: 40018 |
| ZAP Alert Name | 예: SQL Injection |
| 재현 가능 여부 | YES / NO |

---

## 2. 위험도 분류

### 2.1 P0/P1/P2 결정 매트릭스

| 등급 | 기준 | 봉합 SLA |
|---|---|---|
| **P0** | 즉시 운영 차단. ① 인증 우회·테넌트 격리 실패 ② SQLi·RCE ③ 본사 데이터 최소주의(#22) 위반 ④ 민감정보 평문 노출 | 24시간 |
| **P1** | XSS·CSRF·SSRF·권한 상승·세션 고정·약한 암호화 | 7일 |
| **P2** | 보안 헤더 누락·정보 누출·Best Practice 위반 | 14일 |

### 2.2 본 Finding 등급

- [ ] P0
- [ ] P1
- [ ] P2

**판단 근거:**
```
(예: 헌법 #22 위반 — 본사 telemetry 엔드포인트에서 고객 매출 데이터 평문 노출 → 즉시 P0)
```

---

## 3. 취약점 상세

### 3.1 OWASP Top 10 매핑

- [ ] A01 Broken Access Control
- [ ] A02 Cryptographic Failures
- [ ] A03 Injection
- [ ] A04 Insecure Design
- [ ] A05 Security Misconfiguration
- [ ] A06 Vulnerable Components
- [ ] A07 Identification & Auth Failures
- [ ] A08 Software & Data Integrity
- [ ] A09 Logging & Monitoring Failures
- [ ] A10 SSRF

### 3.2 재현 절차

```http
(ZAP raw request 붙여넣기 — 단, 실제 토큰·비밀번호는 ***로 마스킹. 헌법 #22)

POST /api/... HTTP/1.1
Host: localhost:5257
Authorization: Bearer ***
Content-Type: application/json

{...}
```

### 3.3 응답 증거

```http
(ZAP raw response — 민감 데이터 ***로 마스킹)
```

### 3.4 영향 범위

- 영향 받는 테넌트: (예: 전체 / 특정 권한 / anonymous)
- 영향 받는 데이터: (예: tenant_id 격리 실패 — 타 테넌트 매출 데이터 조회 가능)
- 헌법 위반 여부: #__ (해당 시 명시)

---

## 4. 봉합 계획

### 4.1 책임 매니저

- [ ] 보안 매니저 1 (헌법·앱·인증)
- [ ] 보안 매니저 2 (인프라·OS)
- [ ] 백엔드 매니저
- [ ] 프론트 매니저
- [ ] DB 매니저

### 4.2 봉합 작업지시서

- 작업지시서 번호: WS-YYYYMMDD-NN
- 발행일: YYYY-MM-DD
- 마감일: YYYY-MM-DD (SLA 기준)

### 4.3 봉합 PR

| 항목 | 내용 |
|---|---|
| PR 번호 | #____ |
| 브랜치 | fix/dast-{ID} |
| 머지 일자 | YYYY-MM-DD |
| 5중 검증 통과 | [ ] ① 작지서 보안 요구사항 [ ] ② 매니저 리뷰 [ ] ③ SAST [ ] ④ DAST 재스캔 [ ] ⑤ #22 검증 |

### 4.4 재스캔 결과

- 재스캔 일자: YYYY-MM-DD
- ZAP report: `tests/security/reports/{YYYYMMDD}_{target}_rescan.html`
- 결과: [ ] PASS (Alert 사라짐) / [ ] FAIL (재발 → 작지서 재발행)

---

## 5. 사장님 보고 (P0 한정)

P0 Finding은 발견 즉시 사장님 보고 필수.

- [ ] 보고 일자: YYYY-MM-DD HH:MM
- [ ] 보고 경로: PM 직결
- [ ] 임시 차단 조치: (예: 엔드포인트 비활성화 / 기능 토글 OFF)
- [ ] 사장님 결재: [ ] 즉시 봉합 / [ ] 임시 차단 후 봉합 / [ ] 수용 (사유 필수)

---

## 6. 학습 박제

- 동일 패턴 재발 방지책:
- AI수석 학습 패턴 등록 여부: [ ] YES (`docs/security/patterns/`) [ ] NO
- 어벤져스 5중 검증 체크리스트 추가 여부: [ ] YES [ ] NO
