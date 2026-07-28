SET @plastic_tenant = 'tenant-plastic-c0-cccc-cccccccccccc';
SET @wh = 'wh-plas0-main-0000-cccccccccccccccc';
SET SESSION max_recursive_iterations = 1000;

-- ====== 특화: mold_assets 7개 (완제품 P001~P007 전용 금형) ======
INSERT INTO mold_assets (mold_id, tenant_id, mold_code, mold_name, product_item_id, supplier_partner_id, acquisition_date, acquisition_cost, max_shots, current_shots, cycle_time_sec, status, customer_prepaid_amount, amortization_per_unit, amortized_cumulative, memo, created_at, updated_at) VALUES
('mo-plas0-0001-cccccccccccccccccccc', @plastic_tenant, 'MOLD001', '자동차 부품 A 금형', 'ip-plas0-fin0-0001-ccccccccccccc',
 'pp-plas0-supp-0004-cccccccccccc', '2021-01-15', 35000000, 800000, 0, 28, 'active', 20000000, 250, 0, '현대모비스 전용', NOW(6), NOW(6)),
('mo-plas0-0002-cccccccccccccccccccc', @plastic_tenant, 'MOLD002', '자동차 부품 B 금형', 'ip-plas0-fin0-0002-ccccccccccccc',
 'pp-plas0-supp-0004-cccccccccccc', '2021-01-20', 42000000, 800000, 0, 32, 'active', 25000000, 300, 0, '현대모비스 전용', NOW(6), NOW(6)),
('mo-plas0-0003-cccccccccccccccccccc', @plastic_tenant, 'MOLD003', '가전 하우징 금형', 'ip-plas0-fin0-0003-ccccccccccccc',
 'pp-plas0-supp-0004-cccccccccccc', '2021-02-10', 68000000, 1000000, 0, 45, 'active', 40000000, 450, 0, 'LG전자 전용', NOW(6), NOW(6)),
('mo-plas0-0004-cccccccccccccccccccc', @plastic_tenant, 'MOLD004', '정수기 부품 금형', 'ip-plas0-fin0-0004-ccccccccccccc',
 'pp-plas0-supp-0004-cccccccccccc', '2021-03-05', 28000000, 1000000, 0, 22, 'active', 15000000, 200, 0, '코웨이 전용', NOW(6), NOW(6)),
('mo-plas0-0005-cccccccccccccccccccc', @plastic_tenant, 'MOLD005', '식품용기 금형 (2개 캐비티)', 'ip-plas0-fin0-0005-ccccccccccccc',
 'pp-plas0-supp-0004-cccccccccccc', '2021-04-15', 18000000, 2000000, 0, 12, 'active', 10000000, 80, 0, '삼양사 전용', NOW(6), NOW(6)),
('mo-plas0-0006-cccccccccccccccccccc', @plastic_tenant, 'MOLD006', '완구 몸체 금형', 'ip-plas0-fin0-0006-ccccccccccccc',
 'pp-plas0-supp-0004-cccccccccccc', '2022-02-20', 22000000, 500000, 0, 25, 'active', 12000000, 180, 0, '완구A 전용', NOW(6), NOW(6)),
('mo-plas0-0007-cccccccccccccccccccc', @plastic_tenant, 'MOLD007', '포장뚜껑 금형', 'ip-plas0-fin0-0007-ccccccccccccc',
 'pp-plas0-supp-0004-cccccccccccc', '2021-06-01', 8000000, 3000000, 0, 8, 'active', 5000000, 30, 0, '포장용기업체 전용', NOW(6), NOW(6));

-- ====== Opening 재고 ======
INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
SELECT UUID(), @plastic_tenant, i.item_id, @wh,
  CASE i.item_type WHEN 'material' THEN 2000 WHEN 'assembly' THEN 300 ELSE 150 END,
  i.cost_price, NOW(6)
FROM items i WHERE i.tenant_id=@plastic_tenant;

INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo, created_at)
SELECT @plastic_tenant, i.item_id, @wh, '2021-01-01', '2021-01', 'in', 'opening', UUID(), 'OPEN-PLAS',
  CASE i.item_type WHEN 'material' THEN 2000 WHEN 'assembly' THEN 300 ELSE 150 END,
  0, i.cost_price,
  (CASE i.item_type WHEN 'material' THEN 2000 WHEN 'assembly' THEN 300 ELSE 150 END) * i.cost_price,
  'opening plastic', '2021-01-01 00:00:00.000'
FROM items i WHERE i.tenant_id=@plastic_tenant;

-- ====== 5년치 거래 (월 PO 6 + SO 12) ======
DROP TEMPORARY TABLE IF EXISTS tmp_months;
CREATE TEMPORARY TABLE tmp_months (ym_start DATE);
INSERT INTO tmp_months
WITH RECURSIVE m AS (SELECT DATE('2021-02-01') d UNION ALL SELECT d + INTERVAL 1 MONTH FROM m WHERE d + INTERVAL 1 MONTH < '2026-08-01')
SELECT d FROM m;

DROP TEMPORARY TABLE IF EXISTS tmp_seq6;
CREATE TEMPORARY TABLE tmp_seq6 (n INT);
INSERT INTO tmp_seq6 VALUES (1),(2),(3),(4),(5),(6);

DROP TEMPORARY TABLE IF EXISTS tmp_seq12;
CREATE TEMPORARY TABLE tmp_seq12 (n INT);
INSERT INTO tmp_seq12 VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12);

INSERT INTO purchase_orders (po_id, tenant_id, po_no, partner_id, employee_id, po_date, expected_date, status, total_amount, vat_amount, memo, created_at, updated_at)
SELECT UUID(), @plastic_tenant,
  CONCAT('PPO', DATE_FORMAT(m.ym_start, '%y%m'), LPAD(s.n, 3, '0')),
  CASE MOD(CRC32(CONCAT(m.ym_start, s.n)), 6)
    WHEN 0 THEN 'pp-plas0-supp-0001-cccccccccccc'
    WHEN 1 THEN 'pp-plas0-supp-0002-cccccccccccc'
    WHEN 2 THEN 'pp-plas0-supp-0003-cccccccccccc'
    WHEN 3 THEN 'pp-plas0-supp-0004-cccccccccccc'
    WHEN 4 THEN 'pp-plas0-out0-0001-cccccccccccc'
    ELSE 'pp-plas0-out0-0002-cccccccccccc'
  END,
  'em-plas0-0002-cccccccccccccccccccc',
  DATE_ADD(m.ym_start, INTERVAL (s.n * 4) DAY),
  DATE_ADD(m.ym_start, INTERVAL (s.n * 4 + 5) DAY),
  'received', 0, 0, 'plastic PO', NOW(6), NOW(6)
FROM tmp_months m CROSS JOIN tmp_seq6 s;

INSERT INTO purchase_order_items (po_item_id, po_id, tenant_id, item_id, ordered_qty, received_qty, unit_price, supply_amount, vat_amount, warehouse_id, item_status)
SELECT UUID(), po.po_id, po.tenant_id,
  CONCAT('ip-plas0-mat-', LPAD(MOD(CRC32(po.po_id), 15)+1, 4, '0'), '-cccccccccccccc'),
  200 + MOD(CRC32(CONCAT(po.po_id, 'q')), 800),
  200 + MOD(CRC32(CONCAT(po.po_id, 'q')), 800),
  0, 0, 0, @wh, 'received'
FROM purchase_orders po WHERE po.tenant_id=@plastic_tenant;

UPDATE purchase_order_items poi
JOIN items i ON poi.item_id=i.item_id
SET poi.unit_price = i.cost_price;

UPDATE purchase_order_items poi
SET poi.supply_amount = poi.ordered_qty * poi.unit_price,
    poi.vat_amount = ROUND(poi.ordered_qty * poi.unit_price * 0.10);

UPDATE purchase_orders po
JOIN (SELECT po_id, SUM(supply_amount) s, SUM(vat_amount) v FROM purchase_order_items GROUP BY po_id) x USING(po_id)
SET po.total_amount=x.s, po.vat_amount=x.v
WHERE po.tenant_id=@plastic_tenant;

INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, po_id, partner_id, receipt_date, source_type, status, total_amount, vat_amount, memo, created_at)
SELECT UUID(), po.tenant_id,
  CONCAT('PRC', DATE_FORMAT(po.po_date,'%y%m'), LPAD(ROW_NUMBER() OVER (ORDER BY po.po_date, po.po_id), 4, '0')),
  po.po_id, po.partner_id, DATE_ADD(po.po_date, INTERVAL 3 DAY),
  'purchase_order', 'confirmed', po.total_amount, po.vat_amount,
  'plastic receipt', NOW(6)
FROM purchase_orders po WHERE po.tenant_id=@plastic_tenant;

INSERT INTO purchase_receipt_items (receipt_item_id, receipt_id, tenant_id, po_item_id, item_id, warehouse_id, qty, unit_price, supply_amount, vat_amount)
SELECT UUID(), pr.receipt_id, pr.tenant_id, poi.po_item_id, poi.item_id, @wh,
  poi.ordered_qty, poi.unit_price, poi.supply_amount, poi.vat_amount
FROM purchase_receipts pr JOIN purchase_order_items poi USING(po_id) WHERE pr.tenant_id=@plastic_tenant;

INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, partner_id, ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo, created_at)
SELECT pr.tenant_id, pri.item_id, @wh, pr.partner_id,
  pr.receipt_date, DATE_FORMAT(pr.receipt_date, '%Y-%m'),
  'in', 'purchase_receipt', pri.receipt_item_id, pr.receipt_no,
  pri.qty, 0, pri.unit_price, pri.supply_amount,
  'plastic receipt ledger', NOW(6)
FROM purchase_receipts pr JOIN purchase_receipt_items pri USING(receipt_id) WHERE pr.tenant_id=@plastic_tenant;

-- 매출 (월 12건 × 66)
INSERT INTO sales_orders (order_id, tenant_id, order_no, partner_id, employee_id, order_date, delivery_date, status, total_amount, vat_amount, memo, created_at, updated_at)
SELECT UUID(), @plastic_tenant,
  CONCAT('PSO', DATE_FORMAT(m.ym_start,'%y%m'), LPAD(s.n, 3, '0')),
  CASE MOD(CRC32(CONCAT(m.ym_start, s.n, 'so')), 8)
    WHEN 0 THEN 'pp-plas0-cust-0001-cccccccccccc'
    WHEN 1 THEN 'pp-plas0-cust-0002-cccccccccccc'
    WHEN 2 THEN 'pp-plas0-cust-0003-cccccccccccc'
    WHEN 3 THEN 'pp-plas0-cust-0004-cccccccccccc'
    WHEN 4 THEN 'pp-plas0-cust-0005-cccccccccccc'
    WHEN 5 THEN 'pp-plas0-cust-0006-cccccccccccc'
    WHEN 6 THEN 'pp-plas0-cust-0007-cccccccccccc'
    ELSE 'pp-plas0-cust-0008-cccccccccccc'
  END,
  CASE MOD(CRC32(CONCAT(m.ym_start, s.n, 'emp')), 2) WHEN 0 THEN 'em-plas0-0004-cccccccccccccccccccc' ELSE 'em-plas0-0005-cccccccccccccccccccc' END,
  DATE_ADD(m.ym_start, INTERVAL (s.n * 2) DAY),
  DATE_ADD(m.ym_start, INTERVAL (s.n * 2 + 3) DAY),
  'invoiced', 0, 0, 'plastic SO', NOW(6), NOW(6)
FROM tmp_months m CROSS JOIN tmp_seq12 s;

INSERT INTO sales_order_items (order_item_id, order_id, tenant_id, item_id, ordered_qty, delivered_qty, unit_price, supply_amount, vat_amount, item_status)
SELECT UUID(), so.order_id, so.tenant_id,
  CONCAT('ip-plas0-fin0-', LPAD(MOD(CRC32(so.order_id), 15)+1, 4, '0'), '-ccccccccccccc'),
  50 + MOD(CRC32(CONCAT(so.order_id,'q')), 200),
  50 + MOD(CRC32(CONCAT(so.order_id,'q')), 200),
  0, 0, 0, 'delivered'
FROM sales_orders so WHERE so.tenant_id=@plastic_tenant;

UPDATE sales_order_items soi
JOIN sales_orders so ON soi.order_id=so.order_id
JOIN items i ON soi.item_id=i.item_id
JOIN partners p ON so.partner_id=p.partner_id
SET soi.unit_price = i.std_price,
    soi.supply_amount = soi.ordered_qty * i.std_price,
    soi.vat_amount = CASE p.vat_handling WHEN 'standard' THEN ROUND(soi.ordered_qty * i.std_price * 0.10) ELSE 0 END
WHERE so.tenant_id=@plastic_tenant;

UPDATE sales_orders so
JOIN (SELECT order_id, SUM(supply_amount) s, SUM(vat_amount) v FROM sales_order_items GROUP BY order_id) x USING(order_id)
SET so.total_amount=x.s, so.vat_amount=x.v
WHERE so.tenant_id=@plastic_tenant;

INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, order_id, partner_id, employee_id, delivery_date, source_type, status, total_amount, vat_amount, memo, created_at, created_by, updated_at)
SELECT UUID(), so.tenant_id,
  CONCAT('PDL', DATE_FORMAT(so.delivery_date,'%y%m'), LPAD(ROW_NUMBER() OVER (ORDER BY so.delivery_date, so.order_id), 4, '0')),
  so.order_id, so.partner_id, so.employee_id,
  so.delivery_date, 'sales_order', 'confirmed',
  so.total_amount, so.vat_amount, 'plastic delivery', NOW(6), so.employee_id, NOW(6)
FROM sales_orders so WHERE so.tenant_id=@plastic_tenant;

INSERT INTO sales_delivery_items (delivery_item_id, delivery_id, tenant_id, order_item_id, item_id, warehouse_id, qty, unit_price, supply_amount, vat_amount)
SELECT UUID(), sd.delivery_id, sd.tenant_id, soi.order_item_id, soi.item_id, @wh,
  soi.ordered_qty, soi.unit_price, soi.supply_amount, soi.vat_amount
FROM sales_deliveries sd JOIN sales_order_items soi USING(order_id) WHERE sd.tenant_id=@plastic_tenant;

-- production 원장 (완제품 생산)
INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo, created_at)
SELECT @plastic_tenant, sdi.item_id, @wh,
  DATE_SUB(sd.delivery_date, INTERVAL 2 DAY),
  DATE_FORMAT(DATE_SUB(sd.delivery_date, INTERVAL 2 DAY), '%Y-%m'),
  'in', 'production', UUID(), CONCAT('PRD-', sd.delivery_no),
  sdi.qty, 0, (SELECT cost_price FROM items WHERE item_id=sdi.item_id),
  sdi.qty * COALESCE((SELECT cost_price FROM items WHERE item_id=sdi.item_id), 0),
  'plastic production', NOW(6)
FROM sales_deliveries sd JOIN sales_delivery_items sdi USING(delivery_id) WHERE sd.tenant_id=@plastic_tenant;

-- 출고 원장
INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, partner_id, ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo, created_at)
SELECT sd.tenant_id, sdi.item_id, @wh, sd.partner_id,
  sd.delivery_date, DATE_FORMAT(sd.delivery_date, '%Y-%m'),
  'out', 'sales_delivery', sdi.delivery_item_id, sd.delivery_no,
  0, sdi.qty, sdi.unit_price, sdi.supply_amount,
  'plastic delivery ledger', NOW(6)
FROM sales_deliveries sd JOIN sales_delivery_items sdi USING(delivery_id) WHERE sd.tenant_id=@plastic_tenant;

-- ====== 특화: mold_production_log (P001~P007 금형 생산별 샷 카운트) ======
INSERT INTO mold_production_log (tenant_id, mold_id, production_date, shot_count, good_count, defect_count, source_type, source_id, memo, created_at)
SELECT @plastic_tenant, ma.mold_id,
  DATE_SUB(sd.delivery_date, INTERVAL 2 DAY),
  sdi.qty, FLOOR(sdi.qty * 0.98), FLOOR(sdi.qty * 0.02),
  'production', sdi.delivery_item_id, 'auto-log', NOW(6)
FROM mold_assets ma
JOIN sales_delivery_items sdi ON sdi.item_id = ma.product_item_id
JOIN sales_deliveries sd ON sdi.delivery_id=sd.delivery_id
WHERE ma.tenant_id=@plastic_tenant AND sd.tenant_id=@plastic_tenant;

-- mold_assets.current_shots = log의 shot_count 합계 동기화
UPDATE mold_assets ma
JOIN (SELECT mold_id, SUM(shot_count) s FROM mold_production_log GROUP BY mold_id) mp USING(mold_id)
SET ma.current_shots = mp.s,
    ma.amortized_cumulative = ROUND(ma.amortization_per_unit * mp.s)
WHERE ma.tenant_id=@plastic_tenant;

-- item_stock 재계산
UPDATE item_stock s
JOIN (SELECT tenant_id, item_id, warehouse_id, SUM(qty_in)-SUM(qty_out) net FROM stock_ledger WHERE tenant_id=@plastic_tenant GROUP BY tenant_id, item_id, warehouse_id) c USING(tenant_id, item_id, warehouse_id)
SET s.current_qty = c.net;

-- 집계
INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, collection_method, ref_doc_type, memo, created_at, updated_at)
SELECT UUID(), @plastic_tenant, sd.partner_id,
  DATE_ADD(LAST_DAY(sd.delivery_date), INTERVAL 15 DAY),
  ROUND((sd.total_amount+sd.vat_amount) * 0.70, 0),
  'bank', 'sales_delivery', 'plastic coll', NOW(6), NOW(6)
FROM sales_deliveries sd WHERE sd.tenant_id=@plastic_tenant AND sd.is_deleted=0;

INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, payment_method, memo, created_at, updated_at)
SELECT UUID(), @plastic_tenant, pr.partner_id, 'general',
  ROUND((pr.total_amount+pr.vat_amount) * 0.80, 0),
  DATE_ADD(LAST_DAY(pr.receipt_date), INTERVAL 10 DAY),
  'bank', 'plastic pmt', NOW(6), NOW(6)
FROM purchase_receipts pr WHERE pr.tenant_id=@plastic_tenant;

INSERT INTO partner_balance (balance_id, tenant_id, partner_id, total_sales, total_receipt, total_purchase, total_payment, last_updated_at)
SELECT UUID(), @plastic_tenant, p.partner_id,
  COALESCE(s.v,0), COALESCE(c.v,0), COALESCE(pu.v,0), COALESCE(pm.v,0), NOW(6)
FROM partners p
LEFT JOIN (SELECT partner_id, SUM(total_amount+vat_amount) v FROM sales_deliveries WHERE tenant_id=@plastic_tenant AND is_deleted=0 GROUP BY partner_id) s ON p.partner_id=s.partner_id
LEFT JOIN (SELECT partner_id, SUM(amount) v FROM collections WHERE tenant_id=@plastic_tenant GROUP BY partner_id) c ON p.partner_id=c.partner_id
LEFT JOIN (SELECT partner_id, SUM(total_amount+vat_amount) v FROM purchase_receipts WHERE tenant_id=@plastic_tenant GROUP BY partner_id) pu ON p.partner_id=pu.partner_id
LEFT JOIN (SELECT partner_id, SUM(amount) v FROM payments WHERE tenant_id=@plastic_tenant GROUP BY partner_id) pm ON p.partner_id=pm.partner_id
WHERE p.tenant_id=@plastic_tenant;

INSERT INTO monthly_closing (closing_id, tenant_id, `year_month`, status, sales_amount, purchase_amount, receipt_amount, payment_amount, memo)
SELECT UUID(), @plastic_tenant, months.ym,
  CASE WHEN months.ym < DATE_FORMAT(CURDATE() - INTERVAL 2 MONTH, '%Y%m') THEN 'closed' ELSE 'open' END,
  COALESCE(s.v,0), COALESCE(p.v,0), COALESCE(c.v,0), COALESCE(pmt.v,0), 'plastic monthly'
FROM (SELECT DATE_FORMAT(DATE('2021-01-01') + INTERVAL n MONTH, '%Y%m') ym
  FROM (SELECT a.N + b.N*10 n FROM (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) a, (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6) b) x WHERE n < 67) months
LEFT JOIN (SELECT DATE_FORMAT(delivery_date,'%Y%m') ym, SUM(total_amount+vat_amount) v FROM sales_deliveries WHERE tenant_id=@plastic_tenant AND is_deleted=0 GROUP BY ym) s USING(ym)
LEFT JOIN (SELECT DATE_FORMAT(receipt_date,'%Y%m') ym, SUM(total_amount+vat_amount) v FROM purchase_receipts WHERE tenant_id=@plastic_tenant GROUP BY ym) p USING(ym)
LEFT JOIN (SELECT DATE_FORMAT(collection_date,'%Y%m') ym, SUM(amount) v FROM collections WHERE tenant_id=@plastic_tenant GROUP BY ym) c USING(ym)
LEFT JOIN (SELECT DATE_FORMAT(payment_date,'%Y%m') ym, SUM(amount) v FROM payments WHERE tenant_id=@plastic_tenant GROUP BY ym) pmt USING(ym);

DROP TEMPORARY TABLE tmp_months;
DROP TEMPORARY TABLE tmp_seq6;
DROP TEMPORARY TABLE tmp_seq12;

SELECT
  (SELECT COUNT(*) FROM purchase_orders WHERE tenant_id=@plastic_tenant) po,
  (SELECT COUNT(*) FROM sales_orders WHERE tenant_id=@plastic_tenant) so,
  (SELECT COUNT(*) FROM sales_deliveries WHERE tenant_id=@plastic_tenant) sd,
  (SELECT COUNT(*) FROM stock_ledger WHERE tenant_id=@plastic_tenant) ledger,
  (SELECT COUNT(*) FROM mold_assets WHERE tenant_id=@plastic_tenant) molds,
  (SELECT COUNT(*) FROM mold_production_log WHERE tenant_id=@plastic_tenant) mold_logs,
  (SELECT SUM(current_shots) FROM mold_assets WHERE tenant_id=@plastic_tenant) total_shots,
  (SELECT COUNT(*) FROM partner_balance WHERE tenant_id=@plastic_tenant) balance,
  (SELECT COUNT(*) FROM monthly_closing WHERE tenant_id=@plastic_tenant) closing;
