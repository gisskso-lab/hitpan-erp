-- =============================================================
-- DB-83: 고객 PC 로컬 ERP 테이블 — 로컬 새버전 상태(고리2 마지막 빈 칸) (헌법 #30·#36)
-- - local_update_status: 워치독이 발견한 "현재 사용 가능한 새버전(Major)" 정보를 고객 PC 로컬 ERP DB에 적재한다.
--   · 워치독(고객 PC)이 Major 새버전 manifest 발견 시 본 테이블에 UPSERT(최신 1건 유지) →
--   · ERP 가 로그인 시 본 테이블을 SELECT 해 "설치버전보다 높은 새버전 있나" 판단 → Y/N 동의 팝업 노출.
--   · 본사를 거치지 않는다(A안, 헌법 #30 본사 의존 0). 워치독→로컬 DB→ERP 단방향 로컬 자가완결.
-- 대상 DB: hitpan_erp (고객 PC 로컬 ERP DB) — local_company·local_subscription·local_update_consents 와 동일 위치.
--          신규 고객 PC 설치의 단일 진실원 installer/hitpan_db_clean.sql 에 편입한다(헌법 #36).
-- 패턴 참고: DB-82 local_update_consents (동일 로컬 자가완결 테이블).
-- 헌법: #1 추가만 / #16 무관(단일 쿼리) / #17 InnoDB / #30 로컬 자가완결 / #36 clean DDL 편입
-- 이력: 2026-06-29 고리2 마지막 빈 칸 — 워치독이 발견한 새버전을 ERP 로컬 DB로 내려 적재하는 수신 테이블 신설.
--       (이전 갭: 워치독이 새버전을 본사로 MetaPing 만 했고 ERP 로컬엔 안 내려 UpdateAvailable=false 영구 폴백.)
-- 멱등 설계: latest_version 을 UNIQUE 로 두지 않고, "최신 새버전 1건"을 update_channel 단위로 UPSERT 한다.
--   ERP 는 가장 최근(discovered_at DESC) 1행을 읽으므로, 워치독이 새 Major 발견 시 DELETE→INSERT 로 1건만 남긴다.
-- =============================================================

CREATE TABLE IF NOT EXISTS local_update_status (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    tenant_id VARCHAR(36) NULL COMMENT '로컬 테넌트 식별자(local_company.tenant_id). 워치독은 로컬 단일 테넌트라 NULL 허용',
    latest_version VARCHAR(20) NOT NULL COMMENT '워치독이 발견한 새버전(SemVer Major.Minor.Build, 예: 2.0.0)',
    update_channel VARCHAR(20) NOT NULL DEFAULT 'Major' COMMENT '업데이트 채널(Major/Normal/Emergency). 로그인 팝업은 Major 위주',
    consent_message TEXT NULL COMMENT 'Major 동의 팝업에 노출할 안내 문구(manifest.ConsentMessage)',
    download_url VARCHAR(500) NULL COMMENT 'manifest.DownloadUrl (참고용 — 실제 적용은 워치독이 수행)',
    requires_migration TINYINT(1) NOT NULL DEFAULT 0 COMMENT 'DB 스키마 마이그 동반 여부(manifest.RequiresMigration)',
    discovered_at DATETIME(3) NOT NULL COMMENT '워치독이 새버전을 발견·적재한 시각',
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) COMMENT '로컬 적재 시각',
    INDEX idx_local_update_status_discovered (discovered_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='로컬 새버전 상태(고리2) — 워치독 발견→ERP 로그인 팝업, 고객 PC 로컬 자가완결 — 헌법 #30';
