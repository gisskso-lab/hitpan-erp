-- WEEK 1 Day 1 — 보안·성능 테이블 (hitpan_erp)
-- ledger_balance_snapshot: 요청 본문이 잘려 있어 월·계정별 기말 잔액 스냅샷으로 보완함.
--
-- MariaDB 10.0.x: GENERATED STORED / 인덱스 DESC 미지원 환경 대비
--   balance, payable, gross_profit 은 일반 컬럼(앱·배치에서 total_* 와 일치 유지).

-- ━━━ STEP 1 — 보안 ━━━

CREATE TABLE IF NOT EXISTS audit_logs (
  log_id       VARCHAR(36)  NOT NULL PRIMARY KEY,
  tenant_id    VARCHAR(36)  NULL,
  user_id      VARCHAR(36)  NULL,
  account_type VARCHAR(50)  NULL,
  ip_address   VARCHAR(50)  NULL,
  method       VARCHAR(10)  NULL,
  endpoint     VARCHAR(500) NULL,
  status_code  INT          NULL,
  user_agent   VARCHAR(500) NULL,
  created_at   DATETIME(6)  NOT NULL DEFAULT NOW(6),
  INDEX idx_tenant  (tenant_id),
  INDEX idx_user    (user_id),
  INDEX idx_created (created_at),
  INDEX idx_ip      (ip_address)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS login_attempts (
  attempt_id   VARCHAR(36)  NOT NULL PRIMARY KEY,
  email        VARCHAR(200) NOT NULL,
  ip_address   VARCHAR(50)  NOT NULL,
  is_success   TINYINT(1)   NOT NULL DEFAULT 0,
  attempted_at DATETIME(6)  NOT NULL DEFAULT NOW(6),
  INDEX idx_email_ip  (email, ip_address),
  INDEX idx_attempted (attempted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS refresh_tokens (
  token_id     VARCHAR(36)  NOT NULL PRIMARY KEY,
  user_id      VARCHAR(36)  NOT NULL,
  token_hash   VARCHAR(256) NOT NULL,
  ip_address   VARCHAR(50)  NULL,
  user_agent   VARCHAR(500) NULL,
  expires_at   DATETIME(6)  NOT NULL,
  is_revoked   TINYINT(1)   NOT NULL DEFAULT 0,
  created_at   DATETIME(6)  NOT NULL DEFAULT NOW(6),
  INDEX idx_user    (user_id),
  INDEX idx_token   (token_hash),
  INDEX idx_expires (expires_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS user_sessions (
  session_id     VARCHAR(36)  NOT NULL PRIMARY KEY,
  user_id        VARCHAR(36)  NOT NULL,
  tenant_id      VARCHAR(36)  NULL,
  ip_address     VARCHAR(50)  NULL,
  user_agent     VARCHAR(500) NULL,
  login_at       DATETIME(6)  NOT NULL DEFAULT NOW(6),
  last_active_at DATETIME(6)  NOT NULL DEFAULT NOW(6),
  is_active      TINYINT(1)   NOT NULL DEFAULT 1,
  INDEX idx_user   (user_id),
  INDEX idx_tenant (tenant_id),
  INDEX idx_active (is_active)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS security_alerts (
  alert_id    VARCHAR(36)  NOT NULL PRIMARY KEY,
  tenant_id   VARCHAR(36)  NULL,
  user_id     VARCHAR(36)  NULL,
  alert_type  VARCHAR(50)  NOT NULL,
  description VARCHAR(500) NULL,
  ip_address  VARCHAR(50)  NULL,
  is_resolved TINYINT(1)   NOT NULL DEFAULT 0,
  created_at  DATETIME(6)  NOT NULL DEFAULT NOW(6),
  INDEX idx_tenant  (tenant_id),
  INDEX idx_type    (alert_type),
  INDEX idx_created (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ━━━ STEP 2 — 성능 ━━━

CREATE TABLE IF NOT EXISTS partner_balance (
  balance_id      VARCHAR(36)   NOT NULL PRIMARY KEY,
  tenant_id       VARCHAR(36)   NOT NULL,
  partner_id      VARCHAR(36)   NOT NULL,
  total_sales     DECIMAL(15,2) NOT NULL DEFAULT 0,
  total_receipt   DECIMAL(15,2) NOT NULL DEFAULT 0,
  total_purchase  DECIMAL(15,2) NOT NULL DEFAULT 0,
  total_payment   DECIMAL(15,2) NOT NULL DEFAULT 0,
  last_updated_at DATETIME(6)   NOT NULL DEFAULT NOW(6),
  balance         DECIMAL(15,2) NOT NULL DEFAULT 0 COMMENT 'total_sales - total_receipt',
  payable         DECIMAL(15,2) NOT NULL DEFAULT 0 COMMENT 'total_purchase - total_payment',
  UNIQUE KEY uk_tenant_partner (tenant_id, partner_id),
  INDEX idx_tenant (tenant_id),
  INDEX idx_balance (tenant_id, balance)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS item_stock (
  stock_id        VARCHAR(36)   NOT NULL PRIMARY KEY,
  tenant_id       VARCHAR(36)   NOT NULL,
  item_id         VARCHAR(36)   NOT NULL,
  warehouse_id    VARCHAR(36)   NOT NULL DEFAULT 'default',
  current_qty     DECIMAL(10,2) NOT NULL DEFAULT 0,
  avg_cost        DECIMAL(15,2) NOT NULL DEFAULT 0,
  last_updated_at DATETIME(6)   NOT NULL DEFAULT NOW(6),
  UNIQUE KEY uk_tenant_item_wh (tenant_id, item_id, warehouse_id),
  INDEX idx_tenant (tenant_id),
  INDEX idx_low_stock (tenant_id, current_qty)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS monthly_summary (
  summary_id      VARCHAR(36)   NOT NULL PRIMARY KEY,
  tenant_id       VARCHAR(36)   NOT NULL,
  `year_month`    CHAR(6)       NOT NULL,
  total_sales     DECIMAL(15,2) NOT NULL DEFAULT 0,
  total_purchase  DECIMAL(15,2) NOT NULL DEFAULT 0,
  total_receipt   DECIMAL(15,2) NOT NULL DEFAULT 0,
  total_payment   DECIMAL(15,2) NOT NULL DEFAULT 0,
  last_updated_at DATETIME(6)   NOT NULL DEFAULT NOW(6),
  gross_profit    DECIMAL(15,2) NOT NULL DEFAULT 0 COMMENT 'total_sales - total_purchase',
  UNIQUE KEY uk_tenant_month (tenant_id, `year_month`),
  INDEX idx_tenant (tenant_id),
  INDEX idx_month (tenant_id, `year_month`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS bom_cost_cache (
  cache_id           VARCHAR(36)   NOT NULL PRIMARY KEY,
  tenant_id          VARCHAR(36)   NOT NULL,
  product_item_id    VARCHAR(36)   NOT NULL,
  calculated_cost    DECIMAL(15,2) NOT NULL DEFAULT 0,
  material_count     INT           NOT NULL DEFAULT 0,
  is_dirty           TINYINT(1)    NOT NULL DEFAULT 0,
  last_calculated_at DATETIME(6)   NOT NULL DEFAULT NOW(6),
  UNIQUE KEY uk_tenant_item (tenant_id, product_item_id),
  INDEX idx_dirty (tenant_id, is_dirty)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ledger_balance_snapshot (
  snapshot_id     VARCHAR(36)   NOT NULL PRIMARY KEY,
  tenant_id       VARCHAR(36)   NOT NULL,
  `year_month`    CHAR(6)       NOT NULL COMMENT 'YYYYMM',
  account_code    VARCHAR(40)   NOT NULL,
  ending_balance  DECIMAL(15,2) NOT NULL DEFAULT 0,
  created_at      DATETIME(6)   NOT NULL DEFAULT NOW(6),
  UNIQUE KEY uk_ledger_snap (tenant_id, `year_month`, account_code),
  INDEX idx_tenant (tenant_id),
  INDEX idx_year_month (tenant_id, `year_month`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
