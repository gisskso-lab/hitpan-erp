// ============================================================================
// MDB 마이그 베이스라인 실측 도구 (20260904작21 · W1)
// ----------------------------------------------------------------------------
// 무엇을 재나:
//   [1] (옵션 --apply-schema) MigrationRunner 로 테스트 DB 스키마를 최신 DB-NN 까지 올린다.
//   [2] MdbMigrationService.MigrateAsync 를 실제로 불러 레거시 MDB 3개를 이관한다.
//       진행 콜백으로 표별 (status, rows, elapsedMs) 를 받아 표로 찍는다 — 이것이 속도 베이스라인.
//   [3] 이관 직후 검증 SQL 몇 개를 찍는다 — 재고 수량 합·방향별 헤더 수·회계 행수 (선행검증서 P0-A·B·D 판정기).
//
// 운영 보호: DB_NAME 이 hitpan_e2e / hitpan_trgtest 가 아니면 즉시 ABORT (헌법 #39).
// 입력(환경변수):
//   HITPAN_DB_CONF        db.conf 경로 (DB_HOST/PORT/NAME/USER/PASSWORD) — 옆에 hitpan-keys.conf(ERP_ENCRYPTION_KEY)
//   HITPAN_MDB_FOLDER     MDB 3개가 있는 폴더 (기본 C:\Users\소순근\Desktop\공영정보DB)
//   HITPAN_MDB_PASSWORD   MDB 비번 (기록에 남기지 않는다)
//   HITPAN_TENANT_ID      이관할 테넌트 (기본 e2e-mig-baseline-tenant)
//   HITPAN_BASELINE_OUT   결과 markdown 을 쓸 경로 (선택)
// ============================================================================

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Dapper;
using HitPan.Application.Services;
using HitPan.Infrastructure.Configuration;
using HitPan.Infrastructure.Persistence;
using HitPan.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using MySqlConnector;

// ── [0] 운영 보호 가드 ─────────────────────────────────────────────────────────
var dbName = TenantConfigReader.Get("DB_NAME") ?? "";
var allowed = new[] { "hitpan_e2e", "hitpan_trgtest" };
if (!allowed.Any(a => string.Equals(a, dbName, StringComparison.OrdinalIgnoreCase)))
{
    Console.Error.WriteLine($"[ABORT] DB_NAME='{dbName}' — 베이스라인은 테스트 DB(hitpan_e2e/hitpan_trgtest)에서만 돈다(헌법 #39).");
    return 2;
}

var folder = Environment.GetEnvironmentVariable("HITPAN_MDB_FOLDER") ?? @"C:\Users\소순근\Desktop\공영정보DB";
var mdbPassword = Environment.GetEnvironmentVariable("HITPAN_MDB_PASSWORD");
var tenantId = Environment.GetEnvironmentVariable("HITPAN_TENANT_ID") ?? "e2e-mig-baseline-tenant";
var outPath = Environment.GetEnvironmentVariable("HITPAN_BASELINE_OUT");
var applySchema = args.Contains("--apply-schema", StringComparer.OrdinalIgnoreCase);
var skipMigrate = args.Contains("--skip-migrate", StringComparer.OrdinalIgnoreCase);

using var loggerFactory = LoggerFactory.Create(b => b
    .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
    .SetMinimumLevel(LogLevel.Information));
var log = loggerFactory.CreateLogger("baseline");

log.LogInformation("대상 DB={Db} tenant={Tenant} folder={Folder}", dbName, tenantId, folder);

var factory = new MigrationDbConnectionFactory(loggerFactory.CreateLogger<MigrationDbConnectionFactory>());

// ── [1] 스키마 최신화 (선택) ───────────────────────────────────────────────────
if (applySchema)
{
    var runner = new MigrationRunner(factory, loggerFactory.CreateLogger<MigrationRunner>());
    var r = await runner.ApplyPendingAsync(appVersion: "baseline-run");
    log.LogInformation("스키마: success={S} applied={A} skipped={K} failed={F} {Msg}",
        r.Success, r.AppliedMigrationIds.Count, r.SkippedCount, r.FailedMigrationId, r.FailureMessage);
    if (!r.Success) return 1;
}

// ── [2] 이관 실행 + 표별 소요 ─────────────────────────────────────────────────
var host = TenantConfigReader.Get("DB_HOST") ?? "localhost";
var port = TenantConfigReader.Get("DB_PORT") ?? "3306";
var user = TenantConfigReader.GetRequired("DB_USER");
var pwd = TenantConfigReader.GetRequired("DB_PASSWORD");
var connStr = $"Server={host};Port={port};Database={dbName};User={user};Password={pwd};" +
              "DefaultCommandTimeout=600;AllowLoadLocalInfile=true;AllowUserVariables=true;GuidFormat=None;";

var perTable = new ConcurrentDictionary<string, (string Status, int Rows, long Ms, string? Err, int Order)>();
var order = 0;
var report = new StringBuilder();
report.AppendLine($"# 베이스라인 실측 — {DateTime.Now:yyyy-MM-dd HH:mm} · DB={dbName} · tenant={tenantId}");
report.AppendLine();

if (!skipMigrate)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("[ABORT] MDB(Jet OLEDB) 이관은 Windows 에서만 돈다.");
        return 3;
    }
    await using var db = new MySqlConnection(connStr);
    await db.OpenAsync();

    var sw = Stopwatch.StartNew();
    MdbMigrationResult result;
    try
    {
        result = await RunMigrateAsync(db, loggerFactory, factory, folder, tenantId, mdbPassword,
            (table, status, rows, ms, err) =>
            {
                perTable.AddOrUpdate(table,
                    _ => (status, rows, ms, err, Interlocked.Increment(ref order)),
                    (_, old) => (status, rows, ms, err, old.Order));
                if (status is "completed" or "failed")
                    Console.WriteLine($"  [{status,-9}] {table,-22} rows={rows,9:N0}  {ms / 1000.0,8:F1}s {err}");
            });
    }
    catch (Exception ex)
    {
        sw.Stop();
        log.LogError(ex, "이관 실패 ({Elapsed}s)", sw.Elapsed.TotalSeconds);
        return 1;
    }
    sw.Stop();

    report.AppendLine($"## 표별 소요 (총 {sw.Elapsed.TotalSeconds:F1}s)");
    report.AppendLine();
    report.AppendLine("| # | 표 | 상태 | 행수(보고) | 소요(s) | 오류 |");
    report.AppendLine("|---|---|---|---:|---:|---|");
    foreach (var kv in perTable.OrderBy(k => k.Value.Order))
    {
        var v = kv.Value;
        report.AppendLine($"| {v.Order} | {kv.Key} | {v.Status} | {v.Rows:N0} | {v.Ms / 1000.0:F1} | {v.Err} |");
    }
    report.AppendLine();
    report.AppendLine($"결과 객체: `{result}`");
    report.AppendLine();
    Console.WriteLine($"총 소요 {sw.Elapsed.TotalSeconds:F1}s — {result}");
}

// ── [3] 판정 SQL ──────────────────────────────────────────────────────────────
await using (var db2 = new MySqlConnection(connStr))
{
    await db2.OpenAsync();
    var checks = new (string Label, string Sql)[]
    {
        ("stock_ledger 행수", "SELECT COUNT(*) FROM stock_ledger WHERE tenant_id=@T AND source_type='migration'"),
        ("stock_ledger Σqty_in", "SELECT COALESCE(SUM(qty_in),0) FROM stock_ledger WHERE tenant_id=@T AND source_type='migration'"),
        ("stock_ledger Σqty_out", "SELECT COALESCE(SUM(qty_out),0) FROM stock_ledger WHERE tenant_id=@T AND source_type='migration'"),
        ("stock_ledger Σsupply_amount", "SELECT COALESCE(SUM(supply_amount),0) FROM stock_ledger WHERE tenant_id=@T AND source_type='migration'"),
        ("item_stock Σcurrent_qty", "SELECT COALESCE(SUM(s.current_qty),0) FROM item_stock s WHERE s.tenant_id=@T"),
        ("sales_deliveries 헤더", "SELECT COUNT(*) FROM sales_deliveries WHERE tenant_id=@T AND source_type='migration'"),
        ("sales_deliveries Σtotal", "SELECT COALESCE(SUM(total_amount),0) FROM sales_deliveries WHERE tenant_id=@T AND source_type='migration'"),
        ("purchase_receipts 헤더", "SELECT COUNT(*) FROM purchase_receipts WHERE tenant_id=@T AND source_type='migration'"),
        ("purchase_receipts Σtotal", "SELECT COALESCE(SUM(total_amount),0) FROM purchase_receipts WHERE tenant_id=@T AND source_type='migration'"),
        ("tax_invoices 행수", "SELECT COUNT(*) FROM tax_invoices WHERE tenant_id=@T"),
        ("tax_invoices direction 분포", "SELECT GROUP_CONCAT(CONCAT(COALESCE(direction,'NULL'),':',c) ORDER BY direction) FROM (SELECT direction, COUNT(*) c FROM tax_invoices WHERE tenant_id=@T GROUP BY direction) x"),
        ("journal_lines 행수", "SELECT COUNT(*) FROM journal_lines WHERE tenant_id=@T"),
        ("journal_lines Σdebit", "SELECT COALESCE(SUM(debit_amount),0) FROM journal_lines WHERE tenant_id=@T"),
        ("journal_lines Σcredit", "SELECT COALESCE(SUM(credit_amount),0) FROM journal_lines WHERE tenant_id=@T"),
        ("collections 행수", "SELECT COUNT(*) FROM collections WHERE tenant_id=@T"),
        ("collections Σamount", "SELECT COALESCE(SUM(amount),0) FROM collections WHERE tenant_id=@T"),
        ("payments 행수", "SELECT COUNT(*) FROM payments WHERE tenant_id=@T"),
        ("partners", "SELECT COUNT(*) FROM partners WHERE tenant_id=@T"),
        ("items", "SELECT COUNT(*) FROM items WHERE tenant_id=@T"),
        ("bom_headers / bom_items", "SELECT CONCAT((SELECT COUNT(*) FROM bom_headers WHERE tenant_id=@T),' / ',(SELECT COUNT(*) FROM bom_items WHERE tenant_id=@T))"),
        ("warehouses (wh_code)", "SELECT GROUP_CONCAT(wh_code) FROM warehouses WHERE tenant_id=@T"),
        ("cashbook 행수", "SELECT COUNT(*) FROM cashbook WHERE tenant_id=@T"),
        ("expenses 행수", "SELECT COUNT(*) FROM expenses WHERE tenant_id=@T"),
        ("bank_transactions 행수", "SELECT COUNT(*) FROM bank_transactions WHERE tenant_id=@T"),
    };
    report.AppendLine("## 판정 SQL");
    report.AppendLine();
    report.AppendLine("| 항목 | 값 |");
    report.AppendLine("|---|---:|");
    foreach (var (label, sql) in checks)
    {
        string val;
        try
        {
            var o = await db2.ExecuteScalarAsync<object?>(sql, new { T = tenantId });
            val = o switch { null => "NULL", decimal d => d.ToString("N2"), double f => f.ToString("N2"), long l => l.ToString("N0"), int i => i.ToString("N0"), _ => o.ToString() ?? "" };
        }
        catch (Exception ex)
        {
            // 헌법 #15: 삼키지 않고 값 자리에 사유를 남긴다 (컬럼명 불일치 등 — 그 자체가 정보다).
            val = $"ERR {ex.GetType().Name}: {ex.Message}";
        }
        Console.WriteLine($"  {label,-30} {val}");
        report.AppendLine($"| {label} | {val} |");
    }
}

// ── [4] 대사표 (작21 갈래 B — --reconcile) ──────────────────────────────────
// 화면(갈래 C)이 부르는 것과 같은 서비스를 직접 불러 9항목을 찍는다. API·로그인 없이 실측하기 위한 경로.
if (args.Contains("--reconcile", StringComparer.OrdinalIgnoreCase) && OperatingSystem.IsWindows())
{
    await using var db3 = new MySqlConnection(connStr);
    await db3.OpenAsync();
    var recon = await RunReconcileAsync(db3, loggerFactory, folder, mdbPassword, tenantId);
    report.AppendLine();
    report.AppendLine($"## 대사표 (OK {recon.OkCount} / DIFF {recon.DiffCount} / 항목 {recon.Items.Count})");
    report.AppendLine();
    report.AppendLine("| 구분 | 항목 | 레거시 | 히트판 | 차이 | 판정 | 비고 |");
    report.AppendLine("|---|---|---:|---:|---:|---|---|");
    foreach (var it in recon.Items)
    {
        report.AppendLine($"| {it.Category} | {it.Label} | {it.Legacy?.ToString("N2") ?? "—"} | {it.Erp?.ToString("N2") ?? "—"} | {it.Diff?.ToString("N2") ?? "—"} | {it.Status} | {it.Detail} |");
        Console.WriteLine($"  [{it.Status,-4}] {it.Label,-16} L={it.Legacy?.ToString("N2"),20} E={it.Erp?.ToString("N2"),20} D={it.Diff?.ToString("N2"),18} {it.Detail}");
    }
    if (recon.PartnerDiffs.Count > 0)
    {
        report.AppendLine(); report.AppendLine("### 거래처별 차이 상위"); report.AppendLine(); report.AppendLine("| 거래처 | 레거시 | 히트판 | 차이 |"); report.AppendLine("|---|---:|---:|---:|");
        foreach (var d in recon.PartnerDiffs) report.AppendLine($"| {d.Name} | {d.Legacy:N2} | {d.Erp:N2} | {d.Diff:N2} |");
    }
    if (recon.ItemDiffs.Count > 0)
    {
        report.AppendLine(); report.AppendLine("### 품목별 차이 상위"); report.AppendLine(); report.AppendLine("| 품목 | 레거시 | 히트판 | 차이 |"); report.AppendLine("|---|---:|---:|---:|");
        foreach (var d in recon.ItemDiffs) report.AppendLine($"| {d.Name} | {d.Legacy:N2} | {d.Erp:N2} | {d.Diff:N2} |");
    }
}

if (!string.IsNullOrWhiteSpace(outPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
    await File.WriteAllTextAsync(outPath, report.ToString(), new UTF8Encoding(false));
    Console.WriteLine($"기록: {outPath}");
}
return 0;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
static Task<MdbReconciliationReport> RunReconcileAsync(
    MySqlConnection db, ILoggerFactory lf, string folder, string? mdbPassword, string tenantId)
{
    var svc = new MdbReconciliationService(db, lf.CreateLogger<MdbReconciliationService>());
    return svc.BuildAsync(folder, mdbPassword, tenantId, CancellationToken.None);
}

// MdbMigrationService 는 [SupportedOSPlatform("windows")] — 호출부를 같은 표식의 지역 함수로 감싼다(CA1416).
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
static Task<MdbMigrationResult> RunMigrateAsync(
    MySqlConnection db, ILoggerFactory lf, MigrationDbConnectionFactory f,
    string folder, string tenantId, string? mdbPassword,
    Action<string, string, int, long, string?> progress)
{
    var crypto = new BinaryCryptoServiceAdapter(new EncryptionService());
    var svc = new MdbMigrationService(db, lf.CreateLogger<MdbMigrationService>(), crypto, f);
    return svc.MigrateAsync(folder, tenantId, mdbPassword, jobId: null, progressCallback: progress, CancellationToken.None);
}
