-- 본 테넌트(452ca266-97b9-4cd1-a0ac-2f37830c81f6) 옛 마이그 데이터 정리
-- 2026-05-13 야간, 사장님 GO 결재
-- 안전: created_at 분석 결과 전 데이터가 오늘 마이그 시간(11:52~12:29)에 집중,
--       사장님 수동 입력 0건 확인. employee 1건(사장님 본인)은 emp_no NOT LIKE 'MIG-%'로 보호.
-- 순서: FK 의존성 역순 (lines/items 먼저, header 나중)

SET @tenant := '452ca266-97b9-4cd1-a0ac-2f37830c81f6';

-- 1) 거래·재무 leaf 테이블
DELETE FROM bom_items WHERE tenant_id=@tenant;
DELETE FROM bom_headers WHERE tenant_id=@tenant;

DELETE FROM stock_ledger WHERE tenant_id=@tenant AND source_type='migration';

DELETE FROM collections WHERE tenant_id=@tenant;
DELETE FROM cashbook WHERE tenant_id=@tenant;
DELETE FROM expenses WHERE tenant_id=@tenant;

-- 세금계산서 → 거래명세서 역순 (tax_invoices의 delivery_id FK)
DELETE FROM tax_invoices WHERE tenant_id=@tenant;
DELETE FROM sales_delivery_items WHERE tenant_id=@tenant;
DELETE FROM sales_deliveries WHERE tenant_id=@tenant;

-- 판매·매입 주문
DELETE FROM sales_order_items WHERE tenant_id=@tenant;
DELETE FROM sales_orders WHERE tenant_id=@tenant;
DELETE FROM purchase_order_items WHERE tenant_id=@tenant;
DELETE FROM purchase_orders WHERE tenant_id=@tenant;

-- 은행·카드·어음
DELETE FROM bank_transactions WHERE tenant_id=@tenant;
DELETE FROM card_payment_lines WHERE tenant_id=@tenant;
DELETE FROM card_payments WHERE tenant_id=@tenant;
DELETE FROM bills WHERE tenant_id=@tenant;

-- 마스터 (MIG- 접두사만 — 사장님 본인 계정 보호)
DELETE FROM partners WHERE tenant_id=@tenant AND partner_code LIKE 'MIG-%';
DELETE FROM items WHERE tenant_id=@tenant AND item_code LIKE 'MIG-%';
DELETE FROM employees WHERE tenant_id=@tenant AND emp_no LIKE 'MIG-%';
DELETE FROM warehouses WHERE tenant_id=@tenant AND warehouse_id LIKE 'wh-mig%';

-- 검증
SELECT 'partners' AS tbl, COUNT(*) AS remain FROM partners WHERE tenant_id=@tenant
UNION ALL SELECT 'items', COUNT(*) FROM items WHERE tenant_id=@tenant
UNION ALL SELECT 'employees', COUNT(*) FROM employees WHERE tenant_id=@tenant
UNION ALL SELECT 'stock_ledger', COUNT(*) FROM stock_ledger WHERE tenant_id=@tenant
UNION ALL SELECT 'collections', COUNT(*) FROM collections WHERE tenant_id=@tenant
UNION ALL SELECT 'cashbook', COUNT(*) FROM cashbook WHERE tenant_id=@tenant
UNION ALL SELECT 'expenses', COUNT(*) FROM expenses WHERE tenant_id=@tenant
UNION ALL SELECT 'tax_invoices', COUNT(*) FROM tax_invoices WHERE tenant_id=@tenant
UNION ALL SELECT 'sales_deliveries', COUNT(*) FROM sales_deliveries WHERE tenant_id=@tenant
UNION ALL SELECT 'sales_orders', COUNT(*) FROM sales_orders WHERE tenant_id=@tenant
UNION ALL SELECT 'purchase_orders', COUNT(*) FROM purchase_orders WHERE tenant_id=@tenant
UNION ALL SELECT 'bank_transactions', COUNT(*) FROM bank_transactions WHERE tenant_id=@tenant
UNION ALL SELECT 'card_payments', COUNT(*) FROM card_payments WHERE tenant_id=@tenant
UNION ALL SELECT 'bills', COUNT(*) FROM bills WHERE tenant_id=@tenant;
