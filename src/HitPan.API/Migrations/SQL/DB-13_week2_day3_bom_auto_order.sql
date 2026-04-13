-- WEEK 2 Day 3 — BOM + 반자동발주 기반

CREATE TABLE IF NOT EXISTS bom_headers (
  bom_id           VARCHAR(36)  NOT NULL PRIMARY KEY,
  tenant_id        VARCHAR(36)  NOT NULL,
  product_item_id  VARCHAR(36)  NOT NULL,
  bom_name         VARCHAR(100) NOT NULL,
  bom_version      INT          NOT NULL DEFAULT 1,
  is_default       TINYINT(1)   NOT NULL DEFAULT 1,
  is_active        TINYINT(1)   NOT NULL DEFAULT 1,
  memo             TEXT         NULL,
  created_at       DATETIME(6)  NOT NULL DEFAULT NOW(6),
  updated_at       DATETIME(6)  NOT NULL DEFAULT NOW(6),
  INDEX idx_tenant (tenant_id),
  INDEX idx_product (tenant_id, product_item_id),
  INDEX idx_tenant_active (tenant_id, is_active)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS bom_items (
  bom_item_id      VARCHAR(36)   NOT NULL PRIMARY KEY,
  bom_id           VARCHAR(36)   NOT NULL,
  tenant_id        VARCHAR(36)   NOT NULL,
  seq_no           INT           NOT NULL DEFAULT 1,
  material_item_id VARCHAR(36)   NOT NULL,
  qty              DECIMAL(10,2) NOT NULL DEFAULT 1,
  unit             VARCHAR(20)   NOT NULL DEFAULT 'EA',
  loss_rate        DECIMAL(5,2)  NOT NULL DEFAULT 0,
  memo             VARCHAR(200)  NULL,
  INDEX idx_bom (bom_id),
  INDEX idx_tenant (tenant_id),
  INDEX idx_material (tenant_id, material_item_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS stock_alerts (
  alert_id      VARCHAR(36)   NOT NULL PRIMARY KEY,
  tenant_id     VARCHAR(36)   NOT NULL,
  item_id       VARCHAR(36)   NOT NULL,
  alert_type    VARCHAR(20)   NOT NULL,
  current_qty   DECIMAL(10,2) NOT NULL DEFAULT 0,
  safety_qty    DECIMAL(10,2) NOT NULL DEFAULT 0,
  shortage_qty  DECIMAL(10,2) NOT NULL DEFAULT 0,
  partner_id    VARCHAR(36)   NULL,
  order_qty     DECIMAL(10,2) NOT NULL DEFAULT 0,
  bom_id        VARCHAR(36)   NULL,
  status        VARCHAR(20)   NOT NULL DEFAULT 'pending',
  created_at    DATETIME(6)   NOT NULL DEFAULT NOW(6),
  updated_at    DATETIME(6)   NOT NULL DEFAULT NOW(6),
  INDEX idx_tenant (tenant_id),
  INDEX idx_item (tenant_id, item_id),
  INDEX idx_status (tenant_id, status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE items
  ADD COLUMN IF NOT EXISTS auto_order_enabled TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS auto_order_partner_id VARCHAR(36) NULL,
  ADD COLUMN IF NOT EXISTS auto_order_qty DECIMAL(10,2) NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS bom_cost_cache (
  cache_id           VARCHAR(36)   NOT NULL PRIMARY KEY,
  tenant_id          VARCHAR(36)   NOT NULL,
  product_item_id    VARCHAR(36)   NOT NULL,
  calculated_cost    DECIMAL(15,2) NOT NULL DEFAULT 0,
  material_count     INT           NOT NULL DEFAULT 0,
  is_dirty           TINYINT(1)    NOT NULL DEFAULT 0,
  last_calculated_at DATETIME(6)   NOT NULL DEFAULT NOW(6),
  UNIQUE KEY uk_tenant_product (tenant_id, product_item_id),
  INDEX idx_tenant (tenant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
