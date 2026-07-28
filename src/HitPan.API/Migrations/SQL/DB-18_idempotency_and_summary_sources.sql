-- =========================================================================
-- DB-18: idempotency_keys + monthly_summary_sources (P0-4)
-- 작업지시서: 20260425작4
-- 발행: 2026-04-25 PM 닥터스트레인지
-- 목적: 멱등 처리 인프라 신설 + monthly_summary 멱등성 보강
-- 결재 라인: PM → 어벤져스 → DV-S → DV-D → BK → CTO → 사장님
-- 적용 전: hitpan_erp DB에서 적용 / 검증 환경에서 1차 검증 후 운영
-- =========================================================================

-- §절대원칙 #13 DESCRIBE 결과 (적용 전 PM 직접 확인 4/25):
--   monthly_summary 컬럼: summary_id, tenant_id, year_month, total_sales,
--     total_purchase, total_receipt, total_payment, last_updated_at, gross_profit
--   UNIQUE: (tenant_id, year_month)
-- → source_id 컬럼 없음. monthly_summary 자체는 그대로 두고
--   별도 추적 테이블(monthly_summary_sources)로 멱등 보장.

-- -------------------------------------------------------------------------
-- A. idempotency_keys — Idempotency-Key 헤더 멱등 처리 (DESIGN_PRINCIPLES §5.3)
-- -------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS idempotency_keys (
  id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  tenant_id       VARCHAR(36)     NOT NULL,
  idempotency_key VARCHAR(64)     NOT NULL,
  endpoint        VARCHAR(128)    NOT NULL COMMENT 'METHOD + path, 예: POST /api/sales/tax-invoices',
  request_hash    CHAR(64)        NOT NULL COMMENT 'SHA-256 of request body — 같은 키+다른 본문 차단',
  status_code     INT             NOT NULL COMMENT '캐시된 HTTP 상태 코드',
  response_body   MEDIUMTEXT      NOT NULL COMMENT '캐시된 응답 JSON (민감 시 액션에서 SkipCacheBody)',
  created_at      DATETIME(6)     NOT NULL DEFAULT NOW(6),
  expires_at      DATETIME(6)     NOT NULL COMMENT 'TTL 24h, IdempotencyCleanupService가 1h 주기 정리',
  PRIMARY KEY (id),
  UNIQUE KEY uk_tenant_key (tenant_id, idempotency_key),
  KEY idx_expires (expires_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Idempotency-Key 헤더 처리 캐시 (DESIGN_PRINCIPLES §5.3)';

-- -------------------------------------------------------------------------
-- B. monthly_summary_sources — monthly_summary 멱등 추적
-- -------------------------------------------------------------------------
-- 사용 패턴 (8곳 코드 수정 시):
--   1) INSERT IGNORE INTO monthly_summary_sources (...) VALUES (...);
--   2) IF ROW_COUNT() = 1 THEN  -- 신규 source면 가산
--        INSERT INTO monthly_summary (...) ON DUPLICATE KEY UPDATE total_xxx = total_xxx + @amount;
--      END IF;
-- → 같은 source 두 번 가산 차단.
CREATE TABLE IF NOT EXISTS monthly_summary_sources (
  id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  tenant_id     VARCHAR(36)     NOT NULL,
  `year_month`  CHAR(6)         NOT NULL,
  source_type   VARCHAR(32)     NOT NULL COMMENT '예: sales_delivery_confirm, purchase_receipt_confirm, purchase_return_confirm',
  source_id     VARCHAR(64)     NOT NULL COMMENT 'delivery_id / receipt_id / return_id 등',
  field_name    VARCHAR(32)     NOT NULL COMMENT 'total_sales | total_purchase | total_receipt | total_payment',
  amount_delta  DECIMAL(15,2)   NOT NULL COMMENT '실제 가산된 금액 (감사 추적용, 음수 = 감산)',
  applied_at    DATETIME(6)     NOT NULL DEFAULT NOW(6),
  PRIMARY KEY (id),
  UNIQUE KEY uk_source (tenant_id, source_type, source_id, field_name),
  KEY idx_tenant_month (tenant_id, `year_month`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='monthly_summary 멱등 추적 — 같은 source의 같은 field 중복 가산 차단';

-- =========================================================================
-- 롤백 (위급 시):
-- DROP TABLE IF EXISTS monthly_summary_sources;
-- DROP TABLE IF EXISTS idempotency_keys;
-- =========================================================================
