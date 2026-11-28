-- =============================================================
-- DB-134: 퇴근 기록이 어디서 왔나 (attendance.check_out_source)
-- =============================================================
-- 근거: 작업지시서 docs/운영기록/20260928작4_쿨타임_자동로그아웃_작업지시서.md 2절 A · F
--       설계문서   docs/설계/erp/쿨타임_자동로그아웃_설계_20260928.md 6절 · D-2(안 A · 대표 결재 11/27)
--
--   값: NULL           = 사람이 만든 기록(손 로그아웃 · [퇴근] · 대신 퇴근) — 기존 행 전부
--       cooltime       = 자동 로그아웃으로 정시 퇴근 시각을 적음(화면 「자동」)
--       cooltime_blank = 자동 로그아웃이 됐지만 정시 퇴근 시각이 아직 안 와 퇴근을 비워 둠(C-2 (다))
--       edited         = 인사담당이 고침(화면 「고침」)
--   손 퇴근이 cooltime_blank 행을 채우면 표시를 지운다(NULL · 설계 D-2 안 A).
--
-- 🚫 건드리지 않는 것: 기존 칸·색인·값 · 다른 표 · 새 표 없음(#17 해당 없음) · 삭제·이름변경 없음(#1·#37)
-- 대상 DB: hitpan_erp (고객 PC 로컬 ERP DB)
--          신규 설치 단일 진실원 installer/hitpan_db_clean.sql 에 함께 편입했다(#36) —
--          표 정의 + schema_migrations 시드 ('DB-134','clean-ddl',1).
-- #13: 출하 DDL attendance 정의 실측(attendance_id · tenant_id · employee_id · work_date · check_in ·
--      check_out · work_hours · status · memo · created_at · updated_at · check_out_source 없음).
-- 멱등: IF NOT EXISTS — 다시 돌려도 결과가 같다.
-- 되돌림: 이전 판은 이 칸을 읽지 않는다. 이미 적힌 값은 남는다(퇴근 시각 자체는 정상 값). DB 복원 불필요.
-- =============================================================

ALTER TABLE attendance
    ADD COLUMN IF NOT EXISTS check_out_source VARCHAR(20) NULL DEFAULT NULL
        COMMENT '퇴근 기록 출처. NULL=사람 · cooltime=자동 로그아웃(정시 퇴근) · cooltime_blank=자동 로그아웃·퇴근 빈칸 · edited=인사담당 고침 (DB-134)';
