-- =============================================================
-- DB-73: 상품 규격 1:N 테이블 (사장님 작업지시 2026-05-31)
-- - items.spec (단일 컬럼) 호환 유지 + item_specs 다중 규격 박제
-- - 그리드(수주·발주·거래명세서·매입·견적·반품) 품명 선택 시 콤보박스 옵션
-- - 규격 없는 품명 = 콤보박스 공란 + 직접 입력 허용 (DB 미저장 임시값 OK)
-- 절대원칙: tenant_id JWT 클레임 / SOFT DELETE (is_active=0)
-- =============================================================

CREATE TABLE IF NOT EXISTS item_specs (
    spec_id CHAR(36) NOT NULL PRIMARY KEY COMMENT 'UUID',
    tenant_id CHAR(36) NOT NULL,
    item_id CHAR(36) NOT NULL,
    spec_value VARCHAR(100) NOT NULL COMMENT '예: 100×200×3mm, 1.0T, M8×30',
    display_order INT NOT NULL DEFAULT 0 COMMENT '콤보박스 정렬 순서',
    is_default TINYINT(1) NOT NULL DEFAULT 0 COMMENT '1=신규 라인 기본 선택',
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    UNIQUE KEY uk_item_specs_value (tenant_id, item_id, spec_value),
    INDEX idx_item_specs_item (tenant_id, item_id, is_active, display_order)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='상품 규격 1:N (그리드 콤보박스 옵션)';

-- items.spec 기존 단일값을 item_specs로 시드 (선택 — 운영 데이터 보존)
-- 실행 시점은 운영자 결재 후 별도 (마이그 #N로 추후 박제)
-- INSERT INTO item_specs (spec_id, tenant_id, item_id, spec_value, display_order, is_default)
-- SELECT UUID(), tenant_id, item_id, spec, 0, 1
-- FROM items
-- WHERE spec IS NOT NULL AND spec <> ''
--   AND NOT EXISTS (
--     SELECT 1 FROM item_specs s WHERE s.tenant_id = items.tenant_id AND s.item_id = items.item_id AND s.spec_value = items.spec
--   );
