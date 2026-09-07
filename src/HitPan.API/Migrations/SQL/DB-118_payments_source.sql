-- ═══════════════════════════════════════════════════════════════════════════
-- DB-118 · payments 에 이관 추적 컬럼 3개 + UNIQUE (20260904작21 A5-DDL · 전결1 D4)
-- ═══════════════════════════════════════════════════════════════════════════
--
-- 🔴 왜 이 DDL 이 필요한가
--   레거시 DOCF5 지급 계열(S_GU B·C·D·E·F, 실측 2,228행)을 payments 로 이관한다.
--   다른 이관 표(collections·cashbook·stock_ledger·tax_invoices…)는 전부
--   source_type / source_id / migrated_source_hash + UNIQUE 로 재실행 멱등을 보장하는데
--   payments 만 그 자리가 없었다 — 지급 이관이 0건이던 이유의 절반이다.
--   (나머지 절반은 코드가 S_GU 를 안 갈랐던 것 — MdbMigrationService.MigratePaymentsAsync 신설)
--
-- 하는 일: 컬럼 추가 3 · UNIQUE 1. 데이터 변경 0건. 컬럼 삭제 0건(헌법 #37).
-- 멱등: information_schema 로 있으면 건너뛴다 — 2회 적용 = 신규 0 (DB-94 와 같은 관용구).
-- 출하 DDL(installer/hitpan_db_clean.sql) 에 같은 정의를 편입했다(헌법 #36).
--
-- ⚠️ 이 마이그는 tenants 를 조인하지 않는다.
--    DB-111/112 사고: tenants 를 조인한 마이그가 고객 PC 에서 0행이라 "성공"으로 기록되고 아무 일도 안 했다.
-- ═══════════════════════════════════════════════════════════════════════════

-- ── 1) source_type ──
SET @col_exists := (
    SELECT COUNT(*) FROM information_schema.columns
    WHERE table_schema = DATABASE()
      AND table_name = 'payments'
      AND column_name = 'source_type'
);

SET @ddl := IF(@col_exists = 0,
    'ALTER TABLE payments
       ADD COLUMN source_type varchar(30) DEFAULT NULL
       COMMENT ''이관 출처 - migration/manual (DB-118)''
       AFTER updated_by',
    'SELECT ''DB-118: payments.source_type already exists'' AS skipped');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ── 2) source_id ──
SET @col_exists := (
    SELECT COUNT(*) FROM information_schema.columns
    WHERE table_schema = DATABASE()
      AND table_name = 'payments'
      AND column_name = 'source_id'
);

SET @ddl := IF(@col_exists = 0,
    'ALTER TABLE payments
       ADD COLUMN source_id varchar(80) DEFAULT NULL
       COMMENT ''이관 원본 식별자 - mig-{S_BUY}-{S_YMD}-{S_SUN}-{S_GU} (DB-118)''
       AFTER source_type',
    'SELECT ''DB-118: payments.source_id already exists'' AS skipped');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ── 3) migrated_source_hash ──
SET @col_exists := (
    SELECT COUNT(*) FROM information_schema.columns
    WHERE table_schema = DATABASE()
      AND table_name = 'payments'
      AND column_name = 'migrated_source_hash'
);

SET @ddl := IF(@col_exists = 0,
    'ALTER TABLE payments
       ADD COLUMN migrated_source_hash char(64) DEFAULT NULL
       COMMENT ''WS-11 축 2: SHA256 멱등 키 (DB-118)''
       AFTER source_id',
    'SELECT ''DB-118: payments.migrated_source_hash already exists'' AS skipped');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ── 4) UNIQUE uq_payments_source (tenant_id, source_type, source_id) ──
--    collections 의 uq_collections_source 와 같은 모양. INSERT IGNORE 가 이 키로 중복을 거른다.
--    기존 행은 source_type/source_id 가 NULL 이라(수기 입력) UNIQUE 에 걸리지 않는다.
SET @idx_exists := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'payments'
      AND index_name = 'uq_payments_source'
);

SET @ddl := IF(@idx_exists = 0,
    'ALTER TABLE payments ADD UNIQUE KEY `uq_payments_source` (`tenant_id`,`source_type`,`source_id`)',
    'SELECT ''DB-118: uq_payments_source already exists'' AS skipped');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ── 되돌리기 (참고 · 실행하지 않는다) ──
-- ALTER TABLE payments DROP INDEX uq_payments_source;
-- ALTER TABLE payments DROP COLUMN migrated_source_hash, DROP COLUMN source_id, DROP COLUMN source_type;
