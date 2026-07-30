# 작업지시서 20260513작9 — partners 19개 컬럼 ALTER

> **발행:** 2026-05-12 야간 (W2 D2 사전 발행)
> **담당:** DB개발자 (Phase 1) + 보안개발자 (형사영역 1개)
> **헌법:** #1 (ADD COLUMN만), #5 (AES-256), #17 (InnoDB), #19 (errors 0 + warnings 0), #20 (멱등)
> **선행 결재:** 사장님 결재 #3 (ALTER 52개) 2026-05-12 완료
> **참조 설계서:** [ALTER_52_COLUMNS.md](../migration/ALTER_52_COLUMNS.md) §2.1

---

## 1. 작업 목적

DOCF8 (레거시 거래처 마스터) 41개 컬럼 중 누락된 19개 컬럼을 `partners` 테이블에 추가.
- 일반 18개 (단가등급·할인율·마진율·키맨 등)
- 형사 1개 (`ceo_resident_no_encrypted` — 대표 주민번호 AES-256)

---

## 2. 사전 확인 (DB개발자 의무)

```sql
-- 1. ENGINE·COLLATION 확인 (2026-05-12 PowerShell 결과)
SELECT TABLE_NAME, ENGINE, TABLE_COLLATION
FROM information_schema.TABLES
WHERE TABLE_SCHEMA='hitpan_erp' AND TABLE_NAME='partners';
-- 결과: InnoDB + utf8mb4_unicode_ci ✅

-- 2. 운영 데이터 확인 (락 영향)
SELECT COUNT(*) FROM partners;
-- 결과: 0건 = 락 없음

-- 3. 기존 컬럼 확인 (중복 방지)
DESCRIBE partners;
```

---

## 3. ALTER 실행 SQL (멱등 안전 모드)

```sql
ALTER TABLE partners
    ADD COLUMN IF NOT EXISTS card_commission_rate DECIMAL(5,2) DEFAULT 0
        COMMENT '카드 수수료율 (buy_cardyul)',
    ADD COLUMN IF NOT EXISTS classification_code VARCHAR(30) NULL
        COMMENT '분류 코드 (buy_ccode)',
    ADD COLUMN IF NOT EXISTS manager_department VARCHAR(30) NULL
        COMMENT '담당 부서 (buy_damdangbu)',
    ADD COLUMN IF NOT EXISTS price_grade_code VARCHAR(10) NULL
        COMMENT '단가등급 코드 (buy_DOSCODE) — 옵션 H 원본 보존',
    ADD COLUMN IF NOT EXISTS price_grade TINYINT DEFAULT 1
        COMMENT '단가등급 1~5 (옵션 H 결정 결과)',
    ADD COLUMN IF NOT EXISTS legacy_extra VARCHAR(30) NULL
        COMMENT '레거시 예비 (buy_fil)',
    ADD COLUMN IF NOT EXISTS discount_rate DECIMAL(5,2) DEFAULT 0
        COMMENT '할인율 (buy_halyul)',
    ADD COLUMN IF NOT EXISTS keyman_birth VARCHAR(10) NULL
        COMMENT '키맨 생일 (buy_keybirth)',
    ADD COLUMN IF NOT EXISTS keyman_name VARCHAR(50) NULL
        COMMENT '키맨 이름 (buy_keyname)',
    ADD COLUMN IF NOT EXISTS keyman_phone VARCHAR(20) NULL
        COMMENT '키맨 연락처 (buy_keytel)',
    ADD COLUMN IF NOT EXISTS margin_rate DECIMAL(5,2) DEFAULT 0
        COMMENT '마진율 (buy_mayul)',
    ADD COLUMN IF NOT EXISTS sales_employee VARCHAR(30) NULL
        COMMENT '담당 영업사원 (buy_sawon)',
    ADD COLUMN IF NOT EXISTS trade_start_date DATE NULL
        COMMENT '거래 시작일 (buy_startdt)',
    ADD COLUMN IF NOT EXISTS business_registration_date DATE NULL
        COMMENT '사업자등록일 (buy_taxdt)',
    ADD COLUMN IF NOT EXISTS tel_secondary VARCHAR(20) NULL
        COMMENT '전화 2번 (buy_tel1)',
    ADD COLUMN IF NOT EXISTS tax_classification VARCHAR(10) NULL
        COMMENT '과세 구분 (buy_taxgubun)',
    ADD COLUMN IF NOT EXISTS ceo_name VARCHAR(50) NULL
        COMMENT '대표명 (buy_top)',
    ADD COLUMN IF NOT EXISTS partner_type VARCHAR(10) NULL
        COMMENT '거래처 분류 (buy_gu)',
    -- 형사 영역 (보안개발자 담당, 결재 #4 정책)
    ADD COLUMN IF NOT EXISTS ceo_resident_no_encrypted VARBINARY(255) NULL
        COMMENT '대표 주민번호 AES-256 (buy_topjumin, 부가가치세법 §32)';
```

---

## 4. 인덱스 추가

```sql
CREATE INDEX IF NOT EXISTS idx_partners_price_grade
    ON partners (tenant_id, price_grade);

CREATE INDEX IF NOT EXISTS idx_partners_sales_emp
    ON partners (tenant_id, sales_employee);
```

---

## 5. 검증 (DB개발자 의무)

```sql
-- 1. 19개 컬럼 추가 확인
SELECT COUNT(*) AS added_columns
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA='hitpan_erp' AND TABLE_NAME='partners'
  AND COLUMN_NAME IN (
    'card_commission_rate','classification_code','manager_department',
    'price_grade_code','price_grade','legacy_extra','discount_rate',
    'keyman_birth','keyman_name','keyman_phone','margin_rate',
    'sales_employee','trade_start_date','business_registration_date',
    'tel_secondary','tax_classification','ceo_name','partner_type',
    'ceo_resident_no_encrypted'
  );
-- 기대: 19

-- 2. VARBINARY 컬럼 확인
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA='hitpan_erp' AND TABLE_NAME='partners'
  AND COLUMN_NAME='ceo_resident_no_encrypted';
-- 기대: varbinary, 255

-- 3. 인덱스 확인
SHOW INDEX FROM partners WHERE Key_name IN ('idx_partners_price_grade','idx_partners_sales_emp');
-- 기대: 2건

-- 4. ENGINE 유지 확인 (헌법 #17)
SELECT ENGINE FROM information_schema.TABLES
WHERE TABLE_SCHEMA='hitpan_erp' AND TABLE_NAME='partners';
-- 기대: InnoDB
```

---

## 6. 롤백 절차 (사고 시)

⚠️ **운영 데이터 0건 가정 = 안전. 컬럼 추가 후 데이터 들어가면 롤백 불가.**

```sql
-- 5분 이내 사고 발생 시만 사용
ALTER TABLE partners
    DROP COLUMN IF EXISTS card_commission_rate,
    DROP COLUMN IF EXISTS classification_code,
    -- ... (19개 전체)
    DROP COLUMN IF EXISTS ceo_resident_no_encrypted;
```

---

## 7. 후속 작업

- 작9-1: Value Converter 인터페이스 명세 (`docs/migration/VALUE_CONVERTER_SPEC.md`) — 보안개발자 담당
- 작9-2: MdbToHitpanMapper.MapPartnerAsync 추출 + 19개 INSERT 추가 — 백엔드개발자 담당 (W2 D3)

---

## 8. 헌법 부합 매트릭스

| 헌법 | 적용 |
|---|---|
| #1 ADD COLUMN만 (DROP/MODIFY 0) | ✅ |
| #5 ceo_resident_no_encrypted = VARBINARY + Value Converter | ✅ |
| #17 InnoDB 유지 | ✅ |
| #19 errors 0 + warnings 0 | ✅ DEFAULT 명시 |
| #20 멱등 (IF NOT EXISTS) | ✅ |
| #22 본사 송신 0 | ✅ 로컬 |

---

## 9. 5중 검증 체크리스트 (헌법 #23)

- [ ] ① 작업지시서 보안 요구사항 명시 ✅ (본 문서)
- [ ] ② 매니저 리뷰 (DB·보안·설계팀장) — DB개발자 PR 후
- [ ] ③ 정적 분석 SAST (Roslyn·CodeQL) — 코드 추출 단계 (W2 D3)
- [ ] ④ 동적 분석 DAST (OWASP ZAP) — 베타 전 1회
- [ ] ⑤ 데이터 최소주의 검증 — 본사 송신 X 확인 완료

---

**발행:** PM 닥터스트레인지
**검토:** DB매니저, 보안매니저, 설계팀장 브라운킴
**실행자:** DB개발자 (W2 D2 아침)
**예상 소요:** 15분 (운영 데이터 0건)
**결재:** 사장님 2026-05-12
