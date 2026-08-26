-- DB-111 — 계정과목 확장 (수금·지급·경비·급여 기표 전제) · DB-32 시드 동기화
--
-- 🔴 사장님 오더 (2026-08-27)
--   "수금, 지급, 경비, 급여등 모든 돈의 흐름을 회계장부 하나로 모두 모여서 정합하도록 배선작업할것."
--   "현금은 수기로!!!!"
--
-- 🔴 왜 계정과목부터인가 (착수 순서의 물리적 전제)
--   journal_lines → accounts 로 FK 가 걸려 있다 (fk_jl_account).
--   없는 계정과목에 기표하면 **FK 1452 로 죽는다**. 즉 계정을 먼저 심지 않으면
--   수금·지급·경비·급여 배선은 코드를 아무리 잘 짜도 첫 줄에서 터진다.
--   ⇒ 이 마이그가 2·3차(기표 배선)의 물리적 전제다.
--
-- 🔴 현금은 수기 — 그런데 계정은 왜 만드나 (사장님 지시 정확히 반영)
--   사장님 지시는 **현금 잔액을 시스템이 자동으로 굴리지 말라**는 것이다.
--   그래서 시재 자동계산·현금출납부 자동생성은 **만들지 않는다.**
--   다만 복식부기는 차변·대변이 **반드시 짝**이라, 수금 분개의 차변 자리에
--   놓을 계정 **행 자체**는 있어야 한다. 그 그릇이 10100 현금이다.
--   ⇒ 값은 사람이 넣고(수기), 계정과목은 그 값을 받는 그릇으로만 존재한다.
--
-- 🔴 별건 동반 봉합 — DB-32 가 6개, 프로비저너가 8개였다 (잠복 P0)
--   CompanyBootstrapProvisioner.cs:395  → 8개 (14600 원재료 · 16900 재공품 포함)
--   DB-32_seed_chart_of_accounts.sql:18 → 6개 (그 둘이 **없다**)
--   ⇒ 마이그 경로로만 만들어진 테넌트는 **BOM 생산 확정 시 FK 1452 로 죽는다.**
--     아직 신고가 없는 건 BOM 을 쓰는 고객이 없어서로 추정된다.
--     이 마이그가 14600·16900 도 같이 심어 두 경로를 일치시킨다.
--
-- 🔴 왜 INSERT IGNORE 가 아니라 NOT EXISTS 인가 (헌법 #13 · 8/25 교훈)
--   INSERT IGNORE 는 FK 위반·데이터 잘림 같은 **진짜 오류까지 삼킨다.**
--   (20260825작17 에서 이미 당한 자리 — 오류가 조용히 사라져 원인을 못 찾았다.)
--   NOT EXISTS 는 "이미 있으면 건너뛴다"만 하고 다른 오류는 그대로 터뜨린다.
--   재실행해도 안전하다(멱등).
--
-- 🔴 대상: 살아있는 모든 테넌트 (status IN active/trial/suspended) — DB-32 와 같은 축.
--
-- 계정체계 — 한국 표준 계정과목 코드 5자리. 기존 8개와 번호가 겹치지 않는다.
--   자산  10100 현금 / 10300 보통예금
--   부채  25300 미지급금 / 25400 예수금
--   비용  80100 급여 / 81100 복리후생비 / 81200 여비교통비 / 81300 접대비
--         81400 통신비 / 81500 수도광열비 / 81700 세금과공과 / 81900 감가상각비
--         82100 보험료 / 82200 차량유지비 / 82500 소모품비 / 82600 지급수수료
--         82700 광고선전비 / 83100 지급임차료 / 84100 잡비
--
-- 관련: AutoJournalHelper.cs (기표 상수) · CompanyBootstrapProvisioner.cs:395 (신규 시드)
--       docs/검증/20260827_전수조사_회계배선_매입라인_창고정합.md §1-3

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
FROM tenants tn
CROSS JOIN (
    -- ── 자산 ──────────────────────────────────────────────
    -- 현금: 사장님 지시로 **수기 입력**. 자동 시재 계산 없음. 분개 상대계정 그릇.
    SELECT '10100' AS account_code, '현금'         AS account_name, 'asset'     AS account_type, '10000' AS parent_code, 101 AS sort_order
    UNION ALL SELECT '10300', '보통예금',     'asset',     '10000', 103
    -- ── 부채 ──────────────────────────────────────────────
    UNION ALL SELECT '25300', '미지급금',     'liability', '25000', 253
    UNION ALL SELECT '25400', '예수금',       'liability', '25000', 254
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
    -- ── DB-32 누락분 동반 봉합 (프로비저너에는 있는데 마이그에는 없던 2개) ──
    UNION ALL SELECT '14600', '원재료',       'asset',     '14000', 146
    UNION ALL SELECT '16900', '재공품',       'asset',     '16000', 169
) t
WHERE tn.status IN ('active', 'trial', 'suspended')
  AND NOT EXISTS (
      SELECT 1 FROM accounts a
       WHERE a.tenant_id = tn.tenant_id
         AND a.account_code = t.account_code
  );
