-- WS-F-D-1 후처리: 거래명세서 ↔ 세금계산서 정합성 복구
-- 작성: 2026-05-18 PM 브라운킴
-- 실행 시점: DOCFB 마이그 완료 후 (mig 완료 보고 화면에서 자동 실행 권장 — 후속 PR)
-- 헌법 #20 워크플로우 끊김 0 정합

-- ── 1. sales_deliveries → tax_invoices (매출 세금계산서 연결) ──
UPDATE sales_deliveries sd
INNER JOIN tax_invoices ti
    ON ti.tenant_id = sd.tenant_id
    AND ti.tax_no = CAST(sd.legacy_tax_no AS CHAR(8))
    AND ti.direction = 'S'
SET sd.tax_invoice_id = ti.invoice_id
WHERE sd.source_type = 'migration'
  AND sd.tax_invoice_id IS NULL
  AND sd.legacy_tax_no IS NOT NULL
  AND sd.legacy_tax_no > 0;

SELECT ROW_COUNT() AS sales_linked;

-- ── 2. tax_invoices.delivery_id 역참조 (sales_deliveries 우선) ──
-- 진범 #3 (5/15) 봉합 시 delivery_id NULL 허용 ALTER 완료 — 이제 채워넣음.
UPDATE tax_invoices ti
INNER JOIN sales_deliveries sd
    ON sd.tax_invoice_id = ti.invoice_id
SET ti.delivery_id = sd.delivery_id
WHERE ti.delivery_id IS NULL
  AND ti.source_type = 'migration';

SELECT ROW_COUNT() AS tax_delivery_back_linked;

-- ── 3. purchase_receipts → tax_invoices (매입 세금계산서 연결) ──
-- 매입 세금계산서는 direction='B'.
-- 단, purchase_receipts에는 tax_invoice_id 컬럼이 없으므로 별도 매핑 테이블 또는 ALTER 필요.
-- 본 PR 범위 외 — 5/19 후속 PR.

-- 검증 쿼리
-- SELECT
--   (SELECT COUNT(*) FROM sales_deliveries WHERE source_type='migration') AS sd_migrated,
--   (SELECT COUNT(*) FROM sales_deliveries WHERE source_type='migration' AND tax_invoice_id IS NOT NULL) AS sd_linked,
--   (SELECT COUNT(*) FROM tax_invoices WHERE source_type='migration') AS tx_migrated,
--   (SELECT COUNT(*) FROM tax_invoices WHERE source_type='migration' AND delivery_id IS NOT NULL) AS tx_linked;
