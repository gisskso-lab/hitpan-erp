SET @tid = '452ca266-97b9-4cd1-a0ac-2f37830c81f6';
SET @i = 1;

-- 매입처 100개
INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, biz_no, ceo_name, tel, email, price_grade, credit_limit, is_active, is_deleted, created_at, updated_at)
SELECT UUID(), @tid,
  CONCAT('S', LPAD(seq, 4, '0')),
  CONCAT(
    ELT(1 + (seq % 20), '삼성','LG','SK','현대','기아','포스코','한화','두산','롯데','CJ','오뚜기','농심','동원','풀무원','매일','대상','빙그레','삼양','오리온','하이트'),
    ELT(1 + (seq % 10), '전자','산업','테크','소재','바이오','에너지','IT','물류','식품','화학'),
    CONCAT(' S',seq)
  ),
  'supplier',
  CONCAT(LPAD(100+seq,3,'0'),'-',LPAD(10+(seq%90),2,'0'),'-',LPAD(10000+seq*7,5,'0')),
  CONCAT(ELT(1+(seq%5),'김','이','박','최','정'), ELT(1+(seq%5),'영수','미영','철수','지혜','민수')),
  CONCAT('02-', LPAD(1000+seq*3,4,'0'), '-', LPAD(2000+seq*7,4,'0')),
  CONCAT('supplier',seq,'@test.com'),
  ELT(1+(seq%3),'A','B','C'),
  (seq * 500000) + 5000000,
  1, 0, NOW(6), NOW(6)
FROM (
  SELECT @i := @i + 1 AS seq FROM information_schema.COLUMNS LIMIT 100
) t;

SET @i = 0;
-- 매출처 100개
INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, biz_no, ceo_name, tel, email, price_grade, credit_limit, is_active, is_deleted, created_at, updated_at)
SELECT UUID(), @tid,
  CONCAT('C', LPAD(seq, 4, '0')),
  CONCAT(
    ELT(1 + (seq % 20), '네이버','카카오','쿠팡','배민','토스','당근','야놀자','직방','리디','왓챠','마켓컬리','무신사','오늘의집','클래스101','밀리','라프텔','스포티파이','요기요','타다','카페24'),
    ELT(1 + (seq % 8), '코리아','글로벌','테크','플러스','넥스트','프로','랩','솔루션'),
    CONCAT(' C',seq)
  ),
  'customer',
  CONCAT(LPAD(200+seq,3,'0'),'-',LPAD(20+(seq%80),2,'0'),'-',LPAD(20000+seq*11,5,'0')),
  CONCAT(ELT(1+(seq%5),'강','조','윤','장','임'), ELT(1+(seq%5),'서연','도윤','하은','시우','지아')),
  CONCAT('02-', LPAD(5000+seq*3,4,'0'), '-', LPAD(6000+seq*7,4,'0')),
  CONCAT('customer',seq,'@test.com'),
  ELT(1+(seq%3),'A','B','C'),
  (seq * 300000) + 3000000,
  1, 0, NOW(6), NOW(6)
FROM (
  SELECT @i := @i + 1 AS seq FROM information_schema.COLUMNS LIMIT 100
) t;
