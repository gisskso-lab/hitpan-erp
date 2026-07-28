-- WEEK 1 Day 2 — 핵심 인덱스 + 소프트 삭제
--
-- 선행: ERROR 1300(깨진 COLUMN COMMENT) 시 `DB-07_fix_column_comments_utf8.sql` 먼저 실행.
-- InnoDB: 서버가 읽는 my.ini 에서 skip-innodb 제거 후 MariaDB 서비스 재시작(관리자 권한).
--   이 PC 기본 설정 파일 예: C:\MariaDB\mariadb-10.0.17-win32\my.ini
-- 검증: InnoDB DEFAULT + default_storage_engine=InnoDB 환경에서 인덱스·is_deleted 적용 확인됨.
--
-- 스키마 정합:
--   sales_deliveries: delivery_date (컬럼명 order_date 아님), status 가 longtext 인 경우 접두 인덱스
--   purchase_orders: po_date
--   stock_ledger: ledger_date (created_at 없음)
--   quotation_items: FK 컬럼명 quote_id (quotation_id 아님). 기존 idx_qi_quote 가 있으면 IF NOT EXISTS 로 스킵
--
-- 권장: InnoDB 활성 MariaDB/MySQL. (InnoDB 비활성 + MyISAM + 손상된 COLUMN COMMENT 환경에서는
--       일부 테이블에서 ERROR 1300 이 날 수 있음 — 주석/엔진 정비 후 재실행)
--
-- 날짜 인덱스: MyISAM/특수 빌드 호환을 위해 DESC 생략(필요 시 InnoDB에서 DESC 추가 가능)

ALTER TABLE sales_deliveries DROP INDEX IF EXISTS idx_test_w1d2;

-- STEP 1 — 인덱스
ALTER TABLE sales_deliveries
  ADD INDEX IF NOT EXISTS idx_tenant_date (tenant_id, delivery_date),
  ADD INDEX IF NOT EXISTS idx_tenant_partner (tenant_id, partner_id),
  ADD INDEX IF NOT EXISTS idx_tenant_status (tenant_id, status(32));

ALTER TABLE sales_delivery_items
  ADD INDEX IF NOT EXISTS idx_delivery (delivery_id);

ALTER TABLE quotations
  ADD INDEX IF NOT EXISTS idx_tenant_date (tenant_id, quote_date),
  ADD INDEX IF NOT EXISTS idx_tenant_partner (tenant_id, partner_id),
  ADD INDEX IF NOT EXISTS idx_tenant_status (tenant_id, status);

ALTER TABLE quotation_items
  ADD INDEX IF NOT EXISTS idx_quotation (quote_id);

ALTER TABLE purchase_orders
  ADD INDEX IF NOT EXISTS idx_tenant_date (tenant_id, po_date),
  ADD INDEX IF NOT EXISTS idx_tenant_partner (tenant_id, partner_id),
  ADD INDEX IF NOT EXISTS idx_tenant_status (tenant_id, status);

ALTER TABLE purchase_order_items
  ADD INDEX IF NOT EXISTS idx_po (po_id);

ALTER TABLE sales_orders
  ADD INDEX IF NOT EXISTS idx_tenant_date (tenant_id, order_date),
  ADD INDEX IF NOT EXISTS idx_tenant_partner (tenant_id, partner_id),
  ADD INDEX IF NOT EXISTS idx_tenant_status (tenant_id, status);

ALTER TABLE partners
  ADD INDEX IF NOT EXISTS idx_tenant_name (tenant_id, partner_name),
  ADD INDEX IF NOT EXISTS idx_tenant_active (tenant_id, is_active);

ALTER TABLE items
  ADD INDEX IF NOT EXISTS idx_tenant_name (tenant_id, item_name),
  ADD INDEX IF NOT EXISTS idx_tenant_active (tenant_id, is_active);

ALTER TABLE stock_ledger
  ADD INDEX IF NOT EXISTS idx_tenant_item_date (tenant_id, item_id, ledger_date),
  ADD INDEX IF NOT EXISTS idx_tenant_date (tenant_id, ledger_date);

ALTER TABLE users
  ADD INDEX IF NOT EXISTS idx_tenant_active (tenant_id, is_active),
  ADD INDEX IF NOT EXISTS idx_email (email);

-- STEP 2 — 소프트 삭제
ALTER TABLE partners
  ADD COLUMN IF NOT EXISTS is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS deleted_at DATETIME(6) NULL;

ALTER TABLE items
  ADD COLUMN IF NOT EXISTS is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS deleted_at DATETIME(6) NULL;

ALTER TABLE quotations
  ADD COLUMN IF NOT EXISTS is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS deleted_at DATETIME(6) NULL;

ALTER TABLE purchase_orders
  ADD COLUMN IF NOT EXISTS is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS deleted_at DATETIME(6) NULL;

ALTER TABLE sales_orders
  ADD COLUMN IF NOT EXISTS is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS deleted_at DATETIME(6) NULL;

ALTER TABLE sales_deliveries
  ADD COLUMN IF NOT EXISTS is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS deleted_at DATETIME(6) NULL;

ALTER TABLE users
  ADD COLUMN IF NOT EXISTS is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS deleted_at DATETIME(6) NULL;
