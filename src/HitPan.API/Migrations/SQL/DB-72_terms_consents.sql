-- =============================================================
-- DB-72: 첫 로그인 약관 4건 강제 동의 기록 (헌법 #24 책임 분산)
-- - 모든 사용자 (대표·직원) 필수
-- - 동의 일시·IP·약관 버전 DB 기록 필수 (3개 시스템 헌법)
-- - 필수 4건 미동의 시 미들웨어에서 차단 (조회·동의 endpoint 외 전부 403)
-- 절대원칙: tenant_id JWT 클레임 / INSERT ONLY (UPDATE 금지 - 버전 변경 시 신규 row)
-- 테이블명: user_terms_consent (TermsConsentController 박제 정합)
-- =============================================================

CREATE TABLE IF NOT EXISTS user_terms_consent (
    consent_id CHAR(36) NOT NULL PRIMARY KEY COMMENT 'UUID',
    tenant_id CHAR(36) NOT NULL,
    user_id CHAR(36) NOT NULL,
    terms_version VARCHAR(20) NOT NULL COMMENT 'v2.0.0 등',
    agree_service TINYINT(1) NOT NULL DEFAULT 0,
    agree_privacy TINYINT(1) NOT NULL DEFAULT 0,
    agree_subscription TINYINT(1) NOT NULL DEFAULT 0,
    agree_data_ownership TINYINT(1) NOT NULL DEFAULT 0 COMMENT '헌법 #22·#24',
    agree_marketing TINYINT(1) NULL DEFAULT NULL COMMENT '선택',
    agreed_at DATETIME(3) NOT NULL,
    client_ip VARCHAR(45) NOT NULL COMMENT 'IPv4/IPv6',
    user_agent VARCHAR(500) NULL,
    INDEX idx_user_terms_consent_tenant_user (tenant_id, user_id, terms_version),
    INDEX idx_user_terms_consent_agreed_at (agreed_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='첫 로그인 약관 4건 강제 동의 INSERT ONLY (헌법 #24)';
