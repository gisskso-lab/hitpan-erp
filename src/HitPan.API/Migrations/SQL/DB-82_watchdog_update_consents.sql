-- =============================================================
-- DB-82: 고객 PC 로컬 ERP 테이블 — 업데이트 동의(고리2) 자가완결 (헌법 #30·#36)
-- - local_update_consents: 업데이트 동의(Y/N)를 고객 PC 로컬 ERP DB에 기록한다.
--   · ERP가 로컬에서 새 버전 확인 → 동의 팝업 → 동의/거부를 본 테이블에 INSERT
--   · 워치독(고객 PC)이 본 테이블을 로컬에서 SELECT 해 적용 가부 판단
--   · 동의는 본사를 거치지 않고 고객 PC 안에서 완결된다(A안, 헌법 #30 본사의존 0).
--     본사에는 "동의함" 메타만 MetaPing 으로 알린다(MetaPingPayload 확장은 별개·유지).
-- 대상 DB: hitpan_erp (고객 PC 로컬 ERP DB) — local_company·local_subscription 과 동일 위치.
--          신규 고객 PC 설치의 단일 진실원 installer/hitpan_db_clean.sql 에 편입한다(헌법 #36).
-- 패턴 참고: installer/hitpan_db_clean.sql 의 local_company·local_subscription·login_attempts
-- 헌법: #1 추가만 / #15 빈 catch 무관(DDL) / #17 InnoDB / #30 로컬 자가완결 / #36 clean DDL 편입
-- 이력: 2026-06-29 본사 수신 테이블(watchdog_consents, hitpan_backoffice 대상)에서
--       A안 결재에 따라 고객 PC 로컬 테이블(local_update_consents, hitpan_erp 대상)로 재설계.
-- =============================================================

CREATE TABLE IF NOT EXISTS local_update_consents (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    tenant_id VARCHAR(36) NOT NULL COMMENT '로컬 테넌트 식별자(로컬 DB이므로 평문 — local_company.tenant_id 와 동일값)',
    user_id VARCHAR(64) NOT NULL COMMENT '동의한 ERP 로컬 사용자 식별자',
    update_version VARCHAR(20) NOT NULL COMMENT '동의 대상 업데이트 버전(예: 2.0.0)',
    action ENUM('approve','reject') NOT NULL COMMENT '고리2 Y/N 동의 결과',
    consented_at DATETIME(3) NOT NULL COMMENT '고객이 ERP에서 동의/거부한 시각',
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) COMMENT '로컬 적재 시각',
    INDEX idx_local_update_consents_version (update_version)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='업데이트 동의(고리2) 고객 PC 로컬 자가완결 — 헌법 #30';
