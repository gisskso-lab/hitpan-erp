SET @@session.block_encryption_mode = 'aes-256-cbc';
SET @key = _binary 'hitpan-aes-key-32bytes-exactly!!';
SET @food_tenant = 'tenant-food000-e000-eeee-eeeeeeeeee';
SET @wh = 'wh-food0-main-0000-eeeeeeeeeeeeeeee';
SET @cold = 'wh-food0-cold-0000-eeeeeeeeeeeeeeee';
SET SESSION max_recursive_iterations = 1000;

-- tenants
INSERT INTO tenants (tenant_id, tenant_code, company_name, biz_no, biz_no_hash, ceo_name, tel, email, address, max_users, status, db_host, db_name, license_key_hash, reseller_tier, biz_type, biz_item, tax_type, fiscal_month, created_at, updated_at)
VALUES (@food_tenant, 'FOOD001', '(주)이천푸드',
  TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1358923456', @key, @i))), SHA2('1358923456',256),
  '황대표', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-633-1234', @key, @i))),
  TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('ceo@icheon-food.co.kr', @key, @i))),
  '경기도 이천시', 20, 'active', 'localhost', 'hitpan_erp',
  SHA2('food-license', 256), 1, '제조업', '식품 제조', 'taxable', 12, NOW(6), NOW(6));

INSERT INTO tenant_settings (tenant_id, allow_force_price_input, allow_force_vat_input, allow_zero_price, allow_past_edit, allow_force_stock_adjust, allow_credit_override, price_deviation_limit, force_edit_require_password, stock_eval_method, use_multi_warehouse, stock_shortage_alert, allow_minus_stock, price_input_type, auto_vat_adjust, vat_round_type, price_a_rate, price_b_rate, price_c_rate, price_d_rate, price_e_rate, use_credit_limit, credit_limit_amount, show_purchase_price, use_sales_by_employee, use_personal_info_protect, industry_type)
VALUES (@food_tenant, 1, 0, 0, 0, 1, 0, 15, 1, 'fifo', 1, 1, 0, 'net', 1, 'round', 1.00, 1.05, 1.10, 1.18, 1.30, 1, 50000000, 0, 1, 1, 'food');

INSERT INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, location, is_active, created_at, updated_at) VALUES
  (@wh, @food_tenant, 'MAIN', '상온창고', 'normal', '이천 본사', 1, NOW(6), NOW(6)),
  (@cold, @food_tenant, 'COLD', '냉장창고', 'normal', '이천 냉장동', 1, NOW(6), NOW(6));

INSERT INTO accounts (account_code, tenant_id, account_name, account_type, sort_order, is_active, created_at)
SELECT account_code, @food_tenant, account_name, account_type, sort_order, is_active, NOW(6)
FROM accounts WHERE tenant_id='452ca266-97b9-4cd1-a0ac-2f37830c81f6';

-- 거래처 16 (대리점 10 + 공급사 5 + 외주 1)
INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, biz_no, biz_no_hash, ceo_name, biz_type, biz_item, tel, address, credit_limit, payment_terms, bank_name, bank_account, account_holder, is_active, created_at, updated_at, price_grade, tax_type, vat_handling, price_display_preference) VALUES
('pf-food0-cust-0001-eeeeeeeeeeee', @food_tenant, 'FC001', '서울대리점', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1038923456', @key, @i))), SHA2('1038923456',256),
 '박서울', '도소매업', '대리점',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('02-901-1000', @key, @i))),
 '서울시 종로구', 100000000, 30, '국민은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-700-000001', @key, @i))),
 '서울대리점', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inherit'),
('pf-food0-cust-0002-eeeeeeeeeeee', @food_tenant, 'FC002', '부산대리점', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2018923478', @key, @i))), SHA2('2018923478',256),
 '김부산', '도소매업', '대리점',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('051-222-2000', @key, @i))),
 '부산시 동구', 80000000, 30, '우리은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('101-710-000002', @key, @i))),
 '부산대리점', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inclusive'),
('pf-food0-cust-0003-eeeeeeeeeeee', @food_tenant, 'FC003', '대구대리점', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('3038923489', @key, @i))), SHA2('3038923489',256),
 '이대구', '도소매업', '대리점',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('053-444-3000', @key, @i))),
 '대구시 중구', 60000000, 30, '신한은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('110-720-000003', @key, @i))),
 '대구대리점', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit'),
('pf-food0-cust-0004-eeeeeeeeeeee', @food_tenant, 'FC004', '인천대리점', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1218923490', @key, @i))), SHA2('1218923490',256),
 '최인천', '도소매업', '대리점',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('032-777-4000', @key, @i))),
 '인천시 중구', 60000000, 30, '기업은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('312-730-000004', @key, @i))),
 '인천대리점', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit'),
('pf-food0-cust-0005-eeeeeeeeeeee', @food_tenant, 'FC005', '광주대리점', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('4108923501', @key, @i))), SHA2('4108923501',256),
 '장광주', '도소매업', '대리점',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('062-555-5000', @key, @i))),
 '광주시 동구', 40000000, 60, '하나은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('310-740-000005', @key, @i))),
 '광주대리점', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inclusive'),
('pf-food0-cust-0006-eeeeeeeeeeee', @food_tenant, 'FC006', '편의점체인', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2208923512', @key, @i))), SHA2('2208923512',256),
 '강편의', '도소매업', '편의점',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('02-6000-7000', @key, @i))),
 '서울시 중구', 120000000, 60, '농협',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('351-750-000006', @key, @i))),
 '편의점체인', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inherit'),
('pf-food0-cust-0007-eeeeeeeeeeee', @food_tenant, 'FC007', '마트체인', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2108923523', @key, @i))), SHA2('2108923523',256),
 '윤마트', '도소매업', '대형마트',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('02-3777-8000', @key, @i))),
 '서울시 영등포구', 150000000, 60, '국민은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-760-000007', @key, @i))),
 '마트체인', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inclusive'),
('pf-food0-cust-0008-eeeeeeeeeeee', @food_tenant, 'FC008', '온라인몰', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2318923534', @key, @i))), SHA2('2318923534',256),
 '조온라', '도소매업', '전자상거래',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1544-9000', @key, @i))),
 '서울시 강남구', 50000000, 30, '우리은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('101-770-000008', @key, @i))),
 '온라인몰', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit'),
('pf-food0-cust-0009-eeeeeeeeeeee', @food_tenant, 'FC009', '급식업체', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1328923545', @key, @i))), SHA2('1328923545',256),
 '임급식', '서비스업', '단체급식',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-780-9000', @key, @i))),
 '경기도 성남시', 40000000, 30, '신한은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('110-780-000009', @key, @i))),
 '급식업체', 1, NOW(6), NOW(6), 'C', 'taxable', 'zero', 'inherit'),
('pf-food0-cust-0010-eeeeeeeeeeee', @food_tenant, 'FC010', '수출무역', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1408923556', @key, @i))), SHA2('1408923556',256),
 '정수출', '도소매업', '수출',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('02-3779-1000', @key, @i))),
 '서울시 종로구', 30000000, 30, '기업은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('312-790-000010', @key, @i))),
 '수출무역', 1, NOW(6), NOW(6), 'C', 'exempt', 'exempt', 'inherit');

INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, biz_no, biz_no_hash, ceo_name, biz_type, biz_item, tel, address, credit_limit, payment_terms, bank_name, bank_account, account_holder, is_active, created_at, updated_at, price_grade, tax_type, vat_handling, price_display_preference) VALUES
('pf-food0-supp-0001-eeeeeeeeeeee', @food_tenant, 'FS001', '농협밀가루', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1038912345', @key, @i))), SHA2('1038912345',256),
 '김농협', '도매업', '밀가루',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-633-5000', @key, @i))),
 '경기도 이천시', 0, 30, '농협',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('351-801-000011', @key, @i))),
 '농협밀가루', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inherit'),
('pf-food0-supp-0002-eeeeeeeeeeee', @food_tenant, 'FS002', 'CJ제일제당', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1048912356', @key, @i))), SHA2('1048912356',256),
 '박씨제이', '제조업', '식품첨가물',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('02-6740-2000', @key, @i))),
 '서울시 중구', 0, 60, '국민은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-811-000012', @key, @i))),
 'CJ제일제당', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inclusive'),
('pf-food0-supp-0003-eeeeeeeeeeee', @food_tenant, 'FS003', '대상당', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1058912367', @key, @i))), SHA2('1058912367',256),
 '이대상', '도매업', '설탕',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-450-3000', @key, @i))),
 '경기도 수원시', 0, 30, '우리은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('101-821-000013', @key, @i))),
 '대상당', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit'),
('pf-food0-supp-0004-eeeeeeeeeeee', @food_tenant, 'FS004', '우유낙농', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1068912378', @key, @i))), SHA2('1068912378',256),
 '최낙농', '제조업', '유제품',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('033-555-4000', @key, @i))),
 '강원도 원주시', 0, 30, '신한은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('110-831-000014', @key, @i))),
 '우유낙농', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit'),
('pf-food0-supp-0005-eeeeeeeeeeee', @food_tenant, 'FS005', '포장재공급', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1078912389', @key, @i))), SHA2('1078912389',256),
 '윤포장', '제조업', '식품포장재',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-390-6000', @key, @i))),
 '경기도 안산시', 0, 30, '기업은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('312-841-000015', @key, @i))),
 '포장재공급', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inclusive'),
('pf-food0-out0-0001-eeeeeeeeeeee', @food_tenant, 'FO001', '저온물류', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2138912390', @key, @i))), SHA2('2138912390',256),
 '조물류', '운수업', '냉장운송',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-780-7000', @key, @i))),
 '경기도 이천시', 0, 30, '하나은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('310-851-000016', @key, @i))),
 '저온물류', 1, NOW(6), NOW(6), 'C', 'taxable', 'standard', 'inherit');

-- 직원 15
INSERT INTO employees (employee_id, tenant_id, emp_no, emp_name, position, job_title, emp_type, join_date, phone, email, is_active, created_at, updated_at, role) VALUES
('em-food0-0001-eeeeeeeeeeeeeeeeeeee', @food_tenant, 'E001', '황대표', 'ceo', '대표이사', 'regular', '2015-03-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-5001-0001', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('ceo@icheon-food.co.kr', @key, @i))),
 1, NOW(6), NOW(6), 'tenant_admin'),
('em-food0-0002-eeeeeeeeeeeeeeeeeeee', @food_tenant, 'E002', '오부장', 'manager', 'HACCP팀장', 'regular', '2015-06-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-5002-0002', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('oh@icheon-food.co.kr', @key, @i))),
 1, NOW(6), NOW(6), 'qa_user'),
('em-food0-0003-eeeeeeeeeeeeeeeeeeee', @food_tenant, 'E003', '조부장', 'manager', '생산팀장', 'regular', '2016-01-15',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-5003-0003', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('cho@icheon-food.co.kr', @key, @i))),
 1, NOW(6), NOW(6), 'production_manager'),
('em-food0-0004-eeeeeeeeeeeeeeeeeeee', @food_tenant, 'E004', '윤대리', 'staff', '영업대리', 'regular', '2020-04-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-5004-0004', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('yoon@icheon-food.co.kr', @key, @i))),
 1, NOW(6), NOW(6), 'sales_user'),
('em-food0-0005-eeeeeeeeeeeeeeeeeeee', @food_tenant, 'E005', '박사원', 'staff', '영업사원', 'regular', '2022-02-15',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-5005-0005', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('park@icheon-food.co.kr', @key, @i))),
 1, NOW(6), NOW(6), 'sales_user'),
('em-food0-0006-eeeeeeeeeeeeeeeeeeee', @food_tenant, 'E006', '김경리', 'clerk', '경리과장', 'regular', '2018-05-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-5006-0006', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('kim@icheon-food.co.kr', @key, @i))),
 1, NOW(6), NOW(6), 'accountant'),
('em-food0-0007-eeeeeeeeeeeeeeeeeeee', @food_tenant, 'E007', '강반장', 'staff', '생산반장', 'regular', '2017-08-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-5007-0007', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('kang@icheon-food.co.kr', @key, @i))),
 1, NOW(6), NOW(6), 'production_user'),
('em-food0-0008-eeeeeeeeeeeeeeeeeeee', @food_tenant, 'E008', '이기사', 'staff', '생산기사', 'regular', '2019-03-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-5008-0008', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('lee@icheon-food.co.kr', @key, @i))),
 1, NOW(6), NOW(6), 'production_user'),
('em-food0-0009-eeeeeeeeeeeeeeeeeeee', @food_tenant, 'E009', '정사원', 'staff', '포장담당', 'regular', '2021-06-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-5009-0009', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('jung@icheon-food.co.kr', @key, @i))),
 1, NOW(6), NOW(6), 'production_user'),
('em-food0-0010-eeeeeeeeeeeeeeeeeeee', @food_tenant, 'E010', '유사원', 'staff', '품질검사', 'regular', '2020-10-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-5010-0010', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('yoo@icheon-food.co.kr', @key, @i))),
 1, NOW(6), NOW(6), 'qa_user'),
('em-food0-0011-eeeeeeeeeeeeeeeeeeee', @food_tenant, 'E011', '한사원', 'staff', '자재관리', 'regular', '2022-08-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-5011-0011', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('han@icheon-food.co.kr', @key, @i))),
 1, NOW(6), NOW(6), 'warehouse_user'),
('em-food0-0012-eeeeeeeeeeeeeeeeeeee', @food_tenant, 'E012', '송기사', 'staff', '출하담당', 'regular', '2020-12-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-5012-0012', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('song@icheon-food.co.kr', @key, @i))),
 1, NOW(6), NOW(6), 'shipping_user'),
('em-food0-0013-eeeeeeeeeeeeeeeeeeee', @food_tenant, 'E013', '서기사', 'staff', '보조 생산', 'regular', '2023-03-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-5013-0013', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('seo@icheon-food.co.kr', @key, @i))),
 1, NOW(6), NOW(6), 'production_user'),
('em-food0-0014-eeeeeeeeeeeeeeeeeeee', @food_tenant, 'E014', '최사원', 'staff', '보조 영업', 'regular', '2023-06-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-5014-0014', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('choi@icheon-food.co.kr', @key, @i))),
 1, NOW(6), NOW(6), 'sales_user'),
('em-food0-0015-eeeeeeeeeeeeeeeeeeee', @food_tenant, 'E015', '임사원', 'staff', '냉장관리', 'regular', '2023-09-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-5015-0015', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('lim@icheon-food.co.kr', @key, @i))),
 1, NOW(6), NOW(6), 'warehouse_user');

-- 품목 30 (원재료 12·반제품 6·완제품 12)
INSERT INTO items (item_id, tenant_id, item_code, item_name, item_type, unit, std_price, price_a, price_b, price_c, price_d, price_e, cost_price, tax_type, safe_stock, is_active, created_at, updated_at, item_group, spec, purchase_price, sale_price, standard_price, safety_stock) VALUES
('if-food0-mat-0001-eeeeeeeeeeeeee', @food_tenant, 'FM001', '박력분 밀가루 20kg', 'material', 'EA', 32000, 32000, 33600, 35200, 37760, 41600, 28160, 'taxable', 500, 1, NOW(6), NOW(6), '분말', '박력 20kg포', 28160, 32000, 32000, 500),
('if-food0-mat-0002-eeeeeeeeeeeeee', @food_tenant, 'FM002', '강력분 밀가루 20kg', 'material', 'EA', 35000, 35000, 36750, 38500, 41300, 45500, 30800, 'taxable', 400, 1, NOW(6), NOW(6), '분말', '강력 20kg포', 30800, 35000, 35000, 400),
('if-food0-mat-0003-eeeeeeeeeeeeee', @food_tenant, 'FM003', '설탕 25kg', 'material', 'EA', 38000, 38000, 39900, 41800, 44840, 49400, 33440, 'taxable', 300, 1, NOW(6), NOW(6), '분말', '정백당 25kg', 33440, 38000, 38000, 300),
('if-food0-mat-0004-eeeeeeeeeeeeee', @food_tenant, 'FM004', '소금 25kg', 'material', 'EA', 18000, 18000, 18900, 19800, 21240, 23400, 15840, 'taxable', 200, 1, NOW(6), NOW(6), '분말', '천일염', 15840, 18000, 18000, 200),
('if-food0-mat-0005-eeeeeeeeeeeeee', @food_tenant, 'FM005', '버터 2.5kg', 'material', 'EA', 42000, 42000, 44100, 46200, 49560, 54600, 36960, 'taxable', 100, 1, NOW(6), NOW(6), '유제품', '무염 2.5kg', 36960, 42000, 42000, 100),
('if-food0-mat-0006-eeeeeeeeeeeeee', @food_tenant, 'FM006', '우유 1L', 'material', 'EA', 2800, 2800, 2940, 3080, 3304, 3640, 2464, 'taxable', 500, 1, NOW(6), NOW(6), '유제품', '냉장 1L', 2464, 2800, 2800, 500),
('if-food0-mat-0007-eeeeeeeeeeeeee', @food_tenant, 'FM007', '계란 30개', 'material', 'EA', 8500, 8500, 8925, 9350, 10030, 11050, 7480, 'taxable', 200, 1, NOW(6), NOW(6), '축산', '대란 30구', 7480, 8500, 8500, 200),
('if-food0-mat-0008-eeeeeeeeeeeeee', @food_tenant, 'FM008', '식용유 18L', 'material', 'EA', 48000, 48000, 50400, 52800, 56640, 62400, 42240, 'taxable', 100, 1, NOW(6), NOW(6), '유지', '콩기름', 42240, 48000, 48000, 100),
('if-food0-mat-0009-eeeeeeeeeeeeee', @food_tenant, 'FM009', '이스트 500g', 'material', 'EA', 6500, 6500, 6825, 7150, 7670, 8450, 5720, 'taxable', 200, 1, NOW(6), NOW(6), '첨가제', '생이스트', 5720, 6500, 6500, 200),
('if-food0-mat-0010-eeeeeeeeeeeeee', @food_tenant, 'FM010', '베이킹소다 5kg', 'material', 'EA', 8800, 8800, 9240, 9680, 10384, 11440, 7744, 'taxable', 80, 1, NOW(6), NOW(6), '첨가제', '팽창제', 7744, 8800, 8800, 80),
('if-food0-mat-0011-eeeeeeeeeeeeee', @food_tenant, 'FM011', '초코칩 10kg', 'material', 'EA', 85000, 85000, 89250, 93500, 100300, 110500, 74800, 'taxable', 50, 1, NOW(6), NOW(6), '과자원료', '다크 초코칩', 74800, 85000, 85000, 50),
('if-food0-mat-0012-eeeeeeeeeeeeee', @food_tenant, 'FM012', '포장봉투 1000매', 'material', 'EA', 15000, 15000, 15750, 16500, 17700, 19500, 13200, 'taxable', 200, 1, NOW(6), NOW(6), '포장재', 'PE 투명', 13200, 15000, 15000, 200),
-- 반제품 6
('if-food0-semi-0001-eeeeeeeeeeeee', @food_tenant, 'FSP001', '빵 반죽 A (5kg)', 'assembly', 'KG', 6500, 6500, 6825, 7150, 7670, 8450, 5720, 'taxable', 50, 1, NOW(6), NOW(6), '반제품', '식빵용', 5720, 6500, 6500, 50),
('if-food0-semi-0002-eeeeeeeeeeeee', @food_tenant, 'FSP002', '빵 반죽 B (5kg)', 'assembly', 'KG', 7200, 7200, 7560, 7920, 8496, 9360, 6336, 'taxable', 50, 1, NOW(6), NOW(6), '반제품', '크로와상용', 6336, 7200, 7200, 50),
('if-food0-semi-0003-eeeeeeeeeeeee', @food_tenant, 'FSP003', '과자 반죽 (10kg)', 'assembly', 'KG', 4800, 4800, 5040, 5280, 5664, 6240, 4224, 'taxable', 80, 1, NOW(6), NOW(6), '반제품', '쿠키용', 4224, 4800, 4800, 80),
('if-food0-semi-0004-eeeeeeeeeeeee', @food_tenant, 'FSP004', '크림 (3kg)', 'assembly', 'KG', 12000, 12000, 12600, 13200, 14160, 15600, 10560, 'taxable', 30, 1, NOW(6), NOW(6), '반제품', '케이크용', 10560, 12000, 12000, 30),
('if-food0-semi-0005-eeeeeeeeeeeee', @food_tenant, 'FSP005', '시럽 (5kg)', 'assembly', 'KG', 3500, 3500, 3675, 3850, 4130, 4550, 3080, 'taxable', 50, 1, NOW(6), NOW(6), '반제품', '베이스', 3080, 3500, 3500, 50),
('if-food0-semi-0006-eeeeeeeeeeeee', @food_tenant, 'FSP006', '토핑 과일 (2kg)', 'assembly', 'KG', 15000, 15000, 15750, 16500, 17700, 19500, 13200, 'taxable', 20, 1, NOW(6), NOW(6), '반제품', '당절임', 13200, 15000, 15000, 20),
-- 완제품 12
('if-food0-fin0-0001-eeeeeeeeeeeee', @food_tenant, 'FP001', '식빵 500g', 'product', 'EA', 3500, 3500, 3675, 3850, 4130, 4550, 3000, 'taxable', 200, 1, NOW(6), NOW(6), '완제품', '일반', 3000, 3500, 3500, 200),
('if-food0-fin0-0002-eeeeeeeeeeeee', @food_tenant, 'FP002', '통밀빵 500g', 'product', 'EA', 4500, 4500, 4725, 4950, 5310, 5850, 3850, 'taxable', 150, 1, NOW(6), NOW(6), '완제품', '통밀', 3850, 4500, 4500, 150),
('if-food0-fin0-0003-eeeeeeeeeeeee', @food_tenant, 'FP003', '크로와상 6개입', 'product', 'EA', 8500, 8500, 8925, 9350, 10030, 11050, 7280, 'taxable', 100, 1, NOW(6), NOW(6), '완제품', '냉동', 7280, 8500, 8500, 100),
('if-food0-fin0-0004-eeeeeeeeeeeee', @food_tenant, 'FP004', '초코칩 쿠키 200g', 'product', 'EA', 4800, 4800, 5040, 5280, 5664, 6240, 4100, 'taxable', 300, 1, NOW(6), NOW(6), '완제품', '상온 180일', 4100, 4800, 4800, 300),
('if-food0-fin0-0005-eeeeeeeeeeeee', @food_tenant, 'FP005', '버터 쿠키 200g', 'product', 'EA', 5500, 5500, 5775, 6050, 6490, 7150, 4700, 'taxable', 250, 1, NOW(6), NOW(6), '완제품', '상온 180일', 4700, 5500, 5500, 250),
('if-food0-fin0-0006-eeeeeeeeeeeee', @food_tenant, 'FP006', '생크림 케이크 1호', 'product', 'EA', 28000, 28000, 29400, 30800, 33040, 36400, 24000, 'taxable', 30, 1, NOW(6), NOW(6), '완제품', '냉장 5일', 24000, 28000, 28000, 30),
('if-food0-fin0-0007-eeeeeeeeeeeee', @food_tenant, 'FP007', '치즈 케이크 1호', 'product', 'EA', 32000, 32000, 33600, 35200, 37760, 41600, 27500, 'taxable', 30, 1, NOW(6), NOW(6), '완제품', '냉장 5일', 27500, 32000, 32000, 30),
('if-food0-fin0-0008-eeeeeeeeeeeee', @food_tenant, 'FP008', '마들렌 12개입', 'product', 'EA', 9800, 9800, 10290, 10780, 11564, 12740, 8400, 'taxable', 100, 1, NOW(6), NOW(6), '완제품', '상온 60일', 8400, 9800, 9800, 100),
('if-food0-fin0-0009-eeeeeeeeeeeee', @food_tenant, 'FP009', '마카롱 6개입', 'product', 'EA', 12000, 12000, 12600, 13200, 14160, 15600, 10300, 'taxable', 50, 1, NOW(6), NOW(6), '완제품', '냉장 10일', 10300, 12000, 12000, 50),
('if-food0-fin0-0010-eeeeeeeeeeeee', @food_tenant, 'FP010', '스콘 8개입', 'product', 'EA', 7800, 7800, 8190, 8580, 9204, 10140, 6700, 'taxable', 80, 1, NOW(6), NOW(6), '완제품', '상온 20일', 6700, 7800, 7800, 80),
('if-food0-fin0-0011-eeeeeeeeeeeee', @food_tenant, 'FP011', '도넛 6개입', 'product', 'EA', 6800, 6800, 7140, 7480, 8024, 8840, 5830, 'taxable', 100, 1, NOW(6), NOW(6), '완제품', '냉장 3일', 5830, 6800, 6800, 100),
('if-food0-fin0-0012-eeeeeeeeeeeee', @food_tenant, 'FP012', '애플파이 1개', 'product', 'EA', 9500, 9500, 9975, 10450, 11210, 12350, 8150, 'taxable', 50, 1, NOW(6), NOW(6), '완제품', '냉장 5일', 8150, 9500, 9500, 50);

-- BOM 12 (간소화)
INSERT INTO bom_headers (bom_id, tenant_id, product_item_id, bom_name, bom_version, is_default, is_active, memo, created_at, updated_at)
SELECT CONCAT('bh-food0-', item_code, '-eeeeeeeeeeeeeeee'), @food_tenant, item_id, CONCAT(item_name, ' BOM'), 1, 1, 1, 'food BOM', NOW(6), NOW(6)
FROM items WHERE tenant_id=@food_tenant AND item_type='product';

INSERT INTO bom_items (bom_item_id, bom_id, tenant_id, seq_no, material_item_id, qty, unit, loss_rate, memo) VALUES
('bi-food0-p001-01-eeeeeeeeeeeeeeee', 'bh-food0-FP001-eeeeeeeeeeeeeeee', @food_tenant, 1, 'if-food0-semi-0001-eeeeeeeeeeeee', 0.5, 'KG', 2, '식빵 반죽'),
('bi-food0-p001-02-eeeeeeeeeeeeeeee', 'bh-food0-FP001-eeeeeeeeeeeeeeee', @food_tenant, 2, 'if-food0-mat-0012-eeeeeeeeeeeeee', 1, 'EA', 1, '포장'),
('bi-food0-p002-01-eeeeeeeeeeeeeeee', 'bh-food0-FP002-eeeeeeeeeeeeeeee', @food_tenant, 1, 'if-food0-semi-0001-eeeeeeeeeeeee', 0.5, 'KG', 2, '빵 반죽'),
('bi-food0-p002-02-eeeeeeeeeeeeeeee', 'bh-food0-FP002-eeeeeeeeeeeeeeee', @food_tenant, 2, 'if-food0-mat-0012-eeeeeeeeeeeeee', 1, 'EA', 1, '포장'),
('bi-food0-p003-01-eeeeeeeeeeeeeeee', 'bh-food0-FP003-eeeeeeeeeeeeeeee', @food_tenant, 1, 'if-food0-semi-0002-eeeeeeeeeeeee', 0.8, 'KG', 3, '크로와상 반죽'),
('bi-food0-p004-01-eeeeeeeeeeeeeeee', 'bh-food0-FP004-eeeeeeeeeeeeeeee', @food_tenant, 1, 'if-food0-semi-0003-eeeeeeeeeeeee', 0.2, 'KG', 2, '쿠키 반죽'),
('bi-food0-p004-02-eeeeeeeeeeeeeeee', 'bh-food0-FP004-eeeeeeeeeeeeeeee', @food_tenant, 2, 'if-food0-mat-0011-eeeeeeeeeeeeee', 0.05, 'EA', 1, '초코칩'),
('bi-food0-p005-01-eeeeeeeeeeeeeeee', 'bh-food0-FP005-eeeeeeeeeeeeeeee', @food_tenant, 1, 'if-food0-semi-0003-eeeeeeeeeeeee', 0.2, 'KG', 2, '쿠키 반죽'),
('bi-food0-p006-01-eeeeeeeeeeeeeeee', 'bh-food0-FP006-eeeeeeeeeeeeeeee', @food_tenant, 1, 'if-food0-semi-0001-eeeeeeeeeeeee', 0.3, 'KG', 2, '기본 반죽'),
('bi-food0-p006-02-eeeeeeeeeeeeeeee', 'bh-food0-FP006-eeeeeeeeeeeeeeee', @food_tenant, 2, 'if-food0-semi-0004-eeeeeeeeeeeee', 0.5, 'KG', 3, '크림'),
('bi-food0-p006-03-eeeeeeeeeeeeeeee', 'bh-food0-FP006-eeeeeeeeeeeeeeee', @food_tenant, 3, 'if-food0-semi-0006-eeeeeeeeeeeee', 0.1, 'KG', 2, '토핑'),
('bi-food0-p007-01-eeeeeeeeeeeeeeee', 'bh-food0-FP007-eeeeeeeeeeeeeeee', @food_tenant, 1, 'if-food0-semi-0001-eeeeeeeeeeeee', 0.3, 'KG', 2, '반죽'),
('bi-food0-p007-02-eeeeeeeeeeeeeeee', 'bh-food0-FP007-eeeeeeeeeeeeeeee', @food_tenant, 2, 'if-food0-semi-0004-eeeeeeeeeeeee', 0.4, 'KG', 3, '크림'),
('bi-food0-p008-01-eeeeeeeeeeeeeeee', 'bh-food0-FP008-eeeeeeeeeeeeeeee', @food_tenant, 1, 'if-food0-semi-0003-eeeeeeeeeeeee', 0.3, 'KG', 2, '반죽'),
('bi-food0-p009-01-eeeeeeeeeeeeeeee', 'bh-food0-FP009-eeeeeeeeeeeeeeee', @food_tenant, 1, 'if-food0-semi-0003-eeeeeeeeeeeee', 0.2, 'KG', 3, '반죽'),
('bi-food0-p010-01-eeeeeeeeeeeeeeee', 'bh-food0-FP010-eeeeeeeeeeeeeeee', @food_tenant, 1, 'if-food0-semi-0001-eeeeeeeeeeeee', 0.3, 'KG', 2, '반죽'),
('bi-food0-p011-01-eeeeeeeeeeeeeeee', 'bh-food0-FP011-eeeeeeeeeeeeeeee', @food_tenant, 1, 'if-food0-semi-0001-eeeeeeeeeeeee', 0.4, 'KG', 2, '반죽'),
('bi-food0-p012-01-eeeeeeeeeeeeeeee', 'bh-food0-FP012-eeeeeeeeeeeeeeee', @food_tenant, 1, 'if-food0-semi-0001-eeeeeeeeeeeee', 0.5, 'KG', 2, '반죽');

-- opening
INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
SELECT UUID(), @food_tenant, i.item_id,
  CASE i.item_group WHEN '유제품' THEN @cold WHEN '축산' THEN @cold ELSE @wh END,
  CASE i.item_type WHEN 'material' THEN 300 WHEN 'assembly' THEN 100 ELSE 200 END,
  i.cost_price, NOW(6)
FROM items i WHERE i.tenant_id=@food_tenant;

INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo, created_at)
SELECT @food_tenant, i.item_id,
  CASE i.item_group WHEN '유제품' THEN @cold WHEN '축산' THEN @cold ELSE @wh END,
  '2021-01-01', '2021-01', 'in', 'opening', UUID(), 'OPEN-FOOD',
  CASE i.item_type WHEN 'material' THEN 300 WHEN 'assembly' THEN 100 ELSE 200 END,
  0, i.cost_price,
  (CASE i.item_type WHEN 'material' THEN 300 WHEN 'assembly' THEN 100 ELSE 200 END) * i.cost_price,
  'opening food', '2021-01-01 00:00:00.000'
FROM items i WHERE i.tenant_id=@food_tenant;

-- ====== 특화 1: inventory_lots (원재료 12·반제품 6·완제품 12 × 분기별 새 로트 20분기 = 600 기대치, 간소화로 300) ======
-- 각 품목당 분기 1개 로트 = 6년치 × 4분기 = 24분기 × 30 품목 = 720건. 간단화: 분기 1 로트.
INSERT INTO inventory_lots (lot_id, tenant_id, item_id, warehouse_id, lot_no, manufacture_date, expiry_date, initial_qty, current_qty, origin_country, supplier_partner_id, status, created_at, updated_at)
SELECT UUID(), @food_tenant, i.item_id,
  CASE i.item_group WHEN '유제품' THEN @cold WHEN '축산' THEN @cold ELSE @wh END,
  CONCAT('LOT-', i.item_code, '-', DATE_FORMAT(q.d, '%Y%m')),
  q.d,
  CASE
    WHEN i.item_group = '유제품' THEN DATE_ADD(q.d, INTERVAL 14 DAY)
    WHEN i.item_group = '축산' THEN DATE_ADD(q.d, INTERVAL 30 DAY)
    WHEN i.item_type = 'product' AND i.spec LIKE '%냉장%' THEN DATE_ADD(q.d, INTERVAL 10 DAY)
    WHEN i.item_type = 'product' AND i.spec LIKE '%냉동%' THEN DATE_ADD(q.d, INTERVAL 180 DAY)
    ELSE DATE_ADD(q.d, INTERVAL 365 DAY)
  END,
  100, 50, 'KR', NULL, 'active',
  NOW(6), NOW(6)
FROM items i
CROSS JOIN (
  SELECT DATE(CONCAT(y, '-', LPAD(m, 2, '0'), '-01')) d
  FROM (SELECT 2022 y UNION SELECT 2023 UNION SELECT 2024 UNION SELECT 2025 UNION SELECT 2026) Y
  CROSS JOIN (SELECT 1 m UNION SELECT 4 UNION SELECT 7 UNION SELECT 10) M
  WHERE DATE(CONCAT(y, '-', LPAD(m, 2, '0'), '-01')) < CURDATE()
) q
WHERE i.tenant_id=@food_tenant;

-- ====== 특화 2: haccp_logs (일일 점검 5년치 일 3건씩 — 간소화로 월별 5건) ======
INSERT INTO haccp_logs (tenant_id, check_date, check_type, check_location, check_value, pass_fail, checker_employee_id, memo)
SELECT @food_tenant, DATE_ADD(DATE('2021-01-01'), INTERVAL d.n DAY),
  CASE MOD(d.n, 4) WHEN 0 THEN 'temperature' WHEN 1 THEN 'cleanliness' WHEN 2 THEN 'pest' ELSE 'cross_contamination' END,
  CASE MOD(d.n, 3) WHEN 0 THEN '냉장창고' WHEN 1 THEN '생산라인' ELSE '포장실' END,
  CASE MOD(d.n, 4) WHEN 0 THEN CONCAT(-20 + MOD(d.n, 10), '°C') ELSE 'OK' END,
  CASE WHEN MOD(d.n, 50) = 0 THEN 'fail' ELSE 'pass' END,
  'em-food0-0002-eeeeeeeeeeeeeeeeeeee',
  'daily HACCP check'
FROM (
  SELECT a.N + b.N*10 + c.N*100 + d.N*1000 n
  FROM (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) a
  CROSS JOIN (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) b
  CROSS JOIN (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) c
  CROSS JOIN (SELECT 0 N UNION SELECT 1 UNION SELECT 2) d
  HAVING n < 2000 AND MOD(n, 2) = 0
) d;

-- 5년치 거래 (월 PO 8 + SO 20)
DROP TEMPORARY TABLE IF EXISTS tmp_months;
CREATE TEMPORARY TABLE tmp_months (ym_start DATE);
INSERT INTO tmp_months
WITH RECURSIVE m AS (SELECT DATE('2021-02-01') d UNION ALL SELECT d + INTERVAL 1 MONTH FROM m WHERE d + INTERVAL 1 MONTH < '2026-08-01') SELECT d FROM m;

DROP TEMPORARY TABLE IF EXISTS tmp_seq8;
CREATE TEMPORARY TABLE tmp_seq8 (n INT);
INSERT INTO tmp_seq8 VALUES (1),(2),(3),(4),(5),(6),(7),(8);

DROP TEMPORARY TABLE IF EXISTS tmp_seq20;
CREATE TEMPORARY TABLE tmp_seq20 (n INT);
INSERT INTO tmp_seq20 VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12),(13),(14),(15),(16),(17),(18),(19),(20);

INSERT INTO purchase_orders (po_id, tenant_id, po_no, partner_id, employee_id, po_date, expected_date, status, total_amount, vat_amount, memo, created_at, updated_at)
SELECT UUID(), @food_tenant,
  CONCAT('FPO', DATE_FORMAT(m.ym_start,'%y%m'), LPAD(s.n, 3, '0')),
  CASE MOD(CRC32(CONCAT(m.ym_start, s.n)), 6)
    WHEN 0 THEN 'pf-food0-supp-0001-eeeeeeeeeeee' WHEN 1 THEN 'pf-food0-supp-0002-eeeeeeeeeeee'
    WHEN 2 THEN 'pf-food0-supp-0003-eeeeeeeeeeee' WHEN 3 THEN 'pf-food0-supp-0004-eeeeeeeeeeee'
    WHEN 4 THEN 'pf-food0-supp-0005-eeeeeeeeeeee' ELSE 'pf-food0-out0-0001-eeeeeeeeeeee'
  END,
  'em-food0-0011-eeeeeeeeeeeeeeeeeeee',
  DATE_ADD(m.ym_start, INTERVAL (s.n * 3) DAY),
  DATE_ADD(m.ym_start, INTERVAL (s.n * 3 + 3) DAY),
  'received', 0, 0, 'food PO', NOW(6), NOW(6)
FROM tmp_months m CROSS JOIN tmp_seq8 s;

INSERT INTO purchase_order_items (po_item_id, po_id, tenant_id, item_id, ordered_qty, received_qty, unit_price, supply_amount, vat_amount, warehouse_id, item_status)
SELECT UUID(), po.po_id, po.tenant_id,
  CONCAT('if-food0-mat-', LPAD(MOD(CRC32(po.po_id), 12)+1, 4, '0'), '-eeeeeeeeeeeeee'),
  50 + MOD(CRC32(CONCAT(po.po_id,'q')), 200),
  50 + MOD(CRC32(CONCAT(po.po_id,'q')), 200),
  0, 0, 0, @wh, 'received'
FROM purchase_orders po WHERE po.tenant_id=@food_tenant;

UPDATE purchase_order_items poi JOIN items i ON poi.item_id=i.item_id SET poi.unit_price = i.cost_price;
UPDATE purchase_order_items poi SET poi.supply_amount = poi.ordered_qty * poi.unit_price, poi.vat_amount = ROUND(poi.ordered_qty * poi.unit_price * 0.10);
UPDATE purchase_orders po JOIN (SELECT po_id, SUM(supply_amount) s, SUM(vat_amount) v FROM purchase_order_items GROUP BY po_id) x USING(po_id) SET po.total_amount=x.s, po.vat_amount=x.v WHERE po.tenant_id=@food_tenant;

INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, po_id, partner_id, receipt_date, source_type, status, total_amount, vat_amount, memo, created_at)
SELECT UUID(), po.tenant_id, CONCAT('FRC', DATE_FORMAT(po.po_date,'%y%m'), LPAD(ROW_NUMBER() OVER (ORDER BY po.po_date), 4, '0')),
  po.po_id, po.partner_id, DATE_ADD(po.po_date, INTERVAL 2 DAY),
  'purchase_order', 'confirmed', po.total_amount, po.vat_amount, 'food rc', NOW(6)
FROM purchase_orders po WHERE po.tenant_id=@food_tenant;

INSERT INTO purchase_receipt_items (receipt_item_id, receipt_id, tenant_id, po_item_id, item_id, warehouse_id, qty, unit_price, supply_amount, vat_amount)
SELECT UUID(), pr.receipt_id, pr.tenant_id, poi.po_item_id, poi.item_id, @wh, poi.ordered_qty, poi.unit_price, poi.supply_amount, poi.vat_amount
FROM purchase_receipts pr JOIN purchase_order_items poi USING(po_id) WHERE pr.tenant_id=@food_tenant;

INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, partner_id, ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo, created_at)
SELECT pr.tenant_id, pri.item_id, @wh, pr.partner_id,
  pr.receipt_date, DATE_FORMAT(pr.receipt_date,'%Y-%m'),
  'in', 'purchase_receipt', pri.receipt_item_id, pr.receipt_no,
  pri.qty, 0, pri.unit_price, pri.supply_amount, 'food rc ledger', NOW(6)
FROM purchase_receipts pr JOIN purchase_receipt_items pri USING(receipt_id) WHERE pr.tenant_id=@food_tenant;

-- 매출
INSERT INTO sales_orders (order_id, tenant_id, order_no, partner_id, employee_id, order_date, delivery_date, status, total_amount, vat_amount, memo, created_at, updated_at)
SELECT UUID(), @food_tenant,
  CONCAT('FSO', DATE_FORMAT(m.ym_start,'%y%m'), LPAD(s.n, 3, '0')),
  CASE MOD(CRC32(CONCAT(m.ym_start, s.n, 'so')), 10)
    WHEN 0 THEN 'pf-food0-cust-0001-eeeeeeeeeeee' WHEN 1 THEN 'pf-food0-cust-0002-eeeeeeeeeeee'
    WHEN 2 THEN 'pf-food0-cust-0003-eeeeeeeeeeee' WHEN 3 THEN 'pf-food0-cust-0004-eeeeeeeeeeee'
    WHEN 4 THEN 'pf-food0-cust-0005-eeeeeeeeeeee' WHEN 5 THEN 'pf-food0-cust-0006-eeeeeeeeeeee'
    WHEN 6 THEN 'pf-food0-cust-0007-eeeeeeeeeeee' WHEN 7 THEN 'pf-food0-cust-0008-eeeeeeeeeeee'
    WHEN 8 THEN 'pf-food0-cust-0009-eeeeeeeeeeee' ELSE 'pf-food0-cust-0010-eeeeeeeeeeee'
  END,
  'em-food0-0004-eeeeeeeeeeeeeeeeeeee',
  DATE_ADD(m.ym_start, INTERVAL (s.n) DAY),
  DATE_ADD(m.ym_start, INTERVAL (s.n + 2) DAY),
  'invoiced', 0, 0, 'food SO', NOW(6), NOW(6)
FROM tmp_months m CROSS JOIN tmp_seq20 s;

INSERT INTO sales_order_items (order_item_id, order_id, tenant_id, item_id, ordered_qty, delivered_qty, unit_price, supply_amount, vat_amount, item_status)
SELECT UUID(), so.order_id, so.tenant_id,
  CONCAT('if-food0-fin0-', LPAD(MOD(CRC32(so.order_id), 12)+1, 4, '0'), '-eeeeeeeeeeeee'),
  20 + MOD(CRC32(CONCAT(so.order_id,'q')), 80),
  20 + MOD(CRC32(CONCAT(so.order_id,'q')), 80),
  0, 0, 0, 'delivered'
FROM sales_orders so WHERE so.tenant_id=@food_tenant;

UPDATE sales_order_items soi
JOIN sales_orders so ON soi.order_id=so.order_id
JOIN items i ON soi.item_id=i.item_id
JOIN partners p ON so.partner_id=p.partner_id
SET soi.unit_price = i.std_price,
    soi.supply_amount = soi.ordered_qty * i.std_price,
    soi.vat_amount = CASE p.vat_handling WHEN 'standard' THEN ROUND(soi.ordered_qty * i.std_price * 0.10) ELSE 0 END
WHERE so.tenant_id=@food_tenant;

UPDATE sales_orders so JOIN (SELECT order_id, SUM(supply_amount) s, SUM(vat_amount) v FROM sales_order_items GROUP BY order_id) x USING(order_id) SET so.total_amount=x.s, so.vat_amount=x.v WHERE so.tenant_id=@food_tenant;

INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, order_id, partner_id, employee_id, delivery_date, source_type, status, total_amount, vat_amount, memo, created_at, created_by, updated_at)
SELECT UUID(), so.tenant_id,
  CONCAT('FDL', DATE_FORMAT(so.delivery_date,'%y%m'), LPAD(ROW_NUMBER() OVER (ORDER BY so.delivery_date, so.order_id), 4, '0')),
  so.order_id, so.partner_id, so.employee_id,
  so.delivery_date, 'sales_order', 'confirmed', so.total_amount, so.vat_amount,
  'food delivery', NOW(6), so.employee_id, NOW(6)
FROM sales_orders so WHERE so.tenant_id=@food_tenant;

INSERT INTO sales_delivery_items (delivery_item_id, delivery_id, tenant_id, order_item_id, item_id, warehouse_id, qty, unit_price, supply_amount, vat_amount)
SELECT UUID(), sd.delivery_id, sd.tenant_id, soi.order_item_id, soi.item_id, @wh,
  soi.ordered_qty, soi.unit_price, soi.supply_amount, soi.vat_amount
FROM sales_deliveries sd JOIN sales_order_items soi USING(order_id) WHERE sd.tenant_id=@food_tenant;

-- production + 출고 원장
INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo, created_at)
SELECT @food_tenant, sdi.item_id, @wh,
  DATE_SUB(sd.delivery_date, INTERVAL 1 DAY),
  DATE_FORMAT(DATE_SUB(sd.delivery_date, INTERVAL 1 DAY), '%Y-%m'),
  'in', 'production', UUID(), CONCAT('PRD-', sd.delivery_no),
  sdi.qty, 0, (SELECT cost_price FROM items WHERE item_id=sdi.item_id),
  sdi.qty * COALESCE((SELECT cost_price FROM items WHERE item_id=sdi.item_id), 0),
  'food production', NOW(6)
FROM sales_deliveries sd JOIN sales_delivery_items sdi USING(delivery_id) WHERE sd.tenant_id=@food_tenant;

INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, partner_id, ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo, created_at)
SELECT sd.tenant_id, sdi.item_id, @wh, sd.partner_id,
  sd.delivery_date, DATE_FORMAT(sd.delivery_date,'%Y-%m'),
  'out', 'sales_delivery', sdi.delivery_item_id, sd.delivery_no,
  0, sdi.qty, sdi.unit_price, sdi.supply_amount, 'food out', NOW(6)
FROM sales_deliveries sd JOIN sales_delivery_items sdi USING(delivery_id) WHERE sd.tenant_id=@food_tenant;

UPDATE item_stock s
JOIN (SELECT tenant_id, item_id, warehouse_id, SUM(qty_in)-SUM(qty_out) net FROM stock_ledger WHERE tenant_id=@food_tenant GROUP BY tenant_id, item_id, warehouse_id) c USING(tenant_id, item_id, warehouse_id)
SET s.current_qty = c.net;

-- 집계
INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, collection_method, ref_doc_type, memo, created_at, updated_at)
SELECT UUID(), @food_tenant, sd.partner_id, DATE_ADD(LAST_DAY(sd.delivery_date), INTERVAL 15 DAY),
  ROUND((sd.total_amount+sd.vat_amount) * 0.70, 0), 'bank', 'sales_delivery', 'food coll', NOW(6), NOW(6)
FROM sales_deliveries sd WHERE sd.tenant_id=@food_tenant AND sd.is_deleted=0;

INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, payment_method, memo, created_at, updated_at)
SELECT UUID(), @food_tenant, pr.partner_id, 'general',
  ROUND((pr.total_amount+pr.vat_amount) * 0.80, 0), DATE_ADD(LAST_DAY(pr.receipt_date), INTERVAL 10 DAY),
  'bank', 'food pmt', NOW(6), NOW(6)
FROM purchase_receipts pr WHERE pr.tenant_id=@food_tenant;

INSERT INTO partner_balance (balance_id, tenant_id, partner_id, total_sales, total_receipt, total_purchase, total_payment, last_updated_at)
SELECT UUID(), @food_tenant, p.partner_id,
  COALESCE(s.v,0), COALESCE(c.v,0), COALESCE(pu.v,0), COALESCE(pm.v,0), NOW(6)
FROM partners p
LEFT JOIN (SELECT partner_id, SUM(total_amount+vat_amount) v FROM sales_deliveries WHERE tenant_id=@food_tenant AND is_deleted=0 GROUP BY partner_id) s ON p.partner_id=s.partner_id
LEFT JOIN (SELECT partner_id, SUM(amount) v FROM collections WHERE tenant_id=@food_tenant GROUP BY partner_id) c ON p.partner_id=c.partner_id
LEFT JOIN (SELECT partner_id, SUM(total_amount+vat_amount) v FROM purchase_receipts WHERE tenant_id=@food_tenant GROUP BY partner_id) pu ON p.partner_id=pu.partner_id
LEFT JOIN (SELECT partner_id, SUM(amount) v FROM payments WHERE tenant_id=@food_tenant GROUP BY partner_id) pm ON p.partner_id=pm.partner_id
WHERE p.tenant_id=@food_tenant;

INSERT INTO monthly_closing (closing_id, tenant_id, `year_month`, status, sales_amount, purchase_amount, receipt_amount, payment_amount, memo)
SELECT UUID(), @food_tenant, months.ym,
  CASE WHEN months.ym < DATE_FORMAT(CURDATE() - INTERVAL 2 MONTH, '%Y%m') THEN 'closed' ELSE 'open' END,
  COALESCE(s.v,0), COALESCE(p.v,0), COALESCE(c.v,0), COALESCE(pmt.v,0), 'food monthly'
FROM (SELECT DATE_FORMAT(DATE('2021-01-01') + INTERVAL n MONTH, '%Y%m') ym
  FROM (SELECT a.N + b.N*10 n FROM (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) a, (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6) b) x WHERE n < 67) months
LEFT JOIN (SELECT DATE_FORMAT(delivery_date,'%Y%m') ym, SUM(total_amount+vat_amount) v FROM sales_deliveries WHERE tenant_id=@food_tenant AND is_deleted=0 GROUP BY ym) s USING(ym)
LEFT JOIN (SELECT DATE_FORMAT(receipt_date,'%Y%m') ym, SUM(total_amount+vat_amount) v FROM purchase_receipts WHERE tenant_id=@food_tenant GROUP BY ym) p USING(ym)
LEFT JOIN (SELECT DATE_FORMAT(collection_date,'%Y%m') ym, SUM(amount) v FROM collections WHERE tenant_id=@food_tenant GROUP BY ym) c USING(ym)
LEFT JOIN (SELECT DATE_FORMAT(payment_date,'%Y%m') ym, SUM(amount) v FROM payments WHERE tenant_id=@food_tenant GROUP BY ym) pmt USING(ym);

DROP TEMPORARY TABLE tmp_months;
DROP TEMPORARY TABLE tmp_seq8;
DROP TEMPORARY TABLE tmp_seq20;

SELECT
  (SELECT COUNT(*) FROM purchase_orders WHERE tenant_id=@food_tenant) po,
  (SELECT COUNT(*) FROM sales_orders WHERE tenant_id=@food_tenant) so,
  (SELECT COUNT(*) FROM stock_ledger WHERE tenant_id=@food_tenant) ledger,
  (SELECT COUNT(*) FROM inventory_lots WHERE tenant_id=@food_tenant) lots,
  (SELECT COUNT(*) FROM haccp_logs WHERE tenant_id=@food_tenant) haccp,
  (SELECT COUNT(*) FROM partner_balance WHERE tenant_id=@food_tenant) balance,
  (SELECT COUNT(*) FROM monthly_closing WHERE tenant_id=@food_tenant) closing;
