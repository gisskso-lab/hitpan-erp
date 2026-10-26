-- =============================================================
-- DB-148: 양식지 그림을 종이에 찍는 새 축 + 로고 워터마크
-- =============================================================
-- 근거: 20261026작1 작업지시서 §A-1 · 설계문서 §4-1·§4-2·§4-3
--       사장님 오더 2026-10-25:
--       *"양식지 인쇄 / 순백지 인쇄 — 고객사의 옵션이 필요함"*
--       *"모든 양식지에 고객사 로고 워터마크 넣기 옵션 추가. 위치 = 정중앙 크게.
--         문서의 내용을 해치지 않을정도의 명도채도."*
--       결-I 결재: 공통 + 양식마다 — 단 고객 선택 옵션
--
-- 🔴 왜 paper_mode 로는 안 되나 — 축이 다르고 곱해진다 ([1-V] 지적)
--   paper_mode        = 어떤 종이에 찍나 (plain 순백지 / preprint 양식용지 구입)
--   print_background  = 우리가 가진 양식지 그림을 그 종이에 **찍나**
--
--   양식용지를 **사다 쓰는** 고객(preprint)은 종이에 이미 양식이 인쇄돼 있으니
--   그림을 **또 찍으면 이중 인쇄**가 된다 ⇒ 찍지 않는다.
--   양식지를 **그림으로 받아** 순백지에 뽑는 고객(plain)은 **찍어야** 한다.
--   같은 칸으로는 이 둘을 가를 수 없다. 그래서 새 칸이다.
--
-- 🔴 기본값이 전부 끔(0)·NULL 인 이유 — 기존 고객의 종이가 한 장도 안 바뀐다
--   오늘까지 실인쇄에는 양식지 그림이 **설계상 안 깔렸다**
--   (PdfRenderService.ResolveFormBackground — 양식 편집기 미리보기만 깔았다).
--   이 마이그로 기본값이 1 이 되면 **모든 고객의 종이가 하루아침에 바뀐다.**
--   켜고 싶은 고객이 양식정보설정에서 한 번 켠다. 반자동 원칙이다.
--
-- 🔴 수치 칸이 NULL 허용인 이유 — NULL = 「공통값을 따른다」 (결-I)
--   값이 들어 있으면 **그 양식만** 공통값을 덮는다.
--   0.00 과 NULL 은 다른 뜻이다: 0.00 = 「완전히 투명하게 해 달라」,
--   NULL = 「나는 정하지 않았다, 공통을 따르겠다」.
--   그래서 NOT NULL DEFAULT 로 쓰면 안 된다 — 두 뜻이 한 칸에서 섞인다.
--
-- 🔴 DEFAULT 수치는 **제안값**이다 (설계문서 §4-3)
--   명도 0.12 · 채도 0.15 · 크기 60% — 2026-10-26 실측에서 나온 값이다
--   (PDF→PNG 픽셀 판독: 0.12 에서 중앙픽셀 R=255 G=223 B=223).
--   개발팀이 정하는 값이 아니다. 고객이 화면에서 바꾼다.
--
-- 멱등: 컬럼·테이블 존재 여부를 확인한 뒤에만 더한다 (DB-147 과 같은 꼴).
-- 🔴 출하 DDL(installer/hitpan_db_clean.sql) 에도 같은 칸을 넣었다 (#36)
--    — 10/14 결-F 재발 방지. 마이그만 쓰면 신규 고객에게 영영 안 생긴다.
-- =============================================================

-- ─── ① form_templates.print_background ───────────────────────
SET @col_exists := (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'form_templates'
    AND column_name = 'print_background'
);

SET @sql := IF(@col_exists = 0,
  'ALTER TABLE `form_templates`
     ADD COLUMN `print_background` tinyint(1) NOT NULL DEFAULT 0
       COMMENT ''1=양식지 그림을 실제 종이에도 찍는다 / 0=안 찍는다(기본, DB-148). paper_mode 와 다른 축 — 곱해진다''',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ─── ② form_templates.show_watermark ─────────────────────────
SET @col_exists := (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'form_templates'
    AND column_name = 'show_watermark'
);

SET @sql := IF(@col_exists = 0,
  'ALTER TABLE `form_templates`
     ADD COLUMN `show_watermark` tinyint(1) NOT NULL DEFAULT 0
       COMMENT ''1=회사 로고를 종이 정중앙에 연하게 깐다 / 0=안 깐다(기본, DB-148). 재료는 local_company.logo_url''',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ─── ③ form_templates.watermark_opacity ──────────────────────
SET @col_exists := (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'form_templates'
    AND column_name = 'watermark_opacity'
);

SET @sql := IF(@col_exists = 0,
  'ALTER TABLE `form_templates`
     ADD COLUMN `watermark_opacity` decimal(3,2) DEFAULT NULL
       COMMENT ''워터마크 명도 0.00~1.00. NULL=공통값(print_common_settings)을 따른다 (DB-148)''',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ─── ④ form_templates.watermark_saturate ─────────────────────
SET @col_exists := (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'form_templates'
    AND column_name = 'watermark_saturate'
);

SET @sql := IF(@col_exists = 0,
  'ALTER TABLE `form_templates`
     ADD COLUMN `watermark_saturate` decimal(3,2) DEFAULT NULL
       COMMENT ''워터마크 채도 0.00~1.00 (0=흑백). NULL=공통값을 따른다 (DB-148)''',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ─── ⑤ form_templates.watermark_scale_pct ────────────────────
SET @col_exists := (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'form_templates'
    AND column_name = 'watermark_scale_pct'
);

SET @sql := IF(@col_exists = 0,
  'ALTER TABLE `form_templates`
     ADD COLUMN `watermark_scale_pct` smallint(5) unsigned DEFAULT NULL
       COMMENT ''워터마크 크기 — 용지 폭 대비 %. NULL=공통값을 따른다 (DB-148)''',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ─── ⑥ print_common_settings — 테넌트 공통 한 벌 ──────────────
-- 🔴 왜 local_company 에 칸을 더하지 않았나
--   local_company 는 **회사가 누구인가**(상호·사업자번호·주소)를 담는 자리다.
--   인쇄 취향은 성격이 다르고, 앞으로 칸이 더 늘 자리다. 섞으면 나중에 못 가른다.
--   #17 — 신규 테이블은 ENGINE=InnoDB 를 명시한다.
CREATE TABLE IF NOT EXISTS `print_common_settings` (
  `tenant_id` char(36) NOT NULL COMMENT 'tenants.tenant_id — JWT 클레임에서만 채운다 (#2)',
  `watermark_enabled` tinyint(1) NOT NULL DEFAULT 0
    COMMENT '1=기본으로 워터마크를 깐다. 양식마다 끌 수 있다',
  `watermark_opacity` decimal(3,2) NOT NULL DEFAULT 0.12
    COMMENT '명도 제안값 0.12 — 2026-10-26 실측(중앙픽셀 R=255 G=223 B=223). 고객이 바꾼다',
  `watermark_saturate` decimal(3,2) NOT NULL DEFAULT 0.15
    COMMENT '채도 제안값 0.15 — 색이 옅은 회색에 가까워져 본문과 안 다툰다. 고객이 바꾼다',
  `watermark_scale_pct` smallint(5) unsigned NOT NULL DEFAULT 60
    COMMENT '크기 제안값 60% (용지 폭 대비) — 「정중앙 크게」. 고객이 바꾼다',
  `print_background_default` tinyint(1) NOT NULL DEFAULT 0
    COMMENT '새 양식을 만들 때 print_background 의 초기값. 0=안 찍는다',
  `created_at` datetime(3) NOT NULL DEFAULT current_timestamp(3),
  `updated_at` datetime(3) NOT NULL DEFAULT current_timestamp(3) ON UPDATE current_timestamp(3),
  PRIMARY KEY (`tenant_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='인쇄 공통 설정 (DB-148) — 양식마다의 NULL 이 이 값을 따른다';
