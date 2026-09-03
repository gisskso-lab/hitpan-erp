-- ============================================================
-- 매입·매출 반품 정합성 실측 (2026-09-03)
-- 🔴 읽기 전용 — SELECT 만 있다. 데이터를 바꾸지 않는다 (헌법 #39).
--
-- 쓰는 법:
--   mysql -u <계정> -p --default-character-set=utf8mb4 <DB이름> < 이 파일
--   ⚠️ --default-character-set=utf8mb4 필수 (없으면 한글이 CP949 로 깨져 길이가 틀리게 나온다)
-- ============================================================

-- ────────────────────────────────────────────────────────────
-- [1] 매출반품 전표 — 무엇이 있고 어떤 상태인가
--   status: draft=반품완료(원장 무접촉) / confirmed=반품확정(재고·원장 반영) / canceled=취소
--   🔴 draft 면 재고가 안 움직이는 게 **정상**이다.
-- ────────────────────────────────────────────────────────────
SELECT '[1] 매출반품 전표' AS 구분;
SELECT sr.return_no        AS 반품번호,
       sr.return_date      AS 일자,
       sr.status           AS 상태,
       sd.delivery_no      AS 원전표,
       sr.total_amount     AS 공급가,
       sr.vat_amount       AS 부가세,
       sr.return_reason    AS 사유,
       sr.memo             AS 비고
  FROM sales_returns sr
  LEFT JOIN sales_deliveries sd ON sd.delivery_id = sr.delivery_id
 WHERE sr.is_deleted = 0
 ORDER BY sr.return_date DESC, sr.return_no DESC;

-- ────────────────────────────────────────────────────────────
-- [2] 🔴 헤더 금액 vs 라인 합계 — 어긋나면 전표 자체가 깨진 것
-- ────────────────────────────────────────────────────────────
SELECT '[2] 매출반품 헤더 vs 라인' AS 구분;
SELECT sr.return_no                              AS 반품번호,
       sr.total_amount                           AS 헤더공급가,
       COALESCE(SUM(sri.supply_amount),0)        AS 라인합계,
       sr.total_amount - COALESCE(SUM(sri.supply_amount),0) AS 차이,
       CASE WHEN ABS(sr.total_amount - COALESCE(SUM(sri.supply_amount),0)) < 0.01
            THEN 'OK' ELSE '🔴 불일치' END       AS 판정
  FROM sales_returns sr
  LEFT JOIN sales_return_items sri ON sri.return_id = sr.return_id
 WHERE sr.is_deleted = 0
 GROUP BY sr.return_id, sr.return_no, sr.total_amount;

-- ────────────────────────────────────────────────────────────
-- [3] 🔴 재고원장 반영 — 확정된 반품은 stock_ledger 에 IN 이 있어야 한다
--   확정인데 원장이 없으면 **P0**. draft 인데 없으면 정상.
-- ────────────────────────────────────────────────────────────
SELECT '[3] 매출반품 → 재고원장' AS 구분;
SELECT sr.return_no                       AS 반품번호,
       sr.status                          AS 상태,
       COUNT(sl.ledger_id)                AS 원장행수,
       COALESCE(SUM(sl.qty_in),0)         AS 입고수량,
       COALESCE(SUM(sl.qty_out),0)        AS 출고수량,
       CASE
         WHEN sr.status <> 'confirmed' AND COUNT(sl.ledger_id) = 0 THEN 'OK (미확정이라 원장 없음이 정상)'
         WHEN sr.status =  'confirmed' AND COUNT(sl.ledger_id) > 0 THEN 'OK'
         WHEN sr.status =  'confirmed' AND COUNT(sl.ledger_id) = 0 THEN '🔴 P0 확정인데 재고 미반영'
         ELSE '⚠️ 미확정인데 원장 있음'
       END                                AS 판정
  FROM sales_returns sr
  LEFT JOIN stock_ledger sl
         ON sl.source_id = sr.return_id AND sl.source_type = 'sales_return'
 WHERE sr.is_deleted = 0
 GROUP BY sr.return_id, sr.return_no, sr.status;

-- ────────────────────────────────────────────────────────────
-- [4] 🔴 회계 반영 — 확정 반품은 분개가 있어야 한다
-- ────────────────────────────────────────────────────────────
SELECT '[4] 매출반품 → 회계분개' AS 구분;
SELECT sr.return_no          AS 반품번호,
       sr.status             AS 상태,
       COUNT(je.entry_id)    AS 분개수,
       CASE
         WHEN sr.status <> 'confirmed' AND COUNT(je.entry_id) = 0 THEN 'OK (미확정)'
         WHEN sr.status =  'confirmed' AND COUNT(je.entry_id) > 0 THEN 'OK'
         WHEN sr.status =  'confirmed' AND COUNT(je.entry_id) = 0 THEN '🔴 P0 확정인데 기표 누락'
         ELSE '⚠️ 미확정인데 분개 있음'
       END                   AS 판정
  FROM sales_returns sr
  LEFT JOIN journal_entries je
         ON je.source_id = sr.return_id AND je.source_type = 'sales_return'
 WHERE sr.is_deleted = 0
 GROUP BY sr.return_id, sr.return_no, sr.status;

-- ────────────────────────────────────────────────────────────
-- [5] 🔴 반품 수량이 판매 수량을 넘지 않는가 (품목별 잔량)
-- ────────────────────────────────────────────────────────────
SELECT '[5] 반품 상한 검사' AS 구분;
SELECT sd.delivery_no                       AS 원전표,
       i.item_name                          AS 품목,
       COALESCE(SUM(DISTINCT sdi.qty),0)    AS 판매수량,
       COALESCE(ret.반품수량,0)              AS 반품수량,
       CASE WHEN COALESCE(ret.반품수량,0) > COALESCE(SUM(DISTINCT sdi.qty),0)
            THEN '🔴 P0 초과반품' ELSE 'OK' END AS 판정
  FROM sales_deliveries sd
  JOIN sales_delivery_items sdi ON sdi.delivery_id = sd.delivery_id
  JOIN items i ON i.item_id = sdi.item_id
  LEFT JOIN (
        SELECT sr.delivery_id, sri.item_id, SUM(sri.qty) AS 반품수량
          FROM sales_returns sr
          JOIN sales_return_items sri ON sri.return_id = sr.return_id
         WHERE sr.is_deleted = 0 AND sr.status = 'confirmed'
         GROUP BY sr.delivery_id, sri.item_id
  ) ret ON ret.delivery_id = sd.delivery_id AND ret.item_id = sdi.item_id
 WHERE sd.is_deleted = 0
 GROUP BY sd.delivery_id, sd.delivery_no, i.item_name, ret.반품수량
HAVING 반품수량 > 0;

-- ────────────────────────────────────────────────────────────
-- [6] 🔴 재고 정합 — item_stock(현재고) vs stock_ledger(원장 누계)
--   이 둘이 어긋나면 재고 숫자를 믿을 수 없다. 전 품목 검사.
-- ────────────────────────────────────────────────────────────
SELECT '[6] 현재고 vs 원장 누계' AS 구분;
SELECT i.item_name                                   AS 품목,
       s.current_qty                                 AS 현재고,
       COALESCE(l.원장누계,0)                         AS 원장누계,
       s.current_qty - COALESCE(l.원장누계,0)         AS 차이,
       CASE WHEN ABS(s.current_qty - COALESCE(l.원장누계,0)) < 0.01
            THEN 'OK' ELSE '🔴 불일치' END            AS 판정
  FROM item_stock s
  JOIN items i ON i.item_id = s.item_id
  LEFT JOIN (
        SELECT item_id, SUM(qty_in) - SUM(qty_out) AS 원장누계
          FROM stock_ledger GROUP BY item_id
  ) l ON l.item_id = s.item_id
 WHERE ABS(s.current_qty - COALESCE(l.원장누계,0)) >= 0.01
    OR s.current_qty <> 0
 ORDER BY 판정 DESC, 품목;

-- ────────────────────────────────────────────────────────────
-- [7] 🔴 업체별 미수금 — 반품이 차감됐는가
--   partner_balance.total_sales = 판매확정 합계 − 반품확정 합계 여야 한다
-- ────────────────────────────────────────────────────────────
SELECT '[7] 업체 미수금 정합' AS 구분;
SELECT p.partner_name                          AS 업체,
       pb.total_sales                          AS 원장_매출,
       COALESCE(s.판매,0)                       AS 판매확정합,
       COALESCE(r.반품,0)                       AS 반품확정합,
       COALESCE(s.판매,0) - COALESCE(r.반품,0)   AS 계산값,
       CASE WHEN ABS(pb.total_sales - (COALESCE(s.판매,0) - COALESCE(r.반품,0))) < 0.01
            THEN 'OK' ELSE '🔴 불일치' END       AS 판정
  FROM partner_balance pb
  JOIN partners p ON p.partner_id = pb.partner_id
  LEFT JOIN (SELECT partner_id, SUM(total_amount) AS 판매 FROM sales_deliveries
              WHERE status='confirmed' AND is_deleted=0 GROUP BY partner_id) s
         ON s.partner_id = pb.partner_id
  LEFT JOIN (SELECT partner_id, SUM(total_amount) AS 반품 FROM sales_returns
              WHERE status='confirmed' AND is_deleted=0 GROUP BY partner_id) r
         ON r.partner_id = pb.partner_id
 WHERE pb.total_sales <> 0 OR s.판매 IS NOT NULL OR r.반품 IS NOT NULL;

-- ────────────────────────────────────────────────────────────
-- [8] 매입 쪽도 같은 검사 (대칭 확인)
-- ────────────────────────────────────────────────────────────
SELECT '[8] 매입반품 → 재고·회계' AS 구분;
SELECT pr.return_no                       AS 반품번호,
       pr.status                          AS 상태,
       COUNT(DISTINCT sl.ledger_id)       AS 재고원장,
       COUNT(DISTINCT je.entry_id)        AS 분개수,
       CASE
         WHEN pr.status <> 'confirmed' THEN 'OK (미확정)'
         WHEN COUNT(DISTINCT sl.ledger_id) > 0 AND COUNT(DISTINCT je.entry_id) > 0 THEN 'OK'
         ELSE '🔴 확정인데 반영 누락'
       END                                AS 판정
  FROM purchase_returns pr
  LEFT JOIN stock_ledger sl
         ON sl.source_id = pr.return_id AND sl.source_type = 'purchase_return'
  LEFT JOIN journal_entries je
         ON je.source_id = pr.return_id AND je.source_type = 'purchase_return'
 WHERE pr.is_deleted = 0
 GROUP BY pr.return_id, pr.return_no, pr.status;

-- ────────────────────────────────────────────────────────────
-- [9] 🔴 부가세 축 — 매출·매입 각각 반품이 빠졌는가 (1.3.33 봉합 확인)
-- ────────────────────────────────────────────────────────────
SELECT '[9] 부가세 집계 (반품 차감 후)' AS 구분;
SELECT '매출' AS 구분,
       COALESCE((SELECT SUM(total_amount) FROM sales_deliveries
                  WHERE status IN ('confirmed','invoiced') AND is_deleted=0),0)
       - COALESCE((SELECT SUM(sri.supply_amount) FROM sales_returns sr
                     JOIN sales_return_items sri ON sri.return_id=sr.return_id
                    WHERE sr.status='confirmed' AND sr.is_deleted=0),0) AS 공급가_반품차감후,
       COALESCE((SELECT SUM(vat_amount) FROM sales_deliveries
                  WHERE status IN ('confirmed','invoiced') AND is_deleted=0),0)
       - COALESCE((SELECT SUM(sri.vat_amount) FROM sales_returns sr
                     JOIN sales_return_items sri ON sri.return_id=sr.return_id
                    WHERE sr.status='confirmed' AND sr.is_deleted=0),0) AS 부가세_반품차감후
UNION ALL
SELECT '매입',
       COALESCE((SELECT SUM(total_amount) FROM purchase_receipts WHERE status='confirmed'),0)
       - COALESCE((SELECT SUM(pri.supply_amount) FROM purchase_returns pr
                     JOIN purchase_return_items pri ON pri.return_id=pr.return_id
                    WHERE pr.status='confirmed' AND pr.is_deleted=0),0),
       COALESCE((SELECT SUM(vat_amount) FROM purchase_receipts WHERE status='confirmed'),0)
       - COALESCE((SELECT SUM(pri.vat_amount) FROM purchase_returns pr
                     JOIN purchase_return_items pri ON pri.return_id=pr.return_id
                    WHERE pr.status='confirmed' AND pr.is_deleted=0),0);
