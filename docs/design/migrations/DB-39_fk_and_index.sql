-- ============================================================
-- DB-39: FK 무결성 + 라인 테이블 tenant_id 인덱스 추가
-- 작성일: 2026-05-05
-- 근거: 감사팀 전수조사 결과 (DB축) — FK 50+개 누락, 인덱스 11개 누락
--
-- 실행 방법:
--   mysql -u hitpan -p hitpan_erp < DB-39_fk_and_index.sql
--
-- 주의:
--   - 기존 데이터 무결성 검증 후 실행 (孤兒 데이터 있으면 FK 추가 실패)
--   - 운영 DB 적용 전 dev 환경에서 먼저 테스트
--   - 각 ALTER TABLE은 독립적으로 실행 가능 (실패 시 개별 재시도)
-- ============================================================

SET FOREIGN_KEY_CHECKS = 0;

-- ─────────────────────────────────────────────
-- 1. 라인 테이블 tenant_id 인덱스 (11개)
-- ─────────────────────────────────────────────

ALTER TABLE bom_headers
    ADD KEY IF NOT EXISTS idx_tenant (tenant_id);

ALTER TABLE bom_items
    ADD KEY IF NOT EXISTS idx_tenant (tenant_id);

ALTER TABLE work_order_materials
    ADD KEY IF NOT EXISTS idx_tenant (tenant_id);

ALTER TABLE work_order_results
    ADD KEY IF NOT EXISTS idx_tenant (tenant_id);

ALTER TABLE purchase_order_items
    ADD KEY IF NOT EXISTS idx_tenant (tenant_id);

ALTER TABLE purchase_receipt_items
    ADD KEY IF NOT EXISTS idx_tenant (tenant_id);

ALTER TABLE sales_order_items
    ADD KEY IF NOT EXISTS idx_tenant (tenant_id);

ALTER TABLE sales_delivery_items
    ADD KEY IF NOT EXISTS idx_tenant (tenant_id);

ALTER TABLE return_items
    ADD KEY IF NOT EXISTS idx_tenant (tenant_id);

ALTER TABLE tax_invoice_items
    ADD KEY IF NOT EXISTS idx_tenant (tenant_id);

ALTER TABLE payroll_deductions
    ADD KEY IF NOT EXISTS idx_tenant (tenant_id);

-- ─────────────────────────────────────────────
-- 2. FK 무결성 — tenants 참조 (격리 보장)
-- ─────────────────────────────────────────────

ALTER TABLE billing_keys
    ADD CONSTRAINT IF NOT EXISTS fk_billing_keys_tenant
        FOREIGN KEY (tenant_id) REFERENCES tenants (tenant_id) ON DELETE RESTRICT;

ALTER TABLE billing_invoices
    ADD CONSTRAINT IF NOT EXISTS fk_billing_invoices_tenant
        FOREIGN KEY (tenant_id) REFERENCES tenants (tenant_id) ON DELETE RESTRICT;

ALTER TABLE departments
    ADD CONSTRAINT IF NOT EXISTS fk_departments_tenant
        FOREIGN KEY (tenant_id) REFERENCES tenants (tenant_id) ON DELETE RESTRICT;

ALTER TABLE users
    ADD CONSTRAINT IF NOT EXISTS fk_users_tenant
        FOREIGN KEY (tenant_id) REFERENCES tenants (tenant_id) ON DELETE RESTRICT;

ALTER TABLE partners
    ADD CONSTRAINT IF NOT EXISTS fk_partners_tenant
        FOREIGN KEY (tenant_id) REFERENCES tenants (tenant_id) ON DELETE RESTRICT;

ALTER TABLE employees
    ADD CONSTRAINT IF NOT EXISTS fk_employees_tenant
        FOREIGN KEY (tenant_id) REFERENCES tenants (tenant_id) ON DELETE RESTRICT;

ALTER TABLE warehouses
    ADD CONSTRAINT IF NOT EXISTS fk_warehouses_tenant
        FOREIGN KEY (tenant_id) REFERENCES tenants (tenant_id) ON DELETE RESTRICT;

ALTER TABLE bom_headers
    ADD CONSTRAINT IF NOT EXISTS fk_bom_headers_tenant
        FOREIGN KEY (tenant_id) REFERENCES tenants (tenant_id) ON DELETE RESTRICT;

ALTER TABLE purchase_orders
    ADD CONSTRAINT IF NOT EXISTS fk_purchase_orders_tenant
        FOREIGN KEY (tenant_id) REFERENCES tenants (tenant_id) ON DELETE RESTRICT;

ALTER TABLE purchase_receipts
    ADD CONSTRAINT IF NOT EXISTS fk_purchase_receipts_tenant
        FOREIGN KEY (tenant_id) REFERENCES tenants (tenant_id) ON DELETE RESTRICT;

ALTER TABLE sales_orders
    ADD CONSTRAINT IF NOT EXISTS fk_sales_orders_tenant
        FOREIGN KEY (tenant_id) REFERENCES tenants (tenant_id) ON DELETE RESTRICT;

ALTER TABLE sales_deliveries
    ADD CONSTRAINT IF NOT EXISTS fk_sales_deliveries_tenant
        FOREIGN KEY (tenant_id) REFERENCES tenants (tenant_id) ON DELETE RESTRICT;

ALTER TABLE journal_entries
    ADD CONSTRAINT IF NOT EXISTS fk_journal_entries_tenant
        FOREIGN KEY (tenant_id) REFERENCES tenants (tenant_id) ON DELETE RESTRICT;

ALTER TABLE tax_invoices
    ADD CONSTRAINT IF NOT EXISTS fk_tax_invoices_tenant
        FOREIGN KEY (tenant_id) REFERENCES tenants (tenant_id) ON DELETE RESTRICT;

ALTER TABLE payroll_headers
    ADD CONSTRAINT IF NOT EXISTS fk_payroll_headers_tenant
        FOREIGN KEY (tenant_id) REFERENCES tenants (tenant_id) ON DELETE RESTRICT;

-- ─────────────────────────────────────────────
-- 3. FK — 헤더-라인 관계 (원장 무결성)
-- ─────────────────────────────────────────────

ALTER TABLE bom_items
    ADD CONSTRAINT IF NOT EXISTS fk_bom_items_header
        FOREIGN KEY (bom_id) REFERENCES bom_headers (bom_id) ON DELETE CASCADE;

ALTER TABLE purchase_order_items
    ADD CONSTRAINT IF NOT EXISTS fk_po_items_header
        FOREIGN KEY (po_id) REFERENCES purchase_orders (po_id) ON DELETE CASCADE;

ALTER TABLE purchase_receipt_items
    ADD CONSTRAINT IF NOT EXISTS fk_receipt_items_header
        FOREIGN KEY (receipt_id) REFERENCES purchase_receipts (receipt_id) ON DELETE CASCADE;

ALTER TABLE sales_order_items
    ADD CONSTRAINT IF NOT EXISTS fk_so_items_header
        FOREIGN KEY (order_id) REFERENCES sales_orders (order_id) ON DELETE CASCADE;

ALTER TABLE sales_delivery_items
    ADD CONSTRAINT IF NOT EXISTS fk_delivery_items_header
        FOREIGN KEY (delivery_id) REFERENCES sales_deliveries (delivery_id) ON DELETE CASCADE;

ALTER TABLE journal_lines
    ADD CONSTRAINT IF NOT EXISTS fk_journal_lines_entry
        FOREIGN KEY (entry_id) REFERENCES journal_entries (entry_id) ON DELETE CASCADE;

ALTER TABLE payroll_details
    ADD CONSTRAINT IF NOT EXISTS fk_payroll_details_header
        FOREIGN KEY (payroll_id) REFERENCES payroll_headers (payroll_id) ON DELETE CASCADE;

-- ─────────────────────────────────────────────
-- 4. FK — 거래처·파트너 참조
-- ─────────────────────────────────────────────

ALTER TABLE purchase_orders
    ADD CONSTRAINT IF NOT EXISTS fk_purchase_orders_partner
        FOREIGN KEY (partner_id) REFERENCES partners (partner_id) ON DELETE RESTRICT;

ALTER TABLE purchase_receipts
    ADD CONSTRAINT IF NOT EXISTS fk_purchase_receipts_partner
        FOREIGN KEY (partner_id) REFERENCES partners (partner_id) ON DELETE RESTRICT;

ALTER TABLE sales_orders
    ADD CONSTRAINT IF NOT EXISTS fk_sales_orders_partner
        FOREIGN KEY (partner_id) REFERENCES partners (partner_id) ON DELETE RESTRICT;

ALTER TABLE sales_deliveries
    ADD CONSTRAINT IF NOT EXISTS fk_sales_deliveries_partner
        FOREIGN KEY (partner_id) REFERENCES partners (partner_id) ON DELETE RESTRICT;

SET FOREIGN_KEY_CHECKS = 1;

-- ─────────────────────────────────────────────
-- 완료 확인 쿼리
-- ─────────────────────────────────────────────
-- SELECT TABLE_NAME, CONSTRAINT_NAME, CONSTRAINT_TYPE
--   FROM information_schema.TABLE_CONSTRAINTS
--  WHERE TABLE_SCHEMA = 'hitpan_erp'
--    AND CONSTRAINT_TYPE = 'FOREIGN KEY'
--  ORDER BY TABLE_NAME;
