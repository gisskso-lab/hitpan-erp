-- =============================================================
-- DB-76: items.spec → item_specs 시드 마이그 + 동기화 정책 박제
-- (사장님 작업지시 2026-05-31 항목 4)
--
-- 동기화 정책 (헌법 박제):
-- - items.spec = "대표 규격 1개" (호환 유지, 기존 코드 안 끊김)
-- - item_specs = "콤보박스 옵션 N개" (그리드 콤보, is_default 1개가 items.spec과 동일)
-- - 상품마스터 화면 items.spec 변경 시 → 동일값 item_specs(is_default=1)에 자동 upsert 가도
-- - item_specs(is_default=1) 변경 시 → items.spec에 자동 sync 가도
-- - Service 계층 (ItemService·ItemSpecService) 양방향 동기화 책무
--
-- 본 SQL은 1회성 시드만 박제 (운영자 결재 후 실행).
-- 정책 자체는 Service 코드 박제로 보장.
-- =============================================================

-- 시드 1: items.spec 존재 + item_specs 미존재 → 신규 default 박제
INSERT INTO item_specs (spec_id, tenant_id, item_id, spec_value, display_order, is_default, is_active)
SELECT
    UUID() AS spec_id,
    i.tenant_id,
    i.item_id,
    i.spec AS spec_value,
    0 AS display_order,
    1 AS is_default,
    1 AS is_active
FROM items i
WHERE i.spec IS NOT NULL
  AND TRIM(i.spec) <> ''
  AND NOT EXISTS (
      SELECT 1 FROM item_specs s
      WHERE s.tenant_id = i.tenant_id
        AND s.item_id = i.item_id
        AND s.spec_value = i.spec
  );

-- 시드 2: items.spec ↔ item_specs default 정합성 점검 쿼리 (조회용)
-- 실행 후 결과 0건이 정합. 0건 아니면 운영자 결재 필요.
-- SELECT i.tenant_id, i.item_id, i.spec AS items_spec, s.spec_value AS default_spec_value
-- FROM items i
-- LEFT JOIN item_specs s ON s.tenant_id = i.tenant_id AND s.item_id = i.item_id AND s.is_default = 1 AND s.is_active = 1
-- WHERE (i.spec IS NOT NULL AND TRIM(i.spec) <> '')
--   AND (s.spec_value IS NULL OR s.spec_value <> i.spec);
