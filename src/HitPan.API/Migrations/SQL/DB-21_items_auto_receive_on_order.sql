-- DB-21: items.auto_receive_on_order 컬럼 추가
-- 사장님 헌법 (2026-04-26): "자동발주를 자동으로 하건, 반자동으로 하건,
--   매입처리→확정 이라는 업무흐름은 목록상으로 반드시 남아야 해."
-- 자재별 옵션. Y면 BOM 생산 시 자재 부족 → 발주→매입→매입확정 한 사슬 자동 진행.
-- N(default)면 발주서만 생성, 매입처리/확정은 사용자가 수동.
-- 어느 쪽이든 발주서·매입명세서·원장·회계기표는 동일하게 남음 (§절대원칙 #20).

ALTER TABLE items
  ADD COLUMN IF NOT EXISTS auto_receive_on_order TINYINT(1) NOT NULL DEFAULT 0
    COMMENT '자동발주 시 매입확정까지 자동 (P2 자동사슬)'
    AFTER auto_order_qty;
