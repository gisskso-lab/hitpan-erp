SET @@session.block_encryption_mode = 'aes-256-cbc';
SET @key = _binary 'hitpan-aes-key-32bytes-exactly!!';
SET @elec_tenant = 'tenant-elec0-b000-bbbb-bbbbbbbbbbbb';

-- tenants
INSERT INTO tenants (tenant_id, tenant_code, company_name, biz_no, biz_no_hash, ceo_name, tel, email, address, max_users, status, db_host, db_name, license_key_hash, reseller_tier, biz_type, biz_item, tax_type, fiscal_month, created_at, updated_at)
VALUES (@elec_tenant, 'ELEC001', '(주)동탄전자',
  TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1348645678', @key, @i))), SHA2('1348645678',256),
  '정대표',
  TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-374-5678', @key, @i))),
  TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('ceo@dongtan-elec.co.kr', @key, @i))),
  '경기도 화성시 동탄산업단지', 15, 'active', 'localhost', 'hitpan_erp',
  SHA2('elec-license-key', 256), 1, '제조업', '전자부품 조립', 'taxable', 12, NOW(6), NOW(6));

-- tenant_settings
INSERT INTO tenant_settings (tenant_id, allow_force_price_input, allow_force_vat_input, allow_zero_price, allow_past_edit, allow_force_stock_adjust, allow_credit_override, price_deviation_limit, force_edit_require_password, stock_eval_method, use_multi_warehouse, stock_shortage_alert, allow_minus_stock, price_input_type, auto_vat_adjust, vat_round_type, price_a_rate, price_b_rate, price_c_rate, price_d_rate, price_e_rate, use_credit_limit, credit_limit_amount, show_purchase_price, use_sales_by_employee, use_personal_info_protect, industry_type)
VALUES (@elec_tenant, 1, 0, 0, 0, 1, 0, 20, 1, 'fifo', 1, 1, 0, 'net', 1, 'round', 1.00, 1.05, 1.12, 1.20, 1.35, 1, 100000000, 0, 1, 1, 'elec');

-- 창고 3 (본사·부자재·외주)
INSERT INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, location, is_active, created_at, updated_at) VALUES
  ('wh-elec0-main-0000-bbbbbbbbbbbbbbbb', @elec_tenant, 'MAIN', '본사창고', 'normal', '동탄 본사', 1, NOW(6), NOW(6)),
  ('wh-elec0-smd0-0000-bbbbbbbbbbbbbbbb', @elec_tenant, 'SMD', '부자재창고', 'normal', '화성 SMD실', 1, NOW(6), NOW(6)),
  ('wh-elec0-out0-0000-bbbbbbbbbbbbbbbb', @elec_tenant, 'OUT', '외주창고', 'normal', '평택 외주처', 1, NOW(6), NOW(6));

-- 계정과목 21 복제
INSERT INTO accounts (account_code, tenant_id, account_name, account_type, sort_order, is_active, created_at)
SELECT account_code, @elec_tenant, account_name, account_type, sort_order, is_active, NOW(6)
FROM accounts WHERE tenant_id = '452ca266-97b9-4cd1-a0ac-2f37830c81f6';

-- ===== 거래처 18곳 (납품처 8 + 공급사 7 + 외주 3) =====
-- 납품처: LG/삼성 협력사·PCB 업체
INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, biz_no, biz_no_hash, ceo_name, biz_type, biz_item, tel, address, credit_limit, payment_terms, bank_name, bank_account, account_holder, is_active, created_at, updated_at, price_grade, tax_type, vat_handling, price_display_preference) VALUES
('pe-elec0-cust-0001-bbbbbbbbbbbb', @elec_tenant, 'EC001', 'LG이노텍', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1068612345', @key, @i))), SHA2('1068612345',256),
 '박이노', '제조업', '전자부품',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-436-7000', @key, @i))),
 '경기도 평택시', 500000000, 60, '국민은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-501-234567', @key, @i))),
 'LG이노텍', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inherit'),
('pe-elec0-cust-0002-bbbbbbbbbbbb', @elec_tenant, 'EC002', '삼성전기', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1258601234', @key, @i))), SHA2('1258601234',256),
 '이삼전', '제조업', '전자부품',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-210-5000', @key, @i))),
 '경기도 수원시', 400000000, 60, '우리은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('101-602-345678', @key, @i))),
 '삼성전기', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inherit'),
('pe-elec0-cust-0003-bbbbbbbbbbbb', @elec_tenant, 'EC003', 'SK하이닉스 협력사', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1348612345', @key, @i))), SHA2('1348612345',256),
 '정하이', '제조업', '반도체 하청',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-630-2000', @key, @i))),
 '경기도 이천시', 300000000, 90, '신한은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('110-703-456789', @key, @i))),
 'SK하이닉스 협력사', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inclusive'),
('pe-elec0-cust-0004-bbbbbbbbbbbb', @elec_tenant, 'EC004', '대덕전자', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1088612345', @key, @i))), SHA2('1088612345',256),
 '김대덕', '제조업', 'PCB',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('032-820-3000', @key, @i))),
 '인천시 서구', 200000000, 60, '기업은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('312-804-567890', @key, @i))),
 '대덕전자', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit'),
('pe-elec0-cust-0005-bbbbbbbbbbbb', @elec_tenant, 'EC005', '파트론', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2148612345', @key, @i))), SHA2('2148612345',256),
 '조파트', '제조업', '모바일부품',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-495-4000', @key, @i))),
 '경기도 화성시', 150000000, 60, '하나은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('310-905-678901', @key, @i))),
 '파트론', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inclusive'),
('pe-elec0-cust-0006-bbbbbbbbbbbb', @elec_tenant, 'EC006', '가온전자', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1398612345', @key, @i))), SHA2('1398612345',256),
 '윤가온', '제조업', '센서모듈',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-362-5000', @key, @i))),
 '경기도 안산시', 80000000, 30, '농협',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('351-006-789012', @key, @i))),
 '가온전자', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit'),
('pe-elec0-cust-0007-bbbbbbbbbbbb', @elec_tenant, 'EC007', '중소 OEM 하청A', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2258634578', @key, @i))), SHA2('2258634578',256),
 '강오엠', '제조업', 'OEM조립',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-493-6000', @key, @i))),
 '경기도 시흥시', 50000000, 30, '국민은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-107-890123', @key, @i))),
 '중소OEM-A', 1, NOW(6), NOW(6), 'C', 'taxable', 'zero', 'inherit'),
('pe-elec0-cust-0008-bbbbbbbbbbbb', @elec_tenant, 'EC008', '수출업체B', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('3108634521', @key, @i))), SHA2('3108634521',256),
 '임수출', '제조업', '수출',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('02-782-7000', @key, @i))),
 '서울시 서초구', 30000000, 30, '우리은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('101-208-901234', @key, @i))),
 '수출업체-B', 1, NOW(6), NOW(6), 'C', 'exempt', 'exempt', 'inherit');

-- 공급사 7
INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, biz_no, biz_no_hash, ceo_name, biz_type, biz_item, tel, address, credit_limit, payment_terms, bank_name, bank_account, account_holder, is_active, created_at, updated_at, price_grade, tax_type, vat_handling, price_display_preference) VALUES
('pe-elec0-supp-0001-bbbbbbbbbbbb', @elec_tenant, 'ES001', '삼성반도체', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1248601234', @key, @i))), SHA2('1248601234',256),
 '이반도', '제조업', '반도체 칩',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-209-8000', @key, @i))),
 '경기도 용인시', 0, 60, '신한은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('110-309-012345', @key, @i))),
 '삼성반도체', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inherit'),
('pe-elec0-supp-0002-bbbbbbbbbbbb', @elec_tenant, 'ES002', 'TXN 한국', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('5108634578', @key, @i))), SHA2('5108634578',256),
 '박티엑스', '제조업', 'IC칩',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('02-345-9000', @key, @i))),
 '서울시 강남구', 0, 30, '기업은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('312-410-123456', @key, @i))),
 'TXN-한국', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inclusive'),
('pe-elec0-supp-0003-bbbbbbbbbbbb', @elec_tenant, 'ES003', '로옴 세미', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1398612331', @key, @i))), SHA2('1398612331',256),
 '최로옴', '제조업', '반도체',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-271-1001', @key, @i))),
 '경기도 성남시', 0, 60, '하나은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('310-511-234567', @key, @i))),
 '로옴세미', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inherit'),
('pe-elec0-supp-0004-bbbbbbbbbbbb', @elec_tenant, 'ES004', '와이솔PCB', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2148634589', @key, @i))), SHA2('2148634589',256),
 '조와이', '제조업', 'PCB 원판',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-436-1002', @key, @i))),
 '경기도 평택시', 0, 30, '농협',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('351-612-345678', @key, @i))),
 '와이솔PCB', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit'),
('pe-elec0-supp-0005-bbbbbbbbbbbb', @elec_tenant, 'ES005', '하네스코리아', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2368645612', @key, @i))), SHA2('2368645612',256),
 '윤하네', '제조업', '하네스·커넥터',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-374-1003', @key, @i))),
 '경기도 화성시', 0, 60, '국민은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-713-456789', @key, @i))),
 '하네스코리아', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inclusive'),
('pe-elec0-supp-0006-bbbbbbbbbbbb', @elec_tenant, 'ES006', '저항기업', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1258645623', @key, @i))), SHA2('1258645623',256),
 '강저항', '제조업', '수동소자',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('032-521-1004', @key, @i))),
 '인천시 남동구', 0, 30, '우리은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('101-814-567890', @key, @i))),
 '저항기업', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit'),
('pe-elec0-supp-0007-bbbbbbbbbbbb', @elec_tenant, 'ES007', '케이스제조', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1398645634', @key, @i))), SHA2('1398645634',256),
 '임케이스', '제조업', '플라스틱 케이스',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-493-1005', @key, @i))),
 '경기도 시흥시', 0, 30, '신한은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('110-915-678901', @key, @i))),
 '케이스제조', 1, NOW(6), NOW(6), 'C', 'taxable', 'standard', 'inherit');

-- 외주 3 (SMT 외주·조립 외주·검사 외주)
INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, biz_no, biz_no_hash, ceo_name, biz_type, biz_item, tel, address, credit_limit, payment_terms, bank_name, bank_account, account_holder, is_active, created_at, updated_at, price_grade, tax_type, vat_handling, price_display_preference) VALUES
('pe-elec0-out0-0001-bbbbbbbbbbbb', @elec_tenant, 'EO001', 'SMT외주', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2168645645', @key, @i))), SHA2('2168645645',256),
 '정외주', '제조업', 'SMT 실장',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-436-1006', @key, @i))),
 '경기도 평택시', 0, 30, '기업은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('312-016-789012', @key, @i))),
 'SMT외주', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit'),
('pe-elec0-out0-0002-bbbbbbbbbbbb', @elec_tenant, 'EO002', '조립외주', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2268645656', @key, @i))), SHA2('2268645656',256),
 '박조립', '제조업', '최종조립',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-495-1007', @key, @i))),
 '경기도 화성시', 0, 60, '하나은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('310-117-890123', @key, @i))),
 '조립외주', 1, NOW(6), NOW(6), 'C', 'taxable', 'standard', 'inherit'),
('pe-elec0-out0-0003-bbbbbbbbbbbb', @elec_tenant, 'EO003', '검사외주', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2368645667', @key, @i))), SHA2('2368645667',256),
 '이검사', '제조업', 'ICT·기능검사',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-362-1008', @key, @i))),
 '경기도 안산시', 0, 30, '농협',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('351-218-901234', @key, @i))),
 '검사외주', 1, NOW(6), NOW(6), 'C', 'taxable', 'standard', 'inclusive');

SELECT
  (SELECT COUNT(*) FROM tenants WHERE tenant_id=@elec_tenant) tenant,
  (SELECT COUNT(*) FROM warehouses WHERE tenant_id=@elec_tenant) wh,
  (SELECT COUNT(*) FROM accounts WHERE tenant_id=@elec_tenant) acc,
  (SELECT COUNT(*) FROM partners WHERE tenant_id=@elec_tenant) partners_cnt,
  (SELECT COUNT(*) FROM partners WHERE tenant_id=@elec_tenant AND partner_type='customer') customers,
  (SELECT COUNT(*) FROM partners WHERE tenant_id=@elec_tenant AND partner_type='supplier') suppliers;
