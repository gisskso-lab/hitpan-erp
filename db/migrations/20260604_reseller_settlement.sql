-- 대리점 정산·시리얼 발급 (사장님 결재 2026-06-04, W9)
--
-- 헌법 정합:
--   #17 ENGINE=InnoDB
--   #4 금액은 DECIMAL
--   #3 settlements는 INSERT ONLY (확정 후 UPDATE 금지)
--   #18·#22 본사 영역 메타만
--
-- 박제 위치: hitpan_backoffice DB

USE hitpan_backoffice;

-- ────────────────────────────────────────
-- 1. 월별 정산 (수수료·인센티브)
-- ────────────────────────────────────────
CREATE TABLE IF NOT EXISTS reseller_settlements (
    settlement_id   BIGINT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
    reseller_id     CHAR(36)        NOT NULL,
    settlement_month CHAR(7)        NOT NULL COMMENT 'YYYY-MM',
    tenant_count    INT             NOT NULL DEFAULT 0 COMMENT '해당 월 활성 고객사 수',
    gross_amount    DECIMAL(14,2)   NOT NULL DEFAULT 0 COMMENT '결제 총액',
    commission_rate DECIMAL(5,4)    NOT NULL DEFAULT 0 COMMENT '수수료율 0.0000~1.0000',
    commission_amount DECIMAL(14,2) NOT NULL DEFAULT 0 COMMENT 'gross × rate',
    incentive_amount DECIMAL(14,2)  NOT NULL DEFAULT 0 COMMENT '추가 인센티브',
    total_payable   DECIMAL(14,2)   NOT NULL DEFAULT 0 COMMENT 'commission + incentive',
    status          VARCHAR(20)     NOT NULL DEFAULT 'draft'
        COMMENT 'draft / confirmed / paid',
    confirmed_by    CHAR(36)        NULL,
    confirmed_at    DATETIME(6)     NULL,
    paid_at         DATETIME(6)     NULL,
    memo            VARCHAR(500)    NULL,
    created_at      DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at      DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),

    UNIQUE KEY uq_reseller_month (reseller_id, settlement_month),
    KEY idx_settlement_month (settlement_month, status),
    CONSTRAINT fk_settlement_reseller FOREIGN KEY (reseller_id)
        REFERENCES resellers(reseller_id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ────────────────────────────────────────
-- 2. 시리얼 발급 풀 (대리점 미리 보유 → 고객사 할당)
-- ────────────────────────────────────────
CREATE TABLE IF NOT EXISTS reseller_serials (
    serial_id       BIGINT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
    serial_code     VARCHAR(40)     NOT NULL COMMENT '발급된 시리얼 (대문자+숫자 32~40자)',
    serial_hash     VARCHAR(255)    NOT NULL COMMENT 'BCrypt 박제 — 평문 1회만 발급 응답',
    reseller_id     CHAR(36)        NOT NULL,
    issued_by       CHAR(36)        NOT NULL COMMENT '발급한 본사 owner bo_users.user_id',
    subscription_tier VARCHAR(20)   NOT NULL DEFAULT 'basic',
    max_users       TINYINT UNSIGNED NOT NULL DEFAULT 3,
    valid_until     DATETIME        NULL COMMENT '시리얼 유효 기한 (NULL=무기한)',
    status          VARCHAR(20)     NOT NULL DEFAULT 'available'
        COMMENT 'available / claimed / revoked / expired',
    claimed_tenant_id CHAR(36)      NULL COMMENT '할당된 고객사 (claimed 후)',
    claimed_at      DATETIME(6)     NULL,
    revoked_at      DATETIME(6)     NULL,
    revoked_reason  VARCHAR(255)    NULL,
    issued_at       DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6),

    UNIQUE KEY uq_serial_code (serial_code),
    KEY idx_serial_reseller (reseller_id, status),
    KEY idx_serial_status (status),
    CONSTRAINT fk_serial_reseller FOREIGN KEY (reseller_id)
        REFERENCES resellers(reseller_id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ────────────────────────────────────────
-- 3. 정산 상세 (집계 산출 근거 — INSERT ONLY)
-- ────────────────────────────────────────
CREATE TABLE IF NOT EXISTS reseller_settlement_lines (
    line_id         BIGINT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
    settlement_id   BIGINT          NOT NULL,
    tenant_id       CHAR(36)        NOT NULL,
    tenant_code     VARCHAR(40)     NOT NULL,
    company_name    VARCHAR(255)    NOT NULL,
    payment_amount  DECIMAL(14,2)   NOT NULL DEFAULT 0,
    commission_amount DECIMAL(14,2) NOT NULL DEFAULT 0,
    created_at      DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6),

    KEY idx_settlement_lines_settlement (settlement_id),
    CONSTRAINT fk_lines_settlement FOREIGN KEY (settlement_id)
        REFERENCES reseller_settlements(settlement_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
