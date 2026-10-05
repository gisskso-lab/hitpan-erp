-- =============================================================
-- DB-137: 사원마스터 「사원계정」 칸 + 한 사원 = 한 계정 DB 보장
-- =============================================================
-- 근거: 작업지시서 docs/운영기록/20261005작5_사원계정연결_사원에서계정추가_아이디표기_작업지시서.md (PM 결재 P-6 P-9)
--       설계문서   docs/설계/erp/20261005_설계_사원계정연결_아이디표기.md §2-2 · §10 R-1 R-3
--
-- #13: 출하 DDL installer/hitpan_db_clean.sql 정의 실측(설치된 히트판 DB 접속 0 · #39)
--   employees.user_id   varchar(36) DEFAULT NULL
--   employees.tenant_id varchar(36) NOT NULL
--   users.email         varchar(100) NOT NULL  (uq_tenant_email = tenant_id email)
--   users.is_deleted    tinyint(1) NOT NULL DEFAULT 0
--   audit_trail         log_id tenant_id user_id action_type(30) entity_type entity_id before_value after_value reason created_at
--   employees 에 (tenant_id user_id) 인덱스는 지금 0개
--
-- 문장
--   (1) employees.login_id 새 칸 — 그 사원의 로그인 아이디 사본(진실원 = users.email) · 계정 없으면 NULL
--       사원 이메일 칸(employees.email)과 별개 — 그 값은 한 글자도 안 건드린다(사장님 10/5 ⑦-③)
--   (2) 기존 데이터 채우기 — 살아 있는 계정(is_deleted=0 · 표식 없음)이 연결된 사원만. 나머지는 NULL 그대로
--       사용 안 함(is_active=0 · is_deleted=0) 계정도 채운다 — 계정은 있다
--   (3) UNIQUE uq_employees_tenant_user (tenant_id user_id) — 한 계정이 두 사원에 붙는 쌍둥이를 DB 가 막는다
--       NULL 은 UNIQUE 에 안 걸린다(계정 없는 사원 여럿 가능)
--       🔴 P-9 — 이미 한 계정이 두 사원에 붙어 있는 회사가 있으면 UNIQUE 를 건너뛰고 경고만 남긴다
--          마이그 체인을 멈추지 않는다 · 코드 쪽 잠금(사원 행 FOR UPDATE + 조건부 UPDATE)이 남은 방어다
--   (4) 건너뛴 회사는 audit_trail 에 db137_user_dup 한 줄(회사당 한 번)
--
-- 지우는 칸 0 · 이름바꿈 0 · 새 표 0 (#17 해당 없음 · ALTER 만) · 사원 이메일 값 무접촉
-- 멱등: (1) IF NOT EXISTS · (2) login_id IS NULL 인 행만 · (3) 인덱스 존재 확인 후 · (4) NOT EXISTS
-- 되돌림: 옛 판(1.3.52)은 새 칸을 안 읽는다. 단 옛 판이 도는 동안 만든·폐기한 계정은 login_id 를 안 맞춘다
--   ⇒ 앱 기동 때 EmployeeLoginIdSync 가 같은 규칙으로 다시 맞춘다(P-6 · 멱등 · Program.cs 마이그 블록 바로 뒤)
-- 실행 방식: 러너가 파일 전체를 한 번에 보낸다 · AllowUserVariables=true 라 SET @ · PREPARE 가 돈다(DB-120 선례)
-- ⚠️ 이 주석에는 문장 끝 기호를 일부러 적지 않았다
-- =============================================================

ALTER TABLE `employees`
  ADD COLUMN IF NOT EXISTS `login_id` varchar(100) DEFAULT NULL
  COMMENT '사원계정 — users.email 사본 · 계정 없으면 NULL · DB-137'
  AFTER `user_id`;

UPDATE `employees` e
  JOIN `users` u
    ON u.`user_id`   = e.`user_id`
   AND u.`tenant_id` = e.`tenant_id`
   SET e.`login_id` = u.`email`
 WHERE e.`login_id` IS NULL
   AND u.`is_deleted` = 0
   AND u.`email` NOT LIKE 'resigned+%'
   AND u.`email` NOT LIKE 'retired+%';

SET @idx_exists := (
  SELECT COUNT(*) FROM information_schema.statistics
   WHERE table_schema = DATABASE()
     AND table_name   = 'employees'
     AND index_name   = 'uq_employees_tenant_user'
);

SET @dup_groups := (
  SELECT COUNT(*) FROM (
    SELECT `tenant_id`, `user_id`
      FROM `employees`
     WHERE `user_id` IS NOT NULL
     GROUP BY `tenant_id`, `user_id`
    HAVING COUNT(*) >= 2
  ) AS `dup`
);

SET @sql := IF(@idx_exists = 0 AND @dup_groups = 0,
  'ALTER TABLE `employees` ADD UNIQUE KEY `uq_employees_tenant_user` (`tenant_id`, `user_id`)',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

INSERT INTO `audit_trail`
  (`log_id`, `tenant_id`, `user_id`, `action_type`, `entity_type`, `entity_id`,
   `before_value`, `after_value`, `reason`, `created_at`)
SELECT UUID(), g.`tenant_id`, NULL, 'db137_user_dup', 'employees', NULL,
       CONCAT('same user_id on 2+ employees groups=', g.`cnt`), NULL,
       '한 계정이 사원 여러 명에 연결되어 있어 한 사원 한 계정 DB 잠금을 걸지 않았습니다. 사원관리에서 계정 연결 확인이 필요합니다.',
       NOW(6)
  FROM (SELECT d.`tenant_id`, COUNT(*) AS `cnt`
          FROM (SELECT `tenant_id`, `user_id`
                  FROM `employees`
                 WHERE `user_id` IS NOT NULL
                 GROUP BY `tenant_id`, `user_id`
                HAVING COUNT(*) >= 2) d
         GROUP BY d.`tenant_id`) g
 WHERE NOT EXISTS (
         SELECT 1
           FROM (SELECT `tenant_id`
                   FROM `audit_trail`
                  WHERE `action_type` = 'db137_user_dup') a
          WHERE a.`tenant_id` = g.`tenant_id`);
