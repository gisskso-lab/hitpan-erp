-- ═══════════════════════════════════════════════════════════════
-- DB-102 : 휴직 저장 불능 봉합 (그룹웨어 단계6 결함)
-- 작성: 2026-08-13 · 근거 20260813검2 코드리뷰
-- ═══════════════════════════════════════════════════════════════
--
-- 🔴 실측으로 확인한 결함 (추측 아님)
--
--   mysql> INSERT INTO employee_leave_of_absence
--            (absence_id, tenant_id, employee_id, start_date, end_date,
--             status, reason, pay_type, pay_amount, pay_note, created_by) VALUES (...);
--   ERROR 1364 (HY000): Field 'absence_type' doesn't have a default value
--
--   ⇒ 휴직 저장이 **100% 실패**한다. 단계6 이 통째로 안 돈다.
--
-- 왜 이렇게 됐나
--
--   DB-98 은 absence_type 을 NOT NULL(기본값 없음)로 만들었는데,
--   그 뒤 설계에서 "휴직 종류를 고르게 하지 않는다"(사장님 지시 — 사유를 글로 받는다)로
--   바뀌면서 화면·INSERT 에서 종류가 빠졌다. **컬럼만 남고 값이 안 들어온다.**
--
--   exceeds_standard 도 같은 처지다 — NOT NULL 인데 INSERT 에 없다.
--
-- 🔴 왜 컬럼을 지우지 않나 (헌법 #1 · #37)
--
--   "안 읽힌다 ≠ 잔재". 종류를 안 고르게 한 것은 **입력 방식**의 결정이지,
--   그 정보가 영영 필요 없다는 뜻이 아니다(육아휴직·병가 통계는 언젠가 필요하다).
--   ⇒ 컬럼은 남기고 **기본값을 준다.**
--
-- 🔴 왜 DB-98 을 고치지 않고 새 마이그를 만드나
--
--   DB-98 은 이미 적용됐다(schema_migrations success=1). 파일을 고쳐도 다시 안 돈다.
--   멱등이 그렇게 설계돼 있다 — 되돌아가지 않는 것이 안전이다.
--
-- ═══════════════════════════════════════════════════════════════

-- absence_type : 기본값 'other'(기타). 화면이 종류를 안 받으므로 전부 여기로 들어간다.
--   실제 사유는 reason 칸에 사람이 글로 적는다(사장님 지시 — 반자동).
ALTER TABLE `employee_leave_of_absence`
  MODIFY COLUMN `absence_type` varchar(30) NOT NULL DEFAULT 'other'
  COMMENT '휴직 종류. 화면에서 고르지 않는다(사장님: 사유를 글로 받는다) — 기본 other. DB-102';

-- exceeds_standard : 법정 기준을 넘겼는지. 값이 안 오면 0(안 넘김)이 맞다.
ALTER TABLE `employee_leave_of_absence`
  MODIFY COLUMN `exceeds_standard` tinyint(1) NOT NULL DEFAULT 0
  COMMENT '법정 기준 초과 여부. 기본 0. DB-102';
