-- DB-33: sales_orders.status ENUM에 partial, closed 추가
-- 원인: 코드(SalesOrderStatus enum)는 partial/closed를 사용하는데
--       DB ENUM에 해당 값이 없어 출고 완료 후 헤더 status 동기화가 안 됨
--       → 수주 재선택 시 "전환 가능한 미출고 품목이 없습니다" 오류 재현
-- 함께 수정: 기존 불일치 수주(아이템 모두 closed인데 헤더 confirmed) 일괄 동기화

ALTER TABLE sales_orders
  MODIFY COLUMN status
    ENUM('draft','quotation','order','confirmed','partial','closed','invoiced','cancelled')
    NOT NULL DEFAULT 'draft';

-- 기존 데이터 정합성 복구: 모든 아이템 closed → 헤더 closed
UPDATE sales_orders so
JOIN (
  SELECT order_id,
         SUM(CASE WHEN item_status = 'closed' THEN 1 ELSE 0 END) AS closed_cnt,
         COUNT(*) AS total_cnt
  FROM sales_order_items
  GROUP BY order_id
) s ON s.order_id = so.order_id
SET so.status = 'closed', so.updated_at = NOW(6)
WHERE s.closed_cnt = s.total_cnt
  AND so.status NOT IN ('closed', 'cancelled');
