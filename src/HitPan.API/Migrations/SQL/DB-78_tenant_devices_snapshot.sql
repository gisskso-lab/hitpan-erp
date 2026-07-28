-- DB-78: 백오피스 Pull 동기화 — 기기 복사본
-- 사장님 결재 2026-06-01 / 헌법 #18·#22 (데이터 최소주의) 정합
-- 본사가 받는 컬럼: 기기ID·기기명·등록일 3개만

CREATE TABLE IF NOT EXISTS tenant_devices_snapshot (
    snapshot_id CHAR(36) NOT NULL COMMENT 'UUID',
    tenant_id CHAR(36) NOT NULL COMMENT '테넌트 ID',
    device_id CHAR(36) NOT NULL COMMENT '기기 ID (원본 tenant_devices 참조)',
    device_name VARCHAR(100) NULL COMMENT '기기명',
    registered_at TIMESTAMP NOT NULL COMMENT '등록일',
    synced_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '마지막 Pull 시각',
    PRIMARY KEY (snapshot_id),
    UNIQUE KEY uq_tenant_device (tenant_id, device_id),
    INDEX idx_tenant_synced (tenant_id, synced_at),
    INDEX idx_synced (synced_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='[헌법 #18·#22] 백오피스 Pull 복사본 — 3개 컬럼만';
