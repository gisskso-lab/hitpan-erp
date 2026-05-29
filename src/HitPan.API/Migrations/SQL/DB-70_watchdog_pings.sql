-- =============================================================
-- DB-70: 본사 워치독 수신 테이블 (헌법 #18 v3·#22·#27·#28)
-- - watchdog_pings: 5분 주기 메타 ping (status·counter만, 업무 데이터 0)
-- - watchdog_emergencies: cool down 초과·자가복구 실패 시 즉시 알림
-- 절대원칙: tenant_id 원본·직원명·거래데이터·IP·매출 컬럼 0
-- =============================================================

CREATE TABLE IF NOT EXISTS watchdog_pings (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    tenant_id_hash CHAR(71) NOT NULL COMMENT 'sha256:{64hex} — 헌법 #22 원본 금지',
    received_at DATETIME(3) NOT NULL,
    status ENUM('healthy','recovering','down') NOT NULL,
    recent_recovery_count INT NOT NULL DEFAULT 0,
    watchdog_version VARCHAR(20) NOT NULL,
    process_status_json JSON NOT NULL,
    last_recovery_stage VARCHAR(20) NULL,
    last_recovery_at DATETIME(3) NULL,
    INDEX idx_watchdog_pings_tenant_received (tenant_id_hash, received_at),
    INDEX idx_watchdog_pings_status_received (status, received_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='헌법 #22 본사 메타 ping (업무 데이터 0)';

CREATE TABLE IF NOT EXISTS watchdog_emergencies (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    tenant_id_hash CHAR(71) NOT NULL,
    received_at DATETIME(3) NOT NULL,
    reason VARCHAR(50) NOT NULL,
    stage VARCHAR(20) NULL,
    cs_notified_at DATETIME(3) NULL,
    cs_resolved_at DATETIME(3) NULL,
    INDEX idx_watchdog_emerg_tenant_received (tenant_id_hash, received_at),
    INDEX idx_watchdog_emerg_unresolved (cs_resolved_at, received_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='워치독 emergency 알림 (CS 자동 발신 트리거)';
