-- ============================================================================
-- 히트판 백오피스(hitpan_backoffice) 자립형 통합 스키마 — 권한 시드 (bo_permissions)
-- ============================================================================
-- 작성: PM 브라운킴 (사장님 결재 2026-06-19 "시드 SQL 파일 신설")
--
-- ★ 왜 이 파일이 필요한가 (2026-06-19 사고 교훈):
--   - 자립형 스키마(00_core.sql)는 bo_permissions "테이블 구조"만 만들고 데이터는 0건.
--   - DB를 통째로 새로 깔면 권한 행이 없어 모든 [BoPermission] 메뉴가 403.
--   - 2026-06-19 NCP 배포 직후 협력업체 목록 403 발생 → 손으로 INSERT해 봉합한 것을
--     이 파일로 정식화 → 앞으로 DB 새로 깔아도 SchemaMigrator가 자동 복구.
--
-- ★ role 어휘 (2026-06-19 사장님 결재):
--   - platform_admins.role enum = super_admin/billing_admin/cs_admin/readonly.
--   - 기존 6/4 시드는 allowed_roles에 super_admin이 없어 master(super_admin)가 막혔음.
--   - → 모든 권한의 allowed_roles에 super_admin 포함. (role 어휘 완전통일은 별건 계정통합 때)
--   - tenants.suspend(정지)만 owner/platform_owner/super_admin 으로 제한(민감 액션).
--
-- ★ 멱등: INSERT IGNORE (PK=permission_key). 여러 번 실행해도 안전. 기존 행 보존.
--   - 사장님이 /owner/permissions UI에서 allowed_roles를 바꾼 뒤에도 이 시드가 덮지 않음
--     (INSERT IGNORE는 이미 있는 키를 건드리지 않음).
--
-- 권한 출처: 백오피스 전 컨트롤러의 [BoPermission("키")] 전수 grep (2026-06-19, 추측 0).
--   고유 키 24개. display_name·category는 각 컨트롤러 헤더 주석 기준.
-- ============================================================================

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

INSERT IGNORE INTO bo_permissions (permission_key, display_name, description, category, allowed_roles) VALUES
-- ── 고객사(tenants) 관리 — TenantsAdminController ──────────────────────────
('tenants.list',     '고객사 목록 조회',   '전체 고객사 목록을 조회',                   'tenant-mgmt',   'owner,platform_owner,platform_admin,super_admin'),
('tenants.detail',   '고객사 상세 조회',   '고객사 가입·결제·라이선스 정보 조회',        'tenant-mgmt',   'owner,platform_owner,platform_admin,super_admin'),
('tenants.suspend',  '고객사 일시 정지',   '결제 누락·약관 위반 등으로 고객사 일시 정지', 'tenant-mgmt',   'owner,platform_owner,super_admin'),
('tenants.activate', '고객사 활성화 복구', '일시 정지된 고객사를 활성화로 복구',          'tenant-mgmt',   'owner,platform_owner,platform_admin,super_admin'),

-- ── 협력업체 신청(reseller-applications) — ResellerApplicationsAdminController ─
('reseller-applications.list',    '협력업체 신청 목록 조회', '협력업체 가입 신청 목록 조회',           'reseller-mgmt', 'owner,platform_owner,platform_admin,super_admin'),
('reseller-applications.detail',  '협력업체 신청 상세 조회', '협력업체 가입 신청 상세 조회',           'reseller-mgmt', 'owner,platform_owner,platform_admin,super_admin'),
('reseller-applications.approve', '협력업체 신청 승인',     '협력업체 신청 승인 및 reseller 계정 발급', 'reseller-mgmt', 'owner,platform_owner,platform_admin,super_admin'),
('reseller-applications.reject',  '협력업체 신청 반려',     '협력업체 가입 신청 반려',                 'reseller-mgmt', 'owner,platform_owner,platform_admin,super_admin'),

-- ── 협력업체(resellers) 운영 관리 — ResellersAdminController ────────────────
('resellers.list',   '협력업체 목록 조회', '활성·정지된 협력업체 목록 조회',             'reseller-mgmt', 'owner,platform_owner,platform_admin,super_admin'),
('resellers.detail', '협력업체 상세 조회', '협력업체 기본·연락처·정산 정보 조회',        'reseller-mgmt', 'owner,platform_owner,platform_admin,super_admin'),

-- ── 대리점 시리얼(reseller_serial) — ResellerSerialController ───────────────
('reseller_serial.list',   '대리점 시리얼 목록 조회', '대리점별 발급 시리얼 목록 조회',         'reseller-serial', 'owner,platform_owner,platform_admin,super_admin'),
('reseller_serial.issue',  '대리점 시리얼 발급',     '대리점에 시리얼 N개 발급(평문 1회 응답)', 'reseller-serial', 'owner,platform_owner,super_admin'),
('reseller_serial.revoke', '대리점 시리얼 회수',     '미사용(available) 시리얼 회수',           'reseller-serial', 'owner,platform_owner,super_admin'),

-- ── 대리점 정산(reseller_settlement) — ResellerSettlementController ─────────
('reseller_settlement.list',      '대리점 정산 목록 조회', '월별 대리점 정산 목록 조회',          'reseller-settlement', 'owner,platform_owner,platform_admin,super_admin'),
('reseller_settlement.detail',    '대리점 정산 상세 조회', '정산 상세 + 라인 조회',               'reseller-settlement', 'owner,platform_owner,platform_admin,super_admin'),
('reseller_settlement.calculate', '대리점 정산 산출',     '월별 정산 산출(draft 생성)',          'reseller-settlement', 'owner,platform_owner,super_admin'),
('reseller_settlement.confirm',   '대리점 정산 확정',     '정산 확정(draft→confirmed)',          'reseller-settlement', 'owner,platform_owner,super_admin'),
('reseller_settlement.paid',      '대리점 정산 송금완료', '송금 완료 표시(confirmed→paid)',       'reseller-settlement', 'owner,platform_owner,super_admin'),

-- ── 도메인 발급(tenant_domain) — TenantDomainController ─────────────────────
('tenant_domain.list',   '도메인 발급 이력 조회', '고객사 도메인 발급 이력 조회',            'tenant-domain', 'owner,platform_owner,platform_admin,super_admin'),
('tenant_domain.issue',  '도메인 발급',          'DNS CNAME 생성 + 도메인 발급',            'tenant-domain', 'owner,platform_owner,super_admin'),
('tenant_domain.revoke', '도메인 회수',          'DNS 회수 + status=revoked',               'tenant-domain', 'owner,platform_owner,super_admin'),

-- ── 프로모션(promotion) — PromotionController ──────────────────────────────
('promotion.list',   '프로모션 목록 조회', '프로모션 목록 조회(상태·기간 필터)',         'promotion', 'owner,platform_owner,platform_admin,super_admin'),
('promotion.create', '프로모션 생성',     '프로모션 생성(코드 UNIQUE)',                'promotion', 'owner,platform_owner,super_admin'),
('promotion.toggle', '프로모션 활성토글', '프로모션 활성/비활성 토글',                  'promotion', 'owner,platform_owner,super_admin'),
('promotion.usage',  '프로모션 사용이력', '프로모션 사용 이력 조회',                    'promotion', 'owner,platform_owner,platform_admin,super_admin'),

-- ── Owner 영역 bo_users CRUD — OwnerBoUsersController (4-eyes) ──────────────
('owner.bo_users.list',   'BO계정 목록 조회', '백오피스 운영 계정 목록 조회',             'owner', 'owner,platform_owner,super_admin'),
('owner.bo_users.create', 'BO계정 생성',     '백오피스 운영 계정 생성(4-eyes)',          'owner', 'owner,platform_owner,super_admin'),
('owner.bo_users.update', 'BO계정 수정',     '백오피스 운영 계정 수정(role 변경 4-eyes)', 'owner', 'owner,platform_owner,super_admin'),
('owner.bo_users.delete', 'BO계정 삭제',     '백오피스 운영 계정 삭제(4-eyes)',          'owner', 'owner,platform_owner,super_admin'),

-- ── Owner 영역 4-eyes 승인 큐 — OwnerApprovalController ─────────────────────
('owner.approvals.list',   '승인 큐 목록 조회', '4-eyes 승인 대기 목록 조회',              'owner', 'owner,platform_owner,super_admin'),
('owner.approvals.decide', '승인/반려 결정',   '4-eyes 승인 또는 반려 결정',              'owner', 'owner,platform_owner,super_admin'),

-- ── 웹훅 outbox 관리 — WebhookAdminController ──────────────────────────────
('webhook.admin', '웹훅 관리', '웹훅 outbox 조회/재발송', 'webhook', 'owner,platform_owner,super_admin');
