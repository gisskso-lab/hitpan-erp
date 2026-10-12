-- =============================================================
-- DB-145: cs_requests.hq_status·hq_status_at 신설 — 본사 진행 상태를 **별도 칸**으로 (작4 B-2)
-- =============================================================
-- 근거: 선행검증서 20261012_1V 작4 §2-4 · 사장님 결재 2026-10-12(「본사 상태를 별도 칸으로 추가」)
--
-- 왜 칸이 필요한가
--   10/12 사장님 실측 반려 ④ — 백오피스 CS창구에서 상태를 「처리중」·「완료」로 바꿔도
--   ERP 는 영원히 몰랐다. 버그가 아니라 **하행 계약에 상태 칸이 아예 없었다**:
--   Pull 응답은 replies·outbound·purgedOutbound 뿐이고(CsInboundController),
--   ERP 쪽 상태 수신 코드도 0건이었다(CsOutboxSenderWorker).
--
-- 왜 기존 status 를 안 쓰나 — 🔴 사장님 결재
--   cs_requests.status 는 **쪽지 자신의 진행**이다(접수·전송중·전송완료·답변도착·종결).
--   본사 진행(접수·처리중·보류·완료)은 **다른 축**이다. 한 칸에 섞으면 「답변도착」이
--   「완료」에 덮여 **어느 쪽 사실인지 사라진다** — 그 순간 사람이 화면을 못 믿는다.
--   그래서 덮지 않고 **더한다**(#1).
--
-- 기존 행에 미치는 영향 — 0
--   둘 다 NULL 허용이고 기본값이 없다. 이미 있는 쪽지는 **한 줄도 안 바뀐다**.
--   NULL = 「본사가 아직 손대지 않았다」로 화면이 읽는다(= 종전 모양 그대로).
--
-- 🔴 추가형 마이그 — 재실행 안전(IF NOT EXISTS). ERP 는 MariaDB 다(백오피스 MySQL 8 과 판이 다르다).
--    이 파일은 ERP 축이라 MariaDB 문법을 그대로 쓴다.
-- 🔴 varchar(10) — 백오피스 bo_cs_tickets.status 와 **같은 폭**이다(40_backoffice_cs.sql:27).
--    좁으면 「고객답대기」 같은 값이 잘려 들어온다.
-- =============================================================

ALTER TABLE cs_requests
    ADD COLUMN IF NOT EXISTS hq_status varchar(10) NULL DEFAULT NULL
        COMMENT '본사 진행 상태(접수/처리중/보류/완료) — NULL = 본사 미착수 · status 와 다른 축(작4 B-2)';

ALTER TABLE cs_requests
    ADD COLUMN IF NOT EXISTS hq_status_at datetime(6) NULL DEFAULT NULL
        COMMENT '본사가 그 상태로 바꾼 시각(UTC) — 뒤로 가는 반영을 막는 근거(작4 B-2)';
