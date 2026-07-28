-- DB-34: sales_orders.is_auto 컬럼 추가
-- 원인: 다이렉트 판매(수주 없이 거래명세서 생성) 시 정합성을 위해 수주를 자동생성하는데
--       자동생성된 수주가 수주서 목록에 노출되어 현장 혼란 발생
-- 해결: is_auto=1 인 수주는 목록 조회에서 제외 (집계/현황에서는 포함)

ALTER TABLE sales_orders
  ADD COLUMN is_auto TINYINT(1) NOT NULL DEFAULT 0
    COMMENT '다이렉트 판매 시 자동생성 수주 (목록 표시 제외)';
