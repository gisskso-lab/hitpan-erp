SET @@session.block_encryption_mode = 'aes-256-cbc';
SET @key = _binary 'hitpan-aes-key-32bytes-exactly!!';
SET @metal_tenant = 'tenant-metal-a000-aaaa-aaaaaaaaaaaa';

-- 직원 8명 (사장·공장장·영업2·경리·생산3)
INSERT INTO employees (employee_id, tenant_id, emp_no, emp_name, position, job_title, emp_type, join_date, phone, email, bank_name, bank_account, is_active, created_at, updated_at, role) VALUES
('em-metal-0001-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'E001', '이대표', 'ceo', '대표이사', 'regular', '2020-03-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-1111-2222', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('ceo@korea-jeonggong.co.kr', @key, @i))),
 '국민은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-000-000001', @key, @i))),
 1, NOW(6), NOW(6), 'tenant_admin'),
('em-metal-0002-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'E002', '박부장', 'manager', '공장장', 'regular', '2020-03-15',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-1111-3333', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('park@korea-jeonggong.co.kr', @key, @i))),
 '우리은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('101-000-000002', @key, @i))),
 1, NOW(6), NOW(6), 'production_manager'),
('em-metal-0003-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'E003', '김여사', 'clerk', '경리부장', 'regular', '2020-04-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-1111-4444', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('kim@korea-jeonggong.co.kr', @key, @i))),
 '신한은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('110-000-000003', @key, @i))),
 1, NOW(6), NOW(6), 'accountant'),
('em-metal-0004-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'E004', '윤대리', 'staff', '영업대리', 'regular', '2023-05-10',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-1111-5555', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('yoon@korea-jeonggong.co.kr', @key, @i))),
 '기업은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('312-000-000004', @key, @i))),
 1, NOW(6), NOW(6), 'sales_user'),
('em-metal-0005-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'E005', '최주임', 'staff', '영업주임', 'regular', '2024-01-15',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-1111-6666', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('choi@korea-jeonggong.co.kr', @key, @i))),
 '하나은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('310-000-000005', @key, @i))),
 1, NOW(6), NOW(6), 'sales_user'),
('em-metal-0006-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'E006', '강기사', 'staff', '생산기사', 'regular', '2020-06-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-1111-7777', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('kang@korea-jeonggong.co.kr', @key, @i))),
 '농협', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('351-000-000006', @key, @i))),
 1, NOW(6), NOW(6), 'production_user'),
('em-metal-0007-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'E007', '정기사', 'staff', '생산기사', 'regular', '2021-03-20',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-1111-8888', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('jung@korea-jeonggong.co.kr', @key, @i))),
 '국민은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-000-000007', @key, @i))),
 1, NOW(6), NOW(6), 'production_user'),
('em-metal-0008-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'E008', '조반장', 'staff', '생산반장', 'regular', '2022-07-01',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('010-1111-9999', @key, @i))),
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('cho@korea-jeonggong.co.kr', @key, @i))),
 '우리은행', TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('101-000-000008', @key, @i))),
 1, NOW(6), NOW(6), 'production_user');

-- items 35종
-- 원자재 12 (material), 반제품 8 (semi/assembly), 완제품 15 (product)
INSERT INTO items (item_id, tenant_id, item_code, item_name, item_type, unit, std_price, price_a, price_b, price_c, price_d, price_e, cost_price, tax_type, safe_stock, is_active, created_at, updated_at, item_group, spec, purchase_price, sale_price, standard_price, safety_stock) VALUES
-- 원자재 (철강·특수강·스테인리스 12종)
('im-metal-mat-0001-aaaaaaaaaaaaaa', @metal_tenant, 'M001', 'SM45C 환봉 Φ20', 'material', 'KG', 2500, 2500, 2700, 2900, 3100, 3500, 2200, 'taxable', 500, 1, NOW(6), NOW(6), '탄소강', 'Φ20×1000mm', 2200, 2500, 2500, 500),
('im-metal-mat-0002-aaaaaaaaaaaaaa', @metal_tenant, 'M002', 'SM45C 환봉 Φ30', 'material', 'KG', 2600, 2600, 2808, 2990, 3250, 3640, 2280, 'taxable', 500, 1, NOW(6), NOW(6), '탄소강', 'Φ30×1000mm', 2280, 2600, 2600, 500),
('im-metal-mat-0003-aaaaaaaaaaaaaa', @metal_tenant, 'M003', 'SM45C 환봉 Φ50', 'material', 'KG', 2700, 2700, 2916, 3105, 3375, 3780, 2370, 'taxable', 300, 1, NOW(6), NOW(6), '탄소강', 'Φ50×1000mm', 2370, 2700, 2700, 300),
('im-metal-mat-0004-aaaaaaaaaaaaaa', @metal_tenant, 'M004', 'S45C 각재 50×50', 'material', 'KG', 2800, 2800, 3024, 3220, 3500, 3920, 2450, 'taxable', 400, 1, NOW(6), NOW(6), '탄소강', '50×50×1000', 2450, 2800, 2800, 400),
('im-metal-mat-0005-aaaaaaaaaaaaaa', @metal_tenant, 'M005', 'SUS304 환봉 Φ20', 'material', 'KG', 8500, 8500, 9180, 9775, 10625, 11900, 7500, 'taxable', 200, 1, NOW(6), NOW(6), '스테인리스', 'Φ20×1000mm', 7500, 8500, 8500, 200),
('im-metal-mat-0006-aaaaaaaaaaaaaa', @metal_tenant, 'M006', 'SUS304 판재 1T', 'material', 'KG', 9000, 9000, 9720, 10350, 11250, 12600, 7900, 'taxable', 300, 1, NOW(6), NOW(6), '스테인리스', '1000×2000×1T', 7900, 9000, 9000, 300),
('im-metal-mat-0007-aaaaaaaaaaaaaa', @metal_tenant, 'M007', 'SUS304 판재 2T', 'material', 'KG', 9100, 9100, 9828, 10465, 11375, 12740, 8000, 'taxable', 300, 1, NOW(6), NOW(6), '스테인리스', '1000×2000×2T', 8000, 9100, 9100, 300),
('im-metal-mat-0008-aaaaaaaaaaaaaa', @metal_tenant, 'M008', 'SCM440 환봉 Φ40', 'material', 'KG', 4200, 4200, 4536, 4830, 5250, 5880, 3700, 'taxable', 250, 1, NOW(6), NOW(6), '크롬몰리강', 'Φ40×1000mm', 3700, 4200, 4200, 250),
('im-metal-mat-0009-aaaaaaaaaaaaaa', @metal_tenant, 'M009', 'STKM13C 파이프', 'material', 'KG', 3500, 3500, 3780, 4025, 4375, 4900, 3080, 'taxable', 400, 1, NOW(6), NOW(6), '탄소강관', '각 40×40 2T', 3080, 3500, 3500, 400),
('im-metal-mat-0010-aaaaaaaaaaaaaa', @metal_tenant, 'M010', '알루미늄 6061 T6', 'material', 'KG', 7500, 7500, 8100, 8625, 9375, 10500, 6600, 'taxable', 200, 1, NOW(6), NOW(6), '비철', '판재 1000×500×10T', 6600, 7500, 7500, 200),
('im-metal-mat-0011-aaaaaaaaaaaaaa', @metal_tenant, 'M011', '볼트 M10 12.9', 'material', 'EA', 500, 500, 540, 575, 625, 700, 440, 'taxable', 1000, 1, NOW(6), NOW(6), '체결', 'M10×40 고장력', 440, 500, 500, 1000),
('im-metal-mat-0012-aaaaaaaaaaaaaa', @metal_tenant, 'M012', '너트 M10 SUS', 'material', 'EA', 300, 300, 324, 345, 375, 420, 264, 'taxable', 1000, 1, NOW(6), NOW(6), '체결', 'M10 SUS304', 264, 300, 300, 1000),
-- 반제품 8종 (공정 진행 중)
('im-metal-semi-0001-aaaaaaaaaaaaa', @metal_tenant, 'SP001', 'Shaft 가공 반제품 A형', 'assembly', 'EA', 12000, 12000, 12960, 13800, 15000, 16800, 10560, 'taxable', 100, 1, NOW(6), NOW(6), '반제품', 'Φ20 가공 전', 10560, 12000, 12000, 100),
('im-metal-semi-0002-aaaaaaaaaaaaa', @metal_tenant, 'SP002', 'Shaft 가공 반제품 B형', 'assembly', 'EA', 15000, 15000, 16200, 17250, 18750, 21000, 13200, 'taxable', 100, 1, NOW(6), NOW(6), '반제품', 'Φ30 가공 전', 13200, 15000, 15000, 100),
('im-metal-semi-0003-aaaaaaaaaaaaa', @metal_tenant, 'SP003', '브라켓 반제품 A형', 'assembly', 'EA', 8500, 8500, 9180, 9775, 10625, 11900, 7480, 'taxable', 200, 1, NOW(6), NOW(6), '반제품', '용접 전', 7480, 8500, 8500, 200),
('im-metal-semi-0004-aaaaaaaaaaaaa', @metal_tenant, 'SP004', '브라켓 반제품 B형', 'assembly', 'EA', 9500, 9500, 10260, 10925, 11875, 13300, 8360, 'taxable', 200, 1, NOW(6), NOW(6), '반제품', '용접 전 대형', 8360, 9500, 9500, 200),
('im-metal-semi-0005-aaaaaaaaaaaaa', @metal_tenant, 'SP005', '하우징 가공 반제품', 'assembly', 'EA', 35000, 35000, 37800, 40250, 43750, 49000, 30800, 'taxable', 50, 1, NOW(6), NOW(6), '반제품', '열처리 전', 30800, 35000, 35000, 50),
('im-metal-semi-0006-aaaaaaaaaaaaa', @metal_tenant, 'SP006', '플랜지 가공 반제품', 'assembly', 'EA', 18000, 18000, 19440, 20700, 22500, 25200, 15840, 'taxable', 80, 1, NOW(6), NOW(6), '반제품', '표면처리 전', 15840, 18000, 18000, 80),
('im-metal-semi-0007-aaaaaaaaaaaaa', @metal_tenant, 'SP007', '기어 가공 반제품', 'assembly', 'EA', 45000, 45000, 48600, 51750, 56250, 63000, 39600, 'taxable', 40, 1, NOW(6), NOW(6), '반제품', '열처리 전', 39600, 45000, 45000, 40),
('im-metal-semi-0008-aaaaaaaaaaaaa', @metal_tenant, 'SP008', '베이스 프레임 반제품', 'assembly', 'EA', 85000, 85000, 91800, 97750, 106250, 119000, 74800, 'taxable', 20, 1, NOW(6), NOW(6), '반제품', '도장 전', 74800, 85000, 85000, 20),
-- 완제품 15종 (도금·열처리·표면처리 완료 납품용)
('im-metal-fin0-0001-aaaaaaaaaaaaa', @metal_tenant, 'P001', 'Drive Shaft 표준형', 'product', 'EA', 28000, 28000, 30240, 32200, 35000, 39200, 24000, 'taxable', 100, 1, NOW(6), NOW(6), '완제품', '도금완', 24000, 28000, 28000, 100),
('im-metal-fin0-0002-aaaaaaaaaaaaa', @metal_tenant, 'P002', 'Drive Shaft 강화형', 'product', 'EA', 35000, 35000, 37800, 40250, 43750, 49000, 30000, 'taxable', 80, 1, NOW(6), NOW(6), '완제품', '열처리완', 30000, 35000, 35000, 80),
('im-metal-fin0-0003-aaaaaaaaaaaaa', @metal_tenant, 'P003', '서브프레임 브라켓 A', 'product', 'EA', 18500, 18500, 19980, 21275, 23125, 25900, 15800, 'taxable', 150, 1, NOW(6), NOW(6), '완제품', '도장완', 15800, 18500, 18500, 150),
('im-metal-fin0-0004-aaaaaaaaaaaaa', @metal_tenant, 'P004', '서브프레임 브라켓 B', 'product', 'EA', 22000, 22000, 23760, 25300, 27500, 30800, 18700, 'taxable', 150, 1, NOW(6), NOW(6), '완제품', '도장완', 18700, 22000, 22000, 150),
('im-metal-fin0-0005-aaaaaaaaaaaaa', @metal_tenant, 'P005', '하우징 어셈블리 A', 'product', 'EA', 78000, 78000, 84240, 89700, 97500, 109200, 66000, 'taxable', 30, 1, NOW(6), NOW(6), '완제품', '열처리 조립완', 66000, 78000, 78000, 30),
('im-metal-fin0-0006-aaaaaaaaaaaaa', @metal_tenant, 'P006', '하우징 어셈블리 B', 'product', 'EA', 95000, 95000, 102600, 109250, 118750, 133000, 80500, 'taxable', 25, 1, NOW(6), NOW(6), '완제품', '열처리 대형', 80500, 95000, 95000, 25),
('im-metal-fin0-0007-aaaaaaaaaaaaa', @metal_tenant, 'P007', '플랜지 완제 150A', 'product', 'EA', 42000, 42000, 45360, 48300, 52500, 58800, 35800, 'taxable', 50, 1, NOW(6), NOW(6), '완제품', '아연도금', 35800, 42000, 42000, 50),
('im-metal-fin0-0008-aaaaaaaaaaaaa', @metal_tenant, 'P008', '플랜지 완제 200A', 'product', 'EA', 52000, 52000, 56160, 59800, 65000, 72800, 44200, 'taxable', 40, 1, NOW(6), NOW(6), '완제품', '아연도금', 44200, 52000, 52000, 40),
('im-metal-fin0-0009-aaaaaaaaaaaaa', @metal_tenant, 'P009', '기어 M2 Z40', 'product', 'EA', 95000, 95000, 102600, 109250, 118750, 133000, 81000, 'taxable', 30, 1, NOW(6), NOW(6), '완제품', '열처리완', 81000, 95000, 95000, 30),
('im-metal-fin0-0010-aaaaaaaaaaaaa', @metal_tenant, 'P010', '기어 M3 Z60', 'product', 'EA', 125000, 125000, 135000, 143750, 156250, 175000, 106000, 'taxable', 20, 1, NOW(6), NOW(6), '완제품', '열처리완', 106000, 125000, 125000, 20),
('im-metal-fin0-0011-aaaaaaaaaaaaa', @metal_tenant, 'P011', '베이스 프레임 소형', 'product', 'EA', 150000, 150000, 162000, 172500, 187500, 210000, 127500, 'taxable', 10, 1, NOW(6), NOW(6), '완제품', '도장완', 127500, 150000, 150000, 10),
('im-metal-fin0-0012-aaaaaaaaaaaaa', @metal_tenant, 'P012', '베이스 프레임 대형', 'product', 'EA', 280000, 280000, 302400, 322000, 350000, 392000, 238000, 'taxable', 5, 1, NOW(6), NOW(6), '완제품', '도장완', 238000, 280000, 280000, 5),
('im-metal-fin0-0013-aaaaaaaaaaaaa', @metal_tenant, 'P013', '커스텀 가공 A', 'product', 'EA', 68000, 68000, 73440, 78200, 85000, 95200, 58000, 'taxable', 20, 1, NOW(6), NOW(6), '완제품', '고객 도면', 58000, 68000, 68000, 20),
('im-metal-fin0-0014-aaaaaaaaaaaaa', @metal_tenant, 'P014', '커스텀 가공 B', 'product', 'EA', 88000, 88000, 95040, 101200, 110000, 123200, 75000, 'taxable', 15, 1, NOW(6), NOW(6), '완제품', '고객 도면', 75000, 88000, 88000, 15),
('im-metal-fin0-0015-aaaaaaaaaaaaa', @metal_tenant, 'P015', '정밀 부품 SUS', 'product', 'EA', 45000, 45000, 48600, 51750, 56250, 63000, 38500, 'taxable', 60, 1, NOW(6), NOW(6), '완제품', 'SUS304 표면처리완', 38500, 45000, 45000, 60);

SELECT item_type, COUNT(*) FROM items WHERE tenant_id=@metal_tenant GROUP BY item_type;
SELECT COUNT(*) employees FROM employees WHERE tenant_id=@metal_tenant;
