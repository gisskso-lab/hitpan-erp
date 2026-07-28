-- STEP: 안전장치 DB — 강제수정·재고 강제조정 이력, tenant_settings
-- ADD COLUMN IF NOT EXISTS 는 DB-08과 동일 서버(MariaDB / 최신 MySQL) 전제

CREATE TABLE IF NOT EXISTS force_edit_logs (
  log_id        VARCHAR(36)   NOT NULL PRIMARY KEY,
  tenant_id     VARCHAR(36)   NOT NULL,
  user_id       VARCHAR(36)   NOT NULL,
  table_name    VARCHAR(50)   NOT NULL,
  record_id     VARCHAR(36)   NOT NULL,
  field_name    VARCHAR(50)   NOT NULL,
  before_value  TEXT          NULL,
  after_value   TEXT          NULL,
  reason        VARCHAR(200)  NULL,
  ip_address    VARCHAR(50)   NULL,
  created_at    DATETIME(6)   NOT NULL
                DEFAULT NOW(6),
  INDEX idx_tenant  (tenant_id),
  INDEX idx_record  (table_name, record_id),
  INDEX idx_created (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS stock_adjust_logs (
  adjust_id     VARCHAR(36)   NOT NULL PRIMARY KEY,
  tenant_id     VARCHAR(36)   NOT NULL,
  item_id       VARCHAR(36)   NOT NULL,
  warehouse_id  VARCHAR(36)   NOT NULL,
  before_qty    DECIMAL(10,2) NOT NULL,
  after_qty     DECIMAL(10,2) NOT NULL,
  adjust_qty    DECIMAL(10,2) NOT NULL,
  before_cost   DECIMAL(15,2) NOT NULL,
  after_cost    DECIMAL(15,2) NOT NULL,
  reason        VARCHAR(200)  NULL,
  user_id       VARCHAR(36)   NOT NULL,
  created_at    DATETIME(6)   NOT NULL
                DEFAULT NOW(6),
  INDEX idx_tenant (tenant_id),
  INDEX idx_item   (tenant_id, item_id),
  INDEX idx_created (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS tenant_settings (
  tenant_id VARCHAR(36) NOT NULL PRIMARY KEY
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE tenant_settings
  ADD COLUMN IF NOT EXISTS
    allow_force_price_input TINYINT(1)
      NOT NULL DEFAULT 1,
  ADD COLUMN IF NOT EXISTS
    allow_force_vat_input TINYINT(1)
      NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS
    allow_zero_price TINYINT(1)
      NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS
    allow_past_edit TINYINT(1)
      NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS
    past_edit_password_hash
      VARCHAR(100) NULL,
  ADD COLUMN IF NOT EXISTS
    allow_force_stock_adjust TINYINT(1)
      NOT NULL DEFAULT 1,
  ADD COLUMN IF NOT EXISTS
    allow_credit_override TINYINT(1)
      NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS
    price_deviation_limit INT
      NOT NULL DEFAULT 50,
  ADD COLUMN IF NOT EXISTS
    force_edit_require_password TINYINT(1)
      NOT NULL DEFAULT 1;
