-- DB-108 — 매출반품 품목줄 로스 표시 (20260825작6)
--
-- 🔴 사장님 정의 (2026-08-25)
--   "매출에서의 반품 = 판매한 물건이 고객사의 반품으로 재입고된 경우
--    = 파손이면 로스로 정의, 파손이 아니면 재입고(재고반영)"
--
--   종전에는 매출반품을 확정하면 **무조건 재고를 늘렸다.**
--   파손품인데 재입고되면 **팔 수 없는 물건이 재고로 잡힌다.**
--   현장에서 재고를 세면 숫자가 안 맞는다.
--
-- 🔴 왜 품목줄(items)인가 — 문서 통째가 아니라
--   한 번의 반품에 정상품과 파손품이 **섞여 온다.**
--   "이 상자는 멀쩡하고 저 상자는 깨졌다" 가 현장이다.
--   문서 단위로 받으면 그런 반품을 두 장으로 쪼개 써야 한다.
--
-- 🔴 판정은 고객사가 한다 — 우리가 정하지 않는다
--   사장님: "로스판정 기준은 고객사가 정하는거지, 너가 왜 정해."
--   그래서 반품사유 코드로 자동 판정하지 않는다. **사람이 줄마다 고른다.**
--   (헌법 #11 과 같은 축 — 업종별 템플릿을 우리가 깔지 않는다)
--
-- 무엇이 달라지나
--   is_loss = 0 (기본) → 종전과 같다. 재고원장 IN + item_stock 증가
--   is_loss = 1        → **재고를 건드리지 않는다.** 팔 수 없는 물건이니까.
--                        단 매출·미수 차감은 **그대로** 한다 — 고객에게 돈은 돌려주니까.
--
-- ⚠️ 기존 행은 0 이다 — 지금까지의 반품은 전부 재입고로 처리됐고 그게 사실이다.
--    없던 파손 표시를 소급해 지어내지 않는다.
-- ⚠️ 재적용 안전 — 컬럼이 이미 있으면 아무 일도 하지 않는다.

SET @col_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'sales_return_items'
      AND COLUMN_NAME = 'is_loss'
);

SET @ddl := IF(@col_exists = 0,
    'ALTER TABLE sales_return_items
       ADD COLUMN is_loss TINYINT(1) NOT NULL DEFAULT 0
       COMMENT ''파손 로스 여부 — 1이면 재고 미반영(팔 수 없는 물건). 매출·미수 차감은 그대로''
       AFTER warehouse_id',
    'SELECT ''DB-108: sales_return_items.is_loss already exists — skip'' AS msg');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
