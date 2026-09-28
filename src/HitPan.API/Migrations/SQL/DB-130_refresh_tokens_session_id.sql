-- =============================================================
-- DB-130: refresh 토큰에 「어느 로그인의 것인가」 칸 신설 (refresh_tokens.session_id)
-- =============================================================
-- 근거: 작업지시서 docs/운영기록/20260928작2_로그아웃기기단위_F4_작업지시서.md 절A (PM 결재 · 개정1)
--       설계문서   docs/설계/erp/로그아웃기기단위_F4_설계_20260928.md §2 「㉠ 채택」
--       선행검증   docs/검증/선행/20260928_선행검증서_별건3_로그아웃_F4_R3.md §1·§2
--
-- ─────────────────────────────────────────────────────────────
-- 무엇이 문제였나
-- ─────────────────────────────────────────────────────────────
--   로그인은 그 계정의 refresh 토큰을 **전부 지우고**(`DELETE ... WHERE user_id`),
--   로그아웃은 그 계정의 refresh 토큰을 **전부 폐기**했다(`UPDATE ... WHERE user_id`).
--   ⇒ 휴대폰에서 로그인·로그아웃만 해도 일하던 PC 가 갱신 때(8h 뒤) 튕겼다.
--   사장님 원문: *"휴대폰에서 로그아웃을 하던, pc에서 로그아웃을 하던 끊기면 안되지."*
--   refresh 행에 「이 토큰이 어느 로그인(sid)의 것인가」가 없어서 **가를 수 없었다.**
--
-- ─────────────────────────────────────────────────────────────
-- 이 파일이 하는 일
-- ─────────────────────────────────────────────────────────────
--   ① session_id 칸 — 그 refresh JWT 의 `sid` 클레임과 같은 값(불변식 · 설계 §2).
--      `sid` 를 안 싣는 로그인(세션 기록 실패)·이 파일 이전의 옛 행은 NULL.
--   ② 색인 (user_id, session_id) — 로그아웃 `WHERE user_id AND session_id` ·
--      죽은 PC 정리 `WHERE user_id AND session_id IN (...)` 두 자리가 탄다.
--
-- 🚫 건드리지 않는 것: 기존 칸·색인·값 · 다른 표 · 새 표 없음(#17 해당 없음) · 삭제·이름변경 없음(#1·#37)
--
-- 대상 DB: hitpan_erp (고객 PC 로컬 ERP DB)
--          신규 설치 단일 진실원 installer/hitpan_db_clean.sql 에 함께 편입했다(#36) —
--          표 정의 + `schema_migrations` 시드 ('DB-130','clean-ddl',1).
-- #13: 출하 DDL `refresh_tokens` 정의 실측(token_id·user_id·token_hash·ip_address·user_agent·
--      expires_at·is_revoked·created_at · session_id 없음 · utf8mb4_unicode_ci).
--      `user_sessions.session_id` = varchar(36) · 같은 표 기본 정렬 ⇒ 같은 폭·같은 정렬로 맞췄다.
-- 멱등: 두 문장 모두 IF NOT EXISTS — 다시 돌려도 결과가 같다.
-- 되돌림: 1.3.46 은 이 칸을 읽지 않는다(INSERT 가 칸을 안 적어 NULL). DB 복원 불필요.
-- =============================================================

ALTER TABLE refresh_tokens
    ADD COLUMN IF NOT EXISTS session_id VARCHAR(36) NULL DEFAULT NULL
        COMMENT '이 refresh 토큰을 낳은 로그인의 세션 번호(JWT sid). 로그아웃·죽은 PC 정리가 이 로그인만 가른다. NULL=sid 미탑재·옛 행 (DB-130)';

ALTER TABLE refresh_tokens
    ADD INDEX IF NOT EXISTS idx_user_session (user_id, session_id);
