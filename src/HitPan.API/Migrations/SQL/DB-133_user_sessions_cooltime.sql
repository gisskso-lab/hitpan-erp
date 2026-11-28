-- =============================================================
-- DB-133: 로그인 한 건마다 쿨타임 상태 2칸 (user_sessions)
-- =============================================================
-- 근거: 작업지시서 docs/운영기록/20260928작4_쿨타임_자동로그아웃_작업지시서.md 2절 A
--       설계문서   docs/설계/erp/쿨타임_자동로그아웃_설계_20260928.md 3절 · 8절 · D-5
--
--   ① cooltime_asked_at     서버가 「계속하시겠습니까」 를 물은 순간(UTC · DB 시계 UTC_TIMESTAMP(6)). NULL=안 물음
--   ② cooltime_snooze_until 이 순간 전에는 다시 묻지 않는다([계속] = 지금 + 1시간 · UTC). NULL=없음
--
-- 세션 행은 로그아웃 때 지워지므로 다음 로그인은 두 칸이 비어 새로 시작한다.
-- 시각 기준: 기존 login_at · expires_at 과 같은 UTC(작2 절M 규칙 · 설계 D-5).
-- 🚫 건드리지 않는 것: 기존 칸·색인·값 · 다른 표 · 새 표 없음(#17 해당 없음) · 삭제·이름변경 없음(#1·#37)
-- 대상 DB: hitpan_erp (고객 PC 로컬 ERP DB)
--          신규 설치 단일 진실원 installer/hitpan_db_clean.sql 에 함께 편입했다(#36) —
--          표 정의 + schema_migrations 시드 ('DB-133','clean-ddl',1).
-- #13: 출하 DDL user_sessions 정의 실측(session_id · user_id · tenant_id · login_at · last_active_at ·
--      is_active · expires_at · device_kind · 쿨타임 칸 없음 · datetime(6) 은 login_at 과 같은 정밀도).
-- 멱등: 두 문장 모두 IF NOT EXISTS — 다시 돌려도 결과가 같다.
-- 되돌림: 이전 판은 이 칸을 읽지 않는다(INSERT 가 칸을 안 적어 NULL). DB 복원 불필요.
-- =============================================================

ALTER TABLE user_sessions
    ADD COLUMN IF NOT EXISTS cooltime_asked_at DATETIME(6) NULL DEFAULT NULL
        COMMENT '자동 로그아웃 물은 순간(UTC). NULL=안 물음. 65초 안에 [계속]이 없으면 서버가 끝낸다 (DB-133)';

ALTER TABLE user_sessions
    ADD COLUMN IF NOT EXISTS cooltime_snooze_until DATETIME(6) NULL DEFAULT NULL
        COMMENT '이 순간(UTC) 전에는 다시 묻지 않는다([계속] = 지금 + 1시간). NULL=없음 (DB-133)';
