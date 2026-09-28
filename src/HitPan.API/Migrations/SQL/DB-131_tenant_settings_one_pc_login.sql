-- =============================================================
-- DB-131: PC 동시로그인 차단을 **새 칸으로 켠다** (tenant_settings.enforce_one_pc_login)
-- =============================================================
-- 근거: 작업지시서 docs/운영기록/20260928작2_로그아웃기기단위_F4_작업지시서.md 절A (PM 결재 · 개정1)
--       설계문서   docs/설계/erp/로그아웃기기단위_F4_설계_20260928.md §5 · §9-1 K-1 (가)
--       사장님 원문 *"한 계정에서 pc접속은 무조건 1대, 모바일 접속은 FREE"*
--
-- ─────────────────────────────────────────────────────────────
-- 왜 옛 칸(enforce_single_pc_login)을 1 로 올리지 않고 새 칸을 만드나 — K-1 (가)
-- ─────────────────────────────────────────────────────────────
--   되돌림이 싸야 한다(사장님 게시 방침). 1.3.46 은 옛 칸만 읽는다.
--     · 새 칸으로 켜면 → 1.3.46 재게시 = 옛 칸 0 그대로 = **1.3.46 과 똑같이 꺼진 상태**로 돌아간다.
--     · 옛 칸을 1 로 올리면 → 1.3.46 재게시 = 검증 안 된 「1.3.46 + 켬」(로그인·로그아웃 전삭 결함 포함).
--   ⇒ 옛 칸은 **무접촉**이다(값·기본값 모두). 안 읽혀도 지우지 않는다(#37 정신 · 되돌림 안전장치).
--
-- ─────────────────────────────────────────────────────────────
-- 문장 두 개 — DB-128·DB-129 가 남긴 교훈 그대로
-- ─────────────────────────────────────────────────────────────
--   ① ADD COLUMN IF NOT EXISTS … NOT NULL DEFAULT 1
--      칸이 없던 DB: MariaDB 가 **있던 행 전부에 1 을 채운다**(DB-129 머리말 실측 · MariaDB 11.4.10).
--   ② MODIFY COLUMN … NOT NULL DEFAULT 1
--      칸이 이미 있던 DB(손으로 만든 것·부분 적용)에서 ① 이 통째로 건너뛰어졌을 때 **기본값만** 고정한다.
--      저장된 값은 손대지 않는다 — 0 은 「비상 끔」(작지 §6 롤백 · `enforce_one_pc_login = 0` 한 줄)이다.
--   ③ UPDATE 는 넣지 않는다 — 비상으로 끈 고객사를 다음 업데이트가 소리 없이 다시 켜면 안 된다(DB-129 ③ 과 같은 이유).
--
-- ⚠️ 행이 아예 없는 고객사(설정을 한 번도 저장 안 함)는 이 파일로 켜지지 않는다 —
--    그 경우는 코드가 받는다: 새 칸 조회 두 자리가 `?? 1`(행 없음 = 켬 · K-2 (가)).
--
-- 🚫 건드리지 않는 것: enforce_single_pc_login(옛 칸) · enforce_session_validity · enforce_tenant_session_limit ·
--    다른 표 · 새 표 없음(#17 해당 없음) · 삭제·이름변경 없음(#1·#37)
--
-- 대상 DB: hitpan_erp (고객 PC 로컬 ERP DB)
--          신규 설치 단일 진실원 installer/hitpan_db_clean.sql 에 함께 편입했다(#36) —
--          표 정의 + `schema_migrations` 시드 ('DB-131','clean-ddl',1).
-- #13: 출하 DDL `tenant_settings` 정의 실측(enforce_tenant_session_limit·enforce_single_pc_login·
--      enforce_session_validity 가 tinyint(1) NOT NULL · enforce_one_pc_login 없음).
-- 멱등: ① IF NOT EXISTS · ② 같은 정의 재적용 안전. 다시 돌려도 결과가 같다.
-- 비상 끔: UPDATE tenant_settings SET enforce_one_pc_login = 0 WHERE tenant_id = '…'
--          (행이 없는 고객사는 INSERT INTO tenant_settings (tenant_id, enforce_one_pc_login) VALUES ('…', 0))
--          ⚠️ 이 주석에는 문장 끝 기호를 일부러 적지 않았다 — 실행기가 주석 안의 기호로 문장을 가르지 않게.
-- =============================================================

-- ① 칸 신설 — 없던 DB 에서는 이 한 문장이 있던 행까지 1 로 채운다
ALTER TABLE tenant_settings
    ADD COLUMN IF NOT EXISTS enforce_one_pc_login TINYINT(1) NOT NULL DEFAULT 1
        COMMENT '같은 계정 PC 동시로그인 차단(PC 1대 · 모바일 FREE · 먼저 들어온 PC 가 이긴다). 1=켬(기본) · 0=비상 끔. 행 없음도 켬으로 읽는다 (DB-131)';

-- ② 칸이 이미 있던 DB 에서 ① 이 건너뛰어졌을 때 기본값을 1 로 고정한다(값은 건드리지 않는다)
ALTER TABLE tenant_settings
    MODIFY COLUMN enforce_one_pc_login TINYINT(1) NOT NULL DEFAULT 1
        COMMENT '같은 계정 PC 동시로그인 차단(PC 1대 · 모바일 FREE · 먼저 들어온 PC 가 이긴다). 1=켬(기본) · 0=비상 끔. 행 없음도 켬으로 읽는다 (DB-131)';
