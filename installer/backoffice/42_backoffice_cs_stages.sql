-- ============================================================================
-- 42_backoffice_cs_stages.sql — CS 진행 4단계 + 응대 유형 3종 + 만족도 칸
--
-- 근거(#42): 사장님 지시 2026-10-08
--   ① 진행 **4단계**: 읽지 않음 · 처리되지 않음 · 처리 완료 · CS평가 완료
--   ② 응대 **유형 3종**: 쪽지로 응대 · 전화로 응대 · 원격지원
--   ③ 읽지 않은/처리되지 않은 건수를 화면 아래쪽에 숫자로 표기(빨간색)
--   ④ (워크플로우 7단계) CS 처리 고객에게 **만족도 평가** 받기 — 칸을 지금 만든다
--
-- 🔴 왜 지금 칸부터 만드나(#9 DB는 미리 다): 만족도·응대 유형은 **소급이 안 되는 자료**다.
--    지금 안 받으면 뒤에 CS 실적·AI 재료를 만들 때 과거가 비어 있다.
--
-- 🔴 두 축을 섞지 않는다
--    · received_channel = **들어온 길**(쪽지/전화) — 「쪽지가 전화를 줄였나」를 세는 재료
--    · handled_via      = **처리한 길**(쪽지/전화/원격지원) — 품과 난이도를 세는 재료
--    한 칸으로 합치면 둘 중 하나는 영영 못 센다.
--
-- 규칙: #1 추가만 · 멱등 IF NOT EXISTS · #40 비번류 칸 0 · #22 업무 데이터 0
-- ============================================================================

ALTER TABLE bo_cs_tickets
    ADD COLUMN IF NOT EXISTS read_at datetime(6) NULL
        COMMENT '본사·대리점이 처음 열어 본 시각 — NULL = 아직 아무도 안 봄(①단계)',
    ADD COLUMN IF NOT EXISTS read_by varchar(36) NULL
        COMMENT '처음 열어 본 사람 — 덮어쓰지 않는다(최초 1회만)',
    ADD COLUMN IF NOT EXISTS handled_via varchar(20) NULL
        COMMENT '응대 유형 message/phone/remote — 완료로 넘길 때 필수(사장님 3단계)',
    ADD COLUMN IF NOT EXISTS rating tinyint NULL
        COMMENT '만족도 1 아쉬워요 / 2 보통 / 3 좋아요 — 고객이 ERP 에서 고름(강제 아님)',
    ADD COLUMN IF NOT EXISTS rated_at datetime(6) NULL
        COMMENT '평가 받은 시각 — NOT NULL 이면 ④ CS평가 완료 단계',
    ADD COLUMN IF NOT EXISTS rating_comment varchar(500) NULL
        COMMENT '고객 한 줄 — 금지필드 검사 통과분만(수신측 문③ 그대로)';

-- 4단계 세기·목록 필터가 읽는 자리
ALTER TABLE bo_cs_tickets
    ADD KEY IF NOT EXISTS idx_bo_cs_stage (read_at, status, rated_at);
