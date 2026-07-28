-- DB-23: 구독결제 관리 (사장님 결재 2026-04-29)
--
-- 결제수단 2갈래: 토스페이먼츠 (사업자카드 빌링) / 무통장입금 (본사 가상계좌)
-- §절대원칙 #18: 본사 가상계좌 = SaaS 운영 데이터 (구독료) → OK.
-- §절대원칙 #5: provider_billing_key (토스 빌링키) AES-256 암호화 저장.
-- §절대원칙 #4: 모든 금액 컬럼 DECIMAL.
-- §절대원칙 #17: ENGINE=InnoDB.
--
-- 5개 테이블:
--   1) billing_settings           — 본사 입금계좌·운영 설정 (단일행, tenant 경계 따름)
--   2) billing_payment_methods    — 등록된 결제수단 (토스 빌링키, 무통장 정보)
--   3) billing_invoices           — 청구서/인보이스
--   4) billing_payment_attempts   — 결제 시도 로그 (감사·디버깅)
--   5) billing_subscriptions      — 활성 구독 (플랜·결제일·다음 결제일)

-- ─────────────────────────────────────────────
-- 1) 본사 입금계좌·운영 설정
-- ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS billing_settings (
    setting_id          VARCHAR(36)  NOT NULL,
    tenant_id           VARCHAR(36)  NOT NULL,
    head_office_bank    VARCHAR(60)  NULL  COMMENT '본사 입금 은행명',
    head_office_account VARCHAR(60)  NULL  COMMENT '본사 입금 계좌번호',
    head_office_holder  VARCHAR(60)  NULL  COMMENT '본사 입금 예금주',
    auto_billing_day    TINYINT      NOT NULL DEFAULT 1   COMMENT '자동결제일 (1~28)',
    grace_period_days   TINYINT      NOT NULL DEFAULT 7   COMMENT '연체 유예일',
    notify_email        VARCHAR(120) NULL  COMMENT '결제 알림 이메일',
    created_at          DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at          DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    updated_by          VARCHAR(60)  NULL,
    PRIMARY KEY (setting_id),
    UNIQUE KEY uk_billing_settings_tenant (tenant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='구독결제 운영 설정 — 본사 입금계좌·자동결제일·알림';

-- ─────────────────────────────────────────────
-- 2) 결제수단 (토스 빌링키 / 무통장)
-- ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS billing_payment_methods (
    payment_method_id    VARCHAR(36)  NOT NULL,
    tenant_id            VARCHAR(36)  NOT NULL,
    provider             VARCHAR(20)  NOT NULL  COMMENT 'toss / manual',
    method_type          VARCHAR(20)  NOT NULL  COMMENT 'card / bank_transfer',
    provider_billing_key VARBINARY(512) NULL    COMMENT '토스 빌링키 (AES-256 암호화)',
    customer_key         VARCHAR(100) NULL      COMMENT '토스 customerKey (테넌트별 고정)',
    display_name         VARCHAR(120) NOT NULL  COMMENT '표시명 (예: 신한카드 ****1234)',
    card_brand           VARCHAR(40)  NULL,
    card_last4           VARCHAR(4)   NULL,
    card_owner_type      VARCHAR(20)  NULL      COMMENT 'corporate / personal',
    is_default           TINYINT(1)   NOT NULL DEFAULT 0,
    is_active            TINYINT(1)   NOT NULL DEFAULT 1,
    registered_at        DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    last_used_at         DATETIME(6)  NULL,
    created_at           DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    created_by           VARCHAR(60)  NULL,
    updated_at           DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    updated_by           VARCHAR(60)  NULL,
    PRIMARY KEY (payment_method_id),
    KEY ix_billing_pm_tenant_active (tenant_id, is_active, is_default),
    KEY ix_billing_pm_provider (tenant_id, provider)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='결제수단 — 토스 빌링키 / 무통장입금';

-- ─────────────────────────────────────────────
-- 3) 활성 구독
-- ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS billing_subscriptions (
    subscription_id      VARCHAR(36)   NOT NULL,
    tenant_id            VARCHAR(36)   NOT NULL,
    plan_code            VARCHAR(40)   NOT NULL  COMMENT 'starter / business / pro',
    plan_name            VARCHAR(80)   NOT NULL,
    monthly_amount       DECIMAL(14,2) NOT NULL  COMMENT '월 구독료 (VAT 별도)',
    license_count        INT           NOT NULL DEFAULT 1,
    payment_method_id    VARCHAR(36)   NULL      COMMENT '연결된 결제수단 (NULL=미연결)',
    started_at           DATETIME(6)   NOT NULL,
    next_billing_date    DATE          NULL      COMMENT '다음 결제 예정일',
    expires_at           DATETIME(6)   NULL      COMMENT '구독 만료일',
    status               VARCHAR(20)   NOT NULL DEFAULT 'active'
                         COMMENT 'active / past_due / cancelled / paused',
    cancelled_at         DATETIME(6)   NULL,
    cancel_reason        VARCHAR(255)  NULL,
    created_at           DATETIME(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at           DATETIME(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (subscription_id),
    KEY ix_billing_sub_tenant_status (tenant_id, status),
    KEY ix_billing_sub_next_billing (next_billing_date, status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='활성 구독 — 플랜·금액·다음 결제일';

-- ─────────────────────────────────────────────
-- 4) 청구서/인보이스 (INSERT ONLY — §절대원칙 #3 정렬)
-- ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS billing_invoices (
    invoice_id            VARCHAR(36)   NOT NULL,
    tenant_id             VARCHAR(36)   NOT NULL,
    invoice_no            VARCHAR(40)   NOT NULL  COMMENT '인보이스 번호 (HP-20260429-001)',
    subscription_id       VARCHAR(36)   NULL,
    plan_code             VARCHAR(40)   NOT NULL,
    plan_name             VARCHAR(80)   NOT NULL,
    billing_period_start  DATE          NOT NULL,
    billing_period_end    DATE          NOT NULL,
    amount                DECIMAL(14,2) NOT NULL  COMMENT '공급가액',
    vat                   DECIMAL(14,2) NOT NULL  COMMENT '부가세',
    total_amount          DECIMAL(14,2) NOT NULL  COMMENT '합계',
    status                VARCHAR(20)   NOT NULL DEFAULT 'pending'
                          COMMENT 'pending / paid / failed / refunded / cancelled',
    issued_at             DATETIME(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    due_date              DATE          NULL,
    paid_at               DATETIME(6)   NULL,
    payment_method_id     VARCHAR(36)   NULL      COMMENT '결제 시 사용된 수단',
    provider              VARCHAR(20)   NULL      COMMENT 'toss / manual',
    provider_payment_key  VARCHAR(120)  NULL      COMMENT '토스 paymentKey',
    receipt_url           VARCHAR(500)  NULL,
    tax_invoice_issued    TINYINT(1)    NOT NULL DEFAULT 0,
    tax_invoice_no        VARCHAR(40)   NULL,
    memo                  VARCHAR(500)  NULL,
    created_at            DATETIME(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    created_by            VARCHAR(60)   NULL,
    updated_at            DATETIME(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    updated_by            VARCHAR(60)   NULL,
    PRIMARY KEY (invoice_id),
    UNIQUE KEY uk_billing_invoice_no (tenant_id, invoice_no),
    KEY ix_billing_invoice_tenant_status (tenant_id, status, issued_at),
    KEY ix_billing_invoice_subscription (tenant_id, subscription_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='구독 인보이스 — INSERT ONLY';

-- ─────────────────────────────────────────────
-- 5) 결제 시도 로그 (감사·디버깅)
-- ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS billing_payment_attempts (
    attempt_id            VARCHAR(36)   NOT NULL,
    tenant_id             VARCHAR(36)   NOT NULL,
    invoice_id            VARCHAR(36)   NOT NULL,
    payment_method_id     VARCHAR(36)   NULL,
    provider              VARCHAR(20)   NOT NULL,
    attempted_at          DATETIME(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    status                VARCHAR(20)   NOT NULL  COMMENT 'success / failed / pending',
    error_code            VARCHAR(60)   NULL,
    error_message         VARCHAR(500)  NULL,
    provider_response_json LONGTEXT     NULL      COMMENT '토스 응답 원본 (디버깅용)',
    PRIMARY KEY (attempt_id),
    KEY ix_billing_attempt_invoice (tenant_id, invoice_id, attempted_at),
    KEY ix_billing_attempt_status (tenant_id, status, attempted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='결제 시도 로그 — 감사·디버깅';

-- ─────────────────────────────────────────────
-- 6) 시드 — tenant-001 기본 설정 (placeholder, 어드민이 화면에서 수정)
-- ─────────────────────────────────────────────
INSERT IGNORE INTO billing_settings
  (setting_id, tenant_id, head_office_bank, head_office_account, head_office_holder,
   auto_billing_day, grace_period_days, notify_email)
VALUES
  (UUID(), 'tenant-001',
   '우리은행', '1234-5678-9012', '히트판',
   1, 7, 'billing@hitpan.kr');
