-- =============================================================
-- DB-90: 양식 인쇄 옵션 — 공급자/공급받는자 2매 + 스타일
-- =============================================================
-- 근거: 사장님 지시 2026-08-11 (챕터3)
--       *"인쇄시 옵션이 필요해. 공급받는자만 (혹은 공급자만) / 공급받는자 / 공급자 인쇄"*
--       *"둘 다 찍을지, 혹은 거래처 명세서만 찍을지"*
--
-- 🔴 왜 필요한가 — 지금 구조로는 법을 못 지킨다
--   세금계산서는 **법정 2매** 서식이다. 부가가치세법 시행규칙이
--   적색(공급자보관용)·청색(공급받는자보관용) 2매 작성 → 1매 교부 → 5년 보관을 요구한다.
--   계산서(면세)도 동일하다("계산서 2매를 작성하여 그 중 1매를 공급받는 자에게 발급").
--
--   그런데 form_templates 에는 **"누구 몫인가" 를 담을 칸이 아예 없었다.**
--   paper_mode 는 plain/preprint(종이 종류)만 가른다. 축이 다르다.
--   ⇒ 세금계산서가 이미 양식 목록에 있는데도 법정 요건을 만족시킬 수 없는 상태였다.
--
-- 무엇을 넣나 (2가지)
--   ① print_copy_mode — 한 번 인쇄할 때 무엇을 찍나
--   ② style_key       — 디자인 4종 (사장님: "스타일4종은 마지막에" → 칸만 미리, 값은 나중)
--
-- ⚠️ paper_mode 와 헷갈리지 말 것 — 셋은 서로 다른 축이다:
--     paper_mode      = 어떤 **종이**에 찍나 (순백지 / 양식용지)
--     print_copy_mode = **누구 몫**을 찍나   (거래처만 / 둘 다)
--     style_key       = 어떤 **모양**으로 찍나 (기본 / 라인 …)
--   한 칸에 섞으면 조합이 폭발하고 화면이 어려워진다(헌법 #25 쉽게).
--
-- 멱등: 컬럼 존재 여부를 확인한 뒤에만 추가한다.
-- =============================================================

-- ── ① 인쇄 매수 ──────────────────────────────────────────────
--   both      = 공급자용 + 공급받는자용 2장  (세금계산서·계산서 법정 요건)
--   recipient = 공급받는자(거래처)용 1장만   ← 기본값
--   supplier  = 공급자(우리) 보관용 1장만
--
--   기본을 recipient 로 두는 이유: 대부분의 인쇄는 거래처에 줄 것 하나다.
--   법정 2매인 세금계산서·계산서는 아래 UPDATE 로 both 를 넣어 출발시킨다.
SET @col_exists := (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'form_templates'
    AND column_name = 'print_copy_mode'
);

SET @sql := IF(@col_exists = 0,
  'ALTER TABLE `form_templates`
     ADD COLUMN `print_copy_mode` varchar(10) NOT NULL DEFAULT ''recipient''
       COMMENT ''both=공급자+공급받는자 2장 / recipient=공급받는자만 / supplier=공급자만 (DB-90)''',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;


-- ── ② 스타일 ────────────────────────────────────────────────
--   사장님: *"스타일4종은 마지막에"* ⇒ 지금은 **칸만** 만들고 값은 basic 하나로 출발한다.
--   나중에 4종이 확정되면 이 컬럼 값만 바꾸면 되고, 스키마를 다시 손대지 않아도 된다.
--   (레거시 히트판 양식은 사장님이 별도로 주시기로 했다 — 그것을 보고 4종을 정한다.)
SET @col_exists := (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'form_templates'
    AND column_name = 'style_key'
);

SET @sql := IF(@col_exists = 0,
  'ALTER TABLE `form_templates`
     ADD COLUMN `style_key` varchar(20) NOT NULL DEFAULT ''basic''
       COMMENT ''디자인 스타일 (DB-90). 4종 확정 전까지 basic 단일''',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;


-- ── ③ 법정 2매 양식은 both 로 출발시킨다 ────────────────────
--   이미 만들어져 있는 세금계산서 템플릿이 recipient(기본값)로 남으면
--   **법정 요건을 못 지키는 채로 조용히 운영된다.** 여기서 바로잡는다.
--   ⚠️ 고객이 일부러 바꾼 값을 되돌리지 않도록, 기본값 그대로인 행만 손댄다.
UPDATE `form_templates`
   SET `print_copy_mode` = 'both'
 WHERE `form_type` IN ('tax_invoice', 'invoice_exempt', 'delivery')
   AND `print_copy_mode` = 'recipient';
