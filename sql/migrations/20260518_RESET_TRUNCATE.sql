-- 5/18 사장님 결재 A안: 마이그 영역 TRUNCATE (DB 초기화 후 6차 마이그)
-- 백업: backups/before_reset_20260518_v3.sql (753MB)
-- 보존: tenants/users/refresh_tokens/warehouses/계정·권한·시스템 메타

SET FOREIGN_KEY_CHECKS=0;

-- ── 거래 헤더+라인 (자식 → 부모 순) ──
TRUNCATE TABLE sales_delivery_items;
TRUNCATE TABLE sales_deliveries;
TRUNCATE TABLE purchase_receipt_items;
TRUNCATE TABLE purchase_receipts;
TRUNCATE TABLE sales_return_items;
TRUNCATE TABLE sales_returns;
TRUNCATE TABLE purchase_return_items;
TRUNCATE TABLE purchase_returns;
TRUNCATE TABLE sales_order_items;
TRUNCATE TABLE sales_orders;
TRUNCATE TABLE purchase_order_items;
TRUNCATE TABLE purchase_orders;
TRUNCATE TABLE quotation_items;
TRUNCATE TABLE quotations;
TRUNCATE TABLE tax_invoice_items;
TRUNCATE TABLE tax_invoices;
TRUNCATE TABLE card_payment_lines;
TRUNCATE TABLE card_payments;

-- ── 원장 (INSERT ONLY) ──
TRUNCATE TABLE stock_ledger;
TRUNCATE TABLE collections;
TRUNCATE TABLE cashbook;
TRUNCATE TABLE expenses;
TRUNCATE TABLE bank_transactions;
TRUNCATE TABLE bills;
TRUNCATE TABLE journal_lines;
TRUNCATE TABLE journal_entries;

-- ── 마스터 (마이그 매핑 초기화) ──
TRUNCATE TABLE accounts;
TRUNCATE TABLE bom_items;
TRUNCATE TABLE bom_headers;
TRUNCATE TABLE bom_cost_cache;
TRUNCATE TABLE items;
TRUNCATE TABLE item_groups;
TRUNCATE TABLE item_special_prices;
TRUNCATE TABLE partner_contacts;
TRUNCATE TABLE partner_special_prices;
TRUNCATE TABLE partner_balance;
TRUNCATE TABLE partners;
TRUNCATE TABLE employees;

-- ── 마이그 메타 (재실행 위해 reset) ──
TRUNCATE TABLE migration_errors;
TRUNCATE TABLE migration_checkpoints;
TRUNCATE TABLE migration_jobs;

-- ── 집계·요약 (재계산 필요) ──
TRUNCATE TABLE monthly_summary;
TRUNCATE TABLE monthly_summary_sources;
TRUNCATE TABLE monthly_closing;
TRUNCATE TABLE ledger_balance_snapshot;
TRUNCATE TABLE item_stock;
TRUNCATE TABLE inventory_lots;
TRUNCATE TABLE stock_adjust_logs;
TRUNCATE TABLE stock_alerts;

-- ── 보조 (마이그 흐름 잔재) ──
TRUNCATE TABLE delivery_tracking;
TRUNCATE TABLE service_tickets;
TRUNCATE TABLE events;
TRUNCATE TABLE document_conversions;
TRUNCATE TABLE force_edit_logs;

-- ── 결재·승인 (마이그 데이터 의존 잔재) ──
TRUNCATE TABLE approval_doc_lines;
TRUNCATE TABLE approval_documents;
TRUNCATE TABLE approval_history;

-- ── 보존 (TRUNCATE 안 함) ──
-- tenants, users, refresh_tokens, backoffice_refresh_tokens
-- warehouses (wh-migration 등)
-- platforms, platform_admins
-- resellers, reseller_* (대리점)
-- subscriptions, billing_*
-- tenant_settings, tenant_certificates, tenant_devices, tenant_etax_settings
-- workflow_settings, approval_settings, approval_lines, approval_line_steps
-- common_codes, departments, positions, user_permissions, user_sessions
-- backup_history, backup_settings
-- audit_logs, audit_trail, login_attempts, device_login_logs
-- email_settings, email_send_history, email_attachment_history, etax_send_history
-- billing_subscriptions, billing_settings, billing_payment_methods, billing_payment_attempts, billing_invoices
-- ai_conversations, ai_usage_logs, hitpan_knowledge
-- idempotency_keys, security_alerts
-- 기타 인프라/플랫폼 테이블

SET FOREIGN_KEY_CHECKS=1;

-- 검증 쿼리
SELECT 'sales_deliveries' AS t, COUNT(*) c FROM sales_deliveries
UNION ALL SELECT 'purchase_receipts', COUNT(*) FROM purchase_receipts
UNION ALL SELECT 'journal_entries', COUNT(*) FROM journal_entries
UNION ALL SELECT 'accounts', COUNT(*) FROM accounts
UNION ALL SELECT 'tax_invoices', COUNT(*) FROM tax_invoices
UNION ALL SELECT 'collections', COUNT(*) FROM collections
UNION ALL SELECT 'partners', COUNT(*) FROM partners
UNION ALL SELECT 'items', COUNT(*) FROM items
UNION ALL SELECT 'employees', COUNT(*) FROM employees
UNION ALL SELECT 'tenants_preserved', COUNT(*) FROM tenants
UNION ALL SELECT 'users_preserved', COUNT(*) FROM users
UNION ALL SELECT 'warehouses_preserved', COUNT(*) FROM warehouses;
