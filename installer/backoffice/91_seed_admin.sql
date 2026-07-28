-- ============================================================================
-- 히트판 백오피스(hitpan_backoffice) 자립형 통합 스키마 — 마스터 관리자 시드
-- ============================================================================
-- 작성: PM 브라운킴 (사장님 결재 2026-06-19 "마스터계정 시드파일로 넣어")
--
-- ★ 왜 이 파일이 필요한가 (2026-06-19 사고 교훈):
--   - 자립형 스키마(00_core.sql)는 platform_admins "테이블 구조"만 만들고 계정 0건.
--   - DB를 통째로 새로 깔면 로그인 계정이 없어 back.hitpan.kr 진입 자체가 불가.
--   - 2026-06-19 NCP 배포 직후 실제 발생 → 손으로 INSERT해 봉합 → 이 파일로 정식화.
--
-- ★ 비밀번호 해시 처리 (2026-06-19 사장님 결재 = 권고안: "이메일만 시드, 비번은 env 주입"):
--   - 해시를 Git/SQL 파일에 평문으로 두지 않음(헌법 #22·#23).
--   - 해시는 환경변수 ${HITPAN_BO_SEED_ADMIN_HASH} 에서 SchemaMigrator가 실행 직전 치환.
--   - env 미설정 시 SchemaMigrator가 이 파일을 "건너뜀"(경고 로그). 빈 해시로 깨진 계정 INSERT 방지.
--     → 즉 NCP에서 사장님이 env 한 번 넣으면 자동 생성. 안 넣으면 시드 스킵(사고 아님).
--   - 해시 생성법: BCrypt(work factor 11)로 사장님이 정한 비밀번호를 해시 → 그 값을 env에 주입.
--
-- ★ role (2026-06-19): platform_admins.role enum = super_admin/billing_admin/cs_admin/readonly.
--   master 계정은 최고 권한 super_admin (90_seed_permissions.sql의 allowed_roles와 정합).
--
-- ★ 멱등: email UNIQUE 제약 + INSERT IGNORE.
--   - admin_id는 UUID()로 매번 다른 값이지만, 두 번째 실행부터는 email 중복으로 IGNORE → 새 행 0.
--   - 따라서 여러 번 적용해도 master 계정은 1행만 유지. 기존 비밀번호 변경분도 덮지 않음(IGNORE).
-- ============================================================================

INSERT IGNORE INTO platform_admins
    (admin_id, email, password_hash, admin_name, role, is_active, created_at, updated_at)
VALUES
    (UUID(), 'master@hitpan.kr', '${HITPAN_BO_SEED_ADMIN_HASH}', '마스터관리자', 'super_admin', 1, NOW(), NOW());
