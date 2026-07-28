-- purchase_receipts + stock_ledger + collections 보충 (sales_deliveries는 이미 완료)
SET @tenant_id = (SELECT tenant_id FROM tenants LIMIT 1);
SET @years_back = IFNULL(@years_back, 1);
SET @records_per_month = IFNULL(@records_per_month, 400);
SET @total_months = @years_back * 12;

DROP TEMPORARY TABLE IF EXISTS tmp_numbers;
CREATE TEMPORARY TABLE tmp_numbers (n INT PRIMARY KEY) ENGINE=Memory;
INSERT INTO tmp_numbers (n)
SELECT (t1.n + t2.n*10 + t3.n*100 + t4.n*1000 + t5.n*10000) AS n
FROM
  (SELECT 0 AS n UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4
   UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) t1,
  (SELECT 0 AS n UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4
   UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) t2,
  (SELECT 0 AS n UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4
   UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) t3,
  (SELECT 0 AS n UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4
   UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) t4,
  (SELECT 0 AS n UNION SELECT 1) t5
WHERE (t1.n + t2.n*10 + t3.n*100 + t4.n*1000 + t5.n*10000) < 30000;

DROP TEMPORARY TABLE IF EXISTS tmp_partners;
CREATE TEMPORARY TABLE tmp_partners (rn INT AUTO_INCREMENT PRIMARY KEY, partner_id VARCHAR(36)) ENGINE=Memory;
INSERT INTO tmp_partners (partner_id)
SELECT partner_id FROM partners WHERE tenant_id = @tenant_id AND is_deleted = 0 ORDER BY partner_id LIMIT 100;
SET @partner_count = (SELECT COUNT(*) FROM tmp_partners);

DROP TEMPORARY TABLE IF EXISTS tmp_items;
CREATE TEMPORARY TABLE tmp_items (rn INT AUTO_INCREMENT PRIMARY KEY, item_id VARCHAR(36), sale_price DECIMAL(15,2), purchase_price DECIMAL(15,2)) ENGINE=Memory;
INSERT INTO tmp_items (item_id, sale_price, purchase_price)
SELECT item_id, COALESCE(sale_price, 10000), COALESCE(purchase_price, 7000)
FROM items WHERE tenant_id = @tenant_id AND is_deleted = 0 ORDER BY item_id LIMIT 100;
SET @item_count = (SELECT COUNT(*) FROM tmp_items);

-- purchase_receipts
SET @pr_offset = (SELECT COUNT(*) FROM purchase_receipts WHERE receipt_no LIKE 'PR-BULK-%');
INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, partner_id, receipt_date, source_type, status, total_amount, vat_amount, created_at)
SELECT
  UUID(), @tenant_id,
  CONCAT('PR-BULK-', LPAD(n.n + @pr_offset, 7, '0')),
  (SELECT partner_id FROM tmp_partners WHERE rn = (n.n MOD @partner_count) + 1),
  DATE_SUB(CURDATE(), INTERVAL FLOOR(n.n / @records_per_month) DAY),
  'direct', 'confirmed',
  ROUND(30000 + (n.n MOD 30) * 5000, 0),
  ROUND((30000 + (n.n MOD 30) * 5000) * 0.1, 0),
  DATE_SUB(NOW(), INTERVAL FLOOR(n.n / @records_per_month) DAY)
FROM tmp_numbers n
WHERE n.n < @total_months * @records_per_month;

-- stock_ledger
INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, source_id, qty_in, qty_out, unit_cost, supply_amount)
SELECT
  @tenant_id,
  (SELECT item_id FROM tmp_items WHERE rn = (n.n MOD @item_count) + 1),
  'default',
  DATE_SUB(CURDATE(), INTERVAL FLOOR(n.n / @records_per_month) DAY),
  DATE_FORMAT(DATE_SUB(CURDATE(), INTERVAL FLOOR(n.n / @records_per_month) DAY), '%Y-%m'),
  IF(n.n MOD 2 = 0, 'in', 'out'),
  IF(n.n MOD 2 = 0, 'purchase_receipt', 'sales_delivery'),
  UUID(),
  IF(n.n MOD 2 = 0, (n.n MOD 20) + 1, 0),
  IF(n.n MOD 2 = 1, (n.n MOD 15) + 1, 0),
  (SELECT purchase_price FROM tmp_items WHERE rn = (n.n MOD @item_count) + 1),
  ((n.n MOD 20) + 1) * (SELECT purchase_price FROM tmp_items WHERE rn = (n.n MOD @item_count) + 1)
FROM tmp_numbers n
WHERE n.n < @total_months * @records_per_month * 2;

-- collections
INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, collection_method, ref_doc_type, is_active, created_at, updated_at)
SELECT
  UUID(), @tenant_id,
  (SELECT partner_id FROM tmp_partners WHERE rn = (n.n MOD @partner_count) + 1),
  DATE_SUB(CURDATE(), INTERVAL FLOOR(n.n / @records_per_month) DAY),
  ROUND(30000 + (n.n MOD 40) * 8000, 0),
  ELT((n.n MOD 4) + 1, 'cash', 'bank_transfer', 'card', 'check'),
  'sales_delivery', 1,
  DATE_SUB(NOW(), INTERVAL FLOOR(n.n / @records_per_month) DAY),
  DATE_SUB(NOW(), INTERVAL FLOOR(n.n / @records_per_month) DAY)
FROM tmp_numbers n
WHERE n.n < @total_months * @records_per_month / 2;

SELECT 'sales_deliveries' AS tbl, COUNT(*) AS total FROM sales_deliveries
UNION ALL SELECT 'purchase_receipts', COUNT(*) FROM purchase_receipts
UNION ALL SELECT 'stock_ledger', COUNT(*) FROM stock_ledger
UNION ALL SELECT 'collections', COUNT(*) FROM collections;

DROP TEMPORARY TABLE IF EXISTS tmp_numbers;
DROP TEMPORARY TABLE IF EXISTS tmp_partners;
DROP TEMPORARY TABLE IF EXISTS tmp_items;
