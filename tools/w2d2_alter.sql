-- ============================================
-- W2 D2 일괄 실행 SQL (작9~작12)
-- 생성: 2026-05-12 야간
-- 백업: C:\hitpan-backup\hitpan_erp_pre_ALTER_20260512_181500.sql
-- ============================================

-- ============================================
-- 작9: partners 19개 컬럼 ALTER
-- ============================================
ALTER TABLE partners
    ADD COLUMN IF NOT EXISTS card_commission_rate DECIMAL(5,2) DEFAULT 0 COMMENT '카드 수수료율 (buy_cardyul)',
    ADD COLUMN IF NOT EXISTS classification_code VARCHAR(30) NULL COMMENT '분류 코드 (buy_ccode)',
    ADD COLUMN IF NOT EXISTS manager_department VARCHAR(30) NULL COMMENT '담당 부서 (buy_damdangbu)',
    ADD COLUMN IF NOT EXISTS price_grade_code VARCHAR(10) NULL COMMENT '단가등급 코드 (buy_DOSCODE)',
    ADD COLUMN IF NOT EXISTS price_grade TINYINT DEFAULT 1 COMMENT '단가등급 1~5 (옵션 H)',
    ADD COLUMN IF NOT EXISTS legacy_extra VARCHAR(30) NULL COMMENT '레거시 예비 (buy_fil)',
    ADD COLUMN IF NOT EXISTS discount_rate DECIMAL(5,2) DEFAULT 0 COMMENT '할인율 (buy_halyul)',
    ADD COLUMN IF NOT EXISTS keyman_birth VARCHAR(10) NULL COMMENT '키맨 생일 (buy_keybirth)',
    ADD COLUMN IF NOT EXISTS keyman_name VARCHAR(50) NULL COMMENT '키맨 이름 (buy_keyname)',
    ADD COLUMN IF NOT EXISTS keyman_phone VARCHAR(20) NULL COMMENT '키맨 연락처 (buy_keytel)',
    ADD COLUMN IF NOT EXISTS margin_rate DECIMAL(5,2) DEFAULT 0 COMMENT '마진율 (buy_mayul)',
    ADD COLUMN IF NOT EXISTS sales_employee VARCHAR(30) NULL COMMENT '담당 영업사원 (buy_sawon)',
    ADD COLUMN IF NOT EXISTS trade_start_date DATE NULL COMMENT '거래 시작일 (buy_startdt)',
    ADD COLUMN IF NOT EXISTS business_registration_date DATE NULL COMMENT '사업자등록일 (buy_taxdt)',
    ADD COLUMN IF NOT EXISTS tel_secondary VARCHAR(20) NULL COMMENT '전화 2번 (buy_tel1)',
    ADD COLUMN IF NOT EXISTS tax_classification VARCHAR(10) NULL COMMENT '과세 구분 (buy_taxgubun)',
    ADD COLUMN IF NOT EXISTS ceo_name VARCHAR(50) NULL COMMENT '대표명 (buy_top)',
    ADD COLUMN IF NOT EXISTS partner_type VARCHAR(10) NULL COMMENT '거래처 분류 (buy_gu)',
    ADD COLUMN IF NOT EXISTS ceo_resident_no_encrypted VARBINARY(255) NULL COMMENT '대표 주민번호 AES-256 (buy_topjumin)';

CREATE INDEX IF NOT EXISTS idx_partners_price_grade ON partners (tenant_id, price_grade);
CREATE INDEX IF NOT EXISTS idx_partners_sales_emp ON partners (tenant_id, sales_employee);

-- ============================================
-- 작10: items 5개 컬럼 ALTER
-- ============================================
ALTER TABLE items
    ADD COLUMN IF NOT EXISTS spec_detail VARCHAR(80) NULL COMMENT '상세 규격 (S_SPEC)',
    ADD COLUMN IF NOT EXISTS unit_secondary VARCHAR(10) NULL COMMENT '2차 단위 (S_UNIT2)',
    ADD COLUMN IF NOT EXISTS safety_stock DECIMAL(15,3) DEFAULT 0 COMMENT '안전 재고 (S_SAFE)',
    ADD COLUMN IF NOT EXISTS reorder_point DECIMAL(15,3) DEFAULT 0 COMMENT '재주문 시점 (S_REORD)',
    ADD COLUMN IF NOT EXISTS supplier_default_id CHAR(36) NULL COMMENT '기본 매입처 (S_VENDOR FK)';

CREATE INDEX IF NOT EXISTS idx_items_supplier ON items (tenant_id, supplier_default_id);

-- ============================================
-- 작11: employees 28+3개 컬럼 ALTER (A~E 그룹)
-- ============================================
-- A. 기본 정보 (8개)
ALTER TABLE employees
    ADD COLUMN IF NOT EXISTS address VARCHAR(120) NULL COMMENT '주소 (SW_ADDR)',
    ADD COLUMN IF NOT EXISTS zip_code VARCHAR(10) NULL COMMENT '우편번호 (SW_POSTNO)',
    ADD COLUMN IF NOT EXISTS birth_date DATE NULL COMMENT '생일 (SW_BIRTH)',
    ADD COLUMN IF NOT EXISTS birth_calendar TINYINT DEFAULT 1 COMMENT '1=양력, 2=음력',
    ADD COLUMN IF NOT EXISTS birth_lunar_converted TINYINT DEFAULT 0 COMMENT '음력 변환',
    ADD COLUMN IF NOT EXISTS home_phone VARCHAR(20) NULL COMMENT '집전화 (SW_TEL)',
    ADD COLUMN IF NOT EXISTS emergency_contact VARCHAR(30) NULL COMMENT '비상연락처 (SW_TELem)',
    ADD COLUMN IF NOT EXISTS memo TEXT NULL COMMENT '비고 (SW_REM)';

-- B. 형사 영역 (5개) - 헌법 #5 AES-256
ALTER TABLE employees
    ADD COLUMN IF NOT EXISTS resident_no_encrypted VARBINARY(255) NULL COMMENT '주민번호 AES-256 (SW_JUMIN, 소득세법 §127·§164 + 4대보험법)',
    ADD COLUMN IF NOT EXISTS salary_encrypted VARBINARY(255) NULL COMMENT '급여 AES-256 (SW_PAY, 근로기준법 §48 + 개인정보보호법 §29)',
    ADD COLUMN IF NOT EXISTS salary_type TINYINT NULL COMMENT '급여 구분 (SW_PAYgu)',
    ADD COLUMN IF NOT EXISTS salary_category TINYINT NULL COMMENT '급여 유형 (SW_PAYeuy)',
    ADD COLUMN IF NOT EXISTS salary_extra_encrypted VARBINARY(500) NULL COMMENT '급여 기타 AES-256 (SW_PAYoth)';

-- C. 직장 정보 (7개)
ALTER TABLE employees
    ADD COLUMN IF NOT EXISTS department VARCHAR(50) NULL COMMENT '부서 (SW_BU)',
    ADD COLUMN IF NOT EXISTS marriage_status VARCHAR(2) NULL COMMENT '혼인 상태 (SW_MARRY)',
    ADD COLUMN IF NOT EXISTS business_type VARCHAR(50) NULL COMMENT '업무 유형 (SW_WORK)',
    ADD COLUMN IF NOT EXISTS is_resigned TINYINT DEFAULT 0 COMMENT '퇴직 여부 (SW_OUT)',
    ADD COLUMN IF NOT EXISTS resign_date DATE NULL COMMENT '퇴직일 (SW_OUTDT)',
    ADD COLUMN IF NOT EXISTS resign_reason VARCHAR(80) NULL COMMENT '퇴직 사유 (SW_OUTREM)',
    ADD COLUMN IF NOT EXISTS nationality VARCHAR(30) NULL COMMENT '국적 (SW_NATION)';

-- D. 레거시 잔액 (10개)
ALTER TABLE employees
    ADD COLUMN IF NOT EXISTS legacy_bal1 VARCHAR(150) NULL,
    ADD COLUMN IF NOT EXISTS legacy_bal2 VARCHAR(150) NULL,
    ADD COLUMN IF NOT EXISTS legacy_bal3 VARCHAR(150) NULL,
    ADD COLUMN IF NOT EXISTS legacy_bal4 VARCHAR(150) NULL,
    ADD COLUMN IF NOT EXISTS legacy_bal5 VARCHAR(150) NULL,
    ADD COLUMN IF NOT EXISTS legacy_bal6 VARCHAR(150) NULL,
    ADD COLUMN IF NOT EXISTS legacy_bal7 VARCHAR(150) NULL,
    ADD COLUMN IF NOT EXISTS legacy_bal8 VARCHAR(150) NULL,
    ADD COLUMN IF NOT EXISTS legacy_bal9 VARCHAR(150) NULL,
    ADD COLUMN IF NOT EXISTS legacy_bal10 VARCHAR(150) NULL;

-- E. 해외 (1개)
ALTER TABLE employees
    ADD COLUMN IF NOT EXISTS salary_country TINYINT NULL COMMENT '해외 직원 구분 (SW_PAYkuk)';

CREATE INDEX IF NOT EXISTS idx_employees_resigned ON employees (tenant_id, is_resigned);
CREATE INDEX IF NOT EXISTS idx_employees_dept ON employees (tenant_id, department);

-- ============================================
-- 작12: 4개 신규 테이블 CREATE
-- ============================================

-- 1. migration_jobs
CREATE TABLE IF NOT EXISTS migration_jobs (
    job_id            CHAR(36) PRIMARY KEY,
    tenant_id         CHAR(36) NOT NULL,
    initiated_by      CHAR(36) NOT NULL,
    source_folder     VARCHAR(500) NOT NULL,
    status            ENUM('pending','preview','running','paused','completed','failed','canceled') NOT NULL DEFAULT 'pending',
    total_tables      SMALLINT UNSIGNED DEFAULT 0,
    completed_tables  SMALLINT UNSIGNED DEFAULT 0,
    total_rows        INT UNSIGNED DEFAULT 0,
    processed_rows    INT UNSIGNED DEFAULT 0,
    skipped_rows      INT UNSIGNED DEFAULT 0,
    error_rows        INT UNSIGNED DEFAULT 0,
    preview_at        DATETIME NULL,
    started_at        DATETIME NULL,
    paused_at          DATETIME NULL,
    completed_at      DATETIME NULL,
    error_summary     TEXT NULL,
    checkpoint_data   JSON NULL,
    client_ip         VARCHAR(45) NULL,
    user_agent        VARCHAR(255) NULL,
    created_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_tenant_status (tenant_id, status),
    INDEX idx_tenant_created (tenant_id, created_at DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 2. migration_checkpoints
CREATE TABLE IF NOT EXISTS migration_checkpoints (
    checkpoint_id     CHAR(36) PRIMARY KEY,
    job_id            CHAR(36) NOT NULL,
    tenant_id         CHAR(36) NOT NULL,
    mdb_file          VARCHAR(50) NOT NULL,
    table_name        VARCHAR(50) NOT NULL,
    table_order       SMALLINT UNSIGNED NOT NULL,
    status            ENUM('pending','running','done','failed','skipped') NOT NULL DEFAULT 'pending',
    total_rows        INT UNSIGNED DEFAULT 0,
    processed_count   INT UNSIGNED DEFAULT 0,
    last_pk_value     JSON NULL,
    chunk_size        SMALLINT UNSIGNED DEFAULT 1000,
    started_at        DATETIME NULL,
    completed_at      DATETIME NULL,
    avg_commit_ms     INT UNSIGNED DEFAULT 0,
    last_error        TEXT NULL,
    retry_count       TINYINT UNSIGNED DEFAULT 0,
    created_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uk_job_table (job_id, table_name),
    INDEX idx_tenant (tenant_id),
    INDEX idx_chkpt_pending (job_id, status, table_order),
    CONSTRAINT fk_checkpoint_job FOREIGN KEY (job_id) REFERENCES migration_jobs(job_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3. migration_errors
CREATE TABLE IF NOT EXISTS migration_errors (
    error_id          CHAR(36) PRIMARY KEY,
    job_id            CHAR(36) NOT NULL,
    tenant_id         CHAR(36) NOT NULL,
    checkpoint_id     CHAR(36) NULL,
    mdb_file          VARCHAR(50) NOT NULL,
    table_name        VARCHAR(50) NOT NULL,
    row_pk_value      JSON NULL,
    row_offset        INT UNSIGNED NULL,
    error_type        ENUM('encoding','fk_missing','duplicate','schema','constraint','timeout','other') NOT NULL,
    error_severity    ENUM('warning','error','critical') NOT NULL DEFAULT 'error',
    error_code        VARCHAR(20) NULL,
    error_message     TEXT NOT NULL,
    error_detail      TEXT NULL,
    raw_data          JSON NULL,
    is_resolved       TINYINT UNSIGNED DEFAULT 0,
    resolved_at       DATETIME NULL,
    resolved_by       CHAR(36) NULL,
    resolution_note   TEXT NULL,
    occurred_at       DATETIME NOT NULL,
    created_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_job (job_id, error_severity, occurred_at),
    INDEX idx_tenant (tenant_id),
    INDEX idx_resolved (is_resolved, occurred_at),
    INDEX idx_errors_severity (job_id, error_severity, occurred_at DESC),
    CONSTRAINT fk_error_job FOREIGN KEY (job_id) REFERENCES migration_jobs(job_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 4. etax_send_history
CREATE TABLE IF NOT EXISTS etax_send_history (
    history_id           CHAR(36) PRIMARY KEY,
    tenant_id            CHAR(36) NOT NULL,
    tax_invoice_id       CHAR(36) NOT NULL,
    issue_date           DATE NULL,
    sent_at              DATETIME NULL,
    nts_read_date        DATE NULL,
    nts_report_date      DATE NULL,
    nts_approval_no      VARCHAR(50) NULL,
    nts_response_code    VARCHAR(20) NULL,
    nts_response_message VARCHAR(500) NULL,
    asp_provider         VARCHAR(20) NULL,
    asp_transaction_id   VARCHAR(100) NULL,
    status               ENUM('legacy','pending','sent','approved','rejected','failed','canceled') NOT NULL DEFAULT 'pending',
    attempt_no           TINYINT UNSIGNED NOT NULL DEFAULT 1,
    is_retry             TINYINT(1) NOT NULL DEFAULT 0,
    raw_request          JSON NULL,
    raw_response_encrypted VARBINARY(4096) NULL,
    created_by           CHAR(36) NULL,
    created_at           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_tenant_invoice (tenant_id, tax_invoice_id),
    INDEX idx_status (tenant_id, status, created_at DESC),
    INDEX idx_sent_date (tenant_id, sent_at DESC),
    INDEX idx_asp (asp_provider, asp_transaction_id),
    CONSTRAINT fk_etax_invoice FOREIGN KEY (tax_invoice_id) REFERENCES tax_invoices(invoice_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
