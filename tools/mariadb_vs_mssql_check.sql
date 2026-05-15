-- =========================================================================
-- MariaDB ↔ MSSQL 마이그 검증 비교 스크립트
-- 작성: 2026-05-15 PM 브라운킴
-- 활용: 5/17 마이그 마무리 후 실측 검증
-- 실행: mysql -uhitpan -pHitpan2025! hitpan_erp -e "source tools/mariadb_vs_mssql_check.sql"
-- =========================================================================

SET @tenant_id = '452ca266-97b9-4cd1-a0ac-2f37830c81f6';

-- ---------------------------------------------------------------------------
-- 1. 행수 합산 (MariaDB 측)
-- ---------------------------------------------------------------------------
SELECT '=== MariaDB 마이그 결과 행수 ===' AS report;

SELECT 'stock_ledger (← DOCFB)' AS table_name,
       COUNT(*) AS rows_total,
       MIN(ledger_date) AS min_date,
       MAX(ledger_date) AS max_date
FROM stock_ledger WHERE tenant_id = @tenant_id
UNION ALL
SELECT 'collections (← DOCF5)', COUNT(*),
       MIN(collection_date), MAX(collection_date)
FROM collections WHERE tenant_id = @tenant_id
UNION ALL
SELECT 'tax_invoices (← DOCF4)', COUNT(*),
       MIN(invoice_date), MAX(invoice_date)
FROM tax_invoices WHERE tenant_id = @tenant_id
UNION ALL
SELECT 'partners (← DOCF8)', COUNT(*), NULL, NULL
FROM partners WHERE tenant_id = @tenant_id
UNION ALL
SELECT 'bank_transactions (← BANKF)', COUNT(*),
       MIN(tx_date), MAX(tx_date)
FROM bank_transactions WHERE tenant_id = @tenant_id
UNION ALL
SELECT 'cashbook (← DOCF6/I)', COUNT(*),
       MIN(tx_date), MAX(tx_date)
FROM cashbook WHERE tenant_id = @tenant_id
UNION ALL
SELECT 'expenses (← DOCF6/E)', COUNT(*),
       MIN(expense_date), MAX(expense_date)
FROM expenses WHERE tenant_id = @tenant_id
UNION ALL
SELECT 'employees (← DOCSW)', COUNT(*), NULL, NULL
FROM employees WHERE tenant_id = @tenant_id
UNION ALL
SELECT 'sales_orders (← DOCF2/DOCFO)', COUNT(*), NULL, NULL
FROM sales_orders WHERE tenant_id = @tenant_id
UNION ALL
SELECT 'purchase_orders (← DOCFA)', COUNT(*), NULL, NULL
FROM purchase_orders WHERE tenant_id = @tenant_id
UNION ALL
SELECT 'items (← DOCFS)', COUNT(*), NULL, NULL
FROM items WHERE tenant_id = @tenant_id;

-- ---------------------------------------------------------------------------
-- 2. 멱등 키 정답 검증 (sourceId 중복 0건이어야 함)
-- ---------------------------------------------------------------------------
SELECT '=== 멱등 키 무결성 검증 ===' AS report;

-- stock_ledger: PK 정답 IJ_DT/IJ_IO/IJ_SEQ/IJ_BUY/IJ_SUN → source_id 5컬럼 복합
SELECT 'stock_ledger sourceId 중복' AS check_name,
       COUNT(*) - COUNT(DISTINCT source_id) AS dup_count
FROM stock_ledger
WHERE tenant_id = @tenant_id AND source_type = 'migration';

-- collections: PK 정답 S_BUY/S_YMD/S_SUN/S_GU
SELECT 'collections sourceId 중복', COUNT(*) - COUNT(DISTINCT source_id)
FROM collections
WHERE tenant_id = @tenant_id AND source_type = 'migration';

-- bank_transactions: PK 정답 BK_NO/BK_YMD/BK_JWASU/BK_JEN
SELECT 'bank_transactions sourceId 중복', COUNT(*) - COUNT(DISTINCT source_id)
FROM bank_transactions
WHERE tenant_id = @tenant_id AND source_type = 'migration';

-- ---------------------------------------------------------------------------
-- 3. 워크플로우 ID 연결 검증 (사장님 격언)
-- ---------------------------------------------------------------------------
SELECT '=== 워크플로우 ID 연결 검증 ===' AS report;

-- stock_ledger.doc_no = "00000000" 비율 (워크플로우 끊긴 row)
SELECT 'stock_ledger 워크플로우 끊김' AS check_name,
       SUM(CASE WHEN doc_no = '00000000' OR doc_no IS NULL THEN 1 ELSE 0 END) AS broken_rows,
       COUNT(*) AS total_rows,
       ROUND(SUM(CASE WHEN doc_no = '00000000' OR doc_no IS NULL THEN 1 ELSE 0 END) / COUNT(*) * 100, 2) AS broken_pct
FROM stock_ledger
WHERE tenant_id = @tenant_id AND source_type = 'migration';

-- ---------------------------------------------------------------------------
-- 4. PII 암호화 검증 (헌법 #5)
-- ---------------------------------------------------------------------------
SELECT '=== PII 암호화 검증 ===' AS report;

-- partners.representative_juminno 컬럼 평문 노출 여부 (Value Converter 적용 시 BLOB)
SELECT 'partners 주민번호 컬럼 타입' AS check_name,
       column_type
FROM information_schema.columns
WHERE table_schema = 'hitpan_erp'
  AND table_name = 'partners'
  AND column_name LIKE '%jumin%';

-- ---------------------------------------------------------------------------
-- 5. FK 무결성 검증
-- ---------------------------------------------------------------------------
SELECT '=== FK 무결성 검증 ===' AS report;

-- stock_ledger → items FK
SELECT 'stock_ledger.item_id orphan' AS check_name,
       COUNT(*) AS orphan_count
FROM stock_ledger sl
LEFT JOIN items i ON sl.item_id = i.item_id AND sl.tenant_id = i.tenant_id
WHERE sl.tenant_id = @tenant_id
  AND i.item_id IS NULL;

-- stock_ledger → warehouses FK
SELECT 'stock_ledger.warehouse_id orphan', COUNT(*)
FROM stock_ledger sl
LEFT JOIN warehouses w ON sl.warehouse_id = w.warehouse_id AND sl.tenant_id = w.tenant_id
WHERE sl.tenant_id = @tenant_id
  AND w.warehouse_id IS NULL;

-- collections → partners FK
SELECT 'collections.partner_id orphan', COUNT(*)
FROM collections c
LEFT JOIN partners p ON c.partner_id = p.partner_id AND c.tenant_id = p.tenant_id
WHERE c.tenant_id = @tenant_id
  AND p.partner_id IS NULL;

-- ---------------------------------------------------------------------------
-- 6. 헌법 #3 INSERT ONLY 원장 검증
-- ---------------------------------------------------------------------------
SELECT '=== INSERT ONLY 원장 검증 ===' AS report;

-- stock_ledger: updated_at은 NULL 또는 created_at과 동일해야 함 (UPDATE 0)
SELECT 'stock_ledger UPDATE 감지' AS check_name,
       COUNT(*) AS update_count
FROM stock_ledger
WHERE tenant_id = @tenant_id
  AND updated_at IS NOT NULL
  AND updated_at <> created_at;

-- ---------------------------------------------------------------------------
-- 7. 5/14 12/13 PASS 확인
-- ---------------------------------------------------------------------------
SELECT '=== 13/13 PASS 카드 ===' AS report;

SELECT 'partners' AS card, COUNT(*) AS rows, 13542 AS target_5_14, COUNT(*) - 13542 AS diff FROM partners WHERE tenant_id = @tenant_id
UNION ALL SELECT 'items', COUNT(*), 309, COUNT(*) - 309 FROM items WHERE tenant_id = @tenant_id
UNION ALL SELECT 'employees', COUNT(*), 10, COUNT(*) - 10 FROM employees WHERE tenant_id = @tenant_id
UNION ALL SELECT 'stock_ledger', COUNT(*), 115460, COUNT(*) - 115460 FROM stock_ledger WHERE tenant_id = @tenant_id
UNION ALL SELECT 'cashbook', COUNT(*), 20175, COUNT(*) - 20175 FROM cashbook WHERE tenant_id = @tenant_id
UNION ALL SELECT 'expenses', COUNT(*), 27639, COUNT(*) - 27639 FROM expenses WHERE tenant_id = @tenant_id
UNION ALL SELECT 'bank_transactions', COUNT(*), 87808, COUNT(*) - 87808 FROM bank_transactions WHERE tenant_id = @tenant_id;
