-- bo_permissions — 백오피스 계층 권한 수동 설정 (사장님 결재 2026-06-04, 헌법 #11·#35)
--
-- 사장님이 백오피스 UI(/owner/permissions)에서 각 기능별 허용 역할(role)을 직접 박제.
-- 코드에 박힌 고정 정책 대신 DB 박제 + 60초 메모리 캐시.
--
-- allowed_roles: CSV (예: 'owner,platform_admin,platform_manager')
--   허용 role 후보: owner, platform_owner, platform_admin, platform_manager, platform_staff

CREATE TABLE IF NOT EXISTS bo_permissions (
    permission_key  VARCHAR(80)  NOT NULL PRIMARY KEY,
    display_name    VARCHAR(100) NOT NULL,
    description     TEXT NULL,
    category        VARCHAR(40)  NOT NULL,
    allowed_roles   VARCHAR(200) NOT NULL,
    updated_at      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by      CHAR(36)     NULL,
    INDEX idx_category (category)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 시드 데이터 — 1차 박제 권한 (협력업체 관리 4건)
-- 기본값: owner + platform_admin + platform_owner (사장님이 UI에서 자유 변경 가능)
INSERT IGNORE INTO bo_permissions (permission_key, display_name, description, category, allowed_roles) VALUES
('reseller-applications.list',
 '협력업체 신청 목록 조회',
 '협력업체 가입 신청 목록을 조회할 수 있는 권한',
 'reseller-mgmt',
 'owner,platform_owner,platform_admin'),

('reseller-applications.detail',
 '협력업체 신청 상세 조회',
 '협력업체 가입 신청의 상세 정보를 조회할 수 있는 권한',
 'reseller-mgmt',
 'owner,platform_owner,platform_admin'),

('reseller-applications.approve',
 '협력업체 신청 승인',
 '협력업체 가입 신청을 승인하고 reseller 계정을 발급할 수 있는 권한',
 'reseller-mgmt',
 'owner,platform_owner,platform_admin'),

('reseller-applications.reject',
 '협력업체 신청 반려',
 '협력업체 가입 신청을 반려할 수 있는 권한',
 'reseller-mgmt',
 'owner,platform_owner,platform_admin');
