SET @tid = '452ca266-97b9-4cd1-a0ac-2f37830c81f6';

-- ═══════════════════════════════════════
-- 임시 테이블: 매입처/매출처/상품 순번
-- ═══════════════════════════════════════
CREATE TEMPORARY TABLE tmp_sup AS SELECT partner_id, @rs:=@rs+1 AS rn FROM partners,(SELECT @rs:=0) r WHERE tenant_id=@tid AND partner_type='supplier' ORDER BY partner_code LIMIT 100;
CREATE TEMPORARY TABLE tmp_cus AS SELECT partner_id, @rc:=@rc+1 AS rn FROM partners,(SELECT @rc:=0) r WHERE tenant_id=@tid AND partner_type='customer' ORDER BY partner_code LIMIT 100;
CREATE TEMPORARY TABLE tmp_itm AS SELECT item_id, purchase_price, sale_price, @ri:=@ri+1 AS rn FROM items,(SELECT @ri:=0) r WHERE tenant_id=@tid AND item_group IN ('raw_material','part') ORDER BY item_code LIMIT 140;
CREATE TEMPORARY TABLE tmp_fitm AS SELECT item_id, sale_price, @rf:=@rf+1 AS rn FROM items,(SELECT @rf:=0) r WHERE tenant_id=@tid AND item_group IN ('semi_finished','finished') ORDER BY item_code LIMIT 60;

-- ═══ 1. 발주 100건 ═══
SET @i = 0;
INSERT INTO purchase_orders (po_id,tenant_id,po_no,partner_id,po_date,status,total_amount,vat_amount,memo,created_at,updated_at,is_deleted)
SELECT CONCAT('po-',LPAD(seq,4,'0')),@tid,CONCAT('PO-2026-',LPAD(seq,4,'0')),
  (SELECT partner_id FROM tmp_sup WHERE rn=1+(seq%100)),
  DATE_ADD('2026-01-01',INTERVAL FLOOR(seq*1.1) DAY),
  IF(seq<=50,'draft','ordered'),
  0,0,CONCAT('발주#',seq),NOW(6),NOW(6),0
FROM (SELECT @i:=@i+1 AS seq FROM information_schema.COLUMNS LIMIT 100) t;

SET @i = 0;
INSERT INTO purchase_order_items (po_item_id,po_id,tenant_id,item_id,ordered_qty,received_qty,unit_price,supply_amount,vat_amount,warehouse_id,item_status)
SELECT CONCAT('poi-',LPAD(seq,4,'0')),
  CONCAT('po-',LPAD(CEIL(seq/2),4,'0')),@tid,
  (SELECT item_id FROM tmp_itm WHERE rn=1+(seq%140)),
  10+(seq%40), 0,
  (SELECT purchase_price FROM tmp_itm WHERE rn=1+(seq%140)),
  (10+(seq%40))*(SELECT purchase_price FROM tmp_itm WHERE rn=1+(seq%140)),
  ROUND((10+(seq%40))*(SELECT purchase_price FROM tmp_itm WHERE rn=1+(seq%140))*0.1),
  'wh-main','pending'
FROM (SELECT @i:=@i+1 AS seq FROM information_schema.COLUMNS LIMIT 200) t;

UPDATE purchase_orders po SET
  total_amount=(SELECT COALESCE(SUM(supply_amount),0) FROM purchase_order_items WHERE po_id=po.po_id),
  vat_amount=(SELECT COALESCE(SUM(vat_amount),0) FROM purchase_order_items WHERE po_id=po.po_id);

-- ═══ 2. 매입명세 100건 (50 confirmed + 50 draft) ═══
SET @i = 0;
INSERT INTO purchase_receipts (receipt_id,tenant_id,receipt_no,po_id,partner_id,receipt_date,source_type,status,total_amount,vat_amount,memo,created_at,created_by)
SELECT CONCAT('rcpt-',LPAD(seq,4,'0')),@tid,CONCAT('RC-2026-',LPAD(seq,4,'0')),
  CONCAT('po-',LPAD(seq,4,'0')),
  (SELECT partner_id FROM purchase_orders WHERE po_id=CONCAT('po-',LPAD(seq,4,'0'))),
  DATE_ADD('2026-01-05',INTERVAL FLOOR(seq*1.1) DAY),
  'purchase_receipt',
  IF(seq<=50,'confirmed','draft'),
  0,0,CONCAT('매입#',seq),NOW(6),'system'
FROM (SELECT @i:=@i+1 AS seq FROM information_schema.COLUMNS LIMIT 100) t;

SET @i = 0;
INSERT INTO purchase_receipt_items (receipt_item_id,receipt_id,tenant_id,po_item_id,item_id,warehouse_id,qty,unit_price,supply_amount,vat_amount)
SELECT CONCAT('rci-',LPAD(seq,4,'0')),
  CONCAT('rcpt-',LPAD(CEIL(seq/2),4,'0')),@tid,
  CONCAT('poi-',LPAD(seq,4,'0')),
  (SELECT item_id FROM purchase_order_items WHERE po_item_id=CONCAT('poi-',LPAD(seq,4,'0'))),
  'wh-main',
  (SELECT ordered_qty FROM purchase_order_items WHERE po_item_id=CONCAT('poi-',LPAD(seq,4,'0'))),
  (SELECT unit_price FROM purchase_order_items WHERE po_item_id=CONCAT('poi-',LPAD(seq,4,'0'))),
  (SELECT supply_amount FROM purchase_order_items WHERE po_item_id=CONCAT('poi-',LPAD(seq,4,'0'))),
  (SELECT vat_amount FROM purchase_order_items WHERE po_item_id=CONCAT('poi-',LPAD(seq,4,'0')))
FROM (SELECT @i:=@i+1 AS seq FROM information_schema.COLUMNS LIMIT 200) t;

UPDATE purchase_receipts pr SET
  total_amount=(SELECT COALESCE(SUM(supply_amount),0) FROM purchase_receipt_items WHERE receipt_id=pr.receipt_id),
  vat_amount=(SELECT COALESCE(SUM(vat_amount),0) FROM purchase_receipt_items WHERE receipt_id=pr.receipt_id);

-- confirmed 매입 → 재고 반영 (item_stock 증가 + stock_ledger + partner_balance)
UPDATE item_stock s
JOIN (
  SELECT ri.item_id, SUM(ri.qty) AS total_qty, AVG(ri.unit_price) AS avg_price
  FROM purchase_receipt_items ri
  JOIN purchase_receipts r ON r.receipt_id = ri.receipt_id
  WHERE r.status = 'confirmed' AND r.tenant_id = @tid
  GROUP BY ri.item_id
) agg ON agg.item_id = s.item_id AND s.tenant_id = @tid
SET s.current_qty = s.current_qty + agg.total_qty, s.avg_cost = agg.avg_price, s.last_updated_at = NOW(6);

INSERT INTO stock_ledger (tenant_id,item_id,warehouse_id,partner_id,ledger_date,ym,move_type,source_type,source_id,doc_no,qty_in,qty_out,unit_cost,supply_amount)
SELECT @tid, ri.item_id, 'wh-main', r.partner_id, r.receipt_date,
  DATE_FORMAT(r.receipt_date,'%Y-%m'), 'in', 'purchase_receipt', r.receipt_id, r.receipt_no,
  ri.qty, 0, ri.unit_price, ri.supply_amount
FROM purchase_receipt_items ri
JOIN purchase_receipts r ON r.receipt_id = ri.receipt_id
WHERE r.status = 'confirmed' AND r.tenant_id = @tid;

INSERT INTO partner_balance (balance_id,tenant_id,partner_id,total_sales,total_receipt,total_purchase,total_payment,last_updated_at)
SELECT UUID(), @tid, r.partner_id, 0, 0, SUM(r.total_amount+r.vat_amount), 0, NOW(6)
FROM purchase_receipts r
WHERE r.status = 'confirmed' AND r.tenant_id = @tid
GROUP BY r.partner_id
ON DUPLICATE KEY UPDATE total_purchase = total_purchase + VALUES(total_purchase), last_updated_at = NOW(6);

-- ═══ 3. 견적 100건 ═══
SET @i = 0;
INSERT INTO quotations (quote_id,tenant_id,quote_no,partner_id,employee_id,quote_date,valid_until,status,total_amount,vat_amount,memo,is_deleted,created_by)
SELECT CONCAT('qt-',LPAD(seq,4,'0')),@tid,CONCAT('QT-2026-',LPAD(seq,4,'0')),
  (SELECT partner_id FROM tmp_cus WHERE rn=1+(seq%100)),
  'emp-002',
  DATE_ADD('2026-01-01',INTERVAL FLOOR(seq*1.1) DAY),
  DATE_ADD('2026-02-01',INTERVAL FLOOR(seq*1.1) DAY),
  CASE WHEN seq<=30 THEN 'draft' WHEN seq<=60 THEN 'submitted' ELSE 'converted' END,
  0,0,CONCAT('견적#',seq),0,'system'
FROM (SELECT @i:=@i+1 AS seq FROM information_schema.COLUMNS LIMIT 100) t;

SET @i = 0;
INSERT INTO quotation_items (quote_item_id,quote_id,tenant_id,item_id,qty,unit_price,supply_amount,vat_amount,warehouse_id)
SELECT CONCAT('qti-',LPAD(seq,4,'0')),
  CONCAT('qt-',LPAD(CEIL(seq/2),4,'0')),@tid,
  (SELECT item_id FROM tmp_fitm WHERE rn=1+(seq%60)),
  5+(seq%20),
  (SELECT sale_price FROM tmp_fitm WHERE rn=1+(seq%60)),
  (5+(seq%20))*(SELECT sale_price FROM tmp_fitm WHERE rn=1+(seq%60)),
  ROUND((5+(seq%20))*(SELECT sale_price FROM tmp_fitm WHERE rn=1+(seq%60))*0.1),
  'wh-main'
FROM (SELECT @i:=@i+1 AS seq FROM information_schema.COLUMNS LIMIT 200) t;

UPDATE quotations q SET
  total_amount=(SELECT COALESCE(SUM(supply_amount),0) FROM quotation_items WHERE quote_id=q.quote_id),
  vat_amount=(SELECT COALESCE(SUM(vat_amount),0) FROM quotation_items WHERE quote_id=q.quote_id);

-- ═══ 4. 수주 100건 ═══
SET @i = 0;
INSERT INTO sales_orders (order_id,tenant_id,order_no,partner_id,employee_id,order_date,status,total_amount,vat_amount,memo,created_at,updated_at,is_deleted)
SELECT CONCAT('so-',LPAD(seq,4,'0')),@tid,CONCAT('SO-2026-',LPAD(seq,4,'0')),
  (SELECT partner_id FROM tmp_cus WHERE rn=1+(seq%100)),
  'emp-004',
  DATE_ADD('2026-01-10',INTERVAL FLOOR(seq*1.1) DAY),
  'draft',
  0,0,CONCAT('수주#',seq),NOW(6),NOW(6),0
FROM (SELECT @i:=@i+1 AS seq FROM information_schema.COLUMNS LIMIT 100) t;

SET @i = 0;
INSERT INTO sales_order_items (order_item_id,order_id,tenant_id,item_id,ordered_qty,delivered_qty,unit_price,supply_amount,vat_amount,warehouse_id,item_status)
SELECT CONCAT('soi-',LPAD(seq,4,'0')),
  CONCAT('so-',LPAD(CEIL(seq/2),4,'0')),@tid,
  (SELECT item_id FROM tmp_fitm WHERE rn=1+(seq%60)),
  5+(seq%15), 0,
  (SELECT sale_price FROM tmp_fitm WHERE rn=1+(seq%60)),
  (5+(seq%15))*(SELECT sale_price FROM tmp_fitm WHERE rn=1+(seq%60)),
  ROUND((5+(seq%15))*(SELECT sale_price FROM tmp_fitm WHERE rn=1+(seq%60))*0.1),
  'wh-main','pending'
FROM (SELECT @i:=@i+1 AS seq FROM information_schema.COLUMNS LIMIT 200) t;

UPDATE sales_orders so SET
  total_amount=(SELECT COALESCE(SUM(supply_amount),0) FROM sales_order_items WHERE order_id=so.order_id),
  vat_amount=(SELECT COALESCE(SUM(vat_amount),0) FROM sales_order_items WHERE order_id=so.order_id);

-- ═══ 5. 거래명세서 100건 (50 confirmed + 30 draft + 20 invoiced) ═══
SET @i = 0;
INSERT INTO sales_deliveries (delivery_id,tenant_id,delivery_no,order_id,partner_id,employee_id,delivery_date,status,total_amount,vat_amount,memo,created_at,updated_at,is_deleted)
SELECT CONCAT('dlv-',LPAD(seq,4,'0')),@tid,CONCAT('DL-2026-',LPAD(seq,4,'0')),
  CONCAT('so-',LPAD(seq,4,'0')),
  (SELECT partner_id FROM sales_orders WHERE order_id=CONCAT('so-',LPAD(seq,4,'0'))),
  'emp-004',
  DATE_ADD('2026-01-15',INTERVAL FLOOR(seq*1.1) DAY),
  CASE WHEN seq<=50 THEN 'confirmed' WHEN seq<=80 THEN 'draft' ELSE 'invoiced' END,
  0,0,CONCAT('거래명세#',seq),NOW(6),NOW(6),0
FROM (SELECT @i:=@i+1 AS seq FROM information_schema.COLUMNS LIMIT 100) t;

SET @i = 0;
INSERT INTO sales_delivery_items (delivery_item_id,delivery_id,tenant_id,item_id,qty,unit_price,supply_amount,vat_amount,warehouse_id)
SELECT CONCAT('dli-',LPAD(seq,4,'0')),
  CONCAT('dlv-',LPAD(CEIL(seq/2),4,'0')),@tid,
  (SELECT item_id FROM tmp_fitm WHERE rn=1+(seq%60)),
  3+(seq%10),
  (SELECT sale_price FROM tmp_fitm WHERE rn=1+(seq%60)),
  (3+(seq%10))*(SELECT sale_price FROM tmp_fitm WHERE rn=1+(seq%60)),
  ROUND((3+(seq%10))*(SELECT sale_price FROM tmp_fitm WHERE rn=1+(seq%60))*0.1),
  'wh-main'
FROM (SELECT @i:=@i+1 AS seq FROM information_schema.COLUMNS LIMIT 200) t;

UPDATE sales_deliveries sd SET
  total_amount=(SELECT COALESCE(SUM(supply_amount),0) FROM sales_delivery_items WHERE delivery_id=sd.delivery_id),
  vat_amount=(SELECT COALESCE(SUM(vat_amount),0) FROM sales_delivery_items WHERE delivery_id=sd.delivery_id);

-- confirmed/invoiced 거래명세 → 재고차감 + stock_ledger + partner_balance
UPDATE item_stock s
JOIN (
  SELECT di.item_id, SUM(di.qty) AS total_qty
  FROM sales_delivery_items di
  JOIN sales_deliveries d ON d.delivery_id = di.delivery_id
  WHERE d.status IN ('confirmed','invoiced') AND d.tenant_id = @tid
  GROUP BY di.item_id
) agg ON agg.item_id = s.item_id AND s.tenant_id = @tid
SET s.current_qty = s.current_qty - agg.total_qty, s.last_updated_at = NOW(6);

INSERT INTO stock_ledger (tenant_id,item_id,warehouse_id,partner_id,ledger_date,ym,move_type,source_type,source_id,doc_no,qty_in,qty_out,unit_cost,supply_amount)
SELECT @tid, di.item_id, 'wh-main', d.partner_id, d.delivery_date,
  DATE_FORMAT(d.delivery_date,'%Y-%m'), 'out', 'sales_delivery', d.delivery_id, d.delivery_no,
  0, di.qty, di.unit_price, di.supply_amount
FROM sales_delivery_items di
JOIN sales_deliveries d ON d.delivery_id = di.delivery_id
WHERE d.status IN ('confirmed','invoiced') AND d.tenant_id = @tid;

INSERT INTO partner_balance (balance_id,tenant_id,partner_id,total_sales,total_receipt,total_purchase,total_payment,last_updated_at)
SELECT UUID(), @tid, d.partner_id, SUM(d.total_amount+d.vat_amount), 0, 0, 0, NOW(6)
FROM sales_deliveries d
WHERE d.status IN ('confirmed','invoiced') AND d.tenant_id = @tid
GROUP BY d.partner_id
ON DUPLICATE KEY UPDATE total_sales = total_sales + VALUES(total_sales), last_updated_at = NOW(6);

-- ═══ 6. 반품 20건 ═══
SET @i = 0;
INSERT INTO purchase_returns (return_id,tenant_id,return_no,receipt_id,partner_id,return_date,status,total_amount,vat_amount,memo,created_at,updated_at,is_deleted)
SELECT CONCAT('ret-',LPAD(seq,4,'0')),@tid,CONCAT('RT-2026-',LPAD(seq,4,'0')),
  CONCAT('rcpt-',LPAD(seq,4,'0')),
  (SELECT partner_id FROM purchase_receipts WHERE receipt_id=CONCAT('rcpt-',LPAD(seq,4,'0'))),
  DATE_ADD('2026-02-01',INTERVAL FLOOR(seq*2) DAY),
  'draft',
  0,0,CONCAT('반품#',seq),NOW(6),NOW(6),0
FROM (SELECT @i:=@i+1 AS seq FROM information_schema.COLUMNS LIMIT 20) t;

SET @i = 0;
INSERT INTO purchase_return_items (return_item_id,return_id,tenant_id,item_id,qty,unit_price,supply_amount,vat_amount,warehouse_id)
SELECT CONCAT('rti-',LPAD(seq,4,'0')),
  CONCAT('ret-',LPAD(seq,4,'0')),@tid,
  (SELECT item_id FROM purchase_receipt_items WHERE receipt_item_id=CONCAT('rci-',LPAD(seq*2-1,4,'0'))),
  2,
  (SELECT unit_price FROM purchase_receipt_items WHERE receipt_item_id=CONCAT('rci-',LPAD(seq*2-1,4,'0'))),
  2*(SELECT unit_price FROM purchase_receipt_items WHERE receipt_item_id=CONCAT('rci-',LPAD(seq*2-1,4,'0'))),
  ROUND(2*(SELECT unit_price FROM purchase_receipt_items WHERE receipt_item_id=CONCAT('rci-',LPAD(seq*2-1,4,'0')))*0.1),
  'wh-main'
FROM (SELECT @i:=@i+1 AS seq FROM information_schema.COLUMNS LIMIT 20) t;

UPDATE purchase_returns pr SET
  total_amount=(SELECT COALESCE(SUM(supply_amount),0) FROM purchase_return_items WHERE return_id=pr.return_id),
  vat_amount=(SELECT COALESCE(SUM(vat_amount),0) FROM purchase_return_items WHERE return_id=pr.return_id);

-- ═══ 7. monthly_summary 재계산 ═══
INSERT INTO monthly_summary (summary_id,tenant_id,year_month,total_sales,total_purchase,total_receipt,total_payment,last_updated_at)
SELECT UUID(), @tid, ym,
  COALESCE(SUM(CASE WHEN move_type='out' THEN supply_amount ELSE 0 END),0),
  COALESCE(SUM(CASE WHEN move_type='in' THEN supply_amount ELSE 0 END),0),
  0, 0, NOW(6)
FROM stock_ledger WHERE tenant_id = @tid
GROUP BY ym
ON DUPLICATE KEY UPDATE
  total_sales = VALUES(total_sales), total_purchase = VALUES(total_purchase), last_updated_at = NOW(6);

-- ═══ 8. 결재 설정 + 결재 문서 ═══
INSERT INTO approval_settings (setting_id,tenant_id,doc_type,is_enabled,threshold_amount,auto_approve_below,max_lines,created_at,updated_at)
VALUES
(UUID(),@tid,'delivery',1,1000000,1,2,NOW(6),NOW(6)),
(UUID(),@tid,'receipt',1,500000,0,2,NOW(6),NOW(6)),
(UUID(),@tid,'quotation',1,0,0,1,NOW(6),NOW(6));

INSERT INTO approval_lines (line_id,tenant_id,doc_type,seq_no,approver_id,approver_name,role_label,is_active,created_at,updated_at)
VALUES
(UUID(),@tid,'delivery',1,'emp-002','이부장','영업부장',1,NOW(6),NOW(6)),
(UUID(),@tid,'delivery',2,'emp-001','김대표','대표이사',1,NOW(6),NOW(6)),
(UUID(),@tid,'receipt',1,'emp-003','박과장','구매과장',1,NOW(6),NOW(6)),
(UUID(),@tid,'receipt',2,'emp-001','김대표','대표이사',1,NOW(6),NOW(6)),
(UUID(),@tid,'quotation',1,'emp-002','이부장','영업부장',1,NOW(6),NOW(6));

-- 정리
DROP TEMPORARY TABLE tmp_sup, tmp_cus, tmp_itm, tmp_fitm;
