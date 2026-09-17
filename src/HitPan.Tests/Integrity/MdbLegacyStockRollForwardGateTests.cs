using System.Data;
using System.Reflection;
using Dapper;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;
using A = HitPan.Application.Services.MdbLegacyUnpostedArchive;
using FS = HitPan.Application.Services.MdbLegacyFinalStock;

// MdbMigrationService 는 [SupportedOSPlatform("windows")] 다(ACE OLEDB 의존). 이 게이트가 부르는 재고 마무리 스텝은 OLEDB 를 안 쓴다
// — MdbLegacyWiringGateTests 와 같은 방식으로 해제(#19).
#pragma warning disable CA1416

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G10 MdbLegacyStockRollForwardGate</b> — 20260915작1 개정 3판 갈래 R3 · 재고 이어 계산 순수 판정 (DB 없음 · SKIP 없음).
/// </summary>
/// <remarks>
/// 진실원: 작업지시서 §15-5 · 설계 §23·§24·§25(G10) · 선행검증 20260917 B-2 · 개발명세서
/// <c>docs/개발/erp/20260917작1_갈래R3_재고이어계산_기준일_개발명세서.md</c>.
/// </remarks>
public sealed class MdbLegacyStockRollForwardGateTests
{
    private static readonly IReadOnlyDictionary<string, decimal> NoMaster = new Dictionary<string, decimal>(StringComparer.Ordinal);

    internal static DataTable Docfc(params (string Ym, string Chang, string Pum, string Ku, decimal Qty, decimal Amt, decimal Dan)[] rows)
    {
        var t = new DataTable("DOCFC");
        t.Columns.Add("IM_YM", typeof(string));
        t.Columns.Add("IM_CHANG", typeof(string));
        t.Columns.Add("IM_PUM", typeof(string));
        t.Columns.Add("IM_KU", typeof(string));
        t.Columns.Add("IM_CQTY", typeof(decimal));
        t.Columns.Add("IM_CAMT", typeof(decimal));
        t.Columns.Add("IM_DAN", typeof(decimal));
        foreach (var r in rows) t.Rows.Add(r.Ym, r.Chang, r.Pum, r.Ku, r.Qty, r.Amt, r.Dan);
        return t;
    }

    internal static FS.LegacyStockMove Mv(string date, string io, string pum, decimal qty, decimal amt, string ku = "", string chang = "00")
        => new(pum, ku, chang, DateTime.ParseExact(date, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture), io, qty, amt);

    private static FS.FinalStockLine Line(FS.RollForwardResult r, string pum, string ku = "")
        => r.Final.Lines.Single(l => l.Key == LegacyMdbMapping.StockItemKey(pum, ku));

    [Fact(DisplayName = "G10-1 🔴 규칙③ 입고 있는 달 = 월 총평균 (기초+입고)/(기초수량+입고수량) · 기말 = ROUND(수량×단가,0)")]
    public void G10_1_입고달_월총평균()
    {
        var docfc = Docfc(("202512", "00", "펜", "", 10m, 1000m, 100m));
        var r = FS.RollForward(docfc, new[]
        {
            Mv("20260105", "1", "펜", 10m, 1200m),
            Mv("20260110", "2", "펜", 5m, 999_999m),   // 출고 금액은 안 쓴다
            Mv("20260111", "1", "펜", 1m, 7m),
        }, NoMaster, new DateTime(2026, 1, 31))!;
        // 단가 = (1000+1207)/(10+11) = 105.0952… · 기말 16 × 105.0952… = 1681.52… → 1682
        var pen = Line(r, "펜");
        Assert.Equal(16m, pen.Qty);
        Assert.Equal(1682m, pen.Amount);
    }

    [Fact(DisplayName = "G10-2 🔴 입고 없는 달(출고만) = 상품마스터 S_IDAN · 0/없으면 직전 단가 · (기초+입고)수량 0 이면 마스터")]
    public void G10_2_입고없는달_마스터단가()
    {
        var docfc = Docfc(
            ("202512", "00", "펜", "", 10m, 1000m, 100m),
            ("202512", "00", "연필", "", 10m, 500m, 50m),
            ("202512", "00", "지우개", "", -2m, -60m, 30m));
        var master = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [LegacyMdbMapping.StockItemKey("펜", "")] = 130m,
            [LegacyMdbMapping.StockItemKey("연필", "")] = 0m,
            [LegacyMdbMapping.StockItemKey("지우개", "")] = 40m,
        };
        var r = FS.RollForward(docfc, new[]
        {
            Mv("20260105", "2", "펜", 4m, 0m),
            Mv("20260105", "2", "연필", 4m, 0m),
            Mv("20260106", "1", "지우개", 2m, 80m),   // 기초 -2 + 입고 2 = 0 → 나눌 수 없음 → 마스터 40
            Mv("20260107", "2", "지우개", -1m, 0m),
        }, master, new DateTime(2026, 1, 31))!;
        Assert.Equal(6m * 130m, Line(r, "펜").Amount);
        Assert.Equal(6m * 50m, Line(r, "연필").Amount);
        Assert.Equal(1m, Line(r, "지우개").Qty);
        Assert.Equal(40m, Line(r, "지우개").Amount);
    }

    [Fact(DisplayName = "G10-3 🔴 반품(IO2 음수) = 출고수량만 줄이고 금액 안 씀 — 「반품 = 입고 절대값」이면 FAIL")]
    public void G10_3_반품은_출고수량만()
    {
        var docfc = Docfc(("202512", "00", "충전", "", 5m, 5000m, 1000m));
        var r = FS.RollForward(docfc, new[]
        {
            Mv("20260103", "2", "충전", -1m, -87_818m),   // 판매가로 잡힌 반품 1개
        }, NoMaster, new DateTime(2026, 1, 31))!;
        var line = Line(r, "충전");
        Assert.Equal(6m, line.Qty);
        // 입고 절대값(1개 · 87,818원)으로 세면 단가 = (5000+87818)/6 = 15,469.67 → 92,818
        Assert.True(line.Amount == 6_000m, $"🔴 반품이 금액을 바꿨다 — {line.Amount} (기대 6,000 = 6 × 직전 단가 1,000)");
    }

    [Fact(DisplayName = "G10-4 🔴 움직임 없는 달 = 기말 그대로(끝전 포함) · 수량 0 금액만 남은 키(P/L) → 0 · 수량 0 금액 줄은 빼고 건수·금액 기록")]
    public void G10_4_움직임없는달_수량0()
    {
        var docfc = Docfc(
            ("202512", "00", "기타", "", -6m, -5454.6m, 909.1m),
            ("202512", "00", "P/L", "", 0m, 23_096_686m, 0m),
            ("202512", "00", "도메인", "", 1m, 21_000m, 21_000m));
        var master = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [LegacyMdbMapping.StockItemKey("도메인", "")] = 24_000m,   // 움직임 없는 달엔 마스터로 다시 재지 않는다
        };
        var r = FS.RollForward(docfc, new[]
        {
            Mv("20260110", "2", "P/L", 0m, -4_454_610m),
            Mv("20260210", "1", "P/L", 0m, 1_000m),
        }, master, new DateTime(2026, 2, 28))!;
        Assert.Equal(-5454.6m, Line(r, "기타").Amount);
        Assert.Equal(21_000m, Line(r, "도메인").Amount);
        Assert.Equal(0m, Line(r, "P/L").Amount);
        Assert.Equal(2, r.Info.ZeroQtyAmountLines);
        Assert.Equal(-4_453_610m, r.Info.ZeroQtyAmount);
        Assert.Equal(0, r.Info.MoveLines);
        Assert.Equal(2, r.Info.CarryMonths);
    }

    [Fact(DisplayName = "G10-5 구간 — DOCFC 마지막 달 이하·기준일 뒤 줄 제외 · DOCFB 에만 있는 키 0 에서 시작 · 창고 합산 · MaxYm = 기준일 달")]
    public void G10_5_구간_새키_창고()
    {
        var docfc = Docfc(
            ("202511", "00", "펜", "", 3m, 300m, 100m),
            ("202512", "00", "펜", "", 10m, 1000m, 100m),
            ("202512", "02", "펜", "", 2m, 200m, 100m));
        var r = FS.RollForward(docfc, new[]
        {
            Mv("20251231", "1", "펜", 100m, 100m),     // DOCFC 달 안 → 제외
            Mv("20260301", "1", "펜", 100m, 100m),     // 기준일 뒤 → 제외
            Mv("20260215", "1", "용역", 3m, 300m),     // DOCFB 에만
            Mv("20260216", "2", "펜", 1m, 0m, chang: "02"),
        }, NoMaster, new DateTime(2026, 2, 28))!;
        Assert.Equal("202602", r.Final.MaxYm);
        Assert.Equal(("202512", "202602", 2), (r.Info.FromYm, r.Info.ToYm, r.Info.CarryMonths));
        Assert.Equal(2, r.Info.MoveLines);
        Assert.Equal(11m, Line(r, "펜").Qty);
        Assert.Equal(1100m, Line(r, "펜").Amount);
        Assert.Equal(2, Line(r, "펜").WarehouseLines);
        Assert.Equal(1, r.Final.NonDefaultWarehouseLines);
        Assert.Equal((3m, 300m), (Line(r, "용역").Qty, Line(r, "용역").Amount));
        Assert.Null(FS.RollForward(null, Array.Empty<FS.LegacyStockMove>(), NoMaster, new DateTime(2026, 2, 28)));
    }

    [Fact(DisplayName = "G10-6 🔴 기준일 = min(DOCFB 마지막 달 말일, 이관일) 양방향")]
    public void G10_6_기준일_min()
    {
        Assert.Equal(new DateTime(2026, 2, 28), FS.CarryBaseDate(new DateTime(2026, 2, 20), new DateTime(2026, 9, 17)));
        Assert.Equal(new DateTime(2026, 9, 17), FS.CarryBaseDate(new DateTime(2026, 9, 10), new DateTime(2026, 9, 17, 8, 30, 0)));
        Assert.Equal(new DateTime(2024, 2, 29), FS.CarryBaseDate(new DateTime(2024, 2, 1), new DateTime(2024, 3, 1)));
    }

    [Fact(DisplayName = "G10-7 대조군 — 최신 DOCFC(같은 달) 는 가드 미발동 · 이어 계산 0개월 = DOCFC 최종과 같다")]
    public void G10_7_최신DOCFC_대조군()
    {
        var docfc = Docfc(("202602", "00", "펜", "", 7m, 770m, 110m));
        Assert.Null(FS.StaleReason(docfc, new DateTime(2026, 2, 20)));
        var r = FS.RollForward(docfc, new[] { Mv("20260220", "2", "펜", 1m, 0m) }, NoMaster, new DateTime(2026, 2, 28))!;
        var final = FS.ComputeFinalStock(docfc)!;
        Assert.Equal(0, r.Info.CarryMonths);
        Assert.Equal((final.MaxYm, final.Lines.Single().Qty, final.Lines.Single().Amount), (r.Final.MaxYm, Line(r, "펜").Qty, Line(r, "펜").Amount));
    }

    [Fact(DisplayName = "G10-8 S1 안내 문구 — 개월 수 자리 1개 · 결재 문구 그대로")]
    public void G10_8_안내문구()
    {
        Assert.Equal("최종재고 표 뒤 2개월은 거래명세서 입출고로 이어 계산했습니다. 창고이동 등 특수입출고는 반영되지 않았을 수 있습니다",
            string.Format(System.Globalization.CultureInfo.InvariantCulture, FS.RollForwardNotice, 2));
    }

    private static DataTable TaxRows(params string[] nos)
    {
        var t = new DataTable("DOCFB");
        t.Columns.Add("IJ_TAXNO", typeof(string));
        foreach (var n in nos) t.Rows.Add(n);
        return t;
    }

    [Fact(DisplayName = "G10-9 🔴 99999999 실번호 우선 3사례 — 99999999 가 앞줄이어도 실번호 · 실번호 없으면 99999999 · 둘 다 없으면 0")]
    public void G10_9_실번호우선()
    {
        Assert.Equal(12345678, A.LegacyTaxNo(TaxRows("00000000", "99999999", "12345678").Rows.Cast<DataRow>()));
        Assert.Equal(99999999, A.LegacyTaxNo(TaxRows("99999999", "00000000", "").Rows.Cast<DataRow>()));
        Assert.Equal(0, A.LegacyTaxNo(TaxRows("00000000", "").Rows.Cast<DataRow>()));
    }
}

/// <summary>G10 DB 시험끼리 병렬로 출하 DDL 을 적재하지 않게 묶는다.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MdbLegacyStockRollForwardGateCollection
{
    public const string Name = "MdbLegacyStockRollForwardGate";
}

/// <summary>
/// 🔴 <b>G10 MdbLegacyStockRollForwardGate — DB 분</b>. 이관 서비스의 실제 재고 마무리 메서드(<c>RunLegacyStockFinalizeAsync</c>)를
/// 격리 MariaDB 임시 DB(<c>hitpan_r3_*</c> · 끝나면 DROP)에서 부른다. 가드 발동 + 이어 계산 자료 → 맞춤 호출 · SkipReason null · 정보 기록.
/// MariaDB 없으면 로컬은 건너뛰고 <c>HITPAN_REQUIRE_DB</c> 면 실패(SKIP=FAIL). 운영 무접촉(#39).
/// </summary>
[Collection(MdbLegacyStockRollForwardGateCollection.Name)]
public sealed class MdbLegacyStockRollForwardGateDbTests : IDisposable
{
    private readonly string _dbName = "hitpan_r3_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;

    private const string TenantId = "73737373-7373-7373-7373-737373737373";
    private static readonly DateTime Now = new(2026, 9, 17, 10, 0, 0);
    private static readonly DateTime CarryBase = new(2026, 1, 31);

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

    private static Task Invoke(MdbMigrationService svc, string method, params object?[] args)
    {
        var m = typeof(MdbMigrationService).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new Xunit.Sdk.XunitException($"🔴 MdbMigrationService.{method} 가 없다.");
        return (Task)m.Invoke(svc, args)!;
    }

    /// <summary>가드 발동 모양(DOCFC 202512 · DOCFB 마지막 2026-01-20) · 기준일 = 이어 계산 기준일. moves null = 이어 계산 자료 안 읽음.</summary>
    private static object Posting(DataTable docfc, IReadOnlyList<FS.LegacyStockMove>? moves)
    {
        var t = typeof(MdbMigrationService).GetNestedType("LegacyPostingContext", BindingFlags.NonPublic)
                ?? throw new Xunit.Sdk.XunitException("🔴 LegacyPostingContext 가 없다.");
        var o = Activator.CreateInstance(t, new object?[] { null, null, null, CarryBase, docfc, null })!;
        t.GetProperty("DocfbLastDate")!.SetValue(o, new DateTime(2026, 1, 20));
        var movesProp = t.GetProperty("StockMovesAfter")
                        ?? throw new Xunit.Sdk.XunitException("🔴 LegacyPostingContext.StockMovesAfter 가 없다 — 이어 계산 자료를 넘길 자리가 없다.");
        movesProp.SetValue(o, moves);
        t.GetProperty("MasterIdanByItemKey")!.SetValue(o, new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [LegacyMdbMapping.StockItemKey("연필", "")] = 100m,
        });
        return o;
    }

    private static DataTable Docfc() => MdbLegacyStockRollForwardGateTests.Docfc(("202512", "00", "펜", "", 10m, 1000m, 100m));

    /// <summary>202601: 펜 입고 10(1,200원) · 출고 5 → 15 · 단가 110 · 1,650 / 연필(DOCFB 에만) 입고 3(300원) → 3 · 100 · 300.</summary>
    private static IReadOnlyList<FS.LegacyStockMove> Moves() => new[]
    {
        MdbLegacyStockRollForwardGateTests.Mv("20260105", "1", "펜", 10m, 1200m),
        MdbLegacyStockRollForwardGateTests.Mv("20260110", "2", "펜", 5m, 0m),
        MdbLegacyStockRollForwardGateTests.Mv("20260120", "1", "연필", 3m, 300m),
    };

    private sealed record Seed(string Pen, string Pencil, Dictionary<string, string> ItemMap);

    private static async Task<Seed> SeedAsync(MySqlConnection db)
    {
        var wh = Guid.NewGuid().ToString();
        await db.ExecuteAsync(
            "INSERT INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, is_active, created_at, updated_at) VALUES (@W, @T, 'MAIN', '기본창고', 'normal', 1, NOW(6), NOW(6))",
            new { W = wh, T = TenantId });
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        async Task<string> Item(string name, int sec)
        {
            var id = Guid.NewGuid().ToString();
            await db.ExecuteAsync("""
                INSERT INTO items (item_id, tenant_id, item_code, item_name, spec, item_type, unit, tax_type, is_active, is_deleted, memo,
                                   purchase_price, sale_price, standard_price, safety_stock, created_at, updated_at, row_version)
                VALUES (@Id, @T, @Code, @Name, NULL, 'product', 'EA', 'taxable', 1, 0, NULL, 0, 0, 0, 0, @C, @C, 0)
                """, new { Id = id, T = TenantId, Code = "R3-" + id[..8], Name = name, C = new DateTime(2026, 9, 1, 9, 0, sec) });
            map[name] = id;
            return id;
        }
        var pen = await Item("펜", 0);
        var pencil = await Item("연필", 1);
        // 재고원장 스텝이 남긴 이력 누계(단가 50) — 펜 18 · 연필 3
        foreach (var (item, sid, qin) in new[] { (pen, "mb-1", 18m), (pencil, "mb-2", 3m) })
        {
            await db.ExecuteAsync("""
                INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, source_id, qty_in, qty_out, unit_cost)
                VALUES (@T, @I, @W, '2026-01-10', '2026-01', 'in', 'migration', @S, @Q, 0, 50)
                """, new { T = TenantId, I = item, W = wh, S = sid, Q = qin });
        }
        return new Seed(pen, pencil, map);
    }

    private static Task<decimal> QtyAsync(MySqlConnection db, string item) =>
        db.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(current_qty),0) FROM item_stock WHERE tenant_id=@T AND item_id=@I", new { T = TenantId, I = item });

    private static Task<decimal> AmountAsync(MySqlConnection db) =>
        db.ExecuteScalarAsync<decimal>("SELECT ROUND(COALESCE(SUM(current_qty * avg_cost),0), 2) FROM item_stock WHERE tenant_id = @T", new { T = TenantId });

    [Fact(DisplayName = "G10-D1 🔴 가드 발동 + 이어 계산 자료 → skip 아님 · 맞춤 호출(mb-adj-202601 · 기준일) · 수량·금액 = 이어 계산 · 정보 기록")]
    public async Task G10_D1_발동시_이어계산_맞춤()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(G10_D1_발동시_이어계산_맞춤)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        var seed = await SeedAsync(db);
        var log = new CaptureLogger();
        var svc = new MdbMigrationService(db, log, crypto: null!, migrationFactory: null);
        var result = new MdbMigrationResult();

        await Invoke(svc, "RunLegacyStockFinalizeAsync", TenantId, Posting(Docfc(), Moves()), seed.ItemMap, Now, result, CancellationToken.None);

        Assert.True(log.Errors().Length == 0, $"🔴 재고 마무리 스텝 실패 —\n{log.Errors()}");
        Assert.True(result.LegacyFinalStockSkipReason is null, $"🔴 이어 계산 자료가 있는데 skip 했다 — {result.LegacyFinalStockSkipReason}");
        Assert.NotNull(result.LegacyFinalStockRollForward);
        Assert.Equal(("202512", "202601", 1, 3), (result.LegacyFinalStockRollForward!.FromYm, result.LegacyFinalStockRollForward.ToYm,
            result.LegacyFinalStockRollForward.CarryMonths, result.LegacyFinalStockRollForward.MoveLines));
        Assert.True(result.LegacyFinalStockRows > 0 && result.LegacyFinalCostRows > 0,
            $"🔴 맞춤 {result.LegacyFinalStockRows}행 · 끝전 {result.LegacyFinalCostRows}행 — 맞춤·끝전이 안 돌았다.");

        Assert.Equal(15m, await QtyAsync(db, seed.Pen));
        Assert.Equal(3m, await QtyAsync(db, seed.Pencil));
        Assert.Equal(1650m + 300m, await AmountAsync(db));
        var adj = (await db.QueryAsync<(string SourceId, DateTime LedgerDate)>(
            "SELECT source_id AS SourceId, ledger_date AS LedgerDate FROM stock_ledger WHERE tenant_id=@T AND source_id LIKE 'mb-adj-%'", new { T = TenantId })).ToList();
        Assert.True(adj.Count > 0 && adj.All(a => a.SourceId == "mb-adj-202601" && a.LedgerDate == CarryBase),
            "🔴 맞춤 줄 source_id·날짜가 이어 계산 달·기준일이 아니다: " + string.Join(",", adj));
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("거래명세서 입출고로 이어 계산했습니다"));
    }

    [Fact(DisplayName = "G10-D2 대조군 — 가드 발동 · 이어 계산 자료 없음(null) → 종전 skip · 사유 · 맞춤 0 · 재고 = 원장 누계")]
    public async Task G10_D2_자료없음_skip()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(G10_D2_자료없음_skip)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        var seed = await SeedAsync(db);
        var log = new CaptureLogger();
        var svc = new MdbMigrationService(db, log, crypto: null!, migrationFactory: null);
        var result = new MdbMigrationResult();

        await Invoke(svc, "RunLegacyStockFinalizeAsync", TenantId, Posting(Docfc(), null), seed.ItemMap, Now, result, CancellationToken.None);

        Assert.True(log.Errors().Length == 0, log.Errors());
        Assert.Equal(FS.StaleFinalStockReason, result.LegacyFinalStockSkipReason);
        Assert.Null(result.LegacyFinalStockRollForward);
        Assert.Equal(0, result.LegacyFinalStockRows);
        Assert.Equal(18m, await QtyAsync(db, seed.Pen));
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
            Console.Error.WriteLine($"[G10] 임시 DB 정리 실패 — 손으로 지워라: {_dbName} ({ex.Message})");
        }
    }
}
