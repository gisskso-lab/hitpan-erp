-- =============================================================
-- DB-141: cs_replies 신설 — 본사·대리점이 보낸 답 (작14 묶음 B-2)
-- =============================================================
-- 근거: 작업지시서 20261008작14 §3 B-2·B-9 · 설계 20261007_개발매니저피드백_CS_ERP연동_4인.md §3-1
--       결-6(사장님 10/8): 답은 전용 화면(사내 메신저와 분리) + N-8 「안 읽은 답 N」 전역 표시
--
-- 무엇에 쓰나
--   기존 5분 Pull 에 얹어 받아 적는 답(B-9). reply_id 는 본사 발급 ID 그대로 = 멱등(같은 답 2번 = 1건).
--   replied_by_kind = 본사/대리점 — 🔴 사람 이름은 안 받는다(설계 §5-3).
--   read_at 으로 N-8 「안 읽은 답 N」 을 센다 — 이게 없으면 쪽지는 안 만든 것보다 나쁘다(R-7).
--   ERP 가 cs_request_id→created_by 연결로 해당 자식계정에게 보여준다(교정③).
--
-- 대상 DB: hitpan_erp · 출하 DDL 같은 커밋(#36) · #17 InnoDB · 멱등 IF NOT EXISTS
-- =============================================================

CREATE TABLE IF NOT EXISTS cs_replies (
    reply_id        varchar(36)   NOT NULL COMMENT '본사 발급 ID 그대로 — 멱등',
    tenant_id       varchar(36)   NOT NULL,
    cs_request_id   varchar(36)   NOT NULL,
    body            varchar(4000) NOT NULL COMMENT '본사가 쓴 답(고객에게 보일 글)',
    replied_by_kind varchar(10)   NOT NULL COMMENT '본사/대리점 — 사람 이름은 안 받는다',
    replied_at      datetime(6)   NOT NULL,
    received_at     datetime(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    read_at         datetime(6)   NULL COMMENT 'N-8 「안 읽은 답 N」 의 재료',
    PRIMARY KEY (reply_id),
    KEY idx_cs_reply_req (tenant_id, cs_request_id, replied_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='CS 답 — Pull 수신 · reply_id 멱등 · 전용 화면 결-6(작14 B-2)';
