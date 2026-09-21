-- ═══════════════════════════════════════════════════════════════════════════
-- DB-125 · 잠금 범위 인덱스 2개 (20260921작2 갈래 A · T3-2 · 설계 §16 7판 · PM 결재 §15)
-- ═══════════════════════════════════════════════════════════════════════════
--
-- 🔴 왜 이 DDL 이 필요한가
--   DB-124 가 잠금 범위를 한 번 좁혔지만, 미수 M ②(L2 `ReceivableMatchedDocLockedSql`)는
--   아직 「그 회사 전체」에 가까운 범위를 잠근다. 채움 비율(이관 수금 중 전표에 붙은 비율)이
--   0% 일 때 그 회사 수금 전부(실측 112,020행)를 잠그고, 2~4% 구간에서도 3,000행대를 잠근다.
--   ⇒ 아무 상관 없는 다른 거래처의 수금·지급 등록이 그동안 막힌다.
--
--   🔴 이 DDL 은 **혼자서는 안 된다.** 「인덱스 2개 + 제품 SQL 한 줄(㉪)」이 **한 묶음**일 때만 봉합된다.
--      · 인덱스만 넣고 ㉪ 를 빼면 → 0% 구간이 안 고쳐진다(실측 비 0.9897 · 기준선의 99%).
--      · ㉪ 만 넣고 인덱스를 빼면 → 2~4% 구간이 **지금보다 나빠진다**(실측 +48% ~ +182%).
--      묶음을 쪼개 배포하면 어느 구간에선가 지금보다 나쁜 상태가 산다(작2 §0).
--
-- 하는 일: 인덱스 추가 2. 컬럼 추가·삭제 0 · 데이터 변경 0 · 기존 인덱스 삭제 0 (헌법 #1 · #37).
-- 멱등: information_schema 로 있으면 건너뛴다 — 2회 적용 = 신규 0 (DB-124 와 같은 관용구).
-- 출하 DDL(installer/hitpan_db_clean.sql) 에 같은 KEY 줄을 편입했다 (헌법 #36).
--
-- ⚠️ 이 마이그는 tenants 를 조인하지 않는다.
--    DB-111/112 사고: tenants 를 조인한 마이그가 고객 PC 에서 0행이라 "성공"으로 기록되고 아무 일도 안 했다.
--
-- ⚠️ 인덱스가 없는 구버전 DB 에서도 **동작은 지금과 같다**(느리고 넓게 잠글 뿐).
--    힌트(FORCE INDEX·STRAIGHT_JOIN)를 하나도 안 썼기 때문이다 — 힌트를 쓰면 인덱스가 없는 DB 에서
--    1176 으로 **등록이 죽는다**. 배포 순서로 기능이 깨지지 않는다.
--
-- 🔴 ALGORITHM/LOCK 을 구문으로 걸지 않는다 (설계 P-19)
--    `ALGORITHM=INPLACE, LOCK=NONE` 을 문장에 박으면, 그 방식이 안 되는 상황에서 ALTER 가
--    **실패**한다 — 고객 PC 에서 업데이트가 멈춘다. MariaDB 는 가능하면 알아서 INPLACE 로 간다.
--    실측 소요 (⚠️ **이 사본 기준** · collections 109,822행 · sales_deliveries 115,150행):
--      · sales_deliveries  ADD KEY  … 3.9초  (재측 1.72초)
--      · collections       ADD KEY  … 1.9초  (재측 0.88초)
--      · 디스크 증가 … +57.1MB
--    ⚠️ 실물 최대 규모는 **미확인**이다. 더 큰 DB 에서는 더 걸린다.
--
-- 🔴 부분 적용 차단 장치(T3-6 · 갈래 B)가 읽는 선언이다. 형식·이름을 바꾸지 마라.
-- @verify-index: sales_deliveries.idx_sd_tenant_partner_src
-- @verify-index: collections.idx_coll_tenant_doc_cover
-- ═══════════════════════════════════════════════════════════════════════════

-- ── 1) sales_deliveries (tenant_id, partner_id, source_type) ── C2
--    L2 ReceivableMatchedDocLockedSql 의 sd.
--    지금은 (tenant_id, partner_id) 까지만 타고 source_type 을 테이블에서 되짚어 보느라
--    구동표가 뒤집히거나 필요 없는 행까지 잠근다. source_type 을 인덱스 안에 넣어 **커버링**으로 만든다.
--    실측: L2 잠금 3,122 → 2,013 (−36%) · 힌트 없이 채택.
SET @idx_exists := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'sales_deliveries'
      AND index_name = 'idx_sd_tenant_partner_src'
);

SET @ddl := IF(@idx_exists = 0,
    'ALTER TABLE sales_deliveries ADD KEY `idx_sd_tenant_partner_src` (`tenant_id`,`partner_id`,`source_type`)',
    'SELECT ''DB-125: idx_sd_tenant_partner_src already exists'' AS skipped');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ── 2) collections (tenant_id, ref_doc_type, ref_doc_id, is_active, source_type, amount) ── C4
--    L2 ReceivableMatchedDocLockedSql 의 ec.
--    DB-124 의 idx_coll_tenant_doc (tenant_id, ref_doc_type, ref_doc_id) 를 **지우지 않고**,
--    is_active·source_type·amount 를 뒤에 붙여 **커버링**으로 확장한 별도 인덱스를 둔다.
--    단독 효과는 −1.5% 로 작다. 🔴 **이 인덱스의 존재 이유는 ㉪ 의 2~4% 함정을 없애는 것**이다:
--    ㉪ 만 넣고 이 인덱스가 없으면 2~4% 구간이 +48~182% 로 나빠진다(실측).
SET @idx_exists := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'collections'
      AND index_name = 'idx_coll_tenant_doc_cover'
);

SET @ddl := IF(@idx_exists = 0,
    'ALTER TABLE collections ADD KEY `idx_coll_tenant_doc_cover` (`tenant_id`,`ref_doc_type`,`ref_doc_id`,`is_active`,`source_type`,`amount`)',
    'SELECT ''DB-125: idx_coll_tenant_doc_cover already exists'' AS skipped');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ── 되돌리기 (참고 · 실행하지 않는다) ──
--   🔴 되돌릴 때는 **제품 SQL 의 ㉪ 를 먼저 내리고** 그 다음에 인덱스를 지운다(설계 §16-3 R-2).
--      순서를 뒤집어 인덱스만 먼저 지우면 ㉪ 만 남아 **2~4% 구간이 지금보다 나빠진다.**
--   🔴 세 조각(C2 · C4 · ㉪)은 **하나의 되돌림 단위**다. 부분 되돌림 금지.
-- ALTER TABLE sales_deliveries DROP INDEX idx_sd_tenant_partner_src;
-- ALTER TABLE collections      DROP INDEX idx_coll_tenant_doc_cover;
