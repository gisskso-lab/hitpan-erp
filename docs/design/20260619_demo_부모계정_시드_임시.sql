-- ============================================================
-- demo.hitpan.kr (이 PC 로컬 DB) 테스트용 부모계정 시드 — 임시
-- 작성: 2026-06-19 / 목적: 계정 생성 화면 우회, AI 작업 테스트 로그인 확보
-- 대상 DB: hitpan_erp / 테넌트: HITPAN-MAIN (452ca266-97b9-4cd1-a0ac-2f37830c81f6)
-- 비번: Admin1234!  (BCrypt 해시, work factor 11 — 코드와 동일)
-- 코드 정합: CompanyBootstrapController.CreateParent (role/account_type='tenant_admin', is_parent=1)
-- ⚠️ 임시 테스트용. 정식 부모계정 생성 봉합되면 정리.
-- ============================================================

-- 기존 master@hitpan.kr을 부모계정으로 승격 + 비번 재설정
UPDATE users
SET password_hash = '$2a$11$38ilsRdKgZ01hc/CA1WRtuZc2VsPYrlfuVvia5lteswq0b6QYUH7m',
    role          = 'tenant_admin',
    account_type  = 'tenant_admin',
    is_parent     = 1,
    is_active     = 1,
    is_deleted    = 0,
    failed_login_count = 0,
    lockout_end   = NULL,
    updated_at    = UTC_TIMESTAMP(6)
WHERE email = 'master@hitpan.kr'
  AND tenant_id = '452ca266-97b9-4cd1-a0ac-2f37830c81f6';

-- 확인
SELECT user_id, tenant_id, email, role, account_type, is_parent, is_active, is_deleted
FROM users WHERE email = 'master@hitpan.kr';
