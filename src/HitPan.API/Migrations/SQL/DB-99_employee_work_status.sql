-- ═══════════════════════════════════════════════════════════════
-- DB-99 : 직원 상태(재직·휴직·연차) + 휴직 중 급여 직접 입력 (그룹웨어 단계6)
-- 작성: 2026-08-13 · 사장님 지시 2026-08-13
-- ═══════════════════════════════════════════════════════════════
--
-- 🔴 사장님 지시 원문 (2026-08-13)
--
--   "휴직은 모든걸 다 수동으로."
--   "상태처리 : 재직 휴직 연차"
--   "휴직시 급여 : 텍스트 박스로 수동입력"
--   "그러면 자연스럽게 급여, 회계이슈도 해결될듯"
--
--   ⇒ 맞다. 금액을 사람이 직접 넣으면 **계산할 것이 없어진다.**
--      급여도 회계도 그 숫자를 그대로 쓰면 되므로, 우리가
--      "무급이니까 0" · "일부지급이면 몇 %" 같은 판단을 할 이유가 사라진다.
--      법이 바뀌어도, 회사마다 규정이 달라도 영향을 안 받는다.
--
-- 🔴 ① 직원 상태 — 지금은 '재직 아니면 퇴사' 둘뿐이다
--
--   실측: employees 에 상태 칸이 없다.
--     is_active tinyint(1) · is_resigned · resign_date · resign_reason
--     (marriage_status 는 결혼 여부라 상관없다)
--
--   그래서 휴직자를 넣을 자리가 없었다:
--     is_active=0 으로 두면 → 퇴사자와 구분이 안 된다(복직시킬 근거가 사라진다)
--     is_active=1 로 두면 → 급여·연차 계산에 그대로 들어간다
--   **어느 쪽으로 둬도 사고다.**
--
--   ⇒ work_status 를 둔다. active(재직) / absence(휴직) / leave(연차)
--
--   ⚠️ is_active 를 **없애지 않는다**(헌법 #1 덮어쓰기 금지).
--      기존 화면·쿼리가 전부 is_active 를 보고 있어서, 지우면 그 자리가 다 깨진다.
--      is_active = 재직/퇴사 · work_status = 재직 중 어떤 상태인지. 층이 다르다.
--
-- 🔴 ② 휴직 중 급여 — 금액을 그대로 받는다
--
--   종전 설계는 unpaid/paid/partial 중에 고르게 했다. 그러면 '일부지급' 을 골랐을 때
--   **얼마인지 아무 데도 없다.** 결국 급여 만들 때 또 사람에게 물어야 한다.
--   ⇒ 금액 칸 하나로 바꾼다. 0 이면 무급, 넣으면 그 금액. 고를 것이 없어진다.
--
--   ⚠️ 금액은 decimal 이다(헌법 #4). float/double 로 두면 원 단위가 어긋난다.
--
-- 멱등: 컬럼이 이미 있으면 건너뛴다.
-- ═══════════════════════════════════════════════════════════════

-- ───────────────────────────────────────────────────────────────
-- ① employees.work_status
-- ───────────────────────────────────────────────────────────────
--
-- MariaDB 는 ADD COLUMN IF NOT EXISTS 를 지원한다. 두 번 돌아도 안전하다.

ALTER TABLE `employees`
  ADD COLUMN IF NOT EXISTS `work_status` varchar(20) NOT NULL DEFAULT 'active'
  COMMENT '재직 중 상태: active=재직 · absence=휴직 · leave=연차'
  AFTER `is_active`;

-- 조회가 잦은 자리다(조직도·급여·연차가 "지금 누가 휴직인가" 를 묻는다).
ALTER TABLE `employees`
  ADD INDEX IF NOT EXISTS `idx_emp_work_status` (`tenant_id`, `work_status`);

-- 기존 직원은 전부 재직으로 둔다.
-- ⚠️ 퇴사자(is_active=0)의 work_status 는 의미가 없다 — 재직 중 상태를 말하는 칸이라서다.
--    그래도 NOT NULL 이라 값이 필요하므로 기본값 그대로 둔다.
UPDATE employees
SET work_status = 'active'
WHERE work_status IS NULL OR TRIM(work_status) = '';

-- ───────────────────────────────────────────────────────────────
-- ② 휴직 중 급여 — 금액 직접 입력
-- ───────────────────────────────────────────────────────────────

ALTER TABLE `employee_leave_of_absence`
  ADD COLUMN IF NOT EXISTS `pay_amount` decimal(15,2) NOT NULL DEFAULT 0.00
  COMMENT '🔴 휴직 중 지급할 금액. 사람이 직접 넣는다(사장님 2026-08-13). 0=무급'
  AFTER `pay_type`;

ALTER TABLE `employee_leave_of_absence`
  ADD COLUMN IF NOT EXISTS `pay_note` varchar(200) DEFAULT NULL
  COMMENT '급여 관련 메모. 예: "고용보험에서 별도 수령" · "3개월분 선지급"'
  AFTER `pay_amount`;

-- ⚠️ pay_type 은 **남겨 둔다**(헌법 #1). 지우면 이미 저장된 건의 뜻이 사라진다.
--    앞으로는 금액이 정본이고, pay_type 은 화면에 붙는 이름표 정도로 쓴다.
--    금액이 0 이면 무급, 0 보다 크면 유급 — 사람이 넣은 숫자가 답이다.
UPDATE employee_leave_of_absence
SET pay_type = CASE WHEN pay_amount > 0 THEN 'paid' ELSE 'unpaid' END
WHERE pay_amount IS NOT NULL;
