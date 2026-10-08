-- ============================================================================
-- 41_backoffice_cs_kb.sql — CS 책상 + 누리집 표 3장 + 티켓 1칸 (작14 C-2·C-3·C-4·C-5)
--
-- 근거(#42): 작업지시서 20261008작14 §4 C-1~C-5
--   + 설계서 20261007_설계서_CS쪽지_AI3사자동화_대소메뉴별 §2-ⓑ(누리집 결재본)
--     · bo_kb_docs(doc_id·title·body_md·status·approved_by·approved_at·source_ticket_id·version)
--       — 🔴 **UPDATE 없이 version 증가로 새 행**(옛 판이 남아야 「누가 언제 왜 고쳤나」가 남는다)
--     · bo_kb_doc_logs INSERT ONLY
--   + C-4: 승인 없으면 **행 불생성** ⇒ CHECK 제약으로 DB 가 거절한다(화면·API 검사만으론 우회된다)
--   + C-5: 외부 AI 잠금장치 fail-closed — 동의 기록이 **있을 때만** 열림 · 테넌트 단위 · INSERT ONLY
--     (선례 = ERP `ai_export_consents`(DB-138) · 백오피스는 ProjectReference 0 이라 **독립 복제**)
--
-- 규칙: #1 추가만 · #17 InnoDB · 멱등 IF NOT EXISTS · #40 비번류 칸 0 · #22 업무 데이터 0
-- ============================================================================

-- 0) 티켓에 「답 약속 시계」 1칸 — C-2(약속시간 기준값은 9.설정 몫 · 여기선 담는 칸만)
--    반자동(사장님 헌법): 서버가 제안값을 넣고 사람이 고친다. NULL = 약속 없음.
ALTER TABLE bo_cs_tickets
    ADD COLUMN IF NOT EXISTS promised_at datetime(6) NULL
        COMMENT '답 약속 시각 — 서버 제안 후 사람 조정 가능(C-2 · N-1)';

-- 1) 누리집 정본 — 「이 증상엔 이렇게」가 쌓이는 창고
--    md 가 정본 형식(쓰는 사람에겐 md 와 똑같다 · 파일은 내보내기용일 뿐 — 설계 §2-ⓔ)
CREATE TABLE IF NOT EXISTS bo_kb_docs (
    doc_id          bigint       NOT NULL AUTO_INCREMENT,
    issue_code      varchar(20)  NOT NULL COMMENT '이슈코드 USE/SET/NET/DAT/UPD/BUG/INS/ETC-번호(C-3)',
    category        varchar(20)  NOT NULL COMMENT '8종 정본(use/set/net/dat/upd/bug/ins/etc — 결-9)',
    title           varchar(200) NOT NULL,
    body_md         text         NOT NULL COMMENT '문제점/해결점 PRD 틀(AI수석 실물) — md 가 저장 형식',
    status          varchar(10)  NOT NULL DEFAULT '초안' COMMENT '초안/승인/폐기',
    version         int          NOT NULL DEFAULT 1 COMMENT 'UPDATE 금지 — 고치면 version+1 새 행',
    approved_by     varchar(36)  NULL COMMENT '🔴 승인 판은 NULL 불가(아래 CHECK) — 사람 확정의 증거',
    approved_at     datetime(6)  NULL,
    source_ticket_id bigint      NULL COMMENT '승격 출처 티켓 — 원 티켓 행은 보존(C-3 병합 원칙)',
    created_by      varchar(36)  NOT NULL,
    created_at      datetime(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (doc_id),
    UNIQUE KEY uq_bo_kb_code_ver (issue_code, version),
    KEY idx_bo_kb_status (status, category),
    KEY idx_bo_kb_code (issue_code, version),
    -- 🔴 C-4 「승인 없으면 행 불생성」 — 승인 판에 승인자·승인시각이 없으면 DB 가 거절한다.
    --    (API 를 직접 불러 우회하려 해도 여기서 막힌다 — 설계 §2-ⓑ 승인 API 와 같은 커밋)
    CONSTRAINT ck_bo_kb_approved CHECK (status <> '승인' OR (approved_by IS NOT NULL AND approved_at IS NOT NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='CS 누리집 정본 — version 증가로 이력 보존 · 승인 판만 재사용 재료';

-- 2) 누리집 기록 — INSERT ONLY (승격 초안 생성·등재·승인·거부·폐기 전부 한 줄씩)
CREATE TABLE IF NOT EXISTS bo_kb_doc_logs (
    id         bigint      NOT NULL AUTO_INCREMENT,
    doc_id     bigint      NULL COMMENT '초안 생성 시점엔 행이 없다 — 그래서 NULL 허용',
    issue_code varchar(20) NULL,
    action     varchar(20) NOT NULL COMMENT 'promote_draft/save_draft/approve/reject/revise/discard',
    actor_id   varchar(36) NOT NULL,
    rule_code  varchar(40) NULL COMMENT '거부 사유코드만 — 걸린 값은 저장 안 함(#22·#40)',
    acted_at   datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (id),
    KEY idx_bo_kb_log_doc (doc_id, acted_at),
    KEY idx_bo_kb_log_action (action, acted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='누리집 기록 — INSERT ONLY · 승인 거부도 남는다';

-- 🔴 사장님 지시 2026-10-08 — 원문 그대로 보존(지우지 말 것)
-- 누리집을 만들고 AI를 통한 CS자동화 즉시 실행하지 않고, 준비중인 이유는 추후 쪽지 형태에서 챗봇 형태로 백오피스가 히트판에 1:1로 CS를 자동화 처리 할 수 있도록 하는 준비단계이다. //
--
-- ⇒ 이 표(동의)가 그 전환의 열쇠구멍이다. 행이 0건인 동안은 챗봇이든 초안이든 밖으로 한 글자도 안 나간다.

-- 3) 외부 AI 반출 동의 — 🔴 fail-closed 잠금장치의 유일한 열쇠구멍 (C-5)
--    행이 **있을 때만** 열린다. 표 부재·조회 예외·0건 = 전부 닫힘.
--    지금은 동의 절차 자체가 없으므로 사실상 전면 차단이고, 그게 결재된 상태다
--    (2차 열쇠 = 법무 4건 + 결-7 · 그 전 임의 INSERT·동의 화면 선개발 금지).
CREATE TABLE IF NOT EXISTS bo_ai_export_consents (
    id         bigint      NOT NULL AUTO_INCREMENT,
    tenant_id  varchar(36) NOT NULL COMMENT '테넌트 단위 — 전역 스위치 없음(한 곳 동의가 다른 곳을 열지 않는다)',
    granted_by varchar(36) NOT NULL COMMENT '동의를 받은 사람(본사 담당) — 누가 열었나',
    granted_at datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    legal_ref  varchar(100) NULL COMMENT '약관·계약 판 식별(법무 4건 뒤 채움)',
    PRIMARY KEY (id),
    KEY idx_bo_ai_consent_tenant (tenant_id, granted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='외부 AI 반출 동의 — INSERT ONLY · 행 존재가 유일한 개방 조건(fail-closed)';
