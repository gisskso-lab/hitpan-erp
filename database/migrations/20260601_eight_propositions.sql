-- ============================================================================
-- WS-20260601-15 백오피스 DB 5종 박제 — 사장님 8명제 정합 (평문 0)
-- ============================================================================
-- 작성일       : 2026-06-01
-- 작성팀       : DB 매니저(Harvard·Oracle 30년) + DB 개발자 3명
-- 설계 근거    : docs/design/EIGHT_PROPOSITIONS_BACKOFFICE_LANDING.md §4
-- 작지서       : docs/work-orders/WS-20260601-12-22_EIGHT_PROPOSITIONS_BATCH.md
-- 대상 DB     : hitpan_backoffice (본사 클라우드 1대, MariaDB 11.4.10)
--
-- 헌법 정합
--   #4  decimal       : 수수료율 DECIMAL(5,2)
--   #11 어드민 권한 직접 설정 (Owner/Manager/Staff 직접 부여)
--   #13 DESCRIBE 의무  : 기존 DB-60 tenants 와 충돌 검토 완료 (별도 DB·신규 컬럼만)
--   #15 빈 catch 금지  : (응용 계층 정합)
--   #17 InnoDB 명시   : 5 테이블 일괄 ENGINE=InnoDB + utf8mb4_unicode_ci
--   #18 본사 업무데이터 0 + #22 데이터 최소주의
--                      : 평문 사업자번호·상호·대표자·주소·이메일·휴대폰 컬럼 0
--   #29 인프라 사전결재 : DDL 실행은 사장님 결재 후 (이 파일은 박제만)
--
-- 8명제 정합
--   #1 KYC 1회 후 즉시 폐기      : ntsapi_raw_hash 만 보유 (원문 메모리 폐기)
--   #2 시리얼 본사 발급          : tenants.serial = HP-YYMM-XXXXXXXX-CRC
--                                  resellers.serial = HR-YYMM-XXXXXXXX-CRC
--   #3 백오피스 평문 0           : 모든 식별자 = SHA-256/HMAC 해시
--   #3 biz_no_hash HMAC + pepper HSM
--                                : CHAR(64) BINARY (HMAC-SHA256, pepper는 HSM 보관)
--   #4 계정 본사 발급 + 강제 변경 : password_hash Argon2id (응용)
--   #5 분실 복구                : recovery_log INSERT ONLY
--   #6 대리점 RLS                : reseller_serial FK ON DELETE RESTRICT
--   #7 본사 전권한 + JIT 복호화  : platform_audit_log INSERT ONLY 추적
--   #8 생애주기 표준화           : activation_status / payment_status ENUM
--
-- 절대 금지 컬럼
--   - 사업자번호 평문 (business_number VARCHAR) 0
--   - 상호 평문 (company_name VARCHAR) 0
--   - 대표자 평문 (representative_name VARCHAR) 0
--   - 주소 평문 (address VARCHAR) 0
--   - 이메일 평문 / 휴대폰 평문 0 (응용 계층 envelope 암호화 → 해시 컬럼만)
--   - 평문 비밀번호 / 카드번호 / CVC / 만료일 0
--   - tenant_id 파라미터 수신 (JWT 클레임 only)
-- ============================================================================

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- ============================================================================
-- 1. tenants — 고객사 마스터 (백오피스, 평문 0)
--    PK = 시리얼 (본사 발급: HP-YYMM-XXXXXXXX-CRC, 8명제 #2)
-- ============================================================================
CREATE TABLE IF NOT EXISTS tenants (
    serial                 VARCHAR(24)    NOT NULL                  COMMENT '시리얼 PK: HP-YYMM-XXXXXXXX-CRC (8명제 #2)',
    email_hash             CHAR(64)       NOT NULL                  COMMENT 'SHA-256(email) — 평문 0',
    password_hash          VARCHAR(255)   NOT NULL                  COMMENT 'Argon2id (응용)',
    biz_no_hash            CHAR(64)       BINARY NOT NULL           COMMENT 'HMAC-SHA256(biz_no, pepper) — pepper는 HSM (8명제 #3)',
    verified               BOOLEAN        NOT NULL DEFAULT FALSE    COMMENT '국세청 진위확인 결과',
    verified_at            DATETIME(6)    NULL                      COMMENT '진위확인 시각',
    ntsapi_raw_hash        CHAR(64)       NULL                      COMMENT '진위확인 응답 SHA-256 (원문 즉시 폐기, 8명제 #1)',
    subscription_tier      ENUM('basic','pro','enterprise') NULL    COMMENT '구독 티어',
    payment_status         ENUM('active','pending','expired','suspended') NOT NULL DEFAULT 'pending',
    activation_status      ENUM('pending','active','suspended','revoked') NOT NULL DEFAULT 'pending',
    reseller_serial        VARCHAR(24)    NULL                      COMMENT '대리점 시리얼 (직판=NULL, 8명제 #6)',
    created_at             DATETIME(6)    NOT NULL,
    expires_at             DATETIME(6)    NULL                      COMMENT '구독 만료 시각',
    ip                     VARCHAR(45)    NULL                      COMMENT '가입 IP (IPv6 대응)',
    device_fingerprint     VARCHAR(64)    NULL                      COMMENT '디바이스 핑거프린트 해시',
    PRIMARY KEY (serial),
    UNIQUE KEY uq_tenants_biz_no_hash (biz_no_hash),
    UNIQUE KEY uq_tenants_email_hash (email_hash),
    KEY idx_tenants_reseller (reseller_serial),
    KEY idx_tenants_payment_status (payment_status),
    KEY idx_tenants_activation_status (activation_status),
    KEY idx_tenants_expires_at (expires_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='고객사 마스터 (백오피스, 평문 0, 8명제 #1~#3·#8)';

-- ============================================================================
-- 2. resellers — 대리점 마스터 (백오피스, 평문 0)
--    PK = 시리얼 (본사 발급: HR-YYMM-XXXXXXXX-CRC)
-- ============================================================================
CREATE TABLE IF NOT EXISTS resellers (
    serial                 VARCHAR(24)    NOT NULL                  COMMENT '시리얼 PK: HR-YYMM-XXXXXXXX-CRC',
    email_hash             CHAR(64)       NOT NULL                  COMMENT 'SHA-256(email)',
    password_hash          VARCHAR(255)   NOT NULL                  COMMENT 'Argon2id',
    biz_no_hash            CHAR(64)       BINARY NOT NULL           COMMENT 'HMAC-SHA256 + pepper HSM',
    verified_at            DATETIME(6)    NOT NULL,
    ntsapi_raw_hash        CHAR(64)       NULL                      COMMENT '진위확인 응답 해시 (원문 폐기)',
    reseller_type          ENUM('individual','corp') NOT NULL,
    contract_status        ENUM('active','expired','terminated') NOT NULL DEFAULT 'active',
    commission_rate        DECIMAL(5,2)   NOT NULL DEFAULT 0.00     COMMENT '수수료율 % (헌법 #4 decimal)',
    activation_status      ENUM('pending','active','suspended','revoked') NOT NULL DEFAULT 'pending',
    created_at             DATETIME(6)    NOT NULL,
    PRIMARY KEY (serial),
    UNIQUE KEY uq_resellers_biz_no_hash (biz_no_hash),
    UNIQUE KEY uq_resellers_email_hash (email_hash),
    KEY idx_resellers_contract_status (contract_status),
    KEY idx_resellers_activation_status (activation_status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='대리점 마스터 (백오피스, 평문 0, 8명제 #6)';

-- tenants.reseller_serial FK ON DELETE RESTRICT (8명제 #6 — 대리점 삭제 시 고객사 보호)
ALTER TABLE tenants
  ADD CONSTRAINT fk_tenants_reseller
  FOREIGN KEY (reseller_serial) REFERENCES resellers(serial)
  ON DELETE RESTRICT ON UPDATE CASCADE;

-- ============================================================================
-- 3. platform_users — 본사 운영 계정 (Owner/Manager/Staff, 8명제 #7)
-- ============================================================================
CREATE TABLE IF NOT EXISTS platform_users (
    user_id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    email_hash             CHAR(64)       NOT NULL                  COMMENT 'SHA-256(email)',
    password_hash          VARCHAR(255)   NOT NULL                  COMMENT 'Argon2id',
    role                   ENUM('owner','platform_manager','platform_staff') NOT NULL,
    full_name              VARCHAR(50)    NULL                      COMMENT '운영자명 (사내 표시용, 본사 직원)',
    status                 ENUM('active','suspended','revoked') NOT NULL DEFAULT 'active',
    mfa_enabled            BOOLEAN        NOT NULL DEFAULT TRUE     COMMENT 'MFA 강제 (Owner 2/2)',
    ip_whitelist           TEXT           NULL                      COMMENT 'CIDR 목록 (개행 구분)',
    last_login_at          DATETIME(6)    NULL,
    created_at             DATETIME(6)    NOT NULL,
    created_by             BIGINT UNSIGNED NULL                     COMMENT 'Owner 발급자 user_id',
    PRIMARY KEY (user_id),
    UNIQUE KEY uq_platform_users_email_hash (email_hash),
    KEY idx_platform_users_role (role),
    KEY idx_platform_users_status (status),
    CONSTRAINT fk_platform_users_created_by FOREIGN KEY (created_by)
        REFERENCES platform_users(user_id) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='본사 운영 계정 (Owner/Manager/Staff, MFA·IP 화이트리스트, 8명제 #7)';

-- ============================================================================
-- 4. platform_audit_log — 본사 감사로그 (INSERT ONLY, 월별 파티셔닝)
--    8명제 #7 JIT 복호화 6건 추적 + 헌법 #3 INSERT ONLY 원장 정합
-- ============================================================================
CREATE TABLE IF NOT EXISTS platform_audit_log (
    audit_id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    user_id                BIGINT UNSIGNED NOT NULL                 COMMENT 'platform_users.user_id',
    action                 VARCHAR(100)   NOT NULL                  COMMENT '예: SERIAL_ISSUE / JIT_DECRYPT / REFUND',
    target_serial          VARCHAR(24)    NULL                      COMMENT '대상 시리얼 (tenants/resellers)',
    details                JSON           NULL                      COMMENT '감사 메타 (평문 사업자 정보 박제 금지)',
    ip                     VARCHAR(45)    NULL,
    requested_at           DATETIME(6)    NOT NULL,
    PRIMARY KEY (audit_id, requested_at),
    KEY idx_audit_user_time (user_id, requested_at),
    KEY idx_audit_target (target_serial, requested_at),
    KEY idx_audit_action (action, requested_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='본사 감사로그 (INSERT ONLY, 월별 파티셔닝, 8명제 #7)'
  PARTITION BY RANGE (TO_DAYS(requested_at)) (
    PARTITION p202606 VALUES LESS THAN (TO_DAYS('2026-07-01')),
    PARTITION p202607 VALUES LESS THAN (TO_DAYS('2026-08-01')),
    PARTITION p202608 VALUES LESS THAN (TO_DAYS('2026-09-01')),
    PARTITION p202609 VALUES LESS THAN (TO_DAYS('2026-10-01')),
    PARTITION p202610 VALUES LESS THAN (TO_DAYS('2026-11-01')),
    PARTITION p202611 VALUES LESS THAN (TO_DAYS('2026-12-01')),
    PARTITION p202612 VALUES LESS THAN (TO_DAYS('2027-01-01')),
    PARTITION pmax    VALUES LESS THAN MAXVALUE
  );

-- ============================================================================
-- 5. recovery_log — 분실 복구 이력 (8명제 #5)
-- ============================================================================
CREATE TABLE IF NOT EXISTS recovery_log (
    recovery_id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    serial                 VARCHAR(24)    NOT NULL                  COMMENT '복구 대상 시리얼',
    serial_type            ENUM('tenant','reseller') NOT NULL,
    channel_email          BOOLEAN        NOT NULL DEFAULT FALSE    COMMENT '이메일 채널 전송 여부',
    channel_sms            BOOLEAN        NOT NULL DEFAULT FALSE    COMMENT 'SMS 채널 전송 여부 (헌법 #16 독립 connection)',
    admin_user_id          BIGINT UNSIGNED NULL                     COMMENT '발급 어드민 (platform_users)',
    ip                     VARCHAR(45)    NULL,
    device_fingerprint     VARCHAR(64)    NULL,
    requested_at           DATETIME(6)    NOT NULL,
    completed_at           DATETIME(6)    NULL,
    PRIMARY KEY (recovery_id),
    KEY idx_recovery_serial (serial, requested_at),
    KEY idx_recovery_admin (admin_user_id, requested_at),
    CONSTRAINT fk_recovery_admin FOREIGN KEY (admin_user_id)
        REFERENCES platform_users(user_id) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='분실 복구 이력 (INSERT ONLY 권고, 8명제 #5)';

SET FOREIGN_KEY_CHECKS = 1;

-- ============================================================================
-- 검증 쿼리 (실행 후 수동 확인)
-- ============================================================================
-- 1. 평문 금지 컬럼 0건 확인
--    SELECT TABLE_NAME, COLUMN_NAME FROM information_schema.COLUMNS
--    WHERE TABLE_SCHEMA = 'hitpan_backoffice'
--      AND TABLE_NAME IN ('tenants','resellers','platform_users','platform_audit_log','recovery_log')
--      AND COLUMN_NAME IN ('business_number','company_name','representative_name','address','email','phone');
--    -- 기대: 0 rows
--
-- 2. ENGINE / COLLATION 일관성
--    SELECT TABLE_NAME, ENGINE, TABLE_COLLATION FROM information_schema.TABLES
--    WHERE TABLE_SCHEMA = 'hitpan_backoffice'
--      AND TABLE_NAME IN ('tenants','resellers','platform_users','platform_audit_log','recovery_log');
--    -- 기대: 모두 InnoDB / utf8mb4_unicode_ci
-- ============================================================================
