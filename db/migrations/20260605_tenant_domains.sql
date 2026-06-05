-- Cloudflare 도메인 자동 발급 메타 (W14 골격, 사장님 결재 2026-06-05)
--
-- 헌법 정합:
--   #17 ENGINE=InnoDB
--   #18·#22 토큰·시크릿 0건 (Cloudflare API 토큰은 환경변수 CLOUDFLARE_API_TOKEN)
--   #29 자동 발급 호출은 사장님 결재 후 환경변수 설정 후에만 실행
--   #34 베타부터 정식 완성도 — www.{tenant_code}.hitpan.kr 골격

CREATE TABLE IF NOT EXISTS tenant_domains (
    domain_id        BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    tenant_id        CHAR(36)        NOT NULL,
    domain           VARCHAR(255)    NOT NULL,
    cf_zone_id       VARCHAR(64)     NULL,
    cf_record_id     VARCHAR(64)     NULL,
    cf_tunnel_id     VARCHAR(64)     NULL,
    status           VARCHAR(20)     NOT NULL DEFAULT 'pending',
    issued_at        DATETIME(6)     NULL,
    last_error       VARCHAR(500)    NULL,
    created_at       DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at       DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (domain_id),
    UNIQUE KEY uk_domain (domain),
    KEY ix_tenant (tenant_id),
    KEY ix_status (status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT IGNORE INTO bo_permissions (permission_key, display_name, description, category, allowed_roles)
VALUES
    ('tenant_domain.list',   '도메인 발급 이력 조회', 'Cloudflare 도메인 발급 이력·상태 조회', 'tenant', 'owner,admin'),
    ('tenant_domain.issue',  '도메인 자동 발급 실행', 'Cloudflare DNS + 터널 자동 발급 (헌법 #29)', 'tenant', 'owner'),
    ('tenant_domain.revoke', '도메인 회수',          'Cloudflare DNS 레코드 + 터널 회수',        'tenant', 'owner');
