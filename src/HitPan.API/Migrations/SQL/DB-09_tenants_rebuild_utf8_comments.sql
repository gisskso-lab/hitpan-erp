-- tenants: 깨진 COLUMN COMMENT / utf8mb3 검증 오류(ERROR 1300) 우회
-- 기존 DDL을 COMMENT 없이 재생성 후 데이터 이전 → utf8mb4_unicode_ci + 확장 컬럼
--
-- 선행: tenants 를 참조하는 FK 가 없어야 함(히트판 기본 스키마 기준 안전).

CREATE TABLE IF NOT EXISTS tenants_rebuilt (
  tenant_id VARCHAR(36) NOT NULL,
  platform_id VARCHAR(36) DEFAULT NULL,
  tenant_code VARCHAR(20) NOT NULL,
  company_name VARCHAR(100) NOT NULL,
  biz_no VARCHAR(12) NOT NULL,
  ceo_name VARCHAR(50) NOT NULL,
  tel VARCHAR(20) DEFAULT NULL,
  address VARCHAR(200) DEFAULT NULL,
  reseller_id VARCHAR(36) DEFAULT NULL,
  max_users TINYINT UNSIGNED NOT NULL DEFAULT 3,
  status VARCHAR(20) NOT NULL DEFAULT 'trial',
  trial_ends_at DATETIME(6) DEFAULT NULL,
  db_host VARCHAR(100) NOT NULL,
  db_name VARCHAR(50) NOT NULL,
  license_key_hash VARCHAR(256) NOT NULL,
  reseller_tier TINYINT UNSIGNED NOT NULL,
  created_at DATETIME(6) NOT NULL,
  updated_at DATETIME(6) NOT NULL,
  PRIMARY KEY (tenant_id),
  UNIQUE KEY uq_tenant_code (tenant_code),
  KEY fk_tenant_reseller (reseller_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO tenants_rebuilt SELECT * FROM tenants;

RENAME TABLE tenants TO tenants_old, tenants_rebuilt TO tenants;

DROP TABLE tenants_old;

ALTER TABLE tenants
  ADD COLUMN IF NOT EXISTS biz_type VARCHAR(50) NULL,
  ADD COLUMN IF NOT EXISTS biz_item VARCHAR(100) NULL,
  ADD COLUMN IF NOT EXISTS fax VARCHAR(20) NULL,
  ADD COLUMN IF NOT EXISTS zip_code VARCHAR(10) NULL,
  ADD COLUMN IF NOT EXISTS logo_url VARCHAR(200) NULL,
  ADD COLUMN IF NOT EXISTS email VARCHAR(100) NULL,
  ADD COLUMN IF NOT EXISTS tax_type VARCHAR(20) NOT NULL DEFAULT 'taxable',
  ADD COLUMN IF NOT EXISTS fiscal_month INT NOT NULL DEFAULT 12;
