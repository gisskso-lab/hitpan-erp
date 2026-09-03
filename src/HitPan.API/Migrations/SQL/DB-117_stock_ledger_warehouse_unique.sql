-- ═══════════════════════════════════════════════════════════════════════════
-- DB-117 · stock_ledger UNIQUE 키에 warehouse_id 추가 (20260903작19 W2)
-- ═══════════════════════════════════════════════════════════════════════════
--
-- 사장님 오더 (2026-09-03): "재고가 없으면 출고를 막아야 정상이지" → C안 확정
--   C = 회사 합산으로 판매는 허용하되(4/26 헌법), 있는 창고에서 자동 배분해
--       창고별 음수를 없앤다.
--
-- 🔴 왜 이 DDL 이 필요한가
--   현행 키:  uq_stock_ledger_source (tenant_id, source_type, source_id, item_id, move_type)
--   ⇒ 한 전표의 한 품목은 원장에 **한 줄만** 허용된다.
--     5개를 A창고 3 + B창고 2 로 나눠 빼면 두 줄이 필요한데 키에 걸려
--     **거래 전체가 롤백**된다 = "판매했는데 재고 안 빠짐"(헌법 #20).
--
--   🟢 게이트로 실증했다 (WarehousePickingGateTests.GP4):
--      Duplicate entry 'GATE-PICK903-sales_delivery-DELIV-SPLIT-1-ITEM-PICK-out'
--        for key 'uq_stock_ledger_source'
--      추측이 아니라 실제 에러다.
--
-- 🔴 6/23 에 이 키 때문에 "품목 합산"으로 우회했던 자리다 (SalesService.cs:464)
--      "UNIQUE 위반 → 거래 전체 롤백. 표준 데모는 통과하나 실사용 첫날 터지는 잠복형."
--    작19 는 그 우회를 되돌리므로, 우회 대신 **키를 정직하게 넓힌다.**
--
-- 🟢 마이그레이션 멱등성은 깨지지 않는다 (헌법 #26 확인)
--    · 마이그는 원장 창고를 'wh-migration' 하나로 고정한다 (MdbMigrationService.cs:226)
--      ⇒ 키에 warehouse_id 가 붙어도 값이 항상 같아 INSERT IGNORE 가 그대로 듣는다
--    · 마이그의 item_stock 생성은 이미 창고별 GROUP BY 다 (:490) — 창고축이 이미 살아 있다
--
-- ⚠️ 대안(기각): source_id 에 창고를 접어 넣기({returnId}:{whId})
--    → source_id 로 원전표를 되찾는 코드가 여러 곳에 있다(DB-116 소급복구가 그렇게 짜였다).
--      문자열을 오염시키면 그 조회가 전부 깨진다. 키를 넓히는 쪽이 맞다.
--
-- 🟢 되돌릴 수 있다 — 키를 좁히는 역방향 DDL 이 아래 주석에 있다.
--    단 좁히기 전에 창고가 갈린 행을 합산해야 하므로, 되돌림은 데이터 정리가 선행된다.
--
-- 영향: 인덱스 1건 재생성. 데이터 변경 0건. 컬럼 추가·삭제 0건.
-- ═══════════════════════════════════════════════════════════════════════════

-- ── 1) 사전 확인: 새 키로 충돌하는 행이 있는가 ──
--    키가 **넓어지므로** 충돌은 원리상 안 난다(좁은 키를 만족했으면 넓은 키도 만족).
--    그래도 확인하고 넘어간다 — "원리상 안 난다"는 실측이 아니다.
--    아래 SELECT 가 0 이 아니면 이 마이그를 중단하고 원인을 먼저 규명한다.
SELECT COUNT(*) AS conflict_rows
  FROM (
    SELECT tenant_id, source_type, source_id, item_id, move_type, warehouse_id, COUNT(*) c
      FROM stock_ledger
     GROUP BY tenant_id, source_type, source_id, item_id, move_type, warehouse_id
    HAVING c > 1
  ) t;

-- ── 2) 키 교체 ──
--    🔴 DROP 과 ADD 를 한 문장에 둔다. 나누면 그 사이에 중복이 들어올 수 있다.
ALTER TABLE stock_ledger
  DROP INDEX uq_stock_ledger_source,
  ADD UNIQUE KEY uq_stock_ledger_source
      (tenant_id, source_type, source_id, item_id, move_type, warehouse_id);

-- ── 되돌리기 (참고 · 실행하지 않는다) ──
-- 🔴 좁히기 전에 창고가 갈린 행을 품목 단위로 합산해야 한다. 그 정리 없이 아래를 돌리면
--    Duplicate entry 로 실패한다.
--
-- ALTER TABLE stock_ledger
--   DROP INDEX uq_stock_ledger_source,
--   ADD UNIQUE KEY uq_stock_ledger_source
--       (tenant_id, source_type, source_id, item_id, move_type);
