-- ============================================================================
-- 32_backoffice_s2_owner.sql — S-2 대표 아이디·이메일 수집 칸 (작14 B-0)
--
-- 근거 (헌법 #42 — 문서가 근거다):
--   · 작업지시서  docs/운영기록/20261008작14_백오피스_메뉴재구성_CS쪽지_AI자동화_작업지시서.md §3 B-0
--   · DB 명세     docs/설계/백오피스/20261006_DB명세서_백오피스재설계.md  (F-10 · 사장님 결재 10/6)
--   · 10/5 사장님 「명심」 — 백오피스는 대표 아이디·이메일을 반드시 수집한다
--
-- 3칸 분리 설계 (안철수③ · 죽은 칸 K-2 `tenants.email` 에 합치지 않는다):
--   · owner_account_id  = ERP 부모계정 로그인 아이디 (개통 후 첫 보고로 수집)
--   · owner_email       = 가입 시 대표 이메일 (랜딩 가입 입력값)
--   · landing_signups.email = 랜딩 이메일 (기존 — 여기서 손대지 않음)
--
-- #40 — 비번·해시·토큰 컬럼 신설 금지: 아이디·이메일·시각·경로 4칸뿐이다.
-- #37 — 추가만, 제거 0. 멱등(IF NOT EXISTS)이라 재실행 안전.
-- ============================================================================

ALTER TABLE tenants
    ADD COLUMN IF NOT EXISTS owner_account_id   VARCHAR(100) NULL DEFAULT NULL
        COMMENT 'ERP 부모계정 로그인 아이디 — 개통 후 첫 보고(S-2)로 수집 · 비번류 절대 금지(#40)',
    ADD COLUMN IF NOT EXISTS owner_email        VARCHAR(255) NULL DEFAULT NULL
        COMMENT '가입 시 대표 이메일 — 랜딩 가입 입력값 · 죽은 칸 email(K-2)과 별개',
    ADD COLUMN IF NOT EXISTS owner_collected_at DATETIME     NULL DEFAULT NULL
        COMMENT '대표 아이디 수집 시각',
    ADD COLUMN IF NOT EXISTS owner_collect_path VARCHAR(30)  NULL DEFAULT NULL
        COMMENT '대표 아이디 수집 경로 (erp_first_report 등)';
