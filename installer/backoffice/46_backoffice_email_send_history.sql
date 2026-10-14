-- ============================================================================
-- 히트판 백오피스(hitpan_backoffice) — 20261015작B1 갈래 B · B-1 (G2)
--   백오피스 메일 발송기록 표 1개 (bo_email_send_history)
-- ============================================================================
-- 작성: 개발팀 (작업지시서 20261015작B1 §4 갈래 B · PM 결재 2026-10-15 §13 ⓑ)
--
-- ★ 왜 이 표가 필요한가 (선행검증 사실 — 재측정 불필요):
--   백오피스에는 메일 발송 기록을 담는 표·칸이 **0개**였다(ERP 쪽은 `email_send_history` 가 있다).
--   발송기(`EmailSender.cs`)는 자격증명 미저장이면 **정직하게 `false`** 를 돌려주는데,
--   호출자 4곳이 `_ = _email.SendAsync(…)` 로 그 `false` 를 **버렸다**.
--   ⇒ 「메일이 안 갔다」를 아무도 모르고, 「진짜 1통 갔다」를 조회로 증명할 자리도 없었다.
--   이 표가 그 자리다. 성공·실패·예외 **세 경로 모두** 1행을 남긴다.
--
-- ★ 🔴 MySQL 8 · MariaDB 양쪽에서 돌아야 한다 (20261011작1·작2 교훈 — 판이 둘이다).
--   운영 백오피스는 MySQL 8 이고 CI `db-gate` 는 MariaDB 11.4 다.
--   · `CREATE TABLE IF NOT EXISTS` 는 양쪽이 아는 문법이라 그대로 쓴다.
--   · `ALTER TABLE` 은 **한 절도 쓰지 않는다**(신규 표 하나뿐) — 1060 덫 자체가 없다.
--   · `datetime(6)` · `current_timestamp(6)` 는 양쪽 공통. 단 기본값에 맡기지 않고
--     **쓰기 경로가 UTC 를 명시**한다(45 번 [4] V-6 전례: 세션 시간대 KST 와 9시간 어긋남).
--
-- ★ FK 를 걸지 않는다 (45 번과 같은 이유):
--   `31` 번이 FK 1452 로 적재기를 멈춰 백오피스 기동을 거부한 전례가 있다.
--   그리고 이 표는 **고객사가 아닌 상대(협력업체 신청자·가입 신청자)** 에게 보낸 것도 담는다 ⇒
--   tenant 로 묶을 수 없는 행이 정상이다. 인덱스만 둔다.
--
-- ★ #3 INSERT ONLY — 발송 사실은 이력이다. UPDATE·DELETE 하지 않는다.
-- ★ #17 — 신규 표는 `ENGINE=InnoDB` 명시.
-- ★ #18·#22 — 고객사 **업무** 데이터는 한 칸도 없다. 본사가 보낸 메일의 머리와 그 결과뿐.
--   🔴 본문(htmlBody)은 담지 않는다. 담으면 임시비번·라이선스 키가 표에 눕는다.
-- ★ #29 — SMTP 자격증명은 한 칸도 없다(이 표에 값이 들어올 길이 없다).
--
-- ★ 감시 게이트: `BackofficeMailHistoryGateTests` (G-B1-B-1~5) — CI `db-gate` 잡.
--   출하 편입은 기존 `BackofficeSchemaShipGateTests` G-BO-SHIP-2 가 이미 문다
--   (csproj 의 `installer\backoffice\*.sql` 와일드카드 — 새 번호는 자동으로 따라온다).
-- ============================================================================

-- ── 메일 발송기록 ───────────────────────────────────────────────────────────
--   칸 = 받는이 · 제목 · 결과 · 사유 · 발송시각 · 호출출처 (작업지시서 §4 B-1 그대로)
CREATE TABLE IF NOT EXISTS bo_email_send_history (
    send_id       varchar(36)   NOT NULL COMMENT '발송기가 발급(GUID) — 로그와 표를 맞추는 열쇠',
    to_email      varchar(200)  NOT NULL COMMENT '받는이 — 가입 신청자·협력업체 신청자·본사 담당자',
    subject       varchar(200)  NULL     COMMENT '제목(머리만) — 본문은 담지 않는다(#22)',
    -- 🔴 결과는 두 값뿐이다. 「보내는 중」 같은 중간 상태를 두지 않는다 —
    --   중간 상태가 있으면 화면이 또 「보냄」이라고 거짓말할 자리가 생긴다(45 번 ② 교훈).
    result        varchar(20)   NOT NULL COMMENT '결과 — sent / failed. 이 두 값뿐',
    reason_code   varchar(40)   NULL     COMMENT '사유 코드 — ok / not_configured / smtp_error',
    reason        varchar(500)  NULL     COMMENT '사유 사람말 — 실패 원인 요약(예외 메시지 앞부분). 자격증명 값은 담지 않는다',
    sent_at       datetime(6)   NOT NULL COMMENT '발송 시도 시각 — 쓰기 경로가 UTC 를 명시한다',
    caller        varchar(120)  NULL     COMMENT '호출출처 — 예: LandingSignupController.Submit. 못 가리면 unknown',
    has_attachment tinyint(1)   NOT NULL DEFAULT 0 COMMENT '첨부 1건 흐름(협력업체 계약서 PDF) 구분',
    PRIMARY KEY (send_id),
    -- 「최근 발송 N건」 · 「실패만」 두 조회가 이 표의 전부다. 인덱스도 그 둘만 둔다.
    KEY idx_bo_email_hist_sent (sent_at),
    KEY idx_bo_email_hist_result (result, sent_at),
    KEY idx_bo_email_hist_to (to_email, sent_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='백오피스 메일 발송기록 — 성공·실패 둘 다 1행 (20261015작B1 B-1)';
