-- =============================================================
-- DB-143: cs_rating_outbox 신설 — 「받은답」 만족도 평가 송신 큐 (작15 §A · E-1)
-- =============================================================
-- 근거: 작업지시서 20261008작15 §2·§4 §A · 설계 docs/설계/erp/20261008_E1_CS만족도_보내기_설계문서.md §3
--       선행검증서 docs/검증/선행/20261008_1V_ERP_CS_E1E4_선행검증서.md 전제 2·3
--
-- 왜 새 표인가 (PM 결재 D-1 · 안 A 채택)
--   기존 cs_outbox 에는 실을 수 없다 — ① 종류 열이 없고(DB-140:20-33)
--   ② 보낼 주소가 코드 1줄 고정(CsOutboxSenderWorker.cs:129)
--   ③ UNIQUE uq_cs_outbox_req(cs_request_id)(DB-140:32) 때문에 같은 쪽지에 2번째 행이
--      원리적으로 안 들어가고, cs_outbox 에 종류 열을 더하면 「내 문의」 목록 조인
--      (CsRequestController.cs:177)이 쪽지당 2행을 돌려 목록이 두 줄로 늘어난다.
--
-- 무엇에 쓰나
--   평가 기록과 송신 큐를 한 행으로 둔다. 화면이 「보냈다」고 하기 전에 로컬에 먼저 적어
--   터널·본사가 내려가 있어도 평가가 사라지지 않게 한다(유실 0).
--   워커 폴링은 sent_at IS NULL AND next_attempt_at <= UTC_TIMESTAMP(6) 로만 집는다
--   (글자 상태로 찾으면 인덱스를 못 탄다 — DB-140:9 와 같은 규율).
--   🔴 어떤 경우에도 행 삭제 금지 — 상한(50회) 뒤 failed 로 멈추고 사람이 본다(#26 정신).
--      평가는 고객이 쓴 글이다. 되돌릴 때도 DROP 하지 않는다.
--
-- payload_json 칸을 두지 않는 이유 (PM 결재 D-2)
--   rating·comment 열이 유일한 진실원이고, 본사로 가는 몸통(3키)은 보낼 때 만든다.
--   같은 값이 두 곳에 사는 사고를 만들지 않고, 송신 직전 재검(문②)이 JSON 파싱 없이 열을 직접 본다.
--   대가: cs_outbox(payload_json 보관)와 한 칸 모양이 다르다.
--
-- rated_by(쓴 사람) 칸은 두지 않는다 — 오더에 없다(#33). 필요해지면 그때 추가형으로.
--
-- 🚫 건드리지 않는 것: cs_requests·cs_outbox·cs_replies·cs_forbidden_rejects DDL 과 그 흐름
-- 대상 DB: hitpan_erp · 출하 DDL 같은 커밋(#36) · #17 InnoDB 명시 · 멱등 IF NOT EXISTS
-- =============================================================

CREATE TABLE IF NOT EXISTS cs_rating_outbox (
    outbox_id        bigint        NOT NULL AUTO_INCREMENT,
    tenant_id        varchar(36)   NOT NULL COMMENT 'JWT 유래만(#2) — 파라미터 수신 0',
    cs_request_id    varchar(36)   NOT NULL COMMENT '어느 쪽지의 평가인가(cs_requests PK) · 멱등의 자리',
    `rating`         tinyint       NOT NULL COMMENT '1 아쉬워요 / 2 보통 / 3 좋아요 — 본사 계약(정수 1~3)과 같은 축',
    `comment`        varchar(500)  NULL     COMMENT '한 줄 평 · 상한은 본사 저장 상한과 같은 500 ⇒ 잘림 0(PM 결재 D-4)',
    attempt_count    int           NOT NULL DEFAULT 0,
    next_attempt_at  datetime(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6) COMMENT '지수백오프 시각 · INSERT 는 UTC 명시로 넣는다',
    sent_at          datetime(6)   NULL COMMENT 'NULL = 아직 안 갔다 — 워커는 이 칸만 보고 집는다',
    last_status_code int           NULL COMMENT '4xx/5xx 구분',
    last_error       varchar(500)  NULL COMMENT '2xx 인데 본사에 티켓이 없었으면 duplicated 로 남는다 — 조용히 사라진 평가의 유일한 흔적',
    terminal_reason  varchar(30)   NULL COMMENT 'rejected/failed — 비면 아직 살아 있다 · 행 삭제 금지',
    created_at       datetime(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (outbox_id),
    UNIQUE KEY uq_cs_rating_req (cs_request_id),
    KEY idx_cs_rating_pending (sent_at, next_attempt_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='CS 만족도 평가 송신 큐 — 유실 0 · 삭제 금지 · 멱등키 = cs_request_id(작15 E-1)';
