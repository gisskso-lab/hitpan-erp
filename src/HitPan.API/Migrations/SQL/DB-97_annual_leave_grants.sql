-- ═══════════════════════════════════════════════════════════════
-- DB-97 : 연차 부여 이력 (그룹웨어 단계5 — 반자동 3단의 정본)
-- 작성: 2026-08-13 · 사장님 결재 = 그룹웨어 단계4~9 일괄 전결
-- ═══════════════════════════════════════════════════════════════
--
-- 🔴 반자동 3단 (사장님 2026-08-12)
--
--   "무조건 자동이 아닌, 자동계산하되 수동으로 수정가능하도록 해야지."
--   "히트판은 100%자동화는 없어. **무조건 반자동**이야."
--
--   정본 = 제안값 → 사람이 수정 → 확정  (3단)
--   🔴 수정 가능하면 **수정 이력이 필수**다 — 누가·언제·뭘·왜.
--      이력이 없으면 "내 연차가 왜 줄었냐" 에 답할 수 없고, 노동청 다툼에서 근거가 없다.
--
-- 🔴 왜 별도 표인가 — employees 두 칸으로는 못 담는다
--
--   지금 연차는 employees.annual_leave_total / annual_leave_used **숫자 두 개**뿐이다.
--   그래서 이런 걸 전혀 알 수 없다:
--     · 이 15일이 언제 어떤 기준으로 생긴 건지
--     · 누가 손으로 고쳤는지, 왜 고쳤는지
--     · 작년 연차인지 올해 연차인지 (회계연도가 넘어가면 뭉개진다)
--     · 자동 계산이 얼마를 제안했고 사람이 얼마로 확정했는지
--
--   ⇒ 부여 건마다 한 행. employees 의 두 칸은 **합계 캐시**로 남긴다
--     (기존 화면·쿼리가 그걸 보고 있어 끊으면 워크플로우가 깨진다 — 헌법 #20).
--
-- ⚠️ 이 표는 INSERT 위주지만 원장(stock_ledger·journal_lines)은 아니다.
--    수정은 '확정 전' 에만 되고, 확정 후 바꾸려면 새 행을 넣는다(조정 부여).
--
-- 멱등: CREATE TABLE IF NOT EXISTS.
-- ═══════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS `annual_leave_grants` (
  `grant_id`        varchar(36)  NOT NULL COMMENT 'PK',
  `tenant_id`       varchar(36)  NOT NULL COMMENT '테넌트(헌법 #2)',
  `employee_id`     varchar(36)  NOT NULL COMMENT '누구 연차인가',

  -- ── 언제 것인가 ──
  `grant_year`      smallint(6)  NOT NULL COMMENT '귀속 연도. 회계연도가 넘어가도 안 뭉개진다',
  `period_start`    date         NOT NULL COMMENT '이 연차가 쓰이는 기간 시작',
  `period_end`      date         NOT NULL COMMENT '기간 끝. 지나면 소멸(회사 규정에 따름)',

  -- ── 🔴 반자동 3단 ──
  `suggested_days`  decimal(5,1) NOT NULL COMMENT '① 자동 계산이 제안한 일수',
  `granted_days`    decimal(5,1) NOT NULL COMMENT '② 사람이 확정한 일수. 제안과 다를 수 있다',
  `is_adjusted`     tinyint(1)   NOT NULL DEFAULT 0 COMMENT '제안과 확정이 다른가(=사람이 고쳤나)',
  `adjust_reason`   varchar(300) DEFAULT NULL COMMENT '🔴 왜 고쳤나. 고쳤으면 반드시 남긴다',

  -- ── 무슨 근거로 제안했나 ──
  `grant_type`      varchar(30)  NOT NULL DEFAULT 'annual'
                    COMMENT 'annual=연차 / monthly=1년미만 월차 / adjust=조정 / carryover=이월',
  `service_years`   decimal(5,2) DEFAULT NULL COMMENT '계산 당시 근속 연수',
  `calc_basis`      varchar(500) DEFAULT NULL
                    COMMENT '🔴 어떤 기준값으로 계산했는지 그대로 적는다. 법이 바뀌어도 과거 계산을 설명할 수 있어야 한다',

  -- ── 상태 ──
  `status`          varchar(20)  NOT NULL DEFAULT 'draft'
                    COMMENT 'draft=제안됨(아직 반영 안 함) / confirmed=확정(잔여에 반영) / cancelled=취소',
  `confirmed_by`    varchar(36)  DEFAULT NULL COMMENT '③ 누가 확정했나',
  `confirmed_at`    datetime(6)  DEFAULT NULL COMMENT '언제 확정했나',

  `created_at`      datetime(6)  NOT NULL DEFAULT current_timestamp(6),
  `created_by`      varchar(36)  DEFAULT NULL,
  `updated_at`      datetime(6)  NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),

  PRIMARY KEY (`grant_id`),
  -- 같은 사람 같은 해에 같은 종류를 두 번 부여하지 않는다(조정·이월은 따로 세므로 종류를 포함).
  UNIQUE KEY `uk_grant_emp_year_type` (`tenant_id`, `employee_id`, `grant_year`, `grant_type`),
  KEY `idx_grant_emp` (`tenant_id`, `employee_id`, `grant_year`),
  KEY `idx_grant_status` (`tenant_id`, `status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='연차 부여 이력 — 제안→수정→확정 3단과 그 근거를 남긴다. DB-97';
