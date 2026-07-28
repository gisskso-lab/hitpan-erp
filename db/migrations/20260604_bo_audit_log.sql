-- 백오피스 감사로그 (사장님 결재 2026-06-04, W11)
--
-- 헌법 정합:
--   #3 INSERT ONLY 원장 정신 — 감사로그는 UPDATE/DELETE 금지
--   #15 모든 Owner 액션 기록
--   #17 ENGINE=InnoDB
--   #18·#22 본사 영역 메타만, 고객 업무 데이터 0
--
-- 박제 위치: hitpan_backoffice DB

USE hitpan_backoffice;

CREATE TABLE IF NOT EXISTS bo_audit_log (
    log_id          BIGINT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
    actor_user_id   CHAR(36)        NOT NULL COMMENT 'bo_users.user_id 또는 platform_admins.admin_id',
    actor_email     VARCHAR(255)    NOT NULL,
    actor_role      VARCHAR(32)     NOT NULL,
    action          VARCHAR(64)     NOT NULL
        COMMENT 'bo_user.create / bo_user.update / bo_user.delete / tenant.suspend / mfa.enable / etc',
    target_type     VARCHAR(40)     NULL COMMENT 'bo_user / tenant / reseller / payment ...',
    target_id       VARCHAR(64)     NULL,
    detail_json     TEXT            NULL COMMENT '변경 전후 메타. 비밀번호·MFA 시크릿 0건',
    ip_address      VARCHAR(45)     NULL,
    user_agent      VARCHAR(255)    NULL,
    created_at      DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6),

    KEY idx_audit_actor (actor_user_id, created_at),
    KEY idx_audit_action (action, created_at),
    KEY idx_audit_target (target_type, target_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ────────────────────────────────────────
-- 4-eyes 승인 큐 (위험 액션 2명 결재)
-- ────────────────────────────────────────
CREATE TABLE IF NOT EXISTS bo_approval_queue (
    approval_id     BIGINT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
    requester_id    CHAR(36)        NOT NULL COMMENT '요청자 bo_users.user_id',
    requester_email VARCHAR(255)    NOT NULL,
    action          VARCHAR(64)     NOT NULL
        COMMENT 'bo_user.create / bo_user.delete / tenant.suspend / etc',
    target_type     VARCHAR(40)     NULL,
    target_id       VARCHAR(64)     NULL,
    payload_json    TEXT            NOT NULL COMMENT '실행될 작업 메타',
    status          VARCHAR(20)     NOT NULL DEFAULT 'pending'
        COMMENT 'pending / approved / rejected / expired / executed',
    approver_id     CHAR(36)        NULL COMMENT '승인·반려한 다른 Owner bo_users.user_id',
    approver_email  VARCHAR(255)    NULL,
    rejection_reason VARCHAR(500)   NULL,
    requested_at    DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    decided_at      DATETIME(6)     NULL,
    executed_at     DATETIME(6)     NULL,
    expires_at      DATETIME(6)     NOT NULL COMMENT 'requested_at + 24h',

    KEY idx_approval_status (status, expires_at),
    KEY idx_approval_requester (requester_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ────────────────────────────────────────
-- MFA TOTP 시크릿 (Owner 강제)
-- ────────────────────────────────────────
CREATE TABLE IF NOT EXISTS bo_user_mfa (
    user_id         CHAR(36)        NOT NULL PRIMARY KEY,
    secret_encrypted VARBINARY(255) NOT NULL COMMENT 'TOTP 시크릿 AES-256 박제 (HITPAN_BO_MFA_KEY)',
    is_enabled      TINYINT(1)      NOT NULL DEFAULT 0,
    enrolled_at     DATETIME(6)     NULL,
    last_used_at    DATETIME(6)     NULL,
    backup_codes_hash TEXT          NULL COMMENT 'BCrypt 박제 10개. 분실 시 1회용 복구',
    created_at      DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at      DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),

    CONSTRAINT fk_bo_user_mfa_user FOREIGN KEY (user_id)
        REFERENCES bo_users(user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
