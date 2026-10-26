-- ════════════════════════════════════════════════════════════════════
-- DB-149 · form_templates.show_watermark 를 **세 값**으로 넓힌다
--   20261026작1 [5] CTO 1차 결재 C-2 봉합 · 2026-10-26
-- ════════════════════════════════════════════════════════════════════
--
-- 🔴🔴 **왜 DB-148 안에서 못 고치고 새 번호가 필요한가**
--   마이그 적용기는 **파일 내용이 아니라 번호(`migration_id`)로 skip** 한다
--   (`src/HitPan.Application/Services/MigrationRunner.cs` — `success = 1` 인 식별자 집합을
--    읽어 `applied.Contains(mig.MigrationId)` 면 **파일을 읽지도 않는다**).
--   ⇒ `DB-148` 을 **아침 판**(`show_watermark tinyint(1) NOT NULL DEFAULT 0`)으로 이미 돌린 DB 는
--     그 번호가 적용됨으로 적혀 있어, 같은 파일을 고쳐 둬도 **영영 다시 안 돈다.**
--   🔴 PM 은 처음에 DB-148 안에 넓히기 블록(②-2)을 넣었다. **그 블록은 그 DB 에 도달하지 못한다.**
--     [5] CTO 결재가 이것을 적발했다(C-2). 번호를 하나 더 내는 것이 **유일한 길**이다.
--     (저장된 교훈: 「마이그 번호 겹치면 뒤 파일 skip」·「빈 DB 에서 된다 ≠ 고객 DB 에서 된다」)
--
-- 🔴 **고객 DB 에는 해롭지 않다** — `DB-148`·`DB-149` 는 아직 출하되지 않았다(1.3.66 에 없다).
--   고객은 둘을 **한 번에** 받고, 그때 칸은 처음부터 세 값으로 생긴다 ⇒ 이 파일은 `SELECT 1`.
--   도달하는 곳은 **아침 판을 이미 돌린 내부 시험 DB** 다.
--
-- 🔴 **값은 건드리지 않는다.** 이미 들어 있는 `0` 을 NULL 로 바꾸지 않는다 —
--   `0` 은 「이 양식만 끔」으로 읽히고, 그쪽이 **종이가 안 바뀌는 쪽**이다(#1 덮어쓰기 금지).
--   공통을 켠 뒤 그 양식만 안 깔리면, 고객이 그 양식에서 「공통 설정을 따릅니다」를 고르면 된다.
--
-- 🔴 멱등이다 — 이미 세 값이면 `SELECT 1` 로 지나간다. 두 번 돌려도 모양이 그대로다.
-- ════════════════════════════════════════════════════════════════════

-- ─── ① show_watermark 가 NOT NULL 이면 NULL 허용으로 넓힌다 ───────
SET @is_nullable := (
  SELECT IFNULL(MAX(is_nullable), 'YES') FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'form_templates'
    AND column_name = 'show_watermark'
);

SET @sql := IF(@is_nullable = 'NO',
  'ALTER TABLE `form_templates`
     MODIFY COLUMN `show_watermark` tinyint(1) DEFAULT NULL
       COMMENT ''1=이 양식만 켠다 / 0=이 양식만 끈다 / NULL=공통(print_common_settings)을 따른다(기본, DB-148·DB-149). 재료는 local_company.logo_url''',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
