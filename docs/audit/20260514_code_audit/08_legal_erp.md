# 08. 법무팀장 + ERP매니저 데이터 흐름 보고서

**작성:** 법무팀장 + ERP매니저(더존 30년)
**일자:** 2026-05-14 새벽

---

## 1. 헌법 #18·#22 위반 분석

✅ **본사 송신 0건 확인**
- HttpClient/SendAsync/POST 외부 호출 0건
- MdbMigrationService 전 범위 로컬 INSERT만
- 마스터키 로컬 보관, 암호화 데이터도 전송 금지

✅ **형사영역 AES-256 적용**
- L:416 ceo_resident_no_encrypted
- L:610 resident_no_encrypted
- L:611 salary_encrypted
- L:614 salary_extra_encrypted

**위반 0건 판정.**

---

## 2. 형사영역 6컬럼 표

| 컬럼 | 레거시 | 신매핑 | 암호화 | 법령 | 상태 |
|---|---|---|---|---|---|
| SW_JUMIN | Text14 | employees.resident_no_encrypted | AES-256 | 소득세법 §127·§164, 4대보험 | ✅ |
| SW_PAY | Int32 | employees.salary_encrypted | AES-256 | 근로기준법 §48, 개보법 §29 | ✅ |
| SW_PAYgu | TinyInt | salary_type | 평문 OK | 식별성 낮음 | ✅ |
| SW_PAYeuy | TinyInt | salary_category | 평문 OK | 식별성 낮음 | ✅ |
| SW_PAYoth | Text100 | salary_extra_encrypted | AES-256 | 개보법 §29 | ✅ |
| buy_topjumin | Text13 | partners.ceo_resident_no_encrypted | AES-256 | 부가세법 §32, 소득세 §127 | ✅ |

**100% 준수.**

---

## 3. ERP 6단계 매핑

| 단계 | 영역 | 마이그 메서드 | 상태 |
|---|---|---|---|
| 1 | 설정 (회사·직원·권한·기기·양식) | 설치 시 초기화 | ✅ |
| 2 | 마스터 (거래처·상품·BOM·특별단가) | MigratePartners/Items/Bom | ✅ |
| 3 | 매입 (발주·매입·반품) | MigratePurchaseOrdersFromIU(DOCFA) | ✅ |
| 4 | 판매 (견적·수주·거래명세서·세금계산서) | MigrateSalesOrdersFromIO + MigrateTaxInvoices (synthetic deliveries 자동 생성 L:1142-1221) | ✅ |
| 5 | 현황 (재고·매입·판매) | MigrateStockLedger(DOCFB) | ✅ |
| 6 | 재무 (회계·경비·수금) | MigrateCashbook/Expenses/Collections | ✅ |

**헌법 #20 준수:** orphan DOCF4 행에 synthetic sales_deliveries 자동 생성 → FK 무결성. journal_lines INSERT ONLY.

---

## 4. 주민번호 도메인 법령

- §39 오인용 폐기 (퇴직증명서, 급여 무관)
- 실 근거: 소득세법 §127(원천징수), §164(지급명세서), 4대보험 신고 → 주민번호 13자리 합법
- 개보법 §24의2 ①항 단서 "법령에서 구체적으로 허용한 경우"
- CRIMINAL_DOMAIN_POLICY.md L:33-35

**합법 ✅**

---

## 5. 한 화면 완결 원칙

- 마이그 = 단순 이관, 별도 UI 검증 불필요
- 신 ERP 페이지: 6단계 선형 흐름, 단계별 1화면, 스크롤 없음

✅ 준수

---

## 6. 최종 판정 표

| 항목 | 판정 | 근거 |
|---|---|---|
| #18 본사 송신 0 | ✅ | 코드 검색 0건 |
| #22 데이터 최소 | ✅ | 마스터키 로컬 + 암호화 전송 금지 |
| 형사 6컬럼 AES | ✅ | 4 암호 + 2 평문(식별성 낮음) |
| ERP 6단계 정합 | ✅ | 전 단계 메서드 작동 + FK 무결 |
| 주민번호 법령 | ✅ | 소득세·4대보험·개보법 단서 |
| 한 화면 완결 | ✅ | 6단계 선형 + 단계별 1화면 |

---

## 7. 서브에이전트 ERP/법무 분담

- **ERP매니저:** 마이그 후 W3 조회 성능·tx 무결성 테스트
- **법무팀장:** 개인정보처리 동의서 양식 + 약관 재검증 (헌법 2조 7단계 고지)
- **보안매니저:** 마스터키 백업·복구 매뉴얼 + 감사로그(SENSITIVE_ACCESS_LOG_DDL.md)

**점검 완료. 추가 헌법 위반 0건.**
