-- =============================================================
-- DB-91: AI 연동 3사 확장 — 클로드AI · 챗GPT · 제미나이
-- =============================================================
-- 근거: 사장님 지시 2026-08-12
--       *"ai연동도우미 수정 = 기존 : 클로드API만 지원 -> 수정 : 클로드, 챗지피티, 제미나이API까지 받을 수 있게"*
--       화면 표기 결재: *"모두 결재 문구는 / 클로드AI / 챗GPT / 제미나이"*
-- 작업지시서: docs/운영기록/20260812작1_AI연동_3사확장_작업지시서.md
--
-- 🔴 이 파일의 내력 (다음 사람이 헷갈리지 않게)
--   2026-08-12 최초 작성 시 PM 이 이 SQL 을 `installer/migrations/` 에 두었다. **틀린 자리다.**
--   고객 PC 에 실제로 적용되는 마이그레이션은 **이 폴더(src/HitPan.API/Migrations/SQL/DB-NN_*.sql)** 뿐이고,
--   csproj 가 이 폴더를 게시물(payload)로 복사한다.
--   그래서 1.2.71 을 게시하고 샌드박스가 업데이트까지 받았는데도 **컬럼이 안 생겼고**,
--   AI 도우미 연동 화면이 "연동 상태를 불러오지 못했습니다" 로 죽었다(키 입력칸도 안 나옴).
--   ⇒ 같은 내용을 올바른 자리·이름으로 다시 넣는다. `installer/migrations/` 의 것은 삭제한다.
--
-- 무엇을 넣나
--   ① openai_*  5컬럼 — 챗GPT 키
--   ② google_*  5컬럼 — 제미나이 키
--   ③ ai_provider 1컬럼 — 지금 사용 중인 공급자
--
-- 🚨 헌법 #37 — 기존 anthropic_* 5컬럼은 **한 글자도 지우지 않는다.**
--    "이제 안 읽힌다" 고 지우면 이미 클로드 키를 쓰던 고객의 키가 통째로 사라진다.
--
-- 🔴 무회귀 보장 — ai_provider 기본값 'anthropic'
--    마이그만 적용되고 고객이 아무 설정도 안 바꿨으면 **종전과 완전히 같게** 동작해야 한다.
--
-- 헌법: #1(추가만) #5(암호화 — 앱 계층 AES-256) #13(DESCRIBE 선행 완료)
--       #17(신규 표 없음) #22(키는 고객 PC 로컬 전용, 본사 전송 0) #36(clean DDL 편입) #37
-- 멱등: ADD COLUMN IF NOT EXISTS — 재실행 안전(업데이트 재시도 대비)
-- =============================================================

-- ── 1) 챗GPT(openai) 키 컬럼 ──────────────────────────────────
--    기존 anthropic_* 5컬럼과 완전히 대칭. 타입·길이 동일.
ALTER TABLE local_subscription
    ADD COLUMN IF NOT EXISTS openai_api_key_encrypted  VARCHAR(512) NULL     COMMENT 'BYOK 챗GPT 키 AES-256 암호화',
    ADD COLUMN IF NOT EXISTS openai_api_key_last4      VARCHAR(8)   NULL     COMMENT '챗GPT 키 마지막 4자리 (UI 표시용)',
    ADD COLUMN IF NOT EXISTS openai_key_status         VARCHAR(20)  NOT NULL DEFAULT 'none' COMMENT 'none / valid / invalid / expired',
    ADD COLUMN IF NOT EXISTS openai_key_saved_at       DATETIME     NULL     COMMENT '챗GPT 키 저장 시각',
    ADD COLUMN IF NOT EXISTS openai_key_verified_at    DATETIME     NULL     COMMENT '챗GPT 키 연결확인 시각 (실제 외부 호출 성공 시각)';

-- ── 2) 제미나이(google) 키 컬럼 ───────────────────────────────
ALTER TABLE local_subscription
    ADD COLUMN IF NOT EXISTS google_api_key_encrypted  VARCHAR(512) NULL     COMMENT 'BYOK 제미나이 키 AES-256 암호화',
    ADD COLUMN IF NOT EXISTS google_api_key_last4      VARCHAR(8)   NULL     COMMENT '제미나이 키 마지막 4자리 (UI 표시용)',
    ADD COLUMN IF NOT EXISTS google_key_status         VARCHAR(20)  NOT NULL DEFAULT 'none' COMMENT 'none / valid / invalid / expired',
    ADD COLUMN IF NOT EXISTS google_key_saved_at       DATETIME     NULL     COMMENT '제미나이 키 저장 시각',
    ADD COLUMN IF NOT EXISTS google_key_verified_at    DATETIME     NULL     COMMENT '제미나이 키 연결확인 시각 (실제 외부 호출 성공 시각)';

-- ── 3) 현재 사용 공급자 ───────────────────────────────────────
--    화면 표기(사장님 결재): anthropic=클로드AI / openai=챗GPT / google=제미나이
ALTER TABLE local_subscription
    ADD COLUMN IF NOT EXISTS ai_provider VARCHAR(20) NOT NULL DEFAULT 'anthropic'
        COMMENT '현재 사용 AI 공급자: anthropic(클로드AI) / openai(챗GPT) / google(제미나이)';

-- ── 4) 기존 행 보정 ───────────────────────────────────────────
--    ADD COLUMN DEFAULT 가 기존 행도 채우지만, 과거 마이그 중단 등으로 빈 값이 남았을 때를 대비한다.
--    (멱등 — 여러 번 실행해도 결과 동일)
UPDATE local_subscription
   SET ai_provider = 'anthropic'
 WHERE ai_provider IS NULL OR ai_provider = '';

UPDATE local_subscription SET openai_key_status = 'none' WHERE openai_key_status IS NULL OR openai_key_status = '';
UPDATE local_subscription SET google_key_status = 'none' WHERE google_key_status IS NULL OR google_key_status = '';

-- =============================================================
-- 적용 후 눈으로 확인할 것
--   DESCRIBE local_subscription;
--     → anthropic_* 5컬럼이 **그대로 살아있어야** 한다 (헌법 #37). 사라졌으면 사고다.
--     → openai_* 5 / google_* 5 / ai_provider 1 = 11컬럼이 늘어야 한다.
--
--   SELECT tenant_id, ai_provider, anthropic_key_status, openai_key_status, google_key_status
--     FROM local_subscription;
--     → 기존 고객: ai_provider='anthropic', anthropic_key_status 는 종전 값 그대로.
-- =============================================================
