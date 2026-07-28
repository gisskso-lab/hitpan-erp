-- DB-39: 백오피스 스키마 (2026-05-05) [실행완료]
--
-- 본사·대리점 관리 시스템 — 신규 테이블 5개 + tenants FK 추가
-- 실행결과: platform_admins(1행) / resellers(0) / reseller_accounts(0)
--           reseller_commissions(0) / commission_settlements(0)
-- FK 5개 정상: reseller_accounts→resellers, reseller_commissions→resellers,
--             commission_settlements→resellers, commission_settlements→platform_admins,
--             tenants→resellers
-- 주의: 기존 resellers 테이블(데이터 0건)은 DROP 후 ERD 설계대로 재생성함
--
-- §절대원칙 #4:  모든 금액 DECIMAL — float/double 금지
-- §절대원칙 #5:  resellers.bank_account AES-256 암호화 (Value Converter 필수)
-- §절대원칙 #17: ENGINE=InnoDB 명시
-- §절대원칙 #18: 본사 서버용 SaaS 운영 데이터만 — 고객사 업무 데이터 금지
-- INSERT ONLY:   commission_settlements — 취소는 status='cancelled' (DELETE 금지)
--
-- 실행 순서:
--   1. platform_admins       (의존성 없음)
--   2. resellers             (의존성 없음)
--   3. reseller_accounts     (→ resellers)
--   4. reseller_commissions  (→ resellers)
--   5. commission_settlements (→ resellers, platform_admins)
--   6. ALTER TABLE tenants   (→ resellers FK 공식화)

-- ─────────────────────────────────────────────
-- 1) platform_admins — 본사 관리자 계정
-- ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS platform_admins (
    admin_id        CHAR(36)        NOT NULL                            COMMENT 'UUID v4 PK',
    email           VARCHAR(100)    NOT NULL                            COMMENT '로그인 이메일',
    password_hash   VARCHAR(256)    NOT NULL                            COMMENT 'bcrypt(cost=12)',
    admin_name      VARCHAR(50)     NOT NULL                            COMMENT '담당자명',
    role            ENUM(
                        'super_admin',
                        'billing_admin',
                        'cs_admin',
                        'readonly'
                    )               NOT NULL                            COMMENT '권한 등급',
    department      VARCHAR(50)     NULL                                COMMENT '부서명',
    is_active       TINYINT(1)      NOT NULL DEFAULT 1                  COMMENT '1=활성',
    last_login_at   DATETIME        NULL                                COMMENT '마지막 로그인',
    created_at      DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP  COMMENT '생성일시',
    updated_at      DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP
                    ON UPDATE CURRENT_TIMESTAMP                         COMMENT '수정일시',
    CONSTRAINT pk_platform_admins   PRIMARY KEY (admin_id),
    CONSTRAINT uk_platform_admins_email UNIQUE (email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='본사 관리자 계정';

CREATE INDEX idx_platform_admins_role_active ON platform_admins (role, is_active);

-- ─────────────────────────────────────────────
-- 2) resellers — 대리점 마스터
-- ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS resellers (
    reseller_id     CHAR(36)        NOT NULL                            COMMENT 'UUID v4 PK',
    reseller_code   VARCHAR(20)     NOT NULL                            COMMENT '대리점 코드 (RS-001)',
    reseller_name   VARCHAR(100)    NOT NULL                            COMMENT '대리점 상호',
    biz_no          VARCHAR(12)     NOT NULL                            COMMENT '사업자등록번호',
    ceo_name        VARCHAR(50)     NOT NULL                            COMMENT '대표자명',
    tel             VARCHAR(20)     NULL                                COMMENT '대표 전화',
    address         VARCHAR(200)    NULL                                COMMENT '사업장 주소',
    bank_name       VARCHAR(30)     NULL                                COMMENT '은행명',
    bank_account    VARCHAR(500)    NULL                                COMMENT '계좌번호 (AES-256 암호화)',
    account_holder  VARCHAR(30)     NULL                                COMMENT '예금주',
    contact_person  VARCHAR(50)     NULL                                COMMENT '담당자명',
    contact_phone   VARCHAR(20)     NULL                                COMMENT '담당자 연락처',
    contact_email   VARCHAR(100)    NULL                                COMMENT '담당자 이메일',
    join_date       DATE            NOT NULL                            COMMENT '계약 시작일',
    status          ENUM(
                        'active',
                        'suspended',
                        'inactive',
                        'terminated'
                    )               NOT NULL DEFAULT 'active'           COMMENT '상태',
    created_at      DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP  COMMENT '생성일시',
    updated_at      DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP
                    ON UPDATE CURRENT_TIMESTAMP                         COMMENT '수정일시',
    created_by      CHAR(36)        NULL                                COMMENT '등록 관리자 ID',
    CONSTRAINT pk_resellers             PRIMARY KEY (reseller_id),
    CONSTRAINT uk_resellers_code        UNIQUE (reseller_code),
    CONSTRAINT uk_resellers_biz_no      UNIQUE (biz_no)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='대리점 마스터';

CREATE INDEX idx_resellers_status    ON resellers (status);
CREATE INDEX idx_resellers_join_date ON resellers (join_date);

-- ─────────────────────────────────────────────
-- 3) reseller_accounts — 대리점 로그인 계정
-- ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS reseller_accounts (
    account_id      CHAR(36)        NOT NULL                            COMMENT 'UUID v4 PK',
    reseller_id     CHAR(36)        NOT NULL                            COMMENT 'FK → resellers',
    email           VARCHAR(100)    NOT NULL                            COMMENT '로그인 이메일 (대리점 내 UK)',
    password_hash   VARCHAR(256)    NOT NULL                            COMMENT 'bcrypt(cost=12)',
    account_name    VARCHAR(50)     NOT NULL                            COMMENT '담당자명',
    role            ENUM(
                        'reseller_admin',
                        'reseller_user',
                        'reseller_readonly'
                    )               NOT NULL                            COMMENT '권한 등급',
    phone           VARCHAR(20)     NULL                                COMMENT '연락처',
    is_active       TINYINT(1)      NOT NULL DEFAULT 1                  COMMENT '1=활성',
    last_login_at   DATETIME        NULL                                COMMENT '마지막 로그인',
    created_at      DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP  COMMENT '생성일시',
    updated_at      DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP
                    ON UPDATE CURRENT_TIMESTAMP                         COMMENT '수정일시',
    CONSTRAINT pk_reseller_accounts          PRIMARY KEY (account_id),
    CONSTRAINT uk_reseller_accounts_email    UNIQUE (reseller_id, email),
    CONSTRAINT fk_reseller_accounts_reseller
        FOREIGN KEY (reseller_id) REFERENCES resellers (reseller_id)
        ON UPDATE CASCADE ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='대리점 로그인 계정';

CREATE INDEX idx_reseller_accounts_reseller ON reseller_accounts (reseller_id);
CREATE INDEX idx_reseller_accounts_active   ON reseller_accounts (is_active);

-- ─────────────────────────────────────────────
-- 4) reseller_commissions — 수수료 정책
-- ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS reseller_commissions (
    commission_id   CHAR(36)        NOT NULL                            COMMENT 'UUID v4 PK',
    reseller_id     CHAR(36)        NOT NULL                            COMMENT 'FK → resellers',
    plan_code       VARCHAR(20)     NOT NULL                            COMMENT 'basic|pro|enterprise',
    rate            DECIMAL(5,2)    NOT NULL                            COMMENT '수수료율(%) ex: 20.00',
    effective_from  DATE            NOT NULL                            COMMENT '적용 시작일',
    effective_to    DATE            NULL                                COMMENT '적용 종료일 (NULL=현재 유효)',
    is_active       TINYINT(1)      NOT NULL DEFAULT 1                  COMMENT '1=활성 정책',
    created_at      DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP  COMMENT '생성일시',
    created_by      CHAR(36)        NULL                                COMMENT '등록 관리자 ID',
    CONSTRAINT pk_reseller_commissions          PRIMARY KEY (commission_id),
    CONSTRAINT uk_reseller_commissions_period   UNIQUE (reseller_id, plan_code, effective_from),
    CONSTRAINT fk_reseller_commissions_reseller
        FOREIGN KEY (reseller_id) REFERENCES resellers (reseller_id)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    CONSTRAINT chk_reseller_commissions_rate
        CHECK (rate >= 0 AND rate <= 100)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='대리점별 플랜별 수수료 정책 이력';

CREATE INDEX idx_reseller_commissions_reseller_active ON reseller_commissions (reseller_id, is_active);
CREATE INDEX idx_reseller_commissions_effective       ON reseller_commissions (effective_from, effective_to);

-- ─────────────────────────────────────────────
-- 5) commission_settlements — 월별 수수료 정산
-- ─────────────────────────────────────────────
-- INSERT ONLY: 취소는 status='cancelled' — DELETE 절대 금지
CREATE TABLE IF NOT EXISTS commission_settlements (
    settlement_id           CHAR(36)        NOT NULL                            COMMENT 'UUID v4 PK',
    reseller_id             CHAR(36)        NOT NULL                            COMMENT 'FK → resellers',
    settlement_month        CHAR(7)         NOT NULL                            COMMENT '정산월 YYYY-MM',
    status                  ENUM(
                                'draft',
                                'approved',
                                'paid',
                                'cancelled'
                            )               NOT NULL DEFAULT 'draft'            COMMENT '상태 (draft→approved→paid)',
    active_customer_count   INT             NOT NULL DEFAULT 0                  COMMENT '담당 활성 고객사 수',
    total_revenue           DECIMAL(15,2)   NOT NULL DEFAULT 0.00               COMMENT '구독료 합계',
    total_commission        DECIMAL(15,2)   NOT NULL DEFAULT 0.00               COMMENT '수수료 합계 (total_revenue×rate/100)',
    deduction_amount        DECIMAL(15,2)   NOT NULL DEFAULT 0.00               COMMENT '공제액',
    payment_amount          DECIMAL(15,2)   NOT NULL DEFAULT 0.00               COMMENT '실지급액 (total_commission-deduction)',
    payment_date            DATE            NULL                                COMMENT '실제 지급일',
    approval_date           DATE            NULL                                COMMENT '승인일',
    approved_by             CHAR(36)        NULL                                COMMENT 'FK → platform_admins (승인자)',
    memo                    VARCHAR(500)    NULL                                COMMENT '관리자 메모',
    created_at              DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP  COMMENT '생성일시',
    updated_at              DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP
                            ON UPDATE CURRENT_TIMESTAMP                         COMMENT '수정일시',
    CONSTRAINT pk_commission_settlements            PRIMARY KEY (settlement_id),
    CONSTRAINT uk_commission_settlements_month      UNIQUE (reseller_id, settlement_month),
    CONSTRAINT fk_commission_settlements_reseller
        FOREIGN KEY (reseller_id) REFERENCES resellers (reseller_id)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    CONSTRAINT fk_commission_settlements_admin
        FOREIGN KEY (approved_by) REFERENCES platform_admins (admin_id)
        ON UPDATE CASCADE ON DELETE SET NULL,
    CONSTRAINT chk_commission_settlements_amounts
        CHECK (total_revenue >= 0 AND total_commission >= 0
               AND deduction_amount >= 0 AND payment_amount >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='월별 수수료 정산 (INSERT ONLY — 취소는 status=cancelled)';

CREATE INDEX idx_commission_settlements_status       ON commission_settlements (status);
CREATE INDEX idx_commission_settlements_month        ON commission_settlements (settlement_month);
CREATE INDEX idx_commission_settlements_payment_date ON commission_settlements (payment_date);

-- ─────────────────────────────────────────────
-- 6) tenants — reseller_id FK 공식화
-- ─────────────────────────────────────────────
-- 기존 컬럼(reseller_id CHAR(36)) 존재 확인 후 FK만 추가
-- NULL = 히트판 직계약 고객사
ALTER TABLE tenants
    ADD CONSTRAINT fk_tenants_reseller
        FOREIGN KEY (reseller_id) REFERENCES resellers (reseller_id)
        ON UPDATE CASCADE ON DELETE SET NULL;

-- ─────────────────────────────────────────────
-- 7) 초기 본사 관리자 계정 (super_admin)
-- ─────────────────────────────────────────────
-- 비밀번호: Admin1234! (bcrypt hash — 운영 배포 전 반드시 변경)
INSERT IGNORE INTO platform_admins
    (admin_id, email, password_hash, admin_name, role, department, is_active)
VALUES (
    '00000000-0000-0000-0000-000000000001',
    'admin@hitpan.kr',
    '$2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/LewFOX6X6YS4A6m2.',
    '히트판 관리자',
    'super_admin',
    '운영팀',
    1
);
