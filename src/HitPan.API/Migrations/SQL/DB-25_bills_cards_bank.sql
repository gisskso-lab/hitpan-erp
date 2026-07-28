-- ============================================================================
-- DB-25: 어음·카드·은행 3 도메인 (2026-04-29)
-- ----------------------------------------------------------------------------
-- 사장님 결재 2026-04-29: 자료이관 누락 셀링포인트 풀스택.
-- 레거시 매핑: DOCF9(EU)+DOCFQ(EQ)→bills / DOCCD+DOCCD1→card_payments(_lines) / BANKF→bank_transactions
-- §#2 tenant_id 우선 인덱스 / §#3 bank_transactions INSERT ONLY / §#17 InnoDB / §#19 errors 0 + warnings 0
-- ============================================================================

-- 1) 어음 (받을·지급 통합) ──────────────────────────────────────
CREATE TABLE IF NOT EXISTS bills (
    bill_id             VARCHAR(36) NOT NULL,
    tenant_id           VARCHAR(36) NOT NULL,
    bill_type           VARCHAR(1)  NOT NULL,                         -- 'R' 받을어음 / 'P' 지급어음
    bill_no             VARCHAR(30) NOT NULL,                         -- 어음번호
    bank_name           VARCHAR(40) NULL,                             -- 발행은행
    issue_place         VARCHAR(40) NULL,                             -- 발행지
    partner_id          VARCHAR(36) NULL,                             -- 상대거래처 (FK)
    partner_name_legacy VARCHAR(50) NULL,                             -- 이관용 원본 거래처명
    issue_date          DATE        NOT NULL,                         -- 발행일
    maturity_date       DATE        NULL,                             -- 만기일
    discount_date       DATE        NULL,                             -- 할인일 (받을어음 한정)
    settled_date        DATE        NULL,                             -- 회수/결제일
    amount              DECIMAL(15,2) NOT NULL,                       -- 액면금액 (§#4 decimal)
    status              VARCHAR(20) NOT NULL DEFAULT 'issued',        -- issued / discounted / paid / defaulted / cancelled
    remark              VARCHAR(100) NULL,
    legacy_source       VARCHAR(20) NULL,                             -- 'DOCF9' / 'DOCFQ' / NULL(수기)
    created_at          DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (bill_id),
    KEY ix_bills_tenant_type_status (tenant_id, bill_type, status),
    KEY ix_bills_tenant_maturity (tenant_id, maturity_date),
    KEY ix_bills_tenant_partner (tenant_id, partner_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 2) 카드결제 마스터 ──────────────────────────────────────────
CREATE TABLE IF NOT EXISTS card_payments (
    card_payment_id     VARCHAR(36) NOT NULL,
    tenant_id           VARCHAR(36) NOT NULL,
    card_no             VARCHAR(20) NOT NULL,                         -- 카드번호 (앞4+뒤4 가능)
    card_company        VARCHAR(20) NULL,                             -- 카드사명
    holder_name         VARCHAR(20) NULL,                             -- 카드 소유자 (회사명/대표)
    payment_date        DATE        NOT NULL,                         -- 결제 청구일
    bank_settle_date    DATE        NULL,                             -- 은행 출금일
    total_amount        DECIMAL(15,2) NOT NULL,                       -- 결제 총액
    installment_amount  DECIMAL(15,2) NOT NULL DEFAULT 0,             -- 할부 분할 금액
    installment_months  INT         NOT NULL DEFAULT 0,               -- 할부 개월
    settled_amount      DECIMAL(15,2) NOT NULL DEFAULT 0,             -- 인출된 금액
    status              VARCHAR(20) NOT NULL DEFAULT 'pending',       -- pending / settled / cancelled
    remark              VARCHAR(60) NULL,
    legacy_source       VARCHAR(20) NULL,                             -- 'DOCCD' / NULL(수기)
    created_at          DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (card_payment_id),
    KEY ix_cardpay_tenant_date (tenant_id, payment_date),
    KEY ix_cardpay_tenant_status (tenant_id, status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3) 카드결제 라인 ───────────────────────────────────────────
CREATE TABLE IF NOT EXISTS card_payment_lines (
    line_id             VARCHAR(36) NOT NULL,
    card_payment_id     VARCHAR(36) NOT NULL,
    tenant_id           VARCHAR(36) NOT NULL,
    seq                 INT         NOT NULL,
    partner_id          VARCHAR(36) NULL,                             -- 상대거래처 (FK)
    partner_name_legacy VARCHAR(50) NULL,                             -- 이관용 원본 거래처명
    tx_date             DATE        NOT NULL,
    amount              DECIMAL(15,2) NOT NULL,
    remark              VARCHAR(60) NULL,
    PRIMARY KEY (line_id),
    KEY ix_cardline_tenant_master (tenant_id, card_payment_id),
    KEY ix_cardline_tenant_partner (tenant_id, partner_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 4) 은행 거래내역 (INSERT ONLY 원장 §#3) ────────────────────
CREATE TABLE IF NOT EXISTS bank_transactions (
    bank_tx_id          VARCHAR(36) NOT NULL,
    tenant_id           VARCHAR(36) NOT NULL,
    account_no          VARCHAR(30) NOT NULL,                         -- 계좌번호
    bank_name           VARCHAR(40) NULL,
    tx_date             DATE        NOT NULL,
    tx_type             VARCHAR(1)  NOT NULL,                         -- '1' 입금(차변) / '2' 출금(대변)
    amount              DECIMAL(15,2) NOT NULL,
    balance_after       DECIMAL(15,2) NULL,                           -- 거래 후 잔액 (선택)
    partner_id          VARCHAR(36) NULL,
    partner_name_legacy VARCHAR(50) NULL,
    description         VARCHAR(50) NULL,                             -- 적요
    remark              VARCHAR(40) NULL,
    imported_from       VARCHAR(20) NOT NULL DEFAULT 'manual',        -- manual / mdb_legacy / bank_api
    legacy_source       VARCHAR(20) NULL,                             -- 'BANKF' / NULL
    created_at          DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (bank_tx_id),
    KEY ix_banktx_tenant_account_date (tenant_id, account_no, tx_date),
    KEY ix_banktx_tenant_date (tenant_id, tx_date),
    KEY ix_banktx_tenant_partner (tenant_id, partner_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
