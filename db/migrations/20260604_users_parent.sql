-- users 부모계정 플래그 (사장님 결재 2026-06-04, 헌법 #35)
--
-- 헌법 #35 정합:
--   - 최초 가입 시 부모계정 1명 자동 부여
--   - 자식계정은 부모계정만 생성·삭제·권한 변경 가능
--   - 부모는 tenant당 1명만
--
-- 단순 2계층 (사장님 결재):
--   is_parent=1 → 부모 (모든 권한, ERP 마스터)
--   is_parent=0 → 자식 (기존 role/권한체계 따름)

ALTER TABLE users
  ADD COLUMN is_parent TINYINT(1) NOT NULL DEFAULT 0
    COMMENT '1=부모계정 (랜딩 가입자, tenant당 1명) / 0=자식계정 (ERP 내 생성)' AFTER account_type;

-- tenant당 부모 1명만 보장 (is_deleted=0 + is_parent=1 조합)
-- MySQL은 부분 인덱스 미지원이라 일반 인덱스로 박제 후 서비스 레벨 가드
CREATE INDEX idx_users_tenant_parent ON users(tenant_id, is_parent, is_deleted);
