using System.Data;
using Dapper;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G6 MdbReconPostingGate</b> — 20260915작1 갈래 C (대사표 「장부 반영 분류」 · 설계 §7 · §17 · 작업지시서 §11-2 C · §11-3 G6).
/// </summary>
/// <remarks>
/// 대사 서비스는 MDB(OLEDB)를 여는데 CI 에 Access 엔진이 없다 → 계산은 <see cref="MdbReconPosting"/> 에 모았고 이 게이트는
/// 합성 레거시 표(DataTable) + 격리 DB(출하 DDL)로 그 함수와 항목 조립을 부른다. 서비스 BuildAsync 가 이 함수를 부르는지는 W16(글자)이 문다.
/// <list type="bullet">
///   <item>G6a 보관 행: 머리표 X + 같은 날짜·순번 <b>수금</b> 줄만 → 보관(종류 함정) · 판매 줄 연결 → 반영 · 날짜 없음 → 보관 · 줄 = 반영 + 보관 (+구분 없음)</item>
///   <item>G6b 분류 못 함(DOCFE 없음) · 거래처원장 표 없음 알림</item>
///   <item>G6c 미수 F3 열: 마지막 이월 + 그 뒤 줄 (「마지막 이월만」·「전 줄 합」이면 FAIL)</item>
///   <item>G6d 재고 DOCFC 열: 품목별 마지막 달 · 최신 아님 알림 · 원장 입·출 LedgerMove</item>
///   <item>G6e DB: 보관 표 합계 · 명세서 줄 · leftover 경고 · 히트판 미수 = 갈래 E 식 · 재고 금액 · 원장 맞춤 줄 분리 · 품목 불일치 수 · 다른 회사 0</item>
/// </list>
/// CI(<c>db-gate</c>)는 <c>HITPAN_REQUIRE_DB</c> 로 SKIP=FAIL.
/// </remarks>
[Collection("DeviceAndKeyGate")]
public sealed class MdbReconPostingGateTests : IDisposable
{
    private readonly string _dbName = "hitpan_recon_post_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;
    private readonly string _tenant = Guid.NewGuid().ToString();
    private readonly string _other = Guid.NewGuid().ToString();

    // ── 합성 레거시 표 ──
    private static DataTable Docfb()
    {
        var t = new DataTable("DOCFB");
        foreach (var c in new[] { "IJ_DT", "IJ_IO" }) t.Columns.Add(c, typeof(string));
        t.Columns.Add("IJ_SEQ", typeof(int));
        t.Columns.Add("IJ_BUY", typeof(int));
        foreach (var c in new[] { "IJ_QTY", "IJ_AMT", "IJ_VAT" }) t.Columns.Add(c, typeof(decimal));
        void R(string dt, string io, int seq, int buy, decimal qty, decimal amt, decimal vat) => t.Rows.Add(dt, io, seq, buy, qty, amt, vat);
        R("20260210", "2", 5, 10, 3m, 1000m, 100m);   // A 머리표 O → 반영
        R("20260210", "2", 5, 10, -1m, 500m, 50m);    //   (반품 수량 → 입고 칸)
        R("20260211", "2", 6, 11, 2m, 300m, 30m);     // B 머리표 X · 같은 날짜·순번 「수금」 줄만 → 🔴 보관
        R("20260212", "2", 7, 12, 5m, 200m, 20m);     // C 머리표 X · 판매 줄 연결(다른 거래처 코드) → 반영
        R("00000000", "1", 0, 0, 4m, 0m, 1m);         // D 날짜 없음 → 보관
        R("20260105", "1", 3, 20, 10m, 800m, 80m);    // E 머리표 O → 반영
        R("20260110", "3", 1, 30, 9m, 9m, 0m);        // 구분 없음 → 어디에도 안 들어감
        return t;
    }

    private static DataTable Docfe()
    {
        var t = new DataTable("DOCFE");
        foreach (var c in new[] { "IJA_DT", "IJA_IO" }) t.Columns.Add(c, typeof(string));
        t.Columns.Add("IJA_SEQ", typeof(int));
        t.Columns.Add("IJA_BUY", typeof(int));
        t.Rows.Add("20260210", "2", 5, 10);
        t.Rows.Add("20260105", "1", 3, 20);
        return t;
    }

    private static DataTable Docf5()
    {
        var t = new DataTable("DOCF5");
        t.Columns.Add("S_BUY", typeof(int));
        t.Columns.Add("S_YMD", typeof(string));
        t.Columns.Add("S_GU", typeof(string));
        t.Columns.Add("S_BAL", typeof(decimal));
        t.Columns.Add("S_SUK", typeof(decimal));
        t.Columns.Add("S_SSUN", typeof(int));
        void R(int buy, string ymd, string gu, decimal bal, decimal suk, int ssun) => t.Rows.Add(buy, ymd, gu, bal, suk, ssun);
        R(10, "20260100", "0", 100_000m, 0m, 0);   // 옛 이월
        R(10, "20260200", "0", 150_000m, 0m, 0);   // 마지막 이월
        R(10, "20260210", "0", 1_100m, 0m, 5);     // 그 뒤 판매
        R(11, "20260211", "1", 0m, 330m, 6);       // 수금 줄 — B 와 날짜·순번 같음(연결 아님)
        R(99, "20260212", "0", 0m, 0m, 7);         // C 와 날짜·순번 같은 판매 줄(거래처 코드 다름)
        R(20, "20260200", "0", -50_000m, 0m, 0);   // 미지급
        return t;
    }

    private static DataTable Docfc()
    {
        var t = new DataTable("DOCFC");
        foreach (var c in new[] { "IM_YM", "IM_PUM", "IM_KU", "IM_CHANG" }) t.Columns.Add(c, typeof(string));
        t.Columns.Add("IM_CQTY", typeof(decimal));
        t.Columns.Add("IM_CAMT", typeof(decimal));
        t.Rows.Add("202512", "품A", "규1", "00", 99m, 9900m);   // 옛 달 — 쓰지 않는다
        t.Rows.Add("202601", "품A", "규1", "00", 10m, 1000.5m);
        t.Rows.Add("202601", "품B", "", "00", 3m, 30m);
        t.Rows.Add("202512", "품C", "", "00", 7m, 70m);         // 품목별 마지막 달
        return t;
    }

    [Fact(DisplayName = "G6a 🔴 보관 행 — 수금 줄만 같은 키는 보관 · 판매 줄 연결은 반영 · 날짜 없음 보관 · 줄 = 반영 + 보관")]
    public void G6a_PostingLegacy()
    {
        var p = MdbReconPosting.ComputePosting(Docfb(), Docfe(), Docf5());
        Assert.False(p.Unclassified);
        Assert.False(p.NoLedgerTable);
        Assert.True(p.ArchiveSales.Groups == 1, $"🔴 보관 판매 {p.ArchiveSales.Groups} ≠ 1 — 수금 줄을 연결로 봤거나 거래처 코드를 키에 넣었다");
        Assert.Equal(1, p.ArchiveSales.Lines);
        Assert.Equal(300m, p.ArchiveSales.Supply);
        Assert.Equal(30m, p.ArchiveSales.Vat);
        Assert.Equal(1, p.ArchivePurchase.Groups);
        Assert.Equal(1m, p.ArchivePurchase.Vat);
        Assert.Equal(7, p.InputLines);
        Assert.Equal(4, p.StatementLines);
        Assert.Equal(2, p.ArchivedLines);
        Assert.Equal(1, p.UnknownIoLines);
        Assert.Equal(p.InputLines, p.StatementLines + p.ArchivedLines + p.UnknownIoLines);
        Assert.Equal(new[] { "mig-docfb-00000000-1-0-0", "mig-docfb-20260211-2-6-11" }, p.ArchivedSourceIds.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal("20260212", p.LastValidDate);

        var items = new List<ReconItem>();
        var warnings = new List<string>();
        var erp = new MdbReconPosting.ArchiveErp();
        erp.Sales.Groups = 1; erp.Sales.Lines = 1; erp.Sales.Supply = 300m; erp.Sales.Vat = 30m;
        erp.Purchase.Groups = 1; erp.Purchase.Lines = 1; erp.Purchase.Supply = 0m; erp.Purchase.Vat = 1m;
        MdbReconPosting.AddPostingItems(items, warnings, p, erp, statementLines: 4, leftover: 0, err: null);
        Assert.All(items, i => Assert.True(i.Status == "OK", $"{i.Key} {i.Status} legacy={i.Legacy} erp={i.Erp}"));
        Assert.Contains(items, i => i.Key == "archive_sales_count");
        Assert.Contains(items, i => i.Key == "docfb_lines" && i.Legacy == 6m);
        Assert.Empty(warnings);

        // leftover > 0 → DIFF + 덮어쓰기 알림
        items.Clear();
        MdbReconPosting.AddPostingItems(items, warnings, p, erp, 4, leftover: 2, err: null);
        Assert.Equal("DIFF", items.Single(i => i.Key == "excluded_leftover").Status);
        Assert.Contains(warnings, w => w.Contains("덮어쓰기로 다시 가져오기가 필요합니다", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "G6b 머리표 없음 → 분류 못 함 알림 · 거래처원장 표 없음 → 머리표로만")]
    public void G6b_Unclassified_NoLedger()
    {
        var u = MdbReconPosting.ComputePosting(Docfb(), null, Docf5());
        Assert.True(u.Unclassified);
        Assert.Equal(0, u.ArchiveSales.Groups);   // 날짜 있는 묶음은 전부 반영(R5)
        Assert.Equal(1, u.ArchivePurchase.Groups); // 날짜 없는 묶음만 보관
        var w1 = new List<string>();
        MdbReconPosting.AddPostingItems(new List<ReconItem>(), w1, u, null, null, null, null);
        Assert.Contains(MdbReconPosting.WarnUnclassified, w1);

        var n = MdbReconPosting.ComputePosting(Docfb(), Docfe(), null);
        Assert.True(n.NoLedgerTable);
        Assert.Equal(2, n.ArchiveSales.Groups);    // C 는 연결로 살릴 수 없다
        var w2 = new List<string>();
        MdbReconPosting.AddPostingItems(new List<ReconItem>(), w2, n, null, null, null, null);
        Assert.Contains(MdbReconPosting.WarnNoLedgerTable, w2);

        Assert.Null(MdbReconPosting.ComputeBalanceLegacy(null));
        var items = new List<ReconItem>();
        var w3 = new List<string>();
        MdbReconPosting.AddBalanceItems(items, w3, null, docf5Missing: true, erp: new MdbReconPosting.BalanceErp(), err: null);
        Assert.Equal("NA", items.Single(i => i.Key == "receivable").Status);
        Assert.Contains(MdbReconPosting.WarnNoLedgerTable, w3);
    }

    [Fact(DisplayName = "G6c 🔴 미수 F3 열 — 마지막 이월 + 그 뒤 줄 (마지막 이월만·전 줄 합이면 FAIL)")]
    public void G6c_BalanceLegacy_F3()
    {
        var b = MdbReconPosting.ComputeBalanceLegacy(Docf5())!;
        Assert.True(b.Receivable == 151_100m, $"🔴 레거시 미수 {b.Receivable:N0} ≠ 151,100 — 150,000 이면 「마지막 이월만」 · 251,100 이면 전 줄 합");
        Assert.Equal(1, b.ReceivableCount);
        Assert.Equal(50_330m, b.Payable);
        Assert.Equal(2, b.PayableCount);
    }

    [Fact(DisplayName = "G6d 재고 DOCFC 최종 열 · 최신 아님 알림 · 원장 입·출 LedgerMove")]
    public void G6d_StockLegacy()
    {
        var ledger = MdbReconPosting.ComputeLedgerLegacy(Docfb());
        Assert.Equal(6, ledger.Rows);
        Assert.True(ledger.QtyIn == 15m && ledger.QtyOut == 10m, $"🔴 입 {ledger.QtyIn} 출 {ledger.QtyOut} — 음수 수량을 반대 칸 절대값으로 안 넣었다(15/10 기대)");
        Assert.Equal(2800m, ledger.Amount);

        var final = MdbLegacyFinalStock.ComputeFinalStock(Docfc())!;
        Assert.NotNull(MdbReconPosting.FinalStockStaleReason(final.MaxYm, "20260212"));
        Assert.Null(MdbReconPosting.FinalStockStaleReason("202602", "20260212"));

        var erpRows = new List<MdbReconPosting.StockErpRow>
        {
            new() { ItemName = "품A", Spec = "규1", Qty = 10m, Amount = 1000.5m },
            new() { ItemName = "품B", Spec = null, Qty = 3m, Amount = 30m },
            new() { ItemName = "품C", Spec = null, Qty = 6m, Amount = 60m },
        };
        var items = new List<ReconItem>();
        var diffs = new List<ReconDiffRow>();
        var warnings = new List<string>();
        MdbReconPosting.AddStockItems(items, diffs, warnings, final, false, erpRows, ledger, null, "20260212", null);
        var qty = items.Single(i => i.Key == "stock_qty");
        Assert.True(qty.Legacy == 20m, $"🔴 레거시 재고 {qty.Legacy} ≠ 20 (품목별 마지막 달 10+3+7) — 99 가 섞였으면 옛 달 · DOCFB 이력이면 자기 자신과 비교");
        Assert.Equal(1100.5m, items.Single(i => i.Key == "stock_amount").Legacy);
        Assert.Equal(1m, items.Single(i => i.Key == "stock_item_mismatch").Erp);
        Assert.Equal("품C", Assert.Single(diffs).Name);
        Assert.Contains(warnings, w => w.StartsWith("최종재고 표가 최신이 아닙니다", StringComparison.Ordinal));
        Assert.Null(items.Single(i => i.Key == "stock_adjust_net").Legacy);   // 최신 아니면 맞춤 기대값을 내지 않는다
    }

    // ── DB ──
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다.");
    }

    private static string ServerConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};User={user};Password={pass};DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
    }

    private string DbConnString() => ServerConnString().Replace("User=", $"Database={_dbName};User=");

    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL") ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;
        if (!File.Exists(MysqlExe())) return false;
        try
        {
            using var c = new MySqlConnection(ServerConnString());
            c.Open();
            return true;
        }
        catch (MySqlException)
        {
            return false;
        }
    }

    private void SetUpFreshInstall()
    {
        var ddlPath = Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql");
        using (var admin = new MySqlConnection(ServerConnString()))
        {
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`; CREATE DATABASE `{_dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }
        _created = true;
        var psi = new System.Diagnostics.ProcessStartInfo(MysqlExe())
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = new System.Text.UTF8Encoding(false)
        };
        psi.ArgumentList.Add($"--host={Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost"}");
        psi.ArgumentList.Add($"--port={Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306"}");
        psi.ArgumentList.Add($"-u{Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root"}");
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS");
        if (!string.IsNullOrEmpty(pass)) psi.ArgumentList.Add($"-p{pass}");
        psi.ArgumentList.Add("--default-character-set=utf8mb4");
        psi.ArgumentList.Add(_dbName);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardInput.Write(File.ReadAllText(ddlPath));
        proc.StandardInput.Close();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Assert.True(proc.ExitCode == 0, $"출하 DDL import 실패:\n{err}");
    }

    public void Dispose()
    {
        if (!_created) return;
        try
        {
            using var admin = new MySqlConnection(ServerConnString());
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`");
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[G6] 임시 DB 삭제 실패 {_dbName}: {ex.Message}");
        }
    }

    private async Task SeedAsync(MySqlConnection db, string tenant)
    {
        // 거래처 2 · 이월잔액(+151,100 · −50,330)
        await db.ExecuteAsync("""
            INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, is_active, is_deleted, created_at, updated_at)
            VALUES (CONCAT(LEFT(@T,30),'-p1'), @T, 'MIG-00010', '가나', 'both', 1, 0, NOW(6), NOW(6)),
                   (CONCAT(LEFT(@T,30),'-p2'), @T, 'MIG-00020', '다라', 'both', 1, 0, NOW(6), NOW(6))
            """, new { T = tenant });
        await db.ExecuteAsync("""
            INSERT INTO partner_legacy_balances (balance_id, tenant_id, partner_id, legacy_buy_code, base_date, balance_amount, source_type, source_id)
            VALUES (UUID(), @T, CONCAT(LEFT(@T,30),'-p1'), 10, '2026-02-28', 151100, 'migration', CONCAT('b1-', @T)),
                   (UUID(), @T, CONCAT(LEFT(@T,30),'-p2'), 20, '2026-02-28', -50330, 'migration', CONCAT('b2-', @T))
            """, new { T = tenant });
        // 이관 명세서 — 반영 A(줄 2) + 🔴 봉합 전에 들어온 옛 명세서 B(보관 대상 · 줄 1) · 매입 E(줄 1)
        foreach (var (id, src, amt, lines) in new[] { ("a", "mig-docfb-20260210-2-5-10", 1650m, 2), ("b", "mig-docfb-20260211-2-6-11", 330m, 1) })
        {
            await db.ExecuteAsync("""
                INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, partner_id, delivery_date, source_type, source_id, status, total_amount, vat_amount, is_deleted, created_at, updated_at)
                VALUES (CONCAT(LEFT(@T,30),'-d',@Id), @T, CONCAT('D',@Id), CONCAT(LEFT(@T,30),'-p1'), '2026-02-10', 'migration', @Src, 'confirmed', @A, 0, 0, NOW(6), NOW(6))
                """, new { T = tenant, Id = id, Src = src, A = amt });
            for (var i = 0; i < lines; i++)
                await db.ExecuteAsync("""
                    INSERT INTO sales_delivery_items (delivery_item_id, delivery_id, tenant_id, qty, unit_price, supply_amount, vat_amount)
                    VALUES (UUID(), CONCAT(LEFT(@T,30),'-d',@Id), @T, 1, 1, 1, 0)
                    """, new { T = tenant, Id = id });
        }
        await db.ExecuteAsync("""
            INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, partner_id, receipt_date, source_type, source_id, status, total_amount, vat_amount, created_at)
            VALUES (CONCAT(LEFT(@T,30),'-r1'), @T, 'R1', CONCAT(LEFT(@T,30),'-p2'), '2026-01-05', 'migration', 'mig-docfb-20260105-1-3-20', 'confirmed', 880, 0, NOW(6));
            INSERT INTO purchase_receipt_items (receipt_item_id, receipt_id, tenant_id, qty, unit_price, supply_amount, vat_amount)
            VALUES (UUID(), CONCAT(LEFT(@T,30),'-r1'), @T, 1, 1, 1, 0);
            """, new { T = tenant });
        // 보관 표 — 판매 B · 매입 D
        await db.ExecuteAsync("""
            INSERT INTO legacy_unposted_documents (doc_id, tenant_id, io_type, doc_date, legacy_dt, legacy_seq, legacy_buy_code, reason, supply_amount, vat_amount, line_count, source_id)
            VALUES (CONCAT(LEFT(@T,30),'-ub'), @T, 'sales', '2026-02-11', '20260211', 6, 11, 'other', 300, 30, 1, 'mig-docfb-20260211-2-6-11'),
                   (CONCAT(LEFT(@T,30),'-ud'), @T, 'purchase', '2026-02-28', '00000000', 0, 0, 'danga', 0, 1, 1, 'mig-docfb-00000000-1-0-0');
            INSERT INTO legacy_unposted_document_lines (line_id, tenant_id, doc_id, line_no, qty, unit_price, supply_amount, vat_amount, migrated_source_hash)
            VALUES (UUID(), @T, CONCAT(LEFT(@T,30),'-ub'), 1, 2, 150, 300, 30, SHA2(CONCAT(@T,'b'),256)),
                   (UUID(), @T, CONCAT(LEFT(@T,30),'-ud'), 1, 4, 0, 0, 1, SHA2(CONCAT(@T,'d'),256));
            """, new { T = tenant });
        // 품목 · 재고(품C 는 6 — 레거시 7 과 불일치) · 원장 이력 1행 + 맞춤 줄 1행
        foreach (var (code, name, spec, qty, cost) in new[] { ("A", "품A", "규1", 10m, 100.05m), ("B", "품B", (string?)null, 3m, 10m), ("C", "품C", null, 6m, 10m) })
        {
            await db.ExecuteAsync("""
                INSERT INTO items (item_id, tenant_id, item_code, item_name, spec, item_type, unit, is_active, is_deleted, created_at, updated_at)
                VALUES (CONCAT(LEFT(@T,30),'-i',@C), @T, CONCAT('I',@C), @N, @S, 'goods', 'EA', 1, 0, NOW(6), NOW(6));
                INSERT INTO item_stock (stock_id, tenant_id, item_id, warehouse_id, current_qty, avg_cost, last_updated_at)
                VALUES (UUID(), @T, CONCAT(LEFT(@T,30),'-i',@C), 'WH', @Q, @Cost, NOW(6));
                """, new { T = tenant, C = code, N = name, S = spec, Q = qty, Cost = cost });
        }
        await db.ExecuteAsync("""
            INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, source_id, qty_in, qty_out, supply_amount, migrated_source_hash)
            VALUES (@T, CONCAT(LEFT(@T,30),'-iA'), 'WH', '2026-02-10', '2026-02', 'out', 'migration', 'mb-20260210-2-5-10-1', 0, 3, 1000, SHA2(CONCAT(@T,'l1'),256)),
                   (@T, CONCAT(LEFT(@T,30),'-iA'), 'WH', '2026-02-28', '2026-02', 'in', 'migration', 'mb-adj-202601', 13, 0, NULL, SHA2(CONCAT(@T,'l2'),256))
            """, new { T = tenant });
    }

    [Fact(DisplayName = "G6e 🔴 DB — 보관 표 행 · 명세서 줄 · leftover 경고 · 미수 갈래 E 식 · 재고 금액 · 맞춤 줄 분리 · 다른 회사 0")]
    public async Task G6e_ErpColumns()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(G6e_ErpColumns)); return; }
        SetUpFreshInstall();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SeedAsync(db, _tenant);
        await SeedAsync(db, _other);   // 다른 회사 같은 모양 — 하나라도 섞이면 두 배가 된다

        var ct = CancellationToken.None;
        var archive = await MdbReconPosting.ReadArchiveAsync(db, _tenant, ct);
        Assert.Equal(1, archive.Sales.Groups);
        Assert.Equal(1, archive.Sales.Lines);
        Assert.Equal(300m, archive.Sales.Supply);
        Assert.Equal(1, archive.Purchase.Groups);
        Assert.Equal(1m, archive.Purchase.Vat);

        Assert.Equal(4, await MdbReconPosting.CountStatementLinesAsync(db, _tenant, ct));

        var posting = MdbReconPosting.ComputePosting(Docfb(), Docfe(), Docf5());
        var leftover = await MdbReconPosting.CountLeftoverAsync(db, _tenant, posting.ArchivedSourceIds, ct);
        Assert.True(leftover == 1, $"🔴 봉합 전 옛 명세서 {leftover} ≠ 1");
        var items = new List<ReconItem>();
        var warnings = new List<string>();
        MdbReconPosting.AddPostingItems(items, warnings, posting, archive, 4, leftover, null);
        Assert.Equal("DIFF", items.Single(i => i.Key == "excluded_leftover").Status);
        Assert.Contains(warnings, w => w.Contains("덮어쓰기로 다시 가져오기가 필요합니다", StringComparison.Ordinal));
        Assert.Equal("OK", items.Single(i => i.Key == "archive_sales_supply").Status);
        // 줄: 레거시 6(반영 4 + 보관 2) ↔ 히트판 명세서 4 + 보관 2
        Assert.Equal("OK", items.Single(i => i.Key == "docfb_lines").Status);

        // 미수·미지급 — 이월잔액이 있으면 이관 명세서는 빼고 잔액을 더한다(갈래 E) → F3 와 같다
        var bal = await MdbReconPosting.ReadBalanceErpAsync(db, _tenant, ct);
        var legacyBal = MdbReconPosting.ComputeBalanceLegacy(Docf5())!;
        Assert.True(bal.Receivable == legacyBal.Receivable, $"🔴 히트판 미수 {bal.Receivable:N0} ≠ F3 {legacyBal.Receivable:N0} — 이관 명세서를 다시 셌다");
        Assert.Equal(legacyBal.Payable, bal.Payable);
        var bItems = new List<ReconItem>();
        MdbReconPosting.AddBalanceItems(bItems, new List<string>(), legacyBal, false, bal, null);
        Assert.All(bItems, i => Assert.Equal("OK", i.Status));

        // 재고 · 원장
        var stock = await MdbReconPosting.ReadStockErpAsync(db, _tenant, ct);
        Assert.Equal(1090.5m, Math.Round(stock.Sum(s => s.Amount), 2));
        var ledger = await MdbReconPosting.ReadLedgerErpAsync(db, _tenant, ct);
        Assert.True(ledger.HistRows == 1 && ledger.AdjRows == 1, $"🔴 원장 이력 {ledger.HistRows} · 맞춤 {ledger.AdjRows} — 맞춤 줄을 이력에 섞었다");
        Assert.Equal(3m, ledger.HistOut);
        Assert.Equal(13m, ledger.AdjIn);

        var sItems = new List<ReconItem>();
        var diffs = new List<ReconDiffRow>();
        var final = MdbLegacyFinalStock.ComputeFinalStock(Docfc())!;
        MdbReconPosting.AddStockItems(sItems, diffs, new List<string>(), final, false, stock, MdbReconPosting.ComputeLedgerLegacy(Docfb()), ledger, "20260131", null);
        Assert.Equal(1m, sItems.Single(i => i.Key == "stock_item_mismatch").Erp);
        Assert.Equal(-10m, sItems.Single(i => i.Key == "stock_amount").Diff);   // 1090.5 − 1100.5
        Assert.Equal(20m - (15m - 10m), sItems.Single(i => i.Key == "stock_adjust_net").Legacy);
    }
}
