using System.Data;
using Dapper;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;
using S = HitPan.Application.Services.MdbLegacyFinalStock;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>MdbLegacyFinalStockGate (G3)</b> — 20260915작1 갈래 D · 순수 계산 (DB 없음 · SKIP 없음).
/// </summary>
/// <remarks>
/// 진실원: 작업지시서 §4 D · §11-3 G3 · §14 병렬이슈37 · 설계 §10·§11·§12·§16 · 개발명세서 <c>docs/개발/erp/20260915작1_갈래D_재고맞춤_개발명세서.md</c>.
/// 함정: ① 전체 MAX 달만 보면 그 전에 끝난 품목이 빠진다 ② 창고 01~06 을 버리면 수량·금액이 모자란다 ③ 대소문자·뒤 공백 구분 시 품목이 갈라진다
/// ④ 원장에만 있는 품목(목표 0)을 안 맞추면 용역 누계가 재고로 남는다 ⑤ 끝전 2자리면 3품목 합이 −0.11 틀린다.
/// DB 분(맞춤 줄 INSERT · 멱등 · 사람 입력 대조군 · 리빌드 순서)은 <see cref="MdbLegacyFinalStockGateDbTests"/>.
/// </remarks>
public sealed class MdbLegacyFinalStockGateTests
{
    internal static DataTable Docfc(params (string Ym, string Chang, string Pum, string Ku, decimal Qty, decimal Amt)[] rows)
    {
        var t = new DataTable("DOCFC");
        t.Columns.Add("IM_YM", typeof(string));
        t.Columns.Add("IM_CHANG", typeof(string));
        t.Columns.Add("IM_PUM", typeof(string));
        t.Columns.Add("IM_KU", typeof(string));
        t.Columns.Add("IM_CQTY", typeof(decimal));
        t.Columns.Add("IM_CAMT", typeof(decimal));
        foreach (var r in rows) t.Rows.Add(r.Ym, r.Chang, r.Pum, r.Ku, r.Qty, r.Amt);
        return t;
    }

    [Fact(DisplayName = "G3-1 🔴 품목별 마지막 달 — 앞 달 값·달 합이 아니고 · 전체 마지막 달 전에 끝난 품목도 남는다")]
    public void G3_1_품목별_마지막달()
    {
        var r = S.ComputeFinalStock(Docfc(
            ("202512", "00", "펜", "", 5m, 50m),
            ("202602", "00", "펜", "", 7m, 70m),
            ("202601", "00", "펜", "", 6m, 60m),
            ("202511", "00", "종이", "A4", 3m, 30m)));

        Assert.NotNull(r);
        Assert.Equal("202602", r!.MaxYm);
        var pen = r.Lines.Single(l => l.Key == LegacyMdbMapping.StockItemKey("펜", ""));
        Assert.True(pen.Qty == 7m && pen.Amount == 70m,
            $"🔴 펜 = {pen.Qty}/{pen.Amount} — 마지막 달(202602) 7/70 이어야 한다(앞 달·달 합 금지).");
        var paper = r.Lines.SingleOrDefault(l => l.Key == LegacyMdbMapping.StockItemKey("종이", "A4"));
        Assert.True(paper is not null && paper.Qty == 3m,
            "🔴 202511 에 끝난 품목(종이)이 빠졌다 — 전체 MAX 달이 아니라 품목별 MAX 달이다.");
    }

    [Fact(DisplayName = "G3-2 🔴 창고 합산(R7) — 창고별 마지막 달을 골라 기본창고 한 줄로 더한다")]
    public void G3_2_창고합산()
    {
        var r = S.ComputeFinalStock(Docfc(
            ("202602", "00", "마우스", "EA", 4m, 400m),
            ("202601", "01", "마우스", "EA", 2m, 200m),
            ("202512", "01", "마우스", "EA", 9m, 900m)))!;

        var line = Assert.Single(r.Lines);
        Assert.True(line.Qty == 6m && line.Amount == 600m,
            $"🔴 창고 합산 = {line.Qty}/{line.Amount} — 00(202602) 4 + 01(202601) 2 = 6/600 이어야 한다.");
        Assert.Equal(2, line.WarehouseLines);
        Assert.Equal(1, r.NonDefaultWarehouseLines);
    }

    [Fact(DisplayName = "G3-3 🔴 대소문자·앞뒤 공백만 다른 품목 = 한 품목 (구분하면 품목이 갈라지고 옛 달 값이 끼어든다)")]
    public void G3_3_대소문자_공백_한품목()
    {
        var r = S.ComputeFinalStock(Docfc(
            ("202512", "00", "htp21c ", "표준형-l", 500m, 500000m),
            ("202602", "00", "HTP21C", "표준형-L ", -51m, -15599500m)))!;

        var line = Assert.Single(r.Lines);
        Assert.True(line.Qty == -51m && line.Amount == -15599500m,
            $"🔴 {line.Qty}/{line.Amount} — 같은 품목의 마지막 달(−51/−15,599,500)이어야 한다(설계 §16 55,253,974.4 함정).");
    }

    [Fact(DisplayName = "G3-4 🔴 조정량 = 목표 − 이관 원장 순수량 · 원장에만 있는 품목 목표 0 · 같으면 줄 없음")]
    public void G3_4_조정량()
    {
        var target = new Dictionary<string, decimal> { ["A"] = 10m, ["B"] = 5m, ["D"] = 0m };
        var net = new Dictionary<string, decimal> { ["A"] = 25m, ["B"] = 5m, ["C"] = 100m };

        var adj = S.ComputeAdjustments(target, net).ToDictionary(x => x.ItemId, x => x.Diff);

        Assert.Equal(-15m, adj["A"]);
        Assert.False(adj.ContainsKey("B"), "🔴 목표와 같으면 맞춤 줄을 넣지 않는다(멱등).");
        Assert.True(adj.TryGetValue("C", out var c) && c == -100m,
            "🔴 DOCFC 에 없고 원장에만 있는 품목(용역 누계 · 대소문자만 다른 나머지 품목)은 목표 0 으로 맞춰야 한다.");
        Assert.False(adj.ContainsKey("D"));
        Assert.Equal(("out", 0m, 15m), S.AdjustmentMove(-15m));
        Assert.Equal(("in", 7m, 0m), S.AdjustmentMove(7m));
    }

    [Fact(DisplayName = "G3-5 🔴 품목 여럿이면 가장 먼저 등록된 품목 — 입력 순서가 바뀌어도 같다(결정적)")]
    public void G3_5_품목선택_결정적()
    {
        var t0 = new DateTime(2026, 9, 1, 0, 0, 0);
        var a = new[] { ("z", t0.AddSeconds(5)), ("y", t0), ("x", t0) };
        Assert.Equal("x", S.ChooseItem(a));
        Assert.Equal("x", S.ChooseItem(a.Reverse()));
        Assert.Null(S.ChooseItem(Array.Empty<(string, DateTime)>()));

        Assert.Equal(S.ItemNameKey("abc|규격", null), S.ItemNameKey("ABC ", "규격 "));
    }

    [Fact(DisplayName = "G3-6 🔴 끝전 — 단가 6자리 · ROUND(Σ 수량×단가, 2) = 레거시 금액 · 2자리면 −0.11 (설계 §16)")]
    public void G3_6_끝전_6자리()
    {
        (decimal Qty, decimal Amt)[] items = { (-51m, -15599500m), (6m, 18500m), (12m, 26182m) };
        var legacy = items.Sum(i => i.Amt);

        var six = Math.Round(items.Sum(i => i.Qty * S.FinalUnitCost(i.Qty, i.Amt)!.Value), 2, MidpointRounding.AwayFromZero);
        Assert.True(six == legacy, $"🔴 6자리 단가 합 {six} ≠ 레거시 {legacy}");

        var two = Math.Round(items.Sum(i => i.Qty * Math.Round(i.Amt / i.Qty, 2, MidpointRounding.AwayFromZero)), 2);
        Assert.True(two != legacy, "대조군 — 2자리 단가로도 맞으면 이 시험이 끝전을 안 재고 있다.");

        Assert.Null(S.FinalUnitCost(0m, 1234m));
    }

    [Fact(DisplayName = "G3-7 🔴 DOCFC 없음·0행·유효 달 없음 → 계산 없음 · 맞춤 단계는 DB 에 손대지 않고 0")]
    public async Task G3_7_DOCFC없음_skip()
    {
        Assert.Null(S.ComputeFinalStock(null));
        Assert.Null(S.ComputeFinalStock(Docfc()));
        var bad = S.ComputeFinalStock(Docfc(("0000", "00", "펜", "", 1m, 1m)));
        Assert.Null(bad);

        using var unopened = new MySqlConnection();   // 열리지 않은 연결 — 건드리면 예외
        var n = await S.AdjustStockAsync(unopened, "t", null, new DateTime(2026, 2, 28),
            (_, _, _, _) => throw new Xunit.Sdk.XunitException("DOCFC 없음인데 품목 등록을 불렀다"), CancellationToken.None);
        Assert.Equal(0, n);
        Assert.Equal(0, await S.ApplyFinalCostAsync(unopened, "t", Docfc(), CancellationToken.None));
        Assert.Equal(ConnectionState.Closed, unopened.State);
    }

    [Fact(DisplayName = "G3-8 맞춤 줄 source_id·해시 모양 — mb-adj-{달} (36자 이내) · SHA256(adj|달|item|방향) 64자")]
    public void G3_8_키모양()
    {
        Assert.Equal("mb-adj-202602", S.AdjustmentSourceId("202602"));
        var h = S.AdjustmentHash("202602", "11111111-1111-1111-1111-111111111111", "out");
        Assert.Equal(64, h.Length);
        Assert.NotEqual(h, S.AdjustmentHash("202602", "11111111-1111-1111-1111-111111111111", "in"));
    }
}

/// <summary>G3 DB 시험끼리 병렬로 출하 DDL 을 적재하지 않게 묶는다.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MdbLegacyFinalStockGateCollection
{
    public const string Name = "MdbLegacyFinalStockGate";
}

/// <summary>
/// 🔴 <b>MdbLegacyFinalStockGate (G3) — DB 분</b>. 격리 MariaDB 에 출하 DDL 을 적재한 임시 DB(<c>hitpan_finalstock_*</c>)만 쓴다(운영 무접촉 #39).
/// MariaDB 가 없으면 로컬은 건너뛰고, <c>HITPAN_REQUIRE_DB</c> 가 있으면 실패다(SKIP=FAIL).
/// 리빌드 SQL 은 <c>MdbMigrationService.cs</c> 의 <c>rebuildSql</c> 원문을 <b>파일에서 읽어</b> 돌린다 — 복사본이 아니다.
/// </summary>
[Collection(MdbLegacyFinalStockGateCollection.Name)]
public sealed class MdbLegacyFinalStockGateDbTests : IDisposable
{
    private readonly string _dbName = "hitpan_finalstock_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;

    private const string TenantId = "66666666-6666-6666-6666-666666666666";
    private static readonly DateTime BaseDate = new(2026, 2, 28);

    // ── 준비물 (LegacyUnpostedDdlGateTests 와 같은 방식) ──

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

    /// <summary>🔴 이관 서비스의 리빌드 SQL 원문을 파일에서 꺼낸다 — 서비스가 바뀌면 이 게이트도 바뀐 SQL 로 돈다.</summary>
    private static string RebuildSql()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "src", "HitPan.Application", "Services", "MdbMigrationService.cs"));
        const string open = "const string rebuildSql = \"\"\"";
        var i = src.IndexOf(open, StringComparison.Ordinal);
        Assert.True(i >= 0, "🔴 MdbMigrationService.cs 에서 item_stock 리빌드 SQL(rebuildSql)을 못 찾았다.");
        i += open.Length;
        var j = src.IndexOf("\"\"\"", i, StringComparison.Ordinal);
        return src[i..j];
    }

    // ── 시나리오 ──

    private sealed class Fixture
    {
        public required string Wh { get; init; }
        public Dictionary<string, string> Items { get; } = new(StringComparer.Ordinal);   // 이관 매핑 키(Trim·대소문자 보존) → item_id
        public int EnsureCalls { get; set; }
    }

    private static string MapKey(string pum, string ku) =>
        string.IsNullOrWhiteSpace(ku) ? pum.Trim() : $"{pum.Trim()}|{ku.Trim()}";

    private static async Task<string> InsertItemAsync(IDbConnection db, IDbTransaction? tx, string name, string? spec, DateTime created)
    {
        var id = Guid.NewGuid().ToString();
        await db.ExecuteAsync(
            """
            INSERT INTO items
              (item_id, tenant_id, item_code, item_name, spec, item_type, unit, tax_type, is_active, is_deleted, memo,
               purchase_price, sale_price, standard_price, safety_stock, created_at, updated_at, row_version)
            VALUES (@Id, @T, @Code, @Name, @Spec, 'product', 'EA', 'taxable', 1, 0, NULL, 0, 0, 0, 0, @C, @C, 0)
            """,
            new { Id = id, T = TenantId, Code = "G3-" + id[..8], Name = name, Spec = spec, C = created }, tx);
        return id;
    }

    private static Task LedgerAsync(IDbConnection db, string item, string wh, string sourceType, string sourceId, decimal qin, decimal qout, decimal? unitCost) =>
        db.ExecuteAsync(
            """
            INSERT INTO stock_ledger (tenant_id, item_id, warehouse_id, ledger_date, ym, move_type, source_type, source_id, qty_in, qty_out, unit_cost)
            VALUES (@T, @I, @W, '2025-06-01', '2025-06', @M, @S, @Sid, @In, @Out, @U)
            """,
            new { T = TenantId, I = item, W = wh, M = qin > 0 ? "in" : "out", S = sourceType, Sid = sourceId, In = qin, Out = qout, U = unitCost });

    /// <summary>
    /// 품목: 「abc」(가장 먼저) · 「ABC 」(나중) — 대소문자·공백만 다른 둘 · 「HTP21C|표준형-L」 · 「마우스외 4종|EA」 · 「사람상품」 · 「용역」(DOCFB 전용) ·
    /// DOCFC 에만: 「팩시밀리용지」(수량≠0) · 「빈상품」(수량 0·금액 0 — R8 등록 대상).
    /// </summary>
    private async Task<(Fixture Fx, DataTable Docfc, decimal ExpectedAmount)> SeedAsync(MySqlConnection db)
    {
        var wh = Guid.NewGuid().ToString();
        await db.ExecuteAsync(
            "INSERT INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, is_active, created_at, updated_at) VALUES (@W, @T, 'MAIN', '기본창고', 'normal', 1, NOW(6), NOW(6))",
            new { W = wh, T = TenantId });
        var fx = new Fixture { Wh = wh };
        var t0 = new DateTime(2026, 9, 1, 9, 0, 0);

        async Task<string> Item(string pum, string ku, int sec)
        {
            var id = await InsertItemAsync(db, null, pum.Trim(), string.IsNullOrWhiteSpace(ku) ? null : ku.Trim(), t0.AddSeconds(sec));
            fx.Items[MapKey(pum, ku)] = id;
            return id;
        }

        var abc = await Item("abc", "", 1);
        var abcUpper = await Item("ABC ", "", 2);
        var htp = await Item("HTP21C", "표준형-L", 3);
        var mouse = await Item("마우스외 4종", "EA", 4);
        var human = await Item("사람상품", "", 5);
        var service = await Item("용역", "", 6);

        // 이관 이력(DOCFB) — 용역 누계 100 · 품목별 순수량이 DOCFC 와 다르다
        await LedgerAsync(db, abc, wh, "migration", "mb-1", 4m, 0m, 10m);
        await LedgerAsync(db, abcUpper, wh, "migration", "mb-2", 1m, 0m, 10m);
        await LedgerAsync(db, htp, wh, "migration", "mb-3", 20m, 0m, 300000m);
        await LedgerAsync(db, mouse, wh, "migration", "mb-4", 0m, 3m, null);
        await LedgerAsync(db, human, wh, "migration", "mb-5", 10m, 0m, 100m);
        await LedgerAsync(db, service, wh, "migration", "mb-6", 100m, 0m, 1000m);

        // 🔴 사람 입력 원장(이관 뒤 판매 출고 3) — 맞춤 계산에서 빠져야 한다
        await LedgerAsync(db, human, wh, "sales_delivery", "human-sd-1", 0m, 3m, null);

        var docfc = MdbLegacyFinalStockGateTests.Docfc(
            ("202601", "00", "Abc", "", 2m, 20m),
            ("202602", "00", "Abc", "", 9m, 99m),
            ("202602", "00", "HTP21C", "표준형-L", -40m, -12000000m),
            ("202602", "03", "htp21c ", "표준형-l", -11m, -3599500m),
            ("202602", "00", "마우스외 4종", "EA", 6m, 18500m),
            ("202602", "00", "사람상품", "", 10m, 1000m),
            ("202602", "00", "팩시밀리용지", "", 12m, 26182m),
            ("202512", "00", "빈상품", "", 0m, 0m));

        // 화면 금액 기대 = DOCFC 금액 합 − 사람 출고 3 × 100 (용역·ABC 는 0)
        var expected = 99m + (-15599500m) + 18500m + 1000m + 26182m - 300m;
        return (fx, docfc, expected);
    }

    private Func<string, string, IDbTransaction?, CancellationToken, Task<string>> EnsureItem(MySqlConnection db, Fixture fx) =>
        async (pum, ku, tx, ct) =>
        {
            fx.EnsureCalls++;
            var key = MapKey(pum, ku);
            if (fx.Items.TryGetValue(key, out var id)) return id;
            id = await InsertItemAsync(db, tx, pum.Trim(), string.IsNullOrWhiteSpace(ku) ? null : ku.Trim(), DateTime.Now);
            fx.Items[key] = id;
            return id;
        };

    private async Task RebuildAsync(MySqlConnection db) =>
        await db.ExecuteAsync(RebuildSql(), new { TenantId });

    private static Task<decimal> QtyAsync(MySqlConnection db, string item) =>
        db.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(current_qty),0) FROM item_stock WHERE tenant_id=@T AND item_id=@I", new { T = TenantId, I = item });

    private static Task<decimal> AmountAsync(MySqlConnection db) =>
        db.ExecuteScalarAsync<decimal>("SELECT ROUND(SUM(current_qty * avg_cost), 2) FROM item_stock WHERE tenant_id = @T", new { T = TenantId });

    [Fact(DisplayName = "G3-D1 🔴 맞춤→리빌드→끝전 — 품목 수량 = DOCFC · 원장에만 있는 품목 0 · 대소문자 품목 하나 · 사람 거래 그대로 · 멱등 · 금액 ROUND 2 일치")]
    public async Task G3_D1_종단()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(G3_D1_종단)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        var (fx, docfc, expectedAmount) = await SeedAsync(db);
        var humanRowsBefore = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM stock_ledger WHERE tenant_id=@T AND source_type <> 'migration'", new { T = TenantId });

        var inserted = await S.AdjustStockAsync(db, TenantId, docfc, BaseDate, EnsureItem(db, fx), CancellationToken.None);
        await RebuildAsync(db);
        var updated = await S.ApplyFinalCostAsync(db, TenantId, docfc, CancellationToken.None);

        // R8 — DOCFC 전용 품목(수량 0 포함) 등록
        Assert.True(fx.Items.ContainsKey("빈상품") && fx.Items.ContainsKey("팩시밀리용지"),
            "🔴 DOCFC 에만 있는 품목이 등록되지 않았다(R8 — 수량 0 포함 전부 등록).");

        // 대소문자 — 가장 먼저 등록된 「abc」 한 품목에 9 · 나머지 0
        Assert.True(await QtyAsync(db, fx.Items["abc"]) == 9m, $"🔴 abc = {await QtyAsync(db, fx.Items["abc"])} — 가장 먼저 등록된 품목에 DOCFC 9 가 가야 한다.");
        Assert.True(await QtyAsync(db, fx.Items["ABC"]) == 0m, "🔴 대소문자·공백만 다른 나머지 품목(ABC)이 0 이 아니다 — 재고가 두 품목으로 갈렸다.");
        Assert.True(await QtyAsync(db, fx.Items["Abc"]) == 0m, "🔴 DOCFC 표기(Abc)로 새로 등록된 품목에 재고가 갔다 — 결정 규칙(가장 먼저 등록) 위반.");

        Assert.True(await QtyAsync(db, fx.Items["HTP21C|표준형-L"]) == -51m, "🔴 HTP21C 창고 00 + 03 합산(−51)이 아니다(R7).");
        Assert.True(await QtyAsync(db, fx.Items["용역"]) == 0m, "🔴 DOCFB 전용 품목(용역 누계 100)이 0 으로 맞춰지지 않았다.");
        Assert.True(await QtyAsync(db, fx.Items["팩시밀리용지"]) == 12m, "🔴 DOCFC 전용 품목 12 가 안 들어갔다.");

        // 🔴 사람 입력 대조군 — 맞춤은 이관분만 DOCFC 10 으로 맞추고 사람 출고 3 은 그대로 남는다
        var humanQty = await QtyAsync(db, fx.Items["사람상품"]);
        Assert.True(humanQty == 7m,
            $"🔴 사람상품 = {humanQty} — 7(DOCFC 10 − 사람 출고 3)이어야 한다. 10 이면 맞춤이 사람 거래까지 지웠다.");
        var humanRowsAfter = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM stock_ledger WHERE tenant_id=@T AND source_type <> 'migration'", new { T = TenantId });
        Assert.Equal(humanRowsBefore, humanRowsAfter);

        // 맞춤 줄 모양
        var adj = (await db.QueryAsync<(string SourceId, DateTime LedgerDate, string Ym, string Memo, string? PartnerId, decimal? UnitCost, string Hash, string MoveType, string ItemId)>(
            "SELECT source_id, ledger_date, ym, memo, partner_id, unit_cost, migrated_source_hash, move_type, item_id FROM stock_ledger WHERE tenant_id=@T AND source_id LIKE 'mb-adj-%'",
            new { T = TenantId })).ToList();
        Assert.Equal(inserted, adj.Count);
        Assert.All(adj, a =>
        {
            Assert.Equal("mb-adj-202602", a.SourceId);
            Assert.Equal(BaseDate, a.LedgerDate);
            Assert.Equal("2026-02", a.Ym);
            Assert.Equal(S.AdjustmentMemo, a.Memo);
            Assert.Null(a.PartnerId);
            Assert.Null(a.UnitCost);
            Assert.Equal(S.AdjustmentHash("202602", a.ItemId, a.MoveType), a.Hash);
        });

        // 끝전 — ROUND(Σ current_qty × avg_cost, 2) = 레거시
        var amount = await AmountAsync(db);
        Assert.True(amount == expectedAmount,
            $"🔴 재고 금액 {amount} ≠ 기대 {expectedAmount} (DOCFC 금액 − 사람 출고분) — 끝전이 레거시와 다르다.");
        Assert.True(updated >= 5, $"끝전 갱신 {updated}행 — 수량≠0 품목 5개 이상이어야 한다.");

        // 🔴 멱등 — 같은 MDB 로 다시: 맞춤 줄 0 · 원장 행수·금액 그대로
        var rowsBefore = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM stock_ledger WHERE tenant_id=@T", new { T = TenantId });
        var again = await S.AdjustStockAsync(db, TenantId, MdbLegacyFinalStockGateTests.Docfc(
            docfc.Rows.Cast<DataRow>().Select(r => ((string)r[0], (string)r[1], (string)r[2], (string)r[3], (decimal)r[4], (decimal)r[5])).ToArray()),
            BaseDate, EnsureItem(db, fx), CancellationToken.None);
        await RebuildAsync(db);
        await S.ApplyFinalCostAsync(db, TenantId, docfc, CancellationToken.None);
        var rowsAfter = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM stock_ledger WHERE tenant_id=@T", new { T = TenantId });
        Assert.True(again == 0 && rowsAfter == rowsBefore, $"🔴 재실행이 맞춤 줄 {again}행을 더 넣었다(원장 {rowsBefore}→{rowsAfter}) — 멱등이 아니다.");
        Assert.Equal(expectedAmount, await AmountAsync(db));
    }

    [Fact(DisplayName = "G3-D2 🔴 순서 대조군 — 끝전을 리빌드 앞에 두면 금액이 틀어진다(병렬이슈37) · 뒤에 두면 맞는다")]
    public async Task G3_D2_끝전_리빌드뒤()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(G3_D2_끝전_리빌드뒤)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        var (fx, docfc, expectedAmount) = await SeedAsync(db);

        await S.AdjustStockAsync(db, TenantId, docfc, BaseDate, EnsureItem(db, fx), CancellationToken.None);
        await RebuildAsync(db);                                                  // item_stock 행을 먼저 만든다
        await S.ApplyFinalCostAsync(db, TenantId, docfc, CancellationToken.None); // 이 순간은 맞다
        await RebuildAsync(db);                                                  // 🔴 끝전 뒤에 리빌드가 또 돌면(= 끝전을 리빌드 앞에 둔 배선)

        var wrong = await AmountAsync(db);
        Assert.True(wrong != expectedAmount,
            $"대조군 실패 — 리빌드가 끝전을 덮어도 금액이 {wrong} 로 같다. 이 시험은 순서를 재지 못한다.");

        await S.ApplyFinalCostAsync(db, TenantId, docfc, CancellationToken.None); // 올바른 순서: 리빌드 뒤
        var right = await AmountAsync(db);
        Assert.True(right == expectedAmount, $"🔴 리빌드 뒤 끝전 = {right} ≠ {expectedAmount}");
    }

    [Fact(DisplayName = "G3-D3 끝전 폴백 — 맞춤 단계 기록이 없는 새 DOCFC 표여도 품명·규격으로 가장 먼저 등록된 품목을 찾아 맞춘다")]
    public async Task G3_D3_끝전_폴백()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(G3_D3_끝전_폴백)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        var (fx, docfc, expectedAmount) = await SeedAsync(db);

        await S.AdjustStockAsync(db, TenantId, docfc, BaseDate, EnsureItem(db, fx), CancellationToken.None);
        await RebuildAsync(db);

        var fresh = docfc.Copy();   // ExtendedProperties 는 복사되지만 이름을 지워 「기록 없음」을 만든다
        fresh.ExtendedProperties.Clear();
        await S.ApplyFinalCostAsync(db, TenantId, fresh, CancellationToken.None);

        var amount = await AmountAsync(db);
        Assert.True(amount == expectedAmount, $"🔴 폴백 끝전 금액 {amount} ≠ {expectedAmount}");
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
            Console.Error.WriteLine($"[G3] 임시 DB 정리 실패 — 손으로 지워라: {_dbName} ({ex.Message})");
        }
    }
}
