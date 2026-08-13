-- ═══════════════════════════════════════════════════════════════
-- DB-93 : 직급 마스터 시드를 실제 테넌트에 깐다 (그룹웨어 단계4 토대)
-- 작성: 2026-08-13 · 사장님 결재 = 그룹웨어 단계4~9 일괄 전결
-- ═══════════════════════════════════════════════════════════════
--
-- 🔴 왜 필요한가 — DB-22 시드가 고객에게 안 갔다
--
--   DB-22 는 기본 직급 6개를 넣었는데 `tenant_id = 'tenant-001'` 하드코딩이었다.
--   실제 테넌트는 그 값이 아니다(실측: demo-tenant-0001). 그리고 DB-22 주석은
--   "신규 테넌트는 가입 프로비저닝 코드에서 동일 시드 INSERT 권장" 이라 적었는데
--   CompanyBootstrapProvisioner 에 그 코드가 없다(실측: positions 언급 0건).
--
--   ⇒ 결과: positions 0행. 사원 등록 화면의 직급이 자유 텍스트였던 이유가 이것이고,
--     12명 중 8명이 직급 없음(NULL 2 · 공백 5 · "0" 1)이 된 이유도 이것이다.
--     신규 고객사도 똑같이 직급 0개로 시작한다.
--
-- 🔴 무엇을 하나
--   employees 가 실재하는 모든 테넌트에 기본 직급 6개를 깐다.
--   이미 직급을 하나라도 만든 테넌트는 건드리지 않는다 — 관리자가 짜둔 체계를
--   우리가 덮어쓰면 안 된다(헌법 #1 덮어쓰기 금지 · #11 권한은 어드민이 직접 설정).
--
-- ⚠️ 이 값들은 '출발점' 이지 정답이 아니다. 회사마다 직급 체계가 다르므로
--    관리자가 설정 → 직급 관리에서 고치고 지운다. 우리가 업종별 템플릿을 주는 게 아니다.
--
-- 멱등: INSERT ... SELECT + NOT EXISTS. 두 번 돌려도 늘지 않는다.
--       uk_positions_tenant_code(tenant_id, code) 유니크도 이중으로 막는다.
-- ═══════════════════════════════════════════════════════════════

INSERT INTO positions (position_id, tenant_id, code, name, sort_order, is_active)
SELECT UUID(), t.tenant_id, s.code, s.name, s.sort_order, 1
FROM (
    -- 사원이 실재하는 테넌트 = 실제로 쓰이는 회사
    SELECT DISTINCT tenant_id FROM employees
) AS t
CROSS JOIN (
              SELECT 'CEO'       AS code, '대표이사' AS name, 100 AS sort_order
    UNION ALL SELECT 'DIRECTOR',        '부장',      80
    UNION ALL SELECT 'DEPUTY',          '차장',      70
    UNION ALL SELECT 'MANAGER',         '과장',      60
    UNION ALL SELECT 'ASSISTANT_MANAGER', '대리',    50
    UNION ALL SELECT 'STAFF',           '사원',      10
) AS s
WHERE NOT EXISTS (
    -- 🔴 직급을 하나라도 가진 테넌트는 건너뛴다. 관리자가 이미 짠 체계를 건드리지 않는다.
    SELECT 1 FROM positions p WHERE p.tenant_id = t.tenant_id
);

-- ═══════════════════════════════════════════════════════════════
-- 2) 직급칸의 쓰레기값 정리 — 공백·"0" 을 비운다
-- ═══════════════════════════════════════════════════════════════
--
-- 자유 텍스트였던 탓에 실제로 이런 값들이 들어 있다(실측 12명):
--   NULL 2명 / 공백 5명 / "0" 1명 / "과장" 1명 / "사원" 3명
--
-- 위 시드가 '과장'·'사원' 을 그대로 담고 있어 4명은 마스터와 맞는다(실측 MATCH).
-- 남은 문제는 공백과 "0" 이다 — 직급이 아니라 입력 사고의 흔적이다.
-- 드롭다운으로 바뀌면 이 값들이 선택지에 그대로 뜨는데("0" 이라는 직급),
-- 화면에서 보기 흉하고 관리자가 지울 수도 없다(마스터에 없으니까).
--
-- ⚠️ NULL 로 비우기만 한다. 임의로 '사원' 같은 값을 채우지 않는다 —
--    그 사람이 정말 사원인지 우리는 모른다(반자동 원칙: 사람이 확정한다).
--    관리자가 사원관리에서 보고 고른다.
UPDATE employees
SET position = NULL,
    updated_at = NOW(6)
WHERE position IS NOT NULL
  AND (TRIM(position) = '' OR TRIM(position) = '0');
