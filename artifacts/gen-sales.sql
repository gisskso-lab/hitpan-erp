SET @tid = '452ca266-97b9-4cd1-a0ac-2f37830c81f6';

CREATE TEMPORARY TABLE tmp_cus AS SELECT partner_id, @rc:=@rc+1 AS rn FROM partners,(SELECT @rc:=0) r WHERE tenant_id=@tid AND partner_type='customer' ORDER BY partner_code LIMIT 100;
CREATE TEMPORARY TABLE tmp_fitm AS SELECT item_id, sale_price, @rf:=@rf+1 AS rn FROM items,(SELECT @rf:=0) r WHERE tenant_id=@tid AND item_group IN ('semi_finished','finished') ORDER BY item_code LIMIT 60;

-- ═══ 견적 100건 ═══
INSERT INTO quotations (quote_id,tenant_id,quote_no,partner_id,employee_id,quote_date,valid_until,status,total_amount,vat_amount,memo,is_deleted,created_by)
SELECT CONCAT('qt-',LPAD(n.seq,4,'0')),@tid,CONCAT('QT-2026-',LPAD(n.seq,4,'0')),
  (SELECT partner_id FROM tmp_cus WHERE rn=1+((n.seq-1)%100)),
  'emp-002',
  DATE_ADD('2026-01-01',INTERVAL FLOOR(n.seq*1.1) DAY),
  DATE_ADD('2026-02-01',INTERVAL FLOOR(n.seq*1.1) DAY),
  CASE WHEN n.seq<=30 THEN 'draft' WHEN n.seq<=60 THEN 'submitted' ELSE 'converted' END,
  0,0,CONCAT('견적#',n.seq),0,'system'
FROM (SELECT a.N + b.N*10 + 1 AS seq FROM (SELECT 0 AS N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) a, (SELECT 0 AS N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) b ORDER BY seq LIMIT 100) n;

INSERT INTO quotation_items (id,quote_id,item_id,qty,unit_price,amount,vat_amount,sort_order)
SELECT CONCAT('qti-',LPAD(n.seq,4,'0')),
  CONCAT('qt-',LPAD(CEIL(n.seq/2),4,'0')),
  (SELECT item_id FROM tmp_fitm WHERE rn=1+((n.seq-1)%60)),
  5+((n.seq-1)%20),
  (SELECT sale_price FROM tmp_fitm WHERE rn=1+((n.seq-1)%60)),
  (5+((n.seq-1)%20))*(SELECT sale_price FROM tmp_fitm WHERE rn=1+((n.seq-1)%60)),
  ROUND((5+((n.seq-1)%20))*(SELECT sale_price FROM tmp_fitm WHERE rn=1+((n.seq-1)%60))*0.1),
  n.seq
FROM (SELECT a.N + b.N*10 + 1 AS seq FROM (SELECT 0 AS N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) a, (SELECT 0 AS N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) b ORDER BY seq LIMIT 200) n;

UPDATE quotations q SET total_amount=(SELECT COALESCE(SUM(amount),0) FROM quotation_items WHERE quote_id=q.quote_id), vat_amount=(SELECT COALESCE(SUM(vat_amount),0) FROM quotation_items WHERE quote_id=q.quote_id);

-- ═══ 수주 100건 ═══
INSERT INTO sales_orders (order_id,tenant_id,order_no,partner_id,employee_id,order_date,status,total_amount,vat_amount,memo,created_at,updated_at,is_deleted)
SELECT CONCAT('so-',LPAD(n.seq,4,'0')),@tid,CONCAT('SO-2026-',LPAD(n.seq,4,'0')),
  (SELECT partner_id FROM tmp_cus WHERE rn=1+((n.seq-1)%100)),
  'emp-004',
  DATE_ADD('2026-01-10',INTERVAL FLOOR(n.seq*1.1) DAY),
  'draft',0,0,CONCAT('수주#',n.seq),NOW(6),NOW(6),0
FROM (SELECT a.N + b.N*10 + 1 AS seq FROM (SELECT 0 AS N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) a, (SELECT 0 AS N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) b ORDER BY seq LIMIT 100) n;

INSERT INTO sales_order_items (order_item_id,order_id,tenant_id,item_id,ordered_qty,delivered_qty,unit_price,supply_amount,vat_amount,item_status)
SELECT CONCAT('soi-',LPAD(n.seq,4,'0')),
  CONCAT('so-',LPAD(CEIL(n.seq/2),4,'0')),@tid,
  (SELECT item_id FROM tmp_fitm WHERE rn=1+((n.seq-1)%60)),
  5+((n.seq-1)%15),0,
  (SELECT sale_price FROM tmp_fitm WHERE rn=1+((n.seq-1)%60)),
  (5+((n.seq-1)%15))*(SELECT sale_price FROM tmp_fitm WHERE rn=1+((n.seq-1)%60)),
  ROUND((5+((n.seq-1)%15))*(SELECT sale_price FROM tmp_fitm WHERE rn=1+((n.seq-1)%60))*0.1),
  'pending'
FROM (SELECT a.N + b.N*10 + 1 AS seq FROM (SELECT 0 AS N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) a, (SELECT 0 AS N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) b ORDER BY seq LIMIT 200) n;

UPDATE sales_orders so SET total_amount=(SELECT COALESCE(SUM(supply_amount),0) FROM sales_order_items WHERE order_id=so.order_id), vat_amount=(SELECT COALESCE(SUM(vat_amount),0) FROM sales_order_items WHERE order_id=so.order_id);

-- ═══ 거래명세서 100건 ═══
INSERT INTO sales_deliveries (delivery_id,tenant_id,delivery_no,order_id,partner_id,employee_id,delivery_date,source_type,status,total_amount,vat_amount,memo,created_at,updated_at,is_deleted)
SELECT CONCAT('dlv-',LPAD(n.seq,4,'0')),@tid,CONCAT('DL-2026-',LPAD(n.seq,4,'0')),
  CONCAT('so-',LPAD(n.seq,4,'0')),
  (SELECT partner_id FROM sales_orders WHERE order_id=CONCAT('so-',LPAD(n.seq,4,'0'))),
  'emp-004',
  DATE_ADD('2026-01-15',INTERVAL FLOOR(n.seq*1.1) DAY),
  'sales_order',
  CASE WHEN n.seq<=50 THEN 'confirmed' WHEN n.seq<=80 THEN 'draft' ELSE 'invoiced' END,
  0,0,CONCAT('거래명세#',n.seq),NOW(6),NOW(6),0
FROM (SELECT a.N + b.N*10 + 1 AS seq FROM (SELECT 0 AS N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) a, (SELECT 0 AS N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) b ORDER BY seq LIMIT 100) n;

INSERT INTO sales_delivery_items (delivery_item_id,delivery_id,tenant_id,item_id,qty,unit_price,supply_amount,vat_amount,warehouse_id)
SELECT CONCAT('dli-',LPAD(n.seq,4,'0')),
  CONCAT('dlv-',LPAD(CEIL(n.seq/2),4,'0')),@tid,
  (SELECT item_id FROM tmp_fitm WHERE rn=1+((n.seq-1)%60)),
  3+((n.seq-1)%10),
  (SELECT sale_price FROM tmp_fitm WHERE rn=1+((n.seq-1)%60)),
  (3+((n.seq-1)%10))*(SELECT sale_price FROM tmp_fitm WHERE rn=1+((n.seq-1)%60)),
  ROUND((3+((n.seq-1)%10))*(SELECT sale_price FROM tmp_fitm WHERE rn=1+((n.seq-1)%60))*0.1),
  'wh-main'
FROM (SELECT a.N + b.N*10 + 1 AS seq FROM (SELECT 0 AS N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) a, (SELECT 0 AS N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) b ORDER BY seq LIMIT 200) n;

UPDATE sales_deliveries sd SET total_amount=(SELECT COALESCE(SUM(supply_amount),0) FROM sales_delivery_items WHERE delivery_id=sd.delivery_id), vat_amount=(SELECT COALESCE(SUM(vat_amount),0) FROM sales_delivery_items WHERE delivery_id=sd.delivery_id);

-- 판매 확정 → 재고 차감 + stock_ledger + partner_balance
UPDATE item_stock s
JOIN (SELECT di.item_id, SUM(di.qty) AS tq FROM sales_delivery_items di JOIN sales_deliveries d ON d.delivery_id=di.delivery_id WHERE d.status IN ('confirmed','invoiced') AND d.tenant_id=@tid GROUP BY di.item_id) a ON a.item_id=s.item_id AND s.tenant_id=@tid
SET s.current_qty = s.current_qty - a.tq, s.last_updated_at=NOW(6);

INSERT INTO stock_ledger (tenant_id,item_id,warehouse_id,partner_id,ledger_date,ym,move_type,source_type,source_id,doc_no,qty_in,qty_out,unit_cost,supply_amount)
SELECT @tid,di.item_id,'wh-main',d.partner_id,d.delivery_date,DATE_FORMAT(d.delivery_date,'%Y-%m'),'out','sales_delivery',d.delivery_id,d.delivery_no,0,di.qty,di.unit_price,di.supply_amount
FROM sales_delivery_items di JOIN sales_deliveries d ON d.delivery_id=di.delivery_id WHERE d.status IN ('confirmed','invoiced') AND d.tenant_id=@tid;

INSERT INTO partner_balance (balance_id,tenant_id,partner_id,total_sales,total_receipt,total_purchase,total_payment,last_updated_at)
SELECT UUID(),@tid,d.partner_id,SUM(d.total_amount+d.vat_amount),0,0,0,NOW(6)
FROM sales_deliveries d WHERE d.status IN ('confirmed','invoiced') AND d.tenant_id=@tid GROUP BY d.partner_id
ON DUPLICATE KEY UPDATE total_sales=total_sales+VALUES(total_sales),last_updated_at=NOW(6);

-- ═══ 반품 20건 ═══
INSERT INTO purchase_returns (return_id,tenant_id,return_no,receipt_id,partner_id,return_date,status,total_amount,vat_amount,memo,created_at,updated_at,is_deleted)
SELECT CONCAT('ret-',LPAD(n.seq,4,'0')),@tid,CONCAT('RT-2026-',LPAD(n.seq,4,'0')),
  CONCAT('rcpt-',LPAD(n.seq,4,'0')),
  (SELECT partner_id FROM purchase_receipts WHERE receipt_id=CONCAT('rcpt-',LPAD(n.seq,4,'0'))),
  DATE_ADD('2026-02-01',INTERVAL n.seq*2 DAY),'draft',0,0,CONCAT('반품#',n.seq),NOW(6),NOW(6),0
FROM (SELECT a.N + b.N*10 + 1 AS seq FROM (SELECT 0 AS N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) a, (SELECT 0 AS N UNION SELECT 1 UNION SELECT 2) b ORDER BY seq LIMIT 20) n;

INSERT INTO purchase_return_items (return_item_id,return_id,tenant_id,item_id,qty,unit_price,supply_amount,vat_amount,warehouse_id)
SELECT CONCAT('rti-',LPAD(n.seq,4,'0')),CONCAT('ret-',LPAD(n.seq,4,'0')),@tid,
  (SELECT item_id FROM purchase_receipt_items WHERE receipt_item_id=CONCAT('rci-',LPAD(n.seq*2-1,4,'0'))),
  2,(SELECT unit_price FROM purchase_receipt_items WHERE receipt_item_id=CONCAT('rci-',LPAD(n.seq*2-1,4,'0'))),
  2*(SELECT unit_price FROM purchase_receipt_items WHERE receipt_item_id=CONCAT('rci-',LPAD(n.seq*2-1,4,'0'))),
  ROUND(2*(SELECT unit_price FROM purchase_receipt_items WHERE receipt_item_id=CONCAT('rci-',LPAD(n.seq*2-1,4,'0')))*0.1),'wh-main'
FROM (SELECT a.N + b.N*10 + 1 AS seq FROM (SELECT 0 AS N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) a, (SELECT 0 AS N UNION SELECT 1 UNION SELECT 2) b ORDER BY seq LIMIT 20) n;

UPDATE purchase_returns pr SET total_amount=(SELECT COALESCE(SUM(supply_amount),0) FROM purchase_return_items WHERE return_id=pr.return_id), vat_amount=(SELECT COALESCE(SUM(vat_amount),0) FROM purchase_return_items WHERE return_id=pr.return_id);

-- ═══ monthly_summary ═══
INSERT INTO monthly_summary (summary_id,tenant_id,`year_month`,total_sales,total_purchase,total_receipt,total_payment,last_updated_at)
SELECT UUID(),@tid,ym,
  COALESCE(SUM(CASE WHEN move_type='out' THEN supply_amount ELSE 0 END),0),
  COALESCE(SUM(CASE WHEN move_type='in' THEN supply_amount ELSE 0 END),0),0,0,NOW(6)
FROM stock_ledger WHERE tenant_id=@tid GROUP BY ym
ON DUPLICATE KEY UPDATE total_sales=VALUES(total_sales),total_purchase=VALUES(total_purchase);

-- ═══ 결재 설정 ═══
INSERT INTO approval_settings (setting_id,tenant_id,doc_type,is_enabled,threshold_amount,auto_approve_below,max_lines,created_at,updated_at) VALUES
(UUID(),@tid,'delivery',1,1000000,1,2,NOW(6),NOW(6)),
(UUID(),@tid,'receipt',1,500000,0,2,NOW(6),NOW(6)),
(UUID(),@tid,'quotation',1,0,0,1,NOW(6),NOW(6));

INSERT INTO approval_lines (line_id,tenant_id,doc_type,seq_no,approver_id,approver_name,role_label,is_active,created_at,updated_at) VALUES
(UUID(),@tid,'delivery',1,'emp-002','이부장','영업부장',1,NOW(6),NOW(6)),
(UUID(),@tid,'delivery',2,'emp-001','김대표','대표이사',1,NOW(6),NOW(6)),
(UUID(),@tid,'receipt',1,'emp-003','박과장','구매과장',1,NOW(6),NOW(6)),
(UUID(),@tid,'receipt',2,'emp-001','김대표','대표이사',1,NOW(6),NOW(6)),
(UUID(),@tid,'quotation',1,'emp-002','이부장','영업부장',1,NOW(6),NOW(6));

DROP TEMPORARY TABLE tmp_cus, tmp_fitm;
