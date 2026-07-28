-- 가격·프로모션 관리 테이블 (브라운킴 PM 박제 2026-06-08, 사장님 결재)
-- 헌법: 본사 마스터가 화면에서 실시간 변경, 코드 박제 절대 금지

SET FOREIGN_KEY_CHECKS=0;

-- ────────────────────────────────────────
-- 1) pricing_plans: 요금제 정의 + 현재 가격
-- ────────────────────────────────────────
CREATE TABLE IF NOT EXISTS pricing_plans (
    plan_id          VARCHAR(20)  NOT NULL PRIMARY KEY,            -- 'basic', 'pro', 'enterprise'
    plan_name        VARCHAR(50)  NOT NULL,                        -- '기본', '프로', '엔터프라이즈'
    description      TEXT         NULL,
    monthly_price    DECIMAL(10,0) NOT NULL DEFAULT 0,            -- 월 가격(원)
    yearly_price     DECIMAL(10,0) NOT NULL DEFAULT 0,            -- 연 가격(원, 보통 월x10 할인)
    max_users        INT          NOT NULL DEFAULT 3,              -- 자식계정 한도
    max_devices      INT          NOT NULL DEFAULT 3,
    ai_token_monthly INT          NOT NULL DEFAULT 0,              -- 월 AI 토큰 한도
    features_json    TEXT         NULL,                            -- 기능 목록 JSON
    is_active        TINYINT(1)   NOT NULL DEFAULT 1,              -- 0=비활성(신규 가입 차단)
    is_visible       TINYINT(1)   NOT NULL DEFAULT 1,              -- 0=랜딩에 미노출
    display_order    INT          NOT NULL DEFAULT 0,              -- 정렬 순서
    created_at       DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at       DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    INDEX ix_active_visible (is_active, is_visible)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ────────────────────────────────────────
-- 2) promotions: 프로모션·할인·이벤트
-- ────────────────────────────────────────
CREATE TABLE IF NOT EXISTS promotions (
    promotion_id     VARCHAR(36)  NOT NULL PRIMARY KEY,
    promotion_code   VARCHAR(40)  NOT NULL,                        -- 'SUMMER2026', 'BLACK2026'
    promotion_name   VARCHAR(100) NOT NULL,
    description      TEXT         NULL,
    discount_type    VARCHAR(20)  NOT NULL,                        -- 'percent', 'fixed', 'free_months'
    discount_value   DECIMAL(10,2) NOT NULL,                       -- percent=0~100, fixed=원, free_months=개월
    target_plan_id   VARCHAR(20)  NULL,                            -- NULL=전체 요금제
    target_scope     VARCHAR(20)  NOT NULL DEFAULT 'all',          -- 'all', 'reseller', 'new_only', 'specific'
    target_filter    TEXT         NULL,                            -- JSON: 대리점 ID 목록·특정 tenant_id 목록 등
    starts_at        DATETIME     NOT NULL,
    ends_at          DATETIME     NOT NULL,
    max_uses         INT          NULL,                            -- NULL=무제한, 숫자=총 사용 한도
    used_count       INT          NOT NULL DEFAULT 0,
    is_active        TINYINT(1)   NOT NULL DEFAULT 1,
    created_by       VARCHAR(36)  NOT NULL,                        -- platform_admin.admin_id
    created_at       DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at       DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    UNIQUE KEY uk_promotion_code (promotion_code),
    INDEX ix_active_period (is_active, starts_at, ends_at),
    INDEX ix_target_plan (target_plan_id),
    INDEX ix_created_by (created_by)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ────────────────────────────────────────
-- 3) pricing_history: 가격 변경 이력 (INSERT ONLY, 헌법 #3)
--    감사 추적: 누가·언제·무엇을·왜
-- ────────────────────────────────────────
CREATE TABLE IF NOT EXISTS pricing_history (
    history_id       BIGINT       NOT NULL AUTO_INCREMENT PRIMARY KEY,
    entity_type      VARCHAR(20)  NOT NULL,                        -- 'plan', 'promotion'
    entity_id        VARCHAR(36)  NOT NULL,                        -- plan_id or promotion_id
    action           VARCHAR(20)  NOT NULL,                        -- 'create', 'update', 'activate', 'deactivate'
    before_json      TEXT         NULL,                            -- 변경 전 스냅샷
    after_json       TEXT         NULL,                            -- 변경 후 스냅샷
    reason           VARCHAR(500) NULL,                            -- 변경 사유
    changed_by       VARCHAR(36)  NOT NULL,                        -- platform_admin.admin_id
    changed_by_name  VARCHAR(50)  NULL,                            -- 감사용 스냅샷
    changed_at       DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    INDEX ix_entity (entity_type, entity_id, changed_at),
    INDEX ix_changed_by (changed_by, changed_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ────────────────────────────────────────
-- 4) tenant_rewards: 개별 고객사 리워드·크레딧·기간 연장
--    헌법: 본사 마스터가 특정 고객사에 직접 지급
-- ────────────────────────────────────────
CREATE TABLE IF NOT EXISTS tenant_rewards (
    reward_id        BIGINT       NOT NULL AUTO_INCREMENT PRIMARY KEY,
    tenant_id        VARCHAR(36)  NOT NULL,
    reward_type      VARCHAR(20)  NOT NULL,                        -- 'credit', 'extend_days', 'discount', 'plan_upgrade'
    reward_value     DECIMAL(10,2) NOT NULL,                       -- 크레딧 금액·연장 일수·할인율 등
    reason           VARCHAR(500) NULL,                            -- 지급 사유
    expires_at       DATETIME     NULL,                            -- 만료일 (NULL=영구)
    is_consumed      TINYINT(1)   NOT NULL DEFAULT 0,              -- 소진 여부
    consumed_at      DATETIME     NULL,
    granted_by       VARCHAR(36)  NOT NULL,                        -- platform_admin.admin_id
    granted_at       DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    INDEX ix_tenant (tenant_id, is_consumed),
    INDEX ix_granted_by (granted_by, granted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET FOREIGN_KEY_CHECKS=1;

-- ────────────────────────────────────────
-- 시드 요금제 박음 (사장님이 백오피스에서 즉시 수정 가능)
-- 헌법: 코드 박제 아님, DB 행이므로 백오피스 UI에서 자유 변경
-- ────────────────────────────────────────
INSERT INTO pricing_plans (plan_id, plan_name, description, monthly_price, yearly_price, max_users, max_devices, ai_token_monthly, features_json, is_active, is_visible, display_order)
VALUES
    ('basic',      '기본',           '소규모 사업체용 기본 기능',         29000,  290000, 3,  3,  100000, '["기본 ERP","월 10만 토큰","대시보드"]', 1, 1, 1),
    ('pro',        '프로',           '중규모 사업체용 확장 기능',         59000,  590000, 10, 10, 500000, '["프로 ERP","월 50만 토큰","고급 리포트","BOM"]', 1, 1, 2),
    ('enterprise', '엔터프라이즈',   '대규모 사업체용 전체 기능',         100000, 1000000, 50, 50, 3000000, '["전체 ERP","월 300만 토큰","무제한 리포트","API","우선 지원"]', 1, 1, 3)
ON DUPLICATE KEY UPDATE
    plan_name = VALUES(plan_name),
    description = VALUES(description),
    updated_at = CURRENT_TIMESTAMP(6);

SELECT 'pricing_schema_ok' AS status, COUNT(*) AS plan_count FROM pricing_plans;
