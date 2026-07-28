-- =========================================================================
-- DB-20: sales_deliveries.tax_invoice_id 역참조 컬럼 추가
-- 작업지시서: 20260425작5
-- 발행: 2026-04-25 PM 닥터스트레인지 (검증팀 BK #1 발견 → 즉시 처리)
-- 결재 라인: PM → 어벤져스 → DV-S → DV-D → BK → CTO → 사장님
-- 적용 전: hitpan_erp DB에서 검증 → 사장님 승인 후 운영
-- =========================================================================

-- §절대원칙 #13 DESCRIBE 결과 (PM 4/25 직접 확인):
--   sales_deliveries: delivery_id, tenant_id, delivery_no, partner_id,
--     employee_id, delivery_date, source_type, status, total_amount,
--     vat_amount, memo, created_at/by, updated_at/by
-- → tax_invoice_id 컬럼 없음. 본 마이그레이션으로 추가.
--
-- DESIGN_PRINCIPLES §6 무결성:
--   "고객이 화면에서 누른 대로 데이터가 끝까지 흐르는가" — 발행 결과가
--   거래명세서 화면에 안 보이면 무결성 깨진 것 (BK #1 검증).

ALTER TABLE sales_deliveries
  ADD COLUMN tax_invoice_id VARCHAR(36) NULL
    COMMENT '발행된 세금계산서 ID (tax_invoices.invoice_id 역참조). 미발행 시 NULL. 취소 시 NULL로 환원.';

-- 조회 성능 인덱스 (거래명세서 화면에서 발행 여부 표시용)
ALTER TABLE sales_deliveries
  ADD INDEX idx_sales_deliveries_tax_invoice (tax_invoice_id);

-- ※ FK 미설정 사유:
--    tax_invoices.delivery_id가 이미 sales_deliveries(delivery_id) FK ON DELETE RESTRICT로 묶여 있다.
--    여기서 sales_deliveries → tax_invoices 양방향 FK를 걸면 순환 참조 발생.
--    역참조는 비강제 (애플리케이션 트랜잭션으로 정합성 보장 — TaxInvoiceService.IssueAsync UoW).

-- =========================================================================
-- 롤백 (위급 시):
-- ALTER TABLE sales_deliveries DROP INDEX idx_sales_deliveries_tax_invoice;
-- ALTER TABLE sales_deliveries DROP COLUMN tax_invoice_id;
-- =========================================================================
