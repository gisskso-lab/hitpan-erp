-- ============================================================================
-- 40_backoffice_cs.sql — CS 쪽지 수신측 표 4장 (작14 B-5·B-6·B-7)
--
-- 근거(#42): 작업지시서 20261008작14 §3 B-5~B-8 · DB명세서 §3(bo_cs_tickets 결재본)
--   + 설계서 §3-2(더하는 2칸 = client_ticket_uid·received_channel — 멱등·관찰)
--   + 결-5 ㉯(사장님 10/8): 자유 본문 1차 포함 — 쟁점-3(summary 제외)을 같은 결재로 갱신
--     ⇒ body 칸 신설. 🔴 금지필드 문③(수신 최종 판정) 게이트와 **같은 커밋**이 결재 조건이다.
--   + B-6(CTO 조건②): 문과 자물쇠 같은 커밋 — 수신측 거부로그 표가 그 자물쇠의 증거다.
--
-- 규칙: #1 추가만 · #17 InnoDB · 멱등 IF NOT EXISTS · #40 비번류 칸 0
-- 보존: 본사 설정 콤보(90일/6개월/1년/3년 · Q-5) — 파기 배치는 F-25(후속 사이클)
-- ============================================================================

-- 1) 티켓 — ERP 쪽지가 멱등으로 꽂히는 자리
CREATE TABLE IF NOT EXISTS bo_cs_tickets (
    id                bigint       NOT NULL AUTO_INCREMENT,
    tenant_id         varchar(36)  NOT NULL,
    reseller_id       varchar(36)  NULL DEFAULT NULL COMMENT 'NULL = 직판 → 본사 1차(F-14 결재 조건ⓐ)',
    client_ticket_uid varchar(36)  NOT NULL COMMENT 'ERP 발급 cs_request_id — 멱등의 유일한 근거(설계 §3-2)',
    -- varchar(20): 설계 초안(§3-2)은 10 이었으나 'erp_message'(11자)가 STRICT 에서 잘린다 —
    -- CI 양성 게이트 G-B5-0 이 실측으로 잡음(2026-10-08 · Data too long). 값은 그대로, 칸만 넓힘.
    received_channel  varchar(20)  NOT NULL DEFAULT 'phone' COMMENT 'phone/erp_message — 쪽지가 전화를 줄였나를 세는 재료',
    category          varchar(20)  NOT NULL COMMENT '8종 정본(use/set/net/dat/upd/bug/ins/etc — 결-9) · 사람이 재분류 가능',
    sub_tag           varchar(30)  NOT NULL COMMENT '사전정의 목록만 — 자유입력 불가(쟁점-3)',
    shape_tag         varchar(20)  NULL COMMENT '7모양 보조 태그(결-9 보조축) — ERP 가 보낸 고객 언어',
    body              varchar(2000) NULL COMMENT '결-5 ㉯ 본문 — 문③ 통과분만 저장(금지필드 게이트 같은 커밋)',
    status            varchar(10)  NOT NULL DEFAULT '접수' COMMENT '접수/처리중/보류/완료 + 고객답대기(결-1)',
    handler_type      varchar(10)  NULL COMMENT '대리점1차/본사2차',
    handler_id        varchar(36)  NULL,
    screen_code       varchar(40)  NULL,
    erp_version       varchar(20)  NULL,
    kb_doc_id         bigint       NULL,
    auto_class        varchar(20)  NULL COMMENT '규칙 기반 1차 분류(F-21 · 외부 호출 0)',
    auto_confidence   decimal(5,2) NULL,
    rework_of_id      bigint       NULL,
    incident_id       bigint       NULL,
    received_at       datetime(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    completed_at      datetime(6)  NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_bo_cs_client (tenant_id, client_ticket_uid),
    KEY idx_bo_cs_tenant (tenant_id),
    KEY idx_bo_cs_reseller (reseller_id, status),
    KEY idx_bo_cs_status_time (status, received_at) COMMENT '기간+상태 조회(설계 §3-3 권고)'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='CS 티켓 — ERP 쪽지·전화 접수 공용 · 멱등 = (tenant, client_ticket_uid)';

-- 2) 상태 전이 기록 — INSERT ONLY · 전이 자체는 서버 1곳(F-15)
CREATE TABLE IF NOT EXISTS bo_cs_ticket_logs (
    id          bigint      NOT NULL AUTO_INCREMENT,
    ticket_id   bigint      NOT NULL,
    from_status varchar(10) NOT NULL,
    to_status   varchar(10) NOT NULL,
    actor_id    varchar(36) NOT NULL,
    acted_at    datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (id),
    KEY idx_bo_cs_log_ticket (ticket_id, acted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='티켓 상태 전이 — INSERT ONLY';

-- 3) 답 — 반자동 ②겹: approved_by·approved_at NOT NULL — 승인 없인 보낼 몸통이 없다(설계 §4-5)
CREATE TABLE IF NOT EXISTS bo_cs_replies (
    reply_id        varchar(36)   NOT NULL COMMENT '본사 발급 — ERP 가 이 값 그대로 멱등 수신',
    ticket_id       bigint        NOT NULL,
    tenant_id       varchar(36)   NOT NULL,
    client_ticket_uid varchar(36) NOT NULL COMMENT 'ERP cs_request_id — Pull 응답이 이 값으로 연결',
    body            varchar(4000) NOT NULL,
    replied_by_kind varchar(10)   NOT NULL COMMENT 'hq/reseller — 사람 이름은 안 내보낸다(설계 §5-3)',
    approved_by     varchar(36)   NOT NULL COMMENT '🔴 NOT NULL — 사람 확정 없인 행이 안 생긴다',
    approved_at     datetime(6)   NOT NULL,
    created_at      datetime(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    delivered_at    datetime(6)   NULL COMMENT 'ERP Pull 이 가져간 시각(전달 관찰용 · 멱등과 무관)',
    PRIMARY KEY (reply_id),
    KEY idx_bo_cs_reply_tenant (tenant_id, created_at),
    KEY idx_bo_cs_reply_ticket (ticket_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='CS 답 — 승인 없인 행 불생성(반자동 ②겹) · reply_id = ERP 멱등키';

-- 4) 수신측 거부로그 — INSERT ONLY · B-6 자물쇠의 증거(값은 저장 안 함 — 사유코드만)
CREATE TABLE IF NOT EXISTS bo_cs_reject_logs (
    id          bigint      NOT NULL AUTO_INCREMENT,
    tenant_code varchar(20) NULL COMMENT '주장된 테넌트넘버(대조 실패여도 코드 자체는 기록 — 공격 관찰)',
    rule_code   varchar(40) NOT NULL COMMENT 'auth_mismatch/forbidden_field/rate_limited/locked/body_too_long/bad_payload …',
    occurred_at datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (id),
    KEY idx_bo_cs_reject_time (occurred_at),
    KEY idx_bo_cs_reject_code (tenant_code, occurred_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='CS 수신 거부 기록 — INSERT ONLY · 연속 실패 잠금과 남용 게이트가 이 표를 읽는다';
