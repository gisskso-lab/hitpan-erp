-- ══════════════════════════════════════════════════════════════
-- BOM assemble 정합성 검증 스크립트
-- 사용법: mysql -u hitpan -p hitpan_erp < scripts/bom-assemble-integrity-check.sql
--
-- 검증 항목:
--   1. item_stock.current_qty와 stock_ledger 합계 일치 여부
--   2. BOM 자재 차감 ↔ 완성품 증가 수량 정합성
--   3. 음수 재고 발생 여부
--   4. stock_adjust_logs 이상치
-- ══════════════════════════════════════════════════════════════

-- ① item_stock ↔ stock_ledger 수량 정합성
SELECT
    '① 재고 장부/원장 정합성' AS test_name,
    CASE WHEN COUNT(*) = 0 THEN '✅ 통과' ELSE CONCAT('❌ 불일치 ', COUNT(*), '건') END AS result
FROM (
    SELECT s.item_id, s.warehouse_id,
           s.current_qty AS stock_qty,
           COALESCE(SUM(l.qty_in - l.qty_out), 0) AS ledger_qty
    FROM item_stock s
    LEFT JOIN stock_ledger l
      ON l.item_id = s.item_id AND l.warehouse_id = s.warehouse_id AND l.tenant_id = s.tenant_id
    GROUP BY s.item_id, s.warehouse_id, s.current_qty
    HAVING ABS(s.current_qty - COALESCE(SUM(l.qty_in - l.qty_out), 0)) > 0.001
) mismatch;

-- ② BOM 생산 기록 — 자재 출고 건수와 완성품 입고 건수 비교
SELECT
    '② BOM 생산 자재/완성품 정합성' AS test_name,
    COUNT(CASE WHEN move_type = 'out' THEN 1 END) AS material_out_lines,
    COUNT(CASE WHEN move_type = 'in' THEN 1 END)  AS product_in_lines,
    CASE
        WHEN COUNT(CASE WHEN move_type = 'in' THEN 1 END) = 0
             AND COUNT(CASE WHEN move_type = 'out' THEN 1 END) = 0
          THEN '✅ BOM 생산 이력 없음'
        WHEN COUNT(CASE WHEN move_type = 'in' THEN 1 END) > 0
          THEN '✅ 통과 (자재/완성품 모두 기록됨)'
        ELSE '❌ 완성품 기록 누락 — BOM assemble 이중처리 버그 의심'
    END AS result
FROM stock_ledger
WHERE source_type = 'bom_production';

-- ③ 음수 재고 체크
SELECT
    '③ 음수 재고' AS test_name,
    CASE WHEN COUNT(*) = 0 THEN '✅ 통과' ELSE CONCAT('❌ ', COUNT(*), '개 품목 음수') END AS result
FROM item_stock
WHERE current_qty < 0;

-- ④ stock_adjust_logs 전후 잔량 일관성
SELECT
    '④ 재고조정 로그 전후 수량' AS test_name,
    CASE WHEN COUNT(*) = 0 THEN '✅ 통과' ELSE CONCAT('⚠️ ', COUNT(*), '건 before+adjust != after') END AS result
FROM stock_adjust_logs
WHERE ABS(before_qty + adjust_qty - after_qty) > 0.001;

-- ⑤ BOM assemble 당일 생산 수량 합계 (최근 7일)
SELECT
    '⑤ 최근 7일 BOM 생산량' AS test_name,
    CONCAT('자재 출고 ', COALESCE(SUM(CASE WHEN move_type='out' THEN qty_out END), 0),
           ' / 완성품 입고 ', COALESCE(SUM(CASE WHEN move_type='in' THEN qty_in END), 0)) AS result
FROM stock_ledger
WHERE source_type = 'bom_production' AND ledger_date >= DATE_SUB(CURDATE(), INTERVAL 7 DAY);

-- ⑥ 결재 문서와 실제 거래 확정 상태 정합성
SELECT
    '⑥ 결재-거래 상태 불일치' AS test_name,
    CASE WHEN COUNT(*) = 0 THEN '✅ 통과' ELSE CONCAT('⚠️ ', COUNT(*), '건') END AS result
FROM approval_documents ad
WHERE ad.doc_type IN ('sales_delivery','purchase_receipt')
  AND ad.status = 'approved'
  AND NOT EXISTS (
      SELECT 1 FROM sales_deliveries sd WHERE sd.delivery_id = ad.ref_id AND sd.status IN ('confirmed','invoiced')
      UNION ALL
      SELECT 1 FROM purchase_receipts pr WHERE pr.receipt_id = ad.ref_id AND pr.status = 'confirmed'
  );
