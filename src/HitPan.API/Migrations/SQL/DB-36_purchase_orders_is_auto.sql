-- DB-36: purchase_orders 자동발주 식별자
-- 안전재고 자동발주(판매확정 트리거)로 생성된 발주서를 is_auto=1로 표시.
-- GetOrdersAsync에서 AND is_auto=0 필터로 목록 노출 차단 (DB-34 수주 대칭).
-- 적용: 2026-05-04

ALTER TABLE purchase_orders
  ADD COLUMN is_auto TINYINT(1) NOT NULL DEFAULT 0 AFTER memo;
