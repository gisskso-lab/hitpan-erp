SET @@session.block_encryption_mode = 'aes-256-cbc';
SET @key = _binary 'hitpan-aes-key-32bytes-exactly!!';
SET @plastic_tenant = 'tenant-plastic-c0-cccc-cccccccccccc';

-- tenants
INSERT INTO tenants (tenant_id, tenant_code, company_name, biz_no, biz_no_hash, ceo_name, tel, email, address, max_users, status, db_host, db_name, license_key_hash, reseller_tier, biz_type, biz_item, tax_type, fiscal_month, created_at, updated_at)
VALUES (@plastic_tenant, 'PLAS001', '(주)안산몰드',
  TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1348712345', @key, @i))), SHA2('1348712345',256),
  '임대표',
  TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-491-1234', @key, @i))),
  TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('ceo@ansan-mold.co.kr', @key, @i))),
  '경기도 안산시 반월공단', 12, 'active', 'localhost', 'hitpan_erp',
  SHA2('plastic-license', 256), 1, '제조업', '플라스틱 사출', 'taxable', 12, NOW(6), NOW(6));

INSERT INTO tenant_settings (tenant_id, allow_force_price_input, allow_force_vat_input, allow_zero_price, allow_past_edit, allow_force_stock_adjust, allow_credit_override, price_deviation_limit, force_edit_require_password, stock_eval_method, use_multi_warehouse, stock_shortage_alert, allow_minus_stock, price_input_type, auto_vat_adjust, vat_round_type, price_a_rate, price_b_rate, price_c_rate, price_d_rate, price_e_rate, use_credit_limit, credit_limit_amount, show_purchase_price, use_sales_by_employee, use_personal_info_protect, industry_type)
VALUES (@plastic_tenant, 1, 0, 0, 0, 1, 0, 25, 1, 'moving_avg', 1, 1, 0, 'net', 1, 'round', 1.00, 1.07, 1.14, 1.22, 1.38, 1, 80000000, 0, 1, 1, 'plastic');

INSERT INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, location, is_active, created_at, updated_at) VALUES
  ('wh-plas0-main-0000-cccccccccccccccc', @plastic_tenant, 'MAIN', '본사창고', 'normal', '반월공단', 1, NOW(6), NOW(6)),
  ('wh-plas0-mold-0000-cccccccccccccccc', @plastic_tenant, 'MOLD', '금형창고', 'normal', '반월공단 B동', 1, NOW(6), NOW(6));

INSERT INTO accounts (account_code, tenant_id, account_name, account_type, sort_order, is_active, created_at)
SELECT account_code, @plastic_tenant, account_name, account_type, sort_order, is_active, NOW(6)
FROM accounts WHERE tenant_id = '452ca266-97b9-4cd1-a0ac-2f37830c81f6';

-- ===== 거래처 14곳 =====
-- 고객 8 + 공급사 (펠릿/마스터배치) 4 + 외주 (표면처리) 2
INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, biz_no, biz_no_hash, ceo_name, biz_type, biz_item, tel, address, credit_limit, payment_terms, bank_name, bank_account, account_holder, is_active, created_at, updated_at, price_grade, tax_type, vat_handling, price_display_preference) VALUES
('pp-plas0-cust-0001-cccccccccccc', @plastic_tenant, 'PC001', '현대모비스 부품', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1218723456', @key, @i))), SHA2('1218723456',256),
 '박현대', '제조업', '자동차부품',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-280-4000', @key, @i))),
 '경기도 용인시', 300000000, 60, '국민은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-301-000001', @key, @i))),
 '현대모비스', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inherit'),
('pp-plas0-cust-0002-cccccccccccc', @plastic_tenant, 'PC002', 'LG전자 소물', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2108723456', @key, @i))), SHA2('2108723456',256),
 '김엘지', '제조업', '가전 소물',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('02-3777-1000', @key, @i))),
 '서울시 영등포구', 250000000, 60, '우리은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('101-302-000002', @key, @i))),
 'LG전자', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inclusive'),
('pp-plas0-cust-0003-cccccccccccc', @plastic_tenant, 'PC003', '코웨이 부품', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1308745678', @key, @i))), SHA2('1308745678',256),
 '정코웨', '제조업', '정수기부품',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1588-5200', @key, @i))),
 '서울시 중구', 180000000, 60, '신한은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('110-303-000003', @key, @i))),
 '코웨이', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inherit'),
('pp-plas0-cust-0004-cccccccccccc', @plastic_tenant, 'PC004', '삼양사 식품포장', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1108712389', @key, @i))), SHA2('1108712389',256),
 '이삼양', '제조업', '식품포장재',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('02-740-5000', @key, @i))),
 '서울시 종로구', 120000000, 30, '기업은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('312-304-000004', @key, @i))),
 '삼양사', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inclusive'),
('pp-plas0-cust-0005-cccccccccccc', @plastic_tenant, 'PC005', '시흥 자동차부품', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2258712390', @key, @i))), SHA2('2258712390',256),
 '조시흥', '제조업', '자동차 내장',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-491-5000', @key, @i))),
 '경기도 시흥시', 80000000, 60, '하나은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('310-305-000005', @key, @i))),
 '시흥자동차', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit'),
('pp-plas0-cust-0006-cccccccccccc', @plastic_tenant, 'PC006', '완구제조사A', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2368745601', @key, @i))), SHA2('2368745601',256),
 '강완구', '제조업', '완구제조',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('032-451-1000', @key, @i))),
 '인천시 남동구', 50000000, 30, '농협',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('351-306-000006', @key, @i))),
 '완구제조A', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit'),
('pp-plas0-cust-0007-cccccccccccc', @plastic_tenant, 'PC007', '포장용기업체', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1278745612', @key, @i))), SHA2('1278745612',256),
 '윤포장', '제조업', '포장용기',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-362-2000', @key, @i))),
 '경기도 안산시', 40000000, 90, '국민은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-307-000007', @key, @i))),
 '포장용기', 1, NOW(6), NOW(6), 'C', 'taxable', 'zero', 'inherit'),
('pp-plas0-cust-0008-cccccccccccc', @plastic_tenant, 'PC008', '수출사출', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2318745623', @key, @i))), SHA2('2318745623',256),
 '임수출', '제조업', '수출',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-493-3000', @key, @i))),
 '경기도 시흥시', 30000000, 30, '우리은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('101-308-000008', @key, @i))),
 '수출사출', 1, NOW(6), NOW(6), 'C', 'exempt', 'exempt', 'inherit');

INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, biz_no, biz_no_hash, ceo_name, biz_type, biz_item, tel, address, credit_limit, payment_terms, bank_name, bank_account, account_holder, is_active, created_at, updated_at, price_grade, tax_type, vat_handling, price_display_preference) VALUES
('pp-plas0-supp-0001-cccccccccccc', @plastic_tenant, 'PS001', 'LG화학 펠릿', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1078745634', @key, @i))), SHA2('1078745634',256),
 '박엘지', '제조업', '플라스틱 원료',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('02-3773-1000', @key, @i))),
 '서울시 영등포구', 0, 60, '신한은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('110-409-000009', @key, @i))),
 'LG화학', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inherit'),
('pp-plas0-supp-0002-cccccccccccc', @plastic_tenant, 'PS002', '롯데케미칼', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1098745645', @key, @i))), SHA2('1098745645',256),
 '강롯데', '제조업', '원료',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('02-829-4000', @key, @i))),
 '서울시 송파구', 0, 60, '기업은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('312-410-000010', @key, @i))),
 '롯데케미칼', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inclusive'),
('pp-plas0-supp-0003-cccccccccccc', @plastic_tenant, 'PS003', '마스터배치코리아', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1348745656', @key, @i))), SHA2('1348745656',256),
 '조마스', '제조업', '색상원료',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-491-5000', @key, @i))),
 '경기도 시흥시', 0, 30, '하나은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('310-411-000011', @key, @i))),
 '마스터배치', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit'),
('pp-plas0-supp-0004-cccccccccccc', @plastic_tenant, 'PS004', '정우몰드제작', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2148745667', @key, @i))), SHA2('2148745667',256),
 '윤정우', '제조업', '금형제작',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-362-6000', @key, @i))),
 '경기도 안산시', 0, 30, '농협',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('351-412-000012', @key, @i))),
 '정우몰드', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit'),
('pp-plas0-out0-0001-cccccccccccc', @plastic_tenant, 'PO001', '표면처리외주', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2168745678', @key, @i))), SHA2('2168745678',256),
 '임표면', '제조업', '도장·도금',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-495-7000', @key, @i))),
 '경기도 화성시', 0, 30, '국민은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-413-000013', @key, @i))),
 '표면처리외주', 1, NOW(6), NOW(6), 'C', 'taxable', 'standard', 'inclusive'),
('pp-plas0-out0-0002-cccccccccccc', @plastic_tenant, 'PO002', '인쇄외주', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2258745689', @key, @i))), SHA2('2258745689',256),
 '최인쇄', '제조업', '실크인쇄',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-493-8000', @key, @i))),
 '경기도 시흥시', 0, 60, '우리은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('101-414-000014', @key, @i))),
 '인쇄외주', 1, NOW(6), NOW(6), 'C', 'taxable', 'standard', 'inherit');

SELECT
  (SELECT COUNT(*) FROM tenants WHERE tenant_id=@plastic_tenant) t,
  (SELECT COUNT(*) FROM warehouses WHERE tenant_id=@plastic_tenant) wh,
  (SELECT COUNT(*) FROM accounts WHERE tenant_id=@plastic_tenant) acc,
  (SELECT COUNT(*) FROM partners WHERE tenant_id=@plastic_tenant) partners_cnt;
