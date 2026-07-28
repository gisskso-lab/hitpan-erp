-- DB-37: stock_ledger 중복 기입 방지 UNIQUE 제약 (RED-2 보강)
-- 동일 tenant + source_type + source_id + item_id + move_type 조합의 중복 원장 차단.
-- 실행 전: 기존 중복 행을 제거한 뒤 제약을 추가한다 (중복 시 ALTER 실패 방지).

-- STEP 1: 중복 행 제거 (ledger_id가 작은 쪽 보존, 나머지 삭제)
DELETE sl
FROM stock_ledger sl
INNER JOIN (
    SELECT MIN(ledger_id) AS keep_id,
           tenant_id, source_type, source_id, item_id, move_type
    FROM stock_ledger
    GROUP BY tenant_id, source_type, source_id, item_id, move_type
    HAVING COUNT(*) > 1
) dup ON sl.tenant_id   = dup.tenant_id
     AND sl.source_type = dup.source_type
     AND sl.source_id   = dup.source_id
     AND sl.item_id     = dup.item_id
     AND sl.move_type   = dup.move_type
     AND sl.ledger_id  <> dup.keep_id;

-- STEP 2: UNIQUE 제약 추가
ALTER TABLE stock_ledger
    ADD CONSTRAINT uq_stock_ledger_source
        UNIQUE (tenant_id, source_type, source_id, item_id, move_type);
