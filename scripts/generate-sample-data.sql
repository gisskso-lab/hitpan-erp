-- ══════════════════════════════════════════════════════════════
-- 대용량 샘플 데이터 생성 — 1년/2년/3년치 선택 투입
--
-- 사용법:
--   SET @years_back = 1;  -- 1/2/3 선택
--   SET @records_per_month = 400;  -- 월 400건 기준
--   SOURCE scripts/generate-sample-data.sql;
--
-- 주의:
--   - items, partners, employees는 기존 것 재사용 (200건)
--   - sales_deliveries, purchase_receipts 중심으로 대량 생성
--   - 감사로그 트리거 없이 직접 INSERT (성능 우선)
-- ══════════════════════════════════════════════════════════════

SET @tenant_id = (SELECT tenant_id FROM tenants LIMIT 1);
SET @years_back = IFNULL(@years_back, 1);
SET @records_per_month = IFNULL(@records_per_month, 400);
SET @total_months = @years_back * 12;

SELECT CONCAT('📊 생성 계획: ', @years_back, '년치 × ', @records_per_month, '건/월 = ', @total_months * @records_per_month, '건') AS plan;
SELECT CONCAT('📅 기간: ', DATE_SUB(CURDATE(), INTERVAL @years_back YEAR), ' ~ ', CURDATE()) AS period;
SELECT CONCAT('⏱️ 예상 소요: ', ROUND(@total_months * @records_per_month * 0.0005, 1), '초') AS estimated_time;

-- ① 임시 숫자 테이블 (0 ~ N-1)
DROP TEMPORARY TABLE IF EXISTS tmp_numbers;
CREATE TEMPORARY TABLE tmp_numbers (n INT PRIMARY KEY) ENGINE=Memory;

-- 10000까지 숫자 생성 (CROSS JOIN)
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
WHERE (t1.n + t2.n*10 + t3.n*100 + t4.n*1000 + t5.n*10000) < 20000;

-- ② 거래처 ID 배열 (일반 업체만)
DROP TEMPORARY TABLE IF EXISTS tmp_partners;
CREATE TEMPORARY TABLE tmp_partners (rn INT AUTO_INCREMENT PRIMARY KEY, partner_id VARCHAR(36)) ENGINE=Memory;
INSERT INTO tmp_partners (partner_id)
SELECT partner_id FROM partners WHERE tenant_id = @tenant_id AND is_deleted = 0 ORDER BY partner_id LIMIT 100;
SET @partner_count = (SELECT COUNT(*) FROM tmp_partners);

-- ③ 상품 ID 배열
DROP TEMPORARY TABLE IF EXISTS tmp_items;
CREATE TEMPORARY TABLE tmp_items (rn INT AUTO_INCREMENT PRIMARY KEY, item_id VARCHAR(36), sale_price DECIMAL(15,2), purchase_price DECIMAL(15,2)) ENGINE=Memory;
INSERT INTO tmp_items (item_id, sale_price, purchase_price)
SELECT item_id, COALESCE(sale_price, 10000), COALESCE(purchase_price, 7000)
FROM items WHERE tenant_id = @tenant_id AND is_deleted = 0 ORDER BY item_id LIMIT 100;
SET @item_count = (SELECT COUNT(*) FROM tmp_items);

SELECT CONCAT('🏢 거래처: ', @partner_count, '개, 📦 상품: ', @item_count, '개') AS masters;

-- ④ sales_deliveries 대량 생성
INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, partner_id, delivery_date, source_type, status, total_amount, vat_amount, created_at, updated_at, is_deleted)
SELECT
  UUID(),
  @tenant_id,
  CONCAT('SD-BULK-', LPAD(n.n, 7, '0')),
  (SELECT partner_id FROM tmp_partners WHERE rn = (n.n MOD @partner_count) + 1),
  DATE_SUB(CURDATE(), INTERVAL FLOOR(n.n / @records_per_month) DAY),
  'direct',
  'confirmed',
  ROUND(50000 + (n.n MOD 50) * 10000, 0),
  ROUND((50000 + (n.n MOD 50) * 10000) * 0.1, 0),
  DATE_SUB(NOW(), INTERVAL FLOOR(n.n / @records_per_month) DAY),
  DATE_SUB(NOW(), INTERVAL FLOOR(n.n / @records_per_month) DAY),
  0
FROM tmp_numbers n
WHERE n.n < @total_months * @records_per_month;

SET @created_sd = ROW_COUNT();

-- ⑤ purchase_receipts 대량 생성
INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, partner_id, receipt_date, source_type, status, total_amount, vat_amount, created_at)
SELECT
  UUID(),
  @tenant_id,
  CONCAT('PR-BULK-', LPAD(n.n, 7, '0')),
  (SELECT partner_id FROM tmp_partners WHERE rn = (n.n MOD @partner_count) + 1),
  DATE_SUB(CURDATE(), INTERVAL FLOOR(n.n / @records_per_month) DAY),
  'direct',
  'confirmed',
  ROUND(30000 + (n.n MOD 30) * 5000, 0),
  ROUND((30000 + (n.n MOD 30) * 5000) * 0.1, 0),
  DATE_SUB(NOW(), INTERVAL FLOOR(n.n / @records_per_month) DAY)
FROM tmp_numbers n
WHERE n.n < @total_months * @records_per_month;

SET @created_pr = ROW_COUNT();

-- ⑥ stock_ledger 대량 생성 (입고 + 출고 2배수)
INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, qty_in, qty_out, unit_cost, supply_amount)
SELECT
  @tenant_id,
  (SELECT item_id FROM tmp_items WHERE rn = (n.n MOD @item_count) + 1),
  'default',
  DATE_SUB(CURDATE(), INTERVAL FLOOR(n.n / @records_per_month) DAY),
  DATE_FORMAT(DATE_SUB(CURDATE(), INTERVAL FLOOR(n.n / @records_per_month) DAY), '%Y-%m'),
  IF(n.n MOD 2 = 0, 'in', 'out'),
  IF(n.n MOD 2 = 0, 'purchase_receipt', 'sales_delivery'),
  IF(n.n MOD 2 = 0, (n.n MOD 20) + 1, 0),
  IF(n.n MOD 2 = 1, (n.n MOD 15) + 1, 0),
  (SELECT purchase_price FROM tmp_items WHERE rn = (n.n MOD @item_count) + 1),
  ((n.n MOD 20) + 1) * (SELECT purchase_price FROM tmp_items WHERE rn = (n.n MOD @item_count) + 1)
FROM tmp_numbers n
WHERE n.n < @total_months * @records_per_month * 2;

SET @created_sl = ROW_COUNT();

-- ⑦ collections 대량 생성 (수금 — sales_delivery 50% 비율)
INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, collection_method, ref_doc_type, is_active, created_at, updated_at)
SELECT
  UUID(),
  @tenant_id,
  (SELECT partner_id FROM tmp_partners WHERE rn = (n.n MOD @partner_count) + 1),
  DATE_SUB(CURDATE(), INTERVAL FLOOR(n.n / @records_per_month) DAY),
  ROUND(30000 + (n.n MOD 40) * 8000, 0),
  ELT((n.n MOD 4) + 1, 'cash', 'bank_transfer', 'card', 'check'),
  'sales_delivery',
  1,
  DATE_SUB(NOW(), INTERVAL FLOOR(n.n / @records_per_month) DAY),
  DATE_SUB(NOW(), INTERVAL FLOOR(n.n / @records_per_month) DAY)
FROM tmp_numbers n
WHERE n.n < @total_months * @records_per_month / 2;

SET @created_coll = ROW_COUNT();

-- ⑧ 결과 리포트
SELECT '==================== 생성 결과 ====================' AS report;
SELECT CONCAT('✅ sales_deliveries: ', @created_sd, '건') AS result
UNION ALL SELECT CONCAT('✅ purchase_receipts: ', @created_pr, '건')
UNION ALL SELECT CONCAT('✅ stock_ledger: ', @created_sl, '건 (in/out)')
UNION ALL SELECT CONCAT('✅ collections: ', @created_coll, '건');

SELECT '==================== 총 테이블 건수 ====================' AS report;
SELECT 'sales_deliveries' AS tbl, COUNT(*) AS total FROM sales_deliveries
UNION ALL SELECT 'purchase_receipts', COUNT(*) FROM purchase_receipts
UNION ALL SELECT 'stock_ledger', COUNT(*) FROM stock_ledger
UNION ALL SELECT 'collections', COUNT(*) FROM collections;

DROP TEMPORARY TABLE IF EXISTS tmp_numbers;
DROP TEMPORARY TABLE IF EXISTS tmp_partners;
DROP TEMPORARY TABLE IF EXISTS tmp_items;
