-- ═══════════════════════════════════════════════════════════════
-- DB-106 : 전자 퇴직서 (사직서) — 입사/퇴사 메뉴
-- 작성: 2026-08-24 · 작20260824작2 [4]
-- ═══════════════════════════════════════════════════════════════
--
-- 🔴 번호 사고 봉합 (2026-08-24, 1.3.5 동시검증이 적발 — P0)
--
--   이 파일은 처음에 `DB-100_resignation_letters.sql` 로 나갔다. 그런데 `DB-100` 은
--   **이미 `DB-100_payroll.sql`(급여, 8/14 적용분)이 쓰고 있던 번호**였다.
--
--   MigrationRunner 는 파일명이 아니라 **번호로 식별자를 만든다**(MigrationRunner:70-76):
--       DB-100_payroll.sql             → MigrationId "DB-100"
--       DB-100_resignation_letters.sql → MigrationId "DB-100"   ← 같은 ID
--
--   그리고 `schema_migrations` 에 success=1 인 ID 는 건너뛴다(MigrationRunner:103-107).
--   ⇒ 급여 마이그가 8월에 이미 `DB-100` 을 완료로 기록해 둔 탓에,
--     퇴직서 SQL 은 **한 줄도 실행되지 않고 skip** 됐다.
--     표가 없으니 `/api/resignations` 만 500. 나머지 업무는 멀쩡히 돌았다
--     (Program.cs:426 — 마이그 실패가 앱을 죽이지 않는 설계라 증상이 화면 하나로 국한됐다).
--
-- 🔴 왜 빌드도 CI 도 못 잡았나
--   · 빌드 errors 0 + warnings 0 통과 — **파일 이름 문제라 컴파일러가 볼 수 없다.**
--   · 게시 게이트도 통과 — "새 SQL 이 있는데 requiresMigration=false" 만 보지,
--     **번호가 겹쳤는지는 아무도 안 봤다.**
--   · 로컬 실측도 통과 — 빈 DB 에 돌리면 `DB-100` 기록이 없어 그냥 실행된다.
--     🔴 **기적용 상태를 재현하지 않은 것**이 검증의 구멍이었다.
--       "빈 DB 에서 된다" 는 "고객 DB 에서 된다" 가 아니다.
--
-- ⇒ 재발 차단: `scripts/check-migration-id-collision.sh` 를 CI 에 붙였다.
--   번호 집합에 중복이 있으면 머지 자체가 막힌다. 사람 기억에 맡기지 않는다.
--
-- ⚠️ 내용은 한 글자도 안 바꿨다 — 번호만 100 → 106 (당시 실제 최대는 105였다).
--    `CREATE TABLE IF NOT EXISTS` 라 이미 표가 있는 환경에서도 안전하다.
--
-- 사장님 지시(2026-08-24):
--   "전자근로계약서 = 입사/퇴사 로 메뉴변경 전자근로계약서 작성, 전자 퇴직서 작성"
--   착수 전 결재: "퇴직서까지 오늘 다 만든다"
--
-- 🔴 왜 새 표가 필요한가 — 기존 것으로는 못 담는다 (실측)
--
--   실측 1. `employees` 의 퇴사 칸
--     `is_resigned` · `resign_date` · `resign_reason` · `work_status`
--     ⇒ 이건 **관리자가 처리한 결과**다. 이미 멀쩡히 돈다(EmployeeResignDialog·ResignAsync).
--        여기 없는 것은 **직원이 올리는 문서** 쪽이다 —
--        누가 언제 사직 의사를 밝혔고, 결재가 어디까지 갔고, 회사가 수리했는지.
--     🔴 그래서 이 표는 `employees` 를 대체하지 않는다. **앞단**이다.
--        문서가 수리되면 그때 기존 퇴사 처리(ResignAsync)가 돈다 — 그 로직은 안 건드린다.
--
--   실측 2. `labor_contracts` (전자근로계약서)
--     `start_date` · `salary_amount` · `working_hours` · `social_insurance` …
--     ⇒ **들어올 때 정하는 조건**을 담는 표다. 나갈 때 필요한 칸(희망 퇴사일·인수인계·
--        반납물·수리 여부)이 하나도 없다. 억지로 얹으면 계약서 조회가 사직서까지 긁는다.
--
--   ⇒ 입사(labor_contracts)와 퇴사(여기)는 **같은 메뉴 안의 다른 문서**다.
--      화면은 「입사/퇴사」 하나로 합치되, 표는 나눈다.
--
-- 🔴 담는 것 / 안 담는 것
--
--   담는다   — 누가 · 언제 냈고 · 언제 나가길 원하고 · 왜 · 인수인계 대상 ·
--             결재가 어디까지 갔고 · 회사가 수리했는지 · 실제 퇴사일
--   안 담는다 — 퇴직금 계산(급여는 수동입력 원칙 — 사장님) ·
--             4대보험 상실신고(회사 프로그램이 대신 신고해 줄 수 없다) ·
--             경력증명서 발급(별건)
--
-- 🔴 결재는 approval_documents 가 돈다 — 여기서 결재를 다시 구현하지 않는다.
--    doc_type = 'resignation' 은 ①(작20260823작1)에서 **이미 등재해 뒀다.**
--    ⚠️ 등재만 돼 있고 트리거가 없었다. 이번에 그 배선을 붙인다.
--
-- 🔴 상태 흐름 (한 방향으로만 간다)
--
--    draft ──제출──> pending ──승인──> approved ──처리──> completed
--                       │
--                       └──반려──> rejected ──(다시 쓰면)──> draft
--
--    withdrawn = 직원이 스스로 거둬들인 것. 반려와 다르다 —
--                반려는 회사가 물린 것이고, 철회는 본인이 물린 것이다.
--                섞으면 "왜 안 나갔나" 를 나중에 아무도 모른다.
--
-- 헌법 정합: #17(ENGINE=InnoDB 명시) · #4(금액 decimal — 여기는 금액 없음) ·
--            #1(기존 표·로직 무접촉) · #13(작성 전 DESCRIBE 완료)
-- ═══════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS `resignation_letters` (
  `resignation_id`    VARCHAR(36)   NOT NULL                COMMENT '사직서 ID',
  `tenant_id`         VARCHAR(36)   NOT NULL                COMMENT '테넌트 (JWT 클레임에서만 — 헌법 #2)',

  -- 누가
  `employee_id`       VARCHAR(36)   NOT NULL                COMMENT '사직하는 사원',
  `employee_name`     VARCHAR(100)  NOT NULL                COMMENT '작성 시점 이름 (사원명이 바뀌어도 문서는 그대로여야 한다)',
  `dept_name`         VARCHAR(100)  NULL                    COMMENT '작성 시점 부서 (같은 이유로 문자열로 박는다)',
  `position_name`     VARCHAR(50)   NULL                    COMMENT '작성 시점 직급',

  -- 언제 · 왜
  `resign_type`       VARCHAR(20)   NOT NULL DEFAULT 'voluntary'
                      COMMENT '자발(voluntary) · 권고사직(recommended) · 계약만료(expired) · 정년(retirement)',
  `desired_date`      DATE          NOT NULL                COMMENT '희망 퇴사일 (직원이 적는 날)',
  `actual_date`       DATE          NULL                    COMMENT '실제 퇴사일 (회사가 수리하며 정한 날 — 다를 수 있다)',
  `reason`            VARCHAR(500)  NULL                    COMMENT '사직 사유',

  -- 나가기 전 정리
  `handover_to`       VARCHAR(100)  NULL                    COMMENT '인수인계 받을 사람',
  `handover_note`     TEXT          NULL                    COMMENT '인수인계 내용',
  `return_items`      VARCHAR(500)  NULL                    COMMENT '반납물 (사원증·노트북·차량 등)',

  -- 어디까지 갔나
  `status`            VARCHAR(20)   NOT NULL DEFAULT 'draft'
                      COMMENT 'draft · pending · approved · rejected · completed · withdrawn',
  `approval_id`       VARCHAR(36)   NULL                    COMMENT 'approval_documents.approval_id — 결재는 그쪽이 돈다',
  `submitted_at`      DATETIME(6)   NULL                    COMMENT '제출(상신) 시각',
  `approved_at`       DATETIME(6)   NULL                    COMMENT '수리 시각',
  `reject_reason`     VARCHAR(500)  NULL                    COMMENT '반려 사유',

  -- 직원 서명 (전자근로계약서와 같은 방식)
  `signed_at`         DATETIME(6)   NULL                    COMMENT '직원이 서명한 시각',
  `signature_data`    LONGTEXT      NULL                    COMMENT '서명 이미지 (data URI)',

  `created_at`        DATETIME(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `created_by`        VARCHAR(36)   NULL,
  `updated_at`        DATETIME(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  `updated_by`        VARCHAR(36)   NULL,

  PRIMARY KEY (`resignation_id`),
  KEY `idx_tenant_emp`    (`tenant_id`, `employee_id`),
  KEY `idx_tenant_status` (`tenant_id`, `status`),
  KEY `idx_approval`      (`approval_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='전자 퇴직서(사직서) — 직원이 올리는 문서. 관리자 퇴사처리(employees)와 다른 자리다';

-- ── 권한 메뉴는 여기서 등재하지 않는다 ──
--
-- 🔴 실측(2026-08-24): `menu_master` 라는 표는 **없다.**
--    권한 메뉴 목록은 표가 아니라 **코드 두 곳**이 단일 진실원이다:
--      · 백엔드  `PermissionService.MenuList`
--      · 프론트  `PermissionPage.razor.cs` 의 `ErpMenus`
--    둘이 어긋나면 CI 「권한 메뉴 코드 정합」 잡이 잡는다(scripts/check-permission-menu-sync.sh).
--
-- ⇒ 사장님 지시 "권한에 연차부여 권한 추가" 는 그 두 곳에 넣는다. DDL 은 표만 만든다.
--
-- ⚠️ 하마터면 없는 표에 INSERT 를 쓸 뻔했다 — 마이그레이션이 통째로 실패했을 자리다.
--    헌법 #13(새 SQL 작성 전 DESCRIBE 의무)이 막았다.
