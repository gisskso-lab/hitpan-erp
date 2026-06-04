-- 백오피스 → ERP webhook 발송 큐 (사장님 결재 2026-06-04, W10)
--
-- 헌법 정합:
--   #18·#22 — payload은 메타(구독 등급·기기 슬롯·만료일)만. 업무 데이터 0건
--   #20 — 발송 실패해도 INSERT ONLY 보존, 재시도로 끊김 0
--   #29 — 환경변수 신규 0건 (W2 HITPAN_BOOTSTRAP_TOKEN_KEY 재사용)
--   #35 — 백오피스가 ERP에 능동 호출하는 유일한 통로 (역방향 의존 0)
--
-- 박제 위치: hitpan_backoffice DB

USE hitpan_backoffice;

CREATE TABLE IF NOT EXISTS webhook_outbox (
    outbox_id       BIGINT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
    tenant_id       CHAR(36)        NOT NULL,
    event_type      VARCHAR(40)     NOT NULL
        COMMENT 'subscription_changed / device_slot_changed',
    target_url      VARCHAR(255)    NOT NULL
        COMMENT '고객사 ERP /api/internal/webhook/* (테넌트 도메인 기반)',
    payload_json    TEXT            NOT NULL
        COMMENT 'JSON 직렬화. 메타만, 업무 데이터 0',
    signature       VARCHAR(128)    NOT NULL
        COMMENT 'HMAC-SHA256 Base64URL (W2 키 재사용)',
    nonce           VARCHAR(36)     NOT NULL
        COMMENT 'GUID. ERP 측 중복 차단 키',

    status          VARCHAR(20)     NOT NULL DEFAULT 'pending'
        COMMENT 'pending / sent / failed / dead',
    retry_count     INT             NOT NULL DEFAULT 0,
    last_error      VARCHAR(500)    NULL,
    next_retry_at   DATETIME(6)     NULL,

    created_at      DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    sent_at         DATETIME(6)     NULL,

    KEY idx_outbox_status_next (status, next_retry_at),
    KEY idx_outbox_tenant (tenant_id, created_at),
    UNIQUE KEY uk_outbox_nonce (nonce)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
