-- ════════════════════════════════════════════════════════════════════
-- DB-150 · form_templates 유일 키 교체 — 「한 (회사 · 양식 종류)에 쓰이는 기본 양식은 하나」만 막는다
--   W-G1-51 인쇄 작3 갈래 F-1 · 2026-11-19
-- ════════════════════════════════════════════════════════════════════
--
-- 근거
--   설계     docs/설계/erp/20261118_WG1-51_인쇄작3_반려봉합_설계.md §7-4 (대표 11/18 12:1x 「DB 규칙 고침」 · H1 3인 G1 · J3 · J5 ⑶)
--   작업지시 docs/운영기록/20261118작_WG1-51_인쇄작3_반려봉합_작업지시서.md §2 고침 4차 · 5차 F 줄
--   착수     docs/설계/erp/20261119_WG1-51_갈래AF_착수준비표.md §1 F-1 줄 · P-3 정적 확인 §2 C1 · C2
--
-- 🔴 무엇이 문제였나
--   옛 키 `uk_form_templates_default (tenant_id, form_type, is_default, is_active)` 는
--   「활성 기본 ≤1」 뿐 아니라 「활성 비기본 ≤1 · 끈 행도 기본값마다 ≤1」 까지 막았다.
--   ⇒ 같은 종에 양식을 여럿(스타일 여럿 · Y 템플릿 + 원래 양식지) 두면 1062 로 막히고,
--     두 번째 양식 지우기 · 비기본이 있을 때 다른 양식을 기본으로 바꾸기도 막힌다.
--
-- 🔴 새 규칙
--   계산 칸 `default_key` = 활성 기본일 때만 1, 아니면 NULL (VIRTUAL — 값을 쓰지 않는다).
--   UNIQUE `uk_form_templates_one_default (tenant_id, form_type, default_key)` —
--   UNIQUE 는 NULL 을 겹침으로 보지 않으므로 비기본 · 끈 행은 몇 개든 되고, 활성 기본만 하나로 묶인다.
--   선례 = DB-120 `tenant_devices.main_pc_key` (VIRTUAL 계산 칸 + UNIQUE · 출하 DDL 같은 모양).
--   🔴 VIRTUAL 인 까닭 — STORED 계산 칸이 FK 칸을 참조해 출하 DDL 적재가 1901 로 실패한 선례가 있다.
--     form_templates 는 FK 0 이지만 같은 길을 밟지 않는다(설계 §7-4 ⑷).
--   🔴 이름을 새로 쓰는 까닭 — 같은 이름에 다른 칸이면 이름만 보는 G-W13-2 가 못 가른다(HQA 요청 ①).
--
-- 🔴 순서 (규칙 없는 순간 0)
--   ① 계산 칸이 없으면 맨 끝에 더한다.
--   ② 새 키가 없고 「활성 기본 2행 이상인 (회사 · 종)」 = 0 일 때만 새 키를 건다.
--   ③ 새 키가 있고 **그리고** 옛 키가 있을 때만 옛 키를 지운다.
--      ② 를 건너뛰면 옛 키가 남는다 = 지금 동작 그대로(옮기기 단계도 돌지 않는다).
--      새 설치 DB(이미 새 키 · 옛 키 없음) 위에서 다시 돌아도 없는 키를 지우지 않는다(1091 길 0).
--   ④ ② 를 겹침 때문에 건너뛰었으면 그 회사 DB 안 `audit_trail` 에 회사마다 1줄 남긴다(멱등).
--   🔴 form_templates 행 쓰기 0 — 값을 바꾸는 줄이 이 파일에 없다(audit_trail 기록만 예외).
--
-- 🔴 #13 DESCRIBE (정적 — 이 PC DB 접속 0)
--   form_templates  = installer/hitpan_db_clean.sql `form_templates` 표 정의(tenant_id char(36) NOT NULL ·
--                     form_type varchar(30) NOT NULL · is_default tinyint(1) NOT NULL DEFAULT 0 ·
--                     is_active tinyint(1) NOT NULL DEFAULT 1 · FK 0) · DB-74_form_templates.sql:31 옛 키
--   audit_trail     = installer/hitpan_db_clean.sql `audit_trail` 표 정의(tenant_id varchar(36) NOT NULL ·
--                     user_id NULL 허용 · action_type varchar(30) · entity_type varchar(50) NOT NULL ·
--                     reason varchar(500)) · 같은 꼴 선례 DB-120 ④
--   #17 새 표 0(해당 없음).
--
-- 멱등: 두 번 돌려도 결과가 같다(G-W13-4 · G-W13-5). 실행 방식: 러너가 이 파일을 한 번에 보낸다
--   (DB-120 머리 주석과 같음 · `SET @…` / `PREPARE` 는 AllowUserVariables 로 동작).
-- ════════════════════════════════════════════════════════════════════


-- ─── ① 계산 칸 default_key (맨 끝 · 출하 DDL 도 맨 끝) ─────────────
ALTER TABLE `form_templates`
  ADD COLUMN IF NOT EXISTS `default_key` tinyint(4)
    GENERATED ALWAYS AS (if(`is_default` = 1 and `is_active` = 1,1,NULL)) VIRTUAL
    COMMENT '활성 기본 하나 보장용 (DB-150). 활성 기본일 때만 1, 아니면 NULL — 값을 직접 쓰지 않는다';


-- ─── ② 새 유일 키 — 없고 · 겹침 0 일 때만 ──────────────────────────
--   겹침 = 한 (회사 · 종)에 활성 기본이 2행 이상. 옛 키가 걸린 DB 에선 생길 수 없다
--   (옛 키 (t, f, 1, 1) 유일과 모순) → 옛 키가 없는 이상 DB 에서만 건너뛴다.
SET @db150_new_key := (
  SELECT COUNT(*) FROM information_schema.statistics
  WHERE table_schema = DATABASE()
    AND table_name = 'form_templates'
    AND index_name = 'uk_form_templates_one_default'
);

SET @db150_dup_groups := (
  SELECT COUNT(*) FROM (
    SELECT `tenant_id`, `form_type`
      FROM `form_templates`
     WHERE `is_default` = 1 AND `is_active` = 1
     GROUP BY `tenant_id`, `form_type`
    HAVING COUNT(*) >= 2
  ) AS `dup`
);

SET @sql := IF(@db150_new_key = 0 AND @db150_dup_groups = 0,
  'ALTER TABLE `form_templates` ADD UNIQUE KEY `uk_form_templates_one_default` (`tenant_id`,`form_type`,`default_key`)',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;


-- ─── ③ 옛 키 지우기 — 새 키 있음 **그리고** 옛 키 있음 일 때만 ─────
SET @db150_new_key := (
  SELECT COUNT(*) FROM information_schema.statistics
  WHERE table_schema = DATABASE()
    AND table_name = 'form_templates'
    AND index_name = 'uk_form_templates_one_default'
);

SET @db150_old_key := (
  SELECT COUNT(*) FROM information_schema.statistics
  WHERE table_schema = DATABASE()
    AND table_name = 'form_templates'
    AND index_name = 'uk_form_templates_default'
);

SET @sql := IF(@db150_new_key > 0 AND @db150_old_key > 0,
  'ALTER TABLE `form_templates` DROP INDEX `uk_form_templates_default`',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;


-- ─── ④ ② 를 겹침 때문에 건너뛴 회사를 그 고객 DB 안에 남긴다 (회사마다 1줄 · 멱등) ──
--   겹침 0 이면 파생표 g 가 비어 저절로 0줄 — 정상 고객 DB 에는 아무것도 남지 않는다.
--   같은 회사에 `db150_default_dup` 줄이 이미 있으면 넣지 않는다 ⇒ 두 번 돌려도 1줄(측정 D-10).
--   자기 표를 읽으며 자기 표에 넣으므로 읽는 쪽을 파생표로 감싼다(DB-120 ④ 와 같은 까닭).
INSERT INTO `audit_trail`
  (`log_id`, `tenant_id`, `user_id`, `action_type`, `entity_type`, `entity_id`,
   `before_value`, `after_value`, `reason`, `created_at`)
SELECT UUID(), g.`tenant_id`, NULL, 'db150_default_dup', 'form_templates', NULL,
       CONCAT('active default groups over 1=', g.`cnt`), NULL,
       '한 양식 종류에 기본 양식이 여럿이라 새 양식 규칙을 걸지 않았습니다. 양식정보설정에서 기본 양식을 하나로 정리해야 합니다.',
       NOW(6)
  FROM (SELECT d.`tenant_id`, COUNT(*) AS `cnt`
          FROM (SELECT `tenant_id`, `form_type`
                  FROM `form_templates`
                 WHERE `is_default` = 1 AND `is_active` = 1
                 GROUP BY `tenant_id`, `form_type`
                HAVING COUNT(*) >= 2) d
         GROUP BY d.`tenant_id`) g
 WHERE NOT EXISTS (
         SELECT 1
           FROM (SELECT `tenant_id`
                   FROM `audit_trail`
                  WHERE `action_type` = 'db150_default_dup') a
          WHERE a.`tenant_id` = g.`tenant_id`);
