-- WEEK 2 Day 4 — 사용자 권한 + 회사정보 보강

CREATE TABLE IF NOT EXISTS user_permissions (
  perm_id     VARCHAR(36)  NOT NULL PRIMARY KEY,
  tenant_id   VARCHAR(36)  NOT NULL,
  user_id     VARCHAR(36)  NOT NULL,
  menu_code   VARCHAR(30)  NOT NULL,
  can_view    TINYINT(1)   NOT NULL DEFAULT 0,
  can_create  TINYINT(1)   NOT NULL DEFAULT 0,
  can_update  TINYINT(1)   NOT NULL DEFAULT 0,
  can_delete  TINYINT(1)   NOT NULL DEFAULT 0,
  can_export  TINYINT(1)   NOT NULL DEFAULT 0,
  created_at  DATETIME(6)  NOT NULL DEFAULT NOW(6),
  updated_at  DATETIME(6)  NOT NULL DEFAULT NOW(6) ON UPDATE NOW(6),
  UNIQUE KEY uk_user_menu (tenant_id, user_id, menu_code),
  INDEX idx_tenant (tenant_id),
  INDEX idx_user (tenant_id, user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE tenants
  ADD COLUMN IF NOT EXISTS corp_no VARCHAR(13) NULL,
  ADD COLUMN IF NOT EXISTS subsidiary_no VARCHAR(4) NULL,
  ADD COLUMN IF NOT EXISTS homepage VARCHAR(200) NULL,
  ADD COLUMN IF NOT EXISTS initial_date DATE NULL,
  ADD COLUMN IF NOT EXISTS e_invoice_server VARCHAR(200) NULL,
  ADD COLUMN IF NOT EXISTS e_invoice_id VARCHAR(100) NULL,
  ADD COLUMN IF NOT EXISTS e_invoice_enabled TINYINT(1) NOT NULL DEFAULT 0;
