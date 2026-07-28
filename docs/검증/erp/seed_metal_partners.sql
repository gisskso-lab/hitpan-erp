SET @@session.block_encryption_mode = 'aes-256-cbc';
SET @key = _binary 'hitpan-aes-key-32bytes-exactly!!';
SET @metal_tenant = 'tenant-metal-a000-aaaa-aaaaaaaaaaaa';

-- 헬퍼: enc()는 인라인. 한 파트너씩 간결하게.

-- ====== 납품처 7 ======
INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, biz_no, biz_no_hash, ceo_name, biz_type, biz_item, tel, address, credit_limit, payment_terms, bank_name, bank_account, account_holder, is_active, created_at, updated_at, price_grade, tax_type, vat_handling, price_display_preference) VALUES
('pm-metal-cust-0001-aaaaaaaaaaaa', @metal_tenant, 'C001', '현대모비스', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1208134595', @key, @i))), SHA2('1208134595',256),
 '정만국', '제조업', '자동차부품',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-280-3000', @key, @i))),
 '경기도 용인시', 200000000, 30, '국민은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-123-456789', @key, @i))),
 '현대모비스', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inherit'),
('pm-metal-cust-0002-aaaaaaaaaaaa', @metal_tenant, 'C002', '만도', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2188601234', @key, @i))), SHA2('2188601234',256),
 '조성현', '제조업', '제동장치',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-400-1000', @key, @i))),
 '경기도 평택시', 150000000, 60, '우리은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('101-234-567890', @key, @i))),
 '만도', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inherit'),
('pm-metal-cust-0003-aaaaaaaaaaaa', @metal_tenant, 'C003', '성우하이텍', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1308215432', @key, @i))), SHA2('1308215432',256),
 '이성우', '제조업', '차체부품',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-371-2000', @key, @i))),
 '경기도 화성시', 100000000, 60, '신한은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('110-345-678901', @key, @i))),
 '성우하이텍', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inclusive'),
('pm-metal-cust-0004-aaaaaaaaaaaa', @metal_tenant, 'C004', '시화정밀', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1348512345', @key, @i))), SHA2('1348512345',256),
 '강시화', '제조업', '정밀가공',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-493-1001', @key, @i))),
 '경기도 시흥시', 50000000, 30, '기업은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('312-456-789012', @key, @i))),
 '시화정밀', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit'),
('pm-metal-cust-0005-aaaaaaaaaaaa', @metal_tenant, 'C005', '반월기계', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2258623451', @key, @i))), SHA2('2258623451',256),
 '임반월', '제조업', '산업기계',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-362-2002', @key, @i))),
 '경기도 안산시', 80000000, 30, '하나은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('310-567-890123', @key, @i))),
 '반월기계', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inclusive'),
('pm-metal-cust-0006-aaaaaaaaaaaa', @metal_tenant, 'C006', '남동정공', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1378912345', @key, @i))), SHA2('1378912345',256),
 '조남동', '제조업', '금속부품',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('032-813-3003', @key, @i))),
 '인천시 남동구', 40000000, 90, '농협',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('351-678-901234', @key, @i))),
 '남동정공', 1, NOW(6), NOW(6), 'C', 'taxable', 'zero', 'inherit'),
('pm-metal-cust-0007-aaaaaaaaaaaa', @metal_tenant, 'C007', '구로기계', 'customer',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1098634567', @key, @i))), SHA2('1098634567',256),
 '윤구로', '제조업', '정밀기계',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('02-862-4004', @key, @i))),
 '서울시 구로구', 30000000, 30, '국민은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-789-012345', @key, @i))),
 '구로기계', 1, NOW(6), NOW(6), 'C', 'exempt', 'exempt', 'inherit');

-- ====== 공급사 3 ======
INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, biz_no, biz_no_hash, ceo_name, biz_type, biz_item, tel, address, credit_limit, payment_terms, bank_name, bank_account, account_holder, is_active, created_at, updated_at, price_grade, tax_type, vat_handling, price_display_preference) VALUES
('pm-metal-supp-0001-aaaaaaaaaaaa', @metal_tenant, 'S001', '포스코스틸', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1068634521', @key, @i))), SHA2('1068634521',256),
 '김포스', '제조업', '철강',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('054-220-1000', @key, @i))),
 '경상북도 포항시', 0, 30, '우리은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('101-890-123456', @key, @i))),
 '포스코스틸', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inherit'),
('pm-metal-supp-0002-aaaaaaaaaaaa', @metal_tenant, 'S002', '세아특수강', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1198712345', @key, @i))), SHA2('1198712345',256),
 '이세아', '제조업', '특수강',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-691-5005', @key, @i))),
 '경기도 평택시', 0, 60, '신한은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('110-901-234567', @key, @i))),
 '세아특수강', 1, NOW(6), NOW(6), 'A', 'taxable', 'standard', 'inherit'),
('pm-metal-supp-0003-aaaaaaaaaaaa', @metal_tenant, 'S003', '한국스텐레스', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('1398634578', @key, @i))), SHA2('1398634578',256),
 '박한스', '제조업', '스테인리스강',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-362-6006', @key, @i))),
 '경기도 안산시', 0, 30, '기업은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('312-012-345678', @key, @i))),
 '한국스텐레스', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inclusive');

-- ====== 외주 2 ======
INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, biz_no, biz_no_hash, ceo_name, biz_type, biz_item, tel, address, credit_limit, payment_terms, bank_name, bank_account, account_holder, is_active, created_at, updated_at, price_grade, tax_type, vat_handling, price_display_preference) VALUES
('pm-metal-out0-0001-aaaaaaaaaaaa', @metal_tenant, 'O001', '정밀도금', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2168212345', @key, @i))), SHA2('2168212345',256),
 '강정밀', '제조업', '표면처리',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-362-7007', @key, @i))),
 '경기도 안산시', 0, 30, '농협',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('351-123-456789', @key, @i))),
 '정밀도금', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit'),
('pm-metal-out0-0002-aaaaaaaaaaaa', @metal_tenant, 'O002', '한빛열처리', 'supplier',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('2268512345', @key, @i))), SHA2('2268512345',256),
 '이한빛', '제조업', '열처리',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('031-493-8008', @key, @i))),
 '경기도 시흥시', 0, 60, '국민은행',
 TO_BASE64(CONCAT(@i:=RANDOM_BYTES(16), AES_ENCRYPT('100-234-567890', @key, @i))),
 '한빛열처리', 1, NOW(6), NOW(6), 'B', 'taxable', 'standard', 'inherit');

SELECT partner_type, vat_handling, price_display_preference, COUNT(*) cnt
FROM partners WHERE tenant_id=@metal_tenant
GROUP BY partner_type, vat_handling, price_display_preference;
