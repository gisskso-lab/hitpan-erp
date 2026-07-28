-- 백오피스 DB 분리 (사장님 결재 2026-06-04, 헌법 #35 객체 완전 분리)
--
-- 헌법 정합:
--   #18·#22 — 본사(백오피스)는 메타·해시만, ERP 업무 데이터 0건
--   #29 — DB CREATE·USE·운영 실행은 사장님 결재 영역. PM은 SQL 박제만
--   #34 — 베타부터 정식 완성도. 같은 인스턴스 두 DB로 분리 시작, 정식 박제 시 백오피스만 클라우드 이전
--   #35 — ERP(hitpan_erp, 고객사 PC) ↔ 백오피스(hitpan_backoffice, 클라우드) 완전 분리
--
-- 이관 대상 (백오피스 영역 — 평문 사업자정보·매출/매입/원장 0건):
--   landing_signups            가입 신청 (biz_no_hash, 평문 0)
--   tenants                    고객사 마스터 (라이선스 해시·메타)
--   tenant_payments            결제 메타 (카드정보·CVC 0건)
--   resellers                  대리점
--   reseller_applications      대리점 신청
--   bo_permissions             백오피스 권한 시드
--
-- ERP DB(hitpan_erp)는 그대로 유지. 본 SQL 실행 후 백오피스 API의 BackofficeDb 연결만
-- hitpan_backoffice로 전환하면 운영 박제 완료.

-- ────────────────────────────────────────
-- 1) 백오피스 DB 생성
-- ────────────────────────────────────────
CREATE DATABASE IF NOT EXISTS hitpan_backoffice
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

-- ────────────────────────────────────────
-- 2) 테이블 이관 (구조 + 데이터)
--    CREATE TABLE … LIKE 로 구조 박제 후 INSERT … SELECT 로 데이터 박제
--    제약·인덱스가 LIKE 로 따라옴. AUTO_INCREMENT 값도 재정렬됨.
-- ────────────────────────────────────────

CREATE TABLE IF NOT EXISTS hitpan_backoffice.landing_signups       LIKE hitpan_erp.landing_signups;
INSERT IGNORE INTO hitpan_backoffice.landing_signups       SELECT * FROM hitpan_erp.landing_signups;

CREATE TABLE IF NOT EXISTS hitpan_backoffice.tenants               LIKE hitpan_erp.tenants;
INSERT IGNORE INTO hitpan_backoffice.tenants               SELECT * FROM hitpan_erp.tenants;

CREATE TABLE IF NOT EXISTS hitpan_backoffice.tenant_payments       LIKE hitpan_erp.tenant_payments;
INSERT IGNORE INTO hitpan_backoffice.tenant_payments       SELECT * FROM hitpan_erp.tenant_payments;

CREATE TABLE IF NOT EXISTS hitpan_backoffice.resellers             LIKE hitpan_erp.resellers;
INSERT IGNORE INTO hitpan_backoffice.resellers             SELECT * FROM hitpan_erp.resellers;

CREATE TABLE IF NOT EXISTS hitpan_backoffice.reseller_applications LIKE hitpan_erp.reseller_applications;
INSERT IGNORE INTO hitpan_backoffice.reseller_applications SELECT * FROM hitpan_erp.reseller_applications;

CREATE TABLE IF NOT EXISTS hitpan_backoffice.bo_permissions        LIKE hitpan_erp.bo_permissions;
INSERT IGNORE INTO hitpan_backoffice.bo_permissions        SELECT * FROM hitpan_erp.bo_permissions;

-- ────────────────────────────────────────
-- 3) 검증 쿼리 (사장님 실행 후 카운트 일치 확인)
-- ────────────────────────────────────────
-- SELECT 'landing_signups' AS t, (SELECT COUNT(*) FROM hitpan_erp.landing_signups) AS erp_cnt,
--                                (SELECT COUNT(*) FROM hitpan_backoffice.landing_signups) AS bo_cnt
-- UNION ALL SELECT 'tenants',
--                                (SELECT COUNT(*) FROM hitpan_erp.tenants),
--                                (SELECT COUNT(*) FROM hitpan_backoffice.tenants)
-- UNION ALL SELECT 'tenant_payments',
--                                (SELECT COUNT(*) FROM hitpan_erp.tenant_payments),
--                                (SELECT COUNT(*) FROM hitpan_backoffice.tenant_payments)
-- UNION ALL SELECT 'resellers',
--                                (SELECT COUNT(*) FROM hitpan_erp.resellers),
--                                (SELECT COUNT(*) FROM hitpan_backoffice.resellers)
-- UNION ALL SELECT 'reseller_applications',
--                                (SELECT COUNT(*) FROM hitpan_erp.reseller_applications),
--                                (SELECT COUNT(*) FROM hitpan_backoffice.reseller_applications)
-- UNION ALL SELECT 'bo_permissions',
--                                (SELECT COUNT(*) FROM hitpan_erp.bo_permissions),
--                                (SELECT COUNT(*) FROM hitpan_backoffice.bo_permissions);

-- ────────────────────────────────────────
-- 4) ERP DB 정리 (검증 통과 + 백오피스 정식 작동 확인 후 별도 결재로 실행)
--    ERP에는 백오피스 도메인 테이블이 남아있을 필요 0. 다만 잘못된 즉시 DROP 방지 위해 별도 분리.
-- ────────────────────────────────────────
-- DROP TABLE IF EXISTS hitpan_erp.landing_signups;
-- DROP TABLE IF EXISTS hitpan_erp.tenants;
-- DROP TABLE IF EXISTS hitpan_erp.tenant_payments;
-- DROP TABLE IF EXISTS hitpan_erp.resellers;
-- DROP TABLE IF EXISTS hitpan_erp.reseller_applications;
-- DROP TABLE IF EXISTS hitpan_erp.bo_permissions;
