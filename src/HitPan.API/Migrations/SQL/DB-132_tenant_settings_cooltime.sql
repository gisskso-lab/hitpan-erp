-- =============================================================
-- DB-132: 쿨타임(설정 시각 자동 로그아웃) 회사 설정 3칸 (tenant_settings)
-- =============================================================
-- 근거: 작업지시서 docs/운영기록/20260928작4_쿨타임_자동로그아웃_작업지시서.md 2절 A (PM 결재 9/28 · 11/27 재결재)
--       설계문서   docs/설계/erp/쿨타임_자동로그아웃_설계_20260928.md 8절 · 9-1 C-1(정시 퇴근 칸 따로)
--
--   ① cooltime_enabled   자동 로그아웃 스위치. 기본 0 = 꺼짐(오더 Q4 · 관리자가 켠다)
--   ② cooltime_at        자동 로그아웃 시각(서버 PC 벽시계 · 로컬). 00:00 ~ 23:59 전부 허용(Q7)
--   ③ regular_leave_time 정시 퇴근 시각(로컬). 쿨타임으로 끝난 사람의 퇴근을 이 시각으로 적는다(Q1 · C-1)
--   ④ cooltime_changed_at 🆕 11/27 설계 §E E-2(PM 결재 §271) — 마지막으로 켠(0→1) 또는 시각을 바꾼 저장 순간(UTC).
--                        서버 작업은 설정 시각 순간 C 가 이 값 이전(같음 포함)이면 그 C 에서 묻기·묻지 않고 끝내기를 건너뛴다
--                        (저장 뒤 첫 C 부터 적용). NULL = 기록 없음(옛 설정 · 건너뛰지 않음). 저장 함수만 쓴다.
--
-- 행이 없는 고객사 = 꺼짐(서버 작업이 WHERE cooltime_enabled = 1 로만 찾는다).
-- 🚫 건드리지 않는 것: 기존 칸·값·기본값 · 다른 표 · 새 표 없음(#17 해당 없음) · 삭제·이름변경 없음(#1·#37)
-- 대상 DB: hitpan_erp (고객 PC 로컬 ERP DB)
--          신규 설치 단일 진실원 installer/hitpan_db_clean.sql 에 함께 편입했다(#36) —
--          표 정의 + schema_migrations 시드 ('DB-132','clean-ddl',1).
-- #13: 출하 DDL tenant_settings 정의 실측(cooltime_enabled · cooltime_at · regular_leave_time · cooltime_changed_at 없음).
-- 멱등: 네 문장 모두 IF NOT EXISTS — 다시 돌려도 결과가 같다. (11/27 E-2 로 ④ 1문장 덧 — 이 파일은 main 머지·게시 전)
-- 되돌림: 이전 판은 이 칸을 읽지 않는다 ⇒ 쿨타임이 조용히 멈춘다. DB 복원 불필요.
-- 비상 끔: 설정 화면 스위치(또는 UPDATE tenant_settings SET cooltime_enabled = 0 WHERE tenant_id = '…')
--          ⚠️ 이 주석에는 문장 끝 기호를 일부러 적지 않았다 — 실행기가 주석 안의 기호로 문장을 가르지 않게.
-- =============================================================

ALTER TABLE tenant_settings
    ADD COLUMN IF NOT EXISTS cooltime_enabled TINYINT(1) NOT NULL DEFAULT 0
        COMMENT '설정 시각 자동 로그아웃(PC 만). 0=끔(기본) · 1=켬. 행 없음도 끔 (DB-132)';

ALTER TABLE tenant_settings
    ADD COLUMN IF NOT EXISTS cooltime_at TIME NULL DEFAULT NULL
        COMMENT '자동 로그아웃 시각(서버 PC 로컬 시각 · 00:00~23:59). NULL=미설정 — 켜려면 필수 (DB-132)';

ALTER TABLE tenant_settings
    ADD COLUMN IF NOT EXISTS regular_leave_time TIME NULL DEFAULT NULL
        COMMENT '정시 퇴근 시각(로컬). 자동 로그아웃으로 끝난 사람의 퇴근을 이 시각으로 적는다. NULL=미설정 (DB-132)';

ALTER TABLE tenant_settings
    ADD COLUMN IF NOT EXISTS cooltime_changed_at DATETIME(6) NULL DEFAULT NULL
        COMMENT '자동 로그아웃을 켜거나 시각을 바꾼 저장 순간(UTC). 이 순간 이전의 설정 시각에서는 묻지도 끝내지도 않는다(저장 뒤 첫 설정 시각부터). NULL=기록 없음 (DB-132)';
