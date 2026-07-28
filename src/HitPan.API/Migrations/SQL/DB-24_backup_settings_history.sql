-- ============================================================================
-- DB-24: 자료 백업 설정·이력 (2026-04-29)
-- ----------------------------------------------------------------------------
-- 사장님 결재 2026-04-29: 로컬 미러링 백업 (1차 메인디스크 + 2차 외장/USB)
-- §#17 InnoDB 명시 / §#2 tenant_id 우선 / §#18 본사로 송신 절대 없음 (로컬 파일 only)
-- ============================================================================

-- 1) 백업 설정 (테넌트당 1행)
CREATE TABLE IF NOT EXISTS backup_settings (
    tenant_id           VARCHAR(36) NOT NULL,
    primary_path        VARCHAR(500) NOT NULL,                -- 1차 폴더 (메인 하드)
    mirror_path         VARCHAR(500) NULL,                    -- 2차 폴더 (외장/USB), NULL 허용
    schedule_mode       VARCHAR(20)  NOT NULL DEFAULT 'manual', -- manual / hourly / every_6h / daily_03
    retention_count     INT          NOT NULL DEFAULT 30,     -- 보관 개수 (오래된 것부터 삭제)
    last_run_at         DATETIME     NULL,
    last_status         VARCHAR(20)  NULL,                    -- success / failed
    last_error          TEXT         NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (tenant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 2) 백업 이력 (실행마다 1행)
CREATE TABLE IF NOT EXISTS backup_history (
    backup_id           VARCHAR(36) NOT NULL,
    tenant_id           VARCHAR(36) NOT NULL,
    started_at          DATETIME    NOT NULL,
    finished_at         DATETIME    NULL,
    primary_file        VARCHAR(500) NULL,                    -- 1차 저장된 풀패스
    mirror_file         VARCHAR(500) NULL,                    -- 2차 저장된 풀패스 (없으면 NULL)
    file_size_bytes     BIGINT      NULL,
    status              VARCHAR(20) NOT NULL,                 -- running / success / failed
    error_message       TEXT        NULL,
    triggered_by        VARCHAR(20) NOT NULL DEFAULT 'manual', -- manual / schedule
    PRIMARY KEY (backup_id),
    KEY ix_backup_history_tenant_started (tenant_id, started_at DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3) 자료 복원 이력 (감사 추적, INSERT ONLY)
CREATE TABLE IF NOT EXISTS restore_history (
    restore_id          VARCHAR(36) NOT NULL,
    tenant_id           VARCHAR(36) NOT NULL,
    started_at          DATETIME    NOT NULL,
    finished_at         DATETIME    NULL,
    source_file         VARCHAR(500) NOT NULL,                -- 복원에 사용한 .sql 풀패스
    pre_restore_backup  VARCHAR(500) NULL,                    -- 복원 직전 자동 백업 풀패스
    status              VARCHAR(20) NOT NULL,                 -- running / success / failed
    error_message       TEXT        NULL,
    triggered_by_user   VARCHAR(36) NULL,                     -- 누가 눌렀는지 (users.user_id)
    PRIMARY KEY (restore_id),
    KEY ix_restore_history_tenant_started (tenant_id, started_at DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 4) 기본값 시드 (tenant-001)
INSERT IGNORE INTO backup_settings (tenant_id, primary_path, mirror_path, schedule_mode, retention_count)
VALUES ('tenant-001', 'C:\\HitpanBackup', NULL, 'manual', 30);
