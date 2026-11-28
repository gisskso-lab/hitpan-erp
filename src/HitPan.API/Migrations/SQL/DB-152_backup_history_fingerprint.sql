-- ════════════════════════════════════════════════════════════════════
-- DB-152 · backup_history 파일 지문 · 판 표지 칸 — 안전 경로 복원 걸음 ① 「변조 · 판」 판정
--   W-G1-52 갈래 A-2② · 2026-11-28
-- ════════════════════════════════════════════════════════════════════
--
-- 근거
--   작업지시서 docs/운영기록/20261128작_W-G1-52_갈래A2_작업지시서.md §4-1 A-2② 줄(마이그 1 · backup_history 지문 · 종류 칸 · V4)
--   상위     docs/운영기록/20261127작_W-G1-52_최소복구_작업지시서.md §7-1 K5 · K6
--   [1-V]    V4 — 지문(SHA-256) 칸 없음 · 종류는 triggered_by varchar(20) 가 사실상 맡음
--
-- 🔴 무엇을 더하나
--   file_sha256        백업 파일 SHA-256(소문자 16진 64자). 백업이 끝날 때 적는다. NULL = 이 판 전 백업 → 안전 복원에서 고를 수 없음(K6 · G19).
--   schema_fingerprint 판 표지 = schema_migrations(success=1) + EF 이력 식별자 묶음의 SHA-256. 라이브와 다르면 고를 수 없음(K5 · G19).
--   종류 칸은 더하지 않는다 — triggered_by(manual · auto · before_data_reset · pre_restore …)가 이미 맡는다(V4).
--
-- 🔴 행 쓰기 0 — 옛 백업 행은 두 칸 NULL 그대로(지문 없는 옛 백업 = 거절 · CTO).
-- 🔴 #13 DESCRIBE(정적 — 이 PC DB 접속 0): installer/hitpan_db_clean.sql `backup_history` 표 정의
--   (backup_id varchar(36) PK · tenant_id · started_at · finished_at · primary_file · mirror_file · file_size_bytes · status ·
--    error_message · triggered_by varchar(20) NOT NULL DEFAULT 'manual') · 새 칸은 맨 끝 · 출하 DDL 도 같은 자리.
-- #17 새 표 0(해당 없음 · backup_history 는 이미 ENGINE=InnoDB).
-- 멱등: ADD COLUMN IF NOT EXISTS — 두 번 돌려도 같다(G-W13-4 · G-W13-5).
-- ════════════════════════════════════════════════════════════════════

ALTER TABLE `backup_history`
  ADD COLUMN IF NOT EXISTS `file_sha256` char(64) DEFAULT NULL
    COMMENT '백업 파일 SHA-256 소문자 16진(DB-152 · 안전 복원 변조 판정). NULL = 지문 없는 옛 백업 → 안전 복원 거절',
  ADD COLUMN IF NOT EXISTS `schema_fingerprint` char(64) DEFAULT NULL
    COMMENT '판 표지 SHA-256(DB-152 · schema_migrations success=1 + EF 이력). 라이브와 다르면 안전 복원 거절';
