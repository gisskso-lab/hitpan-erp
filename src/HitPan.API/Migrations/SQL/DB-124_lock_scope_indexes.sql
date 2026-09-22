-- ═══════════════════════════════════════════════════════════════════════════
-- DB-124 · 잠금 범위 인덱스 4개 (20260920작1 갈래 S3 ㉳ · 설계 §12-5 · PM 결재 §17 C-1)
-- ═══════════════════════════════════════════════════════════════════════════
--
-- 🔴 왜 이 DDL 이 필요한가
--   수금·지급 등록의 RR(REPEATABLE READ) 경로는 남은금액을 판정할 때 합계를 `LOCK IN SHARE MODE` 로
--   읽는다. 그런데 그 문장들이 타는 인덱스에 `ref_doc_id`·`ref_order_id`·`receipt_id` 를 접두로 쓰는
--   인덱스가 **하나도 없어서**, InnoDB 가 잠그는 인덱스 레코드 범위가 「그 전표」가 아니라
--   **그 회사 전체(또는 전체 스캔)** 가 된다.
--   ⇒ 아무 상관 없는 다른 거래처의 수금·지급 등록이 서로 막히고 교착한다([3-V] 병렬이슈53 실측).
--
--   고치는 방법이 SQL 을 바꾸는 것이 아니라 **인덱스를 다는 것**인 이유(설계 §12-3/§12-4):
--   문장이나 호출 순서를 바꾸면 「덜 세는」 자리가 새로 생긴다 = 금액이 틀린다.
--   인덱스는 **읽는 값을 한 글자도 바꾸지 않고** 잠금 범위만 좁힌다. 되돌리기도 DROP INDEX 4줄이다.
--
-- 하는 일: 인덱스 추가 4. 컬럼 추가·삭제 0 · 데이터 변경 0 · 기존 인덱스 삭제 0 (헌법 #1 · #37).
-- 멱등: information_schema 로 있으면 건너뛴다 — 2회 적용 = 신규 0 (DB-118 과 같은 관용구).
-- 출하 DDL(installer/hitpan_db_clean.sql) 에 같은 KEY 줄을 편입했다 (헌법 #36).
--
-- ⚠️ 이 마이그는 tenants 를 조인하지 않는다.
--    DB-111/112 사고: tenants 를 조인한 마이그가 고객 PC 에서 0행이라 "성공"으로 기록되고 아무 일도 안 했다.
--
-- ⚠️ 인덱스가 없는 구버전 DB 에서도 **동작은 지금과 같다**(느리고 넓게 잠글 뿐).
--    배포 순서로 기능이 깨지지 않는다.
-- ═══════════════════════════════════════════════════════════════════════════

-- ── 1) collections (tenant_id, ref_doc_type, ref_doc_id) ──
--    L2 ReceivableMatchedDocLockedSql 의 ec · D1-a 전표 수금합.
--    지금은 tenant_id 접두만 타거나(또는 전체 스캔) → 그 회사 수금 전부를 잠근다.
SET @idx_exists := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'collections'
      AND index_name = 'idx_coll_tenant_doc'
);

SET @ddl := IF(@idx_exists = 0,
    'ALTER TABLE collections ADD KEY `idx_coll_tenant_doc` (`tenant_id`,`ref_doc_type`,`ref_doc_id`)',
    'SELECT ''DB-124: idx_coll_tenant_doc already exists'' AS skipped');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ── 2) payments (tenant_id, payment_type, ref_order_id) ──
--    L4 PayableMatchedDocLockedSql 의 ep · D1-b 전표 지급합.
SET @idx_exists := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'payments'
      AND index_name = 'idx_pay_tenant_type_ref'
);

SET @ddl := IF(@idx_exists = 0,
    'ALTER TABLE payments ADD KEY `idx_pay_tenant_type_ref` (`tenant_id`,`payment_type`,`ref_order_id`)',
    'SELECT ''DB-124: idx_pay_tenant_type_ref already exists'' AS skipped');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ── 3) payments (tenant_id, partner_id) ──
--    L3 PayableMatchedLegacyLockedSql. payments 에는 (tenant_id,partner_id) 복합이 없어서
--    옵티마이저가 idx_pay_tenant(테넌트 전체) 와 idx_pay_partner(테넌트를 넘어간다) 사이에서 갈린다.
--    collections 의 idx_tenant_partner 와 같은 모양을 지급 축에도 준다.
SET @idx_exists := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'payments'
      AND index_name = 'idx_pay_tenant_partner'
);

SET @ddl := IF(@idx_exists = 0,
    'ALTER TABLE payments ADD KEY `idx_pay_tenant_partner` (`tenant_id`,`partner_id`)',
    'SELECT ''DB-124: idx_pay_tenant_partner already exists'' AS skipped');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ── 4) purchase_returns (tenant_id, receipt_id) ──
--    L5 PayableMatchedReturnLockedSql · D1-c 전표 반품합.
--    receipt_id 를 접두로 쓰는 인덱스가 없어 idx_tenant 접두만 타거나 조인 순서가 뒤집혀
--    purchase_return_items 를 통째로 훑는다.
SET @idx_exists := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'purchase_returns'
      AND index_name = 'idx_rt_tenant_receipt'
);

SET @ddl := IF(@idx_exists = 0,
    'ALTER TABLE purchase_returns ADD KEY `idx_rt_tenant_receipt` (`tenant_id`,`receipt_id`)',
    'SELECT ''DB-124: idx_rt_tenant_receipt already exists'' AS skipped');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ── 되돌리기 (참고 · 실행하지 않는다) ──
--   되돌려도 동작은 그대로다 — SQL 을 한 글자도 안 바꿨기 때문이다(설계 §12-5).
-- ALTER TABLE collections      DROP INDEX idx_coll_tenant_doc;
-- ALTER TABLE payments         DROP INDEX idx_pay_tenant_type_ref;
-- ALTER TABLE payments         DROP INDEX idx_pay_tenant_partner;
-- ALTER TABLE purchase_returns DROP INDEX idx_rt_tenant_receipt;
