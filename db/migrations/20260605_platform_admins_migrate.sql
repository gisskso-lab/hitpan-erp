-- 진범 #3 봉합 — platform_admins/platforms 백오피스 DB 이관 (사장님 결재 2026-06-05)
--
-- 사유:
--   백오피스 DB 분리(20260604_backoffice_db_split.sql) 시 본 2개 테이블 이관 누락.
--   BackofficeAuthController는 백오피스 DB 조회 → 사장님 로그인 시도마다 500.
--
-- 헌법 정합:
--   #15·#17·#18·#22·#25·#35
--   ERP DB 원본 보존 (롤백 가능 — 사장님 결재 후 ERP DB 삭제 별도 차수)

CREATE TABLE IF NOT EXISTS platforms LIKE hitpan_erp.platforms;
INSERT IGNORE INTO platforms SELECT * FROM hitpan_erp.platforms;

CREATE TABLE IF NOT EXISTS platform_admins LIKE hitpan_erp.platform_admins;
INSERT IGNORE INTO platform_admins SELECT * FROM hitpan_erp.platform_admins;
