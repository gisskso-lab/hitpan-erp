-- DB-32: 표준 계정과목 시드 데이터 (AutoJournalHelper 6개 계정 필수)
-- 원인: journal_lines.fk_jl_account → accounts(tenant_id, account_code) FK
--       accounts 테이블이 비어있어 매입·매출 확정 시 1452 FK 위반 → 409 발생
-- 대상: 모든 기존 테넌트에 6개 표준 계정과목 INSERT (이미 있으면 무시)
-- 관련: AutoJournalHelper.cs 상수 50100/17600/23200/40100/25500/10800

INSERT IGNORE INTO accounts (account_code, tenant_id, account_name, account_type, parent_code, is_active, sort_order)
SELECT
    t.account_code,
    tn.tenant_id,
    t.account_name,
    t.account_type,
    t.parent_code,
    1,
    t.sort_order
FROM tenants tn
CROSS JOIN (
    SELECT '10800' AS account_code, '외상매출금'    AS account_name, 'asset'    AS account_type, '10000' AS parent_code, 10 AS sort_order
    UNION ALL
    SELECT '17600',                 '부가세대급금',               'asset',                       '17000',                20
    UNION ALL
    SELECT '23200',                 '외상매입금',                 'liability',                   '23000',                30
    UNION ALL
    SELECT '25500',                 '부가세예수금',               'liability',                   '25000',                40
    UNION ALL
    SELECT '40100',                 '상품매출',                   'revenue',                     '40000',                50
    UNION ALL
    SELECT '50100',                 '상품매입',                   'expense',                     '50000',                60
) t
WHERE tn.status IN ('active', 'trial', 'suspended');
