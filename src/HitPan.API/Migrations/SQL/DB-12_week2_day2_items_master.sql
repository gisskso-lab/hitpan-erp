-- WEEK 2 Day 2 — 상품(items) 마스터 컬럼 보강 + item_special_prices + item_groups
-- 선행: DB-06 items 소프트삭제 컬럼 등

ALTER TABLE items
  ADD COLUMN IF NOT EXISTS item_code VARCHAR(30) NULL,
  ADD COLUMN IF NOT EXISTS item_group VARCHAR(50) NULL,
  ADD COLUMN IF NOT EXISTS item_type VARCHAR(20) NOT NULL DEFAULT 'product',
  ADD COLUMN IF NOT EXISTS unit VARCHAR(20) NOT NULL DEFAULT 'EA',
  ADD COLUMN IF NOT EXISTS spec VARCHAR(100) NULL,
  ADD COLUMN IF NOT EXISTS purchase_price DECIMAL(15,2) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS sale_price DECIMAL(15,2) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS standard_price DECIMAL(15,2) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS tax_type VARCHAR(20) NOT NULL DEFAULT 'taxable',
  ADD COLUMN IF NOT EXISTS safety_stock DECIMAL(10,2) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS barcode VARCHAR(50) NULL,
  ADD COLUMN IF NOT EXISTS memo TEXT NULL,
  ADD COLUMN IF NOT EXISTS row_version INT NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS item_special_prices (
  price_id    VARCHAR(36)   NOT NULL PRIMARY KEY,
  tenant_id   VARCHAR(36)   NOT NULL,
  item_id     VARCHAR(36)   NOT NULL,
  partner_id  VARCHAR(36)   NOT NULL,
  price_type  VARCHAR(20)   NOT NULL DEFAULT 'fixed',
  unit_price  DECIMAL(15,2) NOT NULL,
  start_date  DATE          NULL,
  end_date    DATE          NULL,
  is_active   TINYINT(1)    NOT NULL DEFAULT 1,
  created_at  DATETIME(6)   NOT NULL DEFAULT NOW(6),
  updated_at  DATETIME(6)   NOT NULL DEFAULT NOW(6) ON UPDATE NOW(6),
  UNIQUE KEY uk_item_partner (tenant_id, item_id, partner_id),
  INDEX idx_tenant (tenant_id),
  INDEX idx_item (tenant_id, item_id),
  INDEX idx_partner (tenant_id, partner_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS item_groups (
  group_id   VARCHAR(36)   NOT NULL PRIMARY KEY,
  tenant_id  VARCHAR(36)   NOT NULL,
  group_name VARCHAR(50)   NOT NULL,
  sort_order INT           NOT NULL DEFAULT 0,
  is_active  TINYINT(1)    NOT NULL DEFAULT 1,
  created_at DATETIME(6)  NOT NULL DEFAULT NOW(6),
  UNIQUE KEY uk_tenant_group (tenant_id, group_name),
  INDEX idx_tenant (tenant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
