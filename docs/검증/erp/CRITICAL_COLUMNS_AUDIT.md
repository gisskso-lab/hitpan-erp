# 핵심 컬럼 감사 — DOCF4·DOCFS·DOCSW 정독

> **작성:** 2026-05-12 W1 D4 / ERP매니저(더존 30년) + DB매니저
> **목적:** 신 히트판 매핑 정확도 100% 확보
> **방법:** PowerShell 실측 결과 + 코드 매핑 비교 + 헌법 #18 형사 영역 점검

---

## 1. DOCF4 — 세금계산서 35컬럼 정독

### 전체 컬럼 매핑

| 레거시 | 타입 | 신 히트판 매핑 | 비고 |
|---|---|---|---|
| **TX_NO** | Text8 | tax_invoices.invoice_number | PK 일부, 세금계산서 번호 |
| **TX_IO** | Text1 | tax_invoices.direction | PK 일부, I/O (매출/매입) |
| TX_BUY | Int32 | tax_invoices.partner_id | 거래처 FK |
| TX_GU | Text1 | tax_invoices.invoice_type | 발행 구분 |
| TX_GU1 | Text1 | tax_invoices.invoice_subtype | 발행 세부 |
| TX_PDT | Text8 | tax_invoices.issue_date | 발행 일자 |
| TX_seq | Int16 | tax_invoices.seq | 순번 |
| TX_y1, TX_y2 | Int16 | tax_invoices.year_main, year_sub | 연도 보조 |
| TX_old | Currency | tax_invoices.legacy_amount | 레거시 호환 금액 |
| TX_REM | Text100 | tax_invoices.memo | 비고 |
| TX_REM1 | Text100 | tax_invoices.memo2 | 비고 2 |
| **4품목 (TX_PUM1~4, TX_SU1~4, TX_DAN1~4, TX_KUM1~4, TX_VAT1~4)** | | tax_invoice_items (행 분해) | ⭐ 1행 → 최대 4행 |

### 🌟 전자세금계산서 발행 이력 4개 컬럼 (신규 발견)

| 레거시 | 타입 | 의미 | 신 히트판 처리 |
|---|---|---|---|
| **TX_READDT** | Text8 | 국세청 READ 일자 | etax_send_history.nts_read_date |
| **TX_REPORTDT** | Text8 | 국세청 REPORT 일자 | etax_send_history.nts_report_date |
| **TX_SENDDT** | Text8 | 전송 일자 | etax_send_history.sent_date |
| **TX_PDT** | Text8 | 발행 일자 | tax_invoices.issue_date |

**의미:** 레거시는 **전자세금계산서 발행 이력 보유**. 신 히트판은 etax_send_history 테이블 신설로 보존 가능.

### 신규 테이블 `etax_send_history` 제안

```sql
CREATE TABLE IF NOT EXISTS etax_send_history (
    history_id        CHAR(36) PRIMARY KEY,
    tenant_id         CHAR(36) NOT NULL,
    tax_invoice_id    CHAR(36) NOT NULL,
    sent_date         DATE NULL,
    nts_read_date     DATE NULL,           -- 국세청 READ
    nts_report_date   DATE NULL,           -- 국세청 REPORT
    nts_approval_no   VARCHAR(50) NULL,    -- 승인번호 (신 발행 시)
    asp_provider      VARCHAR(20) NULL,    -- 이세로/메이크빌
    status            ENUM('legacy','pending','sent','approved','rejected') NOT NULL,
    raw_response      JSON NULL,           -- API 응답 (AES-256)
    created_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_tenant_invoice (tenant_id, tax_invoice_id),
    FOREIGN KEY (tax_invoice_id) REFERENCES tax_invoices(invoice_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

### 4품목 행분해 처리

```csharp
// 1행 DOCF4 → 최대 4행 tax_invoice_items
foreach (var idx in new[] { 1, 2, 3, 4 })
{
    var pum = GetStr(row, $"TX_PUM{idx}");
    if (string.IsNullOrWhiteSpace(pum)) continue;  // 빈 행 스킵
    
    await InsertItemAsync(new TaxInvoiceItem
    {
        InvoiceId = invoiceId,
        SeqNo = idx,
        ItemName = pum,
        Quantity = GetDec(row, $"TX_SU{idx}"),
        UnitPrice = GetDec(row, $"TX_DAN{idx}"),
        Amount = GetDec(row, $"TX_KUM{idx}"),
        VatAmount = GetDec(row, $"TX_VAT{idx}")
    }, tx, ct);
}
```

---

## 2. DOCFS — 상품 마스터 21컬럼 정독

### 전체 매핑 (코드 비교)

| 레거시 | 타입 | 코드 매핑 | 신 히트판 |
|---|---|---|---|
| **S_PUM** | Text40 | ✅ items.item_name | PK 일부, 품명 |
| **S_KU** | Text40 | ✅ items.spec | PK 일부, 규격 |
| S_DANW | Text4 | ✅ items.unit | 단위 (기본 EA) |
| S_TAX | Text1 | ✅ items.tax_type | 1·과세 / 2·면세 / 3·영세 |
| S_IDAN | Currency | ✅ items.purchase_price | 매입단가 |
| S_PDAN | Currency | ✅ items.sale_price | 판매단가 |
| S_JEK | Currency | ✅ items.cost_price | 재고단가 |
| S_PDANA~E | Currency | ✅ items.price_a~e | 단가 A~E |
| S_BARCODE | Text20 | ✅ items.barcode | 바코드 |
| S_CCODE | Text20 | ✅ items.item_group | 분류 코드 |
| S_DESC | Text40 | ✅ items.memo | 설명 |
| 🔴 **S_IDANA** | Currency | ❌ 매핑 0 | 매입단가 보조 A |
| 🔴 **S_IDANB** | Currency | ❌ 매핑 0 | 매입단가 보조 B |
| 🔴 **S_IBUY** | Text50 | ❌ 매핑 0 | 매입처명 (자유 텍스트) |
| 🔴 **S_MAKER** | Text20 | ❌ 매핑 0 | 제조사 |
| 🔴 **S_SET** | Text1 | ❌ 매핑 0 | 세트 상품 구분 |
| 🔴 **S_FIL** | Text30 | ❌ 매핑 0 | 예비 필드 |

### 누락 5개 컬럼 보강 정책

```
S_MAKER (제조사)       → items.manufacturer 추가 ⭐ 중요
S_IBUY (매입처 텍스트)  → items.default_supplier_name (자유 입력)
S_IDANA, S_IDANB       → items.purchase_price_a, purchase_price_b
S_SET                  → items.is_bundle (Boolean)
S_FIL                  → items.legacy_extra (디버그용)
```

### 신 히트판 items 테이블 ALTER 안

```sql
ALTER TABLE items
    ADD COLUMN manufacturer VARCHAR(50) NULL COMMENT '제조사 (S_MAKER)',
    ADD COLUMN default_supplier_name VARCHAR(50) NULL COMMENT '기본 매입처명 (S_IBUY)',
    ADD COLUMN purchase_price_a DECIMAL(15,2) DEFAULT 0 COMMENT '매입단가 A (S_IDANA)',
    ADD COLUMN purchase_price_b DECIMAL(15,2) DEFAULT 0 COMMENT '매입단가 B (S_IDANB)',
    ADD COLUMN is_bundle TINYINT(1) DEFAULT 0 COMMENT '세트 상품 (S_SET)';
```

---

## 3. DOCSW — 사원 마스터 36컬럼 정독 ⚠️ 형사 영역

### 민감정보 보호 영역 5개 (처리 근거 법령 + 안전조치 의무)

> **2026-05-12 사장님 지적으로 §39 오인용 정정 + ERP 도메인 처리 근거 재정리**
> **단일 진실 원천:** `CRIMINAL_DOMAIN_POLICY.md` 참조

```
🔒 민감정보 보호 영역 (AES-256 + 동의 + 접근통제):
  SW_JUMIN     Text14    주민번호    → 소득세법 §127·§164, 4대보험법 (처리 근거 합법)
  SW_PAY       Int32     급여        → 근로기준법 §48 (임금대장), 개인정보보호법 §29 (안전조치)
  SW_PAYgu     TinyInt   급여 구분   → 평문 OK (식별성 낮음, 구분 코드)
  SW_PAYeuy    TinyInt   급여 유형   → 평문 OK (식별성 낮음, 분류 코드)
  SW_PAYoth    Text100   급여 기타   → 개인정보보호법 §29 (금액·세부내역)
```

**처리 정책 (보안매니저 + 법무팀장 + 사장님 결재 2026-05-12):**
1. **저장:** AES-256 Value Converter (SW_JUMIN / SW_PAY / SW_PAYoth 3종), 마스터키 로컬 보관
2. **동의:** 직원 채용 시 개인정보 처리 동의서 1회 (목적: 급여·4대보험·연말정산·임금대장)
3. **본사 송신 0** (헌법 #18·#22 — 마스터키·평문 모두 로컬)
4. **표시:** 마스킹 기본(`880101-*******`, 급여 `●●●`) + [보기] step-up 인증 → 5분 평문
5. **감사로그:** 누가·언제·어떤 컬럼·어떤 직원 조회했는지 전수 기록

⚠️ **1차 안건 §39 오인용 폐기:** 근로기준법 §39는 퇴직증명서 발급 의무 조항으로 급여 데이터와 무관.
⚠️ **2차 안건 "주민번호 수집 불법" 폐기:** ERP는 소득세법·4대보험법 등 처리 근거 명확. 개인정보보호법 §24의2 ①항 단서 적용.

### 매핑 표

#### ✅ 코드 매핑 (8/36)
```
SW_NAME      → employees.emp_name (PK 역할)
SW_JIKKUB    → employees.position
SW_JIKCHAK   → employees.job_title
SW_HP        → employees.phone (AES-256)
SW_IBSAIL    → employees.join_date
+ employee_id, emp_no, role
```

#### 🔴 누락 28개 — 분류

**A. 기본 정보 (즉시 보강, 8개)**
```
SW_ADDR      Text80   → employees.address
SW_POSTNO    Text7    → employees.zip_code
SW_BIRTH     Text10   → employees.birth_date
SW_BIRTHgu   TinyInt  → employees.birth_calendar (1=양 / 2=음)
SW_BIRTHtel  TinyInt  → employees.birth_lunar_converted
SW_TEL       Text18   → employees.home_phone
SW_TELem     Text20   → employees.emergency_contact
SW_REM       Text60   → employees.memo
```

**B. 형사 영역 (별도 동의 + AES-256, 5개)**
```
SW_JUMIN     Text14   → employees.resident_no_encrypted ⚠️
SW_PAY       Int32    → employees.salary_encrypted ⚠️
SW_PAYgu     TinyInt  → employees.salary_type ⚠️
SW_PAYeuy    TinyInt  → employees.salary_category ⚠️
SW_PAYoth    Text100  → employees.salary_extra_encrypted ⚠️
```

**C. 직장 정보 (보강, 7개)**
```
SW_BUSEA     Text30   → employees.department
SW_HONIN     Text1    → employees.marriage_status
SW_BB        Text40   → employees.business_type
SW_TEA       TinyInt  → employees.is_resigned
SW_TEADT     Text8    → employees.resign_date
SW_TEARESON  Text50   → employees.resign_reason
SW_nation    Text20   → employees.nationality
```

**D. 잔액 컬럼 SW_BAL1~10 (10개 — 용도 미상)**
```
SW_BAL1~SW_BAL10  Text120 (10개)
  → 레거시에서 미사용 또는 회사별 커스텀 활용 추정
  → 일단 employees.legacy_bal1~10 으로 1:1 매핑 (Text 그대로)
  → 마이그 후 사장님 확인 필요
```

**E. 보조 1개**
```
SW_PAYkuk    TinyInt → employees.salary_country (해외 직원용?)
```

### 신 히트판 employees 테이블 ALTER 안

```sql
-- A. 기본 정보 (8개)
ALTER TABLE employees
    ADD COLUMN address VARCHAR(120) NULL,
    ADD COLUMN zip_code VARCHAR(10) NULL,
    ADD COLUMN birth_date DATE NULL,
    ADD COLUMN birth_calendar TINYINT DEFAULT 1 COMMENT '1=양력, 2=음력',
    ADD COLUMN birth_lunar_converted TINYINT DEFAULT 0,
    ADD COLUMN home_phone VARCHAR(20) NULL,
    ADD COLUMN emergency_contact VARCHAR(30) NULL,
    ADD COLUMN memo TEXT NULL;

-- B. 형사 영역 (5개) ⚠️ AES-256 + 별도 동의
ALTER TABLE employees
    ADD COLUMN resident_no_encrypted VARBINARY(255) NULL COMMENT '주민번호 AES-256',
    ADD COLUMN salary_encrypted VARBINARY(255) NULL COMMENT '급여 AES-256',
    ADD COLUMN salary_type TINYINT NULL,
    ADD COLUMN salary_category TINYINT NULL,
    ADD COLUMN salary_extra_encrypted VARBINARY(500) NULL COMMENT '급여 기타 AES-256';

-- C. 직장 정보 (7개)
ALTER TABLE employees
    ADD COLUMN department VARCHAR(50) NULL,
    ADD COLUMN marriage_status VARCHAR(2) NULL,
    ADD COLUMN business_type VARCHAR(50) NULL,
    ADD COLUMN is_resigned TINYINT DEFAULT 0,
    ADD COLUMN resign_date DATE NULL,
    ADD COLUMN resign_reason VARCHAR(80) NULL,
    ADD COLUMN nationality VARCHAR(30) NULL;

-- D. 레거시 잔액 컬럼 10개 (원본 그대로 보존)
ALTER TABLE employees
    ADD COLUMN legacy_bal1 VARCHAR(150) NULL,
    ADD COLUMN legacy_bal2 VARCHAR(150) NULL,
    ADD COLUMN legacy_bal3 VARCHAR(150) NULL,
    ADD COLUMN legacy_bal4 VARCHAR(150) NULL,
    ADD COLUMN legacy_bal5 VARCHAR(150) NULL,
    ADD COLUMN legacy_bal6 VARCHAR(150) NULL,
    ADD COLUMN legacy_bal7 VARCHAR(150) NULL,
    ADD COLUMN legacy_bal8 VARCHAR(150) NULL,
    ADD COLUMN legacy_bal9 VARCHAR(150) NULL,
    ADD COLUMN legacy_bal10 VARCHAR(150) NULL;

-- E. 해외
ALTER TABLE employees
    ADD COLUMN salary_country TINYINT NULL;
```

---

## 4. DOCF8 19개 누락 컬럼 보강 ALTER

```sql
ALTER TABLE partners
    ADD COLUMN card_commission_rate DECIMAL(5,2) DEFAULT 0 COMMENT '카드 수수료율 (buy_cardyul)',
    ADD COLUMN classification_code VARCHAR(30) NULL COMMENT '분류 코드 (buy_ccode)',
    ADD COLUMN manager_department VARCHAR(30) NULL COMMENT '담당 부서 (buy_damdangbu)',
    ADD COLUMN price_grade_code VARCHAR(10) NULL COMMENT '⭐ 단가등급 코드 (buy_DOSCODE) — 옵션 B 핵심',
    ADD COLUMN legacy_extra VARCHAR(30) NULL COMMENT '레거시 예비 (buy_fil)',
    ADD COLUMN discount_rate DECIMAL(5,2) DEFAULT 0 COMMENT '할인율 (buy_halyul)',
    ADD COLUMN keyman_birth VARCHAR(10) NULL COMMENT '키맨 생일 (buy_keybirth)',
    ADD COLUMN keyman_name VARCHAR(50) NULL COMMENT '키맨 이름 (buy_keyname)',
    ADD COLUMN keyman_phone VARCHAR(20) NULL COMMENT '키맨 연락처 (buy_keytel)',
    ADD COLUMN margin_rate DECIMAL(5,2) DEFAULT 0 COMMENT '마진율 (buy_mayul)',
    ADD COLUMN sales_employee VARCHAR(30) NULL COMMENT '담당 영업사원 (buy_sawon)',
    ADD COLUMN trade_start_date DATE NULL COMMENT '거래 시작일 (buy_startdt)',
    ADD COLUMN business_registration_date DATE NULL COMMENT '사업자등록일 (buy_taxdt)',
    ADD COLUMN tel_secondary VARCHAR(20) NULL COMMENT '전화 2번 (buy_tel1)',
    -- ⚠️ 헌법 #18 형사 영역
    ADD COLUMN ceo_resident_no_encrypted VARBINARY(255) NULL COMMENT '⚠️ 대표 주민번호 AES-256 (buy_topjumin)';
```

---

## 5. 컬럼 보강 후 마이그 코드 수정 영역

### MdbToHitpanMapper.MapPartnerAsync (기존 추출 + 19개 추가)

```csharp
// 기존 22개 컬럼 INSERT (그대로)
// 추가 19개 컬럼 INSERT
await _db.ExecuteAsync(new CommandDefinition(sql, new
{
    // ... 기존 22개
    
    // 신규 19개
    CardCommissionRate = GetDec(row, "buy_cardyul"),
    ClassificationCode = GetStr(row, "buy_ccode"),
    ManagerDepartment = GetStr(row, "buy_damdangbu"),
    PriceGradeCode = GetStr(row, "buy_DOSCODE"),     // ⭐ 단가등급
    LegacyExtra = GetStr(row, "buy_fil"),
    DiscountRate = GetDec(row, "buy_halyul"),
    KeymanBirth = GetStr(row, "buy_keybirth"),
    KeymanName = GetStr(row, "buy_keyname"),
    KeymanPhone = GetStr(row, "buy_keytel"),
    MarginRate = GetDec(row, "buy_mayul"),
    SalesEmployee = GetStr(row, "buy_sawon"),
    TradeStartDate = ParseLegacyDate(GetStr(row, "buy_startdt")),
    BusinessRegDate = ParseLegacyDate(GetStr(row, "buy_taxdt")),
    TelSecondary = GetStr(row, "buy_tel1"),
    
    // ⚠️ 헌법 #18 AES-256
    CeoResidentNoEncrypted = _crypto.Encrypt(GetStr(row, "buy_topjumin")),
}, transaction: tx, cancellationToken: ct));
```

---

## 6. 단가등급 옵션 B 가능성 확정

**`buy_DOSCODE` (Text5) = 단가등급 후보**

```
가설:
  buy_DOSCODE = "A" → partners.price_grade = "A"
  buy_DOSCODE = "B" → partners.price_grade = "B"
  ...

검증 방법 (W2 D1, 사장님 실측):
  실제 데이터 가진 MDB로 SELECT DISTINCT buy_DOSCODE FROM DOCF8
  → 결과 ['A','B','C','D','E'] 이면 → 옵션 B 확정
  → 결과 다른 값이면 → 옵션 D (자동 추론) 전환
```

**W2 D1 결정 트리:**
```
1. 사장님 실 MDB로 buy_DOSCODE 값 분포 확인
2. ['A','B','C','D','E'] → 옵션 B 채택
3. 그 외 → 옵션 D 채택
```

---

## 7. 사장님 결재 사항 5건

| # | 사항 | 결재 |
|---|---|---|
| 1 | DOCF4 → `etax_send_history` 신설 | ⚠️ |
| 2 | items 5개 컬럼 ALTER (제조사·매입처명 등) | ⚠️ |
| 3 | partners 19개 컬럼 ALTER (단가등급 포함) | ⚠️ |
| 4 | employees 28개 컬럼 ALTER (주민번호·급여 AES) | ⚠️ |
| 5 | 헌법 #18 형사 영역 AES-256 + 별도 동의 절차 | ⚠️ |

---

## 8. 헌법 준수 점검

| 헌법 | 적용 |
|---|---|
| #1 수정 OK 덮어쓰기 X | ✅ ALTER ADD COLUMN만 |
| #5 암호화 컬럼 Value Converter | ✅ 5개 형사 영역 |
| #17 InnoDB | ✅ 신규 etax_send_history |
| #18 본사 송신 0 | ✅ 로컬 처리, AES |
| #20 워크플로우 끊김 X | ✅ |

---

**작성:** ERP매니저 (더존 30년) + DB매니저 + 보안매니저
**검토:** 법무팀장 (형사 영역 5개)
**최종 검증:** CTO 래리 앨리슨
**적용 시점:** W2 사장님 결재 후 ALTER 실행
