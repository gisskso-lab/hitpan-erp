-- ============================================================================
-- 히트판 백오피스(hitpan_backoffice) — 20261011작2 절 C
--   ① 본사 선발신 쪽지 큐(bo_cs_outbound)
--   ② CS 보관기한 설정(bo_cs_retention_policy)
--   ③ 쪽지 폐기 표식 3칸(bo_cs_tickets)
-- ============================================================================
-- 작성: 개발팀 (작업지시서 20261011작2 §3·§4 · PM 결재 2026-10-11)
-- 사장님 오더 2026-10-11:
--   "프로모션, 이벤트, 혹은 구독요금 미결재 이슈로 인해 고객사에게 백오피스가
--    먼저 쪽지를 보낼 기능도 필수"
--   "고객사에게 주고받은 쪽지는 관리대상임. 받고 저장해야 할 데이터 필수.
--    그리고 관리후 폐기기한설정 혹은 임의 삭제도 되야 함"
--
-- ★ 🔴 MySQL 8 · MariaDB 양쪽에서 돌아야 한다 (20261011작1 교훈)
--   운영 백오피스는 MySQL 8.0.45 이고 CI 는 MariaDB 11.4 다 — 판이 둘이다.
--   · `CREATE TABLE IF NOT EXISTS` 는 양쪽이 아는 문법이라 그대로 쓴다.
--   · `ALTER TABLE … ADD COLUMN` 은 **한 문장에 한 절**로 쪼갰다.
--     이유: 한 문장에 여러 절을 넣으면 그 중 한 칸만 이미 있어도 MySQL 이
--     **문장 전체를 롤백**하고 1060 을 낸다. 적용기가 그걸 「이미 있다」로 넘기면
--     나머지 칸이 안 생긴 채 적용됨으로 기록돼 **영영 안 생긴다**(작1 [4] 검증 V-1).
--     `44` 번이 올바른 모양이고 `32`(4절)·`42`(6절)가 그 덫이다.
--
-- ★ FK 를 걸지 않는다 (설계 §4):
--   `31` 번이 FK 1452 로 적용기를 멈춰 백오피스 기동을 거부한 전례가 있다.
--   테넌트 존재 확인은 쓰기 경로(컨트롤러)가 하고, 여기서는 인덱스만 둔다.
--
-- ★ #17 — 신규 표는 ENGINE=InnoDB 명시.
-- ★ #18·#22 — 고객사 업무 데이터는 한 칸도 없다. 본사가 보낸 글과 그 전달 상태뿐.
-- ============================================================================

-- ── ① 본사 선발신 쪽지 큐 ────────────────────────────────────────────────
--   왜 「큐」인가: 지금 ERP 수신부는 **원본 티켓이 없는 답을 조용히 버린다**
--   (`CsOutboxSenderWorker.cs:316` — `FROM cs_requests r WHERE r.cs_request_id = @Uid`
--    가 0행이면 INSERT 0건). 그래서 보내는 척하지 않고 여기 쌓아 두고,
--   화면에 「전달 대기 — ERP 수신 준비 중」으로 정직하게 보인다.
--   ERP CS 파이프라인(다음 작업)이 열리면 쌓인 것이 그대로 나간다(#20 흐름 불단절).
CREATE TABLE IF NOT EXISTS bo_cs_outbound (
    outbound_id   varchar(36)   NOT NULL COMMENT '본사가 발급 — ERP 전달 시 멱등 열쇠가 된다',
    tenant_id     varchar(36)   NOT NULL,
    purpose       varchar(20)   NOT NULL DEFAULT 'etc'
                  COMMENT '용도 — promo/event/billing/etc (사장님 오더 3종 + 기타)',
    title         varchar(100)  NULL     COMMENT '한 줄 제목(선택)',
    -- 🔴 NULL 허용 — 폐기하면 본문을 비운다([4] V-4). `NOT NULL` 이면 빈 문자열로 뭉개야 하고
    --   그러면 「안 쓴 쪽지」와 「폐기된 쪽지」가 구별되지 않는다. 쓰기 경로가 비어 있는 글을 막는다.
    body          varchar(2000) NULL     COMMENT '금지필드 검사 통과분만 저장(BoForbiddenFieldScanner) · 폐기 시 NULL',
    created_by    varchar(36)   NOT NULL COMMENT '쓴 사람(본사 어드민 id)',
    created_at    datetime(6)   NOT NULL DEFAULT current_timestamp(6),
    delivered_at  datetime(6)   NULL DEFAULT NULL COMMENT 'NULL = 전달 대기 · 값 = ERP 가 가져간 시각',
    canceled_at   datetime(6)   NULL DEFAULT NULL COMMENT '보내기 전 취소',
    canceled_by   varchar(36)   NULL DEFAULT NULL,
    -- 🔴 [4] V-4 봉합 — 선발신도 「보관기한·임의 폐기」 축 안에 둔다.
    --   사장님 오더는 「고객사에게 **주고받은** 쪽지는 관리대상 … 폐기기한설정 혹은 임의 삭제」다.
    --   취소(`canceled_at`)는 **나가기 전에만** 듣는다 ⇒ 이미 전달·취소된 쪽지를 지울 길이 0 이었다.
    --   폐기 = 본문만 비우고 「누가·언제·왜」는 남긴다(티켓 쪽과 같은 규칙).
    purged_at     datetime(6)   NULL DEFAULT NULL COMMENT '폐기 시각 — 값이 있으면 본문은 비워져 있다',
    purged_by     varchar(36)   NULL DEFAULT NULL COMMENT '폐기한 사람',
    purge_reason  varchar(200)  NULL DEFAULT NULL COMMENT '폐기 사유 — 입력 필수(화면·서버 양쪽이 강제)',
    PRIMARY KEY (outbound_id),
    KEY idx_bo_cs_outbound_pending (delivered_at, created_at),
    KEY idx_bo_cs_outbound_tenant (tenant_id, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='본사 → 고객사 선발신 쪽지 큐 (20261011작2 C-1)';

-- ── ② CS 보관기한 설정 ──────────────────────────────────────────────────
--   🔴 자동 삭제는 하지 않는다. 기한이 지난 것을 「폐기 후보」로 **보여주고**
--   사람이 확정한다. 기한이 지나자마자 말없이 지우면 고객 분쟁에서 우리 증거가 사라진다.
--   (히트판 반자동 원칙 — 제안값 → 사람 확인 → 확정)
CREATE TABLE IF NOT EXISTS bo_cs_retention_policy (
    policy_id   tinyint      NOT NULL DEFAULT 1 COMMENT '전역 1행 고정',
    retain_days int          NULL DEFAULT NULL  COMMENT 'NULL = 무기한(기본값) · n = n일 지난 쪽지는 폐기 후보',
    updated_by  varchar(36)  NULL DEFAULT NULL,
    updated_at  datetime(6)  NOT NULL DEFAULT current_timestamp(6),
    PRIMARY KEY (policy_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='CS 쪽지 보관기한 — 자동삭제 없음(20261011작2 C-6)';

-- 초기 1행 = 무기한. 재실행 안전(INSERT IGNORE) · 이미 사람이 설정한 값은 덮지 않는다.
-- 🔴 `updated_at` 을 **명시**한다([4] V-6) — DEFAULT 에 맡기면 세션 시간대(KST)로 들어가
--   `SetRetention` 이 쓰는 UTC 와 같은 칸에서 9시간 어긋난다(로컬 실측 540분).
INSERT IGNORE INTO bo_cs_retention_policy (policy_id, retain_days, updated_at)
VALUES (1, NULL, UTC_TIMESTAMP(6));

-- ── ③ 쪽지 폐기 표식 — 한 문장에 한 절 (위 ★ 참조) ──────────────────────
--   왜 행을 지우지 않나: `bo_cs_ticket_logs`·`bo_cs_replies` 가 그 티켓을 가리킨다.
--   통째 삭제는 이력을 함께 지우거나 FK 로 실패한다. 그리고 「언제 누가 무엇을 지웠나」가
--   사라지면 분쟁에서 우리 쪽 증거가 없다. ⇒ 몸통(본문)은 비우고 사실은 남긴다.
ALTER TABLE bo_cs_tickets
    ADD COLUMN IF NOT EXISTS purged_at datetime(6) NULL DEFAULT NULL
        COMMENT '폐기 시각 — 값이 있으면 본문은 비워져 있다 (20261011작2 C-10)';

ALTER TABLE bo_cs_tickets
    ADD COLUMN IF NOT EXISTS purged_by varchar(36) NULL DEFAULT NULL
        COMMENT '폐기한 사람';

ALTER TABLE bo_cs_tickets
    ADD COLUMN IF NOT EXISTS purge_reason varchar(200) NULL DEFAULT NULL
        COMMENT '폐기 사유 — 입력 필수(화면이 강제)';
