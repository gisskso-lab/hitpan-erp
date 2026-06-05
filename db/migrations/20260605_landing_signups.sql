-- landing_signups 신규 DDL (2026-06-05 본 PM 신규 작성)
-- 사유: 어제 W1+W2+W5 차수에서 landing_signups 생성 SQL 자체가 누락됨.
-- backoffice_db_split.sql이 hitpan_erp.landing_signups 존재를 전제로 작성됨.
-- 본 SQL을 db_split 이전에 실행해야 함.
--
-- 헌법 정합:
--   #17 ENGINE=InnoDB 명시
--   #18·#22 biz_no 평문 0 (biz_no_hash만 저장)
--   #20 가입 워크플로우 끊김 0
--   #35 백오피스 영역 (db_split 후 hitpan_backoffice로 이관)

CREATE TABLE IF NOT EXISTS landing_signups (
    signup_id      BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    signup_token   VARCHAR(64)     NOT NULL,
    biz_no_hash    VARCHAR(128)    NOT NULL,
    company_name   VARCHAR(120)    NOT NULL,
    email          VARCHAR(100)    NOT NULL,
    phone          VARCHAR(30)     NOT NULL,
    plan_type      VARCHAR(20)     NOT NULL DEFAULT 'basic',
    reseller_code  VARCHAR(20)     NULL,
    agree_terms    TINYINT(1)      NOT NULL DEFAULT 0,
    agree_privacy  TINYINT(1)      NOT NULL DEFAULT 0,
    status         VARCHAR(20)     NOT NULL DEFAULT 'submitted',
    submitted_at   DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (signup_id),
    UNIQUE KEY uk_signup_token (signup_token),
    KEY ix_biz_no_hash (biz_no_hash),
    KEY ix_email (email),
    KEY ix_status (status),
    KEY ix_submitted_at (submitted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
