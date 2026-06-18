-- ============================================================================
-- 히트판 백오피스(hitpan_backoffice) 자립형 통합 스키마 — core
-- ============================================================================
-- 작성: PM 브라운킴 (사장님 결재 2026-06-18)
--
-- 헌법 정합:
--   #18·#22 — 백오피스(NCP 클라우드)는 메타·해시만. 고객사 업무 데이터·민감정보 0건.
--   #29 — 본 SQL은 형상관리 정의일 뿐. NCP 실제 CREATE·실행은 사장님 영역.
--   #34 — 베타부터 정식 완성도. ERP 의존(LIKE) 제거한 자립형으로 처음부터 정식.
--   #35 — ERP(고객사 PC 로컬) ↔ 백오피스(NCP) 물리 완전 분리.
--
-- ★ 자립형 원칙 (이전 db/migrations/20260604_backoffice_db_split.sql의 LIKE hitpan_erp.* 폐기):
--   - 어떤 CREATE TABLE도 hitpan_erp DB에 의존하지 않는다(LIKE/REFERENCES 금지).
--   - 백오피스는 NCP에 ERP DB 없이 단독으로 선다.
--
-- ★ 데이터 흐름도 정정 (사장님 결재 2026-06-18 "길 B" + 다이어트):
--   - tenants에서 민감정보 18컬럼 제외(사업자번호·대표자명·주소·법인번호·업태·종목 등).
--     이 민감정보는 ERP 로컬 local_company에만 저장. 백오피스는 계정·연락처·구독 메타만.
--   - license_key_plain(평문 시리얼키) 폐기 — 해시(license_key_hash)만 보유. 분실 시 신규 재발급.
--   - tenant_certificates(인증서 PFX)·tenant_settings(ERP 업무설정)는 ERP 로컬 전용 → 백오피스 미보유.
--
-- ★ CREATE 순서 — FK 부모를 자식보다 먼저:
--   platforms → resellers → tenants → bo_users → promotions(usage용) → reseller_settlements → 나머지
--
-- DDL 출처: 전수조사(DB 매니저, 2026-06-18) — 모든 컬럼은 실제 .sql에서 수집(추측 0).
-- ============================================================================

SET FOREIGN_KEY_CHECKS=0;

-- ────────────────────────────────────────────────────────────────────────
-- 1) platforms — 플랫폼 마스터 (FK 부모, 의존 0)
--    출처: tmp-build/backoffice-schema.sql:4-14
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS platforms (
    platform_id    varchar(36)  NOT NULL,
    platform_name  varchar(100) NOT NULL,
    domain         varchar(100) NULL DEFAULT NULL,
    contact_email  varchar(100) NULL DEFAULT NULL,
    contact_tel    varchar(20)  NULL DEFAULT NULL,
    is_active      tinyint(1)   NOT NULL DEFAULT 1,
    created_at     datetime(6)  NOT NULL DEFAULT current_timestamp(6),
    updated_at     datetime(6)  NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
    PRIMARY KEY (platform_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='플랫폼 마스터';

-- ────────────────────────────────────────────────────────────────────────
-- 2) platform_admins — 본사 관리자 계정 (의존 0)
--    출처: tmp-build/backoffice-schema.sql:18-32
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS platform_admins (
    admin_id       char(36)     NOT NULL,
    email          varchar(100) NOT NULL,
    password_hash  varchar(256) NOT NULL,
    admin_name     varchar(50)  NOT NULL,
    role           enum('super_admin','billing_admin','cs_admin','readonly') NOT NULL,
    department     varchar(50)  NULL DEFAULT NULL,
    is_active      tinyint(1)   NOT NULL DEFAULT 1,
    last_login_at  datetime     NULL DEFAULT NULL,
    created_at     datetime     NOT NULL DEFAULT current_timestamp(),
    updated_at     datetime     NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
    PRIMARY KEY (admin_id),
    UNIQUE KEY uk_platform_admins_email (email),
    KEY idx_platform_admins_role_active (role, is_active)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='본사 관리자 계정';

-- ────────────────────────────────────────────────────────────────────────
-- 3) resellers — 대리점 마스터 (FK 부모)
--    출처: tmp-build/backoffice-schema.sql:266-290
--    bank_account = AES-256 암호화(헌법 #5). 대리점은 백오피스 고유 도메인(헌법 #35).
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS resellers (
    reseller_id     char(36)     NOT NULL,
    reseller_code   varchar(20)  NOT NULL,
    reseller_name   varchar(100) NOT NULL,
    biz_no          varchar(12)  NOT NULL,
    ceo_name        varchar(50)  NOT NULL,
    tel             varchar(20)  NULL DEFAULT NULL,
    address         varchar(200) NULL DEFAULT NULL,
    bank_name       varchar(30)  NULL DEFAULT NULL,
    bank_account    varchar(500) NULL DEFAULT NULL,
    account_holder  varchar(30)  NULL DEFAULT NULL,
    contact_person  varchar(50)  NULL DEFAULT NULL,
    contact_phone   varchar(20)  NULL DEFAULT NULL,
    contact_email   varchar(100) NULL DEFAULT NULL,
    join_date       date         NOT NULL,
    status          enum('active','suspended','inactive','terminated') NOT NULL DEFAULT 'active',
    created_at      datetime     NOT NULL DEFAULT current_timestamp(),
    updated_at      datetime     NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
    created_by      char(36)     NULL DEFAULT NULL,
    PRIMARY KEY (reseller_id),
    UNIQUE KEY uk_resellers_code (reseller_code),
    UNIQUE KEY uk_resellers_biz_no (biz_no),
    KEY idx_resellers_status (status),
    KEY idx_resellers_join_date (join_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='대리점 마스터';

-- ────────────────────────────────────────────────────────────────────────
-- 4) tenants — 고객사 마스터 (★다이어트: 민감 18컬럼 + license_key_plain 제외)
--    출처: installer/hitpan_db.sql:128508-128556 + serial_verify ALTER 3컬럼
--    ★ 남긴 것: 계정·연락처·구독/라이선스해시·도메인·AI메타·대리점연결만.
--    ★ domain_alias: 원본 DDL이 어느 .sql에도 없음(운영 수동 ALTER만). 코드 사용형태가
--       서브도메인 문자열이라 VARCHAR(255)로 확정(사장님 결재 시 조정 가능).
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS tenants (
    tenant_id                     varchar(36)  NOT NULL,
    platform_id                   varchar(36)  NULL DEFAULT NULL,
    tenant_code                   varchar(20)  NOT NULL,
    company_name                  varchar(100) NOT NULL,
    tel                           varchar(100) NULL DEFAULT NULL,
    email                         varchar(100) NULL DEFAULT NULL,
    domain_alias                  varchar(255) NULL DEFAULT NULL,
    reseller_id                   varchar(36)  NULL DEFAULT NULL,
    reseller_tier                 tinyint(3) unsigned NOT NULL DEFAULT 0,
    max_users                     tinyint(3) unsigned NOT NULL DEFAULT 3,
    status                        varchar(20)  NOT NULL DEFAULT 'trial',
    is_locked_from_landing        tinyint(1)   NOT NULL DEFAULT 0,
    bootstrap_at                  datetime(6)  NULL DEFAULT NULL,
    trial_ends_at                 datetime(6)  NULL DEFAULT NULL,
    db_host                       varchar(100) NOT NULL DEFAULT '',
    db_name                       varchar(50)  NOT NULL DEFAULT '',
    license_key_hash              varchar(256) NOT NULL DEFAULT '',
    serial_verified               tinyint(1)   NOT NULL DEFAULT 0,
    serial_verified_at            datetime     NULL DEFAULT NULL,
    serial_verified_fingerprint   varchar(128) NULL DEFAULT NULL,
    ai_mode                       varchar(20)  NOT NULL DEFAULT 'hitpan_pool',
    ai_token_monthly_limit        int(11)      NOT NULL DEFAULT 100000,
    ai_token_extra                int(11)      NOT NULL DEFAULT 0,
    anthropic_api_key_encrypted   varchar(512) NULL DEFAULT NULL,
    anthropic_api_key_last4       varchar(8)   NULL DEFAULT NULL,
    anthropic_key_status          varchar(20)  NOT NULL DEFAULT 'none',
    subscription_tier             varchar(20)  NOT NULL DEFAULT 'basic',
    extra_device_slots            int(11)      NOT NULL DEFAULT 0,
    created_at                    datetime(6)  NOT NULL DEFAULT current_timestamp(6),
    updated_at                    datetime(6)  NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
    PRIMARY KEY (tenant_id),
    UNIQUE KEY uq_tenant_code (tenant_code),
    KEY ix_tenants_reseller (reseller_id),
    KEY ix_tenants_domain_alias (domain_alias),
    CONSTRAINT fk_tenants_reseller FOREIGN KEY (reseller_id)
        REFERENCES resellers (reseller_id) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='고객사 마스터 (다이어트: 민감정보 제외)';

-- ────────────────────────────────────────────────────────────────────────
-- 5) landing_signups — 가입 신청 (biz_no는 해시만, 평문 0)
--    출처: db/migrations/20260605_landing_signups.sql:12-31
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS landing_signups (
    signup_id      bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    signup_token   varchar(64)  NOT NULL,
    biz_no_hash    varchar(128) NOT NULL,
    company_name   varchar(120) NOT NULL,
    email          varchar(100) NOT NULL,
    phone          varchar(30)  NOT NULL,
    plan_type      varchar(20)  NOT NULL DEFAULT 'basic',
    desired_domain varchar(255) NULL DEFAULT NULL,
    reseller_code  varchar(20)  NULL DEFAULT NULL,
    agree_terms    tinyint(1)   NOT NULL DEFAULT 0,
    agree_privacy  tinyint(1)   NOT NULL DEFAULT 0,
    status         varchar(20)  NOT NULL DEFAULT 'submitted',
    submitted_at   datetime(6)  NOT NULL DEFAULT current_timestamp(6),
    PRIMARY KEY (signup_id),
    UNIQUE KEY uk_signup_token (signup_token),
    KEY ix_biz_no_hash (biz_no_hash),
    KEY ix_email (email),
    KEY ix_status (status),
    KEY ix_submitted_at (submitted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='가입 신청 (biz_no 해시만)';

-- ────────────────────────────────────────────────────────────────────────
-- 6) reseller_accounts — 대리점 계정 (→ resellers)
--    출처: tmp-build/backoffice-schema.sql:294-311
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS reseller_accounts (
    account_id     char(36)     NOT NULL,
    reseller_id    char(36)     NOT NULL,
    email          varchar(100) NOT NULL,
    password_hash  varchar(256) NOT NULL,
    account_name   varchar(50)  NOT NULL,
    role           enum('reseller_admin','reseller_user','reseller_readonly') NOT NULL,
    phone          varchar(20)  NULL DEFAULT NULL,
    is_active      tinyint(1)   NOT NULL DEFAULT 1,
    last_login_at  datetime     NULL DEFAULT NULL,
    created_at     datetime     NOT NULL DEFAULT current_timestamp(),
    updated_at     datetime     NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
    PRIMARY KEY (account_id),
    UNIQUE KEY uk_reseller_accounts_email (reseller_id, email),
    KEY idx_reseller_accounts_reseller (reseller_id),
    KEY idx_reseller_accounts_active (is_active),
    CONSTRAINT fk_reseller_accounts_reseller FOREIGN KEY (reseller_id)
        REFERENCES resellers (reseller_id) ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='대리점 계정';

-- ────────────────────────────────────────────────────────────────────────
-- 7) reseller_applications — 대리점 신청 (의존 0)
--    출처: tmp-build/backoffice-schema.sql:315-335
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS reseller_applications (
    application_id      char(36)     NOT NULL,
    company_name        varchar(120) NOT NULL,
    representative_name varchar(50)  NOT NULL,
    contact_name        varchar(50)  NOT NULL,
    contact_email       varchar(100) NOT NULL,
    contact_phone       varchar(30)  NOT NULL,
    business_no         varchar(20)  NULL DEFAULT NULL,
    region              varchar(50)  NULL DEFAULT NULL,
    sales_channel       varchar(100) NULL DEFAULT NULL,
    expected_customers  int(11)      NULL DEFAULT NULL,
    motivation          text         NULL DEFAULT NULL,
    status              enum('pending','reviewing','approved','rejected') NOT NULL DEFAULT 'pending',
    submitted_at        timestamp    NOT NULL DEFAULT current_timestamp(),
    reviewed_at         timestamp    NULL DEFAULT NULL,
    reviewed_by         varchar(36)  NULL DEFAULT NULL,
    rejection_reason    varchar(500) NULL DEFAULT NULL,
    PRIMARY KEY (application_id),
    KEY idx_status_submitted (status, submitted_at),
    KEY idx_email (contact_email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='대리점 신청';

-- ────────────────────────────────────────────────────────────────────────
-- 8) bo_users — 백오피스 운영자 (FK 부모, → resellers)
--    출처: db/migrations/20260604_bo_users.sql:13-32
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS bo_users (
    user_id          char(36)     NOT NULL,
    email            varchar(255) NOT NULL,
    password_hash    varchar(255) NOT NULL,
    name             varchar(100) NOT NULL,
    role             varchar(32)  NOT NULL,
    reseller_id      char(36)     NULL DEFAULT NULL,
    is_active        tinyint(1)   NOT NULL DEFAULT 1,
    failed_login_cnt int(11)      NOT NULL DEFAULT 0,
    last_login_at    datetime     NULL DEFAULT NULL,
    created_at       datetime     NOT NULL DEFAULT current_timestamp(),
    updated_at       datetime     NULL DEFAULT NULL ON UPDATE current_timestamp(),
    PRIMARY KEY (user_id),
    UNIQUE KEY uq_bo_users_email (email),
    KEY idx_bo_users_role (role, is_active),
    KEY idx_bo_users_reseller (reseller_id),
    CONSTRAINT fk_bo_users_reseller FOREIGN KEY (reseller_id)
        REFERENCES resellers (reseller_id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='백오피스 운영자';

-- ────────────────────────────────────────────────────────────────────────
-- 9) bo_user_mfa — 백오피스 MFA (→ bo_users)
--    출처: db/migrations/20260604_bo_audit_log.sql:61-73
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS bo_user_mfa (
    user_id           char(36)       NOT NULL,
    secret_encrypted  varbinary(255) NOT NULL,
    is_enabled        tinyint(1)     NOT NULL DEFAULT 0,
    enrolled_at       datetime(6)    NULL DEFAULT NULL,
    last_used_at      datetime(6)    NULL DEFAULT NULL,
    backup_codes_hash text           NULL DEFAULT NULL,
    created_at        datetime(6)    NOT NULL DEFAULT current_timestamp(6),
    updated_at        datetime(6)    NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
    PRIMARY KEY (user_id),
    CONSTRAINT fk_bo_user_mfa_user FOREIGN KEY (user_id)
        REFERENCES bo_users (user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='백오피스 MFA';

-- ────────────────────────────────────────────────────────────────────────
-- 10) bo_permissions — 백오피스 권한 시드 (의존 0)
--     출처: db/migrations/20260604_bo_permissions.sql:9-18
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS bo_permissions (
    permission_key  varchar(80)  NOT NULL,
    display_name    varchar(100) NOT NULL,
    description     text         NULL DEFAULT NULL,
    category        varchar(40)  NOT NULL,
    allowed_roles   varchar(200) NOT NULL,
    updated_at      datetime     NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
    updated_by      char(36)     NULL DEFAULT NULL,
    PRIMARY KEY (permission_key),
    KEY idx_category (category)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='백오피스 권한 시드';

-- ────────────────────────────────────────────────────────────────────────
-- 11) bo_audit_log — 백오피스 감사 로그 (의존 0)
--     출처: db/migrations/20260604_bo_audit_log.sql:13-30
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS bo_audit_log (
    log_id        bigint       NOT NULL AUTO_INCREMENT,
    actor_user_id char(36)     NOT NULL,
    actor_email   varchar(255) NOT NULL,
    actor_role    varchar(32)  NOT NULL,
    action        varchar(64)  NOT NULL,
    target_type   varchar(40)  NULL DEFAULT NULL,
    target_id     varchar(64)  NULL DEFAULT NULL,
    detail_json   text         NULL DEFAULT NULL,
    ip_address    varchar(45)  NULL DEFAULT NULL,
    user_agent    varchar(255) NULL DEFAULT NULL,
    created_at    datetime(6)  NOT NULL DEFAULT current_timestamp(6),
    PRIMARY KEY (log_id),
    KEY idx_audit_actor (actor_user_id, created_at),
    KEY idx_audit_action (action, created_at),
    KEY idx_audit_target (target_type, target_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='백오피스 감사 로그';

-- ────────────────────────────────────────────────────────────────────────
-- 12) bo_approval_queue — 백오피스 4-eyes 승인 큐 (의존 0)
--     출처: db/migrations/20260604_bo_audit_log.sql:35-56
--     expires_at = requested_at + 24h (앱에서 계산해 INSERT).
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS bo_approval_queue (
    approval_id      bigint       NOT NULL AUTO_INCREMENT,
    requester_id     char(36)     NOT NULL,
    requester_email  varchar(255) NOT NULL,
    action           varchar(64)  NOT NULL,
    target_type      varchar(40)  NULL DEFAULT NULL,
    target_id        varchar(64)  NULL DEFAULT NULL,
    payload_json     text         NOT NULL,
    status           varchar(20)  NOT NULL DEFAULT 'pending',
    approver_id      char(36)     NULL DEFAULT NULL,
    approver_email   varchar(255) NULL DEFAULT NULL,
    rejection_reason varchar(500) NULL DEFAULT NULL,
    requested_at     datetime(6)  NOT NULL DEFAULT current_timestamp(6),
    decided_at       datetime(6)  NULL DEFAULT NULL,
    executed_at      datetime(6)  NULL DEFAULT NULL,
    expires_at       datetime(6)  NOT NULL,
    PRIMARY KEY (approval_id),
    KEY idx_approval_status (status, expires_at),
    KEY idx_approval_requester (requester_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='백오피스 4-eyes 승인 큐';

-- ────────────────────────────────────────────────────────────────────────
-- 13) tenant_domains — 고객사 도메인/터널 (의존 0, tenant_id FK 미선언 — 원본 유지)
--     출처: db/migrations/20260605_tenant_domains.sql:9-25
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS tenant_domains (
    domain_id     bigint unsigned NOT NULL AUTO_INCREMENT,
    tenant_id     char(36)     NOT NULL,
    domain        varchar(255) NOT NULL,
    cf_zone_id    varchar(64)  NULL DEFAULT NULL,
    cf_record_id  varchar(64)  NULL DEFAULT NULL,
    cf_tunnel_id  varchar(64)  NULL DEFAULT NULL,
    status        varchar(20)  NOT NULL DEFAULT 'pending',
    issued_at     datetime(6)  NULL DEFAULT NULL,
    last_error    varchar(500) NULL DEFAULT NULL,
    created_at    datetime(6)  NOT NULL DEFAULT current_timestamp(6),
    updated_at    datetime(6)  NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
    PRIMARY KEY (domain_id),
    UNIQUE KEY uk_domain (domain),
    KEY ix_tenant (tenant_id),
    KEY ix_status (status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='고객사 도메인/터널';

-- ────────────────────────────────────────────────────────────────────────
-- 14) tenant_etax_settings — 전자세금계산서 발행 스케줄 (의존 0)
--     출처: tmp-build/backoffice-schema.sql:214-228
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS tenant_etax_settings (
    tenant_id          varchar(36) NOT NULL,
    issue_mode         varchar(20) NOT NULL DEFAULT 'scheduled',
    batch_time         time        NOT NULL DEFAULT '18:00:00',
    batch_weekdays     varchar(20) NOT NULL DEFAULT '1,2,3,4,5',
    exclude_holidays   tinyint(1)  NOT NULL DEFAULT 1,
    notify_before_min  int(11)     NULL DEFAULT 30,
    notify_manager_id  varchar(36) NULL DEFAULT NULL,
    retry_count        int(11)     NOT NULL DEFAULT 3,
    retry_interval_min int(11)     NOT NULL DEFAULT 60,
    created_at         datetime(6) NOT NULL DEFAULT current_timestamp(6),
    updated_at         datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
    updated_by         varchar(36) NULL DEFAULT NULL,
    PRIMARY KEY (tenant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='전자세금계산서 발행 스케줄';

-- ────────────────────────────────────────────────────────────────────────
-- 15) promotions — 프로모션 (PromotionsAdminController 사용 정의: VARCHAR PK)
--     출처: tmp-build/pricing-schema.sql:30-52
--     ※ 주의: PromotionController.cs는 별도 정의(promo_code/title, BIGINT PK)를 쓴다.
--       두 컨트롤러 통합은 별건 todo(사장님 결재 2026-06-18). 그 정의는 promotions_legacy로 분리(아래 21).
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS promotions (
    promotion_id    varchar(36)   NOT NULL,
    promotion_code  varchar(40)   NOT NULL,
    promotion_name  varchar(100)  NOT NULL,
    description     text          NULL DEFAULT NULL,
    discount_type   varchar(20)   NOT NULL DEFAULT 'percent',
    discount_value  decimal(10,2) NOT NULL,
    target_plan_id  varchar(20)   NULL DEFAULT NULL,
    target_scope    varchar(20)   NOT NULL DEFAULT 'all',
    target_filter   text          NULL DEFAULT NULL,
    starts_at       datetime      NOT NULL,
    ends_at         datetime      NOT NULL,
    max_uses        int(11)       NOT NULL DEFAULT 0,
    used_count      int(11)       NOT NULL DEFAULT 0,
    is_active       tinyint(1)    NOT NULL DEFAULT 1,
    created_by      varchar(36)   NOT NULL,
    created_at      datetime(6)   NOT NULL DEFAULT current_timestamp(6),
    updated_at      datetime(6)   NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
    PRIMARY KEY (promotion_id),
    UNIQUE KEY uk_promotion_code (promotion_code),
    KEY ix_promotions_active (is_active, starts_at, ends_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='프로모션 (Admin: 가격관리 세트)';

-- ────────────────────────────────────────────────────────────────────────
-- 16) pricing_plans — 요금제 (의존 0)
--     출처: tmp-build/pricing-schema.sql:9-25
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS pricing_plans (
    plan_id          varchar(20)   NOT NULL,
    plan_name        varchar(50)   NOT NULL,
    description      text          NULL DEFAULT NULL,
    monthly_price    decimal(10,0) NOT NULL DEFAULT 0,
    yearly_price     decimal(10,0) NOT NULL DEFAULT 0,
    max_users        int(11)       NOT NULL DEFAULT 3,
    max_devices      int(11)       NOT NULL DEFAULT 3,
    ai_token_monthly int(11)       NOT NULL DEFAULT 0,
    features_json    text          NULL DEFAULT NULL,
    is_active        tinyint(1)    NOT NULL DEFAULT 1,
    is_visible       tinyint(1)    NOT NULL DEFAULT 1,
    display_order    int(11)       NOT NULL DEFAULT 0,
    created_at       datetime(6)   NOT NULL DEFAULT current_timestamp(6),
    updated_at       datetime(6)   NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
    PRIMARY KEY (plan_id),
    KEY ix_active_visible (is_active, is_visible)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='요금제';

-- ────────────────────────────────────────────────────────────────────────
-- 17) pricing_history — 가격/프로모션 변경 이력 (의존 0)
--     출처: tmp-build/pricing-schema.sql:58-71
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS pricing_history (
    history_id      bigint       NOT NULL AUTO_INCREMENT,
    entity_type     varchar(20)  NOT NULL,
    entity_id       varchar(36)  NOT NULL,
    action          varchar(20)  NOT NULL,
    before_json     text         NULL DEFAULT NULL,
    after_json      text         NULL DEFAULT NULL,
    reason          varchar(500) NULL DEFAULT NULL,
    changed_by      varchar(36)  NOT NULL,
    changed_by_name varchar(50)  NULL DEFAULT NULL,
    changed_at      datetime(6)  NOT NULL DEFAULT current_timestamp(6),
    PRIMARY KEY (history_id),
    KEY ix_entity (entity_type, entity_id, changed_at),
    KEY ix_changed_by (changed_by, changed_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='가격/프로모션 변경 이력';

-- ────────────────────────────────────────────────────────────────────────
-- 18) tenant_rewards — 고객사 리워드 (의존 0)
--     출처: tmp-build/pricing-schema.sql:77-90
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS tenant_rewards (
    reward_id    bigint        NOT NULL AUTO_INCREMENT,
    tenant_id    varchar(36)   NOT NULL,
    reward_type  varchar(20)   NOT NULL,
    reward_value decimal(10,2) NOT NULL,
    reason       varchar(500)  NULL DEFAULT NULL,
    expires_at   datetime      NULL DEFAULT NULL,
    is_consumed  tinyint(1)    NOT NULL DEFAULT 0,
    consumed_at  datetime      NULL DEFAULT NULL,
    granted_by   varchar(36)   NOT NULL,
    granted_at   datetime(6)   NOT NULL DEFAULT current_timestamp(6),
    PRIMARY KEY (reward_id),
    KEY ix_tenant (tenant_id, is_consumed),
    KEY ix_granted_by (granted_by, granted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='고객사 리워드';

-- ────────────────────────────────────────────────────────────────────────
-- 19) serial_verify_attempts — 시리얼 검증 시도 (INSERT ONLY, 의존 0)
--     출처: tmp-build/serial-verify-schema.sql:7-18
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS serial_verify_attempts (
    attempt_id         bigint       NOT NULL AUTO_INCREMENT,
    tenant_id          varchar(36)  NULL DEFAULT NULL,
    submitted_hash     varchar(64)  NOT NULL,
    client_ip          varchar(45)  NULL DEFAULT NULL,
    client_fingerprint varchar(128) NULL DEFAULT NULL,
    result             varchar(20)  NOT NULL,
    attempted_at       datetime(6)  NOT NULL DEFAULT current_timestamp(6),
    PRIMARY KEY (attempt_id),
    KEY ix_tenant_attempted (tenant_id, attempted_at),
    KEY ix_fingerprint_attempted (client_fingerprint, attempted_at),
    KEY ix_result (result, attempted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='시리얼 검증 시도';

-- ────────────────────────────────────────────────────────────────────────
-- 20) serial_verify_locks — 시리얼 검증 잠금 (의존 0)
--     출처: tmp-build/serial-verify-schema.sql:22-35
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS serial_verify_locks (
    lock_id            bigint       NOT NULL AUTO_INCREMENT,
    client_fingerprint varchar(128) NOT NULL,
    failed_count       int(11)      NOT NULL DEFAULT 0,
    first_failed_at    datetime(6)  NOT NULL,
    last_failed_at     datetime(6)  NOT NULL,
    is_locked          tinyint(1)   NOT NULL DEFAULT 0,
    locked_at          datetime     NULL DEFAULT NULL,
    unlocked_at        datetime     NULL DEFAULT NULL,
    unlocked_by        varchar(36)  NULL DEFAULT NULL,
    unlock_reason      varchar(500) NULL DEFAULT NULL,
    PRIMARY KEY (lock_id),
    UNIQUE KEY uk_fingerprint (client_fingerprint),
    KEY ix_locked (is_locked, last_failed_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='시리얼 검증 잠금';

-- ────────────────────────────────────────────────────────────────────────
-- 21) promotions_legacy + promotion_usages — PromotionController 사용 정의
--     출처: db/migrations/20260605_promotions.sql:12-50
--     ※ promotions(15번)와 컬럼·PK가 다른 별도 정의. 두 컨트롤러 통합 전까지 공존.
--       (사장님 결재 2026-06-18: 통합은 별건 todo. 지금은 충돌 없이 공존.)
--     ⚠️ PromotionController.cs는 현재 테이블명 'promotions'를 쓴다 → 통합 todo에서 이 컨트롤러를
--        promotions_legacy로 전환하거나 Admin 정의로 일원화해야 함. 본 스키마는 양쪽 정의를 보존만.
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS promotions_legacy (
    promotion_id   bigint unsigned NOT NULL AUTO_INCREMENT,
    promo_code     varchar(40)   NOT NULL,
    title          varchar(200)  NOT NULL,
    discount_type  varchar(20)   NOT NULL DEFAULT 'percent',
    discount_value decimal(10,2) NOT NULL,
    starts_at      datetime(6)   NOT NULL,
    ends_at        datetime(6)   NOT NULL,
    max_uses       int(11)       NOT NULL DEFAULT 0,
    use_count      int(11)       NOT NULL DEFAULT 0,
    target_plan    varchar(40)   NULL DEFAULT NULL,
    status         varchar(20)   NOT NULL DEFAULT 'active',
    created_by     varchar(36)   NULL DEFAULT NULL,
    created_at     datetime(6)   NOT NULL DEFAULT current_timestamp(6),
    updated_at     datetime(6)   NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
    PRIMARY KEY (promotion_id),
    UNIQUE KEY uk_promo_code (promo_code),
    KEY ix_status_period (status, starts_at, ends_at),
    KEY ix_target_plan (target_plan)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='프로모션(legacy, PromotionController). 통합 별건 todo';

CREATE TABLE IF NOT EXISTS promotion_usages (
    usage_id       bigint unsigned NOT NULL AUTO_INCREMENT,
    promotion_id   bigint unsigned NOT NULL,
    promo_code     varchar(40)   NOT NULL,
    tenant_id      char(36)      NULL DEFAULT NULL,
    signup_token   varchar(64)   NULL DEFAULT NULL,
    applied_amount decimal(14,2) NOT NULL DEFAULT 0.00,
    applied_at     datetime(6)   NOT NULL DEFAULT current_timestamp(6),
    source         varchar(40)   NOT NULL DEFAULT 'landing_signup',
    PRIMARY KEY (usage_id),
    KEY ix_promotion (promotion_id),
    KEY ix_tenant (tenant_id),
    KEY ix_signup_token (signup_token),
    KEY ix_applied_at (applied_at),
    CONSTRAINT fk_usage_promo FOREIGN KEY (promotion_id)
        REFERENCES promotions_legacy (promotion_id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='프로모션 사용 이력';

-- ────────────────────────────────────────────────────────────────────────
-- 22) webhook_outbox — ERP 동기화 웹훅 아웃박스 (의존 0)
--     출처: db/migrations/20260604_backoffice_webhook_outbox.sql:13-39
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS webhook_outbox (
    outbox_id     bigint       NOT NULL AUTO_INCREMENT,
    tenant_id     char(36)     NOT NULL,
    event_type    varchar(40)  NOT NULL,
    target_url    varchar(255) NOT NULL,
    payload_json  text         NOT NULL,
    signature     varchar(128) NOT NULL,
    nonce         varchar(36)  NOT NULL,
    status        varchar(20)  NOT NULL DEFAULT 'pending',
    retry_count   int(11)      NOT NULL DEFAULT 0,
    last_error    varchar(500) NULL DEFAULT NULL,
    next_retry_at datetime(6)  NULL DEFAULT NULL,
    created_at    datetime(6)  NOT NULL DEFAULT current_timestamp(6),
    sent_at       datetime(6)  NULL DEFAULT NULL,
    PRIMARY KEY (outbox_id),
    UNIQUE KEY uk_outbox_nonce (nonce),
    KEY idx_outbox_status_next (status, next_retry_at),
    KEY idx_outbox_tenant (tenant_id, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='ERP 동기화 웹훅 아웃박스';

-- ────────────────────────────────────────────────────────────────────────
-- 23) beta_signups — 베타 신청 (의존 0)
--     출처: installer/hitpan_db.sql.bak-137-full-20260618-140509:725-737
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS beta_signups (
    signup_id    varchar(36)  NOT NULL,
    company_name varchar(100) NOT NULL,
    contact_name varchar(50)  NOT NULL,
    phone        varchar(20)  NOT NULL,
    email        varchar(100) NOT NULL,
    employee_cnt varchar(20)  NULL DEFAULT NULL,
    message      text         NULL DEFAULT NULL,
    status       varchar(20)  NOT NULL DEFAULT 'pending',
    created_at   datetime(6)  NOT NULL DEFAULT current_timestamp(6),
    ip_address   varchar(45)  NULL DEFAULT NULL,
    PRIMARY KEY (signup_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='베타 신청';

-- ────────────────────────────────────────────────────────────────────────
-- 24) reseller_serials — 대리점 시리얼 풀 (→ resellers)
--     출처: db/migrations/20260604_reseller_settlement.sql:44-66
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS reseller_serials (
    serial_id         bigint           NOT NULL AUTO_INCREMENT,
    serial_code       varchar(40)      NOT NULL,
    serial_hash       varchar(255)     NOT NULL,
    reseller_id       char(36)         NOT NULL,
    issued_by         char(36)         NOT NULL,
    subscription_tier varchar(20)      NOT NULL DEFAULT 'basic',
    max_users         tinyint unsigned NOT NULL DEFAULT 3,
    valid_until       datetime         NULL DEFAULT NULL,
    status            varchar(20)      NOT NULL DEFAULT 'available',
    claimed_tenant_id char(36)         NULL DEFAULT NULL,
    claimed_at        datetime(6)      NULL DEFAULT NULL,
    revoked_at        datetime(6)      NULL DEFAULT NULL,
    revoked_reason    varchar(255)     NULL DEFAULT NULL,
    issued_at         datetime(6)      NOT NULL DEFAULT current_timestamp(6),
    PRIMARY KEY (serial_id),
    UNIQUE KEY uq_serial_code (serial_code),
    KEY idx_serial_reseller (reseller_id, status),
    KEY idx_serial_status (status),
    CONSTRAINT fk_serial_reseller FOREIGN KEY (reseller_id)
        REFERENCES resellers (reseller_id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='대리점 시리얼 풀';

-- ────────────────────────────────────────────────────────────────────────
-- 25) reseller_settlements — 대리점 정산 (FK 부모, → resellers)
--     출처: db/migrations/20260604_reseller_settlement.sql:16-39
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS reseller_settlements (
    settlement_id     bigint        NOT NULL AUTO_INCREMENT,
    reseller_id       char(36)      NOT NULL,
    settlement_month  char(7)       NOT NULL,
    tenant_count      int(11)       NOT NULL DEFAULT 0,
    gross_amount      decimal(14,2) NOT NULL DEFAULT 0,
    commission_rate   decimal(5,4)  NOT NULL DEFAULT 0,
    commission_amount decimal(14,2) NOT NULL DEFAULT 0,
    incentive_amount  decimal(14,2) NOT NULL DEFAULT 0,
    total_payable     decimal(14,2) NOT NULL DEFAULT 0,
    status            varchar(20)   NOT NULL DEFAULT 'draft',
    confirmed_by      char(36)      NULL DEFAULT NULL,
    confirmed_at      datetime(6)   NULL DEFAULT NULL,
    paid_at           datetime(6)   NULL DEFAULT NULL,
    memo              varchar(500)  NULL DEFAULT NULL,
    created_at        datetime(6)   NOT NULL DEFAULT current_timestamp(6),
    updated_at        datetime(6)   NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
    PRIMARY KEY (settlement_id),
    UNIQUE KEY uq_reseller_month (reseller_id, settlement_month),
    KEY idx_settlement_month (settlement_month, status),
    CONSTRAINT fk_settlement_reseller FOREIGN KEY (reseller_id)
        REFERENCES resellers (reseller_id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='대리점 정산';

-- ────────────────────────────────────────────────────────────────────────
-- 26) reseller_settlement_lines — 대리점 정산 상세 (→ reseller_settlements)
--     출처: db/migrations/20260604_reseller_settlement.sql:71-84
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS reseller_settlement_lines (
    line_id           bigint        NOT NULL AUTO_INCREMENT,
    settlement_id     bigint        NOT NULL,
    tenant_id         char(36)      NOT NULL,
    tenant_code       varchar(40)   NOT NULL,
    company_name      varchar(255)  NOT NULL,
    payment_amount    decimal(14,2) NOT NULL DEFAULT 0,
    commission_amount decimal(14,2) NOT NULL DEFAULT 0,
    created_at        datetime(6)   NOT NULL DEFAULT current_timestamp(6),
    PRIMARY KEY (line_id),
    KEY idx_settlement_lines_settlement (settlement_id),
    CONSTRAINT fk_lines_settlement FOREIGN KEY (settlement_id)
        REFERENCES reseller_settlements (settlement_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='대리점 정산 상세';

-- ────────────────────────────────────────────────────────────────────────
-- 27) tenant_devices — 등록 기기 (→ tenants. ★users FK 제거: users는 ERP 테이블)
--     출처: tmp-build/backoffice-schema.sql:131-153 + device-token-cols.sql ALTER 3컬럼
--     ★ 원본의 fk_device_user → users(ERP 테이블) 제거 — 자립형 필수(헌법 #35).
--       user_id는 컬럼만 유지(FK 없이), 백오피스엔 users 없음.
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS tenant_devices (
    device_id         varchar(36)  NOT NULL,
    tenant_id         varchar(36)  NOT NULL,
    user_id           varchar(36)  NULL DEFAULT NULL,
    device_type       varchar(10)  NOT NULL,
    device_name       varchar(100) NULL DEFAULT NULL,
    fingerprint       varchar(64)  NOT NULL,
    device_token_hash varchar(128) NULL DEFAULT NULL,
    issued_at         datetime     NULL DEFAULT NULL,
    ip_address        varchar(50)  NULL DEFAULT NULL,
    user_agent        varchar(500) NULL DEFAULT NULL,
    os_info           varchar(200) NULL DEFAULT NULL,
    status            varchar(20)  NOT NULL DEFAULT 'approved',
    registered_at     datetime(6)  NOT NULL DEFAULT current_timestamp(6),
    approved_by       varchar(36)  NULL DEFAULT NULL,
    approved_at       datetime(6)  NULL DEFAULT NULL,
    last_seen_at      datetime(6)  NULL DEFAULT NULL,
    revoked_at        datetime(6)  NULL DEFAULT NULL,
    revoked_reason    varchar(200) NULL DEFAULT NULL,
    PRIMARY KEY (device_id),
    UNIQUE KEY uq_tenant_fp (tenant_id, fingerprint),
    KEY idx_tenant_type_status (tenant_id, device_type, status),
    KEY idx_user (user_id),
    KEY ix_device_token_hash (device_token_hash),
    CONSTRAINT fk_device_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenants (tenant_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='등록 기기 (users FK 제거: 자립형)';

SET FOREIGN_KEY_CHECKS=1;

-- ============================================================================
-- 제외 명시 (헌법 #22 — 백오피스 미보유):
--   - tenant_certificates : 인증서 PFX(민감) → ERP 로컬 전용
--   - tenant_settings     : ERP 업무설정 → ERP 로컬 전용
--   - tenants의 민감 18컬럼 + license_key_plain : ERP 로컬(local_company)에만
-- 별건 todo (사장님 결재 2026-06-18):
--   - promotions / promotions_legacy 두 컨트롤러 통합 → 별도 작업으로 일원화
-- ============================================================================
