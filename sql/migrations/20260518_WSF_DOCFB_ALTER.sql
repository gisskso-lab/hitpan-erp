-- WS-F-D-1 ALTER SQL — 거래명세서 마이그 (DOCFB) 봉합용 컬럼·인덱스 추가
-- 작성: 2026-05-18 PM 브라운킴
-- 사장님 결재: Q1·Q2·Q4 모두 OK
-- 헌법 #13 DESCRIBE 선행 완료, #17 InnoDB 정합, #22 본사 미전송 정합

-- ────────────────────────────────────────────────────────────────
-- 1. sales_deliveries: 마이그 멱등 키 + 레거시 코드 보관
-- ────────────────────────────────────────────────────────────────

-- 1-1. source_id (멱등 키): NULL 허용 + UNIQUE (tenant_id, source_id)
ALTER TABLE sales_deliveries
    ADD COLUMN source_id VARCHAR(80) NULL COMMENT 'WS-F: 마이그 멱등 키 (mig-docfb-IJ_DT-IJ_IO-IJ_SEQ-IJ_BUY)',
    ADD COLUMN legacy_tax_no INT NULL COMMENT 'WS-F: DOCFB.IJ_TAXNO (tax_invoices.tax_no 연결 키)',
    ADD COLUMN legacy_buy_code INT NULL COMMENT 'WS-F: DOCFB.IJ_BUY (사장님 결재 Q4: 음수값 그대로 이관)',
    ADD COLUMN migrated_source_hash CHAR(64) NULL COMMENT 'WS-F: SHA256 무결성 해시',
    ADD UNIQUE KEY uq_sd_source (tenant_id, source_id),
    ADD INDEX idx_sd_legacy_tax_no (tenant_id, legacy_tax_no);

-- ────────────────────────────────────────────────────────────────
-- 2. sales_delivery_items: 마이그 봉합용 (item_id NULL 허용 — 매핑 실패 보호)
-- ────────────────────────────────────────────────────────────────

-- item_id NOT NULL → NULL 허용 (매핑 실패 시 워크플로우 끊김 0, 헌법 #20)
ALTER TABLE sales_delivery_items
    MODIFY COLUMN item_id VARCHAR(36) NULL COMMENT 'WS-F: 마이그 매핑 실패 시 NULL 허용 (헌법 #20)',
    MODIFY COLUMN warehouse_id VARCHAR(36) NULL COMMENT 'WS-F: 마이그 매핑 실패 시 NULL 허용',
    ADD COLUMN legacy_pum VARCHAR(100) NULL COMMENT 'WS-F: DOCFB.IJ_PUM (원본 품목명)',
    ADD COLUMN legacy_ku VARCHAR(100) NULL COMMENT 'WS-F: DOCFB.IJ_KU (원본 규격)',
    ADD COLUMN source_id VARCHAR(80) NULL COMMENT 'WS-F: 마이그 멱등 키 (라인)',
    ADD UNIQUE KEY uq_sdi_source (tenant_id, source_id);

-- ────────────────────────────────────────────────────────────────
-- 3. purchase_receipts: 동일 패턴
-- ────────────────────────────────────────────────────────────────

ALTER TABLE purchase_receipts
    ADD COLUMN source_id VARCHAR(80) NULL COMMENT 'WS-F: 마이그 멱등 키',
    ADD COLUMN legacy_tax_no INT NULL COMMENT 'WS-F: DOCFB.IJ_TAXNO',
    ADD COLUMN legacy_buy_code INT NULL COMMENT 'WS-F: DOCFB.IJ_BUY (Q4 그대로)',
    ADD COLUMN migrated_source_hash CHAR(64) NULL COMMENT 'WS-F: SHA256 무결성',
    ADD UNIQUE KEY uq_pr_source (tenant_id, source_id),
    ADD INDEX idx_pr_legacy_tax_no (tenant_id, legacy_tax_no);

ALTER TABLE purchase_receipt_items
    MODIFY COLUMN item_id VARCHAR(36) NULL COMMENT 'WS-F: 마이그 매핑 실패 NULL (헌법 #20)',
    MODIFY COLUMN warehouse_id VARCHAR(36) NULL COMMENT 'WS-F: 마이그 매핑 실패 NULL',
    ADD COLUMN legacy_pum VARCHAR(100) NULL,
    ADD COLUMN legacy_ku VARCHAR(100) NULL,
    ADD COLUMN source_id VARCHAR(80) NULL,
    ADD UNIQUE KEY uq_pri_source (tenant_id, source_id);

-- ────────────────────────────────────────────────────────────────
-- 4. 시드 잔재 정리 (sales_deliveries source_type='migration_tx' 66,603행)
--    WS-B 시드 삭제 시 잔여분 — PM 추가 확인 후 결정
-- ────────────────────────────────────────────────────────────────

-- 검증 쿼리만 박제 — 삭제는 별도 결재
-- SELECT source_type, COUNT(*) FROM sales_deliveries GROUP BY source_type;
-- source_type='migration_tx' = 5/13 시드 잔재 → 본 마이그 진행 전 삭제 결재 필요

-- ────────────────────────────────────────────────────────────────
-- 5. 검증 쿼리
-- ────────────────────────────────────────────────────────────────

-- SHOW INDEX FROM sales_deliveries WHERE Key_name LIKE 'uq_sd_source';
-- SHOW INDEX FROM purchase_receipts WHERE Key_name LIKE 'uq_pr_source';
