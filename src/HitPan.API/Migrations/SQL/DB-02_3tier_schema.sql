-- DB-02 3-tier multi-tenant schema
-- Applied on: 2026-04-10

-- [1] platforms
CREATE TABLE platforms (
  platform_id    VARCHAR(36)   NOT NULL PRIMARY KEY,
  platform_name  VARCHAR(100)  NOT NULL COMMENT '히트판',
  domain         VARCHAR(100)  NULL,
  contact_email  VARCHAR(100)  NULL,
  contact_tel    VARCHAR(20)   NULL,
  is_active      TINYINT(1)    NOT NULL DEFAULT 1,
  created_at     DATETIME(6)   NOT NULL DEFAULT NOW(6),
  updated_at     DATETIME(6)   NOT NULL DEFAULT NOW(6)
                 ON UPDATE NOW(6)
);

CREATE TRIGGER trg_platforms_uuid
BEFORE INSERT ON platforms FOR EACH ROW
SET NEW.platform_id = IF(NEW.platform_id IS NULL OR NEW.platform_id = '', UUID(), NEW.platform_id);

-- [2] resellers
CREATE TABLE resellers (
  reseller_id      VARCHAR(36)    NOT NULL PRIMARY KEY,
  platform_id      VARCHAR(36)    NOT NULL,
  reseller_code    VARCHAR(20)    NOT NULL,
  reseller_name    VARCHAR(100)   NOT NULL,
  biz_no           VARCHAR(12)    NULL,
  ceo_name         VARCHAR(50)    NULL,
  tel              VARCHAR(20)    NULL,
  email            VARCHAR(100)   NULL,
  address          VARCHAR(200)   NULL,
  region           VARCHAR(100)   NULL COMMENT '담당지역',
  commission_grade VARCHAR(20)    NOT NULL DEFAULT 'basic'
    COMMENT 'basic/silver/gold/platinum/diamond',
  commission_rate  DECIMAL(5,2)   NOT NULL DEFAULT 15.00
    COMMENT '현재 적용 수수료율',
  bank_name        VARCHAR(30)    NULL,
  bank_account     VARCHAR(200)   NULL,
  account_holder   VARCHAR(30)    NULL,
  contract_date    DATE           NULL,
  is_active        TINYINT(1)     NOT NULL DEFAULT 1,
  created_at       DATETIME(6)    NOT NULL DEFAULT NOW(6),
  created_by       VARCHAR(36)    NULL,
  updated_at       DATETIME(6)    NOT NULL DEFAULT NOW(6)
                   ON UPDATE NOW(6),
  updated_by       VARCHAR(36)    NULL,
  CONSTRAINT fk_res_platform
    FOREIGN KEY (platform_id) REFERENCES platforms(platform_id)
);
CREATE UNIQUE INDEX uq_reseller_code ON resellers (reseller_code);
CREATE INDEX idx_res_platform ON resellers (platform_id);
CREATE INDEX idx_res_grade ON resellers (commission_grade);

CREATE TRIGGER trg_resellers_uuid
BEFORE INSERT ON resellers FOR EACH ROW
SET NEW.reseller_id = IF(NEW.reseller_id IS NULL OR NEW.reseller_id = '', UUID(), NEW.reseller_id);

-- [3] tenants changes
ALTER TABLE tenants
  ADD COLUMN platform_id VARCHAR(36) NULL
    COMMENT '본사 ID' AFTER tenant_id,
  ADD COLUMN max_users TINYINT UNSIGNED NOT NULL DEFAULT 3
    COMMENT '최대 사용자 수' AFTER reseller_id;

ALTER TABLE tenants
  MODIFY COLUMN status VARCHAR(20) NOT NULL DEFAULT 'trial'
    COMMENT 'trial/active/suspended/cancelled';

ALTER TABLE tenants
  ADD CONSTRAINT fk_tenant_reseller
    FOREIGN KEY (reseller_id) REFERENCES resellers(reseller_id);

-- [4] users changes
ALTER TABLE users
  ADD COLUMN account_type VARCHAR(20) NOT NULL DEFAULT 'tenant_user'
    COMMENT 'platform_admin/reseller_admin/tenant_admin/tenant_user'
    AFTER role,
  ADD COLUMN reseller_id VARCHAR(36) NULL
    COMMENT '대리점 관리자인 경우' AFTER account_type,
  ADD COLUMN platform_id VARCHAR(36) NULL
    COMMENT '본사 관리자인 경우' AFTER reseller_id;
CREATE INDEX idx_users_account_type ON users (account_type);
CREATE INDEX idx_users_reseller ON users (reseller_id);

-- [5] reseller_commission_policies
CREATE TABLE reseller_commission_policies (
  id                  VARCHAR(36)   NOT NULL PRIMARY KEY,
  platform_id         VARCHAR(36)   NOT NULL,
  grade               VARCHAR(20)   NOT NULL
    COMMENT 'basic/silver/gold/platinum/diamond',
  grade_name          VARCHAR(20)   NOT NULL
    COMMENT '일반/실버/골드/플래티넘/다이아',
  commission_rate     DECIMAL(5,2)  NOT NULL COMMENT '수수료율 %',
  min_monthly_revenue DECIMAL(15,2) NOT NULL DEFAULT 0
    COMMENT '등급 조건 — 월 구독료 합계',
  description         VARCHAR(200)  NULL,
  is_active           TINYINT(1)    NOT NULL DEFAULT 1,
  updated_by          VARCHAR(36)   NULL,
  created_at          DATETIME(6)   NOT NULL DEFAULT NOW(6),
  updated_at          DATETIME(6)   NOT NULL DEFAULT NOW(6)
                    ON UPDATE NOW(6),
  CONSTRAINT fk_rcp_platform
    FOREIGN KEY (platform_id) REFERENCES platforms(platform_id)
);
CREATE UNIQUE INDEX uq_rcp_grade
  ON reseller_commission_policies (platform_id, grade);

CREATE TRIGGER trg_rcp_uuid
BEFORE INSERT ON reseller_commission_policies FOR EACH ROW
SET NEW.id = IF(NEW.id IS NULL OR NEW.id = '', UUID(), NEW.id);

-- [6] reseller_promotions
CREATE TABLE reseller_promotions (
  id               VARCHAR(36)   NOT NULL PRIMARY KEY,
  platform_id      VARCHAR(36)   NOT NULL,
  promo_name       VARCHAR(100)  NOT NULL COMMENT '프로모션명',
  target_grade     VARCHAR(20)   NOT NULL DEFAULT 'all'
    COMMENT 'all/basic/silver/gold/platinum/diamond',
  extra_rate       DECIMAL(5,2)  NOT NULL DEFAULT 0
    COMMENT '추가 수수료율 %',
  start_date       DATE          NOT NULL,
  end_date         DATE          NOT NULL,
  description      VARCHAR(500)  NULL,
  is_active        TINYINT(1)    NOT NULL DEFAULT 1,
  created_by       VARCHAR(36)   NULL,
  created_at       DATETIME(6)   NOT NULL DEFAULT NOW(6),
  updated_at       DATETIME(6)   NOT NULL DEFAULT NOW(6)
                   ON UPDATE NOW(6),
  CONSTRAINT fk_rp_platform
    FOREIGN KEY (platform_id) REFERENCES platforms(platform_id)
);
CREATE INDEX idx_rp_date
  ON reseller_promotions (start_date, end_date);

CREATE TRIGGER trg_rp_uuid
BEFORE INSERT ON reseller_promotions FOR EACH ROW
SET NEW.id = IF(NEW.id IS NULL OR NEW.id = '', UUID(), NEW.id);

-- [7] reseller_revenues
CREATE TABLE reseller_revenues (
  id                 VARCHAR(36)   NOT NULL PRIMARY KEY,
  reseller_id        VARCHAR(36)   NOT NULL,
  platform_id        VARCHAR(36)   NOT NULL,
  `year_month`       VARCHAR(7)    NOT NULL COMMENT '2026-04',
  customer_count     INT           NOT NULL DEFAULT 0,
  new_customers      INT           NOT NULL DEFAULT 0,
  churned_customers  INT           NOT NULL DEFAULT 0,
  total_subscription DECIMAL(15,2) NOT NULL DEFAULT 0,
  commission_grade   VARCHAR(20)   NOT NULL,
  commission_rate    DECIMAL(5,2)  NOT NULL,
  promo_rate         DECIMAL(5,2)  NOT NULL DEFAULT 0,
  final_rate         DECIMAL(5,2)  NOT NULL,
  commission_amount  DECIMAL(15,2) NOT NULL DEFAULT 0,
  status             VARCHAR(20)   NOT NULL DEFAULT 'pending'
    COMMENT 'pending/confirmed/paid',
  confirmed_at       DATETIME(6)   NULL,
  confirmed_by       VARCHAR(36)   NULL,
  created_at         DATETIME(6)   NOT NULL DEFAULT NOW(6),
  updated_at         DATETIME(6)   NOT NULL DEFAULT NOW(6)
                   ON UPDATE NOW(6),
  CONSTRAINT fk_rrev_reseller
    FOREIGN KEY (reseller_id) REFERENCES resellers(reseller_id),
  CONSTRAINT fk_rrev_platform
    FOREIGN KEY (platform_id) REFERENCES platforms(platform_id)
);
CREATE UNIQUE INDEX uq_rrev_month
  ON reseller_revenues (reseller_id, `year_month`);
CREATE INDEX idx_rrev_status ON reseller_revenues (status);

CREATE TRIGGER trg_rrev_uuid
BEFORE INSERT ON reseller_revenues FOR EACH ROW
SET NEW.id = IF(NEW.id IS NULL OR NEW.id = '', UUID(), NEW.id);

-- [8] reseller_payouts
CREATE TABLE reseller_payouts (
  id             VARCHAR(36)   NOT NULL PRIMARY KEY,
  reseller_id    VARCHAR(36)   NOT NULL,
  platform_id    VARCHAR(36)   NOT NULL,
  `year_month`   VARCHAR(7)    NOT NULL,
  payout_amount  DECIMAL(15,2) NOT NULL,
  payout_date    DATE          NULL,
  bank_name      VARCHAR(30)   NULL,
  bank_account   VARCHAR(200)  NULL,
  account_holder VARCHAR(30)   NULL,
  memo           VARCHAR(500)  NULL,
  status         VARCHAR(20)   NOT NULL DEFAULT 'pending'
    COMMENT 'pending/completed',
  created_by     VARCHAR(36)   NULL,
  created_at     DATETIME(6)   NOT NULL DEFAULT NOW(6),
  updated_at     DATETIME(6)   NOT NULL DEFAULT NOW(6)
                 ON UPDATE NOW(6),
  CONSTRAINT fk_rpay_reseller
    FOREIGN KEY (reseller_id) REFERENCES resellers(reseller_id)
);
CREATE UNIQUE INDEX uq_rpay_month
  ON reseller_payouts (reseller_id, `year_month`);

CREATE TRIGGER trg_rpay_uuid
BEFORE INSERT ON reseller_payouts FOR EACH ROW
SET NEW.id = IF(NEW.id IS NULL OR NEW.id = '', UUID(), NEW.id);

-- [9] subscriptions changes
ALTER TABLE subscriptions
  MODIFY COLUMN plan_type VARCHAR(20) NOT NULL DEFAULT 'basic'
    COMMENT 'basic/pro/enterprise',
  MODIFY COLUMN status VARCHAR(20) NOT NULL DEFAULT 'trial'
    COMMENT 'trial/active/suspended/cancelled',
  ADD COLUMN reseller_id VARCHAR(36) NULL
    COMMENT '구독 담당 대리점' AFTER tenant_id,
  ADD COLUMN platform_id VARCHAR(36) NULL
    COMMENT '본사' AFTER reseller_id,
  ADD COLUMN annual_discount_rate DECIMAL(5,2) NOT NULL DEFAULT 0
    COMMENT '연간결제 할인율' AFTER extra_fee_per_user;
CREATE INDEX idx_sub_reseller ON subscriptions (reseller_id);
CREATE INDEX idx_sub_status ON subscriptions (status);

-- [10] seed + updates
INSERT INTO platforms (
  platform_id, platform_name, contact_email, is_active
) VALUES (
  'a0000000-0000-0000-0000-000000000001',
  '히트판 본사', 'admin@hitpan.kr', 1
);

INSERT INTO reseller_commission_policies
  (platform_id, grade, grade_name, commission_rate, min_monthly_revenue, description)
VALUES
  ('a0000000-0000-0000-0000-000000000001', 'basic',    '일반',    15.00,       0, '기본 등급'),
  ('a0000000-0000-0000-0000-000000000001', 'silver',   '실버',    20.00,  500000, '월 50만원 이상'),
  ('a0000000-0000-0000-0000-000000000001', 'gold',     '골드',    25.00, 1500000, '월 150만원 이상'),
  ('a0000000-0000-0000-0000-000000000001', 'platinum', '플래티넘', 30.00, 3000000, '월 300만원 이상'),
  ('a0000000-0000-0000-0000-000000000001', 'diamond',  '다이아',  35.00, 5000000, '월 500만원 이상');

UPDATE tenants
SET platform_id = 'a0000000-0000-0000-0000-000000000001'
WHERE tenant_code = 'HP-000001';

UPDATE users
SET platform_id = 'a0000000-0000-0000-0000-000000000001',
    account_type = 'platform_admin'
WHERE email = 'admin@hitpan.kr';
