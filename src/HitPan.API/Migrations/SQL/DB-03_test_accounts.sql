-- 테스트 계정 3종 (공통 비밀번호: Admin1234!)
-- BCrypt 해시: $2a$12$j8KtWNN7I.DmfGEc/e9Q1eAQDfN2hxYRN8jyVecoszlQ.nMGTDGna
-- role 컬럼은 EF Core UserRole enum 문자열이어야 함 (TenantAdmin 등).

SET @tenant_id = '452ca266-97b9-4cd1-a0ac-2f37830c81f6';
SET @platform_id = 'a0000000-0000-0000-0000-000000000001';
SET @pwd = '$2a$12$j8KtWNN7I.DmfGEc/e9Q1eAQDfN2hxYRN8jyVecoszlQ.nMGTDGna';

INSERT INTO users (
  user_id, tenant_id, email,
  password_hash, user_name,
  role, account_type,
  platform_id, is_active,
  created_at, updated_at)
VALUES (
  UUID(), @tenant_id,
  'admin@hitpan.kr',
  @pwd,
  '플랫폼관리자',
  'TenantAdmin',
  'platform_admin',
  @platform_id,
  1, NOW(6), NOW(6)
)
ON DUPLICATE KEY UPDATE
  password_hash = VALUES(password_hash),
  user_name = VALUES(user_name),
  role = VALUES(role),
  account_type = VALUES(account_type),
  platform_id = VALUES(platform_id),
  is_active = VALUES(is_active),
  updated_at = NOW(6);

INSERT INTO users (
  user_id, tenant_id, email,
  password_hash, user_name,
  role, account_type,
  platform_id, is_active,
  created_at, updated_at)
VALUES (
  UUID(), @tenant_id,
  'reseller@hitpan.kr',
  @pwd,
  '대리점관리자',
  'TenantAdmin',
  'reseller_admin',
  @platform_id,
  1, NOW(6), NOW(6)
)
ON DUPLICATE KEY UPDATE
  password_hash = VALUES(password_hash),
  user_name = VALUES(user_name),
  role = VALUES(role),
  account_type = VALUES(account_type),
  platform_id = VALUES(platform_id),
  is_active = VALUES(is_active),
  updated_at = NOW(6);

INSERT INTO users (
  user_id, tenant_id, email,
  password_hash, user_name,
  role, account_type,
  platform_id, is_active,
  created_at, updated_at)
VALUES (
  UUID(), @tenant_id,
  'tenant@hitpan.kr',
  @pwd,
  '고객사관리자',
  'TenantAdmin',
  'tenant_admin',
  @platform_id,
  1, NOW(6), NOW(6)
)
ON DUPLICATE KEY UPDATE
  password_hash = VALUES(password_hash),
  user_name = VALUES(user_name),
  role = VALUES(role),
  account_type = VALUES(account_type),
  platform_id = VALUES(platform_id),
  is_active = VALUES(is_active),
  updated_at = NOW(6);

-- 확인
-- SELECT email, user_name, role, account_type FROM users WHERE tenant_id = @tenant_id;
