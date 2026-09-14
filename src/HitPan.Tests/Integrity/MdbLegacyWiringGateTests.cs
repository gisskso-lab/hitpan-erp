using System.Data;
using System.Reflection;
using Dapper;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;
using FS = HitPan.Application.Services.MdbLegacyFinalStock;

// MdbMigrationService 는 [SupportedOSPlatform("windows")] 다(ACE OLEDB 의존). 이 게이트가 부르는 스텝(RunTableStepAsync ·
// 이월잔액 · 재고 마무리)은 OLEDB 를 안 쓰고, DB 게이트 잡은 ubuntu 에서 돈다 — MdbOverwriteReseedGateTests 와 같은 방식으로 해제(#19).
#pragma warning disable CA1416

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>MdbLegacyWiringGate</b> — 20260915작1 갈래 I · 신선도 가드 순수 판정 (DB 없음 · SKIP 없음).
/// </summary>
/// <remarks>
/// 진실원: 작업지시서 §14-7(통합 과제 1·2) · §14-8(PM 반증 — DOCFC 신선도) · 개발명세서 <c>docs/개발/erp/20260915작1_갈래I_통합_개발명세서.md</c>.
/// </remarks>
public sealed class MdbLegacyWiringGateTests
{
    internal static DataTable Docfb(params string[] dates)
    {
        var t = new DataTable("DOCFB");
        t.Columns.Add("IJ_DT", typeof(string));
        foreach (var d in dates) t.Rows.Add(d);
        return t;
    }

    [Fact(DisplayName = "WG-1 🔴 신선도 판정 — DOCFC 마지막 달 < DOCFB 마지막 날짜의 달이면 사유 · 같은 달·뒤 달·근거 없음은 null")]
    public void WG_1_신선도판정()
    {
        Assert.Equal(FS.StaleFinalStockReason, FS.StaleReason("202602", new DateTime(2026, 3, 1)));
        Assert.Null(FS.StaleReason("202602", new DateTime(2026, 2, 20)));   // 공영정보 실측 모양(DOCFB 마지막 20260220 · DOCFC 202602)
        Assert.Null(FS.StaleReason("202603", new DateTime(2026, 2, 20)));
        Assert.Null(FS.StaleReason((string?)null, new DateTime(2026, 3, 1)));
        Assert.Null(FS.StaleReason("202602", null));

        var docfc = MdbLegacyFinalStockGateTests.Docfc(("202601", "00", "펜", "", 1m, 1m), ("202512", "00", "종이", "", 1m, 1m));
        Assert.Equal(FS.StaleFinalStockReason, FS.StaleReason(docfc, new DateTime(2026, 2, 1)));
    }

    [Fact(DisplayName = "WG-2 🔴 DOCFB 마지막 유효 날짜 — 00000000·00000001·빈칸 제외 · 이관일 뒤(잘못 친 미래 날짜) 제외")]
    public void WG_2_마지막유효날짜()
    {
        var now = new DateTime(2026, 9, 15);
        var t = Docfb("20260110", "00000000", "00000001", "", "20260220", "20991231");
        Assert.Equal(new DateTime(2026, 2, 20), FS.LastValidLegacyDate(t, "IJ_DT", now));
        Assert.Null(FS.LastValidLegacyDate(Docfb("00000000"), "IJ_DT", now));
        Assert.Null(FS.LastValidLegacyDate(null, "IJ_DT", now));
        Assert.Null(FS.LastValidLegacyDate(t, "없는칸", now));
    }
}

/// <summary>WG DB 시험끼리 병렬로 출하 DDL 을 적재하지 않게 묶는다.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MdbLegacyWiringGateCollection
{
    public const string Name = "MdbLegacyWiringGate";
}

/// <summary>
/// 🔴 <b>MdbLegacyWiringGate — DB 분</b>. 이관 서비스의 <b>실제 스텝 메서드</b>(<c>RunTableStepAsync</c> 가 트랜잭션을 연 연결)를 격리 MariaDB 임시 DB 에서 부른다.
/// 합성 DOCF5·DOCFC·DOCFB 날짜 + 이관 원장(재고원장 스텝이 남기는 모양) → 이월잔액 스텝 → 재고 마무리(①맞춤·②리빌드·③끝전).
/// 스텝 실패는 <c>continueOnFail</c> 로 삼켜지므로 <b>오류 로그 0</b> 과 <b>표 결과</b>로 판정한다.
/// 대조군: 트랜잭션 없는 계약 시그니처를 같은 스텝 안에서 부르면 실패해야 한다(게이트가 트랜잭션 계약을 재고 있다는 증거).
/// MariaDB 없으면 로컬은 건너뛰고 <c>HITPAN_REQUIRE_DB</c> 면 실패(SKIP=FAIL). 운영 무접촉(#39).
/// </summary>
[Collection(MdbLegacyWiringGateCollection.Name)]
public sealed class MdbLegacyWiringGateDbTests : IDisposable
{
    private readonly string _dbName = "hitpan_wiring_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;

    private const string TenantId = "77777777-7777-7777-7777-777777777777";
    private const string P1 = "a1111111-0000-0000-0000-000000000001";
    private const string P2 = "a1111111-0000-0000-0000-000000000002";
    private const string P4 = "a1111111-0000-0000-0000-000000000004";
    private static readonly DateTime BaseDate = new(2026, 2, 28);
    private static readonly DateTime Now = new(2026, 9, 15, 10, 0, 0);

    // ── 준비물 (MdbLegacyFinalStockGateDbTests 와 같은 방식) ──

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

    // ── 로그 수집 (스텝 실패는 continueOnFail 로 삼켜지므로 로그로 본다) ──

    private sealed class CaptureLogger : ILogger<MdbMigrationService>
    {
        public List<(LogLevel Level, string Message, Exception? Ex)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));

        public string Errors() => string.Join("\n", Entries.Where(e => e.Level >= LogLevel.Error)
            .Select(e => $"{e.Message} :: {e.Ex?.GetType().Name} {e.Ex?.Message}"));
    }

    private static MdbMigrationService NewService(MySqlConnection db, CaptureLogger log) =>
        new(db, log, crypto: null!, migrationFactory: null);   // jobId 없음 → 암호화 저장 경로 안 탄다

    private static Task Invoke(MdbMigrationService svc, string method, params object?[] args)
    {
        var m = typeof(MdbMigrationService).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new Xunit.Sdk.XunitException($"🔴 MdbMigrationService.{method} 가 없다 — 이관 서비스 배선 메서드 이름이 바뀌었다.");
        return (Task)m.Invoke(svc, args)!;
    }

    private static object Posting(DataTable? docfc, DataTable? docf5, DateTime? docfbLast)
    {
        var t = typeof(MdbMigrationService).GetNestedType("LegacyPostingContext", BindingFlags.NonPublic)
                ?? throw new Xunit.Sdk.XunitException("🔴 LegacyPostingContext 가 없다.");
        var o = Activator.CreateInstance(t, new object?[] { null, null, null, BaseDate, docfc, docf5 })!;
        t.GetProperty("DocfbLastDate")!.SetValue(o, docfbLast);
        return o;
    }

    // ── 합성 자료 ──

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
        R(10, "20260200", "0", 100_000m, 0m, 0);
        R(10, "20260210", "0", 50_000m, 0m, 3);
        R(11, "20260100", "0", 30_000m, 0m, 0);
        R(11, "20260105", "1", 0m, 10_000m, 4);
        R(20, "20260200", "0", -80_000m, 0m, 0);
        R(30, "20260200", "0", 5_000m, 0m, 0);
        R(99, "20260200", "0", 7_000m, 0m, 0);    // 매핑 없음 → 폴백 거래처(트랜잭션 안에서 INSERT)
        return t;
    }

    private static readonly IReadOnlyDictionary<int, string> PartnerMap =
        new Dictionary<int, string> { [10] = P1, [11] = P1, [20] = P2, [30] = P4 };

    private static async Task SeedPartnersAsync(MySqlConnection db)
    {
        foreach (var (id, code) in new[] { (P1, "W1"), (P2, "W2"), (P4, "W4") })
        {
            await db.ExecuteAsync("""
                INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, is_active, is_deleted, created_at, updated_at)
                VALUES (@Id, @T, @Code, CONCAT('거래처', @Code), 'both', 1, 0, NOW(), NOW())
                """, new { Id = id, T = TenantId, Code = code });
        }
    }

    private sealed class StockSeed
    {
        public required string Wh { get; init; }
        public Dictionary<string, string> ItemMap { get; } = new(StringComparer.OrdinalIgnoreCase);   // 1단계가 채우는 이관 매핑 키 모양
    }

    private static async Task<StockSeed> SeedStockAsync(MySqlConnection db)
    {
        var wh = Guid.NewGuid().ToString();
        await db.ExecuteAsync(
            "INSERT INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, is_active, created_at, updated_at) VALUES (@W, @T, 'MAIN', '기본창고', 'normal', 1, NOW(6), NOW(6))",
            new { W = wh, T = TenantId });
        var s = new StockSeed { Wh = wh };
        var t0 = new DateTime(2026, 9, 1, 9, 0, 0);
        var sec = 0;

        async Task<string> Item(string key, string name, string? spec)
        {
            var id = Guid.NewGuid().ToString();
            await db.ExecuteAsync("""
                INSERT INTO items (item_id, tenant_id, item_code, item_name, spec, item_type, unit, tax_type, is_active, is_deleted, memo,
                                   purchase_price, sale_price, standard_price, safety_stock, created_at, updated_at, row_version)
                VALUES (@Id, @T, @Code, @Name, @Spec, 'product', 'EA', 'taxable', 1, 0, NULL, 0, 0, 0, 0, @C, @C, 0)
                """, new { Id = id, T = TenantId, Code = "WG-" + id[..8], Name = name, Spec = spec, C = t0.AddSeconds(sec++) });
            s.ItemMap[key] = id;
            return id;
        }

        Task Ledger(string item, string sourceType, string sourceId, decimal qin, decimal qout, decimal? unit) =>
            db.ExecuteAsync("""
                INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, source_id, qty_in, qty_out, unit_cost)
                VALUES (@T, @I, @W, '2026-02-10', '2026-02', @M, @S, @Sid, @In, @Out, @U)
                """, new { T = TenantId, I = item, W = wh, M = qin > 0 ? "in" : "out", S = sourceType, Sid = sourceId, In = qin, Out = qout, U = unit });

        var pen = await Item("펜", "펜", null);
        var paper = await Item("종이|A4", "종이", "A4");
        var service = await Item("용역", "용역", null);

        // 재고원장 스텝(DOCFB) 이 남긴 모양
        await Ledger(pen, "migration", "mb-1", 10m, 0m, 100m);
        await Ledger(paper, "migration", "mb-2", 5m, 0m, 200m);
        await Ledger(service, "migration", "mb-3", 100m, 0m, 1000m);
        // 사람 입력(이관 뒤 판매 출고 2) — 맞춤이 지우면 안 된다
        await Ledger(pen, "sales_delivery", "human-1", 0m, 2m, null);
        return s;
    }

    /// <summary>DOCFC 202602 — 펜 7/770 · 종이A4 3/601 · 팩스용지(DOCFC 에만 · MIG-AUTO 등록) 4/1000 · 품명 빈칸 0/0(폴백 품목).</summary>
    private static DataTable Docfc() => MdbLegacyFinalStockGateTests.Docfc(
        ("202601", "00", "펜", "", 9m, 900m),
        ("202602", "00", "펜", "", 7m, 770m),
        ("202602", "00", "종이", "A4", 3m, 601m),
        ("202602", "00", "팩스용지", "", 4m, 1000m),
        ("202602", "00", "", "", 0m, 0m));

    /// <summary>화면 금액 기대 = DOCFC 금액 합 − 사람 출고 2 × 끝전 단가 110 = 2,371 − 220.</summary>
    private const decimal ExpectedAmount = 770m + 601m + 1000m - 220m;

    private static Task<decimal> QtyAsync(MySqlConnection db, string item) =>
        db.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(current_qty),0) FROM item_stock WHERE tenant_id=@T AND item_id=@I", new { T = TenantId, I = item });

    private static Task<decimal> AmountAsync(MySqlConnection db) =>
        db.ExecuteScalarAsync<decimal>("SELECT ROUND(COALESCE(SUM(current_qty * avg_cost),0), 2) FROM item_stock WHERE tenant_id = @T", new { T = TenantId });

    private static Task<long> AdjRowsAsync(MySqlConnection db) =>
        db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM stock_ledger WHERE tenant_id=@T AND source_id LIKE 'mb-adj-%'", new { T = TenantId });

    private static string RebuildSql()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "src", "HitPan.Application", "Services", "MdbMigrationService.cs"));
        const string open = "const string rebuildSql = \"\"\"";
        var i = src.IndexOf(open, StringComparison.Ordinal);
        Assert.True(i >= 0, "🔴 MdbMigrationService.cs 에서 rebuildSql 을 못 찾았다.");
        i += open.Length;
        return src[i..src.IndexOf("\"\"\"", i, StringComparison.Ordinal)];
    }

    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "WG-E1 🔴 이월잔액 스텝 — RunTableStepAsync 열린 트랜잭션에서 오류 0 · 거래처 4행 · 옛 코드 합산 · 폴백 · 재실행 같은 값")]
    public async Task WG_E1_이월잔액_실제스텝()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(WG_E1_이월잔액_실제스텝)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SeedPartnersAsync(db);

        var log = new CaptureLogger();
        var svc = NewService(db, log);
        var result = new MdbMigrationResult();
        await Invoke(svc, "RunLegacyPartnerBalanceStepAsync", TenantId, Docf5(), PartnerMap, BaseDate, result, CancellationToken.None);

        Assert.True(log.Errors().Length == 0, $"🔴 이월잔액 스텝이 실패했다(열린 트랜잭션 계약) —\n{log.Errors()}");
        Assert.Equal(4, result.PartnerLegacyBalances);
        var rows = (await db.QueryAsync<(string PartnerId, decimal Amt)>(
            "SELECT partner_id, balance_amount FROM partner_legacy_balances WHERE tenant_id=@T", new { T = TenantId }))
            .ToDictionary(r => r.PartnerId, r => r.Amt);
        Assert.Equal(4, rows.Count);
        Assert.Equal(170_000m, rows[P1]);   // 10: 100,000+50,000 · 11: 30,000−10,000
        Assert.Equal(-80_000m, rows[P2]);
        Assert.Equal(5_000m, rows[P4]);
        var fallback = await db.ExecuteScalarAsync<string>(
            "SELECT partner_id FROM partners WHERE tenant_id=@T AND partner_code=@C", new { T = TenantId, C = MdbLegacyPartnerBalance.FallbackPartnerCode });
        Assert.True(fallback is not null && rows.TryGetValue(fallback, out var fb) && fb == 7_000m,
            "🔴 매핑 없는 옛 코드 99 가 폴백 거래처(스텝 트랜잭션 안에서 만든 행)에 안 붙었다 — 커밋 안 됐거나 트랜잭션 밖 명령.");

        await Invoke(svc, "RunLegacyPartnerBalanceStepAsync", TenantId, Docf5(), PartnerMap, BaseDate, result, CancellationToken.None);
        Assert.True(log.Errors().Length == 0, log.Errors());
        Assert.Equal(4, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM partner_legacy_balances WHERE tenant_id=@T", new { T = TenantId }));
    }

    [Fact(DisplayName = "WG-E2 대조군 — 트랜잭션 없는 계약 시그니처를 같은 스텝 안에서 부르면 실패 · 0행 (게이트가 트랜잭션 계약을 잰다)")]
    public async Task WG_E2_계약시그니처_실패()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(WG_E2_계약시그니처_실패)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SeedPartnersAsync(db);

        var log = new CaptureLogger();
        var svc = NewService(db, log);
        Func<IDbTransaction, Task<int>> work = tx =>
            MdbLegacyPartnerBalance.ApplyAsync(tx.Connection!, TenantId, Docf5(), PartnerMap, BaseDate, CancellationToken.None);
        await Invoke(svc, "RunTableStepAsync", "partner_legacy_balances", work, CancellationToken.None, true, "PANDATA");

        Assert.True(log.Errors().Length > 0,
            "대조군 실패 — 트랜잭션 없는 명령이 열린 트랜잭션 연결에서 통과했다. 이 게이트는 트랜잭션 계약을 못 잰다(연결 설정 확인).");
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM partner_legacy_balances WHERE tenant_id=@T", new { T = TenantId }));
    }

    [Fact(DisplayName = "WG-D1 🔴 재고 마무리 실제 스텝 — ①맞춤→②리빌드→③끝전 오류 0 · 수량 = DOCFC · 사람 출고 그대로 · 매핑 품목 재사용 · 금액 ROUND 2 · 재실행 0행 + 순서 대조군")]
    public async Task WG_D1_재고마무리_실제스텝()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(WG_D1_재고마무리_실제스텝)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        var seed = await SeedStockAsync(db);
        var itemsBefore = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM items WHERE tenant_id=@T", new { T = TenantId });

        var log = new CaptureLogger();
        var svc = NewService(db, log);
        var result = new MdbMigrationResult();
        var docfc = Docfc();
        await Invoke(svc, "RunLegacyStockFinalizeAsync", TenantId, Posting(docfc, null, new DateTime(2026, 2, 20)), seed.ItemMap, Now, result, CancellationToken.None);

        Assert.True(log.Errors().Length == 0, $"🔴 재고 마무리 스텝이 실패했다 —\n{log.Errors()}");
        Assert.Null(result.LegacyFinalStockSkipReason);
        Assert.True(result.LegacyFinalStockRows > 0 && result.LegacyFinalCostRows > 0,
            $"🔴 맞춤 {result.LegacyFinalStockRows}행 · 끝전 {result.LegacyFinalCostRows}행 — 스텝이 안 돌았다.");

        Assert.True(await QtyAsync(db, seed.ItemMap["펜"]) == 5m, $"🔴 펜 = {await QtyAsync(db, seed.ItemMap["펜"])} — DOCFC 7 − 사람 출고 2 = 5.");
        Assert.True(await QtyAsync(db, seed.ItemMap["종이|A4"]) == 3m, "🔴 종이 A4 가 DOCFC 3 이 아니다.");
        Assert.True(await QtyAsync(db, seed.ItemMap["용역"]) == 0m, "🔴 DOCFB 전용 품목(용역 누계 100)이 0 으로 맞춰지지 않았다.");

        // 매핑 품목 재사용: 새로 생긴 품목 = 팩스용지(MIG-AUTO) + 폴백 품목 1 = 2 (펜·종이가 또 등록되면 4)
        var itemsAfter = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM items WHERE tenant_id=@T", new { T = TenantId });
        Assert.True(itemsAfter - itemsBefore == 2,
            $"🔴 새 품목 {itemsAfter - itemsBefore}개 — 2(팩스용지 MIG-AUTO · 품명 빈칸 폴백)여야 한다. 더 많으면 1단계 매핑 품목을 안 보고 같은 이름을 새로 만들었다.");
        var fax = await db.ExecuteScalarAsync<string>("SELECT item_id FROM items WHERE tenant_id=@T AND item_name='팩스용지' AND item_code LIKE 'MIG-AUTO-%'", new { T = TenantId });
        Assert.True(fax is not null && await QtyAsync(db, fax) == 4m, "🔴 DOCFC 에만 있는 팩스용지 4 가 MIG-AUTO 품목으로 안 들어갔다.");

        var amount = await AmountAsync(db);
        Assert.True(amount == ExpectedAmount, $"🔴 재고 금액 {amount} ≠ {ExpectedAmount} — 끝전이 리빌드 뒤에 안 들어갔다(병렬이슈37).");

        // 재실행 — 맞춤 줄 0 · 금액 그대로
        var adjBefore = await AdjRowsAsync(db);
        var again = new MdbMigrationResult();
        await Invoke(svc, "RunLegacyStockFinalizeAsync", TenantId, Posting(docfc, null, new DateTime(2026, 2, 20)), seed.ItemMap, Now, again, CancellationToken.None);
        Assert.True(log.Errors().Length == 0, log.Errors());
        Assert.True(again.LegacyFinalStockRows == 0 && await AdjRowsAsync(db) == adjBefore, $"🔴 재실행이 맞춤 줄 {again.LegacyFinalStockRows}행을 더 넣었다.");
        Assert.Equal(ExpectedAmount, await AmountAsync(db));

        // 🔴 순서 대조군 — 끝전 뒤에 리빌드가 또 돌면 금액이 틀어진다(= 이 게이트가 순서를 잰다)
        await db.ExecuteAsync(RebuildSql(), new { TenantId });
        var wrong = await AmountAsync(db);
        Assert.True(wrong != ExpectedAmount, $"대조군 실패 — 리빌드가 끝전을 덮어도 금액이 {wrong} 로 같다. 순서를 못 잰다.");
    }

    [Fact(DisplayName = "WG-D2 대조군 — D 계약 시그니처(자기 트랜잭션)를 같은 스텝 안에서 부르면 실패 · 맞춤 줄 0")]
    public async Task WG_D2_계약시그니처_실패()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(WG_D2_계약시그니처_실패)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        var seed = await SeedStockAsync(db);

        var log = new CaptureLogger();
        var svc = NewService(db, log);
        var docfc = Docfc();
        Func<IDbTransaction, Task<int>> work = tx =>
            FS.AdjustStockAsync(tx.Connection!, TenantId, docfc, BaseDate,
                (pum, ku, t, c) => Task.FromResult(seed.ItemMap.TryGetValue(string.IsNullOrWhiteSpace(ku) ? pum : $"{pum}|{ku}", out var id) ? id : seed.ItemMap["펜"]),
                CancellationToken.None);
        await Invoke(svc, "RunTableStepAsync", "legacy_final_stock", work, CancellationToken.None, true, "PANDATA");

        Assert.True(log.Errors().Length > 0, "대조군 실패 — 자기 트랜잭션을 여는 계약 시그니처가 열린 트랜잭션 연결에서 통과했다.");
        Assert.Equal(0L, await AdjRowsAsync(db));
    }

    [Fact(DisplayName = "WG-D3 🔴 신선도 가드 — DOCFC 마지막 달 < DOCFB 마지막 날짜의 달 → 맞춤·끝전 skip · 사유 · 경고 · 재고 = 원장 누계(맞춤 0행)")]
    public async Task WG_D3_신선도가드_skip()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(WG_D3_신선도가드_skip)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        var seed = await SeedStockAsync(db);

        var log = new CaptureLogger();
        var svc = NewService(db, log);
        var result = new MdbMigrationResult();
        // DOCFB 에 2026-03-05 거래가 있는데 DOCFC 는 202602 에서 멈춤(월마감 안 돌림)
        await Invoke(svc, "RunLegacyStockFinalizeAsync", TenantId, Posting(Docfc(), null, new DateTime(2026, 3, 5)), seed.ItemMap, Now, result, CancellationToken.None);

        Assert.True(log.Errors().Length == 0, log.Errors());
        Assert.Equal(FS.StaleFinalStockReason, result.LegacyFinalStockSkipReason);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(FS.StaleFinalStockReason) && e.Message.Contains("LegacyFinalStockSkipReason"));
        Assert.True(await AdjRowsAsync(db) == 0, "🔴 오래된 DOCFC 인데 맞춤 줄이 들어갔다 — 최신 거래가 DOCFC 값으로 덮인다.");
        Assert.Equal(0, result.LegacyFinalStockRows);
        Assert.Equal(0, result.LegacyFinalCostRows);

        Assert.True(await QtyAsync(db, seed.ItemMap["펜"]) == 8m, "🔴 펜 = 원장 누계 10 − 2 = 8 이어야 한다.");
        Assert.True(await QtyAsync(db, seed.ItemMap["용역"]) == 100m, "🔴 용역 = 원장 누계 100 이어야 한다(가드 skip 이면 0 으로 안 맞춘다).");
        Assert.Equal(0L, await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM items WHERE tenant_id=@T AND item_name='팩스용지'", new { T = TenantId }));
    }

    public void Dispose()
    {
        if (!_created) return;
        try
        {
            using var admin = new MySqlConnection(ServerConnString());
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`;");
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[WG] 임시 DB 정리 실패 — 손으로 지워라: {_dbName} ({ex.Message})");
        }
    }
}
