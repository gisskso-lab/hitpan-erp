-- DB-113 — 매출 사슬 중복생성 봉합 (20260827작9 W1·W3)
--
-- 사장님 오더: "사슬의 정합률 100%가 목표임. 그리고 가장 중요한거 사슬동작중 중복생성 절대금지"
--
-- 이 마이그가 하는 일 3가지
--   1) sales_returns.return_no  : '매출반품-' → '반-'  (길이 초과 봉합)
--   2) quotations.quote_no      : UNIQUE 신설
--   3) purchase_returns.return_no : UNIQUE 신설
--
-- ============================================================================
-- 🔴 1) 매출반품 번호가 저장 자체를 못 하고 있었다
--
--   '매출반품-20260827-001' = 25자
--   sales_returns.return_no = varchar(20)
--   이 배포는 STRICT_TRANS_TABLES 다 (ApprovalService.cs:118 이 과거 ERROR 1406 사고를 기록)
--   ⇒ 매출반품 생성이 ERROR 1406 Data too long 으로 터진다.
--
--   매입은 '매반-'(2글자)로 19자에 맞춰 놨는데 매출만 '매출반품-'(4글자)라 넘쳤다.
--   사장님 지시: "반품전표 : 반-(전표번호)" ⇒ '반-20260827-001' = 18자. 들어간다.
--
--   ⚠️ 기존 행은 대부분 없을 것이다(애초에 저장이 안 됐으니).
--      혹 non-strict 로 돌던 환경에서 잘린 채 들어간 행이 있으면 그것도 함께 정리한다.
--      있으면 고치고 없으면 넘어간다 — 재실행해도 같은 결과다(멱등).
--
-- ⚠️ 이 마이그는 tenants 를 조인하지 않는다.
--    DB-111/112 사고: tenants 를 조인한 마이그가 고객 PC 에서 0행이라
--    "성공"으로 기록되고 아무 일도 안 했다. 같은 실수를 반복하지 않는다.
-- ============================================================================

UPDATE sales_returns
   SET return_no = CONCAT('반-', SUBSTRING(return_no, 6))
 WHERE return_no LIKE '매출반품-%';

-- 잘린 채 저장된 행 정리 ('매출반품-20260827' 처럼 순번이 날아간 것)
-- 순번을 복원할 수 없으므로 행마다 새 순번을 준다. 없으면 0건.
UPDATE sales_returns
   SET return_no = CONCAT('반-', DATE_FORMAT(return_date, '%Y%m%d'), '-',
                          LPAD(CONV(SUBSTRING(MD5(return_id), 1, 4), 16, 10) % 1000, 3, '0'))
 WHERE return_no NOT LIKE '반-%'
   AND return_no NOT LIKE '매출반품-%'
   AND CHAR_LENGTH(return_no) < 10;

-- ============================================================================
-- 🔴 2) quotations.quote_no UNIQUE 신설
--
--   견적번호는 COUNT+1 로 채번되고 있었고(작9 W2 에서 MAX+1 로 교체), 표에 UNIQUE 가 없어
--   중복이 나도 에러 없이 그냥 저장됐다 — 조용한 중복.
--   sales_orders·sales_deliveries·sales_returns·tax_invoices 에는 전부 있는데 견적만 없었다.
--
--   ⚠️ UNIQUE 를 걸기 전에 기존 중복을 먼저 없애야 ALTER 가 성공한다.
--      중복이 0건이면 아래 UPDATE 는 아무 일도 안 한다.
-- ============================================================================

-- 중복 견적번호에 순번을 붙여 떨어뜨린다 (가장 오래된 1건은 원래 번호 유지)
UPDATE quotations q
  JOIN (
        SELECT quote_id,
               ROW_NUMBER() OVER (PARTITION BY tenant_id, quote_no ORDER BY created_at, quote_id) AS rn
          FROM quotations
       ) d ON d.quote_id = q.quote_id
   SET q.quote_no = CONCAT(SUBSTRING(q.quote_no, 1, 16), '-D', d.rn)
 WHERE d.rn > 1;

SET @has_uq_quote := (
    SELECT COUNT(*) FROM information_schema.statistics
     WHERE table_schema = DATABASE()
       AND table_name   = 'quotations'
       AND index_name   = 'uq_quote_no'
);
SET @sql := IF(@has_uq_quote = 0,
    'ALTER TABLE quotations ADD UNIQUE KEY `uq_quote_no` (`tenant_id`,`quote_no`)',
    'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ============================================================================
-- 🔴 3) purchase_returns.return_no UNIQUE 신설
--
--   sales_returns 에는 uq_sret_tenant_returnno 가 있는데 매입반품에만 없었다 — 비대칭.
--   매입반품도 COUNT+1 채번이었으므로(작9 W2 에서 교체) 중복이 조용히 들어갈 수 있었다.
-- ============================================================================

UPDATE purchase_returns p
  JOIN (
        SELECT return_id,
               ROW_NUMBER() OVER (PARTITION BY tenant_id, return_no ORDER BY created_at, return_id) AS rn
          FROM purchase_returns
       ) d ON d.return_id = p.return_id
   SET p.return_no = CONCAT(SUBSTRING(p.return_no, 1, 15), '-D', d.rn)
 WHERE d.rn > 1;

SET @has_uq_pret := (
    SELECT COUNT(*) FROM information_schema.statistics
     WHERE table_schema = DATABASE()
       AND table_name   = 'purchase_returns'
       AND index_name   = 'uq_pret_return_no'
);
SET @sql := IF(@has_uq_pret = 0,
    'ALTER TABLE purchase_returns ADD UNIQUE KEY `uq_pret_return_no` (`tenant_id`,`return_no`)',
    'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
