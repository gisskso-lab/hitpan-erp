SET @metal_tenant = 'tenant-metal-a000-aaaa-aaaaaaaaaaaa';
SET @wh = 'wh-metal-main-0000-aaaaaaaaaaaaaaaa';
-- MariaDB uses max_recursive_iterations, not cte_max_recursion_depth
SET SESSION max_recursive_iterations = 1000;

-- =====================================================================
-- 5년치 거래 생성 (2021-02 ~ 2026-07, 66개월)
-- PO: 월 5건 = 330 / Delivery: 월 10건 = 660
-- =====================================================================

-- 임시 테이블로 날짜 시퀀스
DROP TEMPORARY TABLE IF EXISTS tmp_months;
CREATE TEMPORARY TABLE tmp_months (ym_start DATE);
INSERT INTO tmp_months
WITH RECURSIVE m AS (
  SELECT DATE('2021-02-01') AS d
  UNION ALL SELECT d + INTERVAL 1 MONTH FROM m WHERE d + INTERVAL 1 MONTH < '2026-08-01'
)
SELECT d FROM m;

DROP TEMPORARY TABLE IF EXISTS tmp_seq5;
CREATE TEMPORARY TABLE tmp_seq5 (n INT);
INSERT INTO tmp_seq5 VALUES (1),(2),(3),(4),(5);

DROP TEMPORARY TABLE IF EXISTS tmp_seq10;
CREATE TEMPORARY TABLE tmp_seq10 (n INT);
INSERT INTO tmp_seq10 VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10);

-- ============ 매입: purchase_orders ============
INSERT INTO purchase_orders (po_id, tenant_id, po_no, partner_id, employee_id, po_date, expected_date, status, total_amount, vat_amount, memo, created_at, updated_at)
SELECT UUID(), @metal_tenant,
  CONCAT('PO', DATE_FORMAT(m.ym_start, '%y%m'), LPAD(s.n, 3, '0')),
  -- 공급사 3 + 외주 2 = 5곳 중 CRC32 분배
  CASE MOD(CRC32(CONCAT(m.ym_start, s.n)), 5)
    WHEN 0 THEN 'pm-metal-supp-0001-aaaaaaaaaaaa'
    WHEN 1 THEN 'pm-metal-supp-0002-aaaaaaaaaaaa'
    WHEN 2 THEN 'pm-metal-supp-0003-aaaaaaaaaaaa'
    WHEN 3 THEN 'pm-metal-out0-0001-aaaaaaaaaaaa'
    ELSE 'pm-metal-out0-0002-aaaaaaaaaaaa'
  END,
  'em-metal-0002-aaaaaaaaaaaaaaaaaaaa',  -- 박부장
  DATE_ADD(m.ym_start, INTERVAL (s.n * 5) DAY),
  DATE_ADD(m.ym_start, INTERVAL (s.n * 5 + 7) DAY),
  'received', 0, 0, 'auto-seed PO', NOW(6), NOW(6)
FROM tmp_months m CROSS JOIN tmp_seq5 s;

-- ============ purchase_order_items (1 item per PO) ============
-- 원자재 12종 중 랜덤 선택
INSERT INTO purchase_order_items (po_item_id, po_id, tenant_id, item_id, ordered_qty, received_qty, unit_price, supply_amount, vat_amount, warehouse_id, item_status)
SELECT UUID(), po.po_id, po.tenant_id,
  -- 원자재 12종 중 CRC32 분배
  CASE MOD(CRC32(po.po_id), 12)
    WHEN 0 THEN 'im-metal-mat-0001-aaaaaaaaaaaaaa'
    WHEN 1 THEN 'im-metal-mat-0002-aaaaaaaaaaaaaa'
    WHEN 2 THEN 'im-metal-mat-0003-aaaaaaaaaaaaaa'
    WHEN 3 THEN 'im-metal-mat-0004-aaaaaaaaaaaaaa'
    WHEN 4 THEN 'im-metal-mat-0005-aaaaaaaaaaaaaa'
    WHEN 5 THEN 'im-metal-mat-0006-aaaaaaaaaaaaaa'
    WHEN 6 THEN 'im-metal-mat-0007-aaaaaaaaaaaaaa'
    WHEN 7 THEN 'im-metal-mat-0008-aaaaaaaaaaaaaa'
    WHEN 8 THEN 'im-metal-mat-0009-aaaaaaaaaaaaaa'
    WHEN 9 THEN 'im-metal-mat-0010-aaaaaaaaaaaaaa'
    WHEN 10 THEN 'im-metal-mat-0011-aaaaaaaaaaaaaa'
    ELSE 'im-metal-mat-0012-aaaaaaaaaaaaaa'
  END AS mat_id,
  -- 수량: 50~200
  50 + MOD(CRC32(CONCAT(po.po_id, 'q')), 150) AS qty,
  50 + MOD(CRC32(CONCAT(po.po_id, 'q')), 150),
  -- 단가: 각 원자재 cost_price 기준 ±5%
  (SELECT cost_price FROM items WHERE item_id = (
    CASE MOD(CRC32(po.po_id), 12)
      WHEN 0 THEN 'im-metal-mat-0001-aaaaaaaaaaaaaa'
      WHEN 1 THEN 'im-metal-mat-0002-aaaaaaaaaaaaaa'
      WHEN 2 THEN 'im-metal-mat-0003-aaaaaaaaaaaaaa'
      WHEN 3 THEN 'im-metal-mat-0004-aaaaaaaaaaaaaa'
      WHEN 4 THEN 'im-metal-mat-0005-aaaaaaaaaaaaaa'
      WHEN 5 THEN 'im-metal-mat-0006-aaaaaaaaaaaaaa'
      WHEN 6 THEN 'im-metal-mat-0007-aaaaaaaaaaaaaa'
      WHEN 7 THEN 'im-metal-mat-0008-aaaaaaaaaaaaaa'
      WHEN 8 THEN 'im-metal-mat-0009-aaaaaaaaaaaaaa'
      WHEN 9 THEN 'im-metal-mat-0010-aaaaaaaaaaaaaa'
      WHEN 10 THEN 'im-metal-mat-0011-aaaaaaaaaaaaaa'
      ELSE 'im-metal-mat-0012-aaaaaaaaaaaaaa'
    END
  )) AS unit_price,
  0, 0, @wh, 'received'
FROM purchase_orders po
WHERE po.tenant_id = @metal_tenant;

-- supply/vat 재계산
UPDATE purchase_order_items poi
SET poi.supply_amount = poi.ordered_qty * poi.unit_price,
    poi.vat_amount = ROUND(poi.ordered_qty * poi.unit_price * 0.10);

-- PO 헤더 금액 집계
UPDATE purchase_orders po
JOIN (SELECT po_id, SUM(supply_amount) s, SUM(vat_amount) v FROM purchase_order_items GROUP BY po_id) x USING(po_id)
SET po.total_amount = x.s, po.vat_amount = x.v
WHERE po.tenant_id = @metal_tenant;

-- ============ purchase_receipts (입고 완료) ============
INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, po_id, partner_id, receipt_date, source_type, status, total_amount, vat_amount, memo, created_at)
SELECT UUID(), po.tenant_id,
  CONCAT('RC', DATE_FORMAT(po.po_date, '%y%m'), LPAD(ROW_NUMBER() OVER (ORDER BY po.po_date, po.po_id), 4, '0')),
  po.po_id, po.partner_id,
  DATE_ADD(po.po_date, INTERVAL 3 DAY),
  'purchase_order', 'confirmed',
  po.total_amount, po.vat_amount,
  'auto-seed receipt', NOW(6)
FROM purchase_orders po WHERE po.tenant_id = @metal_tenant;

-- purchase_receipt_items
INSERT INTO purchase_receipt_items (receipt_item_id, receipt_id, tenant_id, po_item_id, item_id, warehouse_id, qty, unit_price, supply_amount, vat_amount)
SELECT UUID(), pr.receipt_id, pr.tenant_id, poi.po_item_id, poi.item_id, @wh,
  poi.ordered_qty, poi.unit_price, poi.supply_amount, poi.vat_amount
FROM purchase_receipts pr
JOIN purchase_order_items poi ON pr.po_id = poi.po_id
WHERE pr.tenant_id = @metal_tenant;

-- stock_ledger: 매입 입고 기록
INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, partner_id, ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo, created_at)
SELECT pr.tenant_id, pri.item_id, @wh, pr.partner_id,
  pr.receipt_date, DATE_FORMAT(pr.receipt_date, '%Y-%m'),
  'in', 'purchase_receipt', pri.receipt_item_id, pr.receipt_no,
  pri.qty, 0, pri.unit_price, pri.supply_amount,
  'auto-seed ledger (in)', NOW(6)
FROM purchase_receipts pr
JOIN purchase_receipt_items pri ON pr.receipt_id = pri.receipt_id
WHERE pr.tenant_id = @metal_tenant;

-- ============ sales_orders ============
INSERT INTO sales_orders (order_id, tenant_id, order_no, partner_id, employee_id, order_date, delivery_date, status, total_amount, vat_amount, memo, created_at, updated_at)
SELECT UUID(), @metal_tenant,
  CONCAT('SO', DATE_FORMAT(m.ym_start, '%y%m'), LPAD(s.n, 3, '0')),
  CASE MOD(CRC32(CONCAT(m.ym_start, s.n, 'so')), 7)
    WHEN 0 THEN 'pm-metal-cust-0001-aaaaaaaaaaaa'
    WHEN 1 THEN 'pm-metal-cust-0002-aaaaaaaaaaaa'
    WHEN 2 THEN 'pm-metal-cust-0003-aaaaaaaaaaaa'
    WHEN 3 THEN 'pm-metal-cust-0004-aaaaaaaaaaaa'
    WHEN 4 THEN 'pm-metal-cust-0005-aaaaaaaaaaaa'
    WHEN 5 THEN 'pm-metal-cust-0006-aaaaaaaaaaaa'
    ELSE 'pm-metal-cust-0007-aaaaaaaaaaaa'
  END,
  CASE MOD(CRC32(CONCAT(m.ym_start, s.n, 'emp')), 2) WHEN 0 THEN 'em-metal-0004-aaaaaaaaaaaaaaaaaaaa' ELSE 'em-metal-0005-aaaaaaaaaaaaaaaaaaaa' END,
  DATE_ADD(m.ym_start, INTERVAL (s.n * 3) DAY),
  DATE_ADD(m.ym_start, INTERVAL (s.n * 3 + 5) DAY),
  'invoiced', 0, 0, 'auto-seed SO', NOW(6), NOW(6)
FROM tmp_months m CROSS JOIN tmp_seq10 s;

-- sales_order_items: 완제품 15종 중 랜덤
INSERT INTO sales_order_items (order_item_id, order_id, tenant_id, item_id, ordered_qty, delivered_qty, unit_price, supply_amount, vat_amount, item_status)
SELECT UUID(), so.order_id, so.tenant_id,
  CASE MOD(CRC32(so.order_id), 15)
    WHEN 0 THEN 'im-metal-fin0-0001-aaaaaaaaaaaaa'
    WHEN 1 THEN 'im-metal-fin0-0002-aaaaaaaaaaaaa'
    WHEN 2 THEN 'im-metal-fin0-0003-aaaaaaaaaaaaa'
    WHEN 3 THEN 'im-metal-fin0-0004-aaaaaaaaaaaaa'
    WHEN 4 THEN 'im-metal-fin0-0005-aaaaaaaaaaaaa'
    WHEN 5 THEN 'im-metal-fin0-0006-aaaaaaaaaaaaa'
    WHEN 6 THEN 'im-metal-fin0-0007-aaaaaaaaaaaaa'
    WHEN 7 THEN 'im-metal-fin0-0008-aaaaaaaaaaaaa'
    WHEN 8 THEN 'im-metal-fin0-0009-aaaaaaaaaaaaa'
    WHEN 9 THEN 'im-metal-fin0-0010-aaaaaaaaaaaaa'
    WHEN 10 THEN 'im-metal-fin0-0011-aaaaaaaaaaaaa'
    WHEN 11 THEN 'im-metal-fin0-0012-aaaaaaaaaaaaa'
    WHEN 12 THEN 'im-metal-fin0-0013-aaaaaaaaaaaaa'
    WHEN 13 THEN 'im-metal-fin0-0014-aaaaaaaaaaaaa'
    ELSE 'im-metal-fin0-0015-aaaaaaaaaaaaa'
  END,
  5 + MOD(CRC32(CONCAT(so.order_id, 'q')), 20),
  5 + MOD(CRC32(CONCAT(so.order_id, 'q')), 20),
  (SELECT std_price FROM items WHERE item_id = (
    CASE MOD(CRC32(so.order_id), 15)
      WHEN 0 THEN 'im-metal-fin0-0001-aaaaaaaaaaaaa'
      WHEN 1 THEN 'im-metal-fin0-0002-aaaaaaaaaaaaa'
      WHEN 2 THEN 'im-metal-fin0-0003-aaaaaaaaaaaaa'
      WHEN 3 THEN 'im-metal-fin0-0004-aaaaaaaaaaaaa'
      WHEN 4 THEN 'im-metal-fin0-0005-aaaaaaaaaaaaa'
      WHEN 5 THEN 'im-metal-fin0-0006-aaaaaaaaaaaaa'
      WHEN 6 THEN 'im-metal-fin0-0007-aaaaaaaaaaaaa'
      WHEN 7 THEN 'im-metal-fin0-0008-aaaaaaaaaaaaa'
      WHEN 8 THEN 'im-metal-fin0-0009-aaaaaaaaaaaaa'
      WHEN 9 THEN 'im-metal-fin0-0010-aaaaaaaaaaaaa'
      WHEN 10 THEN 'im-metal-fin0-0011-aaaaaaaaaaaaa'
      WHEN 11 THEN 'im-metal-fin0-0012-aaaaaaaaaaaaa'
      WHEN 12 THEN 'im-metal-fin0-0013-aaaaaaaaaaaaa'
      WHEN 13 THEN 'im-metal-fin0-0014-aaaaaaaaaaaaa'
      ELSE 'im-metal-fin0-0015-aaaaaaaaaaaaa'
    END
  )),
  0, 0, 'delivered'
FROM sales_orders so WHERE so.tenant_id = @metal_tenant;

-- supply/vat 계산 (거래처 vat_handling 반영)
UPDATE sales_order_items soi
JOIN sales_orders so ON soi.order_id = so.order_id
JOIN partners p ON so.partner_id = p.partner_id
SET soi.supply_amount = soi.ordered_qty * soi.unit_price,
    soi.vat_amount = CASE p.vat_handling WHEN 'standard' THEN ROUND(soi.ordered_qty * soi.unit_price * 0.10) ELSE 0 END
WHERE so.tenant_id = @metal_tenant;

-- SO 헤더 집계
UPDATE sales_orders so
JOIN (SELECT order_id, SUM(supply_amount) s, SUM(vat_amount) v FROM sales_order_items GROUP BY order_id) x USING(order_id)
SET so.total_amount = x.s, so.vat_amount = x.v
WHERE so.tenant_id = @metal_tenant;

-- ============ sales_deliveries (거래명세서) ============
INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, order_id, partner_id, employee_id, delivery_date, source_type, status, total_amount, vat_amount, memo, created_at, created_by, updated_at)
SELECT UUID(), so.tenant_id,
  CONCAT('DL', DATE_FORMAT(so.delivery_date, '%y%m'), LPAD(ROW_NUMBER() OVER (ORDER BY so.delivery_date, so.order_id), 4, '0')),
  so.order_id, so.partner_id, so.employee_id,
  so.delivery_date, 'sales_order', 'confirmed',
  so.total_amount, so.vat_amount,
  'auto-seed delivery', NOW(6), so.employee_id, NOW(6)
FROM sales_orders so WHERE so.tenant_id = @metal_tenant;

-- sales_delivery_items
INSERT INTO sales_delivery_items (delivery_item_id, delivery_id, tenant_id, order_item_id, item_id, warehouse_id, qty, unit_price, supply_amount, vat_amount)
SELECT UUID(), sd.delivery_id, sd.tenant_id, soi.order_item_id, soi.item_id, @wh,
  soi.ordered_qty, soi.unit_price, soi.supply_amount, soi.vat_amount
FROM sales_deliveries sd
JOIN sales_order_items soi ON sd.order_id = soi.order_id
WHERE sd.tenant_id = @metal_tenant;

-- stock_ledger: 매출 출고 기록
INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, partner_id, ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo, created_at)
SELECT sd.tenant_id, sdi.item_id, @wh, sd.partner_id,
  sd.delivery_date, DATE_FORMAT(sd.delivery_date, '%Y-%m'),
  'out', 'sales_delivery', sdi.delivery_item_id, sd.delivery_no,
  0, sdi.qty, sdi.unit_price, sdi.supply_amount,
  'auto-seed ledger (out)', NOW(6)
FROM sales_deliveries sd
JOIN sales_delivery_items sdi ON sd.delivery_id = sdi.delivery_id
WHERE sd.tenant_id = @metal_tenant;

-- ============ item_stock 재계산 (opening + 거래 반영) ============
UPDATE item_stock s
JOIN (
  SELECT tenant_id, item_id, warehouse_id, SUM(qty_in) - SUM(qty_out) net
  FROM stock_ledger WHERE tenant_id = @metal_tenant
  GROUP BY tenant_id, item_id, warehouse_id
) c USING(tenant_id, item_id, warehouse_id)
SET s.current_qty = c.net;

-- 통계
SELECT
  (SELECT COUNT(*) FROM purchase_orders WHERE tenant_id=@metal_tenant) po,
  (SELECT COUNT(*) FROM purchase_order_items WHERE tenant_id=@metal_tenant) poi,
  (SELECT COUNT(*) FROM purchase_receipts WHERE tenant_id=@metal_tenant) rc,
  (SELECT COUNT(*) FROM sales_orders WHERE tenant_id=@metal_tenant) so,
  (SELECT COUNT(*) FROM sales_deliveries WHERE tenant_id=@metal_tenant) sd,
  (SELECT COUNT(*) FROM stock_ledger WHERE tenant_id=@metal_tenant) ledger;

DROP TEMPORARY TABLE tmp_months;
DROP TEMPORARY TABLE tmp_seq5;
DROP TEMPORARY TABLE tmp_seq10;
