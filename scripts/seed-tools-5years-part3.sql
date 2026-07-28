-- ═══════════════════════════════════════════════════════════════════
-- PART 3/3 — 5년치 거래 (견적 + 수주 + 명세서 + 발주 + 매입 + 경비 + 수금)
-- 기간: 2021-08-01 ~ 2026-07-31 (1,825일)
-- 계절성: 7-8월 -25%, 12월 +30%, 3월 +20%
-- ═══════════════════════════════════════════════════════════════════
SET @tenant = (SELECT tenant_id FROM tenants LIMIT 1);
SET @start_date = '2021-08-01';
SET @end_date = '2026-07-31';
SET @days_span = DATEDIFF(@end_date, @start_date) + 1;  -- ≈1826

-- 숫자 테이블
DROP TEMPORARY TABLE IF EXISTS tmp_n;
CREATE TEMPORARY TABLE tmp_n (n INT PRIMARY KEY) ENGINE=Memory;
INSERT INTO tmp_n (n)
SELECT a.N + b.N*10 + c.N*100 + d.N*1000
FROM (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) a,
     (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) b,
     (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) c,
     (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) d
WHERE (a.N + b.N*10 + c.N*100 + d.N*1000) < 2000;

-- 인덱싱 헬퍼 테이블
DROP TEMPORARY TABLE IF EXISTS tmp_customers;
CREATE TEMPORARY TABLE tmp_customers (rn INT AUTO_INCREMENT PRIMARY KEY, partner_id VARCHAR(36)) ENGINE=Memory;
INSERT INTO tmp_customers (partner_id) SELECT partner_id FROM partners WHERE tenant_id=@tenant AND partner_type='customer';

DROP TEMPORARY TABLE IF EXISTS tmp_suppliers;
CREATE TEMPORARY TABLE tmp_suppliers (rn INT AUTO_INCREMENT PRIMARY KEY, partner_id VARCHAR(36)) ENGINE=Memory;
INSERT INTO tmp_suppliers (partner_id) SELECT partner_id FROM partners WHERE tenant_id=@tenant AND partner_type='supplier';

DROP TEMPORARY TABLE IF EXISTS tmp_items;
CREATE TEMPORARY TABLE tmp_items (rn INT AUTO_INCREMENT PRIMARY KEY, item_id VARCHAR(36), item_type VARCHAR(20),
  purchase_price DECIMAL(15,2), sale_price DECIMAL(15,2)) ENGINE=Memory;
INSERT INTO tmp_items (item_id, item_type, purchase_price, sale_price)
SELECT item_id, item_type, purchase_price, sale_price FROM items WHERE tenant_id=@tenant ORDER BY item_code;

DROP TEMPORARY TABLE IF EXISTS tmp_sales_emp;
CREATE TEMPORARY TABLE tmp_sales_emp (rn INT AUTO_INCREMENT PRIMARY KEY, employee_id VARCHAR(36)) ENGINE=Memory;
INSERT INTO tmp_sales_emp (employee_id)
SELECT employee_id FROM employees WHERE tenant_id=@tenant AND employee_id IN ('emp-smgr','emp-fs1','emp-fs2','emp-fs3');

-- ═══════════════════════════════════════════════════════════════════
-- 1. 일자별 거래 생성 기반
-- ═══════════════════════════════════════════════════════════════════
-- tmp_days: 2021-08-01 ~ 2026-07-31 (주말 제외, 계절 가중치)
DROP TEMPORARY TABLE IF EXISTS tmp_days;
CREATE TEMPORARY TABLE tmp_days (
  d DATE PRIMARY KEY,
  sales_count INT,     -- 그날의 매출 건수
  purchase_count INT   -- 그날의 매입 건수
) ENGINE=Memory;

INSERT INTO tmp_days (d, sales_count, purchase_count)
SELECT
  DATE_ADD(@start_date, INTERVAL n.n DAY) AS d,
  -- 일평균 매출: 평일 15건, 계절 가중치 적용
  GREATEST(0, FLOOR(
    CASE WHEN DAYOFWEEK(DATE_ADD(@start_date, INTERVAL n.n DAY)) IN (1,7) THEN 0
         ELSE 15 END
    * CASE MONTH(DATE_ADD(@start_date, INTERVAL n.n DAY))
        WHEN 7 THEN 0.75 WHEN 8 THEN 0.70
        WHEN 12 THEN 1.30 WHEN 3 THEN 1.20
        WHEN 1 THEN 0.85 WHEN 2 THEN 0.90
        ELSE 1.00 END
    * (0.85 + (CRC32(DATE_ADD(@start_date, INTERVAL n.n DAY)) MOD 30) / 100)  -- 일 변동 ±15%
  )),
  GREATEST(0, FLOOR(
    CASE WHEN DAYOFWEEK(DATE_ADD(@start_date, INTERVAL n.n DAY)) IN (1,7) THEN 0
         ELSE 6 END
    * CASE MONTH(DATE_ADD(@start_date, INTERVAL n.n DAY))
        WHEN 7 THEN 0.80 WHEN 8 THEN 0.75
        WHEN 12 THEN 1.20 WHEN 3 THEN 1.15
        ELSE 1.00 END
    * (0.85 + (CRC32(DATE_ADD(@start_date, INTERVAL n.n DAY)) MOD 30) / 100)
  ))
FROM tmp_n n WHERE n.n < @days_span;

-- tmp_deals: 매출 거래 seed (일자별 매출 개수만큼 행 생성)
DROP TEMPORARY TABLE IF EXISTS tmp_sales_deals;
CREATE TEMPORARY TABLE tmp_sales_deals (
  idx BIGINT AUTO_INCREMENT PRIMARY KEY,
  d DATE, customer_id VARCHAR(36), employee_id VARCHAR(36),
  flow VARCHAR(20),  -- 'direct' | 'order_only' | 'quote_order' | 'quote_only'
  item_count INT
) ENGINE=Memory;

INSERT INTO tmp_sales_deals (d, customer_id, employee_id, flow, item_count)
SELECT
  td.d,
  (SELECT partner_id FROM tmp_customers WHERE rn = ((CRC32(CONCAT(td.d, nn.n, 'c')) MOD 400) + 1)),
  (SELECT employee_id FROM tmp_sales_emp WHERE rn = ((CRC32(CONCAT(td.d, nn.n, 'e')) MOD 4) + 1)),
  ELT((CRC32(CONCAT(td.d, nn.n, 'f')) MOD 100) + 1,
    -- 1~60: direct, 61~75: order_only, 76~90: quote_order, 91~100: quote_only
    'direct','direct','direct','direct','direct','direct','direct','direct','direct','direct',
    'direct','direct','direct','direct','direct','direct','direct','direct','direct','direct',
    'direct','direct','direct','direct','direct','direct','direct','direct','direct','direct',
    'direct','direct','direct','direct','direct','direct','direct','direct','direct','direct',
    'direct','direct','direct','direct','direct','direct','direct','direct','direct','direct',
    'direct','direct','direct','direct','direct','direct','direct','direct','direct','direct',
    'order_only','order_only','order_only','order_only','order_only',
    'order_only','order_only','order_only','order_only','order_only',
    'order_only','order_only','order_only','order_only','order_only',
    'quote_order','quote_order','quote_order','quote_order','quote_order',
    'quote_order','quote_order','quote_order','quote_order','quote_order',
    'quote_order','quote_order','quote_order','quote_order','quote_order',
    'quote_only','quote_only','quote_only','quote_only','quote_only',
    'quote_only','quote_only','quote_only','quote_only','quote_only'
  ),
  ((CRC32(CONCAT(td.d, nn.n, 'ic')) MOD 5) + 1)  -- 1~5개 품목
FROM tmp_days td
CROSS JOIN tmp_n nn
WHERE nn.n < td.sales_count AND td.sales_count > 0;

SELECT CONCAT('✅ 매출 거래 시드: ', COUNT(*), '건') AS r FROM tmp_sales_deals;

-- ═══════════════════════════════════════════════════════════════════
-- 2. 견적 (quote_only + quote_order flow)
-- ═══════════════════════════════════════════════════════════════════
INSERT INTO quotations (quote_id, tenant_id, quote_no, partner_id, employee_id, quote_date, valid_until,
  status, total_amount, vat_amount, created_at, updated_at)
SELECT
  CONCAT('q-', LPAD(idx, 7, '0')),
  @tenant,
  CONCAT('Q-', DATE_FORMAT(d, '%y%m%d'), '-', LPAD(idx MOD 10000, 4, '0')),
  customer_id, employee_id, d,
  DATE_ADD(d, INTERVAL 14 DAY),
  CASE flow WHEN 'quote_order' THEN 'converted'
            WHEN 'quote_only' THEN ELT((CRC32(idx) MOD 3) + 1, 'submitted','accepted','rejected')
       END,
  0, 0,  -- 아래에서 item 합계 sync
  TIMESTAMP(d, '09:30:00'), TIMESTAMP(d, '09:30:00')
FROM tmp_sales_deals
WHERE flow IN ('quote_only','quote_order');

SELECT CONCAT('✅ 견적: ', COUNT(*), '건') AS r FROM quotations WHERE tenant_id=@tenant;

-- 견적 품목
INSERT INTO quotation_items (id, quote_id, item_id, unit, qty, unit_price, amount, vat_amount, sort_order)
SELECT
  UUID(),
  CONCAT('q-', LPAD(sd.idx, 7, '0')),
  ti.item_id,
  ELT((CRC32(CONCAT(sd.idx, n.n)) MOD 3) + 1, 'EA','SET','BOX'),
  ((CRC32(CONCAT(sd.idx, n.n, 'q')) MOD 20) + 1),  -- 1~20 개
  ti.sale_price,
  ((CRC32(CONCAT(sd.idx, n.n, 'q')) MOD 20) + 1) * ti.sale_price,
  ROUND(((CRC32(CONCAT(sd.idx, n.n, 'q')) MOD 20) + 1) * ti.sale_price * 0.1, 0),
  n.n + 1
FROM tmp_sales_deals sd
CROSS JOIN tmp_n n
JOIN tmp_items ti ON ti.rn = ((CRC32(CONCAT(sd.idx, n.n, 'i')) MOD 1000) + 1)
WHERE sd.flow IN ('quote_only','quote_order') AND n.n < sd.item_count;

-- 견적 헤더 총액 sync
UPDATE quotations q
JOIN (SELECT quote_id, SUM(amount) s, SUM(vat_amount) v FROM quotation_items GROUP BY quote_id) qi ON qi.quote_id=q.quote_id
SET q.total_amount=qi.s, q.vat_amount=qi.v;

-- ═══════════════════════════════════════════════════════════════════
-- 3. 수주 (order_only + quote_order)
-- ═══════════════════════════════════════════════════════════════════
INSERT INTO sales_orders (order_id, tenant_id, order_no, partner_id, employee_id, order_date, delivery_date,
  status, total_amount, vat_amount, created_at, updated_at)
SELECT
  CONCAT('so-', LPAD(idx, 7, '0')),
  @tenant,
  CONCAT('SO-', DATE_FORMAT(d, '%y%m%d'), '-', LPAD(idx MOD 10000, 4, '0')),
  customer_id, employee_id, d,
  DATE_ADD(d, INTERVAL ((CRC32(idx) MOD 7) + 1) DAY),
  CASE WHEN CRC32(idx) MOD 100 < 95 THEN 'invoiced'
       ELSE ELT((CRC32(idx) MOD 2) + 1, 'confirmed','cancelled') END,
  0, 0,
  TIMESTAMP(d, '10:15:00'), TIMESTAMP(d, '10:15:00')
FROM tmp_sales_deals
WHERE flow IN ('order_only','quote_order');

SELECT CONCAT('✅ 수주: ', COUNT(*), '건') AS r FROM sales_orders WHERE tenant_id=@tenant;

-- 수주 품목
INSERT INTO sales_order_items (order_item_id, order_id, tenant_id, item_id, ordered_qty, delivered_qty,
  unit_price, supply_amount, vat_amount, item_status)
SELECT
  UUID(),
  CONCAT('so-', LPAD(sd.idx, 7, '0')),
  @tenant,
  ti.item_id,
  ((CRC32(CONCAT(sd.idx, n.n, 'q')) MOD 20) + 1),
  ((CRC32(CONCAT(sd.idx, n.n, 'q')) MOD 20) + 1),  -- 전량 출고
  ti.sale_price,
  ((CRC32(CONCAT(sd.idx, n.n, 'q')) MOD 20) + 1) * ti.sale_price,
  ROUND(((CRC32(CONCAT(sd.idx, n.n, 'q')) MOD 20) + 1) * ti.sale_price * 0.1, 0),
  'delivered'
FROM tmp_sales_deals sd
CROSS JOIN tmp_n n
JOIN tmp_items ti ON ti.rn = ((CRC32(CONCAT(sd.idx, n.n, 'i')) MOD 1000) + 1)
WHERE sd.flow IN ('order_only','quote_order') AND n.n < sd.item_count;

UPDATE sales_orders o
JOIN (SELECT order_id, SUM(supply_amount) s, SUM(vat_amount) v FROM sales_order_items GROUP BY order_id) oi ON oi.order_id=o.order_id
SET o.total_amount=oi.s, o.vat_amount=oi.v;

-- ═══════════════════════════════════════════════════════════════════
-- 4. 거래명세서 (direct + order_only + quote_order)
-- ═══════════════════════════════════════════════════════════════════
INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, order_id, partner_id, employee_id,
  delivery_date, source_type, status, total_amount, vat_amount, created_at, updated_at)
SELECT
  CONCAT('sd-', LPAD(idx, 7, '0')),
  @tenant,
  CONCAT('DN-', DATE_FORMAT(d, '%y%m%d'), '-', LPAD(idx MOD 10000, 4, '0')),
  CASE WHEN flow IN ('order_only','quote_order') THEN CONCAT('so-', LPAD(idx, 7, '0')) END,
  customer_id, employee_id, d,
  CASE WHEN flow = 'direct' THEN 'direct' ELSE 'from_order' END,
  CASE WHEN CRC32(idx) MOD 100 < 97 THEN 'confirmed' ELSE 'draft' END,
  0, 0,
  TIMESTAMP(d, '14:20:00'), TIMESTAMP(d, '14:20:00')
FROM tmp_sales_deals
WHERE flow IN ('direct','order_only','quote_order');

SELECT CONCAT('✅ 거래명세서: ', COUNT(*), '건') AS r FROM sales_deliveries WHERE tenant_id=@tenant;

-- 거래명세서 품목
INSERT INTO sales_delivery_items (delivery_item_id, delivery_id, tenant_id, item_id, warehouse_id,
  qty, unit_price, supply_amount, vat_amount)
SELECT
  UUID(),
  CONCAT('sd-', LPAD(sd.idx, 7, '0')),
  @tenant,
  ti.item_id,
  -- 창고 분산: 60% main, 25% sub1, 15% sub2
  CASE WHEN CRC32(CONCAT(sd.idx, n.n)) MOD 100 < 60 THEN 'wh-main'
       WHEN CRC32(CONCAT(sd.idx, n.n)) MOD 100 < 85 THEN 'wh-sub1'
       ELSE 'wh-sub2' END,
  -- promo 상품은 1+1이니 qty 2배
  ((CRC32(CONCAT(sd.idx, n.n, 'q')) MOD 20) + 1) * (CASE WHEN ti.item_type='promo' THEN 2 ELSE 1 END),
  ti.sale_price,
  ((CRC32(CONCAT(sd.idx, n.n, 'q')) MOD 20) + 1) * (CASE WHEN ti.item_type='promo' THEN 2 ELSE 1 END) * ti.sale_price,
  ROUND(((CRC32(CONCAT(sd.idx, n.n, 'q')) MOD 20) + 1) * (CASE WHEN ti.item_type='promo' THEN 2 ELSE 1 END) * ti.sale_price * 0.1, 0)
FROM tmp_sales_deals sd
CROSS JOIN tmp_n n
JOIN tmp_items ti ON ti.rn = ((CRC32(CONCAT(sd.idx, n.n, 'i')) MOD 1000) + 1)
WHERE sd.flow IN ('direct','order_only','quote_order') AND n.n < sd.item_count;

-- 헤더 총액 sync
UPDATE sales_deliveries sd
JOIN (SELECT delivery_id, SUM(supply_amount) s, SUM(vat_amount) v FROM sales_delivery_items GROUP BY delivery_id) sdi ON sdi.delivery_id=sd.delivery_id
SET sd.total_amount=sdi.s, sd.vat_amount=sdi.v;

SELECT CONCAT('✅ 매출 품목: ', COUNT(*), '건') AS r FROM sales_delivery_items WHERE tenant_id=@tenant;
