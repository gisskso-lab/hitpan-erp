SET @tid = '452ca266-97b9-4cd1-a0ac-2f37830c81f6';
SET @i = 0;

-- 상품 200개 (원자재80 + 부품60 + 반제품30 + 완제품30)
-- 자동발주: 원자재 중 20개에 설정
INSERT INTO items (item_id, tenant_id, item_code, item_name, item_group, item_type, unit, spec,
  purchase_price, sale_price, standard_price, std_price, cost_price, safe_stock, safety_stock,
  tax_type, auto_order_enabled, auto_order_partner_id, auto_order_qty,
  is_active, is_deleted, row_version, created_at, updated_at)
SELECT
  UUID(), @tid,
  CONCAT(
    CASE
      WHEN seq <= 80 THEN CONCAT('RM-', LPAD(seq, 4, '0'))
      WHEN seq <= 140 THEN CONCAT('PT-', LPAD(seq-80, 4, '0'))
      WHEN seq <= 170 THEN CONCAT('SF-', LPAD(seq-140, 4, '0'))
      ELSE CONCAT('FG-', LPAD(seq-170, 4, '0'))
    END
  ),
  CONCAT(
    CASE
      WHEN seq <= 80 THEN CONCAT(
        ELT(1+(seq%10),'알루미늄','구리','스테인리스','철','아연','니켈','코발트','실리콘','텅스텐','티타늄'),
        ELT(1+(seq%8),' 판재',' 봉재',' 파이프',' 선재',' 코일',' 시트',' 빌렛',' 잉곳'),
        CONCAT(' ',ELT(1+(seq%5),'1T','2T','3T','5T','10T'))
      )
      WHEN seq <= 140 THEN CONCAT(
        ELT(1+((seq-80)%12),'볼트','너트','와셔','베어링','기어','모터','센서','밸브','커넥터','PCB','LED','저항'),
        ELT(1+((seq-80)%6),' M6',' M8',' M10',' M12',' 3인치',' 5인치'),
        CONCAT(' P',seq-80)
      )
      WHEN seq <= 170 THEN CONCAT(
        ELT(1+((seq-140)%10),'서브어셈블리','모듈','유닛','패널','프레임','하우징','브라켓','커버','베이스','플레이트'),
        CONCAT(' SF',seq-140)
      )
      ELSE CONCAT(
        ELT(1+((seq-170)%10),'산업용로봇','CNC가공기','프레스','컨베이어','용접기','절단기','측정기','포장기','검사장비','세척기'),
        CONCAT(' FG',seq-170)
      )
    END
  ),
  CASE
    WHEN seq <= 80 THEN 'raw_material'
    WHEN seq <= 140 THEN 'part'
    WHEN seq <= 170 THEN 'semi_finished'
    ELSE 'finished'
  END,
  CASE
    WHEN seq <= 80 THEN 'material'
    WHEN seq <= 140 THEN 'part'
    WHEN seq <= 170 THEN 'semi'
    ELSE 'product'
  END,
  ELT(1+(seq%5),'EA','KG','M','BOX','SET'),
  CONCAT(ELT(1+(seq%4),'규격A','규격B','규격C','규격D'), '-', seq),
  -- 매입가
  CASE WHEN seq <= 80 THEN 1000 + seq * 100
       WHEN seq <= 140 THEN 5000 + (seq-80) * 200
       WHEN seq <= 170 THEN 50000 + (seq-140) * 5000
       ELSE 500000 + (seq-170) * 50000
  END,
  -- 판매가 (매입가 * 1.3~1.8)
  CASE WHEN seq <= 80 THEN ROUND((1000 + seq * 100) * 1.5)
       WHEN seq <= 140 THEN ROUND((5000 + (seq-80) * 200) * 1.4)
       WHEN seq <= 170 THEN ROUND((50000 + (seq-140) * 5000) * 1.3)
       ELSE ROUND((500000 + (seq-170) * 50000) * 1.35)
  END,
  -- standard_price = sale_price
  CASE WHEN seq <= 80 THEN ROUND((1000 + seq * 100) * 1.5)
       WHEN seq <= 140 THEN ROUND((5000 + (seq-80) * 200) * 1.4)
       WHEN seq <= 170 THEN ROUND((50000 + (seq-140) * 5000) * 1.3)
       ELSE ROUND((500000 + (seq-170) * 50000) * 1.35)
  END,
  CASE WHEN seq <= 80 THEN ROUND((1000 + seq * 100) * 1.5)
       WHEN seq <= 140 THEN ROUND((5000 + (seq-80) * 200) * 1.4)
       WHEN seq <= 170 THEN ROUND((50000 + (seq-140) * 5000) * 1.3)
       ELSE ROUND((500000 + (seq-170) * 50000) * 1.35)
  END,
  -- cost_price = purchase_price
  CASE WHEN seq <= 80 THEN 1000 + seq * 100
       WHEN seq <= 140 THEN 5000 + (seq-80) * 200
       WHEN seq <= 170 THEN 50000 + (seq-140) * 5000
       ELSE 500000 + (seq-170) * 50000
  END,
  -- safe_stock, safety_stock
  CASE WHEN seq <= 80 THEN 50 + (seq % 30) ELSE 10 + (seq % 20) END,
  CASE WHEN seq <= 80 THEN 50 + (seq % 30) ELSE 10 + (seq % 20) END,
  'taxable',
  -- 자동발주: 원자재 중 처음 20개만 ON
  CASE WHEN seq <= 20 THEN 1 ELSE 0 END,
  -- 자동발주 업체: NULL (나중에 업데이트)
  NULL,
  -- 자동발주 수량
  CASE WHEN seq <= 20 THEN 100 + seq * 10 ELSE 0 END,
  1, 0, 0, NOW(6), NOW(6)
FROM (
  SELECT @i := @i + 1 AS seq FROM information_schema.COLUMNS LIMIT 200
) t;
