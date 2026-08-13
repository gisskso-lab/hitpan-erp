-- ═══════════════════════════════════════════════════════════════
-- DB-96 : 노무 기준값 설정 (그룹웨어 단계5 — 연차 엔진의 토대)
-- 작성: 2026-08-13 · 사장님 결재 = 그룹웨어 단계4~9 일괄 전결
-- ═══════════════════════════════════════════════════════════════
--
-- 🔴 왜 이 표가 필요한가 — 사장님이 정한 설계 방향이다
--
--   사장님(2026-08-12): "어차피 지금 자세하게 법령조사하고 만들어도
--                        법이라는 게 언제, 어떻게 바뀔지는 몰라. 계속 모니터링 해야 해."
--
--   조사가 이미 증명했다. 우리가 "정확히 조사한" 것들이 최근에 바뀐 것들이다:
--     · 육아휴직 1년 → 1년 6개월 (2025-02)
--     · 국민연금 9% → 9.5%, 2033년까지 매년 인상 확정 (2026-01)
--     · 건강보험 7.09% → 7.19% (2026-01)
--     · 간이세액표 개정 (2026-02)
--
--   ⇒ 지금 정확한 것이 내년에 틀린다. 그게 정상이다.
--
--   설계도 §0 지침 3가지:
--     ① 법정값은 전부 설정 테이블. 하드코딩 금지, **상수로 빼는 것도 금지**
--        (재배포가 필요하면 실패다)
--     ② 값마다 `적용시작일`을 둔다. 법은 시행일이 있고 **과거분은 옛 값으로 계산**해야 한다
--     ③ "이 값은 언제 기준인가" 를 화면에 표시
--
--   실측 — 지금 연차 15일이 어디에 있나:
--     · src/HitPan.Web/Pages/HR/HrLeavePage.razor:115      `_totalLeave = 15m`
--     · src/HitPan.Web/Pages/Settings/EmployeePage.razor.cs:85 `_annualTotal = 15m`
--   ⇒ **화면 코드에 박혀 있다.** 법이 바뀌면 재배포해야 한다. 그게 설계도가 말한 실패다.
--
-- 🔴 이 표에 담는 것 / 안 담는 것
--
--   담는다   — 자주 바뀌는 '값' (연차 일수·기준 시간·요율)
--   안 담는다 — 거의 안 바뀌는 '구조' (신청→결재→반영 흐름, 사원이 축, 이력이 남는다)
--
--   ⚠️ 회사와 직원의 '약정'(주당 근로시간 등)은 여기가 아니라 사원마다 갖는 칸이다(DB-94).
--      여기는 **법정 기준**과 **회사 규정**이다.
--
-- 🔴 반자동 원칙 (사장님: "히트판은 100%자동화는 없어. 무조건 반자동이야")
--
--   여기 값은 '계산의 출발점' 이다. 이 값으로 연차를 **제안**하고,
--   사람이 고치고(이력 남김), 확정한다. 값 자체도 관리자가 고칠 수 있다.
--   ⚠️ 자동값은 **법정 최소**다. 더 줄 수는 있어도 덜 주면 위법이다.
--
-- 멱등: CREATE TABLE IF NOT EXISTS + INSERT ... NOT EXISTS.
-- ═══════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS `labor_policy_settings` (
  `policy_id`     varchar(36)  NOT NULL COMMENT 'PK',
  `tenant_id`     varchar(36)  NOT NULL COMMENT '테넌트(헌법 #2 — JWT 클레임에서만 온다)',
  `policy_key`    varchar(60)  NOT NULL COMMENT '기준값 열쇠. 예: annual_leave_base_days',
  `policy_value`  decimal(12,4) NOT NULL COMMENT '값. 일수·시간·비율 모두 숫자 하나로 담는다',
  `value_unit`    varchar(20)  NOT NULL DEFAULT 'day' COMMENT '단위: day|hour|rate|count',
  `effective_from` date        NOT NULL COMMENT '🔴 적용 시작일. 과거분은 옛 값으로 계산한다',
  `effective_to`   date        DEFAULT NULL COMMENT '적용 종료일. NULL=현재 유효',
  `label`         varchar(100) NOT NULL COMMENT '사람이 읽는 이름. 화면에 그대로 보여준다',
  `description`   varchar(300) DEFAULT NULL COMMENT '무엇을 뜻하는 값인지. 근거 조문이 아니라 설명',
  `is_statutory`  tinyint(1)   NOT NULL DEFAULT 1 COMMENT '1=법정 기준(줄이면 위법) 0=회사 규정',
  `updated_by`    varchar(36)  DEFAULT NULL COMMENT '누가 고쳤나',
  `updated_reason` varchar(200) DEFAULT NULL COMMENT '🔴 왜 고쳤나 — 반자동은 수정 이력이 필수다',
  `created_at`    datetime(6)  NOT NULL DEFAULT current_timestamp(6),
  `updated_at`    datetime(6)  NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`policy_id`),
  -- 같은 열쇠라도 시행일이 다르면 따로 산다 — 과거 계산에 옛 값이 필요하기 때문이다.
  UNIQUE KEY `uk_policy_tenant_key_from` (`tenant_id`, `policy_key`, `effective_from`),
  KEY `idx_policy_lookup` (`tenant_id`, `policy_key`, `effective_from`, `effective_to`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='노무 기준값 — 법이 바뀌면 값만 갈아끼운다(코드 재배포 없이). DB-96';

-- ═══════════════════════════════════════════════════════════════
-- 기본값 시드 — 출발점이지 정답이 아니다
-- ═══════════════════════════════════════════════════════════════
--
-- ⚠️ 여기 숫자는 **2026-08 시점의 것**이다. 바뀌면 관리자가 새 행을 추가한다
--    (기존 행을 고치지 않는다 — 과거분 계산이 틀어진다).
--
-- ⚠️ 법령 해석이 필요한 값은 넣지 않았다. 사장님 지시:
--    "법리해석에 대한 부분은 너무 디테일하게 가져가지 말고, 큰 틀을 일단 만들어."
--    ⇒ 큰 틀에 필요한 최소만 둔다. 세부는 N차에 붙인다.

INSERT INTO labor_policy_settings
  (policy_id, tenant_id, policy_key, policy_value, value_unit, effective_from,
   label, description, is_statutory)
SELECT UUID(), t.tenant_id, s.policy_key, s.policy_value, s.value_unit, '2018-05-29',
       s.label, s.description, s.is_statutory
FROM (SELECT DISTINCT tenant_id FROM employees) AS t
CROSS JOIN (
    -- 1년 이상 근속자의 기본 연차 일수
              SELECT 'annual_leave_base_days'   AS policy_key, 15.0000 AS policy_value,
                     'day'   AS value_unit, '기본 연차 일수'          AS label,
                     '1년 이상 근속한 직원에게 주는 연차 일수'          AS description, 1 AS is_statutory

    -- 3년 이상부터 2년마다 하루씩 늘어난다
    UNION ALL SELECT 'annual_leave_extra_per_years', 1.0000, 'day', '가산 연차 일수',
                     '가산 주기마다 늘어나는 연차 일수', 1
    UNION ALL SELECT 'annual_leave_extra_cycle_years', 2.0000, 'count', '가산 주기(년)',
                     '몇 년마다 연차가 하루 늘어나는지', 1
    UNION ALL SELECT 'annual_leave_extra_start_years', 3.0000, 'count', '가산 시작 근속(년)',
                     '가산이 시작되는 근속 연수', 1
    UNION ALL SELECT 'annual_leave_max_days', 25.0000, 'day', '연차 상한(일)',
                     '가산을 포함한 연차의 최대 일수', 1

    -- 1년 미만 근속자는 한 달 개근에 하루
    UNION ALL SELECT 'monthly_leave_days_under_1y', 1.0000, 'day', '1년 미만 월차(일)',
                     '1년이 안 된 직원이 한 달 개근하면 받는 일수', 1

    -- 판정 기준선
    UNION ALL SELECT 'small_business_threshold', 5.0000, 'count', '소규모 사업장 기준(명)',
                     '상시근로자가 이 수 미만이면 연차 부여 의무가 달라진다', 1
    UNION ALL SELECT 'short_time_weekly_hours', 15.0000, 'hour', '단시간 근로 기준(주 시간)',
                     '주당 소정근로시간이 이 시간 미만이면 연차·주휴가 달라진다', 1
) AS s
WHERE NOT EXISTS (
    -- 🔴 이미 기준값을 손댄 회사는 건드리지 않는다(헌법 #1 덮어쓰기 금지 · #11).
    SELECT 1 FROM labor_policy_settings p WHERE p.tenant_id = t.tenant_id
);
