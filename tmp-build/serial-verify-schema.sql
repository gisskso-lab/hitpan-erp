-- 시리얼 검증 시도 + 잠금 (브라운킴 PM 작성 2026-06-08, 사장님 결재)
-- 사장님 헌법: 5회 실패 시 사용 중지, 본사 수동 잠금 해제

SET FOREIGN_KEY_CHECKS=0;

-- 1) 시리얼 검증 시도 이력 (INSERT ONLY, 헌법 #3)
CREATE TABLE IF NOT EXISTS serial_verify_attempts (
    attempt_id       BIGINT       NOT NULL AUTO_INCREMENT PRIMARY KEY,
    tenant_id        VARCHAR(36)  NULL,                    -- 일치 시 채워짐, 실패 시 NULL
    submitted_hash   VARCHAR(64)  NOT NULL,                -- 사용자가 입력한 시리얼의 해시 (감사용)
    client_ip        VARCHAR(45)  NULL,
    client_fingerprint VARCHAR(128) NULL,                  -- 기기 식별 (헌법 #22 정합)
    result           VARCHAR(20)  NOT NULL,                -- 'success', 'mismatch', 'locked', 'rate_limit'
    attempted_at     DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    INDEX ix_tenant_attempted (tenant_id, attempted_at),
    INDEX ix_fingerprint_attempted (client_fingerprint, attempted_at),
    INDEX ix_result (result, attempted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 2) 시리얼 검증 잠금 상태 (기기·IP 단위)
--    같은 기기에서 5회 연속 실패 시 잠김 (tenant_id 모를 때도 차단 가능)
CREATE TABLE IF NOT EXISTS serial_verify_locks (
    lock_id          BIGINT       NOT NULL AUTO_INCREMENT PRIMARY KEY,
    client_fingerprint VARCHAR(128) NOT NULL,
    failed_count     INT          NOT NULL DEFAULT 0,
    first_failed_at  DATETIME(6)  NOT NULL,
    last_failed_at   DATETIME(6)  NOT NULL,
    is_locked        TINYINT(1)   NOT NULL DEFAULT 0,
    locked_at        DATETIME     NULL,
    unlocked_at      DATETIME     NULL,
    unlocked_by      VARCHAR(36)  NULL,                    -- platform_admin.admin_id
    unlock_reason    VARCHAR(500) NULL,
    UNIQUE KEY uk_fingerprint (client_fingerprint),
    INDEX ix_locked (is_locked, last_failed_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3) tenant 활성화 상태 컬럼 추가 (시리얼 검증 통과 여부)
ALTER TABLE tenants
    ADD COLUMN IF NOT EXISTS serial_verified TINYINT(1) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS serial_verified_at DATETIME NULL,
    ADD COLUMN IF NOT EXISTS serial_verified_fingerprint VARCHAR(128) NULL;

SET FOREIGN_KEY_CHECKS=1;

SELECT 'serial_verify_schema_ok' AS status,
       (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='hitpan_backoffice' AND table_name='serial_verify_attempts') AS attempts_ok,
       (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='hitpan_backoffice' AND table_name='serial_verify_locks') AS locks_ok;
