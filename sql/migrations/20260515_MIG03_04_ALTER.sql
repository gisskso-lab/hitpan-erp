-- =====================================================================
-- 2026-05-15 PM 세션 — 진범 #3 + 1분 절대 잔여 3종 ALTER
-- =====================================================================
-- 작성: PM 브라운킴 (2026-05-15)
-- 실행 권한: 사장님 결재 + 본부장 입회 (헌법 #29 정합) — PM 단독 실행 절대 금지
-- 대상: demo.hitpan.kr (5/16 13:00 풀스택 가동 시점)
-- 선행: WS-20260515-MIG-03, WS-20260515-MIG-04 작지서
-- 영향: 컬럼 추가 + UNIQUE 인덱스 + tax_invoice_items 신규 (기존 row 영향 0)
-- =====================================================================

-- ---------------------------------------------------------------------
-- 1. tax_invoices (진범 #3) — delivery_id NOT NULL 해제 + 10컬럼 추가
-- ---------------------------------------------------------------------

-- 1-1. delivery_id NOT NULL 해제 (레거시 마이그 호환)
--      기존 운영 row는 이미 채워져 있으므로 영향 0
ALTER TABLE tax_invoices
  MODIFY delivery_id VARCHAR(36) NULL;

-- 1-2. delivery_id UNIQUE 인덱스 DROP (마이그 row가 NULL 다수일 수 있어 충돌 회피)
ALTER TABLE tax_invoices
  DROP INDEX `delivery_id`;

-- 1-3. TX_* 레거시 매핑 컬럼 추가
ALTER TABLE tax_invoices
  ADD COLUMN direction CHAR(1) NULL COMMENT 'S=매출, B=매입 (TX_IO)',
  ADD COLUMN tax_no CHAR(8) NULL COMMENT 'TX_NO 세금계산서 번호',
  ADD COLUMN issue_date_yyyymmdd CHAR(8) NULL COMMENT 'TX_PDT 발행일',
  ADD COLUMN partner_code INT NULL COMMENT 'TX_BUY 레거시 거래처 코드',
  ADD COLUMN seq_no SMALLINT NULL COMMENT 'TX_SEQ 순번',
  ADD COLUMN sent_at_yyyymmdd CHAR(8) NULL COMMENT 'TX_SENDDT 발송일',
  ADD COLUMN read_at_yyyymmdd CHAR(8) NULL COMMENT 'TX_READDT 확인일',
  ADD COLUMN reported_at_yyyymmdd CHAR(8) NULL COMMENT 'TX_REPORTDT 신고일',
  ADD COLUMN remark1 VARCHAR(100) NULL COMMENT 'TX_REM 비고1',
  ADD COLUMN remark2 VARCHAR(100) NULL COMMENT 'TX_REM1 비고2';

-- 1-4. 멱등 키 + 워크플로우 추적 컬럼
ALTER TABLE tax_invoices
  ADD COLUMN source_type VARCHAR(30) NULL COMMENT '멱등 추적 (migration|api|ui)',
  ADD COLUMN source_id VARCHAR(80) NULL COMMENT '레거시 PK 조합 (TX_IO+TX_NO)',
  ADD COLUMN migrated_source_hash VARCHAR(64) NULL COMMENT 'SHA-256 무결성 해시';

-- 1-5. UNIQUE 인덱스 (TX_IO+TX_NO 정답 PK 기반)
ALTER TABLE tax_invoices
  ADD UNIQUE KEY uq_tax_invoices_io_no (tenant_id, direction, tax_no),
  ADD UNIQUE KEY uq_tax_invoices_source (tenant_id, source_type, source_id);

-- 1-6. tax_invoice_items 신규 (4 품목 인라인 분해)
CREATE TABLE IF NOT EXISTS tax_invoice_items (
  tax_invoice_item_id VARCHAR(36) PRIMARY KEY,
  invoice_id VARCHAR(36) NOT NULL,
  tenant_id VARCHAR(36) NOT NULL,
  line_no SMALLINT NOT NULL COMMENT '1~4 (TX_PUM1~4 분해)',
  item_name VARCHAR(100) NOT NULL,
  quantity DECIMAL(15,2) NOT NULL DEFAULT 0,
  unit_price DECIMAL(15,2) NOT NULL DEFAULT 0,
  supply_amount DECIMAL(15,2) NOT NULL DEFAULT 0,
  vat_amount DECIMAL(15,2) NOT NULL DEFAULT 0,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_tax_invoice_item_line (invoice_id, line_no),
  KEY idx_tax_invoice_items_tenant (tenant_id),
  CONSTRAINT fk_tax_invoice_items_invoice
    FOREIGN KEY (invoice_id) REFERENCES tax_invoices(invoice_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
-- 헌법 #17 정합: 신규 테이블은 ENGINE=InnoDB 명시

-- ---------------------------------------------------------------------
-- 2. bank_transactions (1분 절대 #1) — 멱등 컬럼 + UNIQUE
-- ---------------------------------------------------------------------

ALTER TABLE bank_transactions
  ADD COLUMN source_type VARCHAR(30) NULL COMMENT '멱등 추적 (migration|api)',
  ADD COLUMN source_id VARCHAR(80) NULL COMMENT '레거시 PK (BK_NO+BK_YMD+BK_JWASU+BK_JEN)',
  ADD COLUMN migrated_source_hash VARCHAR(64) NULL COMMENT 'SHA-256 무결성 해시',
  ADD UNIQUE KEY uq_bank_tx_source (tenant_id, source_type, source_id);

-- ---------------------------------------------------------------------
-- 3. cashbook (1분 절대 #2) — 멱등 컬럼 + UNIQUE
-- ---------------------------------------------------------------------

ALTER TABLE cashbook
  ADD COLUMN source_type VARCHAR(30) NULL COMMENT '멱등 추적 (migration|api)',
  ADD COLUMN source_id VARCHAR(80) NULL COMMENT '레거시 PK (AC_YMD+AC_JWASU+AC_JEN)',
  ADD COLUMN migrated_source_hash VARCHAR(64) NULL COMMENT 'SHA-256 무결성 해시',
  ADD UNIQUE KEY uq_cashbook_source (tenant_id, source_type, source_id);

-- ---------------------------------------------------------------------
-- 4. expenses (1분 절대 #3) — 멱등 컬럼 + UNIQUE
-- ---------------------------------------------------------------------

ALTER TABLE expenses
  ADD COLUMN source_type VARCHAR(30) NULL COMMENT '멱등 추적 (migration|api)',
  ADD COLUMN source_id VARCHAR(80) NULL COMMENT '레거시 PK (SC_KCODE+SC_DT+SC_SAWON+SC_SUN)',
  ADD COLUMN migrated_source_hash VARCHAR(64) NULL COMMENT 'SHA-256 무결성 해시',
  ADD UNIQUE KEY uq_expenses_source (tenant_id, source_type, source_id);

-- =====================================================================
-- 검증 SQL (실행 후 즉시 확인)
-- =====================================================================

-- 검증 1: 컬럼 추가 확인
SHOW COLUMNS FROM tax_invoices LIKE 'direction';
SHOW COLUMNS FROM bank_transactions LIKE 'source_id';
SHOW COLUMNS FROM cashbook LIKE 'source_id';
SHOW COLUMNS FROM expenses LIKE 'source_id';

-- 검증 2: tax_invoice_items 신규 확인
SHOW CREATE TABLE tax_invoice_items;

-- 검증 3: UNIQUE 인덱스 확인
SELECT table_name, index_name, column_name
FROM information_schema.statistics
WHERE table_schema = DATABASE()
  AND index_name LIKE 'uq_%_source'
ORDER BY table_name, seq_in_index;

-- 검증 4: delivery_id NULL 허용 확인
SHOW COLUMNS FROM tax_invoices LIKE 'delivery_id';
-- Null = YES 기대

-- =====================================================================
-- 롤백 SQL (만약 문제 발생 시 사장님 결재 후 실행)
-- =====================================================================
-- DROP TABLE IF EXISTS tax_invoice_items;
-- ALTER TABLE tax_invoices DROP INDEX uq_tax_invoices_source;
-- ALTER TABLE tax_invoices DROP INDEX uq_tax_invoices_io_no;
-- ALTER TABLE tax_invoices
--   DROP COLUMN migrated_source_hash, DROP COLUMN source_id, DROP COLUMN source_type,
--   DROP COLUMN remark2, DROP COLUMN remark1,
--   DROP COLUMN reported_at_yyyymmdd, DROP COLUMN read_at_yyyymmdd, DROP COLUMN sent_at_yyyymmdd,
--   DROP COLUMN seq_no, DROP COLUMN partner_code, DROP COLUMN issue_date_yyyymmdd,
--   DROP COLUMN tax_no, DROP COLUMN direction;
-- ALTER TABLE tax_invoices ADD UNIQUE KEY `delivery_id` (delivery_id);
-- ALTER TABLE tax_invoices MODIFY delivery_id VARCHAR(36) NOT NULL;
-- ALTER TABLE bank_transactions DROP INDEX uq_bank_tx_source,
--   DROP COLUMN migrated_source_hash, DROP COLUMN source_id, DROP COLUMN source_type;
-- ALTER TABLE cashbook DROP INDEX uq_cashbook_source,
--   DROP COLUMN migrated_source_hash, DROP COLUMN source_id, DROP COLUMN source_type;
-- ALTER TABLE expenses DROP INDEX uq_expenses_source,
--   DROP COLUMN migrated_source_hash, DROP COLUMN source_id, DROP COLUMN source_type;

-- =====================================================================
-- 작성: PM 브라운킴 2026-05-15 (사장님 식사 중 PM 단독 작성)
-- 실행: 사장님 결재 + 본부장 입회 (5/16 13:00 풀스택 가동 시점)
-- 문서 ID: SQL-MIG-03-04-20260515
-- =====================================================================
