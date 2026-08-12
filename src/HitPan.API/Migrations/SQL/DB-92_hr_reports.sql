-- ============================================================================
-- DB-92: 업무보고서 4종 (일일·주간·월간·경위서)
-- 작(2026-08-13) 그룹웨어 단계3
--
-- 사장님 지시(2026-08-12):
--   "일일보고서, 주간보고서, 월간보고서, 경위서 메뉴 추가"
--
-- 🔴 설계 정정 — "DDL 변경 0" 이 틀렸다
--   설계 문서에 "보고서는 토대 최상 — DDL 변경 0, 딕셔너리 4줄 + 화면" 이라고 썼으나
--   실측하니 틀렸다. approval_documents 는 결재 흐름만 담고 본문은 ref_id 로 원본을
--   가리키는 구조다. 즉 보고서 본문이 들어갈 자리가 없다.
--     - memo 는 varchar(500) — 월간보고서·경위서를 담을 수 없다
--     - ref_id 가 NOT NULL — 원본 표가 반드시 있어야 한다
--   ⇒ 본문 표가 필요하다.
--
-- 🔴 4종을 각각 만들지 않고 한 표에 담는다
--   결재 흐름·조회·권한이 전부 같고 종류(report_type)만 다르다.
--   표를 4개로 가르면 같은 코드를 네 벌 써야 하고, 종류가 늘 때마다 표가 늘어난다.
--   (사장님이 나중에 "출장보고서" 를 지시하면 값 하나만 늘리면 된다)
--
-- 🔴 마이그레이션은 이 폴더(src/HitPan.API/Migrations/SQL/)에만 둔다.
--   2026-08-12 사고 — installer/migrations/ 에 뒀더니 배포본에 안 실려
--   고객 화면이 죽었다. 빌드 0/0·시험·ddl-smoke 가 전부 통과했는데도.
--   게이트: MigrationLocationGuardTests
--
-- 헌법: #17 InnoDB 명시 · #1 추가만(기존 표 무변경) · #2 tenant_id 격리
-- ============================================================================

CREATE TABLE IF NOT EXISTS `hr_reports` (
  `report_id`     varchar(36)  NOT NULL,
  `tenant_id`     varchar(36)  NOT NULL,

  -- 🔴 사원 기준이다(설계서 §3-5 축 확정). 계정이 없어도 보고서를 쓸 수 있어야 한다
  --    — 실측상 사원 12명 중 계정 보유는 1명뿐이라, 계정 기준으로 잡으면 11명이 사라진다.
  `employee_id`   varchar(36)  NOT NULL,

  -- daily(일일) / weekly(주간) / monthly(월간) / incident(경위서)
  `report_type`   varchar(20)  NOT NULL,

  -- 보고 대상 기간. 일일은 시작=종료, 주간·월간은 범위.
  -- 경위서는 "사건이 일어난 날" 을 시작·종료 같게 넣는다.
  `period_start`  date         NOT NULL,
  `period_end`    date         NOT NULL,

  `title`         varchar(200) NOT NULL,

  -- 🔴 본문. text 로 둔다 — varchar 로는 월간보고서·경위서가 잘린다.
  --    잘리는 건 500 이 안 나서 더 위험하다(고객이 저장됐다고 믿는다).
  `content`       text         NOT NULL,

  -- 경위서 전용. 다른 종류에서는 비어 있다.
  -- 경위서는 "무슨 일이 있었나(content)" 와 "왜 그랬나·어떻게 할 건가" 를 나눠 적는 서식이다.
  `cause`         text         DEFAULT NULL,
  `action_plan`   text         DEFAULT NULL,

  -- draft(작성중) / pending(결재중) / approved(완료) / rejected(반려)
  -- 🔴 draft 를 두는 이유 — 월간보고서는 한 번에 다 못 쓴다. 저장해 두고 이어 쓴다.
  --    결재는 pending 부터 돈다(헌법 #6 — 확정은 사람이).
  `status`        varchar(20)  NOT NULL DEFAULT 'draft',

  `submitted_at`  datetime(6)  DEFAULT NULL,
  `approved_by`   varchar(36)  DEFAULT NULL,
  `approved_at`   datetime(6)  DEFAULT NULL,
  `reject_reason` varchar(200) DEFAULT NULL,

  `created_at`    datetime(6)  NOT NULL DEFAULT current_timestamp(6),
  `updated_at`    datetime(6)  NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),

  PRIMARY KEY (`report_id`),

  -- 내 보고서 목록 — 가장 잦은 조회.
  KEY `idx_hr_reports_tenant_emp` (`tenant_id`, `employee_id`, `report_type`, `period_start`),

  -- 기간별 조회(부서장이 이번 달 것을 볼 때).
  KEY `idx_hr_reports_tenant_period` (`tenant_id`, `report_type`, `period_start`),

  -- 결재 대기 목록.
  KEY `idx_hr_reports_tenant_status` (`tenant_id`, `status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------------------------------------------------------
-- 결재 설정은 여기서 미리 넣지 않는다.
--
-- 🔴 처음에는 보고서 4종을 approval_settings 에 시딩하려 했으나 걷어냈다. 두 가지 이유다.
--
--   ① 실측: approval_settings 는 [결재 설정] 화면이 저장할 때 행을 만든다
--      (ApprovalService.cs:118 INSERT ... ON DUPLICATE KEY UPDATE). 현재 행 0건.
--      즉 "설정이 없다 = 결재를 안 쓴다" 가 정상 상태이고, 미리 넣을 이유가 없다.
--
--   ② 헌법 #11 — 권한·정책은 어드민이 직접 설정한다. 업종·규모별 템플릿을 우리가
--      깔아주지 않는다. 우리가 행을 만들어 두면 고객이 만든 적 없는 설정이 생긴다.
--
--   ⚠️ 그리고 처음 쓴 INSERT 는 실행되지도 않았을 것이다 — PK 인 setting_id 를 빠뜨렸다.
--      DESCRIBE 로 값·제약을 확인하고서야 알았다(헌법 #13). 8/12 에도 컬럼만 보고
--      값을 확인하지 않아 P0 를 냈다. 같은 실수를 반복하지 않으려고 적어 둔다.
--
-- ⇒ 고객이 [결재 설정]에서 보고서 종류를 켜고 결재선을 짜야 결재가 돈다.
--    켜지 않으면 보고서는 결재 없이 저장·조회만 된다(그것도 정상 운영이다).
-- ----------------------------------------------------------------------------
