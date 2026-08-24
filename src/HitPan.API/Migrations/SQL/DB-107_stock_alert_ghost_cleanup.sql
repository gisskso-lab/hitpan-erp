-- DB-107 — 안전재고 경고 유령 행 정리 (20260825작1 W7)
--
-- 🔴 왜 필요한가
--   W1 이전에는 화면을 갱신할 때마다 이미 발주된('ordered') 품목에 새 'pending' 알림이
--   다시 들어갔다(중복 가드가 'pending' 만 봤다). 사장님이 배너를 누를수록,
--   화면을 새로고침할수록 같은 품목의 알림 행이 쌓였다.
--
--   W1 로 재삽입은 멎었지만 **이미 쌓인 행은 그대로 남아 있다.**
--   고치기만 하고 두면 옛 찌꺼기가 계속 배너에 떠서 "고쳤는데 그대로" 로 보인다.
--
-- 🔴 지우지 않는다 — 상태만 바꾼다
--   stock_alerts 는 "언제 무엇이 부족했나" 의 기록이다. DELETE 하면 그 이력이 사라진다.
--   같은 품목에 pending 이 여러 건이면 **가장 최근 1건만 남기고** 나머지는 'dismissed' 로 닫는다.
--   dismissed 는 "사용자가 닫음" 과 같은 값이라, 조회(status='pending')에서 자연히 빠지고
--   기록은 남는다.
--
-- ⚠️ 재적용 안전 — 이미 정리된 뒤 또 돌려도 남길 1건 외에 pending 이 없으므로 아무 일도 안 한다.

UPDATE stock_alerts sa
  JOIN (
        -- 품목마다 '살려둘' 최신 pending 알림 하나를 고른다.
        --   created_at 이 같은 행이 있을 수 있어 alert_id 로 한 번 더 갈라 유일하게 만든다.
        SELECT tenant_id, item_id,
               SUBSTRING_INDEX(
                   GROUP_CONCAT(alert_id ORDER BY created_at DESC, alert_id DESC), ',', 1
               ) AS keep_alert_id
          FROM stock_alerts
         WHERE status = 'pending'
         GROUP BY tenant_id, item_id
        HAVING COUNT(*) > 1          -- 1건뿐인 품목은 건드릴 이유가 없다
       ) k
    ON k.tenant_id = sa.tenant_id
   AND k.item_id   = sa.item_id
   SET sa.status = 'dismissed',
       sa.updated_at = NOW(6)
 WHERE sa.status = 'pending'
   AND sa.alert_id <> k.keep_alert_id;
