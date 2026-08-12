-- ============================================================================
-- 히트판 ERP — AI 연동 3사(클로드AI·챗GPT·제미나이) 확장 마이그레이션
-- ============================================================================
-- 문서번호: 20260812작1 / 작업지시서: docs/운영기록/20260812작1_AI연동_3사확장_작업지시서.md
-- 작성: DB 매니저 + DB 개발자 (사장님 결재 2026-08-12)
--
-- 사장님 오더:
--   "ai연동도우미 수정 = 기존 : 클로드API만 지원 -> 수정 : 클로드, 챗지피티, 제미나이API까지 받을 수 있게"
--   화면 표기 결재: 클로드AI / 챗GPT / 제미나이
--
-- 헌법 정합:
--   #1  — 덮어쓰기 금지. 기존 anthropic_* 5컬럼은 이름·타입 그대로 유지하고 신규 컬럼만 추가한다.
--   #5  — API 키는 암호화 필수. 신규 컬럼도 기존과 동일하게 앱 계층 IEncryptionService(AES-256) 저장.
--   #13 — 실DB DESCRIBE 완료(2026-08-12). local_subscription 기존 20컬럼 실측 확인 후 작성.
--   #22 — 고객 API 키는 고객 PC 로컬 전용. 본사 전송 0건.
--   #36 — 출하 DDL(hitpan_db_clean.sql)에 동일 정의 편입.
--   #37 — 🚨 anthropic_* 컬럼 제거 금지. "이제 안 읽힌다"고 지우면 기존 고객 키가 사라진다.
--
-- 적용 대상 DB: hitpan_erp (고객사 PC 로컬)
-- 실행 주체: 설치 마법사 / 업데이트 워치독 (본 SQL은 형상관리 정의)
-- 멱등성: ALTER ... ADD COLUMN IF NOT EXISTS — 재실행 안전.
--
-- ⚠️ 회귀 주의: 이 마이그레이션을 적용해도 기존 클로드 고객은 키를 다시 넣지 않아도 된다.
--    기존 anthropic_* 컬럼을 그대로 읽기 때문이다. ai_provider 기본값 'anthropic' 이 그것을 보장한다.
-- ============================================================================

-- ────────────────────────────────────────────────────────────────────────
-- 1) local_subscription — 챗GPT(openai) 키 컬럼 추가
--    기존 anthropic_* 5컬럼과 완전히 대칭 구조. 타입·길이 동일.
-- ────────────────────────────────────────────────────────────────────────
ALTER TABLE local_subscription
    ADD COLUMN IF NOT EXISTS openai_api_key_encrypted  VARCHAR(512) NULL     COMMENT 'BYOK 챗GPT 키 AES-256 암호화',
    ADD COLUMN IF NOT EXISTS openai_api_key_last4      VARCHAR(8)   NULL     COMMENT '챗GPT 키 마지막 4자리 (UI 표시용)',
    ADD COLUMN IF NOT EXISTS openai_key_status         VARCHAR(20)  NOT NULL DEFAULT 'none' COMMENT 'none / valid / invalid / expired',
    ADD COLUMN IF NOT EXISTS openai_key_saved_at       DATETIME     NULL     COMMENT '챗GPT 키 저장 시각',
    ADD COLUMN IF NOT EXISTS openai_key_verified_at    DATETIME     NULL     COMMENT '챗GPT 키 연결확인 시각 (실제 외부 호출 성공 시각)';

-- ────────────────────────────────────────────────────────────────────────
-- 2) local_subscription — 제미나이(google) 키 컬럼 추가
-- ────────────────────────────────────────────────────────────────────────
ALTER TABLE local_subscription
    ADD COLUMN IF NOT EXISTS google_api_key_encrypted  VARCHAR(512) NULL     COMMENT 'BYOK 제미나이 키 AES-256 암호화',
    ADD COLUMN IF NOT EXISTS google_api_key_last4      VARCHAR(8)   NULL     COMMENT '제미나이 키 마지막 4자리 (UI 표시용)',
    ADD COLUMN IF NOT EXISTS google_key_status         VARCHAR(20)  NOT NULL DEFAULT 'none' COMMENT 'none / valid / invalid / expired',
    ADD COLUMN IF NOT EXISTS google_key_saved_at       DATETIME     NULL     COMMENT '제미나이 키 저장 시각',
    ADD COLUMN IF NOT EXISTS google_key_verified_at    DATETIME     NULL     COMMENT '제미나이 키 연결확인 시각 (실제 외부 호출 성공 시각)';

-- ────────────────────────────────────────────────────────────────────────
-- 3) local_subscription — 현재 사용 공급자 1칸
--    🔴 기본값 'anthropic' — 이 값이 기존 고객의 무회귀를 보장한다.
--       마이그레이션만 적용되고 고객이 아무 설정도 안 바꿨을 때,
--       종전과 똑같이 클로드AI 키로 동작해야 한다.
--    화면 표기(사장님 결재): anthropic=클로드AI / openai=챗GPT / google=제미나이
-- ────────────────────────────────────────────────────────────────────────
ALTER TABLE local_subscription
    ADD COLUMN IF NOT EXISTS ai_provider VARCHAR(20) NOT NULL DEFAULT 'anthropic'
        COMMENT '현재 사용 AI 공급자: anthropic(클로드AI) / openai(챗GPT) / google(제미나이)';

-- ────────────────────────────────────────────────────────────────────────
-- 4) 기존 행 보정
--    ADD COLUMN DEFAULT 는 기존 행에도 기본값을 채우지만,
--    과거 마이그레이션 중단 등으로 빈 값이 남았을 경우를 대비해 명시적으로 보정한다.
--    (멱등 — 여러 번 실행해도 결과 동일)
-- ────────────────────────────────────────────────────────────────────────
UPDATE local_subscription
   SET ai_provider = 'anthropic'
 WHERE ai_provider IS NULL OR ai_provider = '';

UPDATE local_subscription SET openai_key_status = 'none' WHERE openai_key_status IS NULL OR openai_key_status = '';
UPDATE local_subscription SET google_key_status = 'none' WHERE google_key_status IS NULL OR google_key_status = '';

-- ============================================================================
-- 검증 쿼리 (수동 확인용 — 실행 후 눈으로 볼 것)
--
--   DESCRIBE local_subscription;
--     → anthropic_* 5컬럼이 그대로 살아있어야 한다 (헌법 #37). 사라졌으면 사고.
--     → openai_* 5 / google_* 5 / ai_provider 1 = 총 11컬럼이 늘어야 한다.
--
--   SELECT tenant_id, ai_provider, anthropic_key_status, openai_key_status, google_key_status
--     FROM local_subscription;
--     → 기존 고객: ai_provider='anthropic', anthropic_key_status 는 종전 값 그대로.
-- ============================================================================
