-- ERP 로컬 본사 영역 캐시 (사장님 결재 2026-06-04, 헌법 #35 객체 완전 분리 Ⓐ)
--
-- 헌법 #35 정합:
--   - 본사 영역(라이선스·구독·AI·기기·대리점)의 원본은 백오피스 DB(hitpan_backoffice.tenants)
--   - ERP는 자체 캐시(local_subscription)만 보고, 백오피스 URL·존재 의존 0건
--   - 백오피스→ERP 동기화는 W2 서명 토큰(부트스트랩) + 추후 webhook(별도 차수)으로
--
-- 박제 위치: hitpan_erp DB
-- tenant_id는 백오피스 tenants.tenant_id와 동일값 (FK 정합 보존)

USE hitpan_erp;

CREATE TABLE IF NOT EXISTS local_subscription (
    tenant_id                CHAR(36)        NOT NULL PRIMARY KEY,

    -- 구독 (백오피스 tenants.subscription_tier 미러)
    subscription_tier        VARCHAR(20)     NOT NULL DEFAULT 'basic'
        COMMENT 'basic / pro / enterprise',
    status                   VARCHAR(20)     NOT NULL DEFAULT 'active'
        COMMENT 'active / suspended / trial / inactive',
    trial_ends_at            DATETIME        NULL,

    -- AI (백오피스 tenants.ai_* 미러)
    ai_mode                  VARCHAR(20)     NOT NULL DEFAULT 'hitpan_pool'
        COMMENT 'hitpan_pool / byok / hybrid',
    ai_token_monthly_limit   INT             NOT NULL DEFAULT 100000,
    ai_token_extra           INT             NOT NULL DEFAULT 0,
    anthropic_api_key_encrypted VARCHAR(512) NULL COMMENT 'BYOK 모드 AES-256 암호화',
    anthropic_api_key_last4  VARCHAR(8)      NULL,
    anthropic_key_status     VARCHAR(20)     NOT NULL DEFAULT 'none',

    -- 기기 (백오피스 tenants.extra_device_slots 미러)
    max_users                TINYINT UNSIGNED NOT NULL DEFAULT 3,
    extra_device_slots       INT             NOT NULL DEFAULT 0,

    -- 대리점 (백오피스 tenants.reseller_id / reseller_tier 미러)
    reseller_id              VARCHAR(36)     NULL,
    reseller_tier            TINYINT UNSIGNED NOT NULL DEFAULT 0,

    -- 동기화 메타
    last_sync_at             DATETIME(6)     NULL COMMENT '백오피스→ERP 마지막 동기화 시각',
    sync_source              VARCHAR(20)     NOT NULL DEFAULT 'bootstrap'
        COMMENT 'bootstrap / webhook / manual',

    created_at               DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at               DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),

    KEY idx_local_sub_status (status),
    KEY idx_local_sub_reseller (reseller_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ────────────────────────────────────────
-- 기존 tenants 데이터에서 본사 영역만 이관 (W1 DB 분리 전 실행)
-- ────────────────────────────────────────
INSERT IGNORE INTO local_subscription (
    tenant_id, subscription_tier, status, trial_ends_at,
    ai_mode, ai_token_monthly_limit, ai_token_extra,
    anthropic_api_key_encrypted, anthropic_api_key_last4, anthropic_key_status,
    max_users, extra_device_slots,
    reseller_id, reseller_tier,
    last_sync_at, sync_source, created_at, updated_at
)
SELECT
    tenant_id,
    COALESCE(subscription_tier, 'basic'),
    COALESCE(status, 'active'),
    trial_ends_at,
    COALESCE(ai_mode, 'hitpan_pool'),
    COALESCE(ai_token_monthly_limit, 100000),
    COALESCE(ai_token_extra, 0),
    anthropic_api_key_encrypted,
    anthropic_api_key_last4,
    COALESCE(anthropic_key_status, 'none'),
    COALESCE(max_users, 3),
    COALESCE(extra_device_slots, 0),
    reseller_id,
    COALESCE(reseller_tier, 0),
    NOW(6), 'bootstrap', created_at, updated_at
FROM tenants;

-- ────────────────────────────────────────
-- 검증 쿼리
-- ────────────────────────────────────────
-- SELECT 'tenants' AS t, COUNT(*) AS cnt FROM tenants
-- UNION ALL SELECT 'local_subscription', COUNT(*) FROM local_subscription;
