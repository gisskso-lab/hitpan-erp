SET @elec_tenant = 'tenant-elec0-b000-bbbb-bbbbbbbbbbbb';
SET @wh = 'wh-elec0-main-0000-bbbbbbbbbbbbbbbb';
SET SESSION max_recursive_iterations = 1000;

-- ====== STEP E: Opening 재고 ======
-- 원자재 1000·반제품 200·완제품 100 (전자조립은 부품 수 많아서 재고 높게)
INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
SELECT UUID(), @elec_tenant, i.item_id, @wh,
  CASE i.item_type WHEN 'material' THEN 1000 WHEN 'assembly' THEN 200 ELSE 100 END,
  i.cost_price, NOW(6)
FROM items i WHERE i.tenant_id=@elec_tenant;

INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo, created_at)
SELECT @elec_tenant, i.item_id, @wh, '2021-01-01', '2021-01', 'in', 'opening', UUID(), 'OPEN-ELEC',
  CASE i.item_type WHEN 'material' THEN 1000 WHEN 'assembly' THEN 200 ELSE 100 END,
  0, i.cost_price,
  (CASE i.item_type WHEN 'material' THEN 1000 WHEN 'assembly' THEN 200 ELSE 100 END) * i.cost_price,
  'opening inventory (elec seed)', '2021-01-01 00:00:00.000'
FROM items i WHERE i.tenant_id=@elec_tenant;

-- ====== STEP F: 5년치 거래 ======
DROP TEMPORARY TABLE IF EXISTS tmp_months;
CREATE TEMPORARY TABLE tmp_months (ym_start DATE);
INSERT INTO tmp_months
WITH RECURSIVE m AS (
  SELECT DATE('2021-02-01') d
  UNION ALL SELECT d + INTERVAL 1 MONTH FROM m WHERE d + INTERVAL 1 MONTH < '2026-08-01'
) SELECT d FROM m;

DROP TEMPORARY TABLE IF EXISTS tmp_seq8;
CREATE TEMPORARY TABLE tmp_seq8 (n INT);
INSERT INTO tmp_seq8 VALUES (1),(2),(3),(4),(5),(6),(7),(8);

DROP TEMPORARY TABLE IF EXISTS tmp_seq15;
CREATE TEMPORARY TABLE tmp_seq15 (n INT);
INSERT INTO tmp_seq15 VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12),(13),(14),(15);

-- 매입 PO (월 8건 × 66개월 = 528)
INSERT INTO purchase_orders (po_id, tenant_id, po_no, partner_id, employee_id, po_date, expected_date, status, total_amount, vat_amount, memo, created_at, updated_at)
SELECT UUID(), @elec_tenant,
  CONCAT('EPO', DATE_FORMAT(m.ym_start, '%y%m'), LPAD(s.n, 3, '0')),
  CASE MOD(CRC32(CONCAT(m.ym_start, s.n)), 10)
    WHEN 0 THEN 'pe-elec0-supp-0001-bbbbbbbbbbbb'
    WHEN 1 THEN 'pe-elec0-supp-0002-bbbbbbbbbbbb'
    WHEN 2 THEN 'pe-elec0-supp-0003-bbbbbbbbbbbb'
    WHEN 3 THEN 'pe-elec0-supp-0004-bbbbbbbbbbbb'
    WHEN 4 THEN 'pe-elec0-supp-0005-bbbbbbbbbbbb'
    WHEN 5 THEN 'pe-elec0-supp-0006-bbbbbbbbbbbb'
    WHEN 6 THEN 'pe-elec0-supp-0007-bbbbbbbbbbbb'
    WHEN 7 THEN 'pe-elec0-out0-0001-bbbbbbbbbbbb'
    WHEN 8 THEN 'pe-elec0-out0-0002-bbbbbbbbbbbb'
    ELSE 'pe-elec0-out0-0003-bbbbbbbbbbbb'
  END,
  'em-elec0-0003-bbbbbbbbbbbbbbbbbbbb',
  DATE_ADD(m.ym_start, INTERVAL (s.n * 3) DAY),
  DATE_ADD(m.ym_start, INTERVAL (s.n * 3 + 5) DAY),
  'received', 0, 0, 'elec PO seed', NOW(6), NOW(6)
FROM tmp_months m CROSS JOIN tmp_seq8 s;

-- po_items: 원자재 20종 중 하나
INSERT INTO purchase_order_items (po_item_id, po_id, tenant_id, item_id, ordered_qty, received_qty, unit_price, supply_amount, vat_amount, warehouse_id, item_status)
SELECT UUID(), po.po_id, po.tenant_id,
  CONCAT('ie-elec0-mat-', LPAD(MOD(CRC32(po.po_id), 20)+1, 4, '0'), '-bbbbbbbbbbbbbb'),
  100 + MOD(CRC32(CONCAT(po.po_id, 'q')), 500),
  100 + MOD(CRC32(CONCAT(po.po_id, 'q')), 500),
  0, 0, 0, @wh, 'received'
FROM purchase_orders po WHERE po.tenant_id=@elec_tenant;

-- unit_price: 해당 item의 cost_price × 월별 변동 ±15% (칩 단가 등락 시뮬)
UPDATE purchase_order_items poi
JOIN purchase_orders po ON poi.po_id = po.po_id
JOIN items i ON poi.item_id = i.item_id
SET poi.unit_price = ROUND(i.cost_price * (0.85 + (MOD(CRC32(CONCAT(po.po_date, poi.po_item_id)), 30) / 100.0)), 2),
    poi.supply_amount = (100 + MOD(CRC32(CONCAT(poi.po_id, 'q')), 500)) * ROUND(i.cost_price * (0.85 + (MOD(CRC32(CONCAT(po.po_date, poi.po_item_id)), 30) / 100.0)), 2)
WHERE po.tenant_id=@elec_tenant;

UPDATE purchase_order_items poi
SET poi.supply_amount = poi.ordered_qty * poi.unit_price,
    poi.vat_amount = ROUND(poi.ordered_qty * poi.unit_price * 0.10);

UPDATE purchase_orders po
JOIN (SELECT po_id, SUM(supply_amount) s, SUM(vat_amount) v FROM purchase_order_items GROUP BY po_id) x USING(po_id)
SET po.total_amount = x.s, po.vat_amount = x.v
WHERE po.tenant_id=@elec_tenant;

-- receipts
INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, po_id, partner_id, receipt_date, source_type, status, total_amount, vat_amount, memo, created_at)
SELECT UUID(), po.tenant_id,
  CONCAT('ERC', DATE_FORMAT(po.po_date, '%y%m'), LPAD(ROW_NUMBER() OVER (ORDER BY po.po_date, po.po_id), 4, '0')),
  po.po_id, po.partner_id,
  DATE_ADD(po.po_date, INTERVAL 3 DAY),
  'purchase_order', 'confirmed', po.total_amount, po.vat_amount,
  'elec receipt', NOW(6)
FROM purchase_orders po WHERE po.tenant_id=@elec_tenant;

INSERT INTO purchase_receipt_items (receipt_item_id, receipt_id, tenant_id, po_item_id, item_id, warehouse_id, qty, unit_price, supply_amount, vat_amount)
SELECT UUID(), pr.receipt_id, pr.tenant_id, poi.po_item_id, poi.item_id, @wh,
  poi.ordered_qty, poi.unit_price, poi.supply_amount, poi.vat_amount
FROM purchase_receipts pr JOIN purchase_order_items poi USING(po_id) WHERE pr.tenant_id=@elec_tenant;

-- stock_ledger: 매입 입고
INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, partner_id, ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo, created_at)
SELECT pr.tenant_id, pri.item_id, @wh, pr.partner_id,
  pr.receipt_date, DATE_FORMAT(pr.receipt_date, '%Y-%m'),
  'in', 'purchase_receipt', pri.receipt_item_id, pr.receipt_no,
  pri.qty, 0, pri.unit_price, pri.supply_amount,
  'elec receipt ledger', NOW(6)
FROM purchase_receipts pr JOIN purchase_receipt_items pri USING(receipt_id) WHERE pr.tenant_id=@elec_tenant;

-- 매출 SO (월 15건 × 66 = 990)
INSERT INTO sales_orders (order_id, tenant_id, order_no, partner_id, employee_id, order_date, delivery_date, status, total_amount, vat_amount, memo, created_at, updated_at)
SELECT UUID(), @elec_tenant,
  CONCAT('ESO', DATE_FORMAT(m.ym_start, '%y%m'), LPAD(s.n, 3, '0')),
  CASE MOD(CRC32(CONCAT(m.ym_start, s.n, 'so')), 8)
    WHEN 0 THEN 'pe-elec0-cust-0001-bbbbbbbbbbbb'
    WHEN 1 THEN 'pe-elec0-cust-0002-bbbbbbbbbbbb'
    WHEN 2 THEN 'pe-elec0-cust-0003-bbbbbbbbbbbb'
    WHEN 3 THEN 'pe-elec0-cust-0004-bbbbbbbbbbbb'
    WHEN 4 THEN 'pe-elec0-cust-0005-bbbbbbbbbbbb'
    WHEN 5 THEN 'pe-elec0-cust-0006-bbbbbbbbbbbb'
    WHEN 6 THEN 'pe-elec0-cust-0007-bbbbbbbbbbbb'
    ELSE 'pe-elec0-cust-0008-bbbbbbbbbbbb'
  END,
  CASE MOD(CRC32(CONCAT(m.ym_start, s.n, 'emp')), 2) WHEN 0 THEN 'em-elec0-0005-bbbbbbbbbbbbbbbbbbbb' ELSE 'em-elec0-0006-bbbbbbbbbbbbbbbbbbbb' END,
  DATE_ADD(m.ym_start, INTERVAL (s.n * 2) DAY),
  DATE_ADD(m.ym_start, INTERVAL (s.n * 2 + 3) DAY),
  'invoiced', 0, 0, 'elec SO', NOW(6), NOW(6)
FROM tmp_months m CROSS JOIN tmp_seq15 s;

-- so_items: 완제품 18종 중
INSERT INTO sales_order_items (order_item_id, order_id, tenant_id, item_id, ordered_qty, delivered_qty, unit_price, supply_amount, vat_amount, item_status)
SELECT UUID(), so.order_id, so.tenant_id,
  CONCAT('ie-elec0-fin0-', LPAD(MOD(CRC32(so.order_id), 18)+1, 4, '0'), '-bbbbbbbbbbbbb'),
  10 + MOD(CRC32(CONCAT(so.order_id, 'q')), 30),
  10 + MOD(CRC32(CONCAT(so.order_id, 'q')), 30),
  0, 0, 0, 'delivered'
FROM sales_orders so WHERE so.tenant_id=@elec_tenant;

UPDATE sales_order_items soi
JOIN sales_orders so ON soi.order_id=so.order_id
JOIN items i ON soi.item_id=i.item_id
JOIN partners p ON so.partner_id=p.partner_id
SET soi.unit_price = i.std_price,
    soi.supply_amount = soi.ordered_qty * i.std_price,
    soi.vat_amount = CASE p.vat_handling WHEN 'standard' THEN ROUND(soi.ordered_qty * i.std_price * 0.10) ELSE 0 END
WHERE so.tenant_id=@elec_tenant;

UPDATE sales_orders so
JOIN (SELECT order_id, SUM(supply_amount) s, SUM(vat_amount) v FROM sales_order_items GROUP BY order_id) x USING(order_id)
SET so.total_amount=x.s, so.vat_amount=x.v
WHERE so.tenant_id=@elec_tenant;

-- deliveries + items
INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, order_id, partner_id, employee_id, delivery_date, source_type, status, total_amount, vat_amount, memo, created_at, created_by, updated_at)
SELECT UUID(), so.tenant_id,
  CONCAT('EDL', DATE_FORMAT(so.delivery_date, '%y%m'), LPAD(ROW_NUMBER() OVER (ORDER BY so.delivery_date, so.order_id), 4, '0')),
  so.order_id, so.partner_id, so.employee_id,
  so.delivery_date, 'sales_order', 'confirmed',
  so.total_amount, so.vat_amount, 'elec delivery', NOW(6), so.employee_id, NOW(6)
FROM sales_orders so WHERE so.tenant_id=@elec_tenant;

INSERT INTO sales_delivery_items (delivery_item_id, delivery_id, tenant_id, order_item_id, item_id, warehouse_id, qty, unit_price, supply_amount, vat_amount)
SELECT UUID(), sd.delivery_id, sd.tenant_id, soi.order_item_id, soi.item_id, @wh,
  soi.ordered_qty, soi.unit_price, soi.supply_amount, soi.vat_amount
FROM sales_deliveries sd JOIN sales_order_items soi USING(order_id) WHERE sd.tenant_id=@elec_tenant;

-- production 원장 (완제품 생산)
INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo, created_at)
SELECT @elec_tenant, sdi.item_id, @wh,
  DATE_SUB(sd.delivery_date, INTERVAL 2 DAY),
  DATE_FORMAT(DATE_SUB(sd.delivery_date, INTERVAL 2 DAY), '%Y-%m'),
  'in', 'production', UUID(), CONCAT('PRD-', sd.delivery_no),
  sdi.qty, 0,
  (SELECT cost_price FROM items WHERE item_id=sdi.item_id),
  sdi.qty * COALESCE((SELECT cost_price FROM items WHERE item_id=sdi.item_id), 0),
  'elec production', NOW(6)
FROM sales_deliveries sd JOIN sales_delivery_items sdi USING(delivery_id) WHERE sd.tenant_id=@elec_tenant;

-- 매출 출고 원장
INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, partner_id, ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo, created_at)
SELECT sd.tenant_id, sdi.item_id, @wh, sd.partner_id,
  sd.delivery_date, DATE_FORMAT(sd.delivery_date, '%Y-%m'),
  'out', 'sales_delivery', sdi.delivery_item_id, sd.delivery_no,
  0, sdi.qty, sdi.unit_price, sdi.supply_amount,
  'elec delivery ledger', NOW(6)
FROM sales_deliveries sd JOIN sales_delivery_items sdi USING(delivery_id) WHERE sd.tenant_id=@elec_tenant;

-- item_stock 재계산
UPDATE item_stock s
JOIN (SELECT tenant_id, item_id, warehouse_id, SUM(qty_in)-SUM(qty_out) net FROM stock_ledger WHERE tenant_id=@elec_tenant GROUP BY tenant_id, item_id, warehouse_id) c USING(tenant_id, item_id, warehouse_id)
SET s.current_qty = c.net;

-- ====== 특화: material_price_history (칩 단가 등락 이력) ======
INSERT INTO material_price_history (history_id, tenant_id, item_id, ym, avg_price, min_price, max_price, change_rate, source, memo, created_at)
SELECT UUID(), @elec_tenant, poi.item_id, DATE_FORMAT(po.po_date, '%Y-%m'),
  AVG(poi.unit_price), MIN(poi.unit_price), MAX(poi.unit_price),
  0, 'auto', 'monthly avg from PO', NOW(6)
FROM purchase_order_items poi JOIN purchase_orders po USING(po_id)
WHERE po.tenant_id=@elec_tenant
GROUP BY poi.item_id, DATE_FORMAT(po.po_date, '%Y-%m');

-- change_rate 계산 (전월 대비 %)
UPDATE material_price_history mph1
JOIN material_price_history mph2
  ON mph1.tenant_id=mph2.tenant_id
  AND mph1.item_id=mph2.item_id
  AND mph2.ym = DATE_FORMAT(DATE_SUB(STR_TO_DATE(CONCAT(mph1.ym,'-01'),'%Y-%m-%d'), INTERVAL 1 MONTH), '%Y-%m')
SET mph1.change_rate = ROUND((mph1.avg_price - mph2.avg_price) / mph2.avg_price * 100, 2)
WHERE mph1.tenant_id=@elec_tenant;

-- ====== STEP G: 집계 (collections/payments/partner_balance/monthly_closing) ======
INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, collection_method, ref_doc_type, memo, created_at, updated_at)
SELECT UUID(), @elec_tenant, sd.partner_id,
  DATE_ADD(LAST_DAY(sd.delivery_date), INTERVAL 15 DAY),
  ROUND((sd.total_amount+sd.vat_amount) * 0.70, 0),
  'bank', 'sales_delivery', 'elec collection', NOW(6), NOW(6)
FROM sales_deliveries sd WHERE sd.tenant_id=@elec_tenant AND sd.is_deleted=0;

INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, payment_method, memo, created_at, updated_at)
SELECT UUID(), @elec_tenant, pr.partner_id, 'general',
  ROUND((pr.total_amount+pr.vat_amount) * 0.80, 0),
  DATE_ADD(LAST_DAY(pr.receipt_date), INTERVAL 10 DAY),
  'bank', 'elec payment', NOW(6), NOW(6)
FROM purchase_receipts pr WHERE pr.tenant_id=@elec_tenant;

INSERT INTO partner_balance (balance_id, tenant_id, partner_id, total_sales, total_receipt, total_purchase, total_payment, last_updated_at)
SELECT UUID(), @elec_tenant, p.partner_id,
  COALESCE(s.v,0), COALESCE(c.v,0), COALESCE(pu.v,0), COALESCE(pm.v,0), NOW(6)
FROM partners p
LEFT JOIN (SELECT partner_id, SUM(total_amount+vat_amount) v FROM sales_deliveries WHERE tenant_id=@elec_tenant AND is_deleted=0 GROUP BY partner_id) s ON p.partner_id=s.partner_id
LEFT JOIN (SELECT partner_id, SUM(amount) v FROM collections WHERE tenant_id=@elec_tenant GROUP BY partner_id) c ON p.partner_id=c.partner_id
LEFT JOIN (SELECT partner_id, SUM(total_amount+vat_amount) v FROM purchase_receipts WHERE tenant_id=@elec_tenant GROUP BY partner_id) pu ON p.partner_id=pu.partner_id
LEFT JOIN (SELECT partner_id, SUM(amount) v FROM payments WHERE tenant_id=@elec_tenant GROUP BY partner_id) pm ON p.partner_id=pm.partner_id
WHERE p.tenant_id=@elec_tenant;

INSERT INTO monthly_closing (closing_id, tenant_id, `year_month`, status, sales_amount, purchase_amount, receipt_amount, payment_amount, memo)
SELECT UUID(), @elec_tenant, months.ym,
  CASE WHEN months.ym < DATE_FORMAT(CURDATE() - INTERVAL 2 MONTH, '%Y%m') THEN 'closed' ELSE 'open' END,
  COALESCE(s.v,0), COALESCE(p.v,0), COALESCE(c.v,0), COALESCE(pmt.v,0), 'elec monthly'
FROM (SELECT DATE_FORMAT(DATE('2021-01-01') + INTERVAL n MONTH, '%Y%m') ym
  FROM (SELECT a.N + b.N*10 n FROM (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) a, (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6) b) x WHERE n < 67
) months
LEFT JOIN (SELECT DATE_FORMAT(delivery_date,'%Y%m') ym, SUM(total_amount+vat_amount) v FROM sales_deliveries WHERE tenant_id=@elec_tenant AND is_deleted=0 GROUP BY ym) s USING(ym)
LEFT JOIN (SELECT DATE_FORMAT(receipt_date,'%Y%m') ym, SUM(total_amount+vat_amount) v FROM purchase_receipts WHERE tenant_id=@elec_tenant GROUP BY ym) p USING(ym)
LEFT JOIN (SELECT DATE_FORMAT(collection_date,'%Y%m') ym, SUM(amount) v FROM collections WHERE tenant_id=@elec_tenant GROUP BY ym) c USING(ym)
LEFT JOIN (SELECT DATE_FORMAT(payment_date,'%Y%m') ym, SUM(amount) v FROM payments WHERE tenant_id=@elec_tenant GROUP BY ym) pmt USING(ym);

DROP TEMPORARY TABLE tmp_months;
DROP TEMPORARY TABLE tmp_seq8;
DROP TEMPORARY TABLE tmp_seq15;

SELECT
  (SELECT COUNT(*) FROM purchase_orders WHERE tenant_id=@elec_tenant) po,
  (SELECT COUNT(*) FROM sales_orders WHERE tenant_id=@elec_tenant) so,
  (SELECT COUNT(*) FROM sales_deliveries WHERE tenant_id=@elec_tenant) sd,
  (SELECT COUNT(*) FROM stock_ledger WHERE tenant_id=@elec_tenant) ledger,
  (SELECT COUNT(*) FROM material_price_history WHERE tenant_id=@elec_tenant) price_hist,
  (SELECT COUNT(*) FROM collections WHERE tenant_id=@elec_tenant) coll,
  (SELECT COUNT(*) FROM partner_balance WHERE tenant_id=@elec_tenant) balance,
  (SELECT COUNT(*) FROM monthly_closing WHERE tenant_id=@elec_tenant) closing;
