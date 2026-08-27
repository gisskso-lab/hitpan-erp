-- DB-112 — 계정과목 시드가 고객 PC 에서 0건이던 것 봉합 (20260827작5)
--
-- 🔴 사장님 실측 반려 (2026-08-27, 1.3.26)
--   수금 → 409 · 지급 → 409 (수금처리 실패 / 지급처리 실패)
--
-- 🔴 409 는 FK 1452 다
--   GlobalExceptionMiddleware.cs:41 이 MySql 1451/1452 를 409 로 바꾼다.
--   1452 = 자식 INSERT 실패(부모 없음) = **journal_lines 가 참조할 accounts 행이 없다.**
--   즉 DB-111 이 심었어야 할 계정과목이 **고객 PC 에 안 들어갔다.**
--
-- 🔴🔴 왜 안 들어갔나 — DB-111(과 DB-32)이 `tenants` 를 조인한다
--     INSERT ... SELECT ... FROM tenants tn WHERE tn.status IN ('active','trial','suspended')
--
--   그런데 **ERP 로컬 DB 의 `tenants` 는 빈 표다.**
--   전 소스에 `INSERT INTO tenants` 가 **0건**이다(실측). 로컬 ERP 는 테넌트 상태를
--   **`local_subscription`** 에 쓴다(CompanyBootstrapProvisioner.cs:124 — 백오피스 수신 캐시).
--   `tenants` 는 백오피스(NCP) 쪽 개념이라 고객 PC 에선 채워지지 않는다.
--
--   ⇒ 조인 결과 **0행** ⇒ INSERT 0건 ⇒ **마이그는 "성공"으로 기록되고 아무 일도 안 했다.**
--   조용히 실패했다. 그래서 아무도 못 봤다.
--
-- 🔴 이건 DB-111 만의 문제가 아니다 — DB-32 도 같은 모양이라 **처음부터 그랬다.**
--   판매·매입이 여태 기표되던 건, 8계정을 **CompanyBootstrapProvisioner**(설치 시 코드 경로)가
--   심어줬기 때문이다. 마이그 경로는 원래부터 0건이었다.
--   ⇒ 신규 설치는 프로비저너가 살렸고, **이미 깔린 고객은 마이그로 못 받는다**는 뜻이다.
--
-- 🔴 PM 자책 — 나는 DB-32 패턴을 **그대로 베꼈다.**
--   "기존 마이그가 이렇게 하니 맞겠지" 로 검증을 건너뛰었다. 시험은 내가 만든
--   `hitpan_e2e` 에 **tenants 행을 손수 넣고** 돌려서 24계정이 나왔다 —
--   **고객 PC 에는 그 행이 없다는 걸 안 봤다.** 가짜 전제로 초록불을 만든 것이다.
--
-- ── 봉합 방식 ───────────────────────────────────────────────────────────
--   테넌트 출처를 **로컬에 실제로 행이 있는 표**로 바꾼다. 셋을 UNION 해서
--   어느 하나만 살아 있어도 잡히게 한다(설치 시점·마이그 시점 차이 방어):
--     ① local_subscription — 정식 설치 부트스트랩이 채운다
--     ② users             — 로그인 계정. 쓸 수 있는 설치본이면 최소 1행은 있다
--     ③ accounts          — 이미 8계정이 있는 기존 고객(가장 확실한 증거)
--
--   ⚠️ `tenants` 는 **빼지 않고 함께 UNION** 한다 — 백오피스 DB 에서 이 마이그가
--     돌 가능성을 막지 않는다(헌법 #1 — 덮어쓰기 금지, 있는 경로는 살린다).
--
--   ⚠️ 멱등 — NOT EXISTS 라 재실행해도 중복 INSERT 없다. DB-111 이 (백오피스처럼)
--     이미 심은 환경에서는 이 마이그가 0건으로 조용히 지나간다. 그게 맞는 동작이다.

INSERT INTO accounts
    (account_code, tenant_id, account_name, account_type, parent_code, is_active, sort_order, created_at)
SELECT
    t.account_code,
    tn.tenant_id,
    t.account_name,
    t.account_type,
    t.parent_code,
    1,
    t.sort_order,
    NOW(6)
FROM (
    -- 로컬·본사 어느 쪽이든 살아 있는 테넌트를 모은다 (중복은 UNION 이 제거)
    SELECT tenant_id FROM local_subscription
    UNION
    SELECT tenant_id FROM users
    UNION
    SELECT tenant_id FROM accounts
    UNION
    SELECT tenant_id FROM tenants WHERE status IN ('active', 'trial', 'suspended')
) tn
CROSS JOIN (
    -- ── 자산 ──────────────────────────────────────────────
    -- 현금: 사장님 지시로 **수기 입력**. 자동 시재 계산 없음. 분개 상대계정 그릇.
    SELECT '10100' AS account_code, '현금'         AS account_name, 'asset'     AS account_type, '10000' AS parent_code, 101 AS sort_order
    UNION ALL SELECT '10300', '보통예금',     'asset',     '10000', 103
    UNION ALL SELECT '10800', '외상매출금',   'asset',     '10000', 108
    UNION ALL SELECT '14600', '원재료',       'asset',     '14000', 146
    UNION ALL SELECT '16900', '재공품',       'asset',     '16000', 169
    UNION ALL SELECT '17600', '부가세대급금', 'asset',     '17000', 176
    -- ── 부채 ──────────────────────────────────────────────
    UNION ALL SELECT '23200', '외상매입금',   'liability', '23000', 232
    UNION ALL SELECT '25300', '미지급금',     'liability', '25000', 253
    UNION ALL SELECT '25400', '예수금',       'liability', '25000', 254
    UNION ALL SELECT '25500', '부가세예수금', 'liability', '25000', 255
    -- ── 수익 ──────────────────────────────────────────────
    UNION ALL SELECT '40100', '상품매출',     'revenue',   '40000', 401
    -- ── 비용 (매출원가) ───────────────────────────────────
    UNION ALL SELECT '50100', '상품매입',     'expense',   '50000', 501
    -- ── 비용 (판매비와관리비) ─────────────────────────────
    UNION ALL SELECT '80100', '급여',         'expense',   '80000', 801
    UNION ALL SELECT '81100', '복리후생비',   'expense',   '81000', 811
    UNION ALL SELECT '81200', '여비교통비',   'expense',   '81000', 812
    UNION ALL SELECT '81300', '접대비',       'expense',   '81000', 813
    UNION ALL SELECT '81400', '통신비',       'expense',   '81000', 814
    UNION ALL SELECT '81500', '수도광열비',   'expense',   '81000', 815
    UNION ALL SELECT '81700', '세금과공과',   'expense',   '81000', 817
    UNION ALL SELECT '81900', '감가상각비',   'expense',   '81000', 819
    UNION ALL SELECT '82100', '보험료',       'expense',   '81000', 821
    UNION ALL SELECT '82200', '차량유지비',   'expense',   '81000', 822
    UNION ALL SELECT '82500', '소모품비',     'expense',   '81000', 825
    UNION ALL SELECT '82600', '지급수수료',   'expense',   '81000', 826
    UNION ALL SELECT '82700', '광고선전비',   'expense',   '81000', 827
    UNION ALL SELECT '83100', '지급임차료',   'expense',   '81000', 831
    UNION ALL SELECT '84100', '잡비',         'expense',   '81000', 841
) t
WHERE tn.tenant_id IS NOT NULL
  AND tn.tenant_id <> ''
  AND NOT EXISTS (
      SELECT 1 FROM accounts a
       WHERE a.tenant_id = tn.tenant_id
         AND a.account_code = t.account_code
  );
