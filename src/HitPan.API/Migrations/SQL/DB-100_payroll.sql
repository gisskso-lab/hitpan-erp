-- ═══════════════════════════════════════════════════════════════
-- DB-100 : 급여 명세 (그룹웨어 단계8)
-- 작성: 2026-08-13 · 사장님 결재 = 그룹웨어 단계4~9 일괄 전결
-- ═══════════════════════════════════════════════════════════════
--
-- 🔴 사장님이 정한 방식 (2026-08-13, 원문)
--
--   "급여는 자동계산하지 말고 수동으로 int값 직접 받아서 입력하는게 가장 깔끔함"
--   "각 고객사 니즈나 사정도 부합시킬 수 있고."
--   "히트판은 100%자동화는 없어. 무조건 반자동이야." (2026-08-12)
--
--   ⇒ 4대보험 요율·간이세액표를 우리가 계산하지 않는다. **금액을 받는다.**
--
--   왜 이게 맞나:
--     · 국민연금 9%→9.5% (2026-01), 건강보험 7.09%→7.19% (2026-01),
--       간이세액표 개정 (2026-02) — 우리가 "정확히 조사한" 것이 매년 바뀐다.
--     · 회사마다 상여 주기·수당 종류·비과세 항목이 전부 다르다.
--     · 계산해서 틀리면 **직원 돈이 틀리고**, 그건 되돌리기 어렵다.
--   ⇒ 금액을 그대로 받으면 어떤 회사든, 법이 어떻게 바뀌든 그대로 된다.
--      계산은 회사가 쓰던 방식(세무사·엑셀·공단 프로그램)을 그대로 쓰고,
--      히트판은 **그 결과를 담고·명세서로 뽑고·이력을 남긴다.**
--
-- 🔴 급여 보호는 **암호화가 아니라 권한 계층**으로 한다 (사장님 결정 2026-08-13)
--
--   사장님 원문:
--     "급여 암호화 / 암호 = 부모계정 비번, 담당자 계정 비번"
--     "권한 계층분리로 급여를 관리해도 충분히 됨."
--     "히트판의 계층분리는 굉장히 촘촘하게 설계되어있음"
--
--   ⇒ 맞다. 실측하니 촘촘하다 — user_permissions 가 **메뉴별 × 5동작**
--     (can_view · can_create · can_update · can_delete · can_export) 으로 갈리고,
--     부모계정(tenant_admin)은 PermissionService 에서 바이패스한다.
--
--   🔴 처음엔 금액 칸을 VARBINARY 로 암호화하려 했다가 **걷어냈다.** 이유:
--     · 컬럼 암호화는 **DB 파일을 통째로 훔쳐갔을 때만** 의미가 있다.
--       로그인만 하면 화면에 그대로 보이므로, 실제 위협(내부자 열람)을 못 막는다.
--     · 권한으로 막으면 **볼 사람만 본다** — 부모계정과 급여 담당자.
--       그게 실무에서 실제로 필요한 보호다.
--     · 항목마다 복호하면 목록이 느리고, **정렬·집계·합계가 불가능**하다.
--       급여는 "이 달 총액 얼마" 를 매번 물어보는 자료다.
--
--   ⇒ 금액은 **평문 decimal** (헌법 #4 — float/double 금지).
--      접근은 menu_code='PAYROLL' 권한으로 막는다.
--
--   ⚠️ employees.base_salary 의 기존 암호화는 **그대로 둔다**(헌법 #1).
--      그 칸은 이미 그렇게 저장돼 있고, 바꾸면 기존 자료를 못 읽는다.
--
-- 🔴 담는 것 / 안 담는 것
--
--   담는다   — 누구의 · 몇 년 몇 월분 · 항목별 금액 · 합계 · 지급일 · 확정 여부
--   안 담는다 — 4대보험 요율 계산 · 소득세 계산 · 연말정산 · 원천징수이행상황신고
--             ⇒ 세무 영역이다. 회사가 쓰던 방식을 그대로 쓴다.
--
-- 멱등: CREATE TABLE IF NOT EXISTS + INSERT ... NOT EXISTS.
-- ═══════════════════════════════════════════════════════════════

-- ───────────────────────────────────────────────────────────────
-- ① 급여 명세 (머리)
-- ───────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS `payroll_slips` (
  `slip_id`       varchar(36)  NOT NULL COMMENT 'PK',
  `tenant_id`     varchar(36)  NOT NULL COMMENT '테넌트(헌법 #2 — JWT 클레임에서만 온다)',
  `employee_id`   varchar(36)  NOT NULL COMMENT '누구의 급여인가',

  -- 귀속 — "몇 년 몇 월분" 이다. 지급일과 다르다(2월분을 3월에 주는 회사가 많다).
  `pay_year`      int          NOT NULL COMMENT '귀속 연도',
  `pay_month`     int          NOT NULL COMMENT '귀속 월 (1~12)',
  `pay_date`      date         DEFAULT NULL COMMENT '실제 지급일. 사람이 넣는다',

  -- 🔴 합계 — 평문 decimal (헌법 #4 금액은 decimal, float/double 금지)
  --    개별 항목은 암호화하되 합계는 평문이다. 목록·정렬·회계 이관에 필요하다.
  `total_payment` decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '총지급액',
  `total_deduct`  decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '총공제액',
  `net_payment`   decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '실지급액(총지급 - 총공제)',

  -- 확정 — 확정 전에는 고칠 수 있고, 확정하면 잠긴다.
  --   draft=작성중 · confirmed=확정 · paid=지급완료 · cancelled=취소
  `status`        varchar(20)  NOT NULL DEFAULT 'draft' COMMENT '진행 단계',
  `confirmed_by`  varchar(36)  DEFAULT NULL COMMENT '누가 확정했나',
  `confirmed_at`  datetime(6)  DEFAULT NULL,

  -- 🔴 휴직 연동 — 단계6 에서 사람이 정해 둔 금액을 그대로 가져온다.
  --    사장님: "그러면 자연스럽게 급여, 회계이슈도 해결될듯"
  `absence_id`    varchar(36)  DEFAULT NULL COMMENT '이 달에 휴직이 걸려 있으면 그 건',

  `memo`          varchar(500) DEFAULT NULL COMMENT '비고. 자유롭게 쓴다',

  `created_by`    varchar(36)  DEFAULT NULL,
  `created_at`    datetime(6)  NOT NULL DEFAULT current_timestamp(6),
  `updated_at`    datetime(6)  NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),

  PRIMARY KEY (`slip_id`),
  -- 같은 사람의 같은 달 명세는 하나뿐이다. 두 장이면 어느 것이 진짜인지 모른다.
  UNIQUE KEY `uk_payroll_emp_month` (`tenant_id`, `employee_id`, `pay_year`, `pay_month`),
  KEY `idx_payroll_month` (`tenant_id`, `pay_year`, `pay_month`),
  KEY `idx_payroll_status` (`tenant_id`, `status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='급여 명세 — 금액을 사람이 직접 넣는다. 계산하지 않는다. DB-100';

-- ───────────────────────────────────────────────────────────────
-- ② 급여 항목 (줄)
-- ───────────────────────────────────────────────────────────────
--
-- 🔴 왜 항목을 따로 두나 — 회사마다 수당이 다르기 때문이다.
--   어떤 회사는 식대·차량유지비·직책수당, 어떤 회사는 야근수당·근속수당.
--   칸을 고정하면(기본급·수당1·수당2...) 회사가 늘리고 싶을 때 못 늘린다.
--   ⇒ 줄로 두고 **이름도 사람이 적는다.**

CREATE TABLE IF NOT EXISTS `payroll_slip_lines` (
  `line_id`     varchar(36) NOT NULL COMMENT 'PK',
  `tenant_id`   varchar(36) NOT NULL,
  `slip_id`     varchar(36) NOT NULL COMMENT '어느 명세의 줄인가',

  -- payment=지급 · deduct=공제
  `line_type`   varchar(20) NOT NULL COMMENT 'payment=지급 · deduct=공제',
  `item_name`   varchar(60) NOT NULL COMMENT '항목 이름. 사람이 적는다(기본급·식대·국민연금 등)',

  -- 🔴 금액은 평문 decimal 이다(헌법 #4). 보호는 권한 계층이 한다 —
  --    사장님: "권한 계층분리로 급여를 관리해도 충분히 됨."
  --    암호화하면 목록이 느리고 정렬·집계가 안 된다. 급여는 합계를 매번 묻는 자료다.
  `amount`      decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '금액. 사람이 직접 넣는다',

  `sort_order`  int         NOT NULL DEFAULT 0 COMMENT '명세서에 찍히는 순서',
  `is_taxable`  tinyint(1)  NOT NULL DEFAULT 1 COMMENT '1=과세 0=비과세. 사람이 고른다',
  `memo`        varchar(200) DEFAULT NULL,

  `created_at`  datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at`  datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),

  PRIMARY KEY (`line_id`),
  KEY `idx_payroll_line_slip` (`tenant_id`, `slip_id`, `line_type`, `sort_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='급여 항목 — 회사마다 수당이 달라 이름도 사람이 적는다. DB-100';

-- ───────────────────────────────────────────────────────────────
-- ③ 퇴직금
-- ───────────────────────────────────────────────────────────────
--
-- 🔴 퇴직금도 **금액을 받는다.** 계산하지 않는다.
--   법정 산식(평균임금 × 30일 × 재직일수/365)이 있지만:
--     · 평균임금 산정에 상여·연차수당을 어떻게 넣는지가 회사마다 다르고 다툼이 잦다
--     · 퇴직연금(DB·DC·IRP)이면 산식 자체가 다르다
--     · 틀리면 **법적 분쟁**이 된다
--   ⇒ 계산은 회사·노무사가 하고, 히트판은 담고 명세로 뽑는다.
--   ⚠️ 자동값은 법정 최소다 — 더 줄 순 있어도 덜 주면 위법이다. 그래서 더더욱
--      우리가 계산해서 넣으면 안 된다.

CREATE TABLE IF NOT EXISTS `severance_payments` (
  `severance_id`  varchar(36)  NOT NULL COMMENT 'PK',
  `tenant_id`     varchar(36)  NOT NULL,
  `employee_id`   varchar(36)  NOT NULL,

  -- 기간 — 사람이 넣는다.
  `join_date`     date         NOT NULL COMMENT '입사일',
  `resign_date`   date         NOT NULL COMMENT '퇴사일',
  `service_days`  int          NOT NULL DEFAULT 0 COMMENT '재직일수. 사람이 넣거나 화면이 보여준다',

  -- 🔴 금액 — 전부 사람이 넣는다
  `avg_wage`      decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '평균임금(1일). 사람이 넣는다',
  `severance_amount` decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '퇴직금. 사람이 넣는다',
  `tax_amount`    decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '퇴직소득세 등 공제액',
  `net_amount`    decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '실지급액',

  -- 지급 방식 — 회사가 고른다. 우리가 판정하지 않는다.
  --   direct=회사 직접지급 · db=확정급여형 · dc=확정기여형 · irp=IRP계좌
  `pay_type`      varchar(20)  NOT NULL DEFAULT 'direct' COMMENT '지급 방식',
  `pay_date`      date         DEFAULT NULL COMMENT '지급일',

  `status`        varchar(20)  NOT NULL DEFAULT 'draft' COMMENT 'draft/confirmed/paid/cancelled',
  `confirmed_by`  varchar(36)  DEFAULT NULL,
  `confirmed_at`  datetime(6)  DEFAULT NULL,

  `calc_basis`    varchar(500) DEFAULT NULL COMMENT '산정 근거를 글로 남긴다. 분쟁 시 설명해야 한다',
  `memo`          varchar(500) DEFAULT NULL,

  `created_by`    varchar(36)  DEFAULT NULL,
  `created_at`    datetime(6)  NOT NULL DEFAULT current_timestamp(6),
  `updated_at`    datetime(6)  NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),

  PRIMARY KEY (`severance_id`),
  KEY `idx_severance_emp` (`tenant_id`, `employee_id`),
  KEY `idx_severance_status` (`tenant_id`, `status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='퇴직금 — 금액을 사람이 직접 넣는다. 법정 산식을 우리가 돌리지 않는다. DB-100';
