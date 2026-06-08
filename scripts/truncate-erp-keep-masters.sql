-- 사장님 결재 2026-06-08: 마스터 계정·시스템 마스터 외 ERP 운영 데이터 전체 초기화
-- 보존:
--   users(5), common_codes(21), local_company(2), platform_admins(1), platforms(1),
--   bo_permissions(10), approval_settings(5), billing_settings(1), backup_settings(2),
--   email_settings(1), form_templates(0), departments(0), positions(0),
--   __efmigrationshistory(2)

SET FOREIGN_KEY_CHECKS=0;

-- 회계·원장·전표 영역
TRUNCATE TABLE journal_lines;
TRUNCATE TABLE journal_entries;
TRUNCATE TABLE ledger_balance_snapshot;
TRUNCATE TABLE monthly_closing;
TRUNCATE TABLE monthly_summary;
TRUNCATE TABLE monthly_summary_sources;
TRUNCATE TABLE partner_balance;
TRUNCATE TABLE accounts;

-- 거래처·상품 영역
TRUNCATE TABLE partners;
TRUNCATE TABLE partner_contacts;
TRUNCATE TABLE partner_special_prices;
TRUNCATE TABLE items;
TRUNCATE TABLE item_groups;
TRUNCATE TABLE item_special_prices;
TRUNCATE TABLE item_specs;
TRUNCATE TABLE item_stock;
TRUNCATE TABLE material_price_history;
TRUNCATE TABLE inventory_lots;
TRUNCATE TABLE bom_headers;
TRUNCATE TABLE bom_items;
TRUNCATE TABLE bom_cost_cache;
TRUNCATE TABLE custom_order_specs;
TRUNCATE TABLE mold_assets;
TRUNCATE TABLE mold_production_log;

-- 매입·매출·견적 영역
TRUNCATE TABLE quotations;
TRUNCATE TABLE quotation_items;
TRUNCATE TABLE purchase_orders;
TRUNCATE TABLE purchase_order_items;
TRUNCATE TABLE purchase_receipts;
TRUNCATE TABLE purchase_receipt_items;
TRUNCATE TABLE purchase_returns;
TRUNCATE TABLE purchase_return_items;
TRUNCATE TABLE sales_orders;
TRUNCATE TABLE sales_order_items;
TRUNCATE TABLE sales_returns;
TRUNCATE TABLE sales_return_items;
TRUNCATE TABLE tax_invoices;
TRUNCATE TABLE tax_invoice_items;

-- 수금·결제·은행 영역
TRUNCATE TABLE collections;
TRUNCATE TABLE payments;
TRUNCATE TABLE bills;
TRUNCATE TABLE bank_transactions;
TRUNCATE TABLE cashbook;
TRUNCATE TABLE card_payments;
TRUNCATE TABLE card_payment_lines;
TRUNCATE TABLE expenses;
TRUNCATE TABLE hr_expense_requests;

-- 결재 영역
TRUNCATE TABLE approval_documents;
TRUNCATE TABLE approval_doc_lines;
TRUNCATE TABLE approval_lines;
TRUNCATE TABLE approval_line_steps;
TRUNCATE TABLE approval_history;

-- 직원·인사 영역
TRUNCATE TABLE employees;
TRUNCATE TABLE attendance;
TRUNCATE TABLE overtime;
TRUNCATE TABLE leave_requests;
TRUNCATE TABLE labor_contracts;
TRUNCATE TABLE evaluations;

-- 시리얼·기기·인증 영역
TRUNCATE TABLE local_subscription;
TRUNCATE TABLE device_login_logs;
TRUNCATE TABLE login_attempts;
TRUNCATE TABLE refresh_tokens;
TRUNCATE TABLE backoffice_refresh_tokens;
TRUNCATE TABLE esign_records;
TRUNCATE TABLE tenant_certificates;

-- 로그·감사·이벤트 영역
TRUNCATE TABLE audit_logs;
TRUNCATE TABLE audit_trail;
TRUNCATE TABLE force_edit_logs;
TRUNCATE TABLE events;
TRUNCATE TABLE haccp_logs;
TRUNCATE TABLE delivery_tracking;
TRUNCATE TABLE document_conversions;

-- 이메일·전자세금계산서 송수신 영역
TRUNCATE TABLE email_send_history;
TRUNCATE TABLE email_attachment_history;
TRUNCATE TABLE etax_send_history;

-- AI·챗봇 영역
TRUNCATE TABLE ai_conversations;
TRUNCATE TABLE ai_usage_logs;
TRUNCATE TABLE hitpan_knowledge;

-- 백업·복원 영역
TRUNCATE TABLE backup_history;

-- 청구·구독 영역
TRUNCATE TABLE billing_invoices;
TRUNCATE TABLE billing_payment_attempts;
TRUNCATE TABLE billing_payment_methods;
TRUNCATE TABLE billing_subscriptions;

-- 마이그·시리얼 검증 영역
TRUNCATE TABLE migration_checkpoints;
TRUNCATE TABLE migration_errors;
TRUNCATE TABLE migration_jobs;
TRUNCATE TABLE idempotency_keys;
TRUNCATE TABLE beta_signups;

-- ERP 내부 백오피스 미러 영역 (로컬 캐시)
TRUNCATE TABLE landing_signups;
TRUNCATE TABLE reseller_accounts;
TRUNCATE TABLE reseller_applications;
TRUNCATE TABLE reseller_commission_policies;
TRUNCATE TABLE commission_settlements;

SET FOREIGN_KEY_CHECKS=1;

SELECT '=== TRUNCATE 완료 ===' status;

-- 보존 영역 검증
SELECT 'users' t, COUNT(*) c FROM users
UNION ALL SELECT 'common_codes', COUNT(*) FROM common_codes
UNION ALL SELECT 'local_company', COUNT(*) FROM local_company
UNION ALL SELECT 'platform_admins', COUNT(*) FROM platform_admins
UNION ALL SELECT 'bo_permissions', COUNT(*) FROM bo_permissions
UNION ALL SELECT 'approval_settings', COUNT(*) FROM approval_settings;

-- 운영 영역 영(0)건 검증
SELECT 'partners' t, COUNT(*) c FROM partners
UNION ALL SELECT 'items', COUNT(*) FROM items
UNION ALL SELECT 'sales_invoices', COUNT(*) FROM sales_invoices
UNION ALL SELECT 'journal_lines', COUNT(*) FROM journal_lines
UNION ALL SELECT 'local_subscription', COUNT(*) FROM local_subscription
UNION ALL SELECT 'employees', COUNT(*) FROM employees;
