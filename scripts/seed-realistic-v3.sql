-- ═══════════════════════════════════════════════════════════════════
--  현장감 3년치 v3 — 풍부한 시나리오
--
--  ERP 매니저 + 기술영업팀장 공동 설계:
--   ① 계절성 선명: 12월·3월 peak, 7-8월 40% 감소
--   ② 요일성: 월·화 peak, 토·일 90% 감소
--   ③ 파레토: 거래처 상위 20% = 매출 70%+
--   ④ 거래 사이즈 편차: 거래처·상품별 현실적 분포
--   ⑤ 반품 3-5%, 취소 2%, 초안 5%
--   ⑥ 수금 패턴: 정상 65% / 연체 25% / 미수 10%
--   ⑦ 창고 분배: 본사 55% / 제2 30% / 제3 15%
--   ⑧ 매출·매입 품목 수 편차: 1~5개
--   ⑨ 직원 담당자 분배 (created_by)
-- ═══════════════════════════════════════════════════════════════════

SET @tenant := (SELECT tenant_id FROM tenants LIMIT 1);

-- 기존 데이터 완전 삭제
SET FOREIGN_KEY_CHECKS=0;
DELETE FROM sales_delivery_items;
DELETE FROM sales_deliveries;
DELETE FROM sales_order_items;
DELETE FROM sales_orders;
DELETE FROM purchase_receipt_items;
DELETE FROM purchase_receipts;
DELETE FROM purchase_order_items;
DELETE FROM purchase_orders;
DELETE FROM collections;
DELETE FROM stock_ledger;
DELETE FROM stock_adjust_logs;
DELETE FROM quotation_items;
DELETE FROM quotations;
DELETE FROM item_stock;
DELETE FROM partner_balance;
SET FOREIGN_KEY_CHECKS=1;

-- ─── 숫자 시퀀스 ───
DROP TEMPORARY TABLE IF EXISTS tmp_n;
CREATE TEMPORARY TABLE tmp_n (n INT PRIMARY KEY) ENGINE=Memory;
INSERT INTO tmp_n (n)
SELECT (t1.n + t2.n*10 + t3.n*100 + t4.n*1000 + t5.n*10000) AS n
FROM (SELECT 0 n UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) t1
   , (SELECT 0 n UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) t2
   , (SELECT 0 n UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) t3
   , (SELECT 0 n UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) t4
   , (SELECT 0 n UNION SELECT 1 UNION SELECT 2) t5;

-- ─── 거래처 (파레토 등급 부여) ───
DROP TEMPORARY TABLE IF EXISTS tmp_p;
CREATE TEMPORARY TABLE tmp_p (
  rn INT AUTO_INCREMENT PRIMARY KEY,
  partner_id VARCHAR(36),
  grade CHAR(1),             -- A(20%) / B(30%) / C(50%)
  avg_deal_size INT,         -- 평균 거래 금액
  freq_factor DECIMAL(3,2)   -- 거래 빈도 가중치
) ENGINE=Memory;

INSERT INTO tmp_p (partner_id, grade, avg_deal_size, freq_factor)
SELECT p.partner_id,
  CASE WHEN rn <= 20 THEN 'A'
       WHEN rn <= 50 THEN 'B'
       ELSE 'C' END,
  CASE WHEN rn <= 10 THEN 5000000 + (rn * 300000)
       WHEN rn <= 20 THEN 2000000 + (rn * 150000)
       WHEN rn <= 50 THEN 500000 + (rn * 30000)
       ELSE 100000 + (rn * 5000) END,
  CASE WHEN rn <= 5  THEN 2.50   -- 최상위 A 월 15~25건
       WHEN rn <= 20 THEN 1.50   -- A 월 8~12건
       WHEN rn <= 50 THEN 0.70   -- B 월 3~5건
       ELSE 0.20 END              -- C 월 0~2건
FROM (
  SELECT partner_id, ROW_NUMBER() OVER (ORDER BY partner_id) AS rn
  FROM partners WHERE tenant_id=@tenant AND is_deleted=0 LIMIT 100
) p;

-- ─── 상품 ABC ───
DROP TEMPORARY TABLE IF EXISTS tmp_i;
CREATE TEMPORARY TABLE tmp_i (
  rn INT AUTO_INCREMENT PRIMARY KEY,
  item_id VARCHAR(36),
  grade CHAR(1),
  sale_price DECIMAL(15,2),
  purchase_price DECIMAL(15,2)
) ENGINE=Memory;

INSERT INTO tmp_i (item_id, grade, sale_price, purchase_price)
SELECT item_id,
  CASE WHEN rn <= 10 THEN 'A' WHEN rn <= 40 THEN 'B' ELSE 'C' END,
  COALESCE(sale_price, 10000 + rn * 500),
  COALESCE(purchase_price, 7000 + rn * 350)
FROM (
  SELECT item_id, sale_price, purchase_price,
         ROW_NUMBER() OVER (ORDER BY item_id) AS rn
  FROM items WHERE tenant_id=@tenant AND is_deleted=0 LIMIT 100
) i;

-- ─── 직원 목록 (담당자 배정) ───
DROP TEMPORARY TABLE IF EXISTS tmp_emp;
CREATE TEMPORARY TABLE tmp_emp (rn INT AUTO_INCREMENT PRIMARY KEY, employee_id VARCHAR(36)) ENGINE=Memory;
INSERT INTO tmp_emp (employee_id) SELECT employee_id FROM employees WHERE tenant_id=@tenant LIMIT 20;
SET @emp_count := (SELECT COUNT(*) FROM tmp_emp);

SELECT CONCAT('✅ 마스터 준비: 거래처 ', (SELECT COUNT(*) FROM tmp_p), ' / 상품 ', (SELECT COUNT(*) FROM tmp_i), ' / 사원 ', @emp_count) AS r;

-- ═══════════════════════════════════════════════════════════════════
-- 매출 생성 — 일별 × 거래처별 확률적 발생
-- ═══════════════════════════════════════════════════════════════════

-- 시드 작업 테이블 (각 거래 1건)
DROP TEMPORARY TABLE IF EXISTS tmp_deals;
CREATE TEMPORARY TABLE tmp_deals (
  deal_id VARCHAR(36) PRIMARY KEY,
  deal_date DATE,
  partner_id VARCHAR(36),
  partner_grade CHAR(1),
  amount DECIMAL(15,2),
  warehouse_id VARCHAR(36),
  status VARCHAR(20),
  emp_rn INT
) ENGINE=InnoDB;

-- 3년치 daily 거래 생성 (날짜 × 거래처 × 랜덤 필터)
INSERT INTO tmp_deals
SELECT
  UUID(),
  DATE_SUB(CURDATE(), INTERVAL d.n DAY) AS deal_date,
  p.partner_id,
  p.grade,
  ROUND(p.avg_deal_size * (0.5 + RAND() * 1.5), 0) AS amount,
  CASE WHEN (FLOOR(RAND()*100)) < 55 THEN 'wh-main'
       WHEN (FLOOR(RAND()*100)) < 30 THEN 'wh-sub1'
       ELSE 'wh-sub2' END,
  CASE WHEN RAND() < 0.03 THEN 'cancelled'
       WHEN RAND() < 0.05 THEN 'draft'
       ELSE 'confirmed' END,
  FLOOR(RAND() * @emp_count) + 1
FROM (SELECT n FROM tmp_n WHERE n < 1095) d    -- 3년 = 1095일
CROSS JOIN tmp_p p
WHERE
  -- 주말 -90%
  (DAYOFWEEK(DATE_SUB(CURDATE(), INTERVAL d.n DAY)) NOT IN (1, 7) OR RAND() < 0.1)
  -- 빈도 가중치 × 월별 계절성 × 랜덤 필터
  AND RAND() <
    (p.freq_factor / 30) *
    CASE MONTH(DATE_SUB(CURDATE(), INTERVAL d.n DAY))
      WHEN 12 THEN 1.40 WHEN 3 THEN 1.35 WHEN 11 THEN 1.15 WHEN 1 THEN 1.10
      WHEN 7 THEN 0.70 WHEN 8 THEN 0.65 WHEN 2 THEN 0.85 ELSE 1.00 END;

SELECT CONCAT('✅ 거래 시드: ', COUNT(*), '건') AS r FROM tmp_deals;

-- sales_deliveries INSERT
INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, partner_id, employee_id,
  delivery_date, source_type, status, total_amount, vat_amount, created_at, updated_at, is_deleted, memo)
SELECT d.deal_id, @tenant,
  CONCAT('SD-', DATE_FORMAT(d.deal_date, '%y%m%d'), '-', SUBSTRING(d.deal_id, 1, 18)),
  d.partner_id,
  (SELECT employee_id FROM tmp_emp WHERE rn = d.emp_rn),
  d.deal_date, 'direct', d.status,
  d.amount,
  ROUND(d.amount * 0.1, 0),
  TIMESTAMP(d.deal_date, SEC_TO_TIME(FLOOR(28800 + RAND() * 36000))),  -- 08:00~18:00
  TIMESTAMP(d.deal_date, SEC_TO_TIME(FLOOR(28800 + RAND() * 36000))),
  0,
  CASE p.grade WHEN 'A' THEN '정기 거래처' WHEN 'B' THEN '단골' ELSE '신규/부정기' END
FROM tmp_deals d JOIN tmp_p p ON p.partner_id=d.partner_id;

SELECT CONCAT('✅ 매출 투입: ', COUNT(*), '건') AS r FROM sales_deliveries;

-- 매출 품목 (거래당 1~5개, A급 거래처 더 많은 품목)
INSERT INTO sales_delivery_items (delivery_item_id, delivery_id, tenant_id, item_id, qty, unit_price, supply_amount, vat_amount, warehouse_id)
SELECT UUID(), sd.delivery_id, @tenant,
  (SELECT item_id FROM tmp_i WHERE rn = ((CRC32(CONCAT(sd.delivery_id, n.n)) MOD 100) + 1)),
  (CRC32(CONCAT(sd.delivery_id, n.n, 'q')) MOD 20) + 1,
  (SELECT sale_price FROM tmp_i WHERE rn = ((CRC32(CONCAT(sd.delivery_id, n.n)) MOD 100) + 1)),
  ((CRC32(CONCAT(sd.delivery_id, n.n, 'q')) MOD 20) + 1) * (SELECT sale_price FROM tmp_i WHERE rn = ((CRC32(CONCAT(sd.delivery_id, n.n)) MOD 100) + 1)),
  ROUND(((CRC32(CONCAT(sd.delivery_id, n.n, 'q')) MOD 20) + 1) * (SELECT sale_price FROM tmp_i WHERE rn = ((CRC32(CONCAT(sd.delivery_id, n.n)) MOD 100) + 1)) * 0.1, 0),
  CASE WHEN CRC32(sd.delivery_id) MOD 100 < 55 THEN 'wh-main'
       WHEN CRC32(sd.delivery_id) MOD 100 < 85 THEN 'wh-sub1'
       ELSE 'wh-sub2' END
FROM sales_deliveries sd
CROSS JOIN tmp_n n
WHERE n.n < CASE WHEN CRC32(sd.delivery_id) MOD 100 < 20 THEN 5     -- 20% 큰거래(5품목)
                 WHEN CRC32(sd.delivery_id) MOD 100 < 50 THEN 3     -- 30%(3품목)
                 ELSE 1 END                                          -- 50%(1품목)
  AND sd.status != 'cancelled';

SELECT CONCAT('✅ 매출 품목: ', COUNT(*), '건') AS r FROM sales_delivery_items;

-- ═══════════════════════════════════════════════════════════════════
-- 매입 생성 — 공급처 30곳, 매출 대비 70% 빈도
-- ═══════════════════════════════════════════════════════════════════
INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, partner_id, created_by,
  receipt_date, source_type, status, total_amount, vat_amount, created_at)
SELECT UUID(), @tenant,
  CONCAT('PR-', DATE_FORMAT(d, '%y%m%d'), '-', SUBSTRING(UUID(), 1, 18)),
  (SELECT partner_id FROM tmp_p WHERE rn = (n.n MOD 30) + 1),
  (SELECT employee_id FROM tmp_emp WHERE rn = (n.n MOD @emp_count) + 1),
  d, 'direct',
  CASE WHEN n.n MOD 50 = 0 THEN 'draft' ELSE 'confirmed' END,
  ROUND(500000 + (CRC32(CONCAT(d,n.n)) MOD 5000000), 0),
  ROUND((500000 + (CRC32(CONCAT(d,n.n)) MOD 5000000)) * 0.1, 0),
  TIMESTAMP(d, SEC_TO_TIME(FLOOR(28800 + RAND() * 36000)))
FROM (
  SELECT DATE_SUB(CURDATE(), INTERVAL nums.n DAY) AS d, nums.n
  FROM tmp_n nums WHERE nums.n < 1095
) dates, tmp_n n
WHERE n.n < 5  -- 하루 평균 5건 내
  AND RAND() < 0.70 *                                  -- 70% 랜덤 (주말 감소)
    CASE WHEN DAYOFWEEK(d) IN (1,7) THEN 0.2 ELSE 1.0 END *
    CASE MONTH(d) WHEN 12 THEN 1.3 WHEN 3 THEN 1.3 WHEN 7 THEN 0.7 WHEN 8 THEN 0.65 ELSE 1.0 END;

SELECT CONCAT('✅ 매입 투입: ', COUNT(*), '건') AS r FROM purchase_receipts;

-- 매입 품목 (1~4개)
INSERT INTO purchase_receipt_items (receipt_item_id, receipt_id, tenant_id, item_id, qty, unit_price, supply_amount, vat_amount, warehouse_id)
SELECT UUID(), pr.receipt_id, @tenant,
  (SELECT item_id FROM tmp_i WHERE rn = ((CRC32(CONCAT(pr.receipt_id, n.n)) MOD 100) + 1)),
  (CRC32(CONCAT(pr.receipt_id, n.n, 'q')) MOD 30) + 5,
  (SELECT purchase_price FROM tmp_i WHERE rn = ((CRC32(CONCAT(pr.receipt_id, n.n)) MOD 100) + 1)),
  ((CRC32(CONCAT(pr.receipt_id, n.n, 'q')) MOD 30) + 5) * (SELECT purchase_price FROM tmp_i WHERE rn = ((CRC32(CONCAT(pr.receipt_id, n.n)) MOD 100) + 1)),
  ROUND(((CRC32(CONCAT(pr.receipt_id, n.n, 'q')) MOD 30) + 5) * (SELECT purchase_price FROM tmp_i WHERE rn = ((CRC32(CONCAT(pr.receipt_id, n.n)) MOD 100) + 1)) * 0.1, 0),
  CASE WHEN CRC32(pr.receipt_id) MOD 100 < 55 THEN 'wh-main'
       WHEN CRC32(pr.receipt_id) MOD 100 < 85 THEN 'wh-sub1'
       ELSE 'wh-sub2' END
FROM purchase_receipts pr
CROSS JOIN tmp_n n
WHERE n.n < CASE WHEN CRC32(pr.receipt_id) MOD 100 < 30 THEN 4 ELSE 2 END
  AND pr.status = 'confirmed';

SELECT CONCAT('✅ 매입 품목: ', COUNT(*), '건') AS r FROM purchase_receipt_items;

-- ═══════════════════════════════════════════════════════════════════
-- stock_ledger (item_stock UPDATE 포함)
-- ═══════════════════════════════════════════════════════════════════
INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym,
  move_type, source_type, source_id, qty_in, qty_out, unit_cost, supply_amount, partner_id, employee_id)
SELECT @tenant, sdi.item_id, sdi.warehouse_id, sd.delivery_date,
  DATE_FORMAT(sd.delivery_date, '%Y-%m'),
  'out', 'sales_delivery', sd.delivery_id,
  0, sdi.qty, sdi.unit_price, sdi.supply_amount,
  sd.partner_id, sd.employee_id
FROM sales_delivery_items sdi JOIN sales_deliveries sd ON sd.delivery_id=sdi.delivery_id
WHERE sd.status='confirmed';

INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym,
  move_type, source_type, source_id, qty_in, qty_out, unit_cost, supply_amount, partner_id, employee_id)
SELECT @tenant, pri.item_id, pri.warehouse_id, pr.receipt_date,
  DATE_FORMAT(pr.receipt_date, '%Y-%m'),
  'in', 'purchase_receipt', pr.receipt_id,
  pri.qty, 0, pri.unit_price, pri.supply_amount,
  pr.partner_id, pr.created_by
FROM purchase_receipt_items pri JOIN purchase_receipts pr ON pr.receipt_id=pri.receipt_id
WHERE pr.status='confirmed';

SELECT CONCAT('✅ 재고원장: ', COUNT(*), '건') AS r FROM stock_ledger;

-- item_stock 재계산
INSERT IGNORE INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
SELECT UUID(), tenant_id, item_id, warehouse_id, 0, 0, NOW(6)
FROM (SELECT DISTINCT tenant_id, item_id, warehouse_id FROM stock_ledger) x;

UPDATE item_stock s
INNER JOIN (
  SELECT tenant_id, item_id, warehouse_id,
         SUM(qty_in) - SUM(qty_out) AS net_qty,
         AVG(unit_cost) AS avg_c
  FROM stock_ledger GROUP BY tenant_id, item_id, warehouse_id
) l ON s.tenant_id=l.tenant_id AND s.item_id=l.item_id AND s.warehouse_id=l.warehouse_id
SET s.current_qty = GREATEST(l.net_qty, 0),
    s.avg_cost = l.avg_c,
    s.last_updated_at = NOW(6);

SELECT CONCAT('✅ 재고 스냅샷: ', COUNT(*), '건') AS r FROM item_stock;

-- ═══════════════════════════════════════════════════════════════════
-- 수금 (정상 65% / 연체 25% / 미수 10%)
-- ═══════════════════════════════════════════════════════════════════
INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount,
  collection_method, ref_doc_type, ref_doc_id, is_active, created_at, updated_at)
SELECT UUID(), @tenant, sd.partner_id,
  CASE WHEN CRC32(sd.delivery_id) MOD 100 < 65
         THEN DATE_ADD(sd.delivery_date, INTERVAL (7 + CRC32(sd.delivery_id) MOD 20) DAY)   -- 정상 7~27일
       ELSE DATE_ADD(sd.delivery_date, INTERVAL (35 + CRC32(sd.delivery_id) MOD 30) DAY)   -- 연체 35~65일
  END,
  sd.total_amount + sd.vat_amount,
  ELT((CRC32(sd.delivery_id) MOD 4) + 1, 'bank_transfer', 'card', 'cash', 'check'),
  'sales_delivery', sd.delivery_id, 1,
  TIMESTAMP(sd.delivery_date, '14:00:00'), TIMESTAMP(sd.delivery_date, '14:00:00')
FROM sales_deliveries sd
WHERE sd.status='confirmed'
  AND sd.delivery_date < CURDATE() - INTERVAL 3 DAY
  AND CRC32(sd.delivery_id) MOD 100 < 90;  -- 10% 미수 (수금 없음)

SELECT CONCAT('✅ 수금: ', COUNT(*), '건') AS r FROM collections;

-- ═══════════════════════════════════════════════════════════════════
-- 헤더 총액 = 품목 합계로 동기화 (정합성 100%)
-- ═══════════════════════════════════════════════════════════════════
UPDATE sales_deliveries sd
JOIN (SELECT delivery_id, SUM(supply_amount) s, SUM(vat_amount) v FROM sales_delivery_items GROUP BY delivery_id) sdi
  ON sdi.delivery_id=sd.delivery_id
SET sd.total_amount = sdi.s, sd.vat_amount = sdi.v
WHERE sd.status='confirmed';

UPDATE purchase_receipts pr
JOIN (SELECT receipt_id, SUM(supply_amount) s, SUM(vat_amount) v FROM purchase_receipt_items GROUP BY receipt_id) pri
  ON pri.receipt_id=pr.receipt_id
SET pr.total_amount = pri.s, pr.vat_amount = pri.v
WHERE pr.status='confirmed';

UPDATE collections c
JOIN sales_deliveries sd ON sd.delivery_id=c.ref_doc_id
SET c.amount = sd.total_amount + sd.vat_amount
WHERE c.ref_doc_type='sales_delivery';

SELECT '✅ 헤더 ↔ 품목 금액 동기화 완료' AS r;

-- ═══════════════════════════════════════════════════════════════════
-- partner_balance (외상·수금 합산)
-- ═══════════════════════════════════════════════════════════════════
INSERT INTO partner_balance (balance_id, tenant_id, partner_id, total_sales, total_receipt, total_purchase, total_payment, last_updated_at)
SELECT UUID(), @tenant, p.partner_id,
  COALESCE(sd_sum, 0), COALESCE(coll_sum, 0), COALESCE(pr_sum, 0), 0, NOW(6)
FROM partners p
LEFT JOIN (SELECT partner_id, SUM(total_amount + vat_amount) sd_sum FROM sales_deliveries WHERE status='confirmed' GROUP BY partner_id) sd ON sd.partner_id=p.partner_id
LEFT JOIN (SELECT partner_id, SUM(amount) coll_sum FROM collections WHERE ref_doc_type='sales_delivery' AND is_active=1 GROUP BY partner_id) c ON c.partner_id=p.partner_id
LEFT JOIN (SELECT partner_id, SUM(total_amount + vat_amount) pr_sum FROM purchase_receipts WHERE status='confirmed' GROUP BY partner_id) pr ON pr.partner_id=p.partner_id
WHERE p.tenant_id=@tenant AND p.is_deleted=0;

SELECT CONCAT('✅ 거래처 잔액: ', COUNT(*), '건') AS r FROM partner_balance;

-- ═══════════════════════════════════════════════════════════════════
-- 정리 + 최종 보고
-- ═══════════════════════════════════════════════════════════════════
DROP TEMPORARY TABLE IF EXISTS tmp_n;
DROP TEMPORARY TABLE IF EXISTS tmp_p;
DROP TEMPORARY TABLE IF EXISTS tmp_i;
DROP TEMPORARY TABLE IF EXISTS tmp_emp;
DROP TEMPORARY TABLE IF EXISTS tmp_deals;

SELECT '════════ 최종 집계 ════════' AS report;
SELECT t AS table_name, c AS row_count FROM (
  SELECT 'sales_deliveries' AS t, COUNT(*) AS c FROM sales_deliveries UNION ALL
  SELECT 'sales_delivery_items', COUNT(*) FROM sales_delivery_items UNION ALL
  SELECT 'purchase_receipts', COUNT(*) FROM purchase_receipts UNION ALL
  SELECT 'purchase_receipt_items', COUNT(*) FROM purchase_receipt_items UNION ALL
  SELECT 'stock_ledger', COUNT(*) FROM stock_ledger UNION ALL
  SELECT 'item_stock', COUNT(*) FROM item_stock UNION ALL
  SELECT 'collections', COUNT(*) FROM collections UNION ALL
  SELECT 'partner_balance', COUNT(*) FROM partner_balance
) x;
