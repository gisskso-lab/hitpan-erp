-- ═══════════════════════════════════════════════════════════════════════════
-- DB-123 · 이전 프로그램 장부 미반영 명세서 보관 표 2개 + 거래처 이월잔액 표 1개
--          + item_stock.avg_cost 소수 6자리 (20260915작1 갈래 F)
-- ═══════════════════════════════════════════════════════════════════════════
--
-- 근거
--   작업지시서 docs/운영기록/20260915작1_자료이관_머리없는줄_분류봉합_작업지시서.md §11-2 F · §11-3 G7 · §13(R6-2·R-A2)
--   설계문서   docs/설계/erp/20260915_설계_자료이관_머리없는줄_분류봉합.md §15 · §16 · §17
--   게이트     src/HitPan.Tests/Integrity/LegacyUnpostedDdlGateTests.cs (G7)
--   개발명세서 docs/개발/erp/20260915작1_갈래F_DDL_개발명세서.md
--
-- 🔴 왜 이 DDL 이 필요한가
--   ① 레거시 DOCFB 줄 중 「머리표(DOCFE)도 없고 거래처원장(DOCF5) 연결도 없는」 줄은
--      장부 반영 명세서가 아니다(설계 §14). 버리면 뺀 줄이 생기고, 명세서로 넣으면 매출·미수가 부푼다.
--      ⇒ 🆕 legacy_unposted_documents / legacy_unposted_document_lines 에 보관만 한다.
--         이관만 INSERT · 화면·API 는 읽기만 · 매출·미수·계산서 합계에 들어가지 않는다.
--   ② 거래처 이월잔액(F3)을 수금·지급 표에 맞춤 줄로 넣으면 2026-02 현황이 억 단위 음수로 오염된다(설계 §17).
--      ⇒ 🆕 partner_legacy_balances (사장님 결재 R-A2 (나)).
--   ③ 화면 재고금액 = current_qty × avg_cost. 소수 2자리로는 3품목 끝전을 레거시와 맞출 수 없다(설계 §16).
--      ⇒ item_stock.avg_cost decimal(15,2) → decimal(19,6) (사장님 결재 R6-2 (가)). 넓히기만 — 값 손실 0.
--
-- 하는 일: 표 생성 3 · 컬럼 형 넓히기 1. 데이터 변경 0건. 컬럼 삭제 0건(헌법 #37).
-- 멱등: CREATE TABLE IF NOT EXISTS · avg_cost 는 information_schema 로 형이 이미 decimal(19,6) 이면 건너뛴다.
--       2회 적용 = 변화 0 (DB-118 과 같은 관용구).
-- 출하 DDL(installer/hitpan_db_clean.sql) 에 칼럼·키 한 글자도 다르지 않게 같은 정의를 편입했다(헌법 #36).
--   ⇒ G7 이 「출하 DDL 로 만든 표」와 「이 파일로 만든 표」의 information_schema 를 칼럼·키 단위로 대조한다.
--
-- 초기화: DataResetService 가 information_schema 에서 tenant_id 칼럼이 있는 표를 자동으로 비운다
--         (DataResetService.cs:111-154) · 세 표 모두 PreservedTables(:47-70)에 없다 ⇒ 코드 수정 0.
--
-- 공통: ENGINE=InnoDB(#17) · utf8mb4_unicode_ci(기존 표 전부와 같음) · 금액 decimal(#4) · tenant_id NOT NULL(#2)
--       FK 없음 — 이관 보관 표라 거래처·상품이 나중에 지워져도 보관 줄은 남아야 한다(설계 §15 partner_id NULL·FK 없음).
--
-- ⚠️ 이 마이그는 tenants 를 조인하지 않는다.
--    DB-111/112 사고: tenants 를 조인한 마이그가 고객 PC 에서 0행이라 "성공"으로 기록되고 아무 일도 안 했다.
-- ═══════════════════════════════════════════════════════════════════════════

-- ── 1) legacy_unposted_documents — 미반영 명세서 머리 (DOCFB 묶음 1개 = 1행) ──
CREATE TABLE IF NOT EXISTS `legacy_unposted_documents` (
  `doc_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `io_type` varchar(10) NOT NULL COMMENT 'sales / purchase',
  `doc_date` date NOT NULL COMMENT '명세서 날짜 — 날짜 없는 묶음은 기준일 (설계 §14)',
  `legacy_dt` varchar(8) DEFAULT NULL COMMENT '레거시 IJ_DT 원문 (00000000 포함)',
  `legacy_seq` int(11) DEFAULT NULL COMMENT '레거시 IJ_SEQ',
  `legacy_buy_code` bigint(20) DEFAULT NULL COMMENT '레거시 거래처 코드 IJ_BUY — 표시·추적용',
  `partner_id` varchar(36) DEFAULT NULL COMMENT '히트판 거래처 — 못 찾으면 NULL · FK 없음',
  `reason` varchar(30) DEFAULT NULL COMMENT '보관 사유 (갈래 A 판정 결과)',
  `supply_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `vat_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `line_count` int(11) NOT NULL DEFAULT 0,
  `memo` varchar(500) DEFAULT NULL,
  `source_type` varchar(20) NOT NULL DEFAULT 'migration',
  `source_id` varchar(80) NOT NULL COMMENT '멱등 키 mig-docfb-{dt}-{io}-{seq}-{buy} (DB-123)',
  `migrated_source_hash` char(64) DEFAULT NULL COMMENT 'SHA256 본문 해시 (DB-123)',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`doc_id`),
  UNIQUE KEY `uq_legacy_unposted_docs_source` (`tenant_id`,`source_id`),
  KEY `idx_legacy_unposted_docs_date` (`tenant_id`,`doc_date`),
  KEY `idx_legacy_unposted_docs_partner` (`tenant_id`,`partner_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='이전 프로그램 장부 미반영 명세서 — 이관 보관·읽기 전용 (DB-123)';

-- ── 2) legacy_unposted_document_lines — 미반영 명세서 줄 (DOCFB 줄 1개 = 1행) ──
--    qty decimal(15,3) = sales_delivery_items.qty · stock_ledger.qty_in 과 같은 형(같은 원본 줄이 재고원장에도 들어간다).
--    unit_price decimal(15,4) = stock_ledger.unit_cost 와 같은 자리 — 레거시 단가 소수 손실 방지.
CREATE TABLE IF NOT EXISTS `legacy_unposted_document_lines` (
  `line_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `doc_id` varchar(36) NOT NULL COMMENT 'legacy_unposted_documents.doc_id · FK 없음',
  `line_no` int(11) NOT NULL DEFAULT 0,
  `item_id` varchar(36) DEFAULT NULL COMMENT '히트판 상품 — 품명 없는 줄은 NULL · FK 없음',
  `item_name` varchar(200) DEFAULT NULL,
  `spec` varchar(200) DEFAULT NULL,
  `qty` decimal(15,3) NOT NULL DEFAULT 0.000,
  `unit_price` decimal(15,4) NOT NULL DEFAULT 0.0000,
  `supply_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `vat_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `memo` varchar(500) DEFAULT NULL,
  `stock_source_id` varchar(80) DEFAULT NULL COMMENT '재고원장 stock_ledger 연결 키 (mb-…)',
  `migrated_source_hash` char(64) NOT NULL COMMENT 'SHA256 줄 해시 — 멱등 키 (DB-123)',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`line_id`),
  UNIQUE KEY `uq_legacy_unposted_lines_hash` (`tenant_id`,`migrated_source_hash`),
  KEY `idx_legacy_unposted_lines_doc` (`tenant_id`,`doc_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='이전 프로그램 장부 미반영 명세서 줄 — 이관 보관·읽기 전용 (DB-123)';

-- ── 3) partner_legacy_balances — 거래처 이월잔액 (사장님 결재 R-A2 (나)) ──
--    balance_amount 부호: + = 받을 돈(미수) · − = 줄 돈(미지급). 레거시 거래처원장 S_BAL 과 같은 부호(설계 §17 F3).
--    UNIQUE 2개: (tenant_id, source_id) 멱등 키 · (tenant_id, partner_id) 한 거래처 한 잔액(병렬이슈39 ① — 재이관 이중 적재 차단).
--    ⚠️ 레거시 거래처 코드 여러 개가 한 거래처로 합쳐지면(폴백 병합) 이관(갈래 E)이 거래처별로 합산해 1행으로 넣어야 한다.
CREATE TABLE IF NOT EXISTS `partner_legacy_balances` (
  `balance_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `partner_id` varchar(36) NOT NULL COMMENT '히트판 거래처 · FK 없음',
  `legacy_buy_code` bigint(20) DEFAULT NULL COMMENT '레거시 거래처 코드 S_BUY — 표시·추적용',
  `base_date` date NOT NULL COMMENT '기준일 (설계 §14)',
  `balance_amount` decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '+ 미수 / - 미지급 (F3)',
  `source_type` varchar(20) NOT NULL DEFAULT 'migration',
  `source_id` varchar(80) NOT NULL COMMENT '멱등 키 (DB-123)',
  `migrated_source_hash` char(64) DEFAULT NULL COMMENT 'SHA256 (DB-123)',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`balance_id`),
  UNIQUE KEY `uq_partner_legacy_balances_source` (`tenant_id`,`source_id`),
  UNIQUE KEY `uq_partner_legacy_balances_partner` (`tenant_id`,`partner_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='거래처 이전 프로그램 이월잔액 — 이관만 INSERT (DB-123)';

-- ── 4) item_stock.avg_cost decimal(15,2) → decimal(19,6) (사장님 결재 R6-2 (가)) ──
--    넓히기만 한다: 정수부 13 → 13자리 · 소수 2 → 6자리. 기존 값 손실 0.
SET @cost_ok := (
    SELECT COUNT(*) FROM information_schema.columns
    WHERE table_schema = DATABASE()
      AND table_name = 'item_stock'
      AND column_name = 'avg_cost'
      AND numeric_precision = 19
      AND numeric_scale = 6
);

SET @ddl := IF(@cost_ok = 0,
    'ALTER TABLE item_stock
       MODIFY COLUMN avg_cost decimal(19,6) NOT NULL DEFAULT 0.000000',
    'SELECT ''DB-123: item_stock.avg_cost already decimal(19,6)'' AS skipped');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ── 되돌리기 (참고 · 실행하지 않는다) ──
-- DROP TABLE legacy_unposted_document_lines; DROP TABLE legacy_unposted_documents; DROP TABLE partner_legacy_balances;
-- ALTER TABLE item_stock MODIFY COLUMN avg_cost decimal(15,2) NOT NULL DEFAULT 0.00;  -- ⚠️ 소수 3~6자리 값 반올림 손실
