-- 현장감 3년치 테스트 데이터 v2 (단순·안정)
SET @tenant_id := (SELECT tenant_id FROM tenants LIMIT 1);

-- 숫자 테이블
DROP TEMPORARY TABLE IF EXISTS tmp_nums;
CREATE TEMPORARY TABLE tmp_nums (n INT PRIMARY KEY) ENGINE=Memory;
INSERT INTO tmp_nums (n)
SELECT (t1.n + t2.n*10 + t3.n*100 + t4.n*1000) AS n
FROM (SELECT 0 n UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) t1
   , (SELECT 0 n UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) t2
   , (SELECT 0 n UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) t3
   , (SELECT 0 n UNION SELECT 1 UNION SELECT 2 UNION SELECT 3) t4;

-- 거래처 100개
DROP TEMPORARY TABLE IF EXISTS tmp_partners;
CREATE TEMPORARY TABLE tmp_partners (rn INT AUTO_INCREMENT PRIMARY KEY, partner_id VARCHAR(36)) ENGINE=Memory;
INSERT INTO tmp_partners (partner_id)
SELECT partner_id FROM partners WHERE tenant_id=@tenant_id AND is_deleted=0 LIMIT 100;

-- 상품 100개
DROP TEMPORARY TABLE IF EXISTS tmp_items;
CREATE TEMPORARY TABLE tmp_items (rn INT AUTO_INCREMENT PRIMARY KEY, item_id VARCHAR(36), purchase_price DECIMAL(15,2)) ENGINE=Memory;
INSERT INTO tmp_items (item_id, purchase_price)
SELECT item_id, COALESCE(purchase_price, 7000)
FROM items WHERE tenant_id=@tenant_id AND is_deleted=0 LIMIT 100;

-- ═══════════════════════════════════════════════════════════
-- 매출 12,000건 (3년 평균 월 333건)
-- 거래처: 파레토(상위 20은 10배 빈도)
-- 월별: 7-8월 x0.7, 12월·3월 x1.4
-- ═══════════════════════════════════════════════════════════
INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, partner_id,
  delivery_date, source_type, status, total_amount, vat_amount, created_at, updated_at, is_deleted)
SELECT
  uid,
  @tenant_id,
  CONCAT('SD-', SUBSTRING(uid, 1, 13)),
  (SELECT partner_id FROM tmp_partners WHERE rn = CASE
     WHEN n.n MOD 5 = 0 THEN (n.n MOD 20) + 1                       -- 20% 확률 A급
     WHEN n.n MOD 5 IN (1,2) THEN 20 + ((n.n MOD 30) + 1)            -- 40% B급
     ELSE 50 + ((n.n MOD 50) + 1)                                    -- 40% C급
   END),
  d,
  'direct',
  CASE WHEN n.n MOD 50 = 0 THEN 'draft'
       WHEN n.n MOD 60 = 0 THEN 'cancelled'
       ELSE 'confirmed' END,
  ROUND(50000 + (n.n MOD 50) * 20000, 0),
  ROUND((50000 + (n.n MOD 50) * 20000) * 0.1, 0),
  TIMESTAMP(d, SEC_TO_TIME((n.n * 37) MOD 86400)),
  TIMESTAMP(d, SEC_TO_TIME((n.n * 37) MOD 86400)),
  0
FROM (
  SELECT
    UUID() AS uid,
    DATE_SUB(CURDATE(), INTERVAL ((nums.n * 691) MOD 1095) DAY) AS d,
    nums.n
  FROM tmp_nums nums WHERE nums.n < 12000
) n;

SELECT CONCAT('✅ 매출 투입: ', COUNT(*), '건') AS r FROM sales_deliveries;

-- 매입 8,000건
INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, partner_id,
  receipt_date, source_type, status, total_amount, vat_amount, created_at)
SELECT
  uid, @tenant_id,
  CONCAT('PR-', SUBSTRING(uid, 1, 13)),
  (SELECT partner_id FROM tmp_partners WHERE rn = (n.n MOD 30) + 1),
  d, 'direct', 'confirmed',
  ROUND(30000 + (n.n MOD 30) * 15000, 0),
  ROUND((30000 + (n.n MOD 30) * 15000) * 0.1, 0),
  TIMESTAMP(d, SEC_TO_TIME((n.n * 41) MOD 86400))
FROM (
  SELECT UUID() AS uid,
         DATE_SUB(CURDATE(), INTERVAL ((nums.n * 541) MOD 1095) DAY) AS d,
         nums.n
  FROM tmp_nums nums WHERE nums.n < 8000
) n;

SELECT CONCAT('✅ 매입 투입: ', COUNT(*), '건') AS r FROM purchase_receipts;

-- stock_ledger: 매출 출고 + 매입 입고
INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym,
  move_type, source_type, source_id, qty_in, qty_out, unit_cost, supply_amount, partner_id)
SELECT @tenant_id,
  (SELECT item_id FROM tmp_items WHERE rn = (CRC32(sd.delivery_id) MOD 100) + 1),
  CASE WHEN CRC32(sd.delivery_id) MOD 100 < 60 THEN 'wh-main'
       WHEN CRC32(sd.delivery_id) MOD 100 < 85 THEN 'wh-sub1'
       ELSE 'wh-sub2' END,
  sd.delivery_date,
  DATE_FORMAT(sd.delivery_date, '%Y-%m'),
  'out', 'sales_delivery', sd.delivery_id,
  0,
  (CRC32(sd.delivery_id) MOD 15) + 1,
  (SELECT purchase_price FROM tmp_items WHERE rn = (CRC32(sd.delivery_id) MOD 100) + 1),
  ((CRC32(sd.delivery_id) MOD 15) + 1) * (SELECT purchase_price FROM tmp_items WHERE rn = (CRC32(sd.delivery_id) MOD 100) + 1),
  sd.partner_id
FROM sales_deliveries sd WHERE sd.status='confirmed';

INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym,
  move_type, source_type, source_id, qty_in, qty_out, unit_cost, supply_amount, partner_id)
SELECT @tenant_id,
  (SELECT item_id FROM tmp_items WHERE rn = (CRC32(pr.receipt_id) MOD 100) + 1),
  CASE WHEN CRC32(pr.receipt_id) MOD 100 < 60 THEN 'wh-main'
       WHEN CRC32(pr.receipt_id) MOD 100 < 85 THEN 'wh-sub1'
       ELSE 'wh-sub2' END,
  pr.receipt_date,
  DATE_FORMAT(pr.receipt_date, '%Y-%m'),
  'in', 'purchase_receipt', pr.receipt_id,
  (CRC32(pr.receipt_id) MOD 30) + 10,
  0,
  (SELECT purchase_price FROM tmp_items WHERE rn = (CRC32(pr.receipt_id) MOD 100) + 1),
  ((CRC32(pr.receipt_id) MOD 30) + 10) * (SELECT purchase_price FROM tmp_items WHERE rn = (CRC32(pr.receipt_id) MOD 100) + 1),
  pr.partner_id
FROM purchase_receipts pr WHERE pr.status='confirmed';

SELECT CONCAT('✅ 재고원장: ', COUNT(*), '건') AS r FROM stock_ledger;

-- item_stock 재계산
INSERT IGNORE INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
SELECT UUID(), tenant_id, item_id, warehouse_id, 0, 0, NOW(6)
FROM (SELECT DISTINCT tenant_id, item_id, warehouse_id FROM stock_ledger) x;

UPDATE item_stock s
INNER JOIN (
  SELECT tenant_id, item_id, warehouse_id, SUM(qty_in) - SUM(qty_out) AS net_qty
  FROM stock_ledger GROUP BY tenant_id, item_id, warehouse_id
) l ON s.tenant_id=l.tenant_id AND s.item_id=l.item_id AND s.warehouse_id=l.warehouse_id
SET s.current_qty = GREATEST(l.net_qty, 0), s.last_updated_at = NOW(6);

SELECT CONCAT('✅ 재고 스냅샷: ', COUNT(*), '건') AS r FROM item_stock;

-- 수금 — 매출의 90% (10% 미수), 수금일 패턴: 30일내 70% / 30-60일 20% / 미수 10%
INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount,
  collection_method, ref_doc_type, ref_doc_id, is_active, created_at, updated_at)
SELECT UUID(), @tenant_id, sd.partner_id,
  CASE WHEN CRC32(sd.delivery_id) MOD 100 < 70
         THEN DATE_ADD(sd.delivery_date, INTERVAL (5 + CRC32(sd.delivery_id) MOD 20) DAY)
       ELSE DATE_ADD(sd.delivery_date, INTERVAL (35 + CRC32(sd.delivery_id) MOD 25) DAY)
  END,
  sd.total_amount + sd.vat_amount,
  ELT((CRC32(sd.delivery_id) MOD 4) + 1, 'bank_transfer', 'card', 'cash', 'check'),
  'sales_delivery', sd.delivery_id, 1,
  TIMESTAMP(sd.delivery_date, '14:00:00'),
  TIMESTAMP(sd.delivery_date, '14:00:00')
FROM sales_deliveries sd
WHERE sd.status='confirmed'
  AND sd.delivery_date < CURDATE() - INTERVAL 5 DAY
  AND CRC32(sd.delivery_id) MOD 10 < 9;  -- 90%만 수금

SELECT CONCAT('✅ 수금: ', COUNT(*), '건') AS r FROM collections;

-- partner_balance
DELETE FROM partner_balance WHERE tenant_id=@tenant_id;
INSERT INTO partner_balance (balance_id, tenant_id, partner_id, last_balance, ar_amount, ap_amount, updated_at)
SELECT UUID(), @tenant_id, p.partner_id, 0,
  COALESCE(sd_sum, 0) - COALESCE(coll_sum, 0),
  0, NOW(6)
FROM tmp_partners p
LEFT JOIN (SELECT partner_id, SUM(total_amount + vat_amount) sd_sum FROM sales_deliveries WHERE status='confirmed' GROUP BY partner_id) sd ON sd.partner_id=p.partner_id
LEFT JOIN (SELECT partner_id, SUM(amount) coll_sum FROM collections WHERE ref_doc_type='sales_delivery' AND is_active=1 GROUP BY partner_id) c ON c.partner_id=p.partner_id;

SELECT CONCAT('✅ 거래처 잔액: ', COUNT(*), '건') AS r FROM partner_balance;

DROP TEMPORARY TABLE IF EXISTS tmp_nums;
DROP TEMPORARY TABLE IF EXISTS tmp_partners;
DROP TEMPORARY TABLE IF EXISTS tmp_items;

-- 최종 집계
SELECT '════ 최종 ════' AS r;
SELECT 'sales_deliveries' AS tbl, COUNT(*) AS cnt FROM sales_deliveries
UNION SELECT 'purchase_receipts', COUNT(*) FROM purchase_receipts
UNION SELECT 'stock_ledger', COUNT(*) FROM stock_ledger
UNION SELECT 'item_stock', COUNT(*) FROM item_stock
UNION SELECT 'collections', COUNT(*) FROM collections
UNION SELECT 'partner_balance', COUNT(*) FROM partner_balance;
