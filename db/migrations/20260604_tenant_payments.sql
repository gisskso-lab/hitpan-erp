-- 토스페이먼츠 결제 박제 테이블 (사장님 결재 2026-06-04, 헌법 #34 정식 완성도)
-- 본사 백오피스 DB만 박제. ERP 로컬 DB 0건.
--
-- 헌법 정합:
--   #18·#22 — 카드번호·CVC·평문 0건. paymentKey·orderId·amount·method만 박제
--   #20 — 결제→tenant 활성화 흐름 박제 (TossPaymentsController + LandingSignupController)
--   #17 — ENGINE=InnoDB 명시

CREATE TABLE IF NOT EXISTS tenant_payments (
    payment_id       CHAR(36)        NOT NULL PRIMARY KEY,
    order_id         VARCHAR(64)     NOT NULL,
    signup_token     VARCHAR(64)     NULL,
    payment_key      VARCHAR(255)    NOT NULL,
    amount           BIGINT          NOT NULL,
    method           VARCHAR(32)     NULL COMMENT '카드/계좌이체/휴대폰 등 토스 method 값',
    status           VARCHAR(32)     NOT NULL DEFAULT 'pending' COMMENT 'pending / confirmed / canceled / failed',
    approved_at      VARCHAR(32)     NULL COMMENT '토스 응답 approvedAt ISO 문자열',
    raw_status       VARCHAR(32)     NULL COMMENT '토스 응답 status (DONE 등)',
    created_at       DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at       DATETIME        NULL ON UPDATE CURRENT_TIMESTAMP,

    UNIQUE KEY uq_tenant_payments_order (order_id),
    KEY idx_tenant_payments_token (signup_token),
    KEY idx_tenant_payments_status (status, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
