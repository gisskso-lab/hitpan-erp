-- DB-109 — 매입반품에 작성자(created_by) 추가 (20260825작16)
--
-- 🔴 사장님 지시 (2026-08-25)
--   "발주서, 매입처리, 반품처리 목록 그리드도 판매관리 대메뉴 전표들 메뉴와
--    마찬가지로 담당자 행 추가"
--
--   판매 전표(거래명세서·수주·견적·매출반품)에는 「작성자」 칸이 있는데
--   매입 전표에는 없다. 누가 쓴 전표인지 목록에서 알 수가 없다.
--
-- 🔴 왜 이 테이블만 ALTER 인가
--   purchase_orders · purchase_receipts 에는 created_by 컬럼이 **이미 있다.**
--   purchase_returns 에만 없다 — 판매쪽 sales_returns 에는 있는데 빠졌다.
--   즉 이 마이그는 **빠진 한 자리를 판매와 맞추는 것**이다.
--
-- 🔴 왜 employees 가 아니라 user_id 를 담나
--   판매쪽이 그렇게 통일돼 있다(작5 결재) — 조회는
--     LEFT JOIN employees ec ON ec.user_id = <전표>.created_by
--   로 이름을 끌어온다. 매입만 employee_id 로 담으면 조인 축이 갈려
--   같은 「작성자」인데 화면마다 다른 사람이 나올 수 있다.
--
-- ⚠️ 과거 전표는 영원히 공란이다
--   매입 경로는 지금까지 created_by 를 **한 번도 쓰지 않았다**(PurchaseService grep 0건).
--   컬럼이 있던 purchase_orders·purchase_receipts 조차 값이 전부 NULL 이다.
--   없던 기록을 지어내지 않는다 — 오늘 이후 작성분부터 채워진다.
--   🔴 사장님께 사전 고지된 사항이다.
--
-- ⚠️ 재적용 안전 — 컬럼이 이미 있으면 아무 일도 하지 않는다.

SET @col_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'purchase_returns'
      AND COLUMN_NAME = 'created_by'
);

SET @ddl := IF(@col_exists = 0,
    'ALTER TABLE purchase_returns
       ADD COLUMN created_by VARCHAR(36) NULL
       COMMENT ''작성자 user_id — employees.user_id 와 조인해 이름을 낸다(판매 전표와 같은 축)''
       AFTER is_deleted',
    'SELECT ''DB-109: purchase_returns.created_by already exists — skip'' AS msg');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
