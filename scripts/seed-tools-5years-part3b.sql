-- PART 3b — 매입 + 발주 + 경비 + 수금 + 재고원장 + sync + 정합성
SET @tenant = (SELECT tenant_id FROM tenants LIMIT 1);
SET @start_date = '2021-08-01';
SET @end_date = '2026-07-31';
SET @days_span = DATEDIFF(@end_date, @start_date) + 1;

-- 재사용 헬퍼
DROP TEMPORARY TABLE IF EXISTS tmp_n;
CREATE TEMPORARY TABLE tmp_n (n INT PRIMARY KEY) ENGINE=Memory;
INSERT INTO tmp_n (n)
SELECT a.N + b.N*10 + c.N*100 + d.N*1000
FROM (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) a,
     (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) b,
     (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) c,
     (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) d
WHERE (a.N + b.N*10 + c.N*100 + d.N*1000) < 2000;

DROP TEMPORARY TABLE IF EXISTS tmp_suppliers;
CREATE TEMPORARY TABLE tmp_suppliers (rn INT AUTO_INCREMENT PRIMARY KEY, partner_id VARCHAR(36)) ENGINE=Memory;
INSERT INTO tmp_suppliers (partner_id) SELECT partner_id FROM partners WHERE tenant_id=@tenant AND partner_type='supplier';

DROP TEMPORARY TABLE IF EXISTS tmp_items;
CREATE TEMPORARY TABLE tmp_items (rn INT AUTO_INCREMENT PRIMARY KEY, item_id VARCHAR(36), item_type VARCHAR(20),
  purchase_price DECIMAL(15,2), sale_price DECIMAL(15,2)) ENGINE=Memory;
INSERT INTO tmp_items (item_id, item_type, purchase_price, sale_price)
SELECT item_id, item_type, purchase_price, sale_price FROM items WHERE tenant_id=@tenant ORDER BY item_code;

-- tmp_purchase_deals: 일자별 매입 생성 시드
DROP TEMPORARY TABLE IF EXISTS tmp_purchase_deals;
CREATE TEMPORARY TABLE tmp_purchase_deals (
  idx BIGINT AUTO_INCREMENT PRIMARY KEY,
  d DATE, supplier_id VARCHAR(36),
  flow VARCHAR(20),  -- 'direct' | 'po_receipt'
  item_count INT
) ENGINE=Memory;

INSERT INTO tmp_purchase_deals (d, supplier_id, flow, item_count)
SELECT
  DATE_ADD(@start_date, INTERVAL n.n DAY),
  (SELECT partner_id FROM tmp_suppliers WHERE rn = ((CRC32(CONCAT(n.n, nn.n, 's')) MOD 300) + 1)),
  CASE WHEN CRC32(CONCAT(n.n, nn.n)) MOD 100 < 40 THEN 'po_receipt' ELSE 'direct' END,
  ((CRC32(CONCAT(n.n, nn.n, 'pi')) MOD 4) + 1)
FROM tmp_n n
CROSS JOIN tmp_n nn
WHERE n.n < @days_span
  AND DAYOFWEEK(DATE_ADD(@start_date, INTERVAL n.n DAY)) NOT IN (1,7)  -- 평일만
  AND nn.n < GREATEST(0, FLOOR(
    6 * CASE MONTH(DATE_ADD(@start_date, INTERVAL n.n DAY))
          WHEN 7 THEN 0.80 WHEN 8 THEN 0.75
          WHEN 12 THEN 1.20 WHEN 3 THEN 1.15
          ELSE 1.00 END
      * (0.85 + (CRC32(DATE_ADD(@start_date, INTERVAL n.n DAY)) MOD 30) / 100)
  ));

SELECT CONCAT('✅ 매입 시드: ', COUNT(*), '건') AS r FROM tmp_purchase_deals;

-- 발주 (po_receipt flow만)
INSERT INTO purchase_orders (po_id, tenant_id, po_no, partner_id, employee_id, po_date, expected_date,
  status, total_amount, vat_amount, created_at, updated_at)
SELECT
  CONCAT('po-', LPAD(idx, 7, '0')),
  @tenant,
  CONCAT('PO-', DATE_FORMAT(d, '%y%m%d'), '-', LPAD(idx MOD 10000, 4, '0')),
  supplier_id, 'emp-pmgr', d,
  DATE_ADD(d, INTERVAL ((CRC32(idx) MOD 10) + 3) DAY),
  'received',
  0, 0,
  TIMESTAMP(d, '08:50:00'), TIMESTAMP(d, '08:50:00')
FROM tmp_purchase_deals WHERE flow='po_receipt';

SELECT CONCAT('✅ 발주: ', COUNT(*), '건') AS r FROM purchase_orders WHERE tenant_id=@tenant;

-- 발주 품목
INSERT INTO purchase_order_items (po_item_id, po_id, tenant_id, item_id, ordered_qty, received_qty,
  unit_price, supply_amount, vat_amount, warehouse_id, item_status)
SELECT
  UUID(),
  CONCAT('po-', LPAD(pd.idx, 7, '0')),
  @tenant,
  ti.item_id,
  ((CRC32(CONCAT(pd.idx, n.n, 'q')) MOD 30) + 5),
  ((CRC32(CONCAT(pd.idx, n.n, 'q')) MOD 30) + 5),
  ti.purchase_price,
  ((CRC32(CONCAT(pd.idx, n.n, 'q')) MOD 30) + 5) * ti.purchase_price,
  ROUND(((CRC32(CONCAT(pd.idx, n.n, 'q')) MOD 30) + 5) * ti.purchase_price * 0.1, 0),
  CASE WHEN CRC32(CONCAT(pd.idx, n.n)) MOD 100 < 60 THEN 'wh-main'
       WHEN CRC32(CONCAT(pd.idx, n.n)) MOD 100 < 85 THEN 'wh-sub1'
       ELSE 'wh-sub2' END,
  'received'
FROM tmp_purchase_deals pd
CROSS JOIN tmp_n n
JOIN tmp_items ti ON ti.rn = ((CRC32(CONCAT(pd.idx, n.n, 'i')) MOD 1000) + 1)
WHERE pd.flow='po_receipt' AND n.n < pd.item_count;

UPDATE purchase_orders po
JOIN (SELECT po_id, SUM(supply_amount) s, SUM(vat_amount) v FROM purchase_order_items GROUP BY po_id) poi ON poi.po_id=po.po_id
SET po.total_amount=poi.s, po.vat_amount=poi.v;

-- 매입 (direct + po_receipt 전체)
INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, po_id, partner_id, created_by,
  receipt_date, source_type, status, total_amount, vat_amount, created_at)
SELECT
  CONCAT('pr-', LPAD(idx, 7, '0')),
  @tenant,
  CONCAT('PR-', DATE_FORMAT(d, '%y%m%d'), '-', LPAD(idx MOD 10000, 4, '0')),
  CASE WHEN flow='po_receipt' THEN CONCAT('po-', LPAD(idx, 7, '0')) END,
  supplier_id, 'emp-pmgr', d,
  CASE WHEN flow='po_receipt' THEN 'from_po' ELSE 'direct' END,
  CASE WHEN CRC32(idx) MOD 100 < 98 THEN 'confirmed' ELSE 'draft' END,
  0, 0,
  TIMESTAMP(d, '11:40:00')
FROM tmp_purchase_deals;

SELECT CONCAT('✅ 매입: ', COUNT(*), '건') AS r FROM purchase_receipts WHERE tenant_id=@tenant;

-- 매입 품목
INSERT INTO purchase_receipt_items (receipt_item_id, receipt_id, tenant_id, item_id, qty,
  unit_price, supply_amount, vat_amount, warehouse_id)
SELECT
  UUID(),
  CONCAT('pr-', LPAD(pd.idx, 7, '0')),
  @tenant,
  ti.item_id,
  ((CRC32(CONCAT(pd.idx, n.n, 'q')) MOD 30) + 5),
  ti.purchase_price,
  ((CRC32(CONCAT(pd.idx, n.n, 'q')) MOD 30) + 5) * ti.purchase_price,
  ROUND(((CRC32(CONCAT(pd.idx, n.n, 'q')) MOD 30) + 5) * ti.purchase_price * 0.1, 0),
  CASE WHEN CRC32(CONCAT(pd.idx, n.n)) MOD 100 < 60 THEN 'wh-main'
       WHEN CRC32(CONCAT(pd.idx, n.n)) MOD 100 < 85 THEN 'wh-sub1'
       ELSE 'wh-sub2' END
FROM tmp_purchase_deals pd
CROSS JOIN tmp_n n
JOIN tmp_items ti ON ti.rn = ((CRC32(CONCAT(pd.idx, n.n, 'i')) MOD 1000) + 1)
WHERE n.n < pd.item_count;

UPDATE purchase_receipts pr
JOIN (SELECT receipt_id, SUM(supply_amount) s, SUM(vat_amount) v FROM purchase_receipt_items GROUP BY receipt_id) pri ON pri.receipt_id=pr.receipt_id
SET pr.total_amount=pri.s, pr.vat_amount=pri.v
WHERE pr.tenant_id=@tenant;

SELECT CONCAT('✅ 매입 품목: ', COUNT(*), '건') AS r FROM purchase_receipt_items WHERE tenant_id=@tenant;

-- ═══════════════════════════════════════════════════════════════════
-- 5. 경비처리 (현장영업 3명 — 월 8건/인 × 60개월 × 3명 ≈ 1,440건)
-- ═══════════════════════════════════════════════════════════════════
INSERT INTO expenses (expense_id, tenant_id, expense_date, employee_id, category, description,
  amount, vat_amount, payment_method, receipt_yn, approval_status, created_at, updated_at)
SELECT
  UUID(),
  @tenant,
  DATE_ADD(@start_date, INTERVAL ((n.n MOD 60) * 30 + (CRC32(CONCAT(n.n, m.n, 'd')) MOD 28) + 1) DAY),
  ELT((n.n MOD 3) + 1, 'emp-fs1','emp-fs2','emp-fs3'),
  ELT((CRC32(CONCAT(n.n, m.n, 'c')) MOD 6) + 1,
    '교통비', '식대', '접대비', '주차비', '유류비', '통신비'),
  CONCAT(
    ELT((CRC32(CONCAT(n.n, m.n, 'd1')) MOD 5) + 1, '거래처 방문', '현장 답사', '고객 미팅', '제품 시연', '긴급 배송'),
    ' — ',
    ELT((CRC32(CONCAT(n.n, m.n, 'd2')) MOD 10) + 1,
      '서울건설','경기산업','부천공업사','인천철물점','수원자재','안산인테리어','성남설비','용인전기','고양공구마트','광명기계')
  ),
  FLOOR(5000 + (CRC32(CONCAT(n.n, m.n, 'a')) MOD 95000)),
  ROUND(FLOOR(5000 + (CRC32(CONCAT(n.n, m.n, 'a')) MOD 95000)) * 0.1, 0),
  ELT((CRC32(CONCAT(n.n, m.n, 'p')) MOD 4) + 1, 'card','card','cash','card'),
  CASE WHEN CRC32(CONCAT(n.n, m.n)) MOD 100 < 85 THEN 1 ELSE 0 END,  -- 85% 영수증 有
  CASE WHEN CRC32(CONCAT(n.n, m.n)) MOD 100 < 75 THEN 'approved'
       WHEN CRC32(CONCAT(n.n, m.n)) MOD 100 < 90 THEN 'pending'
       ELSE 'rejected' END,
  NOW(6), NOW(6)
FROM tmp_n n
CROSS JOIN tmp_n m
WHERE n.n < 180  -- 60개월 × 3명
  AND m.n < 8;   -- 월 8건

SELECT CONCAT('✅ 경비처리: ', COUNT(*), '건 (현장영업 3명, 5년간)') AS r FROM expenses WHERE tenant_id=@tenant;

-- ═══════════════════════════════════════════════════════════════════
-- 6. 수금 (공구상가 현실: 외상 많음 — 정상 55% / 연체 30% / 미수 15%)
-- ═══════════════════════════════════════════════════════════════════
INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount,
  collection_method, ref_doc_type, ref_doc_id, is_active, created_at, updated_at)
SELECT UUID(), @tenant, sd.partner_id,
  CASE WHEN CRC32(sd.delivery_id) MOD 100 < 55
         THEN DATE_ADD(sd.delivery_date, INTERVAL (15 + CRC32(sd.delivery_id) MOD 20) DAY)  -- 정상 15~35일
       ELSE DATE_ADD(sd.delivery_date, INTERVAL (40 + CRC32(sd.delivery_id) MOD 50) DAY)   -- 연체 40~90일
  END,
  sd.total_amount + sd.vat_amount,
  ELT((CRC32(sd.delivery_id) MOD 4) + 1, 'bank_transfer','bank_transfer','card','check'),
  'sales_delivery', sd.delivery_id, 1,
  TIMESTAMP(sd.delivery_date, '16:00:00'), TIMESTAMP(sd.delivery_date, '16:00:00')
FROM sales_deliveries sd
WHERE sd.tenant_id=@tenant
  AND sd.status='confirmed'
  AND sd.delivery_date < CURDATE() - INTERVAL 5 DAY
  AND CRC32(sd.delivery_id) MOD 100 < 85;  -- 15% 미수 (수금 없음)

SELECT CONCAT('✅ 수금: ', COUNT(*), '건') AS r FROM collections WHERE tenant_id=@tenant;

-- ═══════════════════════════════════════════════════════════════════
-- 7. stock_ledger (매출 out + 매입 in)
-- ═══════════════════════════════════════════════════════════════════
INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, partner_id, employee_id, ledger_date, ym,
  move_type, source_type, source_id, qty_in, qty_out, unit_cost, supply_amount)
SELECT @tenant, sdi.item_id, sdi.warehouse_id, sd.partner_id, sd.employee_id, sd.delivery_date,
  DATE_FORMAT(sd.delivery_date, '%Y-%m'),
  'out', 'sales_delivery', sd.delivery_id,
  0, sdi.qty, sdi.unit_price, sdi.supply_amount
FROM sales_delivery_items sdi
JOIN sales_deliveries sd ON sd.delivery_id=sdi.delivery_id
WHERE sd.tenant_id=@tenant AND sd.status='confirmed';

INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, partner_id, employee_id, ledger_date, ym,
  move_type, source_type, source_id, qty_in, qty_out, unit_cost, supply_amount)
SELECT @tenant, pri.item_id, pri.warehouse_id, pr.partner_id, pr.created_by, pr.receipt_date,
  DATE_FORMAT(pr.receipt_date, '%Y-%m'),
  'in', 'purchase_receipt', pr.receipt_id,
  pri.qty, 0, pri.unit_price, pri.supply_amount
FROM purchase_receipt_items pri
JOIN purchase_receipts pr ON pr.receipt_id=pri.receipt_id
WHERE pr.tenant_id=@tenant AND pr.status='confirmed';

SELECT CONCAT('✅ 재고원장: ', COUNT(*), '건') AS r FROM stock_ledger WHERE tenant_id=@tenant;

-- ═══════════════════════════════════════════════════════════════════
-- 8. item_stock 재계산
-- ═══════════════════════════════════════════════════════════════════
UPDATE item_stock s
INNER JOIN (
  SELECT tenant_id, item_id, warehouse_id,
         SUM(qty_in) - SUM(qty_out) AS net_qty,
         AVG(unit_cost) AS avg_c
  FROM stock_ledger GROUP BY tenant_id, item_id, warehouse_id
) l ON s.tenant_id=l.tenant_id AND s.item_id=l.item_id AND s.warehouse_id=l.warehouse_id
SET s.current_qty = GREATEST(l.net_qty, 0),
    s.avg_cost = l.avg_c,
    s.last_updated_at = NOW(6)
WHERE s.tenant_id=@tenant;

SELECT CONCAT('✅ item_stock 재계산: ', COUNT(*), '행') AS r FROM item_stock WHERE tenant_id=@tenant;

-- ═══════════════════════════════════════════════════════════════════
-- 9. partner_balance
-- ═══════════════════════════════════════════════════════════════════
INSERT INTO partner_balance (balance_id, tenant_id, partner_id, total_sales, total_receipt, total_purchase, total_payment, last_updated_at)
SELECT UUID(), @tenant, p.partner_id,
  COALESCE(sd_sum, 0), COALESCE(coll_sum, 0), COALESCE(pr_sum, 0), 0, NOW(6)
FROM partners p
LEFT JOIN (SELECT partner_id, SUM(total_amount+vat_amount) sd_sum FROM sales_deliveries WHERE tenant_id=@tenant AND status='confirmed' GROUP BY partner_id) sd ON sd.partner_id=p.partner_id
LEFT JOIN (SELECT partner_id, SUM(amount) coll_sum FROM collections WHERE tenant_id=@tenant AND ref_doc_type='sales_delivery' AND is_active=1 GROUP BY partner_id) c ON c.partner_id=p.partner_id
LEFT JOIN (SELECT partner_id, SUM(total_amount+vat_amount) pr_sum FROM purchase_receipts WHERE tenant_id=@tenant AND status='confirmed' GROUP BY partner_id) pr ON pr.partner_id=p.partner_id
WHERE p.tenant_id=@tenant AND p.is_deleted=0;

SELECT CONCAT('✅ 거래처잔액: ', COUNT(*), '건') AS r FROM partner_balance WHERE tenant_id=@tenant;

SET FOREIGN_KEY_CHECKS = 1;

-- ═══════════════════════════════════════════════════════════════════
-- 최종 집계
-- ═══════════════════════════════════════════════════════════════════
SELECT '━━━━ 대한공구상사 5년치 DB 집계 ━━━━' AS report;
SELECT t AS table_name, c AS rows_cnt FROM (
  SELECT 'employees' t, COUNT(*) c FROM employees WHERE tenant_id=@tenant UNION ALL
  SELECT 'partners (all)', COUNT(*) FROM partners WHERE tenant_id=@tenant UNION ALL
  SELECT 'partners (supplier)', COUNT(*) FROM partners WHERE tenant_id=@tenant AND partner_type='supplier' UNION ALL
  SELECT 'partners (customer)', COUNT(*) FROM partners WHERE tenant_id=@tenant AND partner_type='customer' UNION ALL
  SELECT 'items (all)', COUNT(*) FROM items WHERE tenant_id=@tenant UNION ALL
  SELECT 'items (promo)', COUNT(*) FROM items WHERE tenant_id=@tenant AND item_type='promo' UNION ALL
  SELECT 'items (assembly)', COUNT(*) FROM items WHERE tenant_id=@tenant AND item_type='assembly' UNION ALL
  SELECT 'bom_headers', COUNT(*) FROM bom_headers WHERE tenant_id=@tenant UNION ALL
  SELECT 'quotations', COUNT(*) FROM quotations WHERE tenant_id=@tenant UNION ALL
  SELECT 'sales_orders', COUNT(*) FROM sales_orders WHERE tenant_id=@tenant UNION ALL
  SELECT 'sales_deliveries', COUNT(*) FROM sales_deliveries WHERE tenant_id=@tenant UNION ALL
  SELECT 'sales_delivery_items', COUNT(*) FROM sales_delivery_items WHERE tenant_id=@tenant UNION ALL
  SELECT 'purchase_orders', COUNT(*) FROM purchase_orders WHERE tenant_id=@tenant UNION ALL
  SELECT 'purchase_receipts', COUNT(*) FROM purchase_receipts WHERE tenant_id=@tenant UNION ALL
  SELECT 'purchase_receipt_items', COUNT(*) FROM purchase_receipt_items WHERE tenant_id=@tenant UNION ALL
  SELECT 'expenses', COUNT(*) FROM expenses WHERE tenant_id=@tenant UNION ALL
  SELECT 'collections', COUNT(*) FROM collections WHERE tenant_id=@tenant UNION ALL
  SELECT 'stock_ledger', COUNT(*) FROM stock_ledger WHERE tenant_id=@tenant UNION ALL
  SELECT 'item_stock', COUNT(*) FROM item_stock WHERE tenant_id=@tenant UNION ALL
  SELECT 'partner_balance', COUNT(*) FROM partner_balance WHERE tenant_id=@tenant
) x;

SELECT '━━━━ 총 매출·매입 ━━━━' AS report;
SELECT
  (SELECT FORMAT(SUM(total_amount+vat_amount),0) FROM sales_deliveries WHERE tenant_id=@tenant AND status='confirmed') AS total_sales,
  (SELECT FORMAT(SUM(total_amount+vat_amount),0) FROM purchase_receipts WHERE tenant_id=@tenant AND status='confirmed') AS total_purchase,
  (SELECT FORMAT(SUM(amount),0) FROM expenses WHERE tenant_id=@tenant) AS total_expense;
