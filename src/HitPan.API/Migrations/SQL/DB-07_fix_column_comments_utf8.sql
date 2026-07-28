-- 손상된 한글 COLUMN COMMENT 제거 (ERROR 1300 utf8mb3 방지)
-- InnoDB 전환 전·후 모두 안전하게 실행 가능

-- items
ALTER TABLE items
  MODIFY COLUMN price_a DECIMAL(15,2) DEFAULT NULL,
  MODIFY COLUMN price_b DECIMAL(15,2) DEFAULT NULL,
  MODIFY COLUMN price_c DECIMAL(15,2) DEFAULT NULL,
  MODIFY COLUMN price_d DECIMAL(15,2) DEFAULT NULL,
  MODIFY COLUMN price_e DECIMAL(15,2) DEFAULT NULL,
  MODIFY COLUMN tax_type TINYINT(1) NOT NULL DEFAULT 1;

-- quotations
ALTER TABLE quotations
  MODIFY COLUMN valid_until DATE DEFAULT NULL,
  MODIFY COLUMN status ENUM('draft','submitted','accepted','rejected','expired','converted')
    NOT NULL DEFAULT 'draft',
  MODIFY COLUMN converted_order_id VARCHAR(36) DEFAULT NULL;

-- purchase_orders
ALTER TABLE purchase_orders
  MODIFY COLUMN status ENUM('draft','ordered','partial','received','cancelled')
    NOT NULL DEFAULT 'draft';

-- sales_orders
ALTER TABLE sales_orders
  MODIFY COLUMN status ENUM('draft','quotation','order','confirmed','invoiced','cancelled')
    NOT NULL DEFAULT 'draft';

-- users (ASCII 주석 컬럼은 유지, 깨진 한글 COMMENT 만 제거)
ALTER TABLE users
  MODIFY COLUMN account_type VARCHAR(20) NOT NULL DEFAULT 'tenant_user'
    COMMENT 'platform_admin/reseller_admin/tenant_admin/tenant_user',
  MODIFY COLUMN reseller_id VARCHAR(36) DEFAULT NULL,
  MODIFY COLUMN platform_id VARCHAR(36) DEFAULT NULL;
