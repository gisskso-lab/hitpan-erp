-- ═══════════════════════════════════════════════════════════════════
-- PART 2/3 — 상품 1,000개 + BOM (조립상품)
-- 단품 600 (item_type='product') / 조립 250 ('assembly') / 1+1기획 150 ('promo')
-- ═══════════════════════════════════════════════════════════════════
SET @tenant = (SELECT tenant_id FROM tenants LIMIT 1);

-- 재실행 안전용 cleanup
DELETE FROM item_stock WHERE tenant_id=@tenant;
DELETE FROM bom_items WHERE tenant_id=@tenant;
DELETE FROM bom_headers WHERE tenant_id=@tenant;
DELETE FROM items WHERE tenant_id=@tenant;

-- tmp_n 재생성
DROP TEMPORARY TABLE IF EXISTS tmp_n;
CREATE TEMPORARY TABLE tmp_n (n INT PRIMARY KEY) ENGINE=Memory;
INSERT INTO tmp_n (n)
SELECT a.N + b.N*10 + c.N*100 + d.N*1000
FROM (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) a,
     (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) b,
     (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) c,
     (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) d
WHERE (a.N + b.N*10 + c.N*100 + d.N*1000) < 1200;

-- ── 단품 600개 (item_type=product) ──
INSERT INTO items (item_id, tenant_id, item_code, item_name, item_type, item_group,
  unit, purchase_price, sale_price, standard_price, cost_price, std_price,
  tax_type, safe_stock, safety_stock, is_active, created_at, updated_at)
SELECT
  CONCAT('it-', LPAD(n.n + 1, 5, '0')),
  @tenant,
  CONCAT('P-', LPAD(n.n + 1, 5, '0')),
  CONCAT(
    ELT((n.n MOD 15) + 1, '드릴비트','렌치','스패너','니퍼','펜치','드라이버','해머','톱','줄자','레벨기','전동드릴','임팩드릴','그라인더','타카','대패'),
    ' ',
    ELT((n.n MOD 20) + 1, '4mm','5mm','6mm','8mm','10mm','12mm','3인치','6인치','8인치','12인치','소','중','대','특대','M6','M8','M10','M12','표준','고급')
  ),
  'product',
  ELT((n.n MOD 8) + 1, '전동공구','수공구','측정기','절삭공구','체결구','안전용품','자재','소모품'),
  ELT((n.n MOD 6) + 1, 'EA','SET','BOX','M','KG','PCS'),
  -- purchase_price (원가): 1,000 ~ 100,000 랜덤
  FLOOR(1000 + (CRC32(CONCAT('pp', n.n)) MOD 99000)),
  -- sale_price (판가): 원가 * 1.3~1.8
  FLOOR((1000 + (CRC32(CONCAT('pp', n.n)) MOD 99000)) * (1.3 + (CRC32(CONCAT('sp', n.n)) MOD 50) / 100)),
  FLOOR((1000 + (CRC32(CONCAT('pp', n.n)) MOD 99000)) * 1.5),
  FLOOR(1000 + (CRC32(CONCAT('pp', n.n)) MOD 99000)),
  FLOOR((1000 + (CRC32(CONCAT('pp', n.n)) MOD 99000)) * 1.5),
  'taxable',
  FLOOR(5 + (n.n MOD 20)), -- 안전재고 5~25
  FLOOR(5 + (n.n MOD 20)),
  1, NOW(6), NOW(6)
FROM tmp_n n WHERE n.n < 600;

-- ── 조립상품 250개 (item_type=assembly, BOM 가짐) ──
INSERT INTO items (item_id, tenant_id, item_code, item_name, item_type, item_group,
  unit, purchase_price, sale_price, standard_price, cost_price, std_price,
  tax_type, safe_stock, safety_stock, is_active, created_at, updated_at)
SELECT
  CONCAT('it-', LPAD(n.n + 601, 5, '0')),
  @tenant,
  CONCAT('A-', LPAD(n.n + 1, 5, '0')),
  CONCAT(
    ELT((n.n MOD 10) + 1, '공구세트','인테리어키트','전동공구세트','작업복세트','안전장비세트','전기공사키트','배관공구세트','용접세트','정비공구세트','측정기세트'),
    ' ',
    ELT((n.n MOD 6) + 1, 'A형','B형','프로','스탠다드','베이직','프리미엄')
  ),
  'assembly',
  ELT((n.n MOD 5) + 1, '공구세트','인테리어','전기공사','배관공사','안전장비'),
  'SET',
  FLOOR(20000 + (CRC32(CONCAT('app', n.n)) MOD 180000)),
  FLOOR((20000 + (CRC32(CONCAT('app', n.n)) MOD 180000)) * 1.5),
  FLOOR((20000 + (CRC32(CONCAT('app', n.n)) MOD 180000)) * 1.5),
  FLOOR(20000 + (CRC32(CONCAT('app', n.n)) MOD 180000)),
  FLOOR((20000 + (CRC32(CONCAT('app', n.n)) MOD 180000)) * 1.5),
  'taxable',
  FLOOR(2 + (n.n MOD 10)),
  FLOOR(2 + (n.n MOD 10)),
  1, NOW(6), NOW(6)
FROM tmp_n n WHERE n.n < 250;

-- ── 1+1 기획상품 150개 (item_type=promo, item_group에 '기획상품') ──
INSERT INTO items (item_id, tenant_id, item_code, item_name, item_type, item_group,
  unit, purchase_price, sale_price, standard_price, cost_price, std_price,
  tax_type, safe_stock, safety_stock, is_active, memo, created_at, updated_at)
SELECT
  CONCAT('it-', LPAD(n.n + 851, 5, '0')),
  @tenant,
  CONCAT('PR-', LPAD(n.n + 1, 5, '0')),
  CONCAT('[1+1] ',
    ELT((n.n MOD 12) + 1, '드릴세트','스패너세트','니퍼','작업장갑','드라이버세트','안전모','용접봉세트','절삭유','LED작업등','멀티탭','실리콘건','공구함'),
    ' ',
    ELT((n.n MOD 4) + 1, '프로모션','특가','할인','기획')
  ),
  'promo',
  '기획상품',
  'SET',
  FLOOR(5000 + (CRC32(CONCAT('ppr', n.n)) MOD 45000)),
  FLOOR((5000 + (CRC32(CONCAT('ppr', n.n)) MOD 45000)) * 1.2),
  FLOOR((5000 + (CRC32(CONCAT('ppr', n.n)) MOD 45000)) * 1.2),
  FLOOR(5000 + (CRC32(CONCAT('ppr', n.n)) MOD 45000)),
  FLOOR((5000 + (CRC32(CONCAT('ppr', n.n)) MOD 45000)) * 1.2),
  'taxable',
  FLOOR(3 + (n.n MOD 15)),
  FLOOR(3 + (n.n MOD 15)),
  1,
  '1+1 기획상품 — 1건 구매 시 동일상품 1개 추가 증정 (판매 수량은 2배로 계산)',
  NOW(6), NOW(6)
FROM tmp_n n WHERE n.n < 150;

SELECT CONCAT('✅ 상품: ', COUNT(*), '개 (단품 ', SUM(item_type='product'),
  ' / 조립 ', SUM(item_type='assembly'),
  ' / 기획 ', SUM(item_type='promo'), ')') AS r
FROM items WHERE tenant_id=@tenant;

-- ── BOM 생성 (조립상품 250개 각각 3~5개 자재) ──
INSERT INTO bom_headers (bom_id, tenant_id, product_item_id, bom_name, bom_version, is_default, is_active, created_at, updated_at)
SELECT
  CONCAT('bom-', SUBSTRING(i.item_id, 4)),
  @tenant, i.item_id,
  CONCAT(i.item_name, ' 기본 BOM'), 1, 1, 1, NOW(6), NOW(6)
FROM items i WHERE i.tenant_id=@tenant AND i.item_type='assembly';

-- 단품 목록을 rn으로 인덱싱
DROP TEMPORARY TABLE IF EXISTS tmp_products;
CREATE TEMPORARY TABLE tmp_products (rn INT AUTO_INCREMENT PRIMARY KEY, item_id VARCHAR(36)) ENGINE=Memory;
INSERT INTO tmp_products (item_id)
SELECT item_id FROM items WHERE tenant_id=@tenant AND item_type='product';

-- BOM 품목 (각 BOM에 3~5개 단품 자재)
INSERT INTO bom_items (bom_item_id, bom_id, tenant_id, seq_no, material_item_id, qty, unit, loss_rate)
SELECT UUID(), bh.bom_id, @tenant, n.n + 1,
  (SELECT item_id FROM tmp_products WHERE rn = ((CRC32(CONCAT(bh.bom_id, n.n)) MOD 600) + 1)),
  ((CRC32(CONCAT(bh.bom_id, n.n, 'q')) MOD 8) + 1),
  'EA', 0.00
FROM bom_headers bh
CROSS JOIN tmp_n n
WHERE bh.tenant_id=@tenant
  AND n.n < CASE WHEN CRC32(bh.bom_id) MOD 100 < 40 THEN 3
                 WHEN CRC32(bh.bom_id) MOD 100 < 80 THEN 4
                 ELSE 5 END;

SELECT CONCAT('✅ BOM 헤더: ', COUNT(*), '개') AS r FROM bom_headers WHERE tenant_id=@tenant;
SELECT CONCAT('✅ BOM 품목: ', COUNT(*), '개') AS r FROM bom_items WHERE tenant_id=@tenant;

-- ── 창고별 초기 재고 스냅샷 (모든 상품 × 3창고) ──
INSERT IGNORE INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
SELECT UUID(), @tenant, i.item_id, w.warehouse_id, 0, 0, NOW(6)
FROM items i
CROSS JOIN warehouses w
WHERE i.tenant_id=@tenant AND w.tenant_id=@tenant;

SELECT CONCAT('✅ 재고 행: ', COUNT(*), '행 (1000 × 3창고)') AS r FROM item_stock WHERE tenant_id=@tenant;
