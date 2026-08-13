-- ═══════════════════════════════════════════════════════════════
-- DB-98 : 휴직 (그룹웨어 단계6)
-- 작성: 2026-08-13 · 사장님 결재 = 그룹웨어 단계4~9 일괄 전결
-- ═══════════════════════════════════════════════════════════════
--
-- 🔴 왜 새 표가 필요한가 — 기존 표로는 구조적으로 못 담는다
--
--   실측 1. `leave_requests` (연차·병가 등 단기 휴가)
--     `leave_days` decimal(3,1)  → 최대 99.9일
--     ⇒ 육아휴직 1년 6개월(약 548일)이 **자릿수에서 안 들어간다.**
--        늘려서 쓰자는 생각은 더 위험하다 — 아래 실측 2를 보라.
--
--   실측 2. `employees` 의 상태 칸
--     `is_active` tinyint(1) · `is_resigned` · `resign_date` · `resign_reason`
--     ⇒ **재직 아니면 퇴사, 둘뿐이다. 휴직이라는 상태가 없다.**
--        휴직자를 is_active=0 으로 두면 → 퇴사자와 구분이 안 된다(복직시킬 근거가 사라진다).
--        휴직자를 is_active=1 로 두면 → 급여·연차 계산에 그대로 들어간다.
--        **어느 쪽으로 둬도 사고다.** 그래서 '기간이 있는 부재' 를 담는 표를 따로 둔다.
--
--   ⇒ 휴가(하루~며칠, 연차에서 깎임)와 휴직(달~년, 근로관계는 유지되나 근로 제공이 멈춤)은
--      성격이 다르다. 한 표에 섞으면 연차 잔여 계산과 급여 계산이 동시에 틀어진다.
--
-- 🔴 담는 것 / 안 담는 것 (설계도 §0 · 사장님: "큰 틀을 일단 만들어")
--
--   담는다   — 누가 · 어떤 휴직을 · 언제부터 언제까지 · 지금 어느 단계인지 · 실제로 언제 복직했는지
--   안 담는다 — 육아휴직 급여 계산(고용보험이 준다·회사 급여가 아니다) · 사회보험 납부유예 신청
--             ⇒ 회사 프로그램이 대신 신청해 줄 수 없는 영역이다. 큰 틀에서 제외.
--
-- 🔴 법정값은 여기 안 쓴다 — 전부 labor_policy_settings(DB-96) 로 간다
--
--   조사가 이미 증명했다: **육아휴직 1년 → 1년 6개월 (2025-02 시행)**.
--   우리가 "정확히 조사한" 것이 바로 얼마 전에 바뀐 것이다.
--   ⇒ 최대 기간·분할 횟수를 이 표나 코드에 쓰면 법이 바뀔 때 전 고객이 재배포를 기다린다.
--      (사장님: "법이 언제, 어떻게 바뀔지는 몰라. 계속 모니터링 해야 해")
--
-- 🔴 반자동 (사장님: "히트판은 100%자동화는 없어. 무조건 반자동이야")
--
--   기준 초과를 **막지 않는다. 경고만 한다.**
--   근거: 법정 기간은 '최소 보장' 이다. 회사가 더 주는 것은 위법이 아니다.
--   기계가 막아버리면 더 주려는 회사가 히트판 때문에 못 준다 — 그게 진짜 사고다.
--   대신 **초과분에는 사유를 남기게 한다.**
--
-- 멱등: CREATE TABLE IF NOT EXISTS + INSERT ... NOT EXISTS.
-- ═══════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS `employee_leave_of_absence` (
  `absence_id`     varchar(36)  NOT NULL COMMENT 'PK',
  `tenant_id`      varchar(36)  NOT NULL COMMENT '테넌트(헌법 #2 — JWT 클레임에서만 온다)',
  `employee_id`    varchar(36)  NOT NULL COMMENT '누가',

  -- 종류. 코드값은 고정, 화면 이름은 코드가 가진다.
  --   childcare=육아휴직 · maternity=출산전후휴가 · family_care=가족돌봄휴직
  --   sick=질병휴직 · military=병역휴직 · study=학업휴직 · personal=개인사정 · other=기타
  -- ⚠️ 종류를 늘릴 때는 마이그레이션으로 추가한다. 회사마다 다른 이름은 leave_label 로 받는다.
  `absence_type`   varchar(30)  NOT NULL COMMENT '휴직 종류 코드',
  `absence_label`  varchar(60)  DEFAULT NULL COMMENT '회사가 부르는 이름(비면 코드의 기본 이름을 쓴다)',

  -- 기간
  `start_date`     date         NOT NULL COMMENT '휴직 시작일',
  `end_date`       date         NOT NULL COMMENT '휴직 종료 예정일',
  `actual_return_date` date     DEFAULT NULL COMMENT '🔴 실제 복직일. 예정과 다를 수 있어 따로 남긴다',

  -- 🔴 상태 — 이 표의 존재 이유다. 재직/퇴사 둘뿐이던 것을 여기서 세분한다.
  --   draft=작성중 · pending=결재중 · approved=승인(아직 시작 전) · active=휴직중
  --   returned=복직 · rejected=반려 · cancelled=취소
  `status`         varchar(20)  NOT NULL DEFAULT 'draft' COMMENT '진행 단계',

  `reason`         varchar(500) DEFAULT NULL COMMENT '휴직 사유(직원이 적는다)',

  -- 🔴 반자동의 흔적 — 기준을 넘겼는데도 승인한 경우 그 이유가 남아야 한다.
  `exceeds_standard` tinyint(1) NOT NULL DEFAULT 0 COMMENT '1=회사·법정 기준을 넘김',
  `exceed_reason`  varchar(300) DEFAULT NULL COMMENT '🔴 넘겼는데 왜 승인했나',
  `calc_basis`     varchar(500) DEFAULT NULL COMMENT '판정 근거를 글로 남긴다(기준이 바뀐 뒤에도 설명되게)',

  -- 급여 처리 방식. 계산은 단계8(급여)에서 하고, 여기서는 '어떻게 하기로 했나' 만 정한다.
  --   unpaid=무급 · paid=유급 · partial=일부지급
  -- ⚠️ 육아휴직 급여는 고용보험이 지급한다. 회사 급여가 아니므로 여기는 unpaid 가 기본이다.
  `pay_type`       varchar(20)  NOT NULL DEFAULT 'unpaid' COMMENT '급여 처리 방식',

  -- 결재 연동 (단계2·3과 같은 방식)
  `approval_id`    varchar(36)  DEFAULT NULL COMMENT 'approval_documents 로 이어지는 자리',
  `approved_by`    varchar(36)  DEFAULT NULL COMMENT '누가 승인했나',
  `approved_at`    datetime(6)  DEFAULT NULL,
  `reject_reason`  varchar(500) DEFAULT NULL COMMENT '반려 사유. 🔴 폭을 500 으로 둔다 — 단계3 P0-2 재발 방지',

  `created_by`     varchar(36)  DEFAULT NULL,
  `created_at`     datetime(6)  NOT NULL DEFAULT current_timestamp(6),
  `updated_at`     datetime(6)  NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),

  PRIMARY KEY (`absence_id`),
  KEY `idx_absence_tenant_emp` (`tenant_id`, `employee_id`, `start_date`),
  KEY `idx_absence_status` (`tenant_id`, `status`),
  -- "오늘 휴직 중인 사람" 을 매번 물어보게 된다(급여·연차·조직도).
  KEY `idx_absence_period` (`tenant_id`, `status`, `start_date`, `end_date`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='휴직 — 기간이 있는 부재. 휴가(leave_requests)와 다르다. DB-98';

-- ═══════════════════════════════════════════════════════════════
-- 휴직 기준값 — DB-96 표에 얹는다 (여기에 숫자를 쓰지 않는다)
-- ═══════════════════════════════════════════════════════════════
--
-- ⚠️ 아래 숫자는 **2026-08 시점의 법정 최소**다. 회사가 더 줄 수 있고, 법도 바뀐다.
--    바뀌면 관리자가 **새 시행일로 행을 추가**한다(기존 행을 고치지 않는다 — 과거분이 틀어진다).
--
-- 🔴 effective_from 을 2025-02-23 으로 두는 이유:
--    육아휴직 1년 → 1년 6개월 확대 시행일이다. 그 전에 시작한 휴직은 옛 기준으로 판정해야 한다.
--    (이게 DB-96 에 시행일 칸을 둔 이유 그 자체다)

INSERT INTO labor_policy_settings
  (policy_id, tenant_id, policy_key, policy_value, value_unit, effective_from,
   label, description, is_statutory)
SELECT UUID(), t.tenant_id, s.policy_key, s.policy_value, s.value_unit, s.effective_from,
       s.label, s.description, s.is_statutory
FROM (SELECT DISTINCT tenant_id FROM employees) AS t
CROSS JOIN (
    -- ── 육아휴직 ──
              SELECT 'childcare_leave_max_months'    AS policy_key, 18.0000 AS policy_value,
                     'count' AS value_unit, DATE '2025-02-23' AS effective_from,
                     '육아휴직 최대 기간(개월)'        AS label,
                     '한 자녀당 쓸 수 있는 육아휴직의 최대 개월 수'  AS description, 1 AS is_statutory
    UNION ALL SELECT 'childcare_leave_split_count', 3.0000, 'count', DATE '2025-02-23',
                     '육아휴직 분할 횟수', '육아휴직을 나눠 쓸 수 있는 횟수', 1

    -- ── 출산전후휴가 ──
    UNION ALL SELECT 'maternity_leave_days', 90.0000, 'day', DATE '2018-05-29',
                     '출산전후휴가(일)', '출산 전후로 주는 휴가 일수(단태아 기준)', 1
    UNION ALL SELECT 'maternity_leave_days_multiple', 120.0000, 'day', DATE '2018-05-29',
                     '출산전후휴가 다태아(일)', '쌍둥이 이상일 때의 휴가 일수', 1

    -- ── 가족돌봄휴직 ──
    UNION ALL SELECT 'family_care_leave_max_days', 90.0000, 'day', DATE '2020-01-01',
                     '가족돌봄휴직 최대(일)', '1년 동안 쓸 수 있는 가족돌봄휴직 일수', 1
    UNION ALL SELECT 'family_care_leave_min_split_days', 30.0000, 'day', DATE '2020-01-01',
                     '가족돌봄휴직 분할 최소(일)', '나눠 쓸 때 한 번에 최소 며칠 이상이어야 하는지', 1

    -- ── 회사 규정 (법정 아님 — is_statutory=0) ──
    -- 질병·개인사정 휴직은 법정 의무가 아니라 회사가 정한다. 기본을 0 으로 두면
    -- "규정 없음" 과 "0개월" 이 구분되지 않으므로, 흔한 관행값을 출발점으로 두고 고치게 한다.
    UNION ALL SELECT 'sick_leave_max_months', 6.0000, 'count', DATE '2018-05-29',
                     '질병휴직 최대(개월)', '회사가 정하는 질병휴직 한도(법정 의무 아님)', 0
    UNION ALL SELECT 'personal_leave_max_months', 12.0000, 'count', DATE '2018-05-29',
                     '개인사정 휴직 최대(개월)', '회사가 정하는 개인사정 휴직 한도(법정 의무 아님)', 0
) AS s
WHERE NOT EXISTS (
    -- 🔴 이미 그 회사가 그 열쇠를 가지고 있으면 건드리지 않는다(헌법 #1 덮어쓰기 금지 · #11).
    --    DB-96 과 달리 '열쇠 단위' 로 본다 — DB-96 이 이미 깔려 있어서
    --    '테넌트 단위' 로 보면 이 시드가 통째로 건너뛰어진다.
    SELECT 1 FROM labor_policy_settings p
    WHERE p.tenant_id = t.tenant_id AND p.policy_key = s.policy_key
);
