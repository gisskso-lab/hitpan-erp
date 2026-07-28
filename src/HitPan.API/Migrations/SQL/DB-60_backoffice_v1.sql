-- ============================================================================
-- DB-60 백오피스 v1.0 DDL — 25 테이블 일괄 박제
-- ============================================================================
-- 작성일       : 2026-05-26
-- 작성팀       : DB 매니저(Harvard·Oracle 30년) + 설계팀장 브라운킴
-- 정합 산출물  : DB_SCHEMA_BACKOFFICE.md / DOMAIN_MODEL_BACKOFFICE.md
--                API_SPEC_BACKOFFICE.md / PAYMENT_INTERFACE.md
-- 대상 DB     : hitpan_backoffice (본사 클라우드 1대, MariaDB 11.4.10)
--
-- 헌법 정합
--   #4  decimal       : 금액 DECIMAL(18,2) / 율 DECIMAL(7,4)
--   #17 InnoDB        : 25 테이블 일괄 ENGINE=InnoDB + utf8mb4_unicode_ci
--   #18 v3            : 본사 데이터 0 — 업무 데이터·카드 원본·CI·비밀번호 컬럼 0
--   #22 데이터 최소주의 : 토큰·메타·R2 URL만 / 사업자등록증·계약서 원본 미보관
--   #24 책임 분산     : 본사=SaaS 메타 / 고객=ERP 업무 / 제3자=PG·R2·CDN
--   #25 3대 원칙      : 정확하게(설계) 충족
--
-- 절대 금지
--   - 매출/매입/원장/거래처/직원/상품/재고/세금계산서 컬럼 0 (헌법 #18)
--   - 카드번호 원본 / CVC / 만료일 / 평문 비밀번호 컬럼 0
--   - tenant_id 파라미터 수신 컬럼 0 (JWT 클레임 only)
-- ============================================================================

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- ============================================================================
-- 1. 고객사 영역 (3 테이블)
-- ============================================================================

-- 1.1 tenants — 고객사 마스터 (랜딩 가입 시 Push 생성)
CREATE TABLE IF NOT EXISTS tenants (
    tenant_id              BINARY(16)     NOT NULL                  COMMENT 'UUID v7',
    company_name           VARCHAR(120)   NOT NULL                  COMMENT '상호',
    business_number        VARCHAR(20)    NOT NULL                  COMMENT '사업자등록번호 10자리',
    industry_category      VARCHAR(40)    NOT NULL                  COMMENT '서비스/도소매/제조/기타',
    status                 ENUM('ACTIVE','SUSPENDED','CANCELLED','DELETED') NOT NULL DEFAULT 'ACTIVE',
    current_tier_code      ENUM('LITE','STANDARD','PRO') NOT NULL   COMMENT '현재 구독 티어',
    device_count_max       SMALLINT UNSIGNED NOT NULL DEFAULT 1     COMMENT '허용 기기 대수',
    reseller_id            BINARY(16)     NULL                      COMMENT '대리점 매핑 (직판=NULL)',
    memo                   TEXT           NULL                      COMMENT '본사 운영 메모',
    created_at             DATETIME(6)    NOT NULL,
    updated_at             DATETIME(6)    NOT NULL,
    deleted_at             DATETIME(6)    NULL                      COMMENT 'soft delete',
    PRIMARY KEY (tenant_id),
    UNIQUE KEY uq_tenants_business_number (business_number),
    KEY idx_tenants_status (status),
    KEY idx_tenants_reseller (reseller_id),
    KEY idx_tenants_tier (current_tier_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='고객사 마스터 (랜딩 → 백오피스 Push, 헌법 #18 v3 정합)';

-- 1.2 tenant_business_info — 사업자 상세 (원본 파일은 R2 위임)
CREATE TABLE IF NOT EXISTS tenant_business_info (
    info_id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    tenant_id              BINARY(16)     NOT NULL,
    legal_name             VARCHAR(120)   NOT NULL                  COMMENT '법인명/상호',
    representative_name    VARCHAR(60)    NOT NULL                  COMMENT '대표자명',
    address                VARCHAR(255)   NOT NULL                  COMMENT '사업장 주소',
    tax_office             VARCHAR(40)    NULL                      COMMENT '관할세무서',
    business_type          VARCHAR(60)    NULL                      COMMENT '업태',
    business_category      VARCHAR(60)    NULL                      COMMENT '종목',
    business_license_url   VARCHAR(500)   NULL                      COMMENT 'R2 URL — 본사는 메타만 (헌법 #22)',
    created_at             DATETIME(6)    NOT NULL,
    updated_at             DATETIME(6)    NOT NULL,
    PRIMARY KEY (info_id),
    UNIQUE KEY uq_tbi_tenant (tenant_id),
    CONSTRAINT fk_tbi_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(tenant_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='사업자 상세 (원본 파일 R2 위임)';

-- 1.3 tenant_contacts — 연락처 (AES-256 암호화 컬럼)
CREATE TABLE IF NOT EXISTS tenant_contacts (
    contact_id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    tenant_id              BINARY(16)     NOT NULL,
    role                   ENUM('REPRESENTATIVE','BILLING','TECH') NOT NULL,
    name                   VARCHAR(60)    NOT NULL,
    phone_encrypted        VARBINARY(255) NOT NULL                  COMMENT 'AES-256 GCM',
    email_encrypted        VARBINARY(255) NOT NULL                  COMMENT 'AES-256 GCM',
    is_primary             TINYINT(1)     NOT NULL DEFAULT 0,
    created_at             DATETIME(6)    NOT NULL,
    updated_at             DATETIME(6)    NOT NULL,
    PRIMARY KEY (contact_id),
    KEY idx_tc_tenant_role (tenant_id, role),
    CONSTRAINT fk_tc_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(tenant_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='연락처 (AES-256 암호화 필수)';

-- ============================================================================
-- 2. 구독 영역 (3 테이블)
-- ============================================================================

-- 2.2 subscription_plans — 3티어 마스터 (시드 데이터 포함)
CREATE TABLE IF NOT EXISTS subscription_plans (
    plan_id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    tier_code              ENUM('LITE','STANDARD','PRO') NOT NULL,
    tier_name              VARCHAR(40)    NOT NULL,
    monthly_price          DECIMAL(18, 2) NOT NULL                  COMMENT '월 결제가 (헌법 #4)',
    annual_price           DECIMAL(18, 2) NOT NULL                  COMMENT '연 결제가 (월×11 할인)',
    device_count_max       SMALLINT UNSIGNED NOT NULL,
    feature_json           JSON           NULL                      COMMENT '기능 매트릭스',
    is_active              TINYINT(1)     NOT NULL DEFAULT 1,
    created_at             DATETIME(6)    NOT NULL,
    updated_at             DATETIME(6)    NOT NULL,
    PRIMARY KEY (plan_id),
    UNIQUE KEY uq_sp_tier (tier_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='구독 티어 마스터 (29k/59k/100k 시드)';

-- 2.1 subscriptions — 구독 상태 머신
CREATE TABLE IF NOT EXISTS subscriptions (
    subscription_id        BINARY(16)     NOT NULL                  COMMENT 'UUID v7',
    tenant_id              BINARY(16)     NOT NULL,
    tier_id                BIGINT UNSIGNED NOT NULL,
    billing_period         ENUM('MONTHLY','ANNUAL') NOT NULL,
    status                 ENUM('ACTIVE','PAST_DUE','CANCELLATION_SCHEDULED','CANCELLED','PAUSED') NOT NULL,
    starts_at              DATETIME(6)    NOT NULL,
    current_period_start   DATETIME(6)    NOT NULL,
    current_period_end     DATETIME(6)    NOT NULL,
    cancellation_scheduled_at DATETIME(6) NULL                      COMMENT '해지 예약일',
    cancelled_at           DATETIME(6)    NULL,
    auto_renew             TINYINT(1)     NOT NULL DEFAULT 1,
    created_at             DATETIME(6)    NOT NULL,
    updated_at             DATETIME(6)    NOT NULL,
    PRIMARY KEY (subscription_id),
    KEY idx_sub_tenant_status (tenant_id, status),
    KEY idx_sub_period_end (current_period_end),
    CONSTRAINT fk_sub_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(tenant_id),
    CONSTRAINT fk_sub_plan FOREIGN KEY (tier_id) REFERENCES subscription_plans(plan_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='구독 상태 머신 (STATE_MACHINE_SUBSCRIPTION 정합)';

-- 2.3 billing_cycles — 청구 주기 (사이클별 1행, 멱등성 보장)
CREATE TABLE IF NOT EXISTS billing_cycles (
    cycle_id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    subscription_id        BINARY(16)     NOT NULL,
    period_start           DATETIME(6)    NOT NULL,
    period_end             DATETIME(6)    NOT NULL,
    amount                 DECIMAL(18, 2) NOT NULL,
    status                 ENUM('SCHEDULED','CHARGED','FAILED','SKIPPED') NOT NULL DEFAULT 'SCHEDULED',
    payment_id             BINARY(16)     NULL                      COMMENT '결제 성공 시 매핑',
    retry_count            TINYINT UNSIGNED NOT NULL DEFAULT 0,
    next_retry_at          DATETIME(6)    NULL,
    created_at             DATETIME(6)    NOT NULL,
    updated_at             DATETIME(6)    NOT NULL,
    PRIMARY KEY (cycle_id),
    UNIQUE KEY uq_bc_sub_period (subscription_id, period_start),
    KEY idx_bc_status_retry (status, next_retry_at),
    CONSTRAINT fk_bc_sub FOREIGN KEY (subscription_id) REFERENCES subscriptions(subscription_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='청구 사이클 (재시도 D+0/+3/+7 멱등성)';

-- ============================================================================
-- 3. 계정 영역 (3 테이블, 본사 데이터 최소주의)
-- ============================================================================

-- 3.1 accounts — 메타정보만 (인증은 ERP 로컬)
CREATE TABLE IF NOT EXISTS accounts (
    account_id             BINARY(16)     NOT NULL                  COMMENT 'UUID v7',
    tenant_id              BINARY(16)     NOT NULL,
    email_encrypted        VARBINARY(255) NOT NULL                  COMMENT 'AES-256 (본사 보관 메타 only)',
    is_representative      TINYINT(1)     NOT NULL DEFAULT 0,
    last_login_at          DATETIME(6)    NULL                      COMMENT 'ERP에서 Pull',
    status                 ENUM('ACTIVE','SUSPENDED','DELETED') NOT NULL DEFAULT 'ACTIVE',
    created_at             DATETIME(6)    NOT NULL,
    updated_at             DATETIME(6)    NOT NULL,
    -- 절대 컬럼 없음: password_hash, password_plain, ci, di, rrn (헌법 #18 v3)
    PRIMARY KEY (account_id),
    KEY idx_acc_tenant (tenant_id),
    CONSTRAINT fk_acc_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(tenant_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='계정 메타 (비밀번호 보관 절대 금지 — 헌법 #18 v3)';

-- 3.2 account_roles
CREATE TABLE IF NOT EXISTS account_roles (
    role_id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    account_id             BINARY(16)     NOT NULL,
    role_code              ENUM('TENANT_ADMIN','EMPLOYEE','READONLY') NOT NULL,
    assigned_at            DATETIME(6)    NOT NULL,
    PRIMARY KEY (role_id),
    UNIQUE KEY uq_ar_account_role (account_id, role_code),
    CONSTRAINT fk_ar_account FOREIGN KEY (account_id) REFERENCES accounts(account_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='계정 역할';

-- 3.3 account_permissions
CREATE TABLE IF NOT EXISTS account_permissions (
    permission_id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    account_id             BINARY(16)     NOT NULL,
    permission_key         VARCHAR(80)    NOT NULL                  COMMENT '예: SALES.READ / PURCHASE.WRITE',
    granted_at             DATETIME(6)    NOT NULL,
    granted_by             BINARY(16)     NULL                      COMMENT '부여자 admin_user_id',
    PRIMARY KEY (permission_id),
    UNIQUE KEY uq_ap_account_key (account_id, permission_key),
    KEY idx_ap_key (permission_key),
    CONSTRAINT fk_ap_account FOREIGN KEY (account_id) REFERENCES accounts(account_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='계정 권한 (행 수준)';

-- ============================================================================
-- 4. 결제 영역 (4 테이블)
-- ============================================================================

-- 4.1 payments — 결제 원장 (PG 토큰만 보관, 헌법 #22)
CREATE TABLE IF NOT EXISTS payments (
    payment_id             BINARY(16)     NOT NULL                  COMMENT 'UUID v7',
    tenant_id              BINARY(16)     NOT NULL,
    subscription_id        BINARY(16)     NOT NULL,
    billing_cycle_id       BIGINT UNSIGNED NULL,
    amount                 DECIMAL(18, 2) NOT NULL,
    currency               CHAR(3)        NOT NULL DEFAULT 'KRW',
    provider               ENUM('TOSS','KCP','MOCK') NOT NULL,
    provider_payment_id    VARCHAR(120)   NOT NULL                  COMMENT 'PG측 결제키',
    payment_method_type    ENUM('CARD','BANK_TRANSFER','TAX_INVOICE') NOT NULL,
    status                 ENUM('PENDING','APPROVED','FAILED','REFUNDED','PARTIAL_REFUNDED') NOT NULL,
    paid_at                DATETIME(6)    NULL,
    failure_reason         VARCHAR(255)   NULL,
    created_at             DATETIME(6)    NOT NULL,
    updated_at             DATETIME(6)    NOT NULL,
    -- 절대 컬럼 없음: card_number, card_cvc, card_expiry (헌법 #22)
    PRIMARY KEY (payment_id),
    UNIQUE KEY uq_pay_provider_pid (provider, provider_payment_id),
    KEY idx_pay_tenant_paid (tenant_id, paid_at),
    KEY idx_pay_status (status),
    CONSTRAINT fk_pay_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(tenant_id),
    CONSTRAINT fk_pay_sub FOREIGN KEY (subscription_id) REFERENCES subscriptions(subscription_id),
    CONSTRAINT fk_pay_cycle FOREIGN KEY (billing_cycle_id) REFERENCES billing_cycles(cycle_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='결제 원장 (PG 토큰 only — 카드 원본 컬럼 0)';

-- 4.2 payment_methods — 빌링키 토큰만
CREATE TABLE IF NOT EXISTS payment_methods (
    method_id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    tenant_id              BINARY(16)     NOT NULL,
    provider               ENUM('TOSS','KCP') NOT NULL,
    provider_billing_key   VARCHAR(255)   NOT NULL                  COMMENT 'PG측 빌링키 (재과금 토큰)',
    card_last4             CHAR(4)        NULL                      COMMENT '마지막 4자리만 (PCI-DSS)',
    card_brand             VARCHAR(20)    NULL                      COMMENT 'VISA/MASTER/LOCAL',
    is_default             TINYINT(1)     NOT NULL DEFAULT 0,
    registered_at          DATETIME(6)    NOT NULL,
    expired_at             DATETIME(6)    NULL                      COMMENT '빌링키 만료 (PG 통보)',
    -- 절대 컬럼 없음: card_full_number, cvc, expiry_full (헌법 #22)
    PRIMARY KEY (method_id),
    UNIQUE KEY uq_pm_provider_billing (provider, provider_billing_key),
    KEY idx_pm_tenant_default (tenant_id, is_default),
    CONSTRAINT fk_pm_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(tenant_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='결제수단 토큰 (PCI-DSS 정합)';

-- 4.3 payment_refunds
CREATE TABLE IF NOT EXISTS payment_refunds (
    refund_id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    payment_id             BINARY(16)     NOT NULL,
    amount                 DECIMAL(18, 2) NOT NULL                  COMMENT '환불액 (부분 환불 허용)',
    reason                 VARCHAR(255)   NOT NULL,
    provider_refund_id     VARCHAR(120)   NULL,
    status                 ENUM('PENDING','APPROVED','FAILED') NOT NULL DEFAULT 'PENDING',
    refunded_at            DATETIME(6)    NULL,
    processed_by           BINARY(16)     NOT NULL                  COMMENT '처리한 admin_user_id',
    created_at             DATETIME(6)    NOT NULL,
    PRIMARY KEY (refund_id),
    KEY idx_pr_payment (payment_id),
    CONSTRAINT fk_pr_payment FOREIGN KEY (payment_id) REFERENCES payments(payment_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='결제 환불 (전자상거래법 §17 청약철회 정합)';

-- 4.4 invoices — 영수증·세금계산서 메타 (원본 외부)
CREATE TABLE IF NOT EXISTS invoices (
    invoice_id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    payment_id             BINARY(16)     NOT NULL,
    invoice_type           ENUM('RECEIPT','TAX_INVOICE') NOT NULL,
    external_url           VARCHAR(500)   NOT NULL                  COMMENT '이세로·메이크빌·PG 위임',
    external_id            VARCHAR(120)   NULL                      COMMENT '외부 발급 ID',
    issued_at              DATETIME(6)    NOT NULL,
    PRIMARY KEY (invoice_id),
    KEY idx_inv_payment (payment_id),
    CONSTRAINT fk_inv_payment FOREIGN KEY (payment_id) REFERENCES payments(payment_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='영수증/세금계산서 메타 (원본 외부 위임)';

-- ============================================================================
-- 5. 대리점 영역 (5 테이블)
-- ============================================================================

-- 5.1 resellers
CREATE TABLE IF NOT EXISTS resellers (
    reseller_id            BINARY(16)     NOT NULL                  COMMENT 'UUID v7',
    company_name           VARCHAR(120)   NOT NULL,
    business_number        VARCHAR(20)    NOT NULL,
    representative_name    VARCHAR(60)    NOT NULL,
    contact_phone_encrypted VARBINARY(255) NOT NULL                 COMMENT 'AES-256',
    contact_email_encrypted VARBINARY(255) NOT NULL                 COMMENT 'AES-256',
    commission_rate        DECIMAL(7, 4)  NOT NULL DEFAULT 0.1000   COMMENT '기본 수수료율 (헌법 #4)',
    status                 ENUM('ACTIVE','SUSPENDED','TERMINATED') NOT NULL DEFAULT 'ACTIVE',
    joined_at              DATETIME(6)    NOT NULL,
    terminated_at          DATETIME(6)    NULL,
    created_at             DATETIME(6)    NOT NULL,
    updated_at             DATETIME(6)    NOT NULL,
    PRIMARY KEY (reseller_id),
    UNIQUE KEY uq_rs_business_number (business_number),
    KEY idx_rs_status (status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='대리점 마스터';

-- 5.2 reseller_contracts
CREATE TABLE IF NOT EXISTS reseller_contracts (
    contract_id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    reseller_id            BINARY(16)     NOT NULL,
    contract_type          ENUM('STANDARD','PARTNER','EXCLUSIVE') NOT NULL,
    effective_from         DATE           NOT NULL,
    effective_to           DATE           NULL,
    commission_rate        DECIMAL(7, 4)  NOT NULL                  COMMENT '계약별 오버라이드율',
    signed_at              DATETIME(6)    NOT NULL,
    contract_file_url      VARCHAR(500)   NULL                      COMMENT 'R2 — 본사 DB 메타만',
    created_at             DATETIME(6)    NOT NULL,
    PRIMARY KEY (contract_id),
    KEY idx_rc_reseller_effective (reseller_id, effective_from),
    CONSTRAINT fk_rc_reseller FOREIGN KEY (reseller_id) REFERENCES resellers(reseller_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='대리점 계약';

-- 5.3 reseller_customers — 대리점↔고객사 매핑
CREATE TABLE IF NOT EXISTS reseller_customers (
    mapping_id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    reseller_id            BINARY(16)     NOT NULL,
    tenant_id              BINARY(16)     NOT NULL,
    assigned_at            DATETIME(6)    NOT NULL,
    released_at            DATETIME(6)    NULL                      COMMENT 'NULL=현재 매핑',
    assigned_by            BINARY(16)     NULL                      COMMENT 'admin_user_id',
    PRIMARY KEY (mapping_id),
    KEY idx_rcust_reseller (reseller_id),
    KEY idx_rcust_tenant_active (tenant_id, released_at),
    CONSTRAINT fk_rcust_reseller FOREIGN KEY (reseller_id) REFERENCES resellers(reseller_id),
    CONSTRAINT fk_rcust_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(tenant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='대리점↔고객사 매핑 (이력 보존)';

-- 5.4 commissions — 수수료 계산 원장 (INSERT ONLY)
CREATE TABLE IF NOT EXISTS commissions (
    commission_id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    reseller_id            BINARY(16)     NOT NULL,
    tenant_id              BINARY(16)     NOT NULL,
    payment_id             BINARY(16)     NOT NULL,
    period_year            SMALLINT UNSIGNED NOT NULL,
    period_month           TINYINT UNSIGNED NOT NULL,
    base_amount            DECIMAL(18, 2) NOT NULL                  COMMENT '결제 원금',
    commission_rate        DECIMAL(7, 4)  NOT NULL                  COMMENT '적용된 수수료율',
    commission_amount      DECIMAL(18, 2) NOT NULL                  COMMENT '수수료액',
    calculated_at          DATETIME(6)    NOT NULL,
    PRIMARY KEY (commission_id),
    UNIQUE KEY uq_com_payment (payment_id)                          COMMENT '결제 1건당 수수료 1행',
    KEY idx_com_reseller_period (reseller_id, period_year, period_month),
    CONSTRAINT fk_com_reseller FOREIGN KEY (reseller_id) REFERENCES resellers(reseller_id),
    CONSTRAINT fk_com_payment FOREIGN KEY (payment_id) REFERENCES payments(payment_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='수수료 계산 원장 (INSERT ONLY — 헌법 #3)';

-- 5.5 commission_settlements — 월별 정산
CREATE TABLE IF NOT EXISTS commission_settlements (
    settlement_id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    reseller_id            BINARY(16)     NOT NULL,
    period_year            SMALLINT UNSIGNED NOT NULL,
    period_month           TINYINT UNSIGNED NOT NULL,
    total_amount           DECIMAL(18, 2) NOT NULL,
    settle_method          ENUM('BANK_TRANSFER','TAX_INVOICE_OFFSET') NOT NULL,
    status                 ENUM('SCHEDULED','SETTLED','HOLD') NOT NULL DEFAULT 'SCHEDULED',
    settled_at             DATETIME(6)    NULL,
    settled_by             BINARY(16)     NULL                      COMMENT 'admin_user_id',
    memo                   VARCHAR(255)   NULL,
    created_at             DATETIME(6)    NOT NULL,
    updated_at             DATETIME(6)    NOT NULL,
    PRIMARY KEY (settlement_id),
    UNIQUE KEY uq_cs_reseller_period (reseller_id, period_year, period_month),
    CONSTRAINT fk_cs_reseller FOREIGN KEY (reseller_id) REFERENCES resellers(reseller_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='대리점 월별 정산';

-- ============================================================================
-- 6. 본사 직원 영역 (4 테이블, ERP tenant_users와 절대 분리 — 헌법 #7)
-- ============================================================================

-- 6.1 admin_users
CREATE TABLE IF NOT EXISTS admin_users (
    admin_user_id          BINARY(16)     NOT NULL                  COMMENT 'UUID v7',
    username               VARCHAR(60)    NOT NULL,
    email_encrypted        VARBINARY(255) NOT NULL                  COMMENT 'AES-256',
    password_hash          VARCHAR(255)   NOT NULL                  COMMENT 'Argon2id (본사 직원만)',
    display_name           VARCHAR(60)    NOT NULL,
    status                 ENUM('ACTIVE','SUSPENDED','LOCKED') NOT NULL DEFAULT 'ACTIVE',
    last_login_at          DATETIME(6)    NULL,
    last_password_changed_at DATETIME(6)  NULL,
    failed_login_count     TINYINT UNSIGNED NOT NULL DEFAULT 0,
    created_at             DATETIME(6)    NOT NULL,
    updated_at             DATETIME(6)    NOT NULL,
    deleted_at             DATETIME(6)    NULL,
    PRIMARY KEY (admin_user_id),
    UNIQUE KEY uq_au_username (username),
    KEY idx_au_status (status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='본사 직원 (ERP tenant_users와 절대 분리)';

-- 6.2 admin_roles
CREATE TABLE IF NOT EXISTS admin_roles (
    role_id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    admin_user_id          BINARY(16)     NOT NULL,
    role_code              ENUM('SUPER_ADMIN','OPS','SALES','SUPPORT','FINANCE','RESELLER_ADMIN','READ_ONLY') NOT NULL,
    assigned_at            DATETIME(6)    NOT NULL,
    assigned_by            BINARY(16)     NULL,
    PRIMARY KEY (role_id),
    UNIQUE KEY uq_aroles_user_role (admin_user_id, role_code),
    CONSTRAINT fk_aroles_user FOREIGN KEY (admin_user_id) REFERENCES admin_users(admin_user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='본사 직원 역할';

-- 6.3 admin_sessions
CREATE TABLE IF NOT EXISTS admin_sessions (
    session_id             BINARY(16)     NOT NULL                  COMMENT 'UUID v7',
    admin_user_id          BINARY(16)     NOT NULL,
    refresh_token_hash     CHAR(64)       NOT NULL                  COMMENT 'SHA-256(refresh_token)',
    client_ip              VARCHAR(45)    NOT NULL                  COMMENT 'IPv4/IPv6',
    user_agent             VARCHAR(255)   NULL,
    issued_at              DATETIME(6)    NOT NULL,
    expires_at             DATETIME(6)    NOT NULL,
    revoked_at             DATETIME(6)    NULL,
    PRIMARY KEY (session_id),
    KEY idx_as_user_expires (admin_user_id, expires_at),
    KEY idx_as_token_hash (refresh_token_hash),
    CONSTRAINT fk_as_user FOREIGN KEY (admin_user_id) REFERENCES admin_users(admin_user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='본사 직원 세션 (Refresh Token)';

-- 6.4 admin_2fa — 본사 직원 전원 강제
CREATE TABLE IF NOT EXISTS admin_2fa (
    tfa_id                 BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    admin_user_id          BINARY(16)     NOT NULL,
    totp_secret_encrypted  VARBINARY(255) NOT NULL                  COMMENT 'AES-256 (TOTP seed)',
    backup_codes_encrypted VARBINARY(1024) NOT NULL                 COMMENT 'AES-256 (10개 백업)',
    enrolled_at            DATETIME(6)    NOT NULL,
    last_used_at           DATETIME(6)    NULL,
    PRIMARY KEY (tfa_id),
    UNIQUE KEY uq_2fa_user (admin_user_id),
    CONSTRAINT fk_2fa_user FOREIGN KEY (admin_user_id) REFERENCES admin_users(admin_user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='본사 직원 2FA (TOTP, 헌법 #25 안전하게)';

-- ============================================================================
-- 7. 모니터링 영역 (3 테이블, 메타만 — 헌법 #18 v3)
-- ============================================================================

-- 7.1 tenant_heartbeats — 워치독 ping (업무 데이터 0건)
CREATE TABLE IF NOT EXISTS tenant_heartbeats (
    heartbeat_id           BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    tenant_id              BINARY(16)     NOT NULL,
    device_id              VARCHAR(64)    NOT NULL                  COMMENT '워치독 디바이스 식별자',
    pinged_at              DATETIME(6)    NOT NULL,
    cloudflared_status     ENUM('OK','DOWN','RECOVERING') NOT NULL,
    mariadb_status         ENUM('OK','DOWN','RECOVERING') NOT NULL,
    erp_status             ENUM('OK','DOWN','RECOVERING') NOT NULL,
    version                VARCHAR(20)    NULL                      COMMENT 'ERP 버전',
    -- 절대 컬럼 없음: sales_count, purchase_count, partner_count (헌법 #18 v3)
    PRIMARY KEY (heartbeat_id, pinged_at),
    KEY idx_hb_tenant_pinged (tenant_id, pinged_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='워치독 ping 메타 (업무 카운터 0건)'
  PARTITION BY RANGE (TO_DAYS(pinged_at)) (
    PARTITION p_2026_05 VALUES LESS THAN (TO_DAYS('2026-06-01')),
    PARTITION p_2026_06 VALUES LESS THAN (TO_DAYS('2026-07-01')),
    PARTITION p_2026_07 VALUES LESS THAN (TO_DAYS('2026-08-01')),
    PARTITION p_2026_08 VALUES LESS THAN (TO_DAYS('2026-09-01')),
    PARTITION p_2026_09 VALUES LESS THAN (TO_DAYS('2026-10-01')),
    PARTITION p_max     VALUES LESS THAN MAXVALUE
  );

-- 7.2 tenant_usage_metrics — 라이선스 검증용
CREATE TABLE IF NOT EXISTS tenant_usage_metrics (
    metric_id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    tenant_id              BINARY(16)     NOT NULL,
    metric_date            DATE           NOT NULL,
    device_count_active    SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    db_size_mb             INT UNSIGNED   NOT NULL DEFAULT 0        COMMENT '라이선스 검증용',
    last_collected_at      DATETIME(6)    NOT NULL,
    PRIMARY KEY (metric_id),
    UNIQUE KEY uq_tum_tenant_date (tenant_id, metric_date),
    CONSTRAINT fk_tum_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(tenant_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='고객사 사용량 메트릭 (메타만)';

-- 7.3 tenant_alerts
CREATE TABLE IF NOT EXISTS tenant_alerts (
    alert_id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    tenant_id              BINARY(16)     NOT NULL,
    alert_type             ENUM('HEARTBEAT_LOST','PAYMENT_FAILED','DISK_FULL','CERT_EXPIRING','LICENSE_EXCEEDED','SECURITY_INCIDENT') NOT NULL,
    severity               ENUM('INFO','WARNING','CRITICAL') NOT NULL,
    message                VARCHAR(500)   NOT NULL,
    triggered_at           DATETIME(6)    NOT NULL,
    resolved_at            DATETIME(6)    NULL,
    assigned_to            BINARY(16)     NULL                      COMMENT 'admin_user_id',
    resolution_memo        VARCHAR(500)   NULL,
    PRIMARY KEY (alert_id),
    KEY idx_ta_tenant_triggered (tenant_id, triggered_at),
    KEY idx_ta_unresolved (resolved_at, severity),
    CONSTRAINT fk_ta_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(tenant_id) ON DELETE CASCADE,
    CONSTRAINT fk_ta_admin FOREIGN KEY (assigned_to) REFERENCES admin_users(admin_user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='고객사 알림 (CS 통합)';

-- ============================================================================
-- 시드 데이터 — subscription_plans 3티어 (29k / 59k / 100k)
-- ============================================================================
INSERT INTO subscription_plans
    (tier_code, tier_name, monthly_price, annual_price, device_count_max, feature_json, is_active, created_at, updated_at)
VALUES
    ('LITE',     '히트판 LITE',     29000.00,  319000.00, 1,
        JSON_OBJECT('users', 3, 'storage_gb', 5, 'ai_tokens', 100000, 'support', 'CHAT'),
        1, NOW(6), NOW(6)),
    ('STANDARD', '히트판 STANDARD', 59000.00,  649000.00, 3,
        JSON_OBJECT('users', 10, 'storage_gb', 20, 'ai_tokens', 500000, 'support', 'CHAT_PHONE'),
        1, NOW(6), NOW(6)),
    ('PRO',      '히트판 PRO',     100000.00, 1100000.00, 10,
        JSON_OBJECT('users', 999, 'storage_gb', 100, 'ai_tokens', 3000000, 'support', 'REMOTE_CS'),
        1, NOW(6), NOW(6))
ON DUPLICATE KEY UPDATE
    monthly_price = VALUES(monthly_price),
    annual_price  = VALUES(annual_price),
    device_count_max = VALUES(device_count_max),
    feature_json  = VALUES(feature_json),
    updated_at    = NOW(6);

SET FOREIGN_KEY_CHECKS = 1;

-- ============================================================================
-- END OF DB-60_backoffice_v1.sql
-- 25 테이블 + 3 시드 = 박제 완료
-- ============================================================================
