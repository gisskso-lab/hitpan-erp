-- ═══════════════════════════════════════════════════════════════════════════
-- DB-119 · hr_reports 에 이관 추적 컬럼 3개 + UNIQUE (20260909작22 D1 · 설계 별지 §4-1)
-- ═══════════════════════════════════════════════════════════════════════════
--
-- 🔴 왜 이 DDL 이 필요한가
--   레거시 POTHER.DOCME(상담·메모 이력, 실측 242,106행)를 (사원,날짜) 묶음 9,167장의 일일보고서로
--   hr_reports 에 이관한다 (사장님 9/8 ③ "일일보고서에 작성자, 내용만 살려서 이관 … 결재 완료된 건으로 보관",
--   "표를 따로 만들면 메뉴도 생겨야 하고 구도가 바뀐다" ⇒ 새 표 없이 기존 표에).
--   다른 이관 표(collections·payments·cashbook·stock_ledger·tax_invoices…)는 전부
--   source_type / source_id / migrated_source_hash + UNIQUE 로 재실행 멱등을 보장하는데
--   hr_reports 에는 그 자리가 없었다(선행검증 §2-3 SHOW COLUMNS 0). 없으면 병합 모드에서 두 번 돌릴 때마다 같은 장이 또 쌓인다.
--
-- 하는 일: 컬럼 추가 3 · UNIQUE 1. 데이터 변경 0건. 컬럼 삭제 0건(헌법 #37).
-- 멱등: information_schema 로 있으면 건너뛴다 — 2회 적용 = 신규 0 · skipped 4 (DB-118 과 같은 관용구).
-- 출하 DDL(installer/hitpan_db_clean.sql) 에 같은 정의를 편입했다(헌법 #36).
--
-- DESCRIBE hr_reports (hitpan_e2e · 2026-09-09 · 헌법 #13) — 적용 전 마지막 컬럼은 updated_at:
--   report_id varchar(36) PK · tenant_id varchar(36) · employee_id varchar(36) · report_type varchar(20)
--   period_start date · period_end date · title varchar(200) · content text · cause text NULL · action_plan text NULL
--   status varchar(20) DEFAULT 'draft' · submitted_at datetime(6) NULL · approved_by varchar(36) NULL
--   approved_at datetime(6) NULL · reject_reason varchar(200) NULL · created_at datetime(6) · updated_at datetime(6)
--
-- ⚠️ 이 마이그는 tenants 를 조인하지 않는다.
--    DB-111/112 사고: tenants 를 조인한 마이그가 고객 PC 에서 0행이라 "성공"으로 기록되고 아무 일도 안 했다.
-- ═══════════════════════════════════════════════════════════════════════════

-- ── 1) source_type ──
SET @col_exists := (
    SELECT COUNT(*) FROM information_schema.columns
    WHERE table_schema = DATABASE()
      AND table_name = 'hr_reports'
      AND column_name = 'source_type'
);

SET @ddl := IF(@col_exists = 0,
    'ALTER TABLE hr_reports
       ADD COLUMN source_type varchar(30) DEFAULT NULL
       COMMENT ''이관 식별 - migration (DB-119)''
       AFTER updated_at',
    'SELECT ''DB-119: hr_reports.source_type already exists'' AS skipped');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ── 2) source_id ──
SET @col_exists := (
    SELECT COUNT(*) FROM information_schema.columns
    WHERE table_schema = DATABASE()
      AND table_name = 'hr_reports'
      AND column_name = 'source_id'
);

SET @ddl := IF(@col_exists = 0,
    'ALTER TABLE hr_reports
       ADD COLUMN source_id varchar(80) DEFAULT NULL
       COMMENT ''이관 원본 식별자 - docme-{ME_DATE}-{sha8(TRIM(ME_SAWON))} (DB-119)''
       AFTER source_type',
    'SELECT ''DB-119: hr_reports.source_id already exists'' AS skipped');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ── 3) migrated_source_hash ──
SET @col_exists := (
    SELECT COUNT(*) FROM information_schema.columns
    WHERE table_schema = DATABASE()
      AND table_name = 'hr_reports'
      AND column_name = 'migrated_source_hash'
);

SET @ddl := IF(@col_exists = 0,
    'ALTER TABLE hr_reports
       ADD COLUMN migrated_source_hash char(64) DEFAULT NULL
       COMMENT ''WS-11 축 2: 본문 SHA256 (DB-119)''
       AFTER source_id',
    'SELECT ''DB-119: hr_reports.migrated_source_hash already exists'' AS skipped');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ── 4) UNIQUE uq_hr_reports_source (tenant_id, source_type, source_id) ──
--    payments 의 uq_payments_source(DB-118) 와 같은 모양. INSERT IGNORE 가 이 키로 중복을 거른다.
--    기존 행(화면에서 쓴 보고서)은 source_type/source_id 가 NULL 이라 UNIQUE 에 걸리지 않는다.
SET @idx_exists := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'hr_reports'
      AND index_name = 'uq_hr_reports_source'
);

SET @ddl := IF(@idx_exists = 0,
    'ALTER TABLE hr_reports ADD UNIQUE KEY `uq_hr_reports_source` (`tenant_id`,`source_type`,`source_id`)',
    'SELECT ''DB-119: uq_hr_reports_source already exists'' AS skipped');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ── 되돌리기 (참고 · 실행하지 않는다) ──
-- ALTER TABLE hr_reports DROP INDEX uq_hr_reports_source;
-- ALTER TABLE hr_reports DROP COLUMN migrated_source_hash, DROP COLUMN source_id, DROP COLUMN source_type;
