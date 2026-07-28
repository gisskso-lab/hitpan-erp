-- =========================================================================
-- 히트판 ERP — 업무 데이터 완전 삭제 (2026-04-24 사장님 지시, CTO 승인)
-- =========================================================================
-- 남기는 것:
--   - tenants (테넌트 1개)
--   - users (tenant_admin 1개)
--   - accounts (계정과목 표준 코드)
--   - warehouses (창고 마스터 — 코드성 기본등록정보)
--   - workflow_settings (워크플로우 설정)
--   - common_codes (공통 코드)
--   - platforms / resellers (SaaS 운영)
--   - user_permissions (권한 매핑)
--   - tenant_settings / tenant_devices / tenant_certificates (테넌트 설정)
--   - refresh_tokens / user_sessions (세션, 로그아웃되게 하려면 비워도 됨)
--   - __efmigrationshistory (EF 이력)
--
-- 비우는 것: 그 외 모든 업무 데이터 (파이프라인으로 재구축할 대상).
-- 수행 방식: FK cascade 대신 FK 체크 일시 해제 후 TRUNCATE, 다시 활성화.
-- =========================================================================

SET FOREIGN_KEY_CHECKS = 0;

-- ─── 거래 원장·집계 ───────────────────────────────────────────────
TRUNCATE TABLE stock_ledger;
TRUNCATE TABLE item_stock;
TRUNCATE TABLE inventory_lots;
TRUNCATE TABLE ledger_balance_snapshot;
TRUNCATE TABLE monthly_summary;
TRUNCATE TABLE monthly_summary_sources;
TRUNCATE TABLE monthly_closing;
TRUNCATE TABLE partner_balance;
TRUNCATE TABLE journal_entries;
TRUNCATE TABLE journal_lines;
TRUNCATE TABLE cashbook;

-- ─── 판매 흐름 ──────────────────────────────────────────────────
TRUNCATE TABLE tax_invoices;
TRUNCATE TABLE sales_delivery_items;
TRUNCATE TABLE sales_deliveries;
TRUNCATE TABLE sales_return_items;
TRUNCATE TABLE sales_returns;
TRUNCATE TABLE sales_order_items;
TRUNCATE TABLE sales_orders;
TRUNCATE TABLE quotation_items;
TRUNCATE TABLE quotations;

-- ─── 매입 흐름 ──────────────────────────────────────────────────
TRUNCATE TABLE purchase_return_items;
TRUNCATE TABLE purchase_returns;
TRUNCATE TABLE purchase_receipt_items;
TRUNCATE TABLE purchase_receipts;
TRUNCATE TABLE purchase_order_items;
TRUNCATE TABLE purchase_orders;

-- ─── BOM·생산 ──────────────────────────────────────────────────
TRUNCATE TABLE bom_cost_cache;
TRUNCATE TABLE bom_items;
TRUNCATE TABLE bom_headers;
TRUNCATE TABLE work_in_process;
TRUNCATE TABLE mold_production_log;
TRUNCATE TABLE mold_assets;
TRUNCATE TABLE material_price_history;

-- ─── 수금·지급 ──────────────────────────────────────────────────
TRUNCATE TABLE collections;
TRUNCATE TABLE payments;

-- ─── 재고 관리 ──────────────────────────────────────────────────
TRUNCATE TABLE stock_adjust_logs;
TRUNCATE TABLE stock_alerts;

-- ─── 마스터 (업무) ───────────────────────────────────────────────
TRUNCATE TABLE item_special_prices;
TRUNCATE TABLE partner_special_prices;
TRUNCATE TABLE items;
TRUNCATE TABLE item_groups;
TRUNCATE TABLE partners;

-- ─── 결재·문서 ──────────────────────────────────────────────────
TRUNCATE TABLE approval_history;
TRUNCATE TABLE approval_documents;
TRUNCATE TABLE approval_lines;
TRUNCATE TABLE approval_settings;
TRUNCATE TABLE document_conversions;
TRUNCATE TABLE status_history;

-- ─── 인사·근태 ─────────────────────────────────────────────────
TRUNCATE TABLE leave_requests;
TRUNCATE TABLE overtime;
TRUNCATE TABLE attendance;
TRUNCATE TABLE evaluations;
TRUNCATE TABLE labor_contracts;
TRUNCATE TABLE hr_expense_requests;
TRUNCATE TABLE expenses;
TRUNCATE TABLE employees;
TRUNCATE TABLE departments;

-- ─── 기타 업무 ──────────────────────────────────────────────────
TRUNCATE TABLE haccp_logs;
TRUNCATE TABLE custom_order_specs;
TRUNCATE TABLE esign_records;

-- ─── 캐시·멱등·이벤트 (선택적 초기화) ────────────────────────────
TRUNCATE TABLE idempotency_keys;
TRUNCATE TABLE force_edit_logs;
TRUNCATE TABLE audit_logs;
TRUNCATE TABLE audit_trail;
TRUNCATE TABLE security_alerts;
TRUNCATE TABLE login_attempts;
TRUNCATE TABLE device_login_logs;

-- ─── AI·지식 (사용 기록) ─────────────────────────────────────────
TRUNCATE TABLE ai_conversations;
TRUNCATE TABLE ai_usage_logs;
TRUNCATE TABLE hitpan_knowledge;

-- ─── SaaS 운영 (대리점 영업) — 본사 데이터이나 파이프라인 테스트 무관 ─
TRUNCATE TABLE reseller_commission_policies;
TRUNCATE TABLE reseller_payouts;
TRUNCATE TABLE reseller_promotions;
TRUNCATE TABLE reseller_revenues;
TRUNCATE TABLE subscriptions;

SET FOREIGN_KEY_CHECKS = 1;

-- =========================================================================
-- 검증 쿼리 (수동 실행):
--   SELECT 'partners' t, COUNT(*) c FROM partners
--     UNION ALL SELECT 'items', COUNT(*) FROM items
--     UNION ALL SELECT 'purchase_receipts', COUNT(*) FROM purchase_receipts
--     UNION ALL SELECT 'sales_deliveries', COUNT(*) FROM sales_deliveries
--     UNION ALL SELECT 'tax_invoices', COUNT(*) FROM tax_invoices
--     UNION ALL SELECT 'stock_ledger', COUNT(*) FROM stock_ledger
--     UNION ALL SELECT 'journal_entries', COUNT(*) FROM journal_entries
--     UNION ALL SELECT 'tenants', COUNT(*) FROM tenants
--     UNION ALL SELECT 'users', COUNT(*) FROM users
--     UNION ALL SELECT 'accounts', COUNT(*) FROM accounts
--     UNION ALL SELECT 'warehouses', COUNT(*) FROM warehouses;
-- =========================================================================
