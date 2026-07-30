-- ============================================================
-- demo.hitpan.kr 완전 초기화 — 사장님 마이그 실측 전 깨끗한 백지
-- 작성: 2026-06-19 / 사장님 지시 "시드계정 빼고 전부 데이터 초기화"
-- 보존: users(master@hitpan.kr) + local_subscription(BYOK 키) + tenants + 권한/설정
-- 삭제: 모든 ERP 업무 데이터 + AI 대화/사용량 (어차피 샘플 — 사장님 확인)
-- 원칙: 사장님 명시 결재. FK 무시 후 TRUNCATE, 끝나고 FK 복원.
-- ⚠️ 되돌릴 수 없음. 사장님 결재 완료 안건.
-- ============================================================

SET FOREIGN_KEY_CHECKS = 0;

-- ── 업무 원장·거래 데이터 (전부 비움) ──
TRUNCATE TABLE stock_ledger;
TRUNCATE TABLE sales_delivery_items;
TRUNCATE TABLE sales_deliveries;
TRUNCATE TABLE sales_order_items;
TRUNCATE TABLE sales_orders;
TRUNCATE TABLE quotation_items;
TRUNCATE TABLE quotations;
TRUNCATE TABLE tax_invoice_items;
TRUNCATE TABLE tax_invoices;
TRUNCATE TABLE collections;
TRUNCATE TABLE purchase_order_items;
TRUNCATE TABLE purchase_orders;
TRUNCATE TABLE purchase_receipt_items;
TRUNCATE TABLE purchase_receipts;
TRUNCATE TABLE purchase_return_items;
TRUNCATE TABLE purchase_returns;
TRUNCATE TABLE stock_adjustments;
TRUNCATE TABLE bom_items;

-- ── 마스터 데이터 ──
TRUNCATE TABLE partners;
TRUNCATE TABLE partner_special_prices;
TRUNCATE TABLE items;
TRUNCATE TABLE item_special_prices;
TRUNCATE TABLE employees;

-- ── 회계·경리 (있으면) ──
TRUNCATE TABLE journal_lines;
TRUNCATE TABLE journal_entries;
TRUNCATE TABLE expenses;
TRUNCATE TABLE cashbook;
TRUNCATE TABLE bank_transactions;

-- ── CS·AI (검수 재시작) ──
TRUNCATE TABLE service_tickets;
TRUNCATE TABLE ai_conversations;
TRUNCATE TABLE ai_usage_logs;

SET FOREIGN_KEY_CHECKS = 1;

-- ── 보존 확인 (이건 절대 안 지움) ──
SELECT '보존 users' AS what, COUNT(*) AS cnt FROM users
UNION ALL SELECT '보존 BYOK 키', COUNT(*) FROM local_subscription WHERE anthropic_key_status='valid'
UNION ALL SELECT '보존 tenants', COUNT(*) FROM tenants
UNION ALL SELECT '비움 deliveries', COUNT(*) FROM sales_deliveries
UNION ALL SELECT '비움 items', COUNT(*) FROM items
UNION ALL SELECT '비움 stock_ledger', COUNT(*) FROM stock_ledger;
