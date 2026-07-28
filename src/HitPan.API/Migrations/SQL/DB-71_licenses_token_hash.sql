-- =============================================================
-- DB-71: licenses 테이블 — 워치독 Bearer 인증
-- 헌법 #22 정합: license_key 원본 미저장, token_hash만 저장
-- =============================================================

CREATE TABLE IF NOT EXISTS licenses (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    license_key_hash CHAR(64) NOT NULL COMMENT 'sha256(license_key) — 회원 발급 키',
    machine_guid_hash CHAR(64) NULL COMMENT 'sha256(machine_guid) — 설치 PC 식별 (1회 바인딩)',
    tenant_id_hash CHAR(71) NOT NULL COMMENT 'sha256:{64hex} — 워치독 ping과 매칭',
    token_hash CHAR(64) NOT NULL COMMENT 'sha256(license_key|machine_guid|tenant_id_hash)',
    status ENUM('pending','active','suspended','expired') NOT NULL DEFAULT 'pending',
    issued_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    activated_at DATETIME(3) NULL,
    expires_at DATETIME(3) NULL,
    revoked_at DATETIME(3) NULL,
    revoked_reason VARCHAR(255) NULL,
    UNIQUE KEY uk_licenses_license_hash (license_key_hash),
    UNIQUE KEY uk_licenses_token_hash (token_hash),
    INDEX idx_licenses_tenant_status (tenant_id_hash, status),
    INDEX idx_licenses_status_expires (status, expires_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='워치독 인증 + SaaS 라이선스 통합 (헌법 #22 정합, 원본 키 0)';
