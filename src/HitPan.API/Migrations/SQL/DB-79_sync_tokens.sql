-- DB-79: 백오피스 Pull 동기화 — Sync 토큰
-- 사장님 결재 2026-06-01 / 헌법 #5·#7·#18·#23 정합
-- 정책: 24시간 만료, 회전(발급 시 이전 토큰 무효화), 읽기 전용

CREATE TABLE IF NOT EXISTS sync_tokens (
    token_id CHAR(36) NOT NULL COMMENT 'UUID',
    tenant_id CHAR(36) NOT NULL COMMENT '테넌트 ID',
    token_hash VARCHAR(128) NOT NULL COMMENT 'SHA-256 hash (평문 토큰 미저장)',
    issued_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '발급 시각',
    expires_at TIMESTAMP NOT NULL COMMENT '만료 시각 (issued_at + 24h)',
    revoked_at TIMESTAMP NULL DEFAULT NULL COMMENT '회수 시각 (회전 시 INSERT)',
    last_used_at TIMESTAMP NULL DEFAULT NULL COMMENT '마지막 사용 시각',
    PRIMARY KEY (token_id),
    UNIQUE KEY uq_token_hash (token_hash),
    INDEX idx_tenant_active (tenant_id, revoked_at, expires_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='[헌법 #5·#23] Sync 토큰 — SHA-256 해시만 저장, 회전 정책';
