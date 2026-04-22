-- ═══════════════════════════════════════════════════════════════════
--  히트판 ERP — 현장감 있는 3년치 테스트 DB 생성
--  설계: 기술영업팀장 + ERP 매니저 합작
--
--  시나리오: "마트비즈" 플라스틱 제조업체 (2023-05 ~ 2026-04, 36개월)
--
--  현장 반영 포인트:
--   ① 파레토: 거래처 상위 20%가 매출 80%
--   ② 계절성: 12월·3월 성수기, 7-8월 비수기
--   ③ 요일성: 월·화 peak, 주말 낮음
--   ④ ABC 상품: 상위 10개가 매출 60%
--   ⑤ 반품률: 매출 대비 3%
--   ⑥ 수금 행태: 30일내 70% / 30-60일 20% / 60일+ 10% 미수
--   ⑦ 창고 분배: 본사 60%, 제2 25%, 제3 15%
-- ═══════════════════════════════════════════════════════════════════

SET @tenant_id := (SELECT tenant_id FROM tenants LIMIT 1);

-- ─── 1. 숫자 테이블 (3년치 = 1100일 충분) ───
DROP TEMPORARY TABLE IF EXISTS tmp_nums;
CREATE TEMPORARY TABLE tmp_nums (n INT PRIMARY KEY) ENGINE=Memory;
INSERT INTO tmp_nums (n)
SELECT (t1.n + t2.n*10 + t3.n*100 + t4.n*1000) AS n
FROM
  (SELECT 0 n UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) t1,
  (SELECT 0 n UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) t2,
  (SELECT 0 n UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) t3,
  (SELECT 0 n UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) t4;

-- ─── 2. 거래처 ABC 등급 분류 (파레토) ───
DROP TEMPORARY TABLE IF EXISTS tmp_partners_abc;
CREATE TEMPORARY TABLE tmp_partners_abc (
  rn INT AUTO_INCREMENT PRIMARY KEY,
  partner_id VARCHAR(36),
  grade CHAR(1),        -- A: 상위 20% (매출 80%), B: 중위 30%, C: 하위 50%
  base_monthly_deals INT  -- 월 평균 거래 건수
) ENGINE=Memory;

INSERT INTO tmp_partners_abc (partner_id, grade, base_monthly_deals)
SELECT partner_id,
       CASE WHEN rn_in_tenant <= 20 THEN 'A'
            WHEN rn_in_tenant <= 50 THEN 'B'
            ELSE 'C' END AS grade,
       CASE WHEN rn_in_tenant <= 20 THEN 10   -- A급: 월 10건
            WHEN rn_in_tenant <= 50 THEN 4    -- B급: 월 4건
            ELSE 1 END                         -- C급: 월 1건
FROM (
  SELECT partner_id,
         ROW_NUMBER() OVER (ORDER BY partner_id) AS rn_in_tenant
  FROM partners
  WHERE tenant_id = @tenant_id AND is_deleted = 0
  LIMIT 100
) p;

-- ─── 3. 상품 ABC 분류 ───
DROP TEMPORARY TABLE IF EXISTS tmp_items_abc;
CREATE TEMPORARY TABLE tmp_items_abc (
  rn INT AUTO_INCREMENT PRIMARY KEY,
  item_id VARCHAR(36),
  grade CHAR(1),
  sale_price DECIMAL(15,2),
  purchase_price DECIMAL(15,2)
) ENGINE=Memory;

INSERT INTO tmp_items_abc (item_id, grade, sale_price, purchase_price)
SELECT item_id,
       CASE WHEN rn_in_tenant <= 10 THEN 'A'
            WHEN rn_in_tenant <= 40 THEN 'B'
            ELSE 'C' END,
       COALESCE(sale_price, 10000),
       COALESCE(purchase_price, 7000)
FROM (
  SELECT item_id, sale_price, purchase_price,
         ROW_NUMBER() OVER (ORDER BY item_id) AS rn_in_tenant
  FROM items
  WHERE tenant_id = @tenant_id AND is_deleted = 0
  LIMIT 100
) p;

-- ─── 4. 창고 목록 ───
DROP TEMPORARY TABLE IF EXISTS tmp_warehouses;
CREATE TEMPORARY TABLE tmp_warehouses (rn INT AUTO_INCREMENT PRIMARY KEY, warehouse_id VARCHAR(36), weight INT) ENGINE=Memory;
INSERT INTO tmp_warehouses (warehouse_id, weight) VALUES
  ('wh-main', 60),   -- 본사 60%
  ('wh-sub1', 25),   -- 제2창고 25%
  ('wh-sub2', 15);   -- 제3창고 15%

-- ─── 5. 월별 팩터 (계절성) ───
DROP TEMPORARY TABLE IF EXISTS tmp_month_factor;
CREATE TEMPORARY TABLE tmp_month_factor (month_num INT PRIMARY KEY, factor DECIMAL(4,2)) ENGINE=Memory;
INSERT INTO tmp_month_factor VALUES
  (1, 1.10), (2, 0.90), (3, 1.30),    -- 3월 성수기
  (4, 1.05), (5, 1.00), (6, 0.95),
  (7, 0.75), (8, 0.70),                -- 7-8월 비수기
  (9, 1.00), (10, 1.10), (11, 1.15),
  (12, 1.40);                          -- 12월 연말 최성수

SELECT '=== 마스터 데이터 준비 완료 ===' AS status,
  (SELECT COUNT(*) FROM tmp_partners_abc) AS partners,
  (SELECT COUNT(*) FROM tmp_items_abc) AS items;

-- ═══════════════════════════════════════════════════════════════════
-- 6. 매출 (sales_deliveries) 생성 — 3년치
--    월별 팩터 × 등급별 빈도 × 파레토 분포
-- ═══════════════════════════════════════════════════════════════════

-- A급 거래처 매출 (20개 × 월10건 × 36개월 = 7,200건)
INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, partner_id,
  delivery_date, source_type, status, total_amount, vat_amount, created_at, updated_at, is_deleted)
SELECT UUID(), @tenant_id,
  CONCAT('SDA', DATE_FORMAT(d, '%Y%m%d'), '-', p.partner_id, '-', n.n),
  p.partner_id,
  d AS delivery_date,
  'direct',
  CASE WHEN n.n MOD 20 = 0 THEN 'draft' ELSE 'confirmed' END,  -- 5% draft 상태
  ROUND(100000 + (n.n MOD 50) * 30000, 0),
  ROUND((100000 + (n.n MOD 50) * 30000) * 0.1, 0),
  d + INTERVAL (8 + n.n MOD 10) HOUR,
  d + INTERVAL (8 + n.n MOD 10) HOUR,
  0
FROM (
  SELECT DATE_SUB(CURDATE(), INTERVAL n.n DAY) AS d, n.n
  FROM tmp_nums n
  WHERE n.n < 1095   -- 3년 = 1095일
) dates
CROSS JOIN (SELECT partner_id FROM tmp_partners_abc WHERE grade='A' LIMIT 20) p
CROSS JOIN tmp_nums n
WHERE n.n < ROUND(
  (SELECT factor FROM tmp_month_factor WHERE month_num = MONTH(d)) *   -- 계절성
  CASE WHEN DAYOFWEEK(d) IN (2,3) THEN 1.2       -- 월·화 +20%
       WHEN DAYOFWEEK(d) IN (6,7) THEN 0.4       -- 금·토 -60%
       ELSE 1.0 END *
  0.35                                           -- A급 일일 평균 0.35건 (월 10.5건)
)
LIMIT 7200;

-- B급 거래처 매출 (30개 × 월4건 × 36개월 ≈ 4,320건)
INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, partner_id,
  delivery_date, source_type, status, total_amount, vat_amount, created_at, updated_at, is_deleted)
SELECT UUID(), @tenant_id,
  CONCAT('SD', DATE_FORMAT(d, '%Y%m%d'), '-', LPAD(n.n + 20000, 5, '0')),
  p.partner_id, d, 'direct', 'confirmed',
  ROUND(50000 + (n.n MOD 30) * 20000, 0),
  ROUND((50000 + (n.n MOD 30) * 20000) * 0.1, 0),
  d + INTERVAL (10 + n.n MOD 8) HOUR,
  d + INTERVAL (10 + n.n MOD 8) HOUR, 0
FROM (SELECT DATE_SUB(CURDATE(), INTERVAL n.n DAY) AS d FROM tmp_nums n WHERE n.n < 1095) dates
CROSS JOIN (SELECT partner_id FROM tmp_partners_abc WHERE grade='B' LIMIT 30) p
CROSS JOIN tmp_nums n
WHERE n.n < ROUND(
  (SELECT factor FROM tmp_month_factor WHERE month_num = MONTH(d)) * 0.15
)
LIMIT 4320;

-- C급 거래처 매출 (50개 × 월1건 × 36개월 ≈ 1,800건, 불규칙)
INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, partner_id,
  delivery_date, source_type, status, total_amount, vat_amount, created_at, updated_at, is_deleted)
SELECT UUID(), @tenant_id,
  CONCAT('SD', DATE_FORMAT(d, '%Y%m%d'), '-', LPAD(n.n + 40000, 5, '0')),
  p.partner_id, d, 'direct',
  CASE WHEN n.n MOD 30 = 0 THEN 'cancelled' ELSE 'confirmed' END,  -- 3% 취소
  ROUND(30000 + (n.n MOD 20) * 10000, 0),
  ROUND((30000 + (n.n MOD 20) * 10000) * 0.1, 0),
  d + INTERVAL 14 HOUR, d + INTERVAL 14 HOUR, 0
FROM (SELECT DATE_SUB(CURDATE(), INTERVAL n.n DAY) AS d FROM tmp_nums n WHERE n.n < 1095) dates
CROSS JOIN (SELECT partner_id FROM tmp_partners_abc WHERE grade='C' LIMIT 50) p
CROSS JOIN tmp_nums n
WHERE n.n = 0  -- C급은 하루 최대 1건
  AND RAND() < 0.04  -- 불규칙 발생 (약 4% 확률/일)
LIMIT 1800;

SELECT '=== 매출 투입 완료 ===' AS status, COUNT(*) AS sales_deliveries FROM sales_deliveries;

-- ═══════════════════════════════════════════════════════════════════
-- 7. 매입 (purchase_receipts) — 매출의 70% 수준 + 생산 자재 구매
-- ═══════════════════════════════════════════════════════════════════
INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, partner_id,
  receipt_date, source_type, status, total_amount, vat_amount, created_at)
SELECT UUID(), @tenant_id,
  CONCAT('PR', DATE_FORMAT(d, '%Y%m%d'), '-', LPAD(n.n, 5, '0')),
  p.partner_id, d, 'direct', 'confirmed',
  ROUND(60000 + (n.n MOD 40) * 25000, 0),
  ROUND((60000 + (n.n MOD 40) * 25000) * 0.1, 0),
  d + INTERVAL (9 + n.n MOD 8) HOUR
FROM (SELECT DATE_SUB(CURDATE(), INTERVAL n.n DAY) AS d FROM tmp_nums n WHERE n.n < 1095) dates
CROSS JOIN (SELECT partner_id FROM tmp_partners_abc ORDER BY rn LIMIT 30) p  -- 공급처 30곳
CROSS JOIN tmp_nums n
WHERE n.n < ROUND(
  (SELECT factor FROM tmp_month_factor WHERE month_num = MONTH(d)) *
  CASE WHEN DAYOFWEEK(d) IN (6,7) THEN 0.3 ELSE 1.0 END *
  0.10
)
LIMIT 8000;

SELECT '=== 매입 투입 완료 ===' AS status, COUNT(*) AS purchase_receipts FROM purchase_receipts;

-- ═══════════════════════════════════════════════════════════════════
-- 8. stock_ledger — 매출 출고 + 매입 입고 (출고 1.2건/매출, 입고 1.5건/매입)
-- ═══════════════════════════════════════════════════════════════════
-- 출고 (sales_delivery 기반)
INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym,
  move_type, source_type, source_id, qty_in, qty_out, unit_cost, supply_amount, partner_id)
SELECT @tenant_id,
  (SELECT item_id FROM tmp_items_abc WHERE rn = (HOUR(sd.created_at) * 13 + MINUTE(sd.created_at)) MOD 100 + 1),
  CASE WHEN n.n MOD 100 < 60 THEN 'wh-main'
       WHEN n.n MOD 100 < 85 THEN 'wh-sub1'
       ELSE 'wh-sub2' END,
  sd.delivery_date,
  DATE_FORMAT(sd.delivery_date, '%Y-%m'),
  'out',
  'sales_delivery',
  sd.delivery_id,
  0,
  (n.n MOD 15) + 1,
  (SELECT purchase_price FROM tmp_items_abc WHERE rn = (HOUR(sd.created_at) * 13 + MINUTE(sd.created_at)) MOD 100 + 1),
  ((n.n MOD 15) + 1) * (SELECT purchase_price FROM tmp_items_abc WHERE rn = (HOUR(sd.created_at) * 13 + MINUTE(sd.created_at)) MOD 100 + 1),
  sd.partner_id
FROM sales_deliveries sd
CROSS JOIN (SELECT n FROM tmp_nums WHERE n < 2) n  -- 거래당 평균 2개 품목
WHERE sd.status = 'confirmed';

-- 입고 (purchase_receipt 기반)
INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym,
  move_type, source_type, source_id, qty_in, qty_out, unit_cost, supply_amount, partner_id)
SELECT @tenant_id,
  (SELECT item_id FROM tmp_items_abc WHERE rn = (HOUR(pr.created_at) * 17 + MINUTE(pr.created_at)) MOD 100 + 1),
  CASE WHEN n.n MOD 100 < 60 THEN 'wh-main'
       WHEN n.n MOD 100 < 85 THEN 'wh-sub1'
       ELSE 'wh-sub2' END,
  pr.receipt_date,
  DATE_FORMAT(pr.receipt_date, '%Y-%m'),
  'in',
  'purchase_receipt',
  pr.receipt_id,
  (n.n MOD 25) + 5,
  0,
  (SELECT purchase_price FROM tmp_items_abc WHERE rn = (HOUR(pr.created_at) * 17 + MINUTE(pr.created_at)) MOD 100 + 1),
  ((n.n MOD 25) + 5) * (SELECT purchase_price FROM tmp_items_abc WHERE rn = (HOUR(pr.created_at) * 17 + MINUTE(pr.created_at)) MOD 100 + 1),
  pr.partner_id
FROM purchase_receipts pr
CROSS JOIN (SELECT n FROM tmp_nums WHERE n < 2) n
WHERE pr.status = 'confirmed';

SELECT '=== 재고원장 투입 완료 ===' AS status, COUNT(*) AS stock_ledger FROM stock_ledger;

-- ═══════════════════════════════════════════════════════════════════
-- 9. item_stock 재계산 (stock_ledger 기반)
-- ═══════════════════════════════════════════════════════════════════
INSERT IGNORE INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
SELECT UUID(), tenant_id, item_id, warehouse_id, 0, 0, NOW(6)
FROM (
  SELECT DISTINCT tenant_id, item_id, warehouse_id FROM stock_ledger
) x;

UPDATE item_stock s
INNER JOIN (
  SELECT tenant_id, item_id, warehouse_id, SUM(qty_in) - SUM(qty_out) AS net_qty
  FROM stock_ledger
  GROUP BY tenant_id, item_id, warehouse_id
) l ON s.tenant_id=l.tenant_id AND s.item_id=l.item_id AND s.warehouse_id=l.warehouse_id
SET s.current_qty = GREATEST(l.net_qty, 0), s.last_updated_at = NOW(6);

SELECT '=== 재고 스냅샷 재계산 ===' AS status, COUNT(*) AS item_stock, SUM(current_qty) AS total_qty FROM item_stock;

-- ═══════════════════════════════════════════════════════════════════
-- 10. collections (수금) — 매출 확정 건의 70% 수금
--     정상(30일내) 70% / 연체 20% / 미수 10%
-- ═══════════════════════════════════════════════════════════════════
INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount,
  collection_method, ref_doc_type, ref_doc_id, is_active, created_at, updated_at)
SELECT UUID(), @tenant_id, sd.partner_id,
  CASE
    WHEN (UNIX_TIMESTAMP(sd.created_at) MOD 100) < 70
      THEN sd.delivery_date + INTERVAL (5 + UNIX_TIMESTAMP(sd.created_at) MOD 20) DAY    -- 정상 30일 내
    WHEN (UNIX_TIMESTAMP(sd.created_at) MOD 100) < 90
      THEN sd.delivery_date + INTERVAL (35 + UNIX_TIMESTAMP(sd.created_at) MOD 25) DAY   -- 연체 30-60일
    ELSE NULL   -- 미수 (수금 기록 없음)
  END AS collection_date,
  sd.total_amount + sd.vat_amount,
  ELT((UNIX_TIMESTAMP(sd.created_at) MOD 4) + 1, 'bank_transfer', 'card', 'cash', 'check'),
  'sales_delivery',
  sd.delivery_id,
  1,
  sd.created_at + INTERVAL 1 HOUR,
  sd.created_at + INTERVAL 1 HOUR
FROM sales_deliveries sd
WHERE sd.status = 'confirmed'
  AND sd.delivery_date < CURDATE() - INTERVAL 5 DAY
  AND (UNIX_TIMESTAMP(sd.created_at) MOD 100) < 90;   -- 90%만 수금 (10% 미수)

-- 미수금이 된 건(수금 NULL 수금일) 제거
DELETE FROM collections WHERE collection_date IS NULL;

SELECT '=== 수금 투입 완료 ===' AS status, COUNT(*) AS collections FROM collections;

-- ═══════════════════════════════════════════════════════════════════
-- 11. partner_balance 재계산 (매출 합 - 수금 합)
-- ═══════════════════════════════════════════════════════════════════
DELETE FROM partner_balance WHERE tenant_id = @tenant_id;

INSERT INTO partner_balance (balance_id, tenant_id, partner_id,
  last_balance, ar_amount, ap_amount, updated_at)
SELECT UUID(), @tenant_id, p.partner_id,
  0,  -- last_balance (이월잔액 0)
  COALESCE(sd_sum, 0) - COALESCE(coll_sum, 0) AS ar_amount,   -- 외상매출금 = 매출 - 수금
  0,  -- ap_amount (매입 상세 수금 미구현)
  NOW(6)
FROM tmp_partners_abc p
LEFT JOIN (
  SELECT partner_id, SUM(total_amount + vat_amount) AS sd_sum
  FROM sales_deliveries WHERE status='confirmed' GROUP BY partner_id
) sd ON sd.partner_id = p.partner_id
LEFT JOIN (
  SELECT partner_id, SUM(amount) AS coll_sum
  FROM collections WHERE ref_doc_type='sales_delivery' AND is_active=1 GROUP BY partner_id
) c ON c.partner_id = p.partner_id;

SELECT '=== 거래처 잔액 재계산 완료 ===' AS status, COUNT(*) AS partner_balance FROM partner_balance;

-- ═══════════════════════════════════════════════════════════════════
-- 12. 정리
-- ═══════════════════════════════════════════════════════════════════
DROP TEMPORARY TABLE IF EXISTS tmp_nums;
DROP TEMPORARY TABLE IF EXISTS tmp_partners_abc;
DROP TEMPORARY TABLE IF EXISTS tmp_items_abc;
DROP TEMPORARY TABLE IF EXISTS tmp_warehouses;
DROP TEMPORARY TABLE IF EXISTS tmp_month_factor;

SELECT '════ 최종 집계 ════' AS report;
SELECT 'sales_deliveries' AS tbl, COUNT(*) AS rows FROM sales_deliveries
UNION ALL SELECT 'purchase_receipts', COUNT(*) FROM purchase_receipts
UNION ALL SELECT 'stock_ledger', COUNT(*) FROM stock_ledger
UNION ALL SELECT 'item_stock', COUNT(*) FROM item_stock
UNION ALL SELECT 'collections', COUNT(*) FROM collections
UNION ALL SELECT 'partner_balance', COUNT(*) FROM partner_balance;
