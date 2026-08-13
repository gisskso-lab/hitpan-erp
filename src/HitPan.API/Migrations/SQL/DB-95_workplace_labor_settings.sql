-- ═══════════════════════════════════════════════════════════════
-- DB-95 : 사업장 노무 정보 (그룹웨어 단계4 토대)
-- 작성: 2026-08-13 · 사장님 결재 = 그룹웨어 단계4~9 일괄 전결
-- ═══════════════════════════════════════════════════════════════
--
-- 🔴 왜 필요한가 — 사장님이 짚은 그 자리다
--
--   사장님(2026-08-12): "사업장의 직원수, 규모, 법인,개인,면세사업장, 등 여러상황이 있어서
--                        자동화는 현실적으로 어려워. 반자동원칙"
--
--   연차·퇴직금·수당이 사업장 조건으로 갈린다. 특히 상시근로자 5인 미만이면
--   연차 부여 의무 자체가 없다(퇴직금은 있다). 그런데 그 숫자를 담는 칸이 없다.
--
--   실측 — local_company 에 있는 것 / 없는 것:
--     ✅ tax_type(과세/면세) · corp_no(법인등록번호) · biz_type(업태) · biz_item(업종)
--     🔴 상시근로자수 — 없음
--     🔴 법인/개인 구분 — 없음 (corp_no 는 '번호' 칸이지 구분 플래그가 아니다.
--                          개인사업자도 corp_no 가 빈 채로 존재할 수 있어 이걸로 판정하면 틀린다)
--
--   ⚠️ tax_type 은 칸만 있고 죽어 있다 — SettingsService 의 SELECT·UPDATE 어디에도 없고
--      회사정보 화면에도 입력칸이 없다. 값을 넣을 방법이 없으니 늘 기본값 'taxable' 이다.
--      (이번 차수에서 화면·서비스까지 살린다)
--
-- 🔴 반자동 원칙 — 상시근로자수를 자동 계산하지 않는다
--
--   법정 상시근로자수는 '연인원 ÷ 가동일수' 이고 알바·가족까지 세는 등 계산이 까다롭다.
--   employees 행을 세서 자동으로 채우면 그럴듯한데 틀린 숫자가 나오고,
--   그 숫자로 "연차 없음" 이 판정되면 법정 미달이 된다.
--   ⇒ NULL 로 두고, 화면이 계산 도우미(현재 재직자 수)를 '제안' 하고, 사람이 확정한다.
--
-- 멱등: information_schema 확인 후 동적 ALTER.
-- ═══════════════════════════════════════════════════════════════

-- ── 1) 상시근로자수 ──────────────────────────────────────────
SET @c1 := (SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = 'local_company'
              AND column_name = 'regular_employee_count');

SET @d1 := IF(@c1 = 0,
    'ALTER TABLE local_company
       ADD COLUMN regular_employee_count int DEFAULT NULL
       COMMENT ''상시근로자수. NULL=미정 — 자동계산 금지(연인원/가동일수, 사람이 확정)''',
    'SELECT ''skip: regular_employee_count'' AS s');
PREPARE s1 FROM @d1; EXECUTE s1; DEALLOCATE PREPARE s1;

-- ── 2) 법인/개인 구분 ────────────────────────────────────────
--    'corporate' 법인 / 'individual' 개인. NULL=미정.
--    corp_no(법인등록번호) 유무로 추정하지 않는다 — 개인사업자도 비어 있고, 법인도 안 적을 수 있다.
SET @c2 := (SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = 'local_company'
              AND column_name = 'business_entity_type');

SET @d2 := IF(@c2 = 0,
    'ALTER TABLE local_company
       ADD COLUMN business_entity_type varchar(20) DEFAULT NULL
       COMMENT ''법인/개인 구분: corporate|individual. NULL=미정''',
    'SELECT ''skip: business_entity_type'' AS s');
PREPARE s2 FROM @d2; EXECUTE s2; DEALLOCATE PREPARE s2;

-- ── 3) 상시근로자수 기준일 ───────────────────────────────────
--    🔴 이 숫자는 시점이 있어야 뜻이 산다. "지금 7명" 이 아니라 "언제 기준 7명" 이다.
--    설계도 §0 지침: "값마다 적용시작일을 둔다. 과거분은 옛 값으로 계산해야 한다."
--    (지금은 최신값 하나만 두되, 언제 정한 것인지는 남긴다)
SET @c3 := (SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = 'local_company'
              AND column_name = 'employee_count_asof');

SET @d3 := IF(@c3 = 0,
    'ALTER TABLE local_company
       ADD COLUMN employee_count_asof date DEFAULT NULL
       COMMENT ''상시근로자수 기준일. 이 숫자가 언제 기준인지''',
    'SELECT ''skip: employee_count_asof'' AS s');
PREPARE s3 FROM @d3; EXECUTE s3; DEALLOCATE PREPARE s3;
