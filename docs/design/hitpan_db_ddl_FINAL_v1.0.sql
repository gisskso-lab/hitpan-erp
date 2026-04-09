-- ============================================================
--  히트판 SaaS ERP — 전체 DB 설계 DDL
--  Database: MariaDB 10.11 LTS
--  Charset:  utf8mb4 / utf8mb4_unicode_ci
--  Version:  v1.0  |  공영정보
-- ============================================================
-- ERD 관계 요약
-- tenants 1:N subscriptions
-- tenants 1:N billing_keys
-- tenants 1:N billing_invoices
-- tenants 1:N users
-- tenants 1:N departments (departments 자기참조 트리)
-- tenants 1:N audit_logs
-- tenants 1:N workflow_settings
-- tenants 1:N items, partners, employees, warehouses
-- items   1:N item_prices
-- items   1:N bom_headers
-- bom_headers 1:N bom_items (bom_items 자기참조 다단계)
-- work_orders 1:N work_order_materials
-- work_orders 1:N work_order_results
-- purchase_orders 1:N purchase_order_items
-- purchase_receipts 1:N purchase_receipt_items
-- sales_orders 1:N sales_order_items
-- sales_deliveries 1:N sales_delivery_items
-- returns 1:N return_items
-- journal_entries 1:N journal_lines
-- tax_invoices 1:N tax_invoice_items
-- payroll_headers 1:N payroll_details
-- payroll_details 1:N payroll_deductions
-- ============================================================

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- ────────────────────────────────────────
-- L1 : 플랫폼 기반
-- ────────────────────────────────────────

CREATE TABLE IF NOT EXISTS tenants (
  tenant_id         CHAR(36)      NOT NULL                         COMMENT 'UUID v4',
  tenant_code       VARCHAR(20)   NOT NULL                         COMMENT '히트판 고객코드 HP-00001',
  company_name      VARCHAR(100)  NOT NULL                         COMMENT '회사명',
  biz_no            VARCHAR(12)   NOT NULL                         COMMENT '사업자번호',
  ceo_name          VARCHAR(50)   NOT NULL                         COMMENT '대표자명',
  tel               VARCHAR(20)   NULL,
  address           VARCHAR(200)  NULL,
  reseller_id       CHAR(36)      NULL                             COMMENT '대리점 tenant_id. NULL=직계약',
  status            ENUM('active','suspended','trial','cancelled') NOT NULL DEFAULT 'trial',
  trial_ends_at     DATETIME      NULL,
  db_host           VARCHAR(100)  NOT NULL                         COMMENT '고객사 MariaDB 호스트',
  db_name           VARCHAR(50)   NOT NULL                         COMMENT '고객사 DB명',
  license_key_hash  VARCHAR(256)  NOT NULL                         COMMENT 'SHA-256 해시. 평문 저장 금지',
  reseller_tier     TINYINT       NOT NULL DEFAULT 0                        COMMENT '0=일반고객 1=대리점 2=총판',
  created_at        DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at        DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (tenant_id),
  UNIQUE KEY uq_tenant_code (tenant_code),
  KEY idx_reseller (reseller_id),
  KEY idx_status   (status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='고객사(테넌트)';

CREATE TABLE IF NOT EXISTS subscriptions (
  subscription_id     CHAR(36)    NOT NULL,
  tenant_id           CHAR(36)    NOT NULL,
  plan_type           ENUM('basic','pro','reseller') NOT NULL,
  base_users          TINYINT     NOT NULL             COMMENT 'Basic=5, Pro=10',
  extra_users         TINYINT     NOT NULL DEFAULT 0,
  base_fee            INT         NOT NULL             COMMENT '기본료(원)',
  extra_fee_per_user  INT         NOT NULL DEFAULT 10000,
  billing_cycle       ENUM('monthly','yearly') NOT NULL DEFAULT 'monthly',
  started_at          DATE        NOT NULL,
  ends_at             DATE        NULL,
  next_billing_at     DATE        NOT NULL,
  status              ENUM('active','paused','cancelled') NOT NULL,
  created_at          DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at          DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (subscription_id),
  KEY idx_tenant       (tenant_id),
  KEY idx_next_billing (next_billing_at, status),
  CONSTRAINT fk_sub_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (tenant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='구독 플랜 이력';

CREATE TABLE IF NOT EXISTS billing_keys (
  billing_key_id  CHAR(36)    NOT NULL,
  tenant_id       CHAR(36)    NOT NULL,
  provider        ENUM('toss','kakao') NOT NULL,
  method_type     ENUM('card','account','tosspay','kakaopay') NOT NULL,
  billing_key     VARCHAR(200) NOT NULL                        COMMENT 'AES-256 앱 레벨 암호화',
  masked_no       VARCHAR(20)  NULL                            COMMENT '표시용 마스킹 ****1234',
  card_name       VARCHAR(50)  NULL,
  is_default      TINYINT(1)   NOT NULL DEFAULT 0,
  is_active       TINYINT(1)   NOT NULL DEFAULT 1,
  expired_at      DATE         NULL,
  created_at      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (billing_key_id),
  KEY idx_tenant_default (tenant_id, is_default)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='결제수단(빌링키)';

CREATE TABLE IF NOT EXISTS billing_invoices (
  invoice_id      CHAR(36)    NOT NULL,
  tenant_id       CHAR(36)    NOT NULL,
  subscription_id CHAR(36)    NOT NULL,
  billing_month   CHAR(7)     NOT NULL                         COMMENT "'2025-01'. 중복청구 방지",
  user_count      TINYINT     NOT NULL,
  base_amount     INT         NOT NULL,
  extra_amount    INT         NOT NULL DEFAULT 0,
  total_amount    INT         NOT NULL,
  status          ENUM('pending','paid','failed','refunded') NOT NULL,
  paid_at         DATETIME    NULL,
  payment_key     VARCHAR(200) NULL,
  fail_reason     VARCHAR(200) NULL,
  retry_count     TINYINT     NOT NULL DEFAULT 0,
  next_retry_at   DATETIME    NULL,
  created_at      DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at      DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (invoice_id),
  UNIQUE KEY uq_tenant_month  (tenant_id, billing_month),
  KEY idx_status_retry (status, next_retry_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='청구서';

CREATE TABLE IF NOT EXISTS departments (
  dept_id         CHAR(36)    NOT NULL,
  tenant_id       CHAR(36)    NOT NULL,
  parent_dept_id  CHAR(36)    NULL,
  dept_name       VARCHAR(50) NOT NULL,
  dept_code       VARCHAR(20) NULL,
  sort_order      SMALLINT    NOT NULL DEFAULT 0,
  is_active       TINYINT(1)  NOT NULL DEFAULT 1,
  created_at      DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at      DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (dept_id),
  KEY idx_tenant (tenant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='부서·조직 (자기참조 트리)';

CREATE TABLE IF NOT EXISTS users (
  user_id              CHAR(36)     NOT NULL,
  tenant_id            CHAR(36)     NOT NULL,
  email                VARCHAR(100) NOT NULL,
  password_hash        VARCHAR(256) NOT NULL                    COMMENT 'bcrypt cost=12',
  user_name            VARCHAR(50)  NOT NULL,
  role                 ENUM('tenant_admin','manager','user','readonly') NOT NULL,
  dept_id              CHAR(36)     NULL,
  phone                VARCHAR(20)  NULL,
  is_active            TINYINT(1)   NOT NULL DEFAULT 1,
  last_login_at        DATETIME     NULL,
  password_changed_at  DATETIME     NULL,
  created_at           DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at           DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  created_by           CHAR(36)     NULL,
  updated_by           CHAR(36)     NULL,
  PRIMARY KEY (user_id),
  UNIQUE KEY uq_tenant_email (tenant_id, email),
  KEY idx_dept (dept_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='사용자 계정';

CREATE TABLE IF NOT EXISTS audit_logs (
  log_id      BIGINT       NOT NULL AUTO_INCREMENT,
  tenant_id   CHAR(36)     NOT NULL,
  user_id     CHAR(36)     NULL,
  action      VARCHAR(50)  NOT NULL                             COMMENT 'CREATE|UPDATE|DELETE|LOGIN',
  table_name  VARCHAR(50)  NOT NULL,
  record_id   VARCHAR(36)  NULL,
  old_values  JSON         NULL,
  new_values  JSON         NULL,
  ip_address  VARCHAR(45)  NULL,
  user_agent  VARCHAR(500) NULL,
  session_id  VARCHAR(100) NULL,
  created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (log_id),
  KEY idx_tenant_date   (tenant_id, created_at),
  KEY idx_table_record  (table_name, record_id),
  KEY idx_session       (session_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='전체 감사 로그';

CREATE TABLE IF NOT EXISTS workflow_settings (
  setting_id    CHAR(36)     NOT NULL,
  tenant_id     CHAR(36)     NOT NULL,
  setting_key   VARCHAR(60)  NOT NULL                           COMMENT "'purchase.require_po' 형태",
  setting_value VARCHAR(200) NOT NULL                           COMMENT 'true|false|역할명|숫자',
  value_type    ENUM('boolean','string','number','role') NOT NULL,
  is_active     TINYINT(1)   NOT NULL DEFAULT 1,
  updated_at    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by    CHAR(36)     NULL,
  PRIMARY KEY (setting_id),
  UNIQUE KEY uq_tenant_key (tenant_id, setting_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='테넌트별 워크플로우 설정';

-- ────────────────────────────────────────
-- L2 : 마스터 데이터
-- ────────────────────────────────────────

CREATE TABLE IF NOT EXISTS items (
  item_id      CHAR(36)      NOT NULL,
  tenant_id    CHAR(36)      NOT NULL,
  item_code    VARCHAR(30)   NOT NULL,
  item_name    VARCHAR(100)  NOT NULL,
  item_type    ENUM('product','material','semi','expense') NOT NULL,
  category_id  CHAR(36)      NULL,
  unit         VARCHAR(10)   NOT NULL,
  std_price    DECIMAL(15,2) NULL,
  cost_price   DECIMAL(15,2) NULL,
  safe_stock   DECIMAL(15,3) NULL,
  is_active    TINYINT(1)    NOT NULL DEFAULT 1,
  memo         VARCHAR(500)  NULL,
  created_at   DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at   DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  created_by   CHAR(36)      NULL,
  updated_by   CHAR(36)      NULL,
  PRIMARY KEY (item_id),
  UNIQUE KEY uq_tenant_code   (tenant_id, item_code),
  KEY idx_tenant_type (tenant_id, item_type),
  CONSTRAINT fk_item_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (tenant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='상품·품목 마스터';

CREATE TABLE IF NOT EXISTS item_prices (
  price_id    CHAR(36)      NOT NULL,
  tenant_id   CHAR(36)      NOT NULL,
  item_id     CHAR(36)      NOT NULL,
  partner_id  CHAR(36)      NULL                                COMMENT 'NULL=기본단가. 있으면 거래처별',
  price_type  ENUM('sell','buy') NOT NULL,
  price       DECIMAL(15,2) NOT NULL,
  valid_from  DATE          NOT NULL,
  valid_to    DATE          NULL,
  created_at  DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by  CHAR(36)      NULL,
  PRIMARY KEY (price_id),
  KEY idx_item_partner (tenant_id, item_id, partner_id, price_type)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='단가 이력 (기간별)';

CREATE TABLE IF NOT EXISTS partners (
  partner_id      CHAR(36)      NOT NULL,
  tenant_id       CHAR(36)      NOT NULL,
  partner_code    VARCHAR(20)   NOT NULL,
  partner_name    VARCHAR(100)  NOT NULL,
  partner_type    ENUM('customer','supplier','both') NOT NULL,
  biz_no          VARCHAR(12)   NULL,
  ceo_name        VARCHAR(50)   NULL,
  biz_type        VARCHAR(50)   NULL,
  biz_item        VARCHAR(50)   NULL,
  tel             VARCHAR(20)   NULL,
  fax             VARCHAR(20)   NULL,
  email           VARCHAR(100)  NULL,
  address         VARCHAR(200)  NULL,
  credit_limit    DECIMAL(15,2) NULL,
  payment_terms   TINYINT       NULL,
  bank_name       VARCHAR(30)   NULL,
  bank_account    VARCHAR(200)  NULL                            COMMENT 'AES-256 암호화',
  account_holder  VARCHAR(30)   NULL,
  is_active       TINYINT(1)    NOT NULL DEFAULT 1,
  memo            VARCHAR(500)  NULL,
  created_at      DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at      DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  created_by      CHAR(36)      NULL,
  updated_by      CHAR(36)      NULL,
  PRIMARY KEY (partner_id),
  UNIQUE KEY uq_tenant_code   (tenant_id, partner_code),
  KEY idx_tenant_type (tenant_id, partner_type),
  KEY idx_biz_no      (biz_no)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='거래처·업체 마스터';

CREATE TABLE IF NOT EXISTS employees (
  employee_id  CHAR(36)     NOT NULL,
  tenant_id    CHAR(36)     NOT NULL,
  user_id      CHAR(36)     NULL,
  emp_no       VARCHAR(20)  NOT NULL,
  emp_name     VARCHAR(50)  NOT NULL,
  dept_id      CHAR(36)     NULL,
  position     VARCHAR(30)  NULL,
  job_title    VARCHAR(30)  NULL,
  emp_type     ENUM('regular','contract','part','dispatch') NOT NULL,
  join_date    DATE         NOT NULL,
  resign_date  DATE         NULL,
  birth_date   VARCHAR(200) NULL                             COMMENT 'AES-256 암호화',
  id_no_hash   VARCHAR(256) NULL                             COMMENT 'SHA-256 해시. 원문 저장 금지',
  phone        VARCHAR(20)  NULL,
  email        VARCHAR(100) NULL,
  bank_name    VARCHAR(30)  NULL,
  bank_account VARCHAR(200) NULL                             COMMENT 'AES-256 암호화',
  base_salary  VARCHAR(200) NULL                             COMMENT 'AES-256 암호화',
  is_active    TINYINT(1)   NOT NULL DEFAULT 1,
  created_at   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  created_by   CHAR(36)     NULL,
  updated_by   CHAR(36)     NULL,
  PRIMARY KEY (employee_id),
  UNIQUE KEY uq_tenant_empno (tenant_id, emp_no),
  KEY idx_dept (dept_id),
  KEY idx_user (user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='사원 마스터';

CREATE TABLE IF NOT EXISTS warehouses (
  warehouse_id  CHAR(36)     NOT NULL,
  tenant_id     CHAR(36)     NOT NULL,
  wh_code       VARCHAR(20)  NOT NULL,
  wh_name       VARCHAR(50)  NOT NULL,
  wh_type       ENUM('normal','consign','defect','return') NOT NULL,
  location      VARCHAR(100) NULL,
  is_active     TINYINT(1)   NOT NULL DEFAULT 1,
  created_at    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (warehouse_id),
  UNIQUE KEY uq_tenant_code (tenant_id, wh_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='창고 마스터';

CREATE TABLE IF NOT EXISTS common_codes (
  code_id      CHAR(36)    NOT NULL,
  tenant_id    CHAR(36)    NULL                                  COMMENT 'NULL=시스템 공통',
  code_group   VARCHAR(30) NOT NULL,
  code_value   VARCHAR(30) NOT NULL,
  code_label   VARCHAR(50) NOT NULL,
  sort_order   SMALLINT    NOT NULL DEFAULT 0,
  is_active    TINYINT(1)  NOT NULL DEFAULT 1,
  created_at   DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (code_id),
  KEY idx_group (tenant_id, code_group)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='공통 코드';

-- ────────────────────────────────────────
-- L3 : 수불 원장·워크플로우
-- ────────────────────────────────────────

CREATE TABLE IF NOT EXISTS stock_ledger (
  ledger_id      BIGINT        NOT NULL AUTO_INCREMENT          COMMENT 'BIGINT — 수억 건 대응',
  tenant_id      CHAR(36)      NOT NULL,
  item_id        CHAR(36)      NOT NULL,
  warehouse_id   CHAR(36)      NOT NULL,
  partner_id     CHAR(36)      NULL                             COMMENT '업체별 원장용',
  employee_id    CHAR(36)      NULL                             COMMENT '담당자별 실적용',
  ledger_date    DATE          NOT NULL,
  ym             CHAR(7)       NOT NULL                         COMMENT "'2025-01' 월 파티션 키",
  move_type      ENUM('in','out','adjust') NOT NULL,
  source_type    VARCHAR(30)   NOT NULL                         COMMENT 'purchase_receipt|sales_delivery|work_order|adjustment|cancel',
  source_id      CHAR(36)      NOT NULL                         COMMENT '원본 전표 UUID',
  doc_no         VARCHAR(20)   NULL                             COMMENT '원본 전표번호 (화면 링크용)',
  qty_in         DECIMAL(15,3) NOT NULL DEFAULT 0,
  qty_out        DECIMAL(15,3) NOT NULL DEFAULT 0,
  unit_cost      DECIMAL(15,4) NULL                             COMMENT '단위원가 (원가계산용)',
  supply_amount  DECIMAL(15,2) NULL                             COMMENT '공급가액 (금액 집계용)',
  memo           VARCHAR(200)  NULL,
  created_at     DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by     CHAR(36)      NULL,
  PRIMARY KEY (ledger_id),
  KEY idx_main_filter      (tenant_id, ledger_date, item_id),
  KEY idx_item_wh          (tenant_id, item_id, warehouse_id, ledger_date),
  KEY idx_partner_date     (tenant_id, partner_id, ledger_date, source_type),
  KEY idx_employee_date    (tenant_id, employee_id, ledger_date),
  KEY idx_source           (source_type, source_id),
  KEY idx_ym               (tenant_id, ym, item_id),
  KEY idx_adjust           (tenant_id, source_type, created_at),
  KEY idx_item_partner_cross (tenant_id, item_id, partner_id, source_type)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='재고 수불 원장 — INSERT ONLY. UPDATE·DELETE 금지';

CREATE TABLE IF NOT EXISTS stock_monthly_snapshot (
  snap_id       CHAR(36)      NOT NULL,
  tenant_id     CHAR(36)      NOT NULL,
  item_id       CHAR(36)      NOT NULL,
  warehouse_id  CHAR(36)      NOT NULL,
  partner_id    CHAR(36)      NULL,
  ym            CHAR(7)       NOT NULL,
  begin_qty     DECIMAL(15,3) NOT NULL DEFAULT 0,
  in_qty        DECIMAL(15,3) NOT NULL DEFAULT 0,
  in_amount     DECIMAL(15,2) NOT NULL DEFAULT 0,
  out_qty       DECIMAL(15,3) NOT NULL DEFAULT 0,
  out_amount    DECIMAL(15,2) NOT NULL DEFAULT 0,
  adjust_qty    DECIMAL(15,3) NOT NULL DEFAULT 0,
  end_qty       DECIMAL(15,3) NOT NULL DEFAULT 0,
  avg_cost      DECIMAL(15,4) NULL,
  snap_created_at DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (snap_id),
  UNIQUE KEY uq_snap_key (tenant_id, ym, item_id, warehouse_id, partner_id),
  KEY idx_snap_lookup  (tenant_id, ym, item_id, warehouse_id),
  KEY idx_snap_partner (tenant_id, ym, partner_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='월별 재고 스냅샷 (성능 최적화)';

CREATE TABLE IF NOT EXISTS document_links (
  link_id     CHAR(36)      NOT NULL,
  tenant_id   CHAR(36)      NOT NULL,
  from_type   VARCHAR(40)   NOT NULL,
  from_id     CHAR(36)      NOT NULL,
  to_type     VARCHAR(40)   NOT NULL,
  to_id       CHAR(36)      NOT NULL,
  link_type   ENUM('derived','reference','manual') NOT NULL,
  linked_qty  DECIMAL(15,3) NULL,
  created_at  DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by  CHAR(36)      NULL,
  PRIMARY KEY (link_id),
  KEY idx_from (tenant_id, from_type, from_id),
  KEY idx_to   (tenant_id, to_type, to_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='전표 간 연결 관계';

CREATE TABLE IF NOT EXISTS workflow_audit_bypasses (
  bypass_id     BIGINT      NOT NULL AUTO_INCREMENT,
  tenant_id     CHAR(36)    NOT NULL,
  user_id       CHAR(36)    NOT NULL,
  bypassed_step VARCHAR(60) NOT NULL,
  doc_type      VARCHAR(40) NOT NULL,
  doc_id        CHAR(36)    NOT NULL,
  reason        VARCHAR(200) NULL,
  created_at    DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (bypass_id),
  KEY idx_tenant_step (tenant_id, bypassed_step)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='워크플로우 우회 감사 로그';

CREATE TABLE IF NOT EXISTS ledger_custom_views (
  view_id       CHAR(36)    NOT NULL,
  tenant_id     CHAR(36)    NOT NULL,
  user_id       CHAR(36)    NULL,
  view_name     VARCHAR(50) NOT NULL,
  ledger_type   VARCHAR(30) NOT NULL,
  filter_config JSON        NOT NULL,
  column_config JSON        NULL,
  is_default    TINYINT(1)  NOT NULL DEFAULT 0,
  created_at    DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (view_id),
  KEY idx_tenant_user (tenant_id, user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='수불부 커스텀 저장 설정';

-- ────────────────────────────────────────
-- L4 : 재고·생산·거래 (헤더-라인 구조)
-- ────────────────────────────────────────

CREATE TABLE IF NOT EXISTS bom_headers (
  bom_id        CHAR(36)      NOT NULL,
  tenant_id     CHAR(36)      NOT NULL,
  item_id       CHAR(36)      NOT NULL,
  bom_version   VARCHAR(10)   NOT NULL,
  is_active     TINYINT(1)    NOT NULL DEFAULT 1,
  effective_from DATE         NOT NULL,
  effective_to  DATE          NULL,
  base_qty      DECIMAL(15,3) NOT NULL DEFAULT 1,
  memo          VARCHAR(500)  NULL,
  created_at    DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by    CHAR(36)      NULL,
  PRIMARY KEY (bom_id),
  UNIQUE KEY uq_bom_version (tenant_id, item_id, bom_version)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='BOM 헤더 (버전 관리)';

CREATE TABLE IF NOT EXISTS bom_items (
  bom_item_id        CHAR(36)      NOT NULL,
  bom_id             CHAR(36)      NOT NULL,
  tenant_id          CHAR(36)      NOT NULL,
  parent_bom_item_id CHAR(36)      NULL                        COMMENT '자기참조. NULL=최상위. 다단계 BOM',
  item_id            CHAR(36)      NOT NULL,
  level              TINYINT       NOT NULL DEFAULT 1,
  qty_per_base       DECIMAL(15,4) NOT NULL,
  loss_rate          DECIMAL(5,2)  NOT NULL DEFAULT 0,
  warehouse_id       CHAR(36)      NULL,
  sort_order         SMALLINT      NOT NULL DEFAULT 0,
  is_active          TINYINT(1)    NOT NULL DEFAULT 1,
  PRIMARY KEY (bom_item_id),
  KEY idx_bom    (bom_id),
  KEY idx_parent (parent_bom_item_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='BOM 자재 라인 (다단계 트리)';

CREATE TABLE IF NOT EXISTS work_orders (
  work_order_id  CHAR(36)      NOT NULL,
  tenant_id      CHAR(36)      NOT NULL,
  wo_no          VARCHAR(20)   NOT NULL,
  item_id        CHAR(36)      NOT NULL,
  bom_id         CHAR(36)      NULL,
  sales_order_id CHAR(36)      NULL,
  planned_qty    DECIMAL(15,3) NOT NULL,
  planned_date   DATE          NOT NULL,
  status         ENUM('draft','confirmed','in_progress','completed','cancelled') NOT NULL DEFAULT 'draft',
  warehouse_id   CHAR(36)      NULL,
  employee_id    CHAR(36)      NULL,
  memo           VARCHAR(500)  NULL,
  created_at     DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by     CHAR(36)      NULL,
  PRIMARY KEY (work_order_id),
  UNIQUE KEY uq_wo_no (tenant_id, wo_no)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='작업지시서';

CREATE TABLE IF NOT EXISTS work_order_materials (
  material_id    CHAR(36)      NOT NULL,
  work_order_id  CHAR(36)      NOT NULL,
  tenant_id      CHAR(36)      NOT NULL,
  item_id        CHAR(36)      NOT NULL,
  bom_item_id    CHAR(36)      NULL,
  planned_qty    DECIMAL(15,3) NOT NULL,
  actual_qty     DECIMAL(15,3) NOT NULL DEFAULT 0,
  warehouse_id   CHAR(36)      NULL,
  issued_at      DATETIME      NULL,
  PRIMARY KEY (material_id),
  KEY idx_wo (work_order_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='작업지시 자재 소요';

CREATE TABLE IF NOT EXISTS work_order_results (
  result_id      CHAR(36)      NOT NULL,
  work_order_id  CHAR(36)      NOT NULL,
  tenant_id      CHAR(36)      NOT NULL,
  process_name   VARCHAR(50)   NULL,
  result_date    DATE          NOT NULL,
  good_qty       DECIMAL(15,3) NOT NULL DEFAULT 0,
  defect_qty     DECIMAL(15,3) NOT NULL DEFAULT 0,
  defect_type    VARCHAR(50)   NULL,
  employee_id    CHAR(36)      NULL,
  status         ENUM('draft','confirmed') NOT NULL DEFAULT 'draft',
  memo           VARCHAR(200)  NULL,
  created_at     DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by     CHAR(36)      NULL,
  PRIMARY KEY (result_id),
  KEY idx_wo_date (work_order_id, result_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='생산 실적 (공정별)';

CREATE TABLE IF NOT EXISTS purchase_orders (
  po_id          CHAR(36)      NOT NULL,
  tenant_id      CHAR(36)      NOT NULL,
  po_no          VARCHAR(20)   NOT NULL,
  partner_id     CHAR(36)      NOT NULL,
  employee_id    CHAR(36)      NULL,
  po_date        DATE          NOT NULL,
  expected_date  DATE          NULL,
  status         ENUM('draft','confirmed','partial','closed','cancelled') NOT NULL DEFAULT 'draft',
  total_amount   DECIMAL(15,2) NOT NULL DEFAULT 0,
  vat_amount     DECIMAL(15,2) NOT NULL DEFAULT 0,
  memo           VARCHAR(500)  NULL,
  created_at     DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by     CHAR(36)      NULL,
  PRIMARY KEY (po_id),
  UNIQUE KEY uq_po_no    (tenant_id, po_no),
  KEY idx_partner (tenant_id, partner_id),
  KEY idx_date    (tenant_id, po_date),
  KEY idx_status  (status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='발주서';

CREATE TABLE IF NOT EXISTS purchase_order_items (
  po_item_id     CHAR(36)      NOT NULL,
  po_id          CHAR(36)      NOT NULL,
  tenant_id      CHAR(36)      NOT NULL,
  item_id        CHAR(36)      NOT NULL,
  ordered_qty    DECIMAL(15,3) NOT NULL,
  received_qty   DECIMAL(15,3) NOT NULL DEFAULT 0,
  unit_price     DECIMAL(15,2) NOT NULL,
  supply_amount  DECIMAL(15,2) NOT NULL,
  vat_amount     DECIMAL(15,2) NOT NULL DEFAULT 0,
  warehouse_id   CHAR(36)      NULL,
  item_status    ENUM('pending','partial','closed') NOT NULL DEFAULT 'pending',
  PRIMARY KEY (po_item_id),
  KEY idx_po (po_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='발주서 라인';

CREATE TABLE IF NOT EXISTS purchase_receipts (
  receipt_id     CHAR(36)      NOT NULL,
  tenant_id      CHAR(36)      NOT NULL,
  receipt_no     VARCHAR(20)   NOT NULL,
  po_id          CHAR(36)      NULL                            COMMENT '발주서 FK. NULL=직접매입',
  partner_id     CHAR(36)      NOT NULL,
  receipt_date   DATE          NOT NULL,
  source_type    ENUM('from_po','direct') NOT NULL,
  status         ENUM('draft','confirmed','cancelled') NOT NULL DEFAULT 'draft',
  total_amount   DECIMAL(15,2) NOT NULL DEFAULT 0,
  vat_amount     DECIMAL(15,2) NOT NULL DEFAULT 0,
  tax_invoice_id CHAR(36)      NULL,
  memo           VARCHAR(500)  NULL,
  created_at     DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by     CHAR(36)      NULL,
  PRIMARY KEY (receipt_id),
  UNIQUE KEY uq_receipt_no    (tenant_id, receipt_no),
  KEY idx_po          (po_id),
  KEY idx_partner     (tenant_id, partner_id),
  KEY idx_date_status (tenant_id, receipt_date, status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='매입 입고 헤더';

CREATE TABLE IF NOT EXISTS purchase_receipt_items (
  receipt_item_id  CHAR(36)      NOT NULL,
  receipt_id       CHAR(36)      NOT NULL,
  tenant_id        CHAR(36)      NOT NULL,
  po_item_id       CHAR(36)      NULL,
  item_id          CHAR(36)      NOT NULL,
  warehouse_id     CHAR(36)      NOT NULL,
  qty              DECIMAL(15,3) NOT NULL,
  unit_price       DECIMAL(15,2) NOT NULL,
  supply_amount    DECIMAL(15,2) NOT NULL,
  vat_amount       DECIMAL(15,2) NOT NULL DEFAULT 0,
  PRIMARY KEY (receipt_item_id),
  KEY idx_receipt (receipt_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='매입 입고 라인';

CREATE TABLE IF NOT EXISTS sales_orders (
  order_id       CHAR(36)      NOT NULL,
  tenant_id      CHAR(36)      NOT NULL,
  order_no       VARCHAR(20)   NOT NULL,
  partner_id     CHAR(36)      NOT NULL,
  employee_id    CHAR(36)      NULL,
  order_date     DATE          NOT NULL,
  delivery_date  DATE          NULL,
  status         ENUM('draft','confirmed','partial','closed','cancelled') NOT NULL DEFAULT 'draft',
  total_amount   DECIMAL(15,2) NOT NULL DEFAULT 0,
  vat_amount     DECIMAL(15,2) NOT NULL DEFAULT 0,
  memo           VARCHAR(500)  NULL,
  created_at     DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by     CHAR(36)      NULL,
  PRIMARY KEY (order_id),
  UNIQUE KEY uq_order_no  (tenant_id, order_no),
  KEY idx_partner (tenant_id, partner_id),
  KEY idx_employee (tenant_id, employee_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='수주 헤더';

CREATE TABLE IF NOT EXISTS sales_order_items (
  order_item_id  CHAR(36)      NOT NULL,
  order_id       CHAR(36)      NOT NULL,
  tenant_id      CHAR(36)      NOT NULL,
  item_id        CHAR(36)      NOT NULL,
  ordered_qty    DECIMAL(15,3) NOT NULL,
  delivered_qty  DECIMAL(15,3) NOT NULL DEFAULT 0,
  unit_price     DECIMAL(15,2) NOT NULL,
  supply_amount  DECIMAL(15,2) NOT NULL,
  vat_amount     DECIMAL(15,2) NOT NULL DEFAULT 0,
  item_status    ENUM('pending','partial','closed') NOT NULL DEFAULT 'pending',
  PRIMARY KEY (order_item_id),
  KEY idx_order (order_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='수주 라인';

CREATE TABLE IF NOT EXISTS sales_deliveries (
  delivery_id    CHAR(36)      NOT NULL,
  tenant_id      CHAR(36)      NOT NULL,
  delivery_no    VARCHAR(20)   NOT NULL,
  order_id       CHAR(36)      NULL                            COMMENT '수주 FK. NULL=직접 발행',
  partner_id     CHAR(36)      NOT NULL,
  employee_id    CHAR(36)      NULL,
  delivery_date  DATE          NOT NULL,
  source_type    ENUM('from_order','direct') NOT NULL,
  status         ENUM('draft','confirmed','cancelled') NOT NULL DEFAULT 'draft',
  total_amount   DECIMAL(15,2) NOT NULL DEFAULT 0,
  vat_amount     DECIMAL(15,2) NOT NULL DEFAULT 0,
  tax_invoice_id CHAR(36)      NULL,
  memo           VARCHAR(500)  NULL,
  created_at     DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by     CHAR(36)      NULL,
  PRIMARY KEY (delivery_id),
  UNIQUE KEY uq_delivery_no   (tenant_id, delivery_no),
  KEY idx_order       (order_id),
  KEY idx_partner     (tenant_id, partner_id),
  KEY idx_date_status (tenant_id, delivery_date, status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='거래명세서(출하) 헤더';

CREATE TABLE IF NOT EXISTS sales_delivery_items (
  delivery_item_id  CHAR(36)      NOT NULL,
  delivery_id       CHAR(36)      NOT NULL,
  tenant_id         CHAR(36)      NOT NULL,
  order_item_id     CHAR(36)      NULL,
  item_id           CHAR(36)      NOT NULL,
  warehouse_id      CHAR(36)      NOT NULL,
  qty               DECIMAL(15,3) NOT NULL,
  unit_price        DECIMAL(15,2) NOT NULL,
  supply_amount     DECIMAL(15,2) NOT NULL,
  vat_amount        DECIMAL(15,2) NOT NULL DEFAULT 0,
  PRIMARY KEY (delivery_item_id),
  KEY idx_delivery (delivery_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='거래명세서 라인';

CREATE TABLE IF NOT EXISTS returns (
  return_id     CHAR(36)      NOT NULL,
  tenant_id     CHAR(36)      NOT NULL,
  return_no     VARCHAR(20)   NOT NULL,
  return_type   ENUM('purchase','sales') NOT NULL,
  partner_id    CHAR(36)      NOT NULL,
  ref_id        CHAR(36)      NULL                            COMMENT '원본 전표 UUID. NULL=원본없이 반품',
  ref_type      VARCHAR(40)   NULL,
  return_date   DATE          NOT NULL,
  status        ENUM('draft','confirmed','cancelled') NOT NULL DEFAULT 'draft',
  total_amount  DECIMAL(15,2) NOT NULL DEFAULT 0,
  reason        VARCHAR(200)  NULL,
  created_at    DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by    CHAR(36)      NULL,
  PRIMARY KEY (return_id),
  UNIQUE KEY uq_return_no (tenant_id, return_no),
  KEY idx_partner (tenant_id, partner_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='반품 헤더 (매입·매출 통합)';

CREATE TABLE IF NOT EXISTS return_items (
  return_item_id  CHAR(36)      NOT NULL,
  return_id       CHAR(36)      NOT NULL,
  tenant_id       CHAR(36)      NOT NULL,
  item_id         CHAR(36)      NOT NULL,
  warehouse_id    CHAR(36)      NOT NULL,
  qty             DECIMAL(15,3) NOT NULL,
  unit_price      DECIMAL(15,2) NOT NULL,
  supply_amount   DECIMAL(15,2) NOT NULL,
  defect_type     VARCHAR(50)   NULL,
  PRIMARY KEY (return_item_id),
  KEY idx_return (return_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='반품 라인';

-- ────────────────────────────────────────
-- L5 : 회계·세금계산서·급여
-- ────────────────────────────────────────

CREATE TABLE IF NOT EXISTS account_subjects (
  account_id        CHAR(36)    NOT NULL,
  tenant_id         CHAR(36)    NULL                            COMMENT 'NULL=시스템 공통',
  account_code      VARCHAR(10) NOT NULL,
  account_name      VARCHAR(50) NOT NULL,
  account_type      ENUM('asset','liability','equity','revenue','expense') NOT NULL,
  parent_account_id CHAR(36)    NULL,
  level             TINYINT     NOT NULL DEFAULT 1,
  is_system         TINYINT(1)  NOT NULL DEFAULT 0,
  is_active         TINYINT(1)  NOT NULL DEFAULT 1,
  normal_balance    ENUM('debit','credit') NOT NULL,
  created_at        DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (account_id),
  UNIQUE KEY uq_account_code (tenant_id, account_code),
  KEY idx_type (account_type)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='계정과목 마스터 (세무사 검토 후 데이터 삽입)';

CREATE TABLE IF NOT EXISTS journal_entries (
  entry_id     CHAR(36)    NOT NULL,
  tenant_id    CHAR(36)    NOT NULL,
  entry_no     VARCHAR(20) NOT NULL,
  entry_date   DATE        NOT NULL,
  source_type  VARCHAR(30) NOT NULL                            COMMENT 'purchase|sales|payroll|manual|cancel',
  source_id    CHAR(36)    NULL,
  status       ENUM('draft','confirmed','closed') NOT NULL DEFAULT 'draft',
  employee_id  CHAR(36)    NULL,
  approver_id  CHAR(36)    NULL,
  approved_at  DATETIME    NULL,
  memo         VARCHAR(500) NULL,
  created_at   DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by   CHAR(36)    NULL,
  PRIMARY KEY (entry_id),
  UNIQUE KEY uq_entry_no  (tenant_id, entry_no),
  KEY idx_date_status (tenant_id, entry_date, status),
  KEY idx_source      (source_type, source_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='전표 헤더';

CREATE TABLE IF NOT EXISTS journal_lines (
  line_id     BIGINT        NOT NULL AUTO_INCREMENT,
  entry_id    CHAR(36)      NOT NULL,
  tenant_id   CHAR(36)      NOT NULL,
  account_id  CHAR(36)      NOT NULL,
  dc_type     ENUM('debit','credit') NOT NULL,
  amount      DECIMAL(15,2) NOT NULL,
  partner_id  CHAR(36)      NULL,
  dept_id     CHAR(36)      NULL,
  memo        VARCHAR(200)  NULL,
  created_at  DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (line_id),
  KEY idx_entry      (entry_id),
  KEY idx_account_ym (tenant_id, account_id),
  KEY idx_partner    (tenant_id, partner_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='전표 라인 — INSERT ONLY. 차변합계=대변합계';

CREATE TABLE IF NOT EXISTS tax_invoices (
  tax_invoice_id  CHAR(36)      NOT NULL,
  tenant_id       CHAR(36)      NOT NULL,
  invoice_no      VARCHAR(24)   NOT NULL,
  direction       ENUM('issue','receive') NOT NULL,
  issue_date      DATE          NOT NULL,
  partner_id      CHAR(36)      NOT NULL,
  supply_amount   DECIMAL(15,2) NOT NULL,
  vat_amount      DECIMAL(15,2) NOT NULL,
  total_amount    DECIMAL(15,2) NOT NULL,
  nts_status      ENUM('pending','sent','accepted','rejected','cancelled') NOT NULL DEFAULT 'pending',
  nts_sent_at     DATETIME      NULL,
  nts_result      VARCHAR(500)  NULL,
  receipt_id      CHAR(36)      NULL,
  delivery_id     CHAR(36)      NULL,
  created_at      DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by      CHAR(36)      NULL,
  PRIMARY KEY (tax_invoice_id),
  UNIQUE KEY uq_invoice_no  (tenant_id, invoice_no),
  KEY idx_direction_date (tenant_id, direction, issue_date),
  KEY idx_nts_status     (nts_status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='세금계산서 (홈택스 e세로 연동)';

CREATE TABLE IF NOT EXISTS tax_invoice_items (
  ti_item_id      CHAR(36)      NOT NULL,
  tax_invoice_id  CHAR(36)      NOT NULL,
  tenant_id       CHAR(36)      NOT NULL,
  item_name       VARCHAR(100)  NOT NULL,
  spec            VARCHAR(100)  NULL,
  qty             DECIMAL(15,3) NULL,
  unit_price      DECIMAL(15,2) NULL,
  supply_amount   DECIMAL(15,2) NOT NULL,
  vat_amount      DECIMAL(15,2) NOT NULL DEFAULT 0,
  PRIMARY KEY (ti_item_id),
  KEY idx_invoice (tax_invoice_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='세금계산서 품목 라인';

CREATE TABLE IF NOT EXISTS payroll_headers (
  payroll_id      CHAR(36)      NOT NULL,
  tenant_id       CHAR(36)      NOT NULL,
  payroll_ym      CHAR(7)       NOT NULL,
  status          ENUM('draft','confirmed','paid') NOT NULL DEFAULT 'draft',
  total_gross     DECIMAL(15,2) NOT NULL DEFAULT 0,
  total_deduction DECIMAL(15,2) NOT NULL DEFAULT 0,
  total_net       DECIMAL(15,2) NOT NULL DEFAULT 0,
  confirmed_by    CHAR(36)      NULL,
  confirmed_at    DATETIME      NULL,
  paid_at         DATETIME      NULL,
  created_at      DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (payroll_id),
  UNIQUE KEY uq_tenant_ym (tenant_id, payroll_ym)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='급여 헤더 (월별)';

CREATE TABLE IF NOT EXISTS payroll_details (
  detail_id     CHAR(36)     NOT NULL,
  payroll_id    CHAR(36)     NOT NULL,
  tenant_id     CHAR(36)     NOT NULL,
  employee_id   CHAR(36)     NOT NULL,
  base_salary   VARCHAR(200) NOT NULL  COMMENT 'AES-256 암호화',
  overtime_pay  VARCHAR(200) NULL      COMMENT 'AES-256 암호화',
  bonus         VARCHAR(200) NULL      COMMENT 'AES-256 암호화',
  gross_pay     VARCHAR(200) NOT NULL  COMMENT 'AES-256 암호화',
  net_pay       VARCHAR(200) NOT NULL  COMMENT 'AES-256 암호화',
  bank_name     VARCHAR(30)  NULL,
  bank_account  VARCHAR(200) NULL      COMMENT 'AES-256 암호화',
  PRIMARY KEY (detail_id),
  KEY idx_payroll    (payroll_id),
  KEY idx_employee   (tenant_id, employee_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='급여 상세 (사원별) — 전체 암호화';

CREATE TABLE IF NOT EXISTS payroll_deductions (
  deduction_id    CHAR(36)     NOT NULL,
  detail_id       CHAR(36)     NOT NULL,
  tenant_id       CHAR(36)     NOT NULL,
  deduction_type  VARCHAR(30)  NOT NULL COMMENT 'income_tax|health|pension|employment|other',
  amount          VARCHAR(200) NOT NULL COMMENT 'AES-256 암호화',
  memo            VARCHAR(100) NULL,
  PRIMARY KEY (deduction_id),
  KEY idx_detail (detail_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='급여 공제 항목';

-- ────────────────────────────────────────
-- L6 : 집계·원장·리포트
-- ────────────────────────────────────────

CREATE TABLE IF NOT EXISTS monthly_closing (
  closing_id    CHAR(36)     NOT NULL,
  tenant_id     CHAR(36)     NOT NULL,
  closing_ym    CHAR(7)      NOT NULL,
  status        ENUM('open','closed','reopened') NOT NULL DEFAULT 'open',
  closed_by     CHAR(36)     NULL,
  closed_at     DATETIME     NULL,
  reopened_by   CHAR(36)     NULL,
  reopened_at   DATETIME     NULL,
  memo          VARCHAR(200) NULL,
  PRIMARY KEY (closing_id),
  UNIQUE KEY uq_tenant_ym (tenant_id, closing_ym)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='월마감 관리';

CREATE TABLE IF NOT EXISTS report_snapshots (
  snapshot_id  CHAR(36)  NOT NULL,
  tenant_id    CHAR(36)  NOT NULL,
  report_type  ENUM('income_statement','balance_sheet','trial_balance') NOT NULL,
  ym           CHAR(7)   NOT NULL,
  report_data  JSON      NOT NULL,
  created_at   DATETIME  NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by   CHAR(36)  NULL,
  PRIMARY KEY (snapshot_id),
  KEY idx_tenant_type_ym (tenant_id, report_type, ym)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='리포트 스냅샷 (월마감 후)';

CREATE TABLE IF NOT EXISTS dashboard_configs (
  config_id   CHAR(36)    NOT NULL,
  tenant_id   CHAR(36)    NOT NULL,
  user_id     CHAR(36)    NULL,
  role        VARCHAR(30) NULL,
  widgets     JSON        NOT NULL,
  updated_at  DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (config_id),
  KEY idx_tenant_user (tenant_id, user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='대시보드 위젯 설정';

-- ────────────────────────────────────────
-- L6 : 실시간 VIEW
-- ────────────────────────────────────────

CREATE OR REPLACE VIEW v_stock_balance AS
SELECT
  tenant_id,
  item_id,
  warehouse_id,
  SUM(qty_in) - SUM(qty_out) AS balance,
  MAX(ledger_date)            AS last_move_date
FROM stock_ledger
WHERE ym >= DATE_FORMAT(NOW(), '%Y-%m')
GROUP BY tenant_id, item_id, warehouse_id;

CREATE OR REPLACE VIEW v_partner_balance AS
SELECT
  jl.tenant_id,
  jl.partner_id,
  SUM(CASE WHEN jl.dc_type = 'debit'  THEN jl.amount ELSE 0 END) AS total_debit,
  SUM(CASE WHEN jl.dc_type = 'credit' THEN jl.amount ELSE 0 END) AS total_credit,
  SUM(CASE WHEN jl.dc_type = 'debit'  THEN jl.amount ELSE 0 END)
  - SUM(CASE WHEN jl.dc_type = 'credit' THEN jl.amount ELSE 0 END) AS net_balance
FROM journal_lines jl
JOIN journal_entries je ON jl.entry_id = je.entry_id AND je.status = 'confirmed'
WHERE jl.partner_id IS NOT NULL
GROUP BY jl.tenant_id, jl.partner_id;

CREATE OR REPLACE VIEW v_account_balance AS
SELECT
  jl.tenant_id,
  jl.account_id,
  DATE_FORMAT(je.entry_date, '%Y-%m')             AS ym,
  SUM(CASE WHEN jl.dc_type = 'debit'  THEN jl.amount ELSE 0 END) AS total_debit,
  SUM(CASE WHEN jl.dc_type = 'credit' THEN jl.amount ELSE 0 END) AS total_credit
FROM journal_lines jl
JOIN journal_entries je ON jl.entry_id = je.entry_id AND je.status = 'confirmed'
GROUP BY jl.tenant_id, jl.account_id, DATE_FORMAT(je.entry_date, '%Y-%m');

SET FOREIGN_KEY_CHECKS = 1;

-- ============================================================
-- ============================================================

-- ────────────────────────────────────────
-- 모듈 레지스트리 (플러그인 모듈 관리)
-- ────────────────────────────────────────

CREATE TABLE IF NOT EXISTS module_registry (
  module_id    CHAR(36)     NOT NULL,
  tenant_id    CHAR(36)     NULL                              COMMENT 'NULL=시스템 전역 모듈',
  module_key   VARCHAR(50)  NOT NULL                          COMMENT 'accounting.coa | hr.advanced 등',
  version      VARCHAR(10)  NOT NULL DEFAULT '1.0',
  is_active    TINYINT(1)   NOT NULL DEFAULT 0,
  config       JSON         NULL                              COMMENT '모듈별 설정값',
  installed_at DATETIME     NULL,
  created_at   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (module_id),
  UNIQUE KEY uq_tenant_module (tenant_id, module_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='모듈 레지스트리 — 플러그인 활성화 관리';

-- ============================================================
-- END OF DDL  |  히트판 SaaS ERP v1.0 FINAL
-- ============================================================
