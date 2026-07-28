SET @@session.block_encryption_mode = 'aes-256-cbc';
SET @key = _binary 'hitpan-aes-key-32bytes-exactly!!';
SET @wood_tenant = 'tenant-wood000-d000-dddd-dddddddddd';
SET @wh = 'wh-wood0-main-0000-dddddddddddddddd';
SET SESSION max_recursive_iterations = 1000;

-- tenants + settings + warehouses + accounts
INSERT INTO tenants (tenant_id, tenant_code, company_name, biz_no, biz_no_hash, ceo_name, tel, email, address, max_users, status, db_host, db_name, license_key_hash, reseller_tier, biz_type, biz_item, tax_type, fiscal_month, created_at, updated_at)
VALUES (@wood_tenant, 'WOOD001', '(주)파주가구',
  TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1458823456', @key, @i))), SHA2('1458823456',256),
  '장대표', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-945-1234', @key, @i))),
  TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('ceo@paju-furniture.co.kr', @key, @i))),
  '경기도 파주시 가구단지', 10, 'active', 'localhost', 'hitpan_erp',
  SHA2('wood-license', 256), 1, '제조업', '가구·목공 맞춤', 'taxable', 12, NOW(6), NOW(6));

INSERT INTO tenant_settings (tenant_id, allow_force_price_input, allow_force_vat_input, allow_zero_price, allow_past_edit, allow_force_stock_adjust, allow_credit_override, price_deviation_limit, force_edit_require_password, stock_eval_method, use_multi_warehouse, stock_shortage_alert, allow_minus_stock, price_input_type, auto_vat_adjust, vat_round_type, price_a_rate, price_b_rate, price_c_rate, price_d_rate, price_e_rate, use_credit_limit, credit_limit_amount, show_purchase_price, use_sales_by_employee, use_personal_info_protect, industry_type)
VALUES (@wood_tenant, 1, 0, 0, 0, 1, 0, 30, 1, 'moving_avg', 1, 1, 0, 'net', 1, 'round', 1.00, 1.10, 1.20, 1.35, 1.55, 1, 30000000, 0, 1, 1, 'wood');

INSERT INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, location, is_active, created_at, updated_at) VALUES
  (@wh, @wood_tenant, 'MAIN', '본사창고', 'normal', '파주 본사', 1, NOW(6), NOW(6)),
  ('wh-wood0-shop-0000-dddddddddddddddd', @wood_tenant, 'SHOP', '전시장창고', 'normal', '파주 쇼룸', 1, NOW(6), NOW(6));

INSERT INTO accounts (account_code, tenant_id, account_name, account_type, sort_order, is_active, created_at)
SELECT account_code, @wood_tenant, account_name, account_type, sort_order, is_active, NOW(6)
FROM accounts WHERE tenant_id='452ca266-97b9-4cd1-a0ac-2f37830c81f6';

-- partners 10곳 (고객 5 + 공급사 3 + 외주 2) — B2C 없음, 전부 B2B 매장·프로젝트
INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, biz_no, biz_no_hash, ceo_name, biz_type, biz_item, tel, address, credit_limit, payment_terms, bank_name, bank_account, account_holder, is_active, created_at, updated_at, price_grade, tax_type, vat_handling, price_display_preference) VALUES
('pw-wood0-cust-0001-dddddddddddd', @wood_tenant, 'WC001', '한샘리모델링', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1128834567', @key, @i))), SHA2('1128834567',256),
 '박한샘', '도소매업', '가구유통',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('02-3019-8000', @key, @i))),
 '서울시 마포구', 80000000, 60, '국민은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-401-000001', @key, @i))),
 '한샘리모델링', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inherit'),
('pw-wood0-cust-0002-dddddddddddd', @wood_tenant, 'WC002', '현대리바트', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2148834578', @key, @i))), SHA2('2148834578',256),
 '정리바', '도소매업', '가구유통',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-900-8000', @key, @i))),
 '경기도 용인시', 60000000, 60, '우리은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('101-402-000002', @key, @i))),
 '현대리바트', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inclusive'),
('pw-wood0-cust-0003-dddddddddddd', @wood_tenant, 'WC003', '리모델링A', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('3088834589', @key, @i))), SHA2('3088834589',256),
 '이리모', '서비스업', '인테리어',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-945-5000', @key, @i))),
 '경기도 파주시', 30000000, 30, '신한은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('110-403-000003', @key, @i))),
 '리모델링A', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit'),
('pw-wood0-cust-0004-dddddddddddd', @wood_tenant, 'WC004', '카페체인', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2208834590', @key, @i))), SHA2('2208834590',256),
 '강카페', '음식점업', '프랜차이즈',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('02-555-6000', @key, @i))),
 '서울시 강남구', 50000000, 60, '기업은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('312-404-000004', @key, @i))),
 '카페체인', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inclusive'),
('pw-wood0-cust-0005-dddddddddddd', @wood_tenant, 'WC005', '오피스인테리어', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2408834501', @key, @i))), SHA2('2408834501',256),
 '윤오피', '서비스업', '사무용가구',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-632-7000', @key, @i))),
 '경기도 분당구', 20000000, 90, '하나은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('310-405-000005', @key, @i))),
 '오피스인테리어', 1, NOW(6), NOW(6), 'C', 'taxable', 'zero', 'inherit'),
('pw-wood0-supp-0001-dddddddddddd', @wood_tenant, 'WS001', '원목수입', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2108834512', @key, @i))), SHA2('2108834512',256),
 '조원목', '도매업', '수입원목',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-945-9000', @key, @i))),
 '경기도 파주시', 0, 30, '국민은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-506-000006', @key, @i))),
 '원목수입', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inherit'),
('pw-wood0-supp-0002-dddddddddddd', @wood_tenant, 'WS002', 'MDF판재', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1408834523', @key, @i))), SHA2('1408834523',256),
 '임엠디', '제조업', 'MDF',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-945-4000', @key, @i))),
 '경기도 파주시', 0, 60, '우리은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('101-507-000007', @key, @i))),
 'MDF판재', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inclusive'),
('pw-wood0-supp-0003-dddddddddddd', @wood_tenant, 'WS003', '하드웨어유통', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1318834534', @key, @i))), SHA2('1318834534',256),
 '최하드', '도매업', '경첩·손잡이',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-945-3000', @key, @i))),
 '경기도 파주시', 0, 30, '신한은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('110-508-000008', @key, @i))),
 '하드웨어유통', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit'),
('pw-wood0-out0-0001-dddddddddddd', @wood_tenant, 'WO001', '도장외주', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2218834545', @key, @i))), SHA2('2218834545',256),
 '서도장', '제조업', '가구도장',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-945-2000', @key, @i))),
 '경기도 파주시', 0, 30, '기업은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('312-609-000009', @key, @i))),
 '도장외주', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit'),
('pw-wood0-out0-0002-dddddddddddd', @wood_tenant, 'WO002', '조각외주', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2318834556', @key, @i))), SHA2('2318834556',256),
 '박조각', '제조업', 'CNC가공',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-945-6000', @key, @i))),
 '경기도 파주시', 0, 60, '하나은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('310-610-000010', @key, @i))),
 '조각외주', 1, NOW(6), NOW(6), 'C', 'taxable', 'standard', 'inclusive');

-- 직원 7명
INSERT INTO employees (employee_id, tenant_id, emp_no, emp_name, position, job_title, emp_type, join_date, phone, email, bank_name, bank_account, is_active, created_at, updated_at, role) VALUES
('em-wood0-0001-dddddddddddddddddddd', @wood_tenant, 'E001', '장대표', 'ceo', '대표이사', 'regular', '2015-03-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-4001-0001', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('ceo@paju-furniture.co.kr', @key, @i))),
 '국민은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-010-000001', @key, @i))),
 1, NOW(6), NOW(6), 'tenant_admin'),
('em-wood0-0002-dddddddddddddddddddd', @wood_tenant, 'E002', '조부장', 'manager', '공방장', 'regular', '2015-06-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-4002-0002', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('cho@paju-furniture.co.kr', @key, @i))),
 '우리은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('101-020-000002', @key, @i))),
 1, NOW(6), NOW(6), 'production_manager'),
('em-wood0-0003-dddddddddddddddddddd', @wood_tenant, 'E003', '서사원', 'staff', '매장영업', 'regular', '2021-05-10',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-4003-0003', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('seo@paju-furniture.co.kr', @key, @i))),
 '신한은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('110-030-000003', @key, @i))),
 1, NOW(6), NOW(6), 'sales_user'),
('em-wood0-0004-dddddddddddddddddddd', @wood_tenant, 'E004', '김여사', 'clerk', '경리', 'regular', '2016-01-15',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-4004-0004', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('kim@paju-furniture.co.kr', @key, @i))),
 '기업은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('312-040-000004', @key, @i))),
 1, NOW(6), NOW(6), 'accountant'),
('em-wood0-0005-dddddddddddddddddddd', @wood_tenant, 'E005', '이목수', 'staff', '목수', 'regular', '2016-03-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-4005-0005', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('lee@paju-furniture.co.kr', @key, @i))),
 '하나은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('310-050-000005', @key, @i))),
 1, NOW(6), NOW(6), 'production_user'),
('em-wood0-0006-dddddddddddddddddddd', @wood_tenant, 'E006', '박목수', 'staff', '목수', 'regular', '2017-07-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-4006-0006', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('park@paju-furniture.co.kr', @key, @i))),
 '농협', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('351-060-000006', @key, @i))),
 1, NOW(6), NOW(6), 'production_user'),
('em-wood0-0007-dddddddddddddddddddd', @wood_tenant, 'E007', '정도장', 'staff', '도장기사', 'regular', '2019-09-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-4007-0007', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('jung@paju-furniture.co.kr', @key, @i))),
 '국민은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-070-000007', @key, @i))),
 1, NOW(6), NOW(6), 'production_user');

-- items 30 (원자재 12·반제품 6·완제품 12)
INSERT INTO items (item_id, tenant_id, item_code, item_name, item_type, unit, std_price, price_a, price_b, price_c, price_d, price_e, cost_price, tax_type, safe_stock, is_active, created_at, updated_at, item_group, spec, purchase_price, sale_price, standard_price, safety_stock) VALUES
-- 원자재
('iw-wood0-mat-0001-dddddddddddddd', @wood_tenant, 'WM001', 'MDF 18T 2440x1220', 'material', 'EA', 35000, 35000, 38500, 42000, 47250, 54250, 30800, 'taxable', 200, 1, NOW(6), NOW(6), '판재', 'MDF 18T', 30800, 35000, 35000, 200),
('iw-wood0-mat-0002-dddddddddddddd', @wood_tenant, 'WM002', 'MDF 15T 2440x1220', 'material', 'EA', 28000, 28000, 30800, 33600, 37800, 43400, 24640, 'taxable', 200, 1, NOW(6), NOW(6), '판재', 'MDF 15T', 24640, 28000, 28000, 200),
('iw-wood0-mat-0003-dddddddddddddd', @wood_tenant, 'WM003', '자작합판 12T', 'material', 'EA', 68000, 68000, 74800, 81600, 91800, 105400, 59840, 'taxable', 80, 1, NOW(6), NOW(6), '판재', '자작나무', 59840, 68000, 68000, 80),
('iw-wood0-mat-0004-dddddddddddddd', @wood_tenant, 'WM004', '원목 오크 판재', 'material', 'SQM', 85000, 85000, 93500, 102000, 114750, 131750, 74800, 'taxable', 30, 1, NOW(6), NOW(6), '원목', 'Oak', 74800, 85000, 85000, 30),
('iw-wood0-mat-0005-dddddddddddddd', @wood_tenant, 'WM005', '원목 호두 판재', 'material', 'SQM', 120000, 120000, 132000, 144000, 162000, 186000, 105600, 'taxable', 20, 1, NOW(6), NOW(6), '원목', 'Walnut', 105600, 120000, 120000, 20),
('iw-wood0-mat-0006-dddddddddddddd', @wood_tenant, 'WM006', '경첩 35mm', 'material', 'EA', 1500, 1500, 1650, 1800, 2025, 2325, 1320, 'taxable', 2000, 1, NOW(6), NOW(6), '하드웨어', '일반', 1320, 1500, 1500, 2000),
('iw-wood0-mat-0007-dddddddddddddd', @wood_tenant, 'WM007', '손잡이 바형 128mm', 'material', 'EA', 3500, 3500, 3850, 4200, 4725, 5425, 3080, 'taxable', 1000, 1, NOW(6), NOW(6), '하드웨어', '알루미늄', 3080, 3500, 3500, 1000),
('iw-wood0-mat-0008-dddddddddddddd', @wood_tenant, 'WM008', '나사 4x30', 'material', 'EA', 30, 30, 33, 36, 41, 47, 26, 'taxable', 20000, 1, NOW(6), NOW(6), '체결', 'Bugle head', 26, 30, 30, 20000),
('iw-wood0-mat-0009-dddddddddddddd', @wood_tenant, 'WM009', '목공용 본드', 'material', 'L', 8500, 8500, 9350, 10200, 11475, 13175, 7480, 'taxable', 50, 1, NOW(6), NOW(6), '접착', '1L', 7480, 8500, 8500, 50),
('iw-wood0-mat-0010-dddddddddddddd', @wood_tenant, 'WM010', '수성 무광 도료 무채색', 'material', 'L', 18000, 18000, 19800, 21600, 24300, 27900, 15840, 'taxable', 30, 1, NOW(6), NOW(6), '도료', '수성', 15840, 18000, 18000, 30),
('iw-wood0-mat-0011-dddddddddddddd', @wood_tenant, 'WM011', '유성 유광 도료 갈색', 'material', 'L', 22000, 22000, 24200, 26400, 29700, 34100, 19360, 'taxable', 30, 1, NOW(6), NOW(6), '도료', '유성', 19360, 22000, 22000, 30),
('iw-wood0-mat-0012-dddddddddddddd', @wood_tenant, 'WM012', '사포 #240', 'material', 'EA', 500, 500, 550, 600, 675, 775, 440, 'taxable', 500, 1, NOW(6), NOW(6), '부자재', '20x28cm', 440, 500, 500, 500),
-- 반제품 6
('iw-wood0-semi-0001-ddddddddddddd', @wood_tenant, 'WSP001', '책장 반제품 재단', 'assembly', 'EA', 85000, 85000, 93500, 102000, 114750, 131750, 74800, 'taxable', 20, 1, NOW(6), NOW(6), '반제품', '재단 완', 74800, 85000, 85000, 20),
('iw-wood0-semi-0002-ddddddddddddd', @wood_tenant, 'WSP002', '책장 반제품 조립', 'assembly', 'EA', 135000, 135000, 148500, 162000, 182250, 209250, 118800, 'taxable', 15, 1, NOW(6), NOW(6), '반제품', '조립 완', 118800, 135000, 135000, 15),
('iw-wood0-semi-0003-ddddddddddddd', @wood_tenant, 'WSP003', '식탁 반제품 재단', 'assembly', 'EA', 180000, 180000, 198000, 216000, 243000, 279000, 158400, 'taxable', 10, 1, NOW(6), NOW(6), '반제품', '재단 완', 158400, 180000, 180000, 10),
('iw-wood0-semi-0004-ddddddddddddd', @wood_tenant, 'WSP004', '식탁 반제품 조립', 'assembly', 'EA', 280000, 280000, 308000, 336000, 378000, 434000, 246400, 'taxable', 8, 1, NOW(6), NOW(6), '반제품', '조립 완', 246400, 280000, 280000, 8),
('iw-wood0-semi-0005-ddddddddddddd', @wood_tenant, 'WSP005', '의자 반제품 조립', 'assembly', 'EA', 55000, 55000, 60500, 66000, 74250, 85250, 48400, 'taxable', 30, 1, NOW(6), NOW(6), '반제품', '조립 완', 48400, 55000, 55000, 30),
('iw-wood0-semi-0006-ddddddddddddd', @wood_tenant, 'WSP006', '서랍장 반제품 조립', 'assembly', 'EA', 220000, 220000, 242000, 264000, 297000, 341000, 193600, 'taxable', 10, 1, NOW(6), NOW(6), '반제품', '조립 완', 193600, 220000, 220000, 10),
-- 완제품 12
('iw-wood0-fin0-0001-ddddddddddddd', @wood_tenant, 'WP001', '책장 5단 오크', 'product', 'EA', 320000, 320000, 352000, 384000, 432000, 496000, 272000, 'taxable', 10, 1, NOW(6), NOW(6), '완제품', '도장완', 272000, 320000, 320000, 10),
('iw-wood0-fin0-0002-ddddddddddddd', @wood_tenant, 'WP002', '책장 5단 호두', 'product', 'EA', 420000, 420000, 462000, 504000, 567000, 651000, 357000, 'taxable', 8, 1, NOW(6), NOW(6), '완제품', '도장완', 357000, 420000, 420000, 8),
('iw-wood0-fin0-0003-ddddddddddddd', @wood_tenant, 'WP003', '책장 7단 오크', 'product', 'EA', 480000, 480000, 528000, 576000, 648000, 744000, 408000, 'taxable', 6, 1, NOW(6), NOW(6), '완제품', '도장완', 408000, 480000, 480000, 6),
('iw-wood0-fin0-0004-ddddddddddddd', @wood_tenant, 'WP004', '식탁 4인 오크', 'product', 'EA', 680000, 680000, 748000, 816000, 918000, 1054000, 578000, 'taxable', 5, 1, NOW(6), NOW(6), '완제품', '도장완', 578000, 680000, 680000, 5),
('iw-wood0-fin0-0005-ddddddddddddd', @wood_tenant, 'WP005', '식탁 6인 호두', 'product', 'EA', 1280000, 1280000, 1408000, 1536000, 1728000, 1984000, 1088000, 'taxable', 3, 1, NOW(6), NOW(6), '완제품', '도장완', 1088000, 1280000, 1280000, 3),
('iw-wood0-fin0-0006-ddddddddddddd', @wood_tenant, 'WP006', '식탁의자 오크', 'product', 'EA', 180000, 180000, 198000, 216000, 243000, 279000, 153000, 'taxable', 30, 1, NOW(6), NOW(6), '완제품', '도장완', 153000, 180000, 180000, 30),
('iw-wood0-fin0-0007-ddddddddddddd', @wood_tenant, 'WP007', '사이드테이블', 'product', 'EA', 240000, 240000, 264000, 288000, 324000, 372000, 204000, 'taxable', 10, 1, NOW(6), NOW(6), '완제품', '도장완', 204000, 240000, 240000, 10),
('iw-wood0-fin0-0008-ddddddddddddd', @wood_tenant, 'WP008', '4단 서랍장', 'product', 'EA', 560000, 560000, 616000, 672000, 756000, 868000, 476000, 'taxable', 5, 1, NOW(6), NOW(6), '완제품', '도장완', 476000, 560000, 560000, 5),
('iw-wood0-fin0-0009-ddddddddddddd', @wood_tenant, 'WP009', '수납장 낮은형', 'product', 'EA', 380000, 380000, 418000, 456000, 513000, 589000, 323000, 'taxable', 8, 1, NOW(6), NOW(6), '완제품', '도장완', 323000, 380000, 380000, 8),
('iw-wood0-fin0-0010-ddddddddddddd', @wood_tenant, 'WP010', 'TV장 원목', 'product', 'EA', 520000, 520000, 572000, 624000, 702000, 806000, 442000, 'taxable', 5, 1, NOW(6), NOW(6), '완제품', '도장완', 442000, 520000, 520000, 5),
('iw-wood0-fin0-0011-ddddddddddddd', @wood_tenant, 'WP011', '맞춤 책장 (대)', 'product', 'EA', 1200000, 1200000, 1320000, 1440000, 1620000, 1860000, 1020000, 'taxable', 2, 1, NOW(6), NOW(6), '완제품', '맞춤', 1020000, 1200000, 1200000, 2),
('iw-wood0-fin0-0012-ddddddddddddd', @wood_tenant, 'WP012', '맞춤 싱크장', 'product', 'EA', 1850000, 1850000, 2035000, 2220000, 2497500, 2867500, 1572500, 'taxable', 2, 1, NOW(6), NOW(6), '완제품', '맞춤', 1572500, 1850000, 1850000, 2);

-- BOM: 완제품 12 × 반제품 + 하드웨어 + 도료
INSERT INTO bom_headers (bom_id, tenant_id, product_item_id, bom_name, bom_version, is_default, is_active, memo, created_at, updated_at)
SELECT CONCAT('bh-wood0-', item_code, '-dddddddddddddddd'), @wood_tenant, item_id, CONCAT(item_name, ' BOM'), 1, 1, 1, 'wood BOM', NOW(6), NOW(6)
FROM items WHERE tenant_id=@wood_tenant AND item_type='product';

INSERT INTO bom_items (bom_item_id, bom_id, tenant_id, seq_no, material_item_id, qty, unit, loss_rate, memo) VALUES
-- WP001 책장 오크: 책장 반제품 조립 + 하드웨어 + 도료
('bi-wood0-p001-01-dddddddddddddddd', 'bh-wood0-WP001-dddddddddddddddd', @wood_tenant, 1, 'iw-wood0-semi-0002-ddddddddddddd', 1, 'EA', 2, '책장 조립 반제품'),
('bi-wood0-p001-02-dddddddddddddddd', 'bh-wood0-WP001-dddddddddddddddd', @wood_tenant, 2, 'iw-wood0-mat-0010-dddddddddddddd', 2, 'L', 3, '무광 도료'),
('bi-wood0-p001-03-dddddddddddddddd', 'bh-wood0-WP001-dddddddddddddddd', @wood_tenant, 3, 'iw-wood0-mat-0006-dddddddddddddd', 10, 'EA', 1, '경첩'),
('bi-wood0-p002-01-dddddddddddddddd', 'bh-wood0-WP002-dddddddddddddddd', @wood_tenant, 1, 'iw-wood0-semi-0002-ddddddddddddd', 1, 'EA', 2, '책장 조립'),
('bi-wood0-p002-02-dddddddddddddddd', 'bh-wood0-WP002-dddddddddddddddd', @wood_tenant, 2, 'iw-wood0-mat-0011-dddddddddddddd', 2, 'L', 3, '유광 도료'),
('bi-wood0-p003-01-dddddddddddddddd', 'bh-wood0-WP003-dddddddddddddddd', @wood_tenant, 1, 'iw-wood0-semi-0002-ddddddddddddd', 1.4, 'EA', 3, '책장 조립 대'),
('bi-wood0-p003-02-dddddddddddddddd', 'bh-wood0-WP003-dddddddddddddddd', @wood_tenant, 2, 'iw-wood0-mat-0010-dddddddddddddd', 3, 'L', 3, '도료'),
('bi-wood0-p004-01-dddddddddddddddd', 'bh-wood0-WP004-dddddddddddddddd', @wood_tenant, 1, 'iw-wood0-semi-0004-ddddddddddddd', 1, 'EA', 2, '식탁 조립'),
('bi-wood0-p004-02-dddddddddddddddd', 'bh-wood0-WP004-dddddddddddddddd', @wood_tenant, 2, 'iw-wood0-mat-0010-dddddddddddddd', 2.5, 'L', 3, '도료'),
('bi-wood0-p005-01-dddddddddddddddd', 'bh-wood0-WP005-dddddddddddddddd', @wood_tenant, 1, 'iw-wood0-semi-0004-ddddddddddddd', 1, 'EA', 2, '식탁 조립'),
('bi-wood0-p005-02-dddddddddddddddd', 'bh-wood0-WP005-dddddddddddddddd', @wood_tenant, 2, 'iw-wood0-mat-0005-dddddddddddddd', 2, 'SQM', 4, '호두 판재 추가'),
('bi-wood0-p006-01-dddddddddddddddd', 'bh-wood0-WP006-dddddddddddddddd', @wood_tenant, 1, 'iw-wood0-semi-0005-ddddddddddddd', 1, 'EA', 2, '의자 조립'),
('bi-wood0-p006-02-dddddddddddddddd', 'bh-wood0-WP006-dddddddddddddddd', @wood_tenant, 2, 'iw-wood0-mat-0010-dddddddddddddd', 0.5, 'L', 3, '도료'),
('bi-wood0-p007-01-dddddddddddddddd', 'bh-wood0-WP007-dddddddddddddddd', @wood_tenant, 1, 'iw-wood0-semi-0001-ddddddddddddd', 1, 'EA', 2, '재단 반제품'),
('bi-wood0-p007-02-dddddddddddddddd', 'bh-wood0-WP007-dddddddddddddddd', @wood_tenant, 2, 'iw-wood0-mat-0010-dddddddddddddd', 1, 'L', 3, '도료'),
('bi-wood0-p008-01-dddddddddddddddd', 'bh-wood0-WP008-dddddddddddddddd', @wood_tenant, 1, 'iw-wood0-semi-0006-ddddddddddddd', 1, 'EA', 2, '서랍장 조립'),
('bi-wood0-p008-02-dddddddddddddddd', 'bh-wood0-WP008-dddddddddddddddd', @wood_tenant, 2, 'iw-wood0-mat-0007-dddddddddddddd', 4, 'EA', 1, '손잡이'),
('bi-wood0-p009-01-dddddddddddddddd', 'bh-wood0-WP009-dddddddddddddddd', @wood_tenant, 1, 'iw-wood0-semi-0002-ddddddddddddd', 0.8, 'EA', 2, '수납장'),
('bi-wood0-p009-02-dddddddddddddddd', 'bh-wood0-WP009-dddddddddddddddd', @wood_tenant, 2, 'iw-wood0-mat-0010-dddddddddddddd', 1.5, 'L', 3, '도료'),
('bi-wood0-p010-01-dddddddddddddddd', 'bh-wood0-WP010-dddddddddddddddd', @wood_tenant, 1, 'iw-wood0-semi-0002-ddddddddddddd', 1.2, 'EA', 2, 'TV장'),
('bi-wood0-p010-02-dddddddddddddddd', 'bh-wood0-WP010-dddddddddddddddd', @wood_tenant, 2, 'iw-wood0-mat-0011-dddddddddddddd', 2, 'L', 3, '도료'),
('bi-wood0-p011-01-dddddddddddddddd', 'bh-wood0-WP011-dddddddddddddddd', @wood_tenant, 1, 'iw-wood0-semi-0002-ddddddddddddd', 2, 'EA', 3, '맞춤 책장 반제품'),
('bi-wood0-p011-02-dddddddddddddddd', 'bh-wood0-WP011-dddddddddddddddd', @wood_tenant, 2, 'iw-wood0-mat-0004-dddddddddddddd', 3, 'SQM', 5, '오크 추가'),
('bi-wood0-p012-01-dddddddddddddddd', 'bh-wood0-WP012-dddddddddddddddd', @wood_tenant, 1, 'iw-wood0-semi-0004-ddddddddddddd', 1, 'EA', 3, '맞춤 싱크장'),
('bi-wood0-p012-02-dddddddddddddddd', 'bh-wood0-WP012-dddddddddddddddd', @wood_tenant, 2, 'iw-wood0-mat-0005-dddddddddddddd', 4, 'SQM', 5, '호두 판재');

-- Opening 재고
INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
SELECT UUID(), @wood_tenant, i.item_id, @wh,
  CASE i.item_type WHEN 'material' THEN 500 WHEN 'assembly' THEN 50 ELSE 30 END,
  i.cost_price, NOW(6)
FROM items i WHERE i.tenant_id=@wood_tenant;

INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo, created_at)
SELECT @wood_tenant, i.item_id, @wh, '2021-01-01', '2021-01', 'in', 'opening', UUID(), 'OPEN-WOOD',
  CASE i.item_type WHEN 'material' THEN 500 WHEN 'assembly' THEN 50 ELSE 30 END,
  0, i.cost_price,
  (CASE i.item_type WHEN 'material' THEN 500 WHEN 'assembly' THEN 50 ELSE 30 END) * i.cost_price,
  'opening wood', '2021-01-01 00:00:00.000'
FROM items i WHERE i.tenant_id=@wood_tenant;

-- 5년치 거래 (월 PO 4건 × 66 = 264, SO 8건 × 66 = 528)
DROP TEMPORARY TABLE IF EXISTS tmp_months;
CREATE TEMPORARY TABLE tmp_months (ym_start DATE);
INSERT INTO tmp_months
WITH RECURSIVE m AS (SELECT DATE('2021-02-01') d UNION ALL SELECT d + INTERVAL 1 MONTH FROM m WHERE d + INTERVAL 1 MONTH < '2026-08-01') SELECT d FROM m;

DROP TEMPORARY TABLE IF EXISTS tmp_seq4;
CREATE TEMPORARY TABLE tmp_seq4 (n INT);
INSERT INTO tmp_seq4 VALUES (1),(2),(3),(4);

DROP TEMPORARY TABLE IF EXISTS tmp_seq8;
CREATE TEMPORARY TABLE tmp_seq8 (n INT);
INSERT INTO tmp_seq8 VALUES (1),(2),(3),(4),(5),(6),(7),(8);

INSERT INTO purchase_orders (po_id, tenant_id, po_no, partner_id, employee_id, po_date, expected_date, status, total_amount, vat_amount, memo, created_at, updated_at)
SELECT UUID(), @wood_tenant,
  CONCAT('WPO', DATE_FORMAT(m.ym_start,'%y%m'), LPAD(s.n, 3, '0')),
  CASE MOD(CRC32(CONCAT(m.ym_start, s.n)), 5)
    WHEN 0 THEN 'pw-wood0-supp-0001-dddddddddddd'
    WHEN 1 THEN 'pw-wood0-supp-0002-dddddddddddd'
    WHEN 2 THEN 'pw-wood0-supp-0003-dddddddddddd'
    WHEN 3 THEN 'pw-wood0-out0-0001-dddddddddddd'
    ELSE 'pw-wood0-out0-0002-dddddddddddd'
  END,
  'em-wood0-0002-dddddddddddddddddddd',
  DATE_ADD(m.ym_start, INTERVAL (s.n * 6) DAY),
  DATE_ADD(m.ym_start, INTERVAL (s.n * 6 + 7) DAY),
  'received', 0, 0, 'wood PO', NOW(6), NOW(6)
FROM tmp_months m CROSS JOIN tmp_seq4 s;

INSERT INTO purchase_order_items (po_item_id, po_id, tenant_id, item_id, ordered_qty, received_qty, unit_price, supply_amount, vat_amount, warehouse_id, item_status)
SELECT UUID(), po.po_id, po.tenant_id,
  CONCAT('iw-wood0-mat-', LPAD(MOD(CRC32(po.po_id), 12)+1, 4, '0'), '-dddddddddddddd'),
  20 + MOD(CRC32(CONCAT(po.po_id,'q')), 80),
  20 + MOD(CRC32(CONCAT(po.po_id,'q')), 80),
  0, 0, 0, @wh, 'received'
FROM purchase_orders po WHERE po.tenant_id=@wood_tenant;

UPDATE purchase_order_items poi JOIN items i ON poi.item_id=i.item_id SET poi.unit_price = i.cost_price;
UPDATE purchase_order_items poi SET poi.supply_amount = poi.ordered_qty * poi.unit_price, poi.vat_amount = ROUND(poi.ordered_qty * poi.unit_price * 0.10);
UPDATE purchase_orders po JOIN (SELECT po_id, SUM(supply_amount) s, SUM(vat_amount) v FROM purchase_order_items GROUP BY po_id) x USING(po_id) SET po.total_amount=x.s, po.vat_amount=x.v WHERE po.tenant_id=@wood_tenant;

INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, po_id, partner_id, receipt_date, source_type, status, total_amount, vat_amount, memo, created_at)
SELECT UUID(), po.tenant_id, CONCAT('WRC', DATE_FORMAT(po.po_date,'%y%m'), LPAD(ROW_NUMBER() OVER (ORDER BY po.po_date), 4, '0')),
  po.po_id, po.partner_id, DATE_ADD(po.po_date, INTERVAL 3 DAY),
  'purchase_order', 'confirmed', po.total_amount, po.vat_amount, 'wood rc', NOW(6)
FROM purchase_orders po WHERE po.tenant_id=@wood_tenant;

INSERT INTO purchase_receipt_items (receipt_item_id, receipt_id, tenant_id, po_item_id, item_id, warehouse_id, qty, unit_price, supply_amount, vat_amount)
SELECT UUID(), pr.receipt_id, pr.tenant_id, poi.po_item_id, poi.item_id, @wh, poi.ordered_qty, poi.unit_price, poi.supply_amount, poi.vat_amount
FROM purchase_receipts pr JOIN purchase_order_items poi USING(po_id) WHERE pr.tenant_id=@wood_tenant;

INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, partner_id, ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo, created_at)
SELECT pr.tenant_id, pri.item_id, @wh, pr.partner_id,
  pr.receipt_date, DATE_FORMAT(pr.receipt_date,'%Y-%m'),
  'in', 'purchase_receipt', pri.receipt_item_id, pr.receipt_no,
  pri.qty, 0, pri.unit_price, pri.supply_amount, 'wood rc ledger', NOW(6)
FROM purchase_receipts pr JOIN purchase_receipt_items pri USING(receipt_id) WHERE pr.tenant_id=@wood_tenant;

-- 매출
INSERT INTO sales_orders (order_id, tenant_id, order_no, partner_id, employee_id, order_date, delivery_date, status, total_amount, vat_amount, memo, created_at, updated_at)
SELECT UUID(), @wood_tenant,
  CONCAT('WSO', DATE_FORMAT(m.ym_start,'%y%m'), LPAD(s.n, 3, '0')),
  CASE MOD(CRC32(CONCAT(m.ym_start, s.n, 'so')), 5)
    WHEN 0 THEN 'pw-wood0-cust-0001-dddddddddddd'
    WHEN 1 THEN 'pw-wood0-cust-0002-dddddddddddd'
    WHEN 2 THEN 'pw-wood0-cust-0003-dddddddddddd'
    WHEN 3 THEN 'pw-wood0-cust-0004-dddddddddddd'
    ELSE 'pw-wood0-cust-0005-dddddddddddd'
  END,
  'em-wood0-0003-dddddddddddddddddddd',
  DATE_ADD(m.ym_start, INTERVAL (s.n * 3) DAY),
  DATE_ADD(m.ym_start, INTERVAL (s.n * 3 + 14) DAY),
  'invoiced', 0, 0, 'wood SO (맞춤)', NOW(6), NOW(6)
FROM tmp_months m CROSS JOIN tmp_seq8 s;

INSERT INTO sales_order_items (order_item_id, order_id, tenant_id, item_id, ordered_qty, delivered_qty, unit_price, supply_amount, vat_amount, item_status)
SELECT UUID(), so.order_id, so.tenant_id,
  CONCAT('iw-wood0-fin0-', LPAD(MOD(CRC32(so.order_id), 12)+1, 4, '0'), '-ddddddddddddd'),
  1 + MOD(CRC32(CONCAT(so.order_id,'q')), 5),
  1 + MOD(CRC32(CONCAT(so.order_id,'q')), 5),
  0, 0, 0, 'delivered'
FROM sales_orders so WHERE so.tenant_id=@wood_tenant;

UPDATE sales_order_items soi
JOIN sales_orders so ON soi.order_id=so.order_id
JOIN items i ON soi.item_id=i.item_id
JOIN partners p ON so.partner_id=p.partner_id
SET soi.unit_price = i.std_price,
    soi.supply_amount = soi.ordered_qty * i.std_price,
    soi.vat_amount = CASE p.vat_handling WHEN 'standard' THEN ROUND(soi.ordered_qty * i.std_price * 0.10) ELSE 0 END
WHERE so.tenant_id=@wood_tenant;

UPDATE sales_orders so
JOIN (SELECT order_id, SUM(supply_amount) s, SUM(vat_amount) v FROM sales_order_items GROUP BY order_id) x USING(order_id)
SET so.total_amount=x.s, so.vat_amount=x.v WHERE so.tenant_id=@wood_tenant;

INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, order_id, partner_id, employee_id, delivery_date, source_type, status, total_amount, vat_amount, memo, created_at, created_by, updated_at)
SELECT UUID(), so.tenant_id,
  CONCAT('WDL', DATE_FORMAT(so.delivery_date,'%y%m'), LPAD(ROW_NUMBER() OVER (ORDER BY so.delivery_date, so.order_id), 4, '0')),
  so.order_id, so.partner_id, so.employee_id,
  so.delivery_date, 'sales_order', 'confirmed', so.total_amount, so.vat_amount,
  'wood delivery', NOW(6), so.employee_id, NOW(6)
FROM sales_orders so WHERE so.tenant_id=@wood_tenant;

INSERT INTO sales_delivery_items (delivery_item_id, delivery_id, tenant_id, order_item_id, item_id, warehouse_id, qty, unit_price, supply_amount, vat_amount)
SELECT UUID(), sd.delivery_id, sd.tenant_id, soi.order_item_id, soi.item_id, @wh,
  soi.ordered_qty, soi.unit_price, soi.supply_amount, soi.vat_amount
FROM sales_deliveries sd JOIN sales_order_items soi USING(order_id) WHERE sd.tenant_id=@wood_tenant;

-- ====== 특화 1: custom_order_specs (모든 SO가 맞춤 주문) ======
INSERT INTO custom_order_specs (spec_id, tenant_id, order_id, order_item_id, width_mm, height_mm, depth_mm, wood_type, color_code, finish_type, drawing_url, special_requirements, revision_no, status, created_at, updated_at)
SELECT UUID(), @wood_tenant, soi.order_id, soi.order_item_id,
  600 + MOD(CRC32(soi.order_id), 1200),
  800 + MOD(CRC32(CONCAT(soi.order_id,'h')), 1000),
  350 + MOD(CRC32(CONCAT(soi.order_id,'d')), 200),
  CASE MOD(CRC32(soi.order_id), 4) WHEN 0 THEN 'oak' WHEN 1 THEN 'walnut' WHEN 2 THEN 'mdf' ELSE 'birch' END,
  CASE MOD(CRC32(soi.order_id), 3) WHEN 0 THEN '#3A2E1F' WHEN 1 THEN '#8B6F47' ELSE '#F5F5DC' END,
  CASE MOD(CRC32(soi.order_id), 3) WHEN 0 THEN 'matte' WHEN 1 THEN 'satin' ELSE 'glossy' END,
  CONCAT('/drawings/', LEFT(soi.order_id, 8), '.pdf'),
  '고객 맞춤 요청사항 메모',
  1 + MOD(CRC32(soi.order_id), 3),
  'confirmed', NOW(6), NOW(6)
FROM sales_order_items soi JOIN sales_orders so ON soi.order_id=so.order_id
WHERE so.tenant_id=@wood_tenant;

-- ====== 특화 2: work_in_process (각 매출 당 3단계 재단→조립→도장) ======
INSERT INTO work_in_process (wip_id, tenant_id, order_item_id, item_id, stage, qty, started_at, completed_at, operator_employee_id, memo, created_at)
SELECT UUID(), @wood_tenant, soi.order_item_id, soi.item_id, 'cut',
  soi.ordered_qty,
  DATE_SUB(so.delivery_date, INTERVAL 10 DAY),
  DATE_SUB(so.delivery_date, INTERVAL 8 DAY),
  'em-wood0-0005-dddddddddddddddddddd', '재단 완료', NOW(6)
FROM sales_order_items soi JOIN sales_orders so USING(order_id) WHERE so.tenant_id=@wood_tenant;

INSERT INTO work_in_process (wip_id, tenant_id, order_item_id, item_id, stage, qty, started_at, completed_at, operator_employee_id, memo, created_at)
SELECT UUID(), @wood_tenant, soi.order_item_id, soi.item_id, 'assemble',
  soi.ordered_qty,
  DATE_SUB(so.delivery_date, INTERVAL 8 DAY),
  DATE_SUB(so.delivery_date, INTERVAL 4 DAY),
  'em-wood0-0006-dddddddddddddddddddd', '조립 완료', NOW(6)
FROM sales_order_items soi JOIN sales_orders so USING(order_id) WHERE so.tenant_id=@wood_tenant;

INSERT INTO work_in_process (wip_id, tenant_id, order_item_id, item_id, stage, qty, started_at, completed_at, operator_employee_id, memo, created_at)
SELECT UUID(), @wood_tenant, soi.order_item_id, soi.item_id, 'paint',
  soi.ordered_qty,
  DATE_SUB(so.delivery_date, INTERVAL 4 DAY),
  DATE_SUB(so.delivery_date, INTERVAL 1 DAY),
  'em-wood0-0007-dddddddddddddddddddd', '도장 완료', NOW(6)
FROM sales_order_items soi JOIN sales_orders so USING(order_id) WHERE so.tenant_id=@wood_tenant;

-- production 원장 + 출고 원장
INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo, created_at)
SELECT @wood_tenant, sdi.item_id, @wh,
  DATE_SUB(sd.delivery_date, INTERVAL 1 DAY),
  DATE_FORMAT(DATE_SUB(sd.delivery_date, INTERVAL 1 DAY), '%Y-%m'),
  'in', 'production', UUID(), CONCAT('PRD-', sd.delivery_no),
  sdi.qty, 0, (SELECT cost_price FROM items WHERE item_id=sdi.item_id),
  sdi.qty * COALESCE((SELECT cost_price FROM items WHERE item_id=sdi.item_id), 0),
  'wood production', NOW(6)
FROM sales_deliveries sd JOIN sales_delivery_items sdi USING(delivery_id) WHERE sd.tenant_id=@wood_tenant;

INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, partner_id, ledger_date, ym, move_type, source_type, source_id, doc_no, qty_in, qty_out, unit_cost, supply_amount, memo, created_at)
SELECT sd.tenant_id, sdi.item_id, @wh, sd.partner_id,
  sd.delivery_date, DATE_FORMAT(sd.delivery_date,'%Y-%m'),
  'out', 'sales_delivery', sdi.delivery_item_id, sd.delivery_no,
  0, sdi.qty, sdi.unit_price, sdi.supply_amount, 'wood out', NOW(6)
FROM sales_deliveries sd JOIN sales_delivery_items sdi USING(delivery_id) WHERE sd.tenant_id=@wood_tenant;

UPDATE item_stock s
JOIN (SELECT tenant_id, item_id, warehouse_id, SUM(qty_in)-SUM(qty_out) net FROM stock_ledger WHERE tenant_id=@wood_tenant GROUP BY tenant_id, item_id, warehouse_id) c USING(tenant_id, item_id, warehouse_id)
SET s.current_qty = c.net;

-- 집계
INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, collection_method, ref_doc_type, memo, created_at, updated_at)
SELECT UUID(), @wood_tenant, sd.partner_id, DATE_ADD(LAST_DAY(sd.delivery_date), INTERVAL 15 DAY),
  ROUND((sd.total_amount+sd.vat_amount) * 0.70, 0), 'bank', 'sales_delivery', 'wood coll', NOW(6), NOW(6)
FROM sales_deliveries sd WHERE sd.tenant_id=@wood_tenant AND sd.is_deleted=0;

INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, payment_method, memo, created_at, updated_at)
SELECT UUID(), @wood_tenant, pr.partner_id, 'general',
  ROUND((pr.total_amount+pr.vat_amount) * 0.80, 0), DATE_ADD(LAST_DAY(pr.receipt_date), INTERVAL 10 DAY),
  'bank', 'wood pmt', NOW(6), NOW(6)
FROM purchase_receipts pr WHERE pr.tenant_id=@wood_tenant;

INSERT INTO partner_balance (balance_id, tenant_id, partner_id, total_sales, total_receipt, total_purchase, total_payment, last_updated_at)
SELECT UUID(), @wood_tenant, p.partner_id,
  COALESCE(s.v,0), COALESCE(c.v,0), COALESCE(pu.v,0), COALESCE(pm.v,0), NOW(6)
FROM partners p
LEFT JOIN (SELECT partner_id, SUM(total_amount+vat_amount) v FROM sales_deliveries WHERE tenant_id=@wood_tenant AND is_deleted=0 GROUP BY partner_id) s ON p.partner_id=s.partner_id
LEFT JOIN (SELECT partner_id, SUM(amount) v FROM collections WHERE tenant_id=@wood_tenant GROUP BY partner_id) c ON p.partner_id=c.partner_id
LEFT JOIN (SELECT partner_id, SUM(total_amount+vat_amount) v FROM purchase_receipts WHERE tenant_id=@wood_tenant GROUP BY partner_id) pu ON p.partner_id=pu.partner_id
LEFT JOIN (SELECT partner_id, SUM(amount) v FROM payments WHERE tenant_id=@wood_tenant GROUP BY partner_id) pm ON p.partner_id=pm.partner_id
WHERE p.tenant_id=@wood_tenant;

INSERT INTO monthly_closing (closing_id, tenant_id, `year_month`, status, sales_amount, purchase_amount, receipt_amount, payment_amount, memo)
SELECT UUID(), @wood_tenant, months.ym,
  CASE WHEN months.ym < DATE_FORMAT(CURDATE() - INTERVAL 2 MONTH, '%Y%m') THEN 'closed' ELSE 'open' END,
  COALESCE(s.v,0), COALESCE(p.v,0), COALESCE(c.v,0), COALESCE(pmt.v,0), 'wood monthly'
FROM (SELECT DATE_FORMAT(DATE('2021-01-01') + INTERVAL n MONTH, '%Y%m') ym
  FROM (SELECT a.N + b.N*10 n FROM (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) a, (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5 UNION SELECT 6) b) x WHERE n < 67) months
LEFT JOIN (SELECT DATE_FORMAT(delivery_date,'%Y%m') ym, SUM(total_amount+vat_amount) v FROM sales_deliveries WHERE tenant_id=@wood_tenant AND is_deleted=0 GROUP BY ym) s USING(ym)
LEFT JOIN (SELECT DATE_FORMAT(receipt_date,'%Y%m') ym, SUM(total_amount+vat_amount) v FROM purchase_receipts WHERE tenant_id=@wood_tenant GROUP BY ym) p USING(ym)
LEFT JOIN (SELECT DATE_FORMAT(collection_date,'%Y%m') ym, SUM(amount) v FROM collections WHERE tenant_id=@wood_tenant GROUP BY ym) c USING(ym)
LEFT JOIN (SELECT DATE_FORMAT(payment_date,'%Y%m') ym, SUM(amount) v FROM payments WHERE tenant_id=@wood_tenant GROUP BY ym) pmt USING(ym);

DROP TEMPORARY TABLE tmp_months;
DROP TEMPORARY TABLE tmp_seq4;
DROP TEMPORARY TABLE tmp_seq8;

SELECT
  (SELECT COUNT(*) FROM purchase_orders WHERE tenant_id=@wood_tenant) po,
  (SELECT COUNT(*) FROM sales_orders WHERE tenant_id=@wood_tenant) so,
  (SELECT COUNT(*) FROM sales_deliveries WHERE tenant_id=@wood_tenant) sd,
  (SELECT COUNT(*) FROM stock_ledger WHERE tenant_id=@wood_tenant) ledger,
  (SELECT COUNT(*) FROM custom_order_specs WHERE tenant_id=@wood_tenant) specs,
  (SELECT COUNT(*) FROM work_in_process WHERE tenant_id=@wood_tenant) wip,
  (SELECT COUNT(*) FROM partner_balance WHERE tenant_id=@wood_tenant) balance,
  (SELECT COUNT(*) FROM monthly_closing WHERE tenant_id=@wood_tenant) closing;
