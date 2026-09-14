-- =============================================================
-- DB-120: 메인PC 표식은 회사당 한 줄뿐이다
--         — 이미 생긴 2줄 정리(①) + DB 방어선 재투입(②③)
-- =============================================================
-- 근거
--   작업지시서 docs/운영기록/20260913작2_메인PC표식_2줄_봉합_작업지시서.md §3-1 ③④ · §5-3
--   설계문서   docs/설계/erp/20260913_설계_메인PC표식_재기동_되올림_차단.md §3 · §4
--   선행검증   docs/검증/선행/20260911_선행검증서_슬롯관리전수_A_서버DB.md §8-1 Q1·Q1b·Q6·Q7·Q8
--   게이트     src/HitPan.Tests/Integrity/MainPcRestartMarkGateTests.cs (G-M3·G-M4·G-M5·G-M7)
--
-- 🔴 무엇이 났나 (사장님 실측 2026-09-13 *"수정안됨. 반려"*)
--   실물 `hitpan_erp_t004` 에 `is_main_pc=1` 이 **2줄**이었다.
--     ⓐ `HFPv2-a341d087`  승인 · 사장님이 실제로 쓰는 줄 (last_seen 9/13 15:18)
--     ⓑ `MAINPC-9c1163c`  폐기 · 사유 「메인PC 표식을 실제 사용 화면으로 옮김 (20260818작4)」
--   8/18 에 표식을 ⓐ 로 옮기며 ⓑ 를 내려뒀는데, 9/11 업데이트(API 재기동)가 ⓑ 를 **되올렸다.**
--   ⇒ 진범은 코드(`MainPcRegistrationService` 되올림 UPDATE)이고 **그쪽은 같은 게시에서 막았다**(축1).
--     이 파일은 **이미 생긴 2줄**을 정리하고(①), 앞으로 2줄이 아예 불가능하게 만든다(②③).
--
-- 🔴 이 파일이 무엇을 흡수하는가 (PM 결재 조건 C-4 · 쟁점 K5)
--   · 하브루타 회의록 docs/운영기록/20260910_하브루타_메인PC_식별등록_회의록.md §7-3 **2번
--     「DB-120 조건부 재정합」** — 그 번호를 이 파일이 쓴다. ⚠️ **내용은 그 초안과 다르다**(아래 ③ 주석).
--   · **사장님 결재 5** 「폐기된 옛 메인 줄 자동 부활 안 함」 — 이 파일은 `status` 를 **한 글자도 쓰지 않는다.**
--   ⚠️ **DB-121 은 「탑승비밀」로 이름이 이미 나가 있다 — 침범하지 않는다.**
--
-- 🔴 왜 새 번호인가 — DB-89 를 고치지 않는다
--   DB-89 는 같은 방어선을 이미 담고 있으나 **출하 DDL 설치 고객에게는 영원히 안 돈다**
--   (`schema_migrations` 에 `app_version='clean-ddl'`·success=1 로 시드돼 러너가 skip · 실측 A §8-1 Q6).
--   이미 success=1 로 기록된 고객이 있어 그 파일을 고치면 **아무 일도 일어나지 않는다.**
--   ⇒ 새 번호로 다시 싣는다. 멱등 패턴(존재 확인 후 생성)은 DB-89 를 그대로 따른다.
--
-- 🔴 DB-89 에서 **일부러 빼는 것 2개** (그대로 옮기면 결재 5 와 충돌한다 · 설계 §4-2)
--   · 꼬리 UPDATE 「이미 revoked 인 메인PC 를 approved 로 되살리기」(DB-89:96-99)
--       → 폐기 줄을 **부활**시켜 슬롯 계수(`status='approved'`)에 다시 들어간다 ⇒ **고객 요금이 움직인다**
--         (화면 PC 4/5 → 5/5). **싣지 않는다.** 게이트 G-M7 이 이 숫자를 대조군으로 잰다.
--   · 트리거 2개 「is_main_pc=1 ∧ revoked 면 status='approved'」(DB-89:73-90)
--       → 같은 부활을 **앞으로 계속** 한다. 그리고 8/11 「폐기된 메인PC 로그인 막힘」 구제는
--         트리거가 아니라 **코드에 실재한다**(`TenantDeviceService.cs:366` `&& !isMainPc` · :408 rejected 자가회복).
--       → **싣지 않는다** (PM 결재 K1 = UNIQUE 만 · 트리거 0).
--
-- 멱등: 두 번 돌려도 결과가 같다. ①은 표식이 1줄이면 대상이 0건이고, ②③은 존재 확인 후에만 만든다.
-- 실행 방식: 러너가 Dapper `ExecuteAsync` **한 번**으로 이 파일 전체를 보낸다(`MigrationRunner.cs:135-137`).
--   연결에 `AllowUserVariables=true` 가 있어 `SET @…`/`PREPARE` 가 동작한다(`MigrationDbConnectionFactory.cs:58`).
-- =============================================================


-- ── ① 이미 표식이 2줄 이상인 회사를 정리한다 ─────────────────
--
--   규칙 (PM 결재 K3 · 사람 판단 0):
--     한 회사에 `is_main_pc=1` 이 **2줄 이상**이고 그중 `status='approved'` 인 줄이 **1개 이상**이면,
--     `approved` 인 줄 중 **`last_seen_at` 이 가장 최신인 1줄**만 표식을 남기고 나머지는 **표식만 내린다.**
--     `approved` 인 표식 줄이 **0개면 아무것도 건드리지 않는다.**
--
--   🔴 왜 `approved` 로 **먼저** 거르나 — `last_seen_at` 을 먼저 보면 사장님이 갇힌다.
--     `last_seen_at` 은 **되올림 UPDATE 가 `NOW()` 로 덮는 값**이다. 실물 ⓑ(폐기)의 `9/11 23:05` 이
--     바로 그 흔적이고(그 줄이 접속한 것이 아니다), 그래서 **폐기 줄이 「최신」이 될 수 있다.**
--     폐기 줄을 진짜로 고르면 8/11·8/16·8/18 에 3회 재발한 그 사고(대표가 자기 화면에서 갇힘)가 또 난다.
--     ⇒ 게이트 V2 가 「폐기 줄이 더 최신」 모양으로 이 순서를 동작으로 잰다(PM 조건 C-2).
--
--   🔴 왜 `is_main_pc` 한 컬럼만 쓰나 (헌법 #1 · 감사기록 보존)
--     `status`·`revoked_at`·`revoked_reason` **무변경** — 사유 원문 「…(20260818작4)」 이 남아야
--     다음 사람이 경로를 읽는다. 줄 `DELETE` **0건**. 부활 **0건**(결재 5).
--     ⇒ 요금 무이동이 구조적으로 보장된다: 계수는 `status='approved'` 로 세므로(계수 SQL 에
--       `is_main_pc` 절이 없다 — `TenantDeviceService.CountUsedSlotsAsync`) 폐기 줄은 전에도 후에도 안 센다.
--
--   🔴 「최신 1줄」 선택은 **결정적**이다 (사람·랜덤 판정 금지 · 게이트 V3)
--     ① 접속기록이 있는 줄 우선(`last_seen_at IS NULL` 을 뒤로) → ② `last_seen_at` 최신 →
--     ③ 동값이면 `registered_at` 최신 → ④ 그래도 같으면 `device_id` 사전순.
--     `device_id` 가 PK 라 **마지막 단계에서 반드시 하나로 갈린다.**
--
--   ⚠️ MariaDB 는 UPDATE 대상 표를 직접 서브쿼리로 못 읽는다(오류 1093).
--     ⇒ **파생표로 두 겹 감싼다** — 안쪽이 먼저 실체화되므로 같은 표를 안전하게 읽는다.
UPDATE `tenant_devices` AS d
  JOIN (
    SELECT * FROM (
      SELECT g.`tenant_id`,
             (SELECT k.`device_id`
                FROM `tenant_devices` k
               WHERE k.`tenant_id`  = g.`tenant_id`
                 AND k.`is_main_pc` = 1
                 AND k.`status`     = 'approved'
               ORDER BY (k.`last_seen_at` IS NULL) ASC,
                        k.`last_seen_at`   DESC,
                        k.`registered_at`  DESC,
                        k.`device_id`      ASC
               LIMIT 1) AS `keep_id`
        FROM (SELECT `tenant_id`
                FROM `tenant_devices`
               WHERE `is_main_pc` = 1
               GROUP BY `tenant_id`
              HAVING COUNT(*) >= 2
                 AND SUM(`status` = 'approved') >= 1) g
    ) AS m
  ) AS t
    ON t.`tenant_id` = d.`tenant_id`
   SET d.`is_main_pc` = 0
 WHERE d.`is_main_pc` = 1
   AND t.`keep_id` IS NOT NULL
   AND d.`device_id` <> t.`keep_id`;

-- ##DB120-STAGE-1-END##
-- 🔴 위 표식은 게이트 G-M4 가 「①단을 건너뛰고 ②③만 실행」하기 위해 자르는 자리다.
--   2줄 상태에서 ③단 UNIQUE 를 먼저 걸면 **중복 키로 마이그가 실패**하고 그 고객의 업데이트가 멈춘다.
--   ⇒ 순서(정리 → UNIQUE)가 지켜지는지를 **동작으로** 증명한다. 이 표식을 지우면 G-M4 가 빨간불이 된다.


-- ── ② 메인PC 1대 보장용 생성컬럼 (DB-89 패턴 그대로) ─────────
--   main_pc_key = 메인PC 일 때만 tenant_id, 아니면 NULL.
--   MySQL/MariaDB 의 UNIQUE 는 NULL 을 중복으로 보지 않으므로
--   **일반 기기는 몇 대든 자유롭고, 메인PC 만 회사당 1행**으로 잠긴다.
SET @col_exists := (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'tenant_devices'
    AND column_name = 'main_pc_key'
);

SET @sql := IF(@col_exists = 0,
  'ALTER TABLE `tenant_devices`
     ADD COLUMN `main_pc_key` char(36)
       GENERATED ALWAYS AS (IF(`is_main_pc` = 1, `tenant_id`, NULL)) VIRTUAL
       COMMENT ''메인PC 1대 보장용 (DB-120). 메인PC 일 때만 tenant_id, 아니면 NULL''',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;


-- ── ③ 회사당 메인PC 1대 (UNIQUE) ────────────────────────────
--   🔴 **왜 CHECK 가 아니라 UNIQUE 인가** — MariaDB 의 CHECK 는 다른 행을 못 본다.
--     「회사당 1대」는 행 간 규칙이라 CHECK 로 표현할 수 없다.
--
--   🔴 **조건부다 — 중복이 남아 있으면 걸지 않는다** (사장님 결재 2026-09-13 **(가) 채택**)
--     근거: CTO 결재문 `docs/운영기록/20260913결1_메인PC표식_V4_마이그정지_CTO결재.md` §1 · §3-3 · §6 **D-1**
--     두 조건을 **AND** 로 본다:
--       ① `@idx_exists = 0`  — 이미 있으면 만들지 않는다 (멱등 · DB-89 패턴 그대로 **유지**)
--       ② `@dup_tenants = 0` — ①단이 끝난 뒤에도 표식이 2줄 이상인 회사가 **하나도 없을 때만** 만든다
--
--   🔴 **왜 「DB 전체」로 판정하나 — 테넌트별이 아니다**
--     UNIQUE 는 **표 전체** 제약이라 한 회사만 더러워도 `ALTER` 가 **통째로** 거부된다
--     ⇒ 판정 단위도 표 전체여야 한다. 한 DB 안 두 번째 회사(`InstallerBootstrapController.cs:133` 별트랙)가
--     그 자리다 — PM 실측으로 실물은 단일 테넌트지만 **`local_company` 행이 2개 되는 것을 코드가 막지 않는다.**
--     「실물이 1개」와 「구조가 1개를 보장」은 다르다(결1 §7 PM 반증 2).
--
--   🔴 **무엇이 갈렸나 — 기록은 지우지 않고 상태만 바꾼다**
--     [CTO-2] 초안 ② = 「중복이 **0 일 때만** UNIQUE」 / 이 파일 **1차** = 「①로 중복을 없앤 뒤 **무조건** UNIQUE」.
--     갈리는 자리: `approved` 표식이 **0줄**인 회사(폐기 1줄 + 대기 1줄이 둘 다 표식 보유)는
--       ①단이 **무처리**(K3)라 중복이 **남는다** ⇒ 1차 모양은 여기서
--       `Duplicate entry … for key 'uq_tenant_main_pc'` 로 실패했고,
--       `MigrationRunner.cs:143-162` 은 실패 시 `return` 이라 **그 고객의 이후 마이그가 전부 중단**됐다.
--       `success=0` 으로 기록돼 다음 업데이트마다 같은 자리에서 또 실패한다(영구 정지).
--       ⇒ **게이트 V4 가 이것을 실측으로 잡았다.** 게이트를 무르지 않고 **구현을 고쳤다**(결1 §4).
--     ⇒ (가) 채택으로 **초안 ②③ 을 복원한다.** 대가: 그 회사는 **DB 방어선 없이** 남는다
--       ⇒ **④단이 그 사실을 그 고객 DB 안에 남긴다**(D-3). 코드 축1(되올림 차단)이 그동안의 방어선이다.
--     🔴 재정합은 **반드시 새 번호(`DB-122` 이후)** — 이 파일은 `success=1` 로 기록돼 **다시 돌지 않고**,
--       `DB-121` 은 「탑승비밀」로 이름이 나가 있어 **침범하지 않는다.**
--
--   ⚠️ UNIQUE 는 **거부**한다 — 조용히 값을 바꾸지 않고 드러낸다(트리거를 뺀 이유).
--     되올림이 이 제약에 부딪혀도 `RegisterAsync` 는 재시도 3회 뒤 로그만 남기고 끝나며
--     `BackgroundService` 라 **API 기동을 막지 않는다** ⇒ 고객 화면 500 없음.
--     단 그것은 **축1(되올림 조건)이 먼저 서 있을 때** 이야기다 — 축1 없이 이 파일만 넣는 것은 금지.
SET @idx_exists := (
  SELECT COUNT(*) FROM information_schema.statistics
  WHERE table_schema = DATABASE()
    AND table_name = 'tenant_devices'
    AND index_name = 'uq_tenant_main_pc'
);

-- 🔴 ①단이 끝난 뒤 **DB 전체**에 표식 2줄 이상인 회사가 몇 곳 남았나 (0 이어야 UNIQUE 를 건다)
--   `main_pc_key` = 메인PC 일 때만 `tenant_id` ⇒ 그 키의 중복은 「같은 회사에 `is_main_pc=1` 이 둘 이상」과 같다.
SET @dup_tenants := (
  SELECT COUNT(*) FROM (
    SELECT `tenant_id`
      FROM `tenant_devices`
     WHERE `is_main_pc` = 1
     GROUP BY `tenant_id`
    HAVING COUNT(*) >= 2
  ) AS `dup`
);

SET @sql := IF(@idx_exists = 0 AND @dup_tenants = 0,
  'ALTER TABLE `tenant_devices` ADD UNIQUE KEY `uq_tenant_main_pc` (`main_pc_key`)',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;


-- ── ④ UNIQUE 를 건너뛴 회사를 **그 고객 DB 안에** 남긴다 (1줄 · 멱등) ──
--   🔴 근거: CTO 결재문 §6 **D-3** (초안 ③). ③단을 건너뛴 회사는 **DB 방어선 없이** 남는다.
--     「언제 걸리나 · 누가 다시 보나」의 답이 **문서가 아니라 그 고객 DB 안에** 있어야 한다
--     — 재정합(`DB-122` 이후)이 이 줄을 보고 대상을 찾는다.
--   🔴 표·컬럼 실재 확인 (#13 DESCRIBE): `installer/hitpan_db_clean.sql:330` `audit_trail`
--     · `user_id` **nullable** ⇒ 사람 행위가 아닌 마이그도 1줄 쓸 수 있다. 표 신설 0 (#17 해당 없음).
--   멱등: 같은 회사에 `action_type='db120_mainpc_dup'` 줄이 **이미 있으면 넣지 않는다** ⇒ **2회 돌려도 1줄.**
--     중복이 0 이면 파생표 `g` 가 비어 **저절로 0줄**이다 ⇒ 정상 고객 DB 에는 아무것도 남지 않는다.
--   ⚠️ 자기 표를 읽으며 자기 표에 넣는다 ⇒ 읽는 쪽을 **파생표로 감싼다**(①단과 같은 이유).
--   🔴 이 단은 **기록만** 한다 — `tenant_devices` 무변경 · `status` 쓰기 0 · `DELETE` 0 (결재 5 · #1).
INSERT INTO `audit_trail`
  (`log_id`, `tenant_id`, `user_id`, `action_type`, `entity_type`, `entity_id`,
   `before_value`, `after_value`, `reason`, `created_at`)
SELECT UUID(), g.`tenant_id`, NULL, 'db120_mainpc_dup', 'tenant_devices', NULL,
       CONCAT('is_main_pc=1 rows=', g.`cnt`), NULL,
       '회사 서버로 표시된 기기가 여러 대라 자동으로 하나를 고르지 않았습니다. 등록 기기 관리에서 확인이 필요합니다.',
       NOW(6)
  FROM (SELECT `tenant_id`, COUNT(*) AS `cnt`
          FROM `tenant_devices`
         WHERE `is_main_pc` = 1
         GROUP BY `tenant_id`
        HAVING COUNT(*) >= 2) g
 WHERE NOT EXISTS (
         SELECT 1
           FROM (SELECT `tenant_id`
                   FROM `audit_trail`
                  WHERE `action_type` = 'db120_mainpc_dup') a
          WHERE a.`tenant_id` = g.`tenant_id`);
