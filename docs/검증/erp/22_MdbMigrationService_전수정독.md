# 22. MdbMigrationService.cs 전수 정독서 (1,541줄, 세미콜론·괄호까지)

**작성:** 백엔드·DB 매니저 합동
**파일:** `src/HitPan.Application/Services/MdbMigrationService.cs`

---

## 1. 클래스 헤더 + 필드 (L:1-44)

```csharp
[SupportedOSPlatform("windows")]
public sealed class MdbMigrationService
{
    private readonly IDbConnection _db;
    private readonly ILogger<MdbMigrationService> _logger;
    private readonly IBinaryCryptoService _crypto;

    private const string OleDbConnTemplate =
        "Provider=Microsoft.ACE.OLEDB.12.0;Data Source={0};Jet OLEDB:Database Password={1};";

    private const int BatchSize = 2000;

    private static readonly AsyncLocal<string?> _mdbPasswordContext = new();
    private static readonly AsyncLocal<Dictionary<string, string>?> _ioDeliveryMap = new();
}
```

**XML 주석 (L:13-19):**
```
/// 레거시 히트판(VB6 + Access MDB) → 신규 히트판(MariaDB) 데이터 마이그레이션 서비스.
/// 2026-05-13 야간 재작성: Bulk INSERT 방식으로 전 16개 메서드 통일.
/// — 1,000행/배치, 다중 행 VALUES, 전체 트랜잭션 복원.
```

**핵심 상수:**
- `OleDbConnTemplate` — `{0}=경로, {1}=비번`
- `BatchSize = 2000` (2026-05-13 야간 #2, 500→2000)

---

## 2. 메서드 인덱스 36개

| # | 메서드 | line | 역할 |
|---|---|---|---|
| 1 | MigrateAsync(folder, tenant, ct) | 50-52 | 비번 없음 진입점 |
| 2 | MigrateAsync(folder, tenant, pwd, ct) | 54-71 | 비번 있음 진입점 |
| 3 | PreviewAsync(folder, tenant, ct) | 73-75 | 미리보기 진입점 |
| 4 | PreviewAsync(folder, tenant, pwd, ct) | 77-82 | 비번 있음 미리보기 |
| 5 | PreviewCoreAsync | 84-129 | 16+4 테이블 COUNT |
| 6 | MigrateCoreAsync | 131-225 | ★ 핵심 오케스트레이션 |
| 7 | BulkInsertAsync | 236-284 | MySqlBulkCopy 헬퍼 |
| 8 | EnsureMigrationWarehouseAsync | 290-308 | 기본 창고 |
| 9 | EnsureMigrationEmployeeAsync | 310-333 | 기본 사원 |
| 10 | MigratePartnersAsync | 339-422 | DOCF8 → partners |
| 11 | MigrateItemsAsync | 428-486 | DOCFS → items |
| 12 | MigrateBomAsync | 492-555 | DOCRT → bom_headers/items |
| 13 | MigrateEmployeesAsync | 561-629 | DOCSW → employees (AES 3건) |
| 14 | MigrateTransactionsAsync | 635-751 | DOCF2+F1 → sales/purchase_orders |
| 15 | MigrateStockLedgerAsync | 757-809 | DOCFB → stock_ledger |
| 16 | MigrateCollectionsAsync | 815-858 | DOCF5 → collections |
| 17 | MigrateCashbookAsync | 864-905 | DOCF6 → cashbook |
| 18 | MigrateExpensesAsync | 911-955 | DOCF7 → expenses |
| 19 | MigratePurchaseOrdersFromIUAsync | 961-1016 | DOCFA → purchase_orders |
| 20 | MigrateSalesOrdersFromIOAsync | 1022-1116 | DOCFO → sales_orders + sales_deliveries |
| 21 | MigrateTaxInvoicesAsync | 1130-1221 | DOCF4 → tax_invoices (합성 deliveries) |
| 22 | MigrateBillsAsync | 1227-1285 | DOCF9+FQ → bills |
| 23 | MigrateCardPaymentsAsync | 1291-1372 | DOCCD+CD1 → card_payments |
| 24 | MigrateBankTransactionsAsync | 1378-1417 | BANKF → bank_transactions |
| 25 | ResolveMdbPaths | 1423-1438 | 파일 경로 해석 |
| 26 | CountMdbTable | 1440-1452 | 행수 COUNT |
| 27 | EnsureOpenAsync | 1454-1463 | DB 커넥션 열기 |
| 28 | OpenOleDb | 1465-1472 | OLEDB 커넥션 |
| 29 | ReadMdbTable | 1474-1481 | MDB 쿼리 실행 |
| 30 | ParseLegacyDate | 1483-1494 | yyyyMMdd 파싱 |
| 31 | ParseDateOrNull | 1496-1505 | 날짜 nullable |
| 32 | BuildItemKey | 1507-1508 | `pum|spec` |
| 33 | GetStr | 1510-1515 | DataRow → string |
| 34 | GetInt | 1517-1523 | → int |
| 35 | GetShort | 1525-1531 | → short |
| 36 | GetDec | 1533-1539 | → decimal |

---

## 3. MigrateCoreAsync 전수 (L:131-225) ★ 진앙지

### 세션 변수 SET SQL (L:154-160) — 7가지
```sql
SET SESSION
    unique_checks=0,
    foreign_key_checks=0,
    innodb_lock_wait_timeout=600,
    net_read_timeout=600,
    net_write_timeout=600,
    max_statement_time=0
```

**용도:**
- `unique_checks=0` — UNIQUE 검증 지연
- `foreign_key_checks=0` — FK 검증 지연
- `innodb_lock_wait_timeout=600` — 50초 → 10분 (좀비 잡 락 회피, 5/14 새벽 봉합)
- `net_read/write_timeout=600` — 거대 BulkCopy 단절 방지
- `max_statement_time=0` — 강제 종료 비활성

### 트랜잭션 (L:163-204)
```csharp
using var tx = _db.BeginTransaction();
try
{
    await EnsureMigrationWarehouseAsync(tenantId, defaultWarehouseId, now, tx, ct);
    var defaultEmployeeId = await EnsureMigrationEmployeeAsync(tenantId, now, tx, ct);
    employeeMap["__MIG_DEFAULT__"] = defaultEmployeeId;

    using (var oleConn = OpenOleDb(pyojunPath))
    {
        result.Partners = await MigratePartnersAsync(oleConn, tenantId, now, partnerMap, tx, ct);
        result.Items = await MigrateItemsAsync(oleConn, tenantId, now, itemMap, tx, ct);
        result.BomHeaders = await MigrateBomAsync(oleConn, tenantId, now, itemMap, tx, ct);
        result.Employees = await MigrateEmployeesAsync(oleConn, tenantId, now, employeeMap, tx, ct);
    }

    using (var oleConn = OpenOleDb(pandataPath))
    {
        var (salesCount, purchaseCount) = await MigrateTransactionsAsync(...);
        result.SalesOrders = salesCount;
        result.PurchaseOrders = purchaseCount;
        result.StockLedger = await MigrateStockLedgerAsync(...);
        result.Collections = await MigrateCollectionsAsync(...);
        result.Cashbook = await MigrateCashbookAsync(...);
        result.Expenses = await MigrateExpensesAsync(...);
        result.PurchaseOrdersFromIU = await MigratePurchaseOrdersFromIUAsync(...);
        result.SalesOrdersFromIO = await MigrateSalesOrdersFromIOAsync(...);
        result.TaxInvoices = await MigrateTaxInvoicesAsync(...);
        result.Bills = await MigrateBillsAsync(...);
        result.CardPayments = await MigrateCardPaymentsAsync(...);
        result.BankTransactions = await MigrateBankTransactionsAsync(...);
    }

    tx.Commit();
    return result;
}
catch (Exception ex)
{
    _logger.LogError(ex, "[MDB마이그레이션] 실패 — 전체 롤백");
    try { tx.Rollback(); } catch (Exception rbex) { _logger.LogError(rbex, "[MDB마이그레이션] 롤백 실패"); }
    throw;
}
```

⚠️ **5/14 새벽 사고 진앙:** 100만+ 행 단일 tx → 좀비 롤백 15분+. **헌법 #20 오독.**

### finally (L:213-223)
```csharp
finally
{
    try
    {
        await _db.ExecuteAsync("SET SESSION unique_checks=1, foreign_key_checks=1");
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[MDB마이그레이션] 세션 변수 원복 실패");
    }
}
```

---

## 4. BulkInsertAsync (L:236-284)

```csharp
private async Task<int> BulkInsertAsync(
    string tableName, IReadOnlyList<string> columns,
    IReadOnlyList<object?[]> rows,
    IDbTransaction? tx, CancellationToken ct)
{
    if (rows.Count == 0) return 0;

    var dt = new DataTable(tableName);
    for (int c = 0; c < columns.Count; c++)
        dt.Columns.Add(columns[c], typeof(object));

    foreach (var row in rows)
    {
        var arr = new object?[columns.Count];
        for (int c = 0; c < columns.Count; c++)
            arr[c] = row[c] ?? DBNull.Value;
        dt.Rows.Add(arr);
    }

    var mysqlConn = (MySqlConnection)_db;
    var mysqlTx = tx as MySqlTransaction;

    var bulk = new MySqlBulkCopy(mysqlConn, mysqlTx)
    {
        DestinationTableName = tableName,
        BulkCopyTimeout = 0  // 무제한 (2026-05-14 #6)
    };

    for (int i = 0; i < columns.Count; i++)
        bulk.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(i, columns[i]));

    var result = await bulk.WriteToServerAsync(dt, ct).ConfigureAwait(false);

    if (result.Warnings.Count > 0)
        _logger.LogDebug("[MDB마이그레이션] {Table} BulkCopy 경고 {Count}건", tableName, result.Warnings.Count);

    return (int)result.RowsInserted;
}
```

**내부 동작:** LOAD DATA LOCAL INFILE 자동 사용. `BulkCopyTimeout=0` 무제한.

---

## 5. EnsureMigrationWarehouseAsync (L:290-308)

### SQL: 존재 확인
```sql
SELECT COUNT(*) FROM warehouses WHERE warehouse_id = @Id AND tenant_id = @TenantId
```

### SQL: INSERT
```sql
INSERT INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, location, is_active, created_at, updated_at)
VALUES (@Id, @TenantId, 'WH-MIG', '마이그레이션창고', 'normal', '레거시 데이터 이관용', 1, @Now, @Now)
```

**5/14 새벽 봉합:** `commandTimeout: 600` 명시 (좀비 락 50초 회피).

---

## 6. EnsureMigrationEmployeeAsync (L:310-333)

### SQL: 존재 확인
```sql
SELECT COUNT(*) FROM employees WHERE employee_id = @Id AND tenant_id = @TenantId
```

### SQL: INSERT
```sql
INSERT INTO employees (employee_id, tenant_id, emp_no, emp_name, position, job_title, emp_type,
                       join_date, is_active, created_at, updated_at)
VALUES (@Id, @TenantId, 'EMP-MIG', '마이그기본담당', '담당', '레거시', 'regular',
                       @Now, 1, @Now, @Now)
```

---

## 7. AES 호출 위치 4건 (절대 외워야 할 보안 라인)

| # | line | 컬럼 | 코드 |
|---|---|---|---|
| 1 | 415 | partners.ceo_resident_no_encrypted | `_crypto.EncryptToBytes(topJumin)` |
| 2 | 610 | employees.resident_no_encrypted | `_crypto.EncryptToBytes(residentNo)` |
| 3 | 611 | employees.salary_encrypted | `_crypto.EncryptToBytes(salary.ToString(CultureInfo.InvariantCulture))` |
| 4 | 614 | employees.salary_extra_encrypted | `_crypto.EncryptToBytes(salaryExtra)` |

🔴 **migration_errors.raw_data** — 미구현 (보안팀장 §10 사장님 결재 요청 1번 사유)

---

## 8. AsyncLocal 컨텍스트 2개

| 이름 | 선언 | 설정 | 읽기 | 용도 |
|---|---|---|---|---|
| `_mdbPasswordContext` | L:34 | L:58, 80 | L:1467 | OpenOleDb에서 MDB 비번 |
| `_ioDeliveryMap` | (L:1119 사용 추정) | L:1112 | L:1138 | DOCFO → delivery_id 매핑 (TaxInvoices 재참조) |

---

## 9. 메서드별 MDB → 신 테이블 매핑 (전수 표)

### 9.1 Partners (L:339-422) — DOCF8 → partners
| MDB | 신 | 변환 |
|---|---|---|
| buy_code | (sourceId) | dedup |
| buy_name | partner_name | GetStr |
| buy_gu | partner_type | 1→supplier, 2→customer, else→both |
| buy_taxgubun | tax_type | 1/과세→taxable, 2/면세→exempt, 3/영세→zero_rate |
| buy_rem0~6 | memo | 구분자 결합 |
| buy_topjumin | ceo_resident_no_encrypted | **AES L:415** |

### 9.2 Items (L:428-486) — DOCFS → items
| MDB | 신 |
|---|---|
| S_PUM | item_name |
| S_KU | spec |
| S_TAX | tax_type |
| S_IDAN | purchase_price (decimal) |
| S_PDAN | sale_price |
| S_JEK | cost_price |
| S_DANW | unit (기본 EA) |

### 9.3 BOM (L:492-555) — DOCRT → bom_headers + bom_items
- `ORDER BY RT_PUM, RT_KU, RT_SUN` (재개 안전)
- 자재 itemMap 매핑 실패 시 경고 로그

### 9.4 Employees (L:561-629) — DOCSW → employees ★ AES 3건
| MDB | 신 | 변환 |
|---|---|---|
| SW_NAME | emp_name | GetStr |
| SW_JUMIN | resident_no_encrypted | **AES L:610** |
| SW_PAY | salary_encrypted | **AES L:611** |
| SW_PAYoth | salary_extra_encrypted | **AES L:614** |
| SW_BIRTHgu | birth_calendar | 0→1 보정 |
| SW_PAYgu | salary_category | 0→null |
| SW_PAYeuy | (미정) | 0→null |
| SW_PAYkuk | salary_country | 0→null |

### 9.5 Transactions (L:635-751) — DOCF2(헤더) + DOCF1(상세)
- K2_GUBUN="S" → sales_orders + sales_order_items
- K2_GUBUN="B" → purchase_orders + purchase_order_items
- total_amount = K2_AMT + K2_VAT

### 9.6 StockLedger (L:757-809) — DOCFB → stock_ledger (헌법 #3 INSERT ONLY)
| MDB | 신 |
|---|---|
| IJ_PUM+IJ_KU | item_id (맵핑) |
| IJ_BUY | partner_id |
| IJ_DT | ledger_date |
| IJ_IO | move_type (I→in, O→out) |
| IJ_QTY | qty_in or qty_out |
| IJ_AMT | supply_amount |
- sourceId 생성 후 36자 제한

### 9.7 Collections (L:815-858) — DOCF5 → collections
| MDB | 신 |
|---|---|
| S_BUY | partner_id |
| S_YMD | collection_date (ParseLegacyDate) |
| S_GU | collection_method (cash/card/note/check) |
| S_SUK / S_BAL | amount (SUK>0 우선) |

⚠️ 5/14 새벽 좀비 진앙 — 614,212행

### 9.8 Cashbook (L:864-905) — DOCF6 → cashbook
- description = AC_JEN + " " + AC_JEK (200자 제한)
- 항상 expense (isExpense=true)

### 9.9 Expenses (L:911-955) — DOCF7 → expenses
- amount = SC_CR or SC_DR (cr>0 우선), 0이면 skip
- 사원 맵핑 실패 시 default 사용

### 9.10 PurchaseOrders/IU (L:961-1016) — DOCFA
- `ORDER BY IU_NO, IU_SUN` (재개 안전)
- GroupBy(IU_NO), supply = Sum(IU_AMT), vat = Sum(IU_VAT)

### 9.11 SalesOrders/IO (L:1022-1116) — DOCFO → sales_orders + sales_deliveries
- `ORDER BY IO_NO, IO_SUN`
- ★ **헌법 #20 흐름 복원:** delivery 자동 합성 + `_ioDeliveryMap.Value` 설정 (L:1112)
- delivered_qty = qty (완전 처리)

### 9.12 TaxInvoices (L:1130-1221) — DOCF4
- `_ioDeliveryMap.Value` 읽기 (L:1138)
- ioMap 매칭 → delivery_id 연결 / 없으면 합성 sales_deliveries 생성
- invoice_no 중복 시 `-1`, `-2` 접미사

### 9.13 Bills (L:1227-1285) — DOCF9+DOCFQ
- EU_CLA="2" → "P" (약속어음), else → "R" (받을어음)

### 9.14 CardPayments (L:1291-1372) — DOCCD + DOCCD1
- 헤더(CD), 라인(CD1) 2단
- months = amt / hal

### 9.15 BankTransactions (L:1378-1417) — BANKF
- BK_JEN="2" → tx_type="2", else → "1"

---

## 10. SQL 전수 인용 (마이그 서비스 내 명시적 SQL)

| # | line | SQL | 용도 |
|---|---|---|---|
| 1 | 154-160 | `SET SESSION unique_checks=0,...` | 세션 설정 |
| 2 | 218 | `SET SESSION unique_checks=1, foreign_key_checks=1` | 세션 원복 |
| 3 | 294 | `SELECT COUNT(*) FROM warehouses WHERE warehouse_id=@Id AND tenant_id=@TenantId` | 창고 존재 확인 |
| 4 | 301-304 | `INSERT INTO warehouses (...) VALUES (...)` | 창고 생성 |
| 5 | 315 | `SELECT COUNT(*) FROM employees WHERE employee_id=@Id AND tenant_id=@TenantId` | 사원 존재 확인 |
| 6 | 322-327 | `INSERT INTO employees (...) VALUES (...)` | 사원 생성 |

**나머지 16개 INSERT는 MySqlBulkCopy 자동 생성** (LOAD DATA LOCAL INFILE).

---

## 11. OLEDB SELECT 전수 (재개 ORDER BY 검증)

| # | MDB 테이블 | SQL | ORDER BY |
|---|---|---|---|
| 1 | DOCF8 | `SELECT * FROM DOCF8` | ❌ 누락 |
| 2 | DOCFS | `SELECT * FROM DOCFS` | ❌ 누락 |
| 3 | DOCRT | `SELECT * FROM DOCRT ORDER BY RT_PUM, RT_KU, RT_SUN` | ✅ |
| 4 | DOCSW | `SELECT * FROM DOCSW` | ❌ 누락 |
| 5 | DOCF2 | `SELECT * FROM DOCF2` | ❌ 누락 |
| 6 | DOCF1 | `SELECT * FROM DOCF1` | ❌ 누락 |
| 7 | DOCFB | `SELECT * FROM DOCFB` | ❌ 누락 |
| 8 | DOCF5 | `SELECT * FROM DOCF5` | ❌ 누락 |
| 9 | DOCF6 | `SELECT * FROM DOCF6` | ❌ 누락 |
| 10 | DOCF7 | `SELECT * FROM DOCF7` | ❌ 누락 |
| 11 | DOCFA | `SELECT * FROM DOCFA ORDER BY IU_NO, IU_SUN` | ✅ |
| 12 | DOCFO | `SELECT * FROM DOCFO ORDER BY IO_NO, IO_SUN` | ✅ |
| 13 | DOCF4 | `SELECT * FROM DOCF4` | ❌ 누락 |
| 14 | DOCF9 | `SELECT * FROM DOCF9` | ❌ 누락 |
| 15 | DOCFQ | `SELECT * FROM DOCFQ` | ❌ 누락 |
| 16 | DOCCD | `SELECT * FROM DOCCD` | ❌ 누락 |
| 17 | DOCCD1 | `SELECT * FROM DOCCD1` | ❌ 누락 |
| 18 | BANKF | `SELECT * FROM BANKF` | ❌ 누락 |

⚠️ **DB매니저 함정 #3 — 11개+ ORDER BY 누락. 재개 시 중복·누락 위험. W3 D1 작업 필수.**

---

## 12. 유틸리티 메서드 (L:1423-1539)

### ParseLegacyDate (L:1483-1494)
```csharp
private static DateTime? ParseLegacyDate(string? dateStr)
{
    if (string.IsNullOrWhiteSpace(dateStr)) return null;
    var clean = dateStr.Replace("-", "").Replace("/", "").Replace(".", "");
    if (clean.Length >= 8 && DateTime.TryParseExact(clean[..8], "yyyyMMdd",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        return dt;
    if (DateTime.TryParse(dateStr, out var dt2)) return dt2;
    return null;
}
```

### Get* 헬퍼 (L:1510-1539) — 4종 (String/Int/Short/Decimal)
```csharp
private static string GetStr(DataRow row, string col)
{
    if (!row.Table.Columns.Contains(col)) return string.Empty;
    var val = row[col];
    return val == DBNull.Value ? string.Empty : Convert.ToString(val) ?? string.Empty;
}
// GetInt, GetShort, GetDec 동일 패턴
```

---

## 13. 헌법 적용 표

| 헌법 | 적용 line | 상태 |
|---|---|---|
| #1 (구조 변경 금지) | 전 영역 | ✅ |
| #3 (INSERT ONLY 원장) | 757-809 stock_ledger | ⚠️ 거대 tx → 일부 실패 시 전체 ROLLBACK 위험 |
| #4 (금액 decimal) | 431, 798, 845... | ✅ GetDec 사용 |
| #5 (AES 컬럼) | 415, 610, 611, 614 | 🟡 4/5 (raw_data 미구현) |
| #13 (DESCRIBE 의무) | OLEDB SELECT 17개 중 11개 ORDER BY 누락 | ⚠️ |
| #15 (빈 catch 금지) | 206-211, 213-223, 1442-1451 | ✅ |
| #16 (Task.WhenAll + MySqlConn) | 미사용 | ✅ |
| #17 (ENGINE=InnoDB) | warehouses, employees | ✅ |
| #18 (본사 송신 금지) | HttpClient 0건 | ✅ |
| #19 (warnings 0) | [SupportedOSPlatform("windows")] | ✅ |
| #20 (워크플로우 끊김) | tx 단일 거대 ⚠️ + SalesOrdersFromIO synthetic delivery ✅ | ⚠️ 진앙 |
| #22 (데이터 최소) | 로컬 INSERT만 | ✅ |
