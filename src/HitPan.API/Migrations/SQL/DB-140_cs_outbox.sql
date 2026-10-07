-- =============================================================
-- DB-140: cs_outbox 신설 — 본사로 보낼 쪽지 큐 (작14 묶음 B-2)
-- =============================================================
-- 근거: 작업지시서 20261008작14 §3 B-2·B-3 · 설계 20261007_개발매니저피드백_CS_ERP연동_4인.md §3-1
--
-- 무엇에 쓰나
--   🔴 「쪽지가 사라지지 않는」 장치의 전부다. 기존 송신 코드(실패 시 버림)를 쓰지 않고
--   DB 큐로 간다 — 한 건이 사라지면 고객이 쓴 글이 사라진다(설계 §5-ⓐ).
--   워커 폴링은 **sent_at IS NULL AND next_attempt_at <= NOW(6)** 로만(설계 §3-3 — status 글자로 찾으면 인덱스를 못 탄다).
--   4xx = rejected 종결(재시도 없음 · 고객 화면 사유) / 5xx·타임아웃 = 지수백오프 재시도 · 상한 후 failed 로 멈춤.
--   🔴 어떤 경우에도 행 삭제 금지 — failed 는 사람이 본다(#26 정신).
--   payload_json 은 화이트리스트 필드만(자식계정 식별자 제외 — 교정③).
--   sent_at 갱신은 메타 갱신 — #3 과 충돌 없음(OutboxPollerWorker:12 선례).
--
-- 🚫 건드리지 않는 것: 기존 OutboxPollerWorker·그 표(#37 — 별건·처분 제안도 금지)
-- 대상 DB: hitpan_erp · 출하 DDL 같은 커밋(#36) · #17 InnoDB · 멱등 IF NOT EXISTS
-- =============================================================

CREATE TABLE IF NOT EXISTS cs_outbox (
    outbox_id        bigint        NOT NULL AUTO_INCREMENT,
    tenant_id        varchar(36)   NOT NULL,
    cs_request_id    varchar(36)   NOT NULL COMMENT '무엇을 보내나(cs_requests PK)',
    payload_json     varchar(4000) NOT NULL COMMENT '화이트리스트 필드만 담긴 완성 몸통(자식계정 제외 · 교정③)',
    attempt_count    int           NOT NULL DEFAULT 0,
    next_attempt_at  datetime(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6) COMMENT '지수백오프 시각',
    sent_at          datetime(6)   NULL COMMENT 'NULL = 아직 안 갔다 — 워커는 이 칸만 보고 집는다',
    last_status_code int           NULL COMMENT '4xx/5xx 구분(설계 §2-2)',
    last_error       varchar(500)  NULL,
    terminal_reason  varchar(30)   NULL COMMENT 'rejected/failed — 비면 아직 살아 있다 · 행 삭제 금지',
    created_at       datetime(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (outbox_id),
    UNIQUE KEY uq_cs_outbox_req (cs_request_id),
    KEY idx_cs_outbox_pending (sent_at, next_attempt_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='CS 쪽지 송신 큐 — 유실 0 · 삭제 금지 · 멱등키 = cs_request_id(작14 B-2)';
