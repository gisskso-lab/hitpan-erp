-- DB-114 — 견적→수주 사슬 링크 신설 (20260827작10 W2)
--
-- 사장님 오더: "매출파트도 견적→수주→거래명세서→세금계산서 모두 정합성 맞춰"
--             "매입처리와 같이 사슬에 대한 전표번호 식별 가능하도록"
--             "몇십만 건 전표가 발행될텐데 이걸 연결사슬에 대한 전표번호가 없다면, 이건 AI도 못 찾음"
--
-- ============================================================================
-- 🔴 1) sales_orders.quotation_id — 견적을 가리키는 축이 **아예 없었다**
--
--   사슬 네 구간 중 견적→수주만 링크 컬럼이 없다:
--     견적 → 수주       : 🔴 컬럼 없음        ← 이 마이그가 메우는 자리
--     수주 → 명세서      : 🟢 order_id
--     명세서 → 계산서    : 🟢 delivery_id (FK)
--     명세서 → 매출반품  : 🟢 delivery_id (FK)
--
--   유일한 흔적은 memo 자유텍스트 "견적서 전환: {QuoteNo}" 였다.
--   ⇒ 사용자가 수주서를 수정하면 memo 가 화면 값으로 덮어써져 **연결이 소멸**한다.
--   ⇒ 견적에서 시작된 매출이 어디로 갔는지 되짚을 기계적 수단이 0 이었다.
--
--   ⚠️ FK 는 걸지 않는다. sales_deliveries.order_id 도 FK 없이 컬럼만 두는 것이 이 레포 관례이고,
--      견적이 지워져도 수주는 살아야 한다(흐름을 끊지 않는다 — 헌법 #20).
-- ============================================================================

SET @has_col := (
    SELECT COUNT(*) FROM information_schema.columns
     WHERE table_schema = DATABASE()
       AND table_name   = 'sales_orders'
       AND column_name  = 'quotation_id'
);
SET @sql := IF(@has_col = 0,
    'ALTER TABLE sales_orders
        ADD COLUMN `quotation_id` varchar(36) DEFAULT NULL COMMENT ''원 견적서 quote_id (20260827작10)''',
    'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @has_idx := (
    SELECT COUNT(*) FROM information_schema.statistics
     WHERE table_schema = DATABASE()
       AND table_name   = 'sales_orders'
       AND index_name   = 'idx_so_quotation'
);
SET @sql := IF(@has_idx = 0,
    'ALTER TABLE sales_orders ADD KEY `idx_so_quotation` (`tenant_id`,`quotation_id`)',
    'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ============================================================================
-- 🔴 2) 기존 데이터 소급 — memo 로 남아 있던 연결을 컬럼으로 옮긴다
--
--   "견적서 전환: 견-20260827-001" 형태의 memo 를 quote_no 로 되짚어 quotation_id 를 채운다.
--   ⚠️ memo 가 이미 덮어써진 건은 복구할 수 없다 — 그런 건은 그대로 NULL 로 둔다.
--      없는 연결을 지어내지 않는다.
-- ============================================================================

UPDATE sales_orders so
  JOIN quotations q
    ON q.tenant_id = so.tenant_id
   AND so.memo LIKE CONCAT('견적서 전환: ', q.quote_no, '%')
   SET so.quotation_id = q.quote_id
 WHERE so.quotation_id IS NULL;

-- ============================================================================
-- 🔴 3) quotations.converted_order_id 교정 — 컬럼명과 내용이 어긋나 있었다
--
--   컬럼명은 converted_order_**id** 인데 실제로는 **order_no(번호 문자열)** 가 들어간다
--   (QuotationService.cs:350-353). varchar(36) 이라 문자열이 그냥 들어가 에러가 안 났다.
--   이 값으로 JOIN 하는 코드는 0곳이다(에러 메시지 표시용으로만 쓰였다).
--
--   ⇒ order_no 로 되짚어 order_id 로 치환한다. 못 찾으면 그대로 둔다(지우지 않는다).
--   ⚠️ 이미 order_id(UUID 36자)가 들어있는 행은 건드리지 않는다 — 재실행 멱등.
-- ============================================================================

UPDATE quotations q
  JOIN sales_orders so
    ON so.tenant_id = q.tenant_id
   AND so.order_no  = q.converted_order_id
   SET q.converted_order_id = so.order_id
 WHERE q.converted_order_id IS NOT NULL
   AND q.converted_order_id <> ''
   AND CHAR_LENGTH(q.converted_order_id) < 36;
