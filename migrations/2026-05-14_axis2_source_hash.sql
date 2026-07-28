-- WS-11 정공법 축 2: 멱등성 영속 키 source_hash
-- 2026-05-14, 사장님 직접 명령
-- 적용 대상: partners, collections, stock_ledger, tax_invoices
-- 컬럼: migrated_source_hash CHAR(64) DEFAULT NULL
-- UNIQUE: (tenant_id, migrated_source_hash) — NULL은 멀티 허용(MySQL/MariaDB 표준)
-- 재실행 시 SHA256 해시 충돌이면 INSERT IGNORE로 자동 skip → 멱등.

ALTER TABLE partners
    ADD COLUMN migrated_source_hash CHAR(64) DEFAULT NULL COMMENT 'WS-11 축 2: SHA256 멱등 키',
    ADD UNIQUE KEY uq_partners_source_hash (tenant_id, migrated_source_hash);

ALTER TABLE collections
    ADD COLUMN migrated_source_hash CHAR(64) DEFAULT NULL COMMENT 'WS-11 축 2: SHA256 멱등 키',
    ADD UNIQUE KEY uq_collections_source_hash (tenant_id, migrated_source_hash);

ALTER TABLE stock_ledger
    ADD COLUMN migrated_source_hash CHAR(64) DEFAULT NULL COMMENT 'WS-11 축 2: SHA256 멱등 키',
    ADD UNIQUE KEY uq_stock_ledger_source_hash (tenant_id, migrated_source_hash);

ALTER TABLE tax_invoices
    ADD COLUMN migrated_source_hash CHAR(64) DEFAULT NULL COMMENT 'WS-11 축 2: SHA256 멱등 키',
    ADD UNIQUE KEY uq_tax_invoices_source_hash (tenant_id, migrated_source_hash);
