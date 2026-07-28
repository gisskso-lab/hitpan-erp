SET @@session.block_encryption_mode = 'aes-256-cbc';
SET @key = _binary 'hitpan-aes-key-32bytes-exactly!!';
SET @plastic_tenant = 'tenant-plastic-c0-cccc-cccccccccccc';

-- 직원 10명
INSERT INTO employees (employee_id, tenant_id, emp_no, emp_name, position, job_title, emp_type, join_date, phone, email, bank_name, bank_account, is_active, created_at, updated_at, role) VALUES
('em-plas0-0001-cccccccccccccccccccc', @plastic_tenant, 'E001', '임대표', 'ceo', '대표이사', 'regular', '2018-03-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-3001-0001', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('ceo@ansan-mold.co.kr', @key, @i))),
 '국민은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-200-000001', @key, @i))),
 1, NOW(6), NOW(6), 'tenant_admin'),
('em-plas0-0002-cccccccccccccccccccc', @plastic_tenant, 'E002', '강부장', 'manager', '금형관리자', 'regular', '2018-05-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-3002-0002', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('kang@ansan-mold.co.kr', @key, @i))),
 '우리은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('101-201-000002', @key, @i))),
 1, NOW(6), NOW(6), 'production_manager'),
('em-plas0-0003-cccccccccccccccccccc', @plastic_tenant, 'E003', '신여사', 'clerk', '경리부장', 'regular', '2019-01-15',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-3003-0003', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('shin@ansan-mold.co.kr', @key, @i))),
 '신한은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('110-202-000003', @key, @i))),
 1, NOW(6), NOW(6), 'accountant'),
('em-plas0-0004-cccccccccccccccccccc', @plastic_tenant, 'E004', '박영업', 'manager', '영업팀장', 'regular', '2019-06-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-3004-0004', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('park@ansan-mold.co.kr', @key, @i))),
 '기업은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('312-203-000004', @key, @i))),
 1, NOW(6), NOW(6), 'sales_user'),
('em-plas0-0005-cccccccccccccccccccc', @plastic_tenant, 'E005', '조대리', 'staff', '영업대리', 'regular', '2022-03-15',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-3005-0005', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('cho@ansan-mold.co.kr', @key, @i))),
 '하나은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('310-204-000005', @key, @i))),
 1, NOW(6), NOW(6), 'sales_user'),
('em-plas0-0006-cccccccccccccccccccc', @plastic_tenant, 'E006', '한기사', 'staff', '사출기사', 'regular', '2019-08-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-3006-0006', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('han@ansan-mold.co.kr', @key, @i))),
 '농협', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('351-205-000006', @key, @i))),
 1, NOW(6), NOW(6), 'production_user'),
('em-plas0-0007-cccccccccccccccccccc', @plastic_tenant, 'E007', '서기사', 'staff', '사출기사', 'regular', '2020-09-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-3007-0007', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('seo@ansan-mold.co.kr', @key, @i))),
 '국민은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-206-000007', @key, @i))),
 1, NOW(6), NOW(6), 'production_user'),
('em-plas0-0008-cccccccccccccccccccc', @plastic_tenant, 'E008', '유기사', 'staff', '금형정비', 'regular', '2020-11-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-3008-0008', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('yoo@ansan-mold.co.kr', @key, @i))),
 '우리은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('101-207-000008', @key, @i))),
 1, NOW(6), NOW(6), 'production_user'),
('em-plas0-0009-cccccccccccccccccccc', @plastic_tenant, 'E009', '권반장', 'staff', '생산반장', 'regular', '2021-05-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-3009-0009', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('kwon@ansan-mold.co.kr', @key, @i))),
 '신한은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('110-208-000009', @key, @i))),
 1, NOW(6), NOW(6), 'production_user'),
('em-plas0-0010-cccccccccccccccccccc', @plastic_tenant, 'E010', '고사원', 'staff', '자재관리', 'regular', '2023-06-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-3010-0010', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('koh@ansan-mold.co.kr', @key, @i))),
 '기업은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('312-209-000010', @key, @i))),
 1, NOW(6), NOW(6), 'warehouse_user');

-- 품목 40종 (원자재 15·반제품 10·완제품 15)
INSERT INTO items (item_id, tenant_id, item_code, item_name, item_type, unit, std_price, price_a, price_b, price_c, price_d, price_e, cost_price, tax_type, safe_stock, is_active, created_at, updated_at, item_group, spec, purchase_price, sale_price, standard_price, safety_stock) VALUES
-- 원자재 15: 펠릿·마스터배치·부자재
('ip-plas0-mat-0001-cccccccccccccc', @plastic_tenant, 'PM001', 'ABS 펠릿 내장재용', 'material', 'KG', 2800, 2800, 2996, 3192, 3416, 3864, 2464, 'taxable', 2000, 1, NOW(6), NOW(6), '펠릿', 'ABS 일반', 2464, 2800, 2800, 2000),
('ip-plas0-mat-0002-cccccccccccccc', @plastic_tenant, 'PM002', 'ABS 펠릿 고내열', 'material', 'KG', 3200, 3200, 3424, 3648, 3904, 4416, 2816, 'taxable', 1500, 1, NOW(6), NOW(6), '펠릿', 'ABS 고내열 120도', 2816, 3200, 3200, 1500),
('ip-plas0-mat-0003-cccccccccccccc', @plastic_tenant, 'PM003', 'PC 펠릿 투명', 'material', 'KG', 4500, 4500, 4815, 5130, 5490, 6210, 3960, 'taxable', 1000, 1, NOW(6), NOW(6), '펠릿', 'PC 투명 고광택', 3960, 4500, 4500, 1000),
('ip-plas0-mat-0004-cccccccccccccc', @plastic_tenant, 'PM004', 'PP 펠릿 일반', 'material', 'KG', 1800, 1800, 1926, 2052, 2196, 2484, 1584, 'taxable', 2500, 1, NOW(6), NOW(6), '펠릿', 'PP 홈용품', 1584, 1800, 1800, 2500),
('ip-plas0-mat-0005-cccccccccccccc', @plastic_tenant, 'PM005', 'PE 펠릿 고밀도', 'material', 'KG', 1600, 1600, 1712, 1824, 1952, 2208, 1408, 'taxable', 2000, 1, NOW(6), NOW(6), '펠릿', 'HDPE', 1408, 1600, 1600, 2000),
('ip-plas0-mat-0006-cccccccccccccc', @plastic_tenant, 'PM006', 'PET 펠릿 투명', 'material', 'KG', 2200, 2200, 2354, 2508, 2684, 3036, 1936, 'taxable', 1500, 1, NOW(6), NOW(6), '펠릿', '식품등급', 1936, 2200, 2200, 1500),
('ip-plas0-mat-0007-cccccccccccccc', @plastic_tenant, 'PM007', '나일론 PA6 펠릿', 'material', 'KG', 3800, 3800, 4066, 4332, 4636, 5244, 3344, 'taxable', 800, 1, NOW(6), NOW(6), '펠릿', '엔지니어링', 3344, 3800, 3800, 800),
('ip-plas0-mat-0008-cccccccccccccc', @plastic_tenant, 'PM008', 'POM 펠릿 델린', 'material', 'KG', 5500, 5500, 5885, 6270, 6710, 7590, 4840, 'taxable', 500, 1, NOW(6), NOW(6), '펠릿', '기계부품용', 4840, 5500, 5500, 500),
('ip-plas0-mat-0009-cccccccccccccc', @plastic_tenant, 'PM009', '마스터배치 검정', 'material', 'KG', 6500, 6500, 6955, 7410, 7930, 8970, 5720, 'taxable', 200, 1, NOW(6), NOW(6), '색상', '카본블랙 40%', 5720, 6500, 6500, 200),
('ip-plas0-mat-0010-cccccccccccccc', @plastic_tenant, 'PM010', '마스터배치 흰색', 'material', 'KG', 7000, 7000, 7490, 7980, 8540, 9660, 6160, 'taxable', 200, 1, NOW(6), NOW(6), '색상', '이산화티타늄', 6160, 7000, 7000, 200),
('ip-plas0-mat-0011-cccccccccccccc', @plastic_tenant, 'PM011', '마스터배치 빨강', 'material', 'KG', 8500, 8500, 9095, 9690, 10370, 11730, 7480, 'taxable', 100, 1, NOW(6), NOW(6), '색상', '적색 안료', 7480, 8500, 8500, 100),
('ip-plas0-mat-0012-cccccccccccccc', @plastic_tenant, 'PM012', '마스터배치 파랑', 'material', 'KG', 8500, 8500, 9095, 9690, 10370, 11730, 7480, 'taxable', 100, 1, NOW(6), NOW(6), '색상', '청색 안료', 7480, 8500, 8500, 100),
('ip-plas0-mat-0013-cccccccccccccc', @plastic_tenant, 'PM013', '첨가제 UV안정제', 'material', 'KG', 12000, 12000, 12840, 13680, 14640, 16560, 10560, 'taxable', 50, 1, NOW(6), NOW(6), '첨가제', 'UV 안정제', 10560, 12000, 12000, 50),
('ip-plas0-mat-0014-cccccccccccccc', @plastic_tenant, 'PM014', '포장 PE필름 1m', 'material', 'M', 800, 800, 856, 912, 976, 1104, 704, 'taxable', 5000, 1, NOW(6), NOW(6), '포장재', '포장필름', 704, 800, 800, 5000),
('ip-plas0-mat-0015-cccccccccccccc', @plastic_tenant, 'PM015', '골판지 박스 중', 'material', 'EA', 1200, 1200, 1284, 1368, 1464, 1656, 1056, 'taxable', 2000, 1, NOW(6), NOW(6), '포장재', '500x400x300', 1056, 1200, 1200, 2000);

-- 반제품 10
INSERT INTO items (item_id, tenant_id, item_code, item_name, item_type, unit, std_price, price_a, price_b, price_c, price_d, price_e, cost_price, tax_type, safe_stock, is_active, created_at, updated_at, item_group, spec, purchase_price, sale_price, standard_price, safety_stock) VALUES
('ip-plas0-semi-0001-ccccccccccccc', @plastic_tenant, 'PSP001', '자동차 부품 A 반제품', 'assembly', 'EA', 3500, 3500, 3745, 3990, 4270, 4830, 3080, 'taxable', 500, 1, NOW(6), NOW(6), '반제품', '사출완료 도장 전', 3080, 3500, 3500, 500),
('ip-plas0-semi-0002-ccccccccccccc', @plastic_tenant, 'PSP002', '자동차 부품 B 반제품', 'assembly', 'EA', 4800, 4800, 5136, 5472, 5856, 6624, 4224, 'taxable', 400, 1, NOW(6), NOW(6), '반제품', '사출완료', 4224, 4800, 4800, 400),
('ip-plas0-semi-0003-ccccccccccccc', @plastic_tenant, 'PSP003', '가전 하우징 반제품', 'assembly', 'EA', 6500, 6500, 6955, 7410, 7930, 8970, 5720, 'taxable', 300, 1, NOW(6), NOW(6), '반제품', '조립 전', 5720, 6500, 6500, 300),
('ip-plas0-semi-0004-ccccccccccccc', @plastic_tenant, 'PSP004', '정수기 부품 반제품', 'assembly', 'EA', 5500, 5500, 5885, 6270, 6710, 7590, 4840, 'taxable', 250, 1, NOW(6), NOW(6), '반제품', '검사 전', 4840, 5500, 5500, 250),
('ip-plas0-semi-0005-ccccccccccccc', @plastic_tenant, 'PSP005', '식품용기 반제품', 'assembly', 'EA', 850, 850, 910, 969, 1037, 1173, 748, 'taxable', 2000, 1, NOW(6), NOW(6), '반제품', '인쇄 전', 748, 850, 850, 2000),
('ip-plas0-semi-0006-ccccccccccccc', @plastic_tenant, 'PSP006', '완구 몸체 반제품', 'assembly', 'EA', 2500, 2500, 2675, 2850, 3050, 3450, 2200, 'taxable', 500, 1, NOW(6), NOW(6), '반제품', '조립 전', 2200, 2500, 2500, 500),
('ip-plas0-semi-0007-ccccccccccccc', @plastic_tenant, 'PSP007', '포장뚜껑 반제품', 'assembly', 'EA', 380, 380, 407, 433, 464, 525, 335, 'taxable', 3000, 1, NOW(6), NOW(6), '반제품', '인쇄 전', 335, 380, 380, 3000),
('ip-plas0-semi-0008-ccccccccccccc', @plastic_tenant, 'PSP008', '케이스 반제품 소형', 'assembly', 'EA', 1800, 1800, 1926, 2052, 2196, 2484, 1584, 'taxable', 400, 1, NOW(6), NOW(6), '반제품', '표면처리 전', 1584, 1800, 1800, 400),
('ip-plas0-semi-0009-ccccccccccccc', @plastic_tenant, 'PSP009', '케이스 반제품 대형', 'assembly', 'EA', 3200, 3200, 3424, 3648, 3904, 4416, 2816, 'taxable', 300, 1, NOW(6), NOW(6), '반제품', '표면처리 전', 2816, 3200, 3200, 300),
('ip-plas0-semi-0010-ccccccccccccc', @plastic_tenant, 'PSP010', '기계부품 반제품', 'assembly', 'EA', 8500, 8500, 9095, 9690, 10370, 11730, 7480, 'taxable', 100, 1, NOW(6), NOW(6), '반제품', 'POM 사출완', 7480, 8500, 8500, 100);

-- 완제품 15
INSERT INTO items (item_id, tenant_id, item_code, item_name, item_type, unit, std_price, price_a, price_b, price_c, price_d, price_e, cost_price, tax_type, safe_stock, is_active, created_at, updated_at, item_group, spec, purchase_price, sale_price, standard_price, safety_stock) VALUES
('ip-plas0-fin0-0001-ccccccccccccc', @plastic_tenant, 'PP001', '자동차 내장 부품 A', 'product', 'EA', 5500, 5500, 5885, 6270, 6710, 7590, 4700, 'taxable', 500, 1, NOW(6), NOW(6), '완제품', '도장완', 4700, 5500, 5500, 500),
('ip-plas0-fin0-0002-ccccccccccccc', @plastic_tenant, 'PP002', '자동차 내장 부품 B', 'product', 'EA', 7800, 7800, 8346, 8892, 9516, 10764, 6700, 'taxable', 400, 1, NOW(6), NOW(6), '완제품', '도장완', 6700, 7800, 7800, 400),
('ip-plas0-fin0-0003-ccccccccccccc', @plastic_tenant, 'PP003', '가전 하우징 완제품', 'product', 'EA', 9500, 9500, 10165, 10830, 11590, 13110, 8100, 'taxable', 300, 1, NOW(6), NOW(6), '완제품', '조립완', 8100, 9500, 9500, 300),
('ip-plas0-fin0-0004-ccccccccccccc', @plastic_tenant, 'PP004', '정수기 부품 완제품', 'product', 'EA', 8200, 8200, 8774, 9348, 10004, 11316, 7000, 'taxable', 250, 1, NOW(6), NOW(6), '완제품', '검사완', 7000, 8200, 8200, 250),
('ip-plas0-fin0-0005-ccccccccccccc', @plastic_tenant, 'PP005', '식품용기 완제품', 'product', 'EA', 1280, 1280, 1370, 1459, 1562, 1766, 1080, 'taxable', 2000, 1, NOW(6), NOW(6), '완제품', '인쇄완', 1080, 1280, 1280, 2000),
('ip-plas0-fin0-0006-ccccccccccccc', @plastic_tenant, 'PP006', '완구 조립완제품', 'product', 'EA', 3800, 3800, 4066, 4332, 4636, 5244, 3240, 'taxable', 500, 1, NOW(6), NOW(6), '완제품', '인쇄·조립', 3240, 3800, 3800, 500),
('ip-plas0-fin0-0007-ccccccccccccc', @plastic_tenant, 'PP007', '포장뚜껑 완제품', 'product', 'EA', 580, 580, 621, 661, 708, 801, 490, 'taxable', 3000, 1, NOW(6), NOW(6), '완제품', '인쇄완', 490, 580, 580, 3000),
('ip-plas0-fin0-0008-ccccccccccccc', @plastic_tenant, 'PP008', '케이스 완제품 소형', 'product', 'EA', 2800, 2800, 2996, 3192, 3416, 3864, 2380, 'taxable', 400, 1, NOW(6), NOW(6), '완제품', '표면처리완', 2380, 2800, 2800, 400),
('ip-plas0-fin0-0009-ccccccccccccc', @plastic_tenant, 'PP009', '케이스 완제품 대형', 'product', 'EA', 4800, 4800, 5136, 5472, 5856, 6624, 4100, 'taxable', 300, 1, NOW(6), NOW(6), '완제품', '표면처리완', 4100, 4800, 4800, 300),
('ip-plas0-fin0-0010-ccccccccccccc', @plastic_tenant, 'PP010', '기계부품 완제품', 'product', 'EA', 12500, 12500, 13375, 14250, 15250, 17250, 10700, 'taxable', 100, 1, NOW(6), NOW(6), '완제품', '검사완', 10700, 12500, 12500, 100),
('ip-plas0-fin0-0011-ccccccccccccc', @plastic_tenant, 'PP011', '투명 디스플레이 커버', 'product', 'EA', 3500, 3500, 3745, 3990, 4270, 4830, 3000, 'taxable', 300, 1, NOW(6), NOW(6), '완제품', 'PC 투명', 3000, 3500, 3500, 300),
('ip-plas0-fin0-0012-ccccccccccccc', @plastic_tenant, 'PP012', '포장용기 1L', 'product', 'EA', 1800, 1800, 1926, 2052, 2196, 2484, 1540, 'taxable', 2000, 1, NOW(6), NOW(6), '완제품', '인쇄완', 1540, 1800, 1800, 2000),
('ip-plas0-fin0-0013-ccccccccccccc', @plastic_tenant, 'PP013', '포장용기 500ml', 'product', 'EA', 980, 980, 1049, 1118, 1197, 1353, 840, 'taxable', 3000, 1, NOW(6), NOW(6), '완제품', '인쇄완', 840, 980, 980, 3000),
('ip-plas0-fin0-0014-ccccccccccccc', @plastic_tenant, 'PP014', 'OEM 커스텀 A', 'product', 'EA', 6500, 6500, 6955, 7410, 7930, 8970, 5550, 'taxable', 100, 1, NOW(6), NOW(6), '완제품', '고객 맞춤', 5550, 6500, 6500, 100),
('ip-plas0-fin0-0015-ccccccccccccc', @plastic_tenant, 'PP015', 'OEM 커스텀 B', 'product', 'EA', 9800, 9800, 10486, 11172, 11956, 13524, 8380, 'taxable', 80, 1, NOW(6), NOW(6), '완제품', '고객 맞춤', 8380, 9800, 9800, 80);

-- BOM 15 (완제품 = 반제품 + 펠릿 + 마스터배치)
INSERT INTO bom_headers (bom_id, tenant_id, product_item_id, bom_name, bom_version, is_default, is_active, memo, created_at, updated_at)
SELECT CONCAT('bh-plas0-', item_code, '-cccccccccccccccc'), @plastic_tenant, item_id, CONCAT(item_name, ' BOM'), 1, 1, 1, 'plastic BOM', NOW(6), NOW(6)
FROM items WHERE tenant_id=@plastic_tenant AND item_type='product';

INSERT INTO bom_items (bom_item_id, bom_id, tenant_id, seq_no, material_item_id, qty, unit, loss_rate, memo) VALUES
-- PP001 ~ PP015 : 각 완제품 = 반제품 1 + 원료 2 (펠릿+마스터배치)
('bi-plas0-p001-01-cccccccccccccccc', 'bh-plas0-PP001-cccccccccccccccc', @plastic_tenant, 1, 'ip-plas0-semi-0001-ccccccccccccc', 1, 'EA', 2, '자동차 A 반제품'),
('bi-plas0-p001-02-cccccccccccccccc', 'bh-plas0-PP001-cccccccccccccccc', @plastic_tenant, 2, 'ip-plas0-mat-0001-cccccccccccccc', 0.5, 'KG', 3, 'ABS 펠릿'),
('bi-plas0-p001-03-cccccccccccccccc', 'bh-plas0-PP001-cccccccccccccccc', @plastic_tenant, 3, 'ip-plas0-mat-0009-cccccccccccccc', 0.02, 'KG', 1, '검정 마스터배치'),
('bi-plas0-p002-01-cccccccccccccccc', 'bh-plas0-PP002-cccccccccccccccc', @plastic_tenant, 1, 'ip-plas0-semi-0002-ccccccccccccc', 1, 'EA', 2, '자동차 B 반제품'),
('bi-plas0-p002-02-cccccccccccccccc', 'bh-plas0-PP002-cccccccccccccccc', @plastic_tenant, 2, 'ip-plas0-mat-0002-cccccccccccccc', 0.8, 'KG', 3, 'ABS 고내열'),
('bi-plas0-p002-03-cccccccccccccccc', 'bh-plas0-PP002-cccccccccccccccc', @plastic_tenant, 3, 'ip-plas0-mat-0009-cccccccccccccc', 0.03, 'KG', 1, '검정 MB'),
('bi-plas0-p003-01-cccccccccccccccc', 'bh-plas0-PP003-cccccccccccccccc', @plastic_tenant, 1, 'ip-plas0-semi-0003-ccccccccccccc', 1, 'EA', 2, '가전 하우징'),
('bi-plas0-p003-02-cccccccccccccccc', 'bh-plas0-PP003-cccccccccccccccc', @plastic_tenant, 2, 'ip-plas0-mat-0001-cccccccccccccc', 1.2, 'KG', 3, 'ABS'),
('bi-plas0-p003-03-cccccccccccccccc', 'bh-plas0-PP003-cccccccccccccccc', @plastic_tenant, 3, 'ip-plas0-mat-0010-cccccccccccccc', 0.04, 'KG', 1, '흰 MB'),
('bi-plas0-p004-01-cccccccccccccccc', 'bh-plas0-PP004-cccccccccccccccc', @plastic_tenant, 1, 'ip-plas0-semi-0004-ccccccccccccc', 1, 'EA', 2, '정수기 반제품'),
('bi-plas0-p004-02-cccccccccccccccc', 'bh-plas0-PP004-cccccccccccccccc', @plastic_tenant, 2, 'ip-plas0-mat-0004-cccccccccccccc', 0.6, 'KG', 3, 'PP'),
('bi-plas0-p005-01-cccccccccccccccc', 'bh-plas0-PP005-cccccccccccccccc', @plastic_tenant, 1, 'ip-plas0-semi-0005-ccccccccccccc', 1, 'EA', 2, '식품용기 반제품'),
('bi-plas0-p005-02-cccccccccccccccc', 'bh-plas0-PP005-cccccccccccccccc', @plastic_tenant, 2, 'ip-plas0-mat-0006-cccccccccccccc', 0.08, 'KG', 3, 'PET'),
('bi-plas0-p006-01-cccccccccccccccc', 'bh-plas0-PP006-cccccccccccccccc', @plastic_tenant, 1, 'ip-plas0-semi-0006-ccccccccccccc', 1, 'EA', 2, '완구 몸체'),
('bi-plas0-p006-02-cccccccccccccccc', 'bh-plas0-PP006-cccccccccccccccc', @plastic_tenant, 2, 'ip-plas0-mat-0001-cccccccccccccc', 0.3, 'KG', 3, 'ABS'),
('bi-plas0-p006-03-cccccccccccccccc', 'bh-plas0-PP006-cccccccccccccccc', @plastic_tenant, 3, 'ip-plas0-mat-0011-cccccccccccccc', 0.02, 'KG', 1, '빨강 MB'),
('bi-plas0-p007-01-cccccccccccccccc', 'bh-plas0-PP007-cccccccccccccccc', @plastic_tenant, 1, 'ip-plas0-semi-0007-ccccccccccccc', 1, 'EA', 2, '포장뚜껑 반제품'),
('bi-plas0-p008-01-cccccccccccccccc', 'bh-plas0-PP008-cccccccccccccccc', @plastic_tenant, 1, 'ip-plas0-semi-0008-ccccccccccccc', 1, 'EA', 2, '케이스 소형'),
('bi-plas0-p008-02-cccccccccccccccc', 'bh-plas0-PP008-cccccccccccccccc', @plastic_tenant, 2, 'ip-plas0-mat-0001-cccccccccccccc', 0.25, 'KG', 3, 'ABS'),
('bi-plas0-p009-01-cccccccccccccccc', 'bh-plas0-PP009-cccccccccccccccc', @plastic_tenant, 1, 'ip-plas0-semi-0009-ccccccccccccc', 1, 'EA', 2, '케이스 대형'),
('bi-plas0-p009-02-cccccccccccccccc', 'bh-plas0-PP009-cccccccccccccccc', @plastic_tenant, 2, 'ip-plas0-mat-0001-cccccccccccccc', 0.5, 'KG', 3, 'ABS'),
('bi-plas0-p010-01-cccccccccccccccc', 'bh-plas0-PP010-cccccccccccccccc', @plastic_tenant, 1, 'ip-plas0-semi-0010-ccccccccccccc', 1, 'EA', 2, '기계부품 반제품'),
('bi-plas0-p010-02-cccccccccccccccc', 'bh-plas0-PP010-cccccccccccccccc', @plastic_tenant, 2, 'ip-plas0-mat-0008-cccccccccccccc', 0.3, 'KG', 3, 'POM'),
('bi-plas0-p011-01-cccccccccccccccc', 'bh-plas0-PP011-cccccccccccccccc', @plastic_tenant, 1, 'ip-plas0-semi-0008-ccccccccccccc', 1, 'EA', 2, '케이스 반제품'),
('bi-plas0-p011-02-cccccccccccccccc', 'bh-plas0-PP011-cccccccccccccccc', @plastic_tenant, 2, 'ip-plas0-mat-0003-cccccccccccccc', 0.2, 'KG', 3, 'PC 투명'),
('bi-plas0-p012-01-cccccccccccccccc', 'bh-plas0-PP012-cccccccccccccccc', @plastic_tenant, 1, 'ip-plas0-semi-0005-ccccccccccccc', 2, 'EA', 2, '용기 반제품'),
('bi-plas0-p012-02-cccccccccccccccc', 'bh-plas0-PP012-cccccccccccccccc', @plastic_tenant, 2, 'ip-plas0-mat-0006-cccccccccccccc', 0.12, 'KG', 3, 'PET'),
('bi-plas0-p013-01-cccccccccccccccc', 'bh-plas0-PP013-cccccccccccccccc', @plastic_tenant, 1, 'ip-plas0-semi-0005-ccccccccccccc', 1, 'EA', 2, '용기 반제품'),
('bi-plas0-p013-02-cccccccccccccccc', 'bh-plas0-PP013-cccccccccccccccc', @plastic_tenant, 2, 'ip-plas0-mat-0006-cccccccccccccc', 0.06, 'KG', 3, 'PET'),
('bi-plas0-p014-01-cccccccccccccccc', 'bh-plas0-PP014-cccccccccccccccc', @plastic_tenant, 1, 'ip-plas0-semi-0003-ccccccccccccc', 1, 'EA', 2, '하우징'),
('bi-plas0-p014-02-cccccccccccccccc', 'bh-plas0-PP014-cccccccccccccccc', @plastic_tenant, 2, 'ip-plas0-mat-0001-cccccccccccccc', 0.8, 'KG', 3, 'ABS'),
('bi-plas0-p015-01-cccccccccccccccc', 'bh-plas0-PP015-cccccccccccccccc', @plastic_tenant, 1, 'ip-plas0-semi-0003-ccccccccccccc', 1, 'EA', 2, '하우징'),
('bi-plas0-p015-02-cccccccccccccccc', 'bh-plas0-PP015-cccccccccccccccc', @plastic_tenant, 2, 'ip-plas0-mat-0002-cccccccccccccc', 1, 'KG', 3, 'ABS 고내열');

SELECT
  (SELECT COUNT(*) FROM employees WHERE tenant_id=@plastic_tenant) e,
  (SELECT COUNT(*) FROM items WHERE tenant_id=@plastic_tenant) i,
  (SELECT COUNT(*) FROM bom_headers WHERE tenant_id=@plastic_tenant) bh,
  (SELECT COUNT(*) FROM bom_items WHERE tenant_id=@plastic_tenant) bi;
