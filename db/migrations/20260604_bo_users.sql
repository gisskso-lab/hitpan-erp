-- 백오피스 자체 인증 테이블 (사장님 결재 2026-06-04, 헌법 #35 객체 완전 분리)
--
-- 헌법 정합:
--   #18·#22 — 비밀번호 BCrypt 박제, 평문 0건
--   #35 — 백오피스 자체 인증. ERP API 의존 0건
--   #17 — ENGINE=InnoDB 명시
--
-- 박제 위치: hitpan_backoffice DB (W1 분리 후)
-- 본 SQL 실행 전 20260604_backoffice_db_split.sql 먼저 실행 필요.

USE hitpan_backoffice;

CREATE TABLE IF NOT EXISTS bo_users (
    user_id          CHAR(36)        NOT NULL PRIMARY KEY,
    email            VARCHAR(255)    NOT NULL,
    password_hash    VARCHAR(255)    NOT NULL COMMENT 'BCrypt 박제, 평문 0건',
    name             VARCHAR(100)    NOT NULL,
    role             VARCHAR(32)     NOT NULL COMMENT 'owner / admin / reseller',
    reseller_id      CHAR(36)        NULL COMMENT 'role=reseller일 때만, resellers.reseller_id FK',
    is_active        TINYINT(1)      NOT NULL DEFAULT 1,
    failed_login_cnt INT             NOT NULL DEFAULT 0,
    last_login_at    DATETIME        NULL,
    created_at       DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at       DATETIME        NULL ON UPDATE CURRENT_TIMESTAMP,

    UNIQUE KEY uq_bo_users_email (email),
    KEY idx_bo_users_role (role, is_active),
    KEY idx_bo_users_reseller (reseller_id),

    CONSTRAINT fk_bo_users_reseller FOREIGN KEY (reseller_id)
        REFERENCES resellers(reseller_id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ────────────────────────────────────────
-- 사장님(Owner) 초기 시드 — 비밀번호는 사장님이 직접 BCrypt 박제 후 INSERT
-- ────────────────────────────────────────
-- INSERT INTO bo_users (user_id, email, password_hash, name, role, is_active, created_at)
-- VALUES (
--   UUID(),
--   'gisskso@gmail.com',
--   '$2a$11$여기에-BCrypt-해시-박제',
--   '사장님',
--   'owner',
--   1,
--   NOW()
-- );
--
-- BCrypt 해시 생성 방법 (사장님 PC PowerShell):
--   1) 임시 .NET 콘솔 또는 ERP API의 BCrypt.Net.BCrypt.HashPassword("비밀번호") 사용
--   2) 또는 https://bcrypt-generator.com (운영 비밀번호는 절대 외부 사이트 사용 금지, 로컬에서만)
