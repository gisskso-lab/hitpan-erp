-- W17 프로모션 백엔드 DDL (사장님 결재 2026-06-05)
--
-- 헌법 정합:
--   #3 INSERT ONLY (promotion_usages)
--   #4 decimal 금액
--   #17 ENGINE=InnoDB
--   #18·#22 본사 메타만 (고객 평문 0)
--   #25 안전하게 (4-eyes 추후 확장)
--   #34 정식 완성도 — 베타부터 정합

-- 1) 프로모션 정의
CREATE TABLE IF NOT EXISTS promotions (
    promotion_id      BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
    promo_code        VARCHAR(40)      NOT NULL,
    title             VARCHAR(200)     NOT NULL,
    discount_type     VARCHAR(20)      NOT NULL DEFAULT 'percent',
    discount_value    DECIMAL(10,2)    NOT NULL,
    starts_at         DATETIME(6)      NOT NULL,
    ends_at           DATETIME(6)      NOT NULL,
    max_uses          INT              NOT NULL DEFAULT 0,
    use_count         INT              NOT NULL DEFAULT 0,
    target_plan       VARCHAR(40)      NULL,
    status            VARCHAR(20)      NOT NULL DEFAULT 'active',
    created_by        VARCHAR(36)      NULL,
    created_at        DATETIME(6)      NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at        DATETIME(6)      NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (promotion_id),
    UNIQUE KEY uk_promo_code (promo_code),
    KEY ix_status_period (status, starts_at, ends_at),
    KEY ix_target_plan (target_plan)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 2) 프로모션 사용 이력 (INSERT ONLY — 헌법 #3)
CREATE TABLE IF NOT EXISTS promotion_usages (
    usage_id          BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
    promotion_id      BIGINT UNSIGNED  NOT NULL,
    promo_code        VARCHAR(40)      NOT NULL,
    tenant_id         CHAR(36)         NULL,
    signup_token      VARCHAR(64)      NULL,
    applied_amount    DECIMAL(14,2)    NOT NULL DEFAULT 0.00,
    applied_at        DATETIME(6)      NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    source            VARCHAR(40)      NOT NULL DEFAULT 'landing_signup',
    PRIMARY KEY (usage_id),
    KEY ix_promotion (promotion_id),
    KEY ix_tenant (tenant_id),
    KEY ix_signup_token (signup_token),
    KEY ix_applied_at (applied_at),
    CONSTRAINT fk_usage_promo FOREIGN KEY (promotion_id)
        REFERENCES promotions(promotion_id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3) bo_permissions 시드
INSERT IGNORE INTO bo_permissions (permission_key, display_name, description, category, allowed_roles)
VALUES
    ('promotion.list',     '프로모션 목록 조회', '본사 영역 프로모션 전수 조회',                  'promotion', 'owner,admin'),
    ('promotion.create',   '프로모션 생성',    '신규 프로모션 등록 (코드 중복 차단)',            'promotion', 'owner,admin'),
    ('promotion.toggle',   '프로모션 활성 토글', '활성/비활성/만료 처리',                        'promotion', 'owner,admin'),
    ('promotion.usage',    '사용 이력 조회',    'promotion_usages 이력 조회 (INSERT ONLY)',     'promotion', 'owner,admin');
