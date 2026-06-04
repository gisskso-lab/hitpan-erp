-- bo_permissions 시드 추가 — tenant-mgmt + reseller-mgmt 확장 (사장님 결재 2026-06-04)
--
-- 기존 시드(reseller-applications.*) 위에 추가.
-- 기본 허용 역할: owner + platform_owner + platform_admin (사장님이 UI에서 변경 가능)

INSERT IGNORE INTO bo_permissions (permission_key, display_name, description, category, allowed_roles) VALUES
-- 고객사(tenants) 관리
('tenants.list',
 '고객사 목록 조회',
 '전체 고객사(tenants) 목록을 조회할 수 있는 권한',
 'tenant-mgmt',
 'owner,platform_owner,platform_admin'),

('tenants.detail',
 '고객사 상세 조회',
 '고객사의 가입 정보·결제 상태·라이선스 정보를 조회할 수 있는 권한',
 'tenant-mgmt',
 'owner,platform_owner,platform_admin'),

('tenants.suspend',
 '고객사 일시 정지',
 '결제 누락·약관 위반 등으로 고객사를 일시 정지(suspended)할 수 있는 권한',
 'tenant-mgmt',
 'owner,platform_owner'),

('tenants.activate',
 '고객사 활성화 복구',
 '일시 정지된 고객사를 활성화(active)로 복구할 수 있는 권한',
 'tenant-mgmt',
 'owner,platform_owner,platform_admin'),

-- 협력업체(resellers) 관리 — 신청 검토는 별도, 운영 관리만
('resellers.list',
 '협력업체 목록 조회',
 '활성·정지된 협력업체(resellers) 목록을 조회할 수 있는 권한',
 'reseller-mgmt',
 'owner,platform_owner,platform_admin'),

('resellers.detail',
 '협력업체 상세 조회',
 '협력업체의 기본 정보·연락처·정산 정보를 조회할 수 있는 권한',
 'reseller-mgmt',
 'owner,platform_owner,platform_admin');
