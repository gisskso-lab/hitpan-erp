-- ============================================================
-- DB-116 — 매출반품 사슬 소급 복구 (20260903작17)
--
-- 🔴 사장님 오더: *"이미 끊긴 전표도 반영되도록 해"*
--
-- [무엇이 끊겼나]
--   실측(2026-09-03): 매출반품 4건 중 1건이 delivery_id = NULL 이었다.
--     반-20260903-001  delivery_id = dcc211c8-…   🟢 연결
--     반-20260903-002  delivery_id = NULL          🔴 끊김
--   그런데 **둘 다 메모가 같았다**: "반품 : 명-20260825-001"
--
-- [왜 끊겼나]
--   화면이 저장할 때 원전표 id 를 **주소창 파라미터(?returnOf=)에서 직접 읽었다.**
--   주소가 한 번이라도 갈리면(재렌더·뒤로가기·두 번째 저장) 조용히 NULL 이 된다.
--   ⇒ 코드는 20260903작17 에서 **상태로 붙잡도록** 고쳤다. 이 마이그는 **이미 샌 것**을 되돌린다.
--
-- [무엇을 근거로 되돌리나]
--   🟢 메모에 원전표 번호가 글자로 남아 있다 — "반품 : 명-YYYYMMDD-NNN".
--   그 번호로 sales_deliveries 를 찾아 delivery_id 를 채운다.
--   ⚠️ **추측이 아니다.** 담당자가 [반품하기]로 그 전표에서 들어왔다는 기록이다.
--
-- [안전장치]
--   · delivery_id 가 NULL 인 행만 건드린다 (이미 연결된 것은 무접촉)
--   · 같은 테넌트 · 같은 거래처인 명세서만 매칭 (엉뚱한 전표에 붙는 것 차단)
--   · 번호가 정확히 하나로 특정될 때만 UPDATE (둘 이상이면 건너뛴다)
--   · 금액·수량·상태는 **한 글자도 안 바꾼다** — 링크만 채운다
--   · 원장(stock_ledger·journal_lines) 무접촉 (헌법 #3 INSERT ONLY)
--
-- 멱등: 두 번 돌려도 안전하다 (이미 채워진 행은 조건에서 빠진다).
-- ============================================================

-- ── 사전 진단: 무엇이 끊겨 있고, 무엇을 되돌릴 수 있나
SELECT '[진단] 끊긴 반품과 복구 가능 여부' AS info;
SELECT sr.return_no                                   AS 반품번호,
       sr.memo                                        AS 메모,
       SUBSTRING_INDEX(TRIM(sr.memo), ' ', -1)        AS 추출번호,
       (SELECT COUNT(*) FROM sales_deliveries d
         WHERE d.tenant_id  = sr.tenant_id
           AND d.partner_id = sr.partner_id
           AND d.delivery_no = SUBSTRING_INDEX(TRIM(sr.memo), ' ', -1)
           AND d.is_deleted = 0)                      AS 후보수,
       CASE
         WHEN (SELECT COUNT(*) FROM sales_deliveries d
                WHERE d.tenant_id  = sr.tenant_id
                  AND d.partner_id = sr.partner_id
                  AND d.delivery_no = SUBSTRING_INDEX(TRIM(sr.memo), ' ', -1)
                  AND d.is_deleted = 0) = 1
         THEN '복구 가능'
         ELSE '수동 확인 필요'
       END                                            AS 판정
  FROM sales_returns sr
 WHERE sr.delivery_id IS NULL
   AND sr.is_deleted = 0;

-- ── 복구: 메모의 원전표 번호로 링크를 채운다
UPDATE sales_returns sr
   SET sr.delivery_id = (
         SELECT d.delivery_id
           FROM sales_deliveries d
          WHERE d.tenant_id   = sr.tenant_id
            AND d.partner_id  = sr.partner_id
            AND d.delivery_no = SUBSTRING_INDEX(TRIM(sr.memo), ' ', -1)
            AND d.is_deleted  = 0
          LIMIT 1)
 WHERE sr.delivery_id IS NULL
   AND sr.is_deleted  = 0
   AND sr.memo LIKE '반품 : %'
   -- 🔴 정확히 하나로 특정될 때만. 둘 이상이면 사람이 판단한다.
   AND (SELECT COUNT(*)
          FROM sales_deliveries d
         WHERE d.tenant_id   = sr.tenant_id
           AND d.partner_id  = sr.partner_id
           AND d.delivery_no = SUBSTRING_INDEX(TRIM(sr.memo), ' ', -1)
           AND d.is_deleted  = 0) = 1;

-- ── 사후 확인: 남은 끊김이 있는가
SELECT '[사후] 아직 끊긴 반품' AS info;
SELECT sr.return_no AS 반품번호,
       sr.memo      AS 메모,
       '수동 확인 필요 — 메모에 원전표 번호가 없거나 후보가 여럿' AS 사유
  FROM sales_returns sr
 WHERE sr.delivery_id IS NULL
   AND sr.is_deleted = 0;

-- ── 전체 사슬 상태
SELECT '[요약] 매출반품 사슬' AS info;
SELECT COUNT(*)                                                   AS 전체,
       SUM(CASE WHEN delivery_id IS NOT NULL THEN 1 ELSE 0 END)   AS 연결됨,
       SUM(CASE WHEN delivery_id IS NULL     THEN 1 ELSE 0 END)   AS 끊김
  FROM sales_returns
 WHERE is_deleted = 0;
