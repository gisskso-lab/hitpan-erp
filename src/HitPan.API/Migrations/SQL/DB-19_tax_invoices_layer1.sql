-- =========================================================================
-- DB-19: tax_invoices (3계층 세금계산서 1계층 — 내부 마킹)
-- 작업지시서: 20260425작2
-- 발행: 2026-04-25 PM 닥터스트레인지
-- 결재 라인: PM → 어벤져스 → DV-S → DV-D → BK → CTO → 사장님
-- 적용 전: hitpan_erp DB에서 검증 → 사장님 승인 후 운영 적용
-- =========================================================================

-- §절대원칙 #13 DESCRIBE 결과 (4/25 PM 직접 확인):
--   sales_deliveries:
--     delivery_id varchar(36) NOT NULL PK
--     tenant_id varchar(36) NOT NULL
--     delivery_no, partner_id, employee_id, delivery_date, source_type,
--     status (longtext, draft|confirmed|cancelled),
--     total_amount decimal(15,2), vat_amount decimal(15,2),
--     created_at/updated_at datetime(6)
--     UNIQUE (tenant_id, delivery_no)
-- → 작지서 작2의 'BIGINT UNSIGNED' 표현은 부정확. 실 스키마 varchar(36) 사용.
-- → FK는 sales_deliveries(delivery_id)로 연결.

-- DESIGN_PRINCIPLES §7 (3계층 세금계산서):
--   1계층 = 내부 마킹 (본 테이블) — 거래명세서 → 계산서 발행 버튼
--   2계층 = 전자세금계산서 즉시발행 (etax_status = issued)
--   3계층 = 전자세금계산서 예약발행 (etax_status = pending + scheduled_at)
-- 본 라운드는 1계층 컬럼만 활성, etax_status는 placeholder.

CREATE TABLE IF NOT EXISTS tax_invoices (
  invoice_id      VARCHAR(36)   NOT NULL,
  tenant_id       VARCHAR(36)   NOT NULL,
  delivery_id     VARCHAR(36)   NOT NULL COMMENT '거래명세서 FK (sales_deliveries.delivery_id)',
  invoice_no      VARCHAR(32)   NOT NULL COMMENT '계산서 번호 (테넌트별 고유)',
  issued_at       DATETIME(6)   NOT NULL,
  issued_by       VARCHAR(36)   NOT NULL COMMENT '발행자 user_id (감사 추적)',
  amount_total    DECIMAL(15,2) NOT NULL COMMENT '공급가액',
  vat_total       DECIMAL(15,2) NOT NULL COMMENT '부가세',
  status          VARCHAR(16)   NOT NULL DEFAULT 'issued' COMMENT 'issued | canceled',
  -- 2/3계층 placeholder (전자세금계산서 단계, 별도 라운드 구현)
  etax_status     VARCHAR(16)   NOT NULL DEFAULT 'pending' COMMENT 'pending | issued | failed',
  etax_issued_at  DATETIME(6)   NULL,
  -- 멱등 추적용 (작4 P0-4 idempotency_keys와 별개. 이건 비즈니스 멱등)
  idempotency_key VARCHAR(64)   NULL COMMENT '발행 요청 시 사용된 Idempotency-Key 헤더 값',
  created_at      DATETIME(6)   NOT NULL DEFAULT NOW(6),
  updated_at      DATETIME(6)   NOT NULL DEFAULT NOW(6) ON UPDATE NOW(6),
  PRIMARY KEY (invoice_id),
  UNIQUE KEY uk_tax_invoices_delivery (delivery_id) COMMENT '한 거래명세서당 1계산서 (취소 후 재발행은 status=canceled + 신규 invoice)',
  UNIQUE KEY uk_tax_invoices_invoice_no (tenant_id, invoice_no),
  KEY idx_tax_invoices_tenant_issued (tenant_id, issued_at),
  KEY idx_tax_invoices_etax_status (tenant_id, etax_status) COMMENT '2/3계층 일괄 발행 조회용',
  CONSTRAINT fk_tax_invoices_delivery
    FOREIGN KEY (delivery_id) REFERENCES sales_deliveries(delivery_id)
    ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='3계층 세금계산서 1계층 — 내부 마킹 (DESIGN_PRINCIPLES §7)';

-- 취소 정책 (§절대원칙 #3 INSERT ONLY 원장과의 관계):
--   tax_invoices는 원장(stock_ledger/journal_lines)이 아니므로 status='canceled' UPDATE 허용.
--   단, 취소 시 별도 역분개 + monthly_summary 차감은 호출자 책임 (작2 §6.2 참조).
--   uk_tax_invoices_delivery로 인해 '취소 후 재발행' 시 기존 row를 canceled로 두고 새 invoice_id 발급.
--   → uk가 (delivery_id, status) 복합으로 가야 했지만 1차는 단순화. 재발행 시나리오는 별도 작지서.

-- =========================================================================
-- 롤백 (위급 시):
-- DROP TABLE IF EXISTS tax_invoices;
-- =========================================================================
