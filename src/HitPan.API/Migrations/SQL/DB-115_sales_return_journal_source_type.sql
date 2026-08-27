-- DB-115 — 매출반품 분개에 자기 이름표를 준다 (20260828작12)
--
-- 사장님 결재 2026-08-28 ("모두결재") — [1-V] 선행검증 판정에 따른 봉합.
-- 선행검증서: docs/검증/선행/20260828_선행검증서_매출반품_회계읽는쪽.md
--
-- ============================================================================
-- 🔴 무엇이 문제였나 — 분개는 맞았는데 «이름표»가 남의 것이었다
--
--   매출반품 확정(SalesService.cs:2487)은 회계 분개를 정상으로 만들고 있었다.
--   금액도 방향도 맞다: 차변 매출 + 부가세예수금 / 대변 외상매출금.
--
--   그런데 자기 source_type 이 없어서 «명세서 취소» 키를 빌려 썼다.
--
--     매입:  purchase_return      + purchase_return_cancel   ← 둘 다 있다
--     매출:  (없음)               + sales_return_cancel      ← 「반품」 키가 없다
--
--   ⇒ 장부에서 «매출반품»과 «명세서 취소»가 같은 키로 섞여 구분이 불가능했다.
--
-- 🔴 그래서 무슨 일이 생겼나
--
--   FinanceService.cs 「확정전표 기표 누락」 검사는 purchase_return 으로 매입반품을 센다.
--   매출반품은 셀 키가 없으니 검사 항목 자체가 없었다(FinanceService 내 sales_return 낱말 0건).
--   ⇒ 매출반품 기표가 실패해도 아무도 못 잡는다. 매입은 잡힌다. 이 비대칭이 사각이었다.
--
-- ============================================================================
-- 이 마이그가 하는 일 — 과거 분개를 반품 키로 되돌려 붙인다
--
--   과거분은 sales_delivery_cancel 로 쌓여 있다. 그대로 두면 영원히 식별 불가다.
--
--   🟢 안전하게 가려낼 수 있다 — source_id 가 다르기 때문이다.
--        매출반품 확정 : source_id = returnId          (SalesService.cs:2487)
--        명세서 취소   : source_id = '{deliveryId}:cancel' (SalesService.cs:1217 주석)
--      ⇒ sales_returns 에 그 id 가 실재하는 행만 반품이다. 명세서 취소는 절대 안 걸린다.
--
--   ⚠️ 금액·계정·차대는 한 줄도 안 건드린다. journal_lines 무접촉.
--      헌법 #3(원장 INSERT ONLY)에 걸리지 않는다 — 지우거나 다시 넣는 게 아니라
--      journal_entries 의 «분류 라벨»만 바로잡는다.
-- ============================================================================

UPDATE journal_entries je
   SET je.source_type = 'sales_return',
       je.description = REPLACE(je.description, '매출취소 역분개', '매출반품 역분개')
 WHERE je.source_type = 'sales_delivery_cancel'
   AND EXISTS (
        SELECT 1
          FROM sales_returns sr
         WHERE sr.return_id = je.source_id
           AND sr.tenant_id = je.tenant_id
   );

-- 검산용 — 남아 있으면 안 되는 것: sales_returns 에 매칭되는데 아직 옛 키인 분개
--   SELECT COUNT(*) FROM journal_entries je
--    WHERE je.source_type='sales_delivery_cancel'
--      AND EXISTS (SELECT 1 FROM sales_returns sr
--                   WHERE sr.return_id=je.source_id AND sr.tenant_id=je.tenant_id);
--   ⇒ 0 이어야 한다.
