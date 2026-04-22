-- ═══════════════════════════════════════════════════════════════════
-- 히트판 ERP — 공구상가 자재상 "대한공구상사" 5년치 샘플 (PART 1/3)
-- 기간: 2021-08-01 ~ 2026-07-31
-- 인원: 대표1/경리1/영업관리1/구매팀장1/인사1/현장영업3 = 8명
-- 상품: 1,000개 (단품 600 / 조립 250 / 1+1기획 150)
-- 거래처: 700개 (매입 300 / 매출 400)
-- 2026-04-22 기준 (현재 7-8월 여름휴가철 설정)
-- ═══════════════════════════════════════════════════════════════════
SET FOREIGN_KEY_CHECKS = 0;

SET @tenant = (SELECT tenant_id FROM tenants LIMIT 1);
SELECT CONCAT('━━━━ TENANT: ', @tenant, ' ━━━━') AS info;

-- ── 0. 기존 샘플 데이터 전량 삭제 ──────────────────────────────────
DELETE FROM partner_balance WHERE tenant_id=@tenant;
DELETE FROM collections WHERE tenant_id=@tenant;
DELETE FROM stock_ledger WHERE tenant_id=@tenant;
DELETE FROM item_stock WHERE tenant_id=@tenant;
DELETE FROM sales_delivery_items WHERE tenant_id=@tenant;
DELETE FROM sales_deliveries WHERE tenant_id=@tenant;
DELETE FROM purchase_receipt_items WHERE tenant_id=@tenant;
DELETE FROM purchase_receipts WHERE tenant_id=@tenant;
DELETE FROM sales_order_items WHERE tenant_id=@tenant;
DELETE FROM sales_orders WHERE tenant_id=@tenant;
DELETE FROM quotation_items WHERE quote_id IN (SELECT quote_id FROM quotations WHERE tenant_id=@tenant);
DELETE FROM quotations WHERE tenant_id=@tenant;
DELETE FROM purchase_order_items WHERE tenant_id=@tenant;
DELETE FROM purchase_orders WHERE tenant_id=@tenant;
DELETE FROM expenses WHERE tenant_id=@tenant;
DELETE FROM bom_items WHERE tenant_id=@tenant;
DELETE FROM bom_headers WHERE tenant_id=@tenant;
DELETE FROM item_special_prices WHERE tenant_id=@tenant;
DELETE FROM partner_special_prices WHERE tenant_id=@tenant;
DELETE FROM items WHERE tenant_id=@tenant;
DELETE FROM partners WHERE tenant_id=@tenant;
DELETE FROM employees WHERE tenant_id=@tenant;
SELECT '✅ 기존 샘플 삭제 완료' AS r;

-- ── 1. 테넌트 정보 갱신 ──
UPDATE tenants SET
  company_name = '대한공구상사',
  ceo_name = '박철수',
  biz_no = '1234567890',
  biz_type = '도소매업',
  biz_item = '공구·자재'
WHERE tenant_id=@tenant;

-- ── 2. 창고 확인/세팅 ──
INSERT IGNORE INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, location, is_active, created_at, updated_at)
VALUES
  ('wh-main', @tenant, 'WH-01', '본점창고', 'normal', '서울 구로구 공구상가', 1, NOW(6), NOW(6)),
  ('wh-sub1', @tenant, 'WH-02', '제2창고', 'normal', '서울 영등포구', 1, NOW(6), NOW(6)),
  ('wh-sub2', @tenant, 'WH-03', '부천물류센터', 'normal', '경기 부천시', 1, NOW(6), NOW(6));

-- ── 3. 사원 8명 (역할별) ──
INSERT INTO employees (employee_id, tenant_id, emp_no, emp_name, position, emp_type, role, join_date, is_active, created_at, updated_at)
VALUES
  ('emp-ceo',   @tenant, 'E001', '박철수', '대표이사',    'regular', 'tenant_admin',      '2018-03-02 09:00:00', 1, NOW(6), NOW(6)),
  ('emp-acc',   @tenant, 'E002', '김정숙', '경리부장',    'regular', 'accountant',        '2019-05-13 09:00:00', 1, NOW(6), NOW(6)),
  ('emp-smgr',  @tenant, 'E003', '이영훈', '영업관리과장','regular', 'sales_manager',     '2020-01-06 09:00:00', 1, NOW(6), NOW(6)),
  ('emp-pmgr',  @tenant, 'E004', '최동구', '구매팀장',    'regular', 'purchase_manager',  '2019-11-04 09:00:00', 1, NOW(6), NOW(6)),
  ('emp-hr',    @tenant, 'E005', '장미경', '인사담당',    'regular', 'hr_admin',          '2021-04-19 09:00:00', 1, NOW(6), NOW(6)),
  ('emp-fs1',   @tenant, 'E006', '강민재', '영업대리',    'regular', 'sales_user',        '2021-08-02 09:00:00', 1, NOW(6), NOW(6)),
  ('emp-fs2',   @tenant, 'E007', '정태호', '영업사원',    'regular', 'sales_user',        '2022-06-01 09:00:00', 1, NOW(6), NOW(6)),
  ('emp-fs3',   @tenant, 'E008', '윤성철', '영업사원',    'regular', 'sales_user',        '2023-02-20 09:00:00', 1, NOW(6), NOW(6));
SELECT CONCAT('✅ 사원: ', COUNT(*), '명') AS r FROM employees WHERE tenant_id=@tenant;

-- ── 4. 거래처 700개 (매입 300 / 매출 400) ──
-- 헬퍼 숫자 테이블
DROP TEMPORARY TABLE IF EXISTS tmp_n;
CREATE TEMPORARY TABLE tmp_n (n INT PRIMARY KEY) ENGINE=Memory;
INSERT INTO tmp_n (n)
SELECT a.N + b.N*10 + c.N*100 + d.N*1000
FROM (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) a,
     (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) b,
     (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) c,
     (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) d
WHERE (a.N + b.N*10 + c.N*100 + d.N*1000) < 2000;

-- 매입처 300개 (제조·도매)
INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, biz_no, ceo_name, biz_type, biz_item,
  tel, is_active, price_grade, tax_type, payment_terms, created_at, updated_at)
SELECT
  CONCAT('sup-', LPAD(n.n + 1, 4, '0')),
  @tenant,
  CONCAT('S', LPAD(n.n + 1, 4, '0')),
  CONCAT(
    ELT((n.n MOD 20) + 1,
      '대한','한국','태평양','동아','성진','신일','금호','한신','대성','우진',
      '동양','삼화','명성','현대','동산','태광','영진','일신','삼성','서진'),
    ELT((n.n MOD 15) + 1, '공구','철강','기계','부품','산업','자재','금속','공업','특수','소재','테크','엔지','정밀','설비','전자')
  ),
  'supplier',
  LPAD(CONV(SUBSTRING(MD5(CONCAT('s', n.n)), 1, 8), 16, 10), 10, '0'),
  ELT((n.n MOD 10) + 1, '김정호','박종수','이정숙','최민재','강상기','윤상민','장수현','한승호','오영수','신태호'),
  '제조업',
  ELT((n.n MOD 8) + 1, '공구','전동공구','측정기','베어링','밸브','볼트너트','전선관','용접기구'),
  CONCAT('02-', LPAD((n.n MOD 9000) + 1000, 4, '0'), '-', LPAD((n.n * 17) MOD 10000, 4, '0')),
  1, 'A', 'taxable',
  ELT((n.n MOD 4) + 1, 30, 60, 90, 15),
  NOW(6), NOW(6)
FROM tmp_n n WHERE n.n < 300;

-- 매출처 400개 (소매점·공사현장·건설사·공장)
INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, biz_no, ceo_name, biz_type, biz_item,
  tel, is_active, price_grade, tax_type, payment_terms, created_at, updated_at)
SELECT
  CONCAT('cus-', LPAD(n.n + 1, 4, '0')),
  @tenant,
  CONCAT('C', LPAD(n.n + 1, 4, '0')),
  CONCAT(
    ELT((n.n MOD 25) + 1,
      '서울','경기','부천','인천','수원','안산','안양','성남','고양','용인',
      '부평','광명','시흥','의정부','파주','김포','하남','구리','남양주','평택',
      '화성','오산','군포','포천','양주'),
    ELT((n.n MOD 18) + 1,
      '건설','공업사','산업','철물점','인테리어','설비','전기공사','엔지니어링','정비','물산',
      '공구마트','자재센터','테크','엔지','종합상사','기계','제작소','공업')
  ),
  'customer',
  LPAD(CONV(SUBSTRING(MD5(CONCAT('c', n.n)), 1, 8), 16, 10), 10, '0'),
  ELT((n.n MOD 12) + 1, '황태석','문상호','임준혁','배철훈','노경수','유태호','정석진','조영만','성민수','한경호','권영진','홍석호'),
  ELT((n.n MOD 5) + 1, '건설업','제조업','도소매업','서비스업','운수업'),
  ELT((n.n MOD 10) + 1, '공구·자재','설비공사','전기공사','기계제작','인테리어','철물점','공장운영','유지보수','조선설비','자동차부품'),
  CONCAT('031-', LPAD((n.n MOD 9000) + 1000, 4, '0'), '-', LPAD((n.n * 23) MOD 10000, 4, '0')),
  1,
  ELT((n.n MOD 5) + 1, 'A','B','B','C','A'),
  'taxable',
  ELT((n.n MOD 4) + 1, 30, 60, 30, 15),
  NOW(6), NOW(6)
FROM tmp_n n WHERE n.n < 400;

SELECT CONCAT('✅ 거래처: ', COUNT(*), '개 (매입 ', SUM(partner_type='supplier'), ' / 매출 ', SUM(partner_type='customer'), ')') AS r
FROM partners WHERE tenant_id=@tenant;
