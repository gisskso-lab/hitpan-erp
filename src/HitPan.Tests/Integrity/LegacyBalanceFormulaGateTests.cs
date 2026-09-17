using System.Data;
using Dapper;
using HitPan.API.Controllers;
using HitPan.Application.DTOs.Approval;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Infrastructure.Persistence;
using HitPan.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>격리 DB 하나(<c>hitpan_r2_*</c>)에 출하 DDL 을 한 번 적재 · 사례마다 새 테넌트.</summary>
public sealed class LegacyBalanceFormulaDbFixture : IDisposable
{
    public string DbName { get; } = "hitpan_r2_g9_" + Guid.NewGuid().ToString("N")[..8];
    public bool Available { get; }
    private readonly bool _created;

    public LegacyBalanceFormulaDbFixture()
    {
        if (!ServerAvailable()) return;
        using (var admin = new MySqlConnection(ServerConnString()))
        {
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{DbName}`; CREATE DATABASE `{DbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
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
        psi.ArgumentList.Add(DbName);

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardInput.Write(File.ReadAllText(Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql")));
        proc.StandardInput.Close();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0) throw new InvalidOperationException($"출하 DDL import 실패:\n{err}");
        Available = true;
    }

    public string DbConnString() => ServerConnString().Replace("User=", $"Database={DbName};User=");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("HitPan.sln 을 못 찾았다.");
    }

    private static string ServerConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};User={user};Password={pass};DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
    }

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
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[G9] MariaDB 연결 실패: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (!_created) return;
        try
        {
            using var admin = new MySqlConnection(ServerConnString());
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{DbName}`");
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[G9] 임시 DB 삭제 실패 {DbName}: {ex.Message}");
        }
    }
}

/// <summary>
/// 🔴 <b>G9 LegacyBalanceFormulaGate</b> — 20260915작1 개정 3판 갈래 R2 (설계 §20·§24·§25 · 작지 §15-4) + [3-V] 병렬이슈45.
/// </summary>
/// <remarks>
/// 한 fixture(손계산):
/// <list type="bullet">
/// <item>PA 이월 미수 +100,000 · 이관 명세서 500,000 + 이관 수금 200,000 · 사람 수금 → 이관 명세서 10,000(E 호환 M) · 이월 수금 30,000(서비스)
///   · 취소된 이월 수금 5,000(is_active=0) · 사람 명세서 50,000 + 수금 20,000(서비스) · 취소된 수금 7,000 → M 40,000 · R 60,000</item>
/// <item>PB 이월 미지급 −80,000 · 이관 매입 300,000 + 이관 지급 100,000 · 이월 지급 25,000(서비스) · 사람 매입 40,000 + 지급 15,000 → R 55,000</item>
/// <item>PC 사람 명세서 70,000(수금 없음)</item>
/// </list>
/// #1 = 120,000 − 20,000 + 60,000 = 160,000 · #2 = 40,000 − 15,000 + 55,000 = 80,000.
/// </remarks>
[Collection("DeviceAndKeyGate")]
public sealed class LegacyBalanceFormulaGateTests : IClassFixture<LegacyBalanceFormulaDbFixture>
{
    private readonly LegacyBalanceFormulaDbFixture _fx;
    private readonly string PA = Guid.NewGuid().ToString();
    private readonly string PB = Guid.NewGuid().ToString();
    private readonly string PC = Guid.NewGuid().ToString();
    private readonly string MigD = "g9-mig-d-" + Guid.NewGuid().ToString("N")[..8];
    private readonly string HumDA = "g9-hum-da-" + Guid.NewGuid().ToString("N")[..8];
    private readonly string HumDC = "g9-hum-dc-" + Guid.NewGuid().ToString("N")[..8];
    private readonly string MigR = "g9-mig-r-" + Guid.NewGuid().ToString("N")[..8];
    private readonly string HumRB = "g9-hum-rb-" + Guid.NewGuid().ToString("N")[..8];
    private static readonly DateTime BaseDate = new(2026, 2, 28);
    private static readonly DateTime After = new(2026, 3, 10);

    public LegacyBalanceFormulaGateTests(LegacyBalanceFormulaDbFixture fx) => _fx = fx;

    private bool Skip(string name) => !_fx.Available && DbGateEnvironment.SkipOrFail("G9 LegacyBalanceFormulaGate " + name);

    // ── 준비 ──
    private async Task<(MySqlConnection Db, string T)> NewTenantAsync()
    {
        var db = new MySqlConnection(_fx.DbConnString());
        await db.OpenAsync();
        var t = Guid.NewGuid().ToString();
        await db.ExecuteAsync("""
            INSERT INTO accounts (account_code, tenant_id, account_name, account_type, sort_order) VALUES
              ('10100', @T, '현금', 'asset', 1), ('10300', @T, '보통예금', 'asset', 2),
              ('10800', @T, '외상매출금', 'asset', 3), ('23200', @T, '외상매입금', 'liability', 4),
              ('40100', @T, '상품매출', 'revenue', 5), ('25500', @T, '부가세예수금', 'liability', 6),
              ('50100', @T, '상품매입', 'expense', 7), ('17600', @T, '부가세대급금', 'asset', 8)
            """, new { T = t });
        var now = DateTime.Now;
        foreach (var (id, code) in new[] { (PA, "A"), (PB, "B"), (PC, "C") })
        {
            await db.ExecuteAsync("""
                INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, is_active, is_deleted, created_at, updated_at)
                VALUES (@Id, @T, @Code, CONCAT('G9거래처', @Code), 'both', 1, 0, @Now, @Now)
                """, new { Id = id, T = t, Code = code, Now = now });
        }
        return (db, t);
    }

    private static Task LegacyAsync(MySqlConnection db, string t, string p, decimal amt)
        => db.ExecuteAsync("""
            INSERT INTO partner_legacy_balances (balance_id, tenant_id, partner_id, base_date, balance_amount, source_type, source_id)
            VALUES (UUID(), @T, @P, @D, @A, 'migration', CONCAT('mig-legacybal-', @P))
            """, new { T = t, P = p, D = BaseDate, A = amt });

    private static Task DeliveryAsync(MySqlConnection db, string t, string id, string p, decimal amt, string source)
        => db.ExecuteAsync("""
            INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, partner_id, delivery_date, source_type, status, total_amount, vat_amount, is_deleted, created_at, updated_at)
            VALUES (@Id, @T, @Id, @P, '2026-03-05', @S, 'confirmed', @A, 0, 0, NOW(6), NOW(6))
            """, new { Id = id, T = t, P = p, A = amt, S = source });

    private static Task ReceiptAsync(MySqlConnection db, string t, string id, string p, decimal amt, string source)
        => db.ExecuteAsync("""
            INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, partner_id, receipt_date, source_type, status, total_amount, vat_amount, created_at)
            VALUES (@Id, @T, @Id, @P, '2026-03-05', @S, 'confirmed', @A, 0, NOW(6))
            """, new { Id = id, T = t, P = p, A = amt, S = source });

    private static Task RawCollectionAsync(MySqlConnection db, string t, string p, decimal amt, string refType, string refId, int active, string? source)
        => db.ExecuteAsync("""
            INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active, source_type, source_id)
            VALUES (UUID(), @T, @P, '2026-03-06', @A, @R, @Ref, @Act, @S, IF(@S IS NULL, NULL, UUID()))
            """, new { T = t, P = p, A = amt, R = refType, Ref = refId, Act = active, S = source });

    private static Task RawPaymentAsync(MySqlConnection db, string t, string p, decimal amt, string type, string refId, string? source)
        => db.ExecuteAsync("""
            INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, ref_order_id, is_active, source_type, source_id)
            VALUES (UUID(), @T, @P, @Ty, @A, '2026-03-06', @Ref, 1, @S, IF(@S IS NULL, NULL, UUID()))
            """, new { T = t, P = p, Ty = type, A = amt, Ref = refId, S = source });

    private static CollectionService Col(MySqlConnection db) => new(db, Mock.Of<IAuditService>());

    /// <summary>표준 fixture 적재 — 헤더 주석의 손계산 자료.</summary>
    private async Task SeedAsync(MySqlConnection db, string t)
    {
        await LegacyAsync(db, t, PA, 100_000m);
        await LegacyAsync(db, t, PB, -80_000m);

        await DeliveryAsync(db, t, MigD, PA, 500_000m, "migration");
        await RawCollectionAsync(db, t, PA, 200_000m, "sales_delivery", MigD, 1, "migration");
        await RawCollectionAsync(db, t, PA, 10_000m, "sales_delivery", MigD, 1, null);          // E 호환 M
        await RawCollectionAsync(db, t, PA, 5_000m, LegacyBalanceMatching.RefType, PA, 0, null); // 취소된 이월 수금
        await DeliveryAsync(db, t, HumDA, PA, 50_000m, "direct");
        await RawCollectionAsync(db, t, PA, 7_000m, "sales_delivery", HumDA, 0, null);           // 취소된 수금
        await DeliveryAsync(db, t, HumDC, PC, 70_000m, "direct");

        await ReceiptAsync(db, t, MigR, PB, 300_000m, "migration");
        await RawPaymentAsync(db, t, PB, 100_000m, "purchase", MigR, "migration");
        await ReceiptAsync(db, t, HumRB, PB, 40_000m, "direct");
        await RawPaymentAsync(db, t, PB, 15_000m, "purchase", HumRB, null);

        var col = Col(db);
        await col.CreateCollectionAsync(new CreateCollectionRequest { PartnerId = PA, CollectionDate = After, Amount = 20_000m, CollectionMethod = "cash", RefDocType = "sales_delivery", RefDocId = HumDA }, t, "g9");
        await col.CreateCollectionAsync(new CreateCollectionRequest { PartnerId = PA, CollectionDate = After, Amount = 30_000m, CollectionMethod = "cash", RefDocType = LegacyBalanceMatching.RefType, RefDocId = PA }, t, "g9");
        await col.CreatePaymentAsync(new CreatePaymentRequest { PartnerId = PB, PaymentDate = After, Amount = 25_000m, PaymentMethod = "bank_transfer", PaymentType = LegacyBalanceMatching.RefType, RefOrderId = PB }, t, "g9");
    }

    private static void ClearDashboardCache()
    {
        var f = typeof(FinanceService).GetField("_dashCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        ((System.Collections.IDictionary)f.GetValue(null)!).Clear();
    }

    private static async Task<(decimal Rec, decimal Pay)> KpiAsync(MySqlConnection db, string t)
    {
        ClearDashboardCache();
        var d = await new FinanceService(db, Mock.Of<IAuditService>(), Mock.Of<INotificationService>()).GetDashboardAsync(t);
        return (d.UnpaidReceivable, d.UnpaidPayable);
    }

    private static SalesService Sales(MySqlConnection db, string t)
    {
        var tenant = new Mock<ICurrentTenant>();
        tenant.SetupGet(x => x.TenantId).Returns(t);
        tenant.SetupGet(x => x.UserId).Returns("g9-user");
        return new SalesService(Mock.Of<IUnitOfWork>(), tenant.Object, db, null!, Mock.Of<IAuditService>(), null!);
    }

    // ── #1 #2 #7 #8 #9 #10 · 교차 ──
    [Fact(DisplayName = "G9-a 🔴 #1·#2 대시보드 · #7 전잔액 · #8 연체 · #9 거래처별(+M) · #10 대사표 · #1=Σ#9−M=#10")]
    public async Task G9a_11곳_손계산_교차()
    {
        if (Skip(nameof(G9a_11곳_손계산_교차))) return;
        var (db, t) = await NewTenantAsync();
        await using var _ = db;
        await SeedAsync(db, t);

        // #1 · #2 — 취소 수금(7,000) 빠짐 · 이월 지급 25,000 은 R 에서만 1번(두 번 빼면 55,000)
        var (rec, pay) = await KpiAsync(db, t);
        Assert.True(rec == 160_000m, $"🔴 #1 미수 {rec:N0} ≠ 160,000 (취소 수금 포함 = 153,000 · 사람 수금→이관 명세서 이중 = 150,000)");
        Assert.True(pay == 80_000m, $"🔴 #2 미지급 {pay:N0} ≠ 80,000 (이월 지급 두 번 빼기 = 55,000)");

        // #7 — 전잔액 PA = 50,000 − 20,000 + R 60,000
        var dA = await Sales(db, t).GetDeliveryAsync(HumDA, t);
        Assert.NotNull(dA);
        Assert.True(dA!.PrevReceivable == 90_000m, $"🔴 #7 전잔액 {dA.PrevReceivable:N0} ≠ 90,000");

        // #8 — PA = R 60,000(사람 명세서는 수금이 붙어 빠짐) · PC 70,000 · PB 없음
        var ctl = new PartnerController(null!) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        ctl.HttpContext.Items["TenantId"] = t;
        var ok = Assert.IsType<OkObjectResult>(await ctl.GetAging(db, CancellationToken.None));
        var aging = ((IEnumerable<object>)ok.Value!).Cast<IDictionary<string, object>>().ToList();
        Assert.Equal(60_000m, Convert.ToDecimal(aging.Single(r => (string)r["PartnerId"] == PA)["TotalUnpaid"]));
        Assert.Equal(70_000m, Convert.ToDecimal(aging.Single(r => (string)r["PartnerId"] == PC)["TotalUnpaid"]));
        Assert.DoesNotContain(aging, r => (string)r["PartnerId"] == PB);

        // #9 — 히트판 열 = 식 값 + M
        var rows = (await db.QueryAsync<(string PartnerId, decimal Receivable, decimal Payable, decimal ReceivableMatched, decimal PayableMatched)>(
            $"SELECT x.PartnerId, x.Receivable, x.Payable, x.ReceivableMatched, x.PayableMatched FROM ({MdbReconPosting.PartnerBalanceErpSql}) x",
            new { TenantId = t })).ToList();
        var a = rows.Single(r => r.PartnerId == PA);
        Assert.Equal(130_000m, a.Receivable);           // 50,000 − 20,000 + 60,000 + 40,000
        Assert.Equal(40_000m, a.ReceivableMatched);
        var b = rows.Single(r => r.PartnerId == PB);
        Assert.Equal(105_000m, b.Payable);              // 40,000 − 15,000 + 55,000 + 25,000
        Assert.Equal(0m, b.Receivable);
        Assert.Equal(70_000m, rows.Single(r => r.PartnerId == PC).Receivable);

        // #10 — 대사표 ⑥ = #1·#2 같은 문자열 + 3행
        var erp = await MdbReconPosting.ReadBalanceErpAsync(db, t, CancellationToken.None);
        Assert.Equal(160_000m, erp.Receivable);
        Assert.Equal(80_000m, erp.Payable);
        Assert.Equal((100_000m, 40_000m, 60_000m), (erp.LegacyReceivable!.Value, erp.MatchedReceivable!.Value, erp.RemainingReceivable!.Value));
        Assert.Equal((80_000m, 25_000m, 55_000m), (erp.LegacyPayable!.Value, erp.MatchedPayable!.Value, erp.RemainingPayable!.Value));

        // 교차: #1 = Σ(#9 − M) = #10 · #2 동일
        Assert.Equal(rec, rows.Sum(r => r.Receivable - r.ReceivableMatched));
        Assert.Equal(rec, erp.Receivable);
        Assert.Equal(pay, rows.Sum(r => r.Payable - r.PayableMatched));
        Assert.Equal(pay, erp.Payable);

        // ⑥ 3행
        var items = new List<ReconItem>();
        var warnings = new List<string>();
        var legacy = new MdbReconPosting.BalanceLegacy(100_000m, 1, 80_000m, 1, new Dictionary<int, decimal>());
        MdbReconPosting.AddBalanceItems(items, warnings, legacy, false, erp, null);
        MdbReconPosting.AddLegacyBalanceRows(items, warnings, legacy, erp, null);
        Assert.Equal("OK", items.Single(i => i.Key == "receivable_legacy").Status);
        Assert.Equal("OK", items.Single(i => i.Key == "payable_legacy").Status);
        // 3판 Z (작지 §15-13) — 정보 행은 INFO(화면 「참고」) · 「—」(NA)로 빈칸처럼 보이면 안 된다.
        Assert.All(items.Where(i => i.Key is "receivable_matched" or "receivable_remaining" or "payable_matched" or "payable_remaining"),
            i => Assert.Equal(MdbReconPosting.StatusInfo, i.Status));
        Assert.Equal(40_000m, items.Single(i => i.Key == "receivable_matched").Erp);
        Assert.Equal(60_000m, items.Single(i => i.Key == "receivable_remaining").Erp);
        Assert.Equal(25_000m, items.Single(i => i.Key == "payable_matched").Erp);
        Assert.Equal(55_000m, items.Single(i => i.Key == "payable_remaining").Erp);
        Assert.Equal("DIFF", items.Single(i => i.Key == "receivable").Status);   // 160,000 + M 40,000 ↔ F3 100,000 — 이관 뒤 새 거래는 차이로 드러낸다
        Assert.Equal(200_000m, items.Single(i => i.Key == "receivable").Erp);
        Assert.Empty(warnings);
    }

    // ── 병렬이슈52 — 이월잔액 매칭만 한 뒤 합계 줄이 거래처별 줄과 같은 말을 한다 (순수) ──
    [Fact(DisplayName = "G9-52 🔴 이월 매칭만으로는 ⑥ 미수·미지급 합계가 「차이」가 되지 않는다(거래처별 #9 와 같은 규칙)")]
    public void Matching_only_keeps_balance_totals_ok()
    {
        // 레거시 이월 미수 100,000 · 미지급 80,000 → 이관 뒤 이월 수금 10,000 · 이월 지급 10,000 만 한 상태.
        var erp = new MdbReconPosting.BalanceErp
        {
            Receivable = 90_000m, Payable = 70_000m,
            LegacyReceivable = 100_000m, MatchedReceivable = 10_000m, RemainingReceivable = 90_000m,
            LegacyPayable = 80_000m, MatchedPayable = 10_000m, RemainingPayable = 70_000m,
        };
        var legacy = new MdbReconPosting.BalanceLegacy(100_000m, 1, 80_000m, 1, new Dictionary<int, decimal>());
        var items = new List<ReconItem>();
        MdbReconPosting.AddBalanceItems(items, new List<string>(), legacy, false, erp, null);
        Assert.Equal(MdbReconPosting.StatusOk, items.Single(i => i.Key == "receivable").Status);
        Assert.Equal(MdbReconPosting.StatusOk, items.Single(i => i.Key == "payable").Status);
    }

    // ── R<0 경고 ──
    [Fact(DisplayName = "G9-b R<0 거래처 — 클램프 없음 · 대사표 경고 · 연체에서 빠짐")]
    public async Task G9b_R음수_경고()
    {
        if (Skip(nameof(G9b_R음수_경고))) return;
        var (db, t) = await NewTenantAsync();
        await using var _ = db;
        await LegacyAsync(db, t, PA, 100_000m);
        await RawCollectionAsync(db, t, PA, 130_000m, LegacyBalanceMatching.RefType, PA, 1, null);   // 서버 검사 우회(과거 자료 가정)

        var (rec, _) = await KpiAsync(db, t);
        Assert.Equal(-30_000m, rec);
        var erp = await MdbReconPosting.ReadBalanceErpAsync(db, t, CancellationToken.None);
        Assert.Equal(1, erp.NegativeReceivableCount);
        var items = new List<ReconItem>();
        var warnings = new List<string>();
        MdbReconPosting.AddLegacyBalanceRows(items, warnings, null, erp, null);
        Assert.Single(warnings, w => w.Contains("미수금 1곳"));
        Assert.Contains("0보다 작은 거래처 1곳", items.Single(i => i.Key == "receivable_remaining").Detail);

        var ctl = new PartnerController(null!) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        ctl.HttpContext.Items["TenantId"] = t;
        var ok = Assert.IsType<OkObjectResult>(await ctl.GetAging(db, CancellationToken.None));
        Assert.Empty((IEnumerable<object>)ok.Value!);
    }

    // ── #5 한 번만 ──
    [Fact(DisplayName = "G9-c 🔴 #5 거래처 잔액은 이월 수금 금액만큼 한 번만 준다 (PartnerService 무수정)")]
    public async Task G9c_거래처잔액_한번()
    {
        if (Skip(nameof(G9c_거래처잔액_한번))) return;
        var (db, t) = await NewTenantAsync();
        await using var _ = db;
        await LegacyAsync(db, t, PA, 100_000m);

        var tenantMock = new Mock<ICurrentTenant>();
        tenantMock.SetupGet(x => x.TenantId).Returns(t);
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(_fx.DbConnString(), new MariaDbServerVersion(new Version(11, 4, 0))).Options;
        await using var ef = new AppDbContext(opts, tenantMock.Object, Mock.Of<IEncryptionService>());
        var ps = new PartnerService(tenantMock.Object, new PartnerBalanceRepository(ef), db, Mock.Of<IGeocodingService>());

        var before = (await ps.GetPartnerListAsync(t)).Single(p => p.PartnerId == PA).Balance;
        await Col(db).CreateCollectionAsync(new CreateCollectionRequest { PartnerId = PA, CollectionDate = After, Amount = 30_000m, CollectionMethod = "cash", RefDocType = LegacyBalanceMatching.RefType, RefDocId = PA }, t, "g9");
        var after = (await ps.GetPartnerListAsync(t)).Single(p => p.PartnerId == PA).Balance;
        Assert.True(before - after == 30_000m, $"🔴 #5 잔액 감소 {before - after:N0} ≠ 30,000 (60,000 이면 R 을 또 뺀 두 번 빼기)");
        Assert.Equal(after, (await ps.GetPartnerDetailAsync(PA, t))!.Balance);
    }

    // ── #11 + 병렬이슈45 ──
    [Fact(DisplayName = "G9-d 🔴 #11 확정취소 재집계 — 이월 수금 보존 · 이관 명세서 미포함 · 병렬이슈45 수금 취소 역분개 1")]
    public async Task G9d_확정취소_재집계_역분개()
    {
        if (Skip(nameof(G9d_확정취소_재집계_역분개))) return;
        var (db, t) = await NewTenantAsync();
        await using var _ = db;
        await LegacyAsync(db, t, PA, 100_000m);
        await DeliveryAsync(db, t, MigD, PA, 500_000m, "migration");
        await DeliveryAsync(db, t, HumDA, PA, 50_000m, "direct");
        var col = Col(db);
        await col.CreateCollectionAsync(new CreateCollectionRequest { PartnerId = PA, CollectionDate = After, Amount = 30_000m, CollectionMethod = "cash", RefDocType = LegacyBalanceMatching.RefType, RefDocId = PA }, t, "g9");
        var linked = await col.CreateCollectionAsync(new CreateCollectionRequest { PartnerId = PA, CollectionDate = After, Amount = 20_000m, CollectionMethod = "bank_transfer", RefDocType = "sales_delivery", RefDocId = HumDA }, t, "g9");
        await db.ExecuteAsync("UPDATE partner_balance SET total_sales = 50000 WHERE tenant_id=@T AND partner_id=@P", new { T = t, P = PA });   // 확정 증분 가정

        await Sales(db, t).CancelConfirmedDeliveryAsync(HumDA, t, null);

        var pb = await db.QuerySingleAsync<(decimal Sales, decimal Receipt)>(
            "SELECT total_sales, total_receipt FROM partner_balance WHERE tenant_id=@T AND partner_id=@P", new { T = t, P = PA });
        Assert.True(pb.Sales == 0m, $"🔴 #11 total_sales {pb.Sales:N0} ≠ 0 (이관 명세서 500,000 포함)");
        Assert.True(pb.Receipt == 30_000m, $"🔴 #11 total_receipt {pb.Receipt:N0} ≠ 30,000 (이월 수금 소실 = 0)");

        // 45 — 끈 수금 1건 → collection_cancel 1 · 원 분개와 합 0
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT is_active FROM collections WHERE collection_id=@Id", new { Id = linked }));
        var cancels = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM journal_entries WHERE tenant_id=@T AND source_type='collection_cancel' AND source_id=@Id", new { T = t, Id = linked });
        Assert.True(cancels == 1, $"🔴 병렬이슈45 수금 취소 역분개 {cancels} ≠ 1");
        var net = await db.QuerySingleAsync<(decimal D, decimal C)>("""
            SELECT COALESCE(SUM(l.debit_amount),0), COALESCE(SUM(l.credit_amount),0)
              FROM journal_lines l JOIN journal_entries e ON e.entry_id = l.entry_id AND e.tenant_id = l.tenant_id
             WHERE e.tenant_id=@T AND e.source_id=@Id AND e.source_type IN ('collection','collection_cancel') AND l.account_code='10800'
            """, new { T = t, Id = linked });
        Assert.Equal(net.D, net.C);
        // 이월 수금은 안 꺼지고 역분개도 없다
        Assert.Equal(0, await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM journal_entries WHERE tenant_id=@T AND source_type='collection_cancel' AND source_id<>@Id", new { T = t, Id = linked }));
    }

    // ── F2 · 재고 안내 (순수) ──
    [Fact(DisplayName = "G9-e F2 EXPLAINED(설명 수와 정확히 같을 때만) · 나머지 DIFF 「설명된 n · 설명 안 되는 m」 · 재고 안내 줄")]
    public void G9e_F2_재고안내()
    {
        static ReconItem It(decimal l, decimal e) => MdbReconPosting.Item("k", "마스터", "상품", l, e, null);

        var ex = MdbReconPosting.ApplyExplained(It(100m, 1_250m), 1_150, "자동등록 1,149 · 받이 품목 1 =");
        Assert.Equal(MdbReconPosting.StatusExplained, ex.Status);
        Assert.Contains("사유 확인됨", ex.Detail);

        var partial = MdbReconPosting.ApplyExplained(It(100m, 1_252m), 1_150, "자동등록");
        Assert.Equal("DIFF", partial.Status);
        Assert.Contains("설명된 1,150", partial.Detail);
        Assert.Contains("설명 안 되는 2", partial.Detail);

        Assert.Equal("DIFF", MdbReconPosting.ApplyExplained(It(10m, 10m), 1, "받이 거래처").Status);   // 상쇄 → 숨기지 않음
        Assert.Equal("OK", MdbReconPosting.ApplyExplained(It(10m, 10m), 0, "받이 거래처").Status);     // 대조군
        Assert.Equal("DIFF", MdbReconPosting.ApplyExplained(It(10m, 12m), 0, "받이 거래처").Status);   // 대조군

        var items = new List<ReconItem>();
        var warnings = new List<string>();
        var info = new MdbLegacyFinalStock.RollForwardInfo("202512", "202602", 2, 10, 3, 1_500m);
        MdbReconPosting.AddStockItems(items, new List<ReconDiffRow>(), warnings, null, false, null, null, null, null, null, info);
        Assert.Contains(warnings, w => w.StartsWith("최종재고 표 뒤 2개월은"));
        Assert.Contains(warnings, w => w.Contains("수량 없이 금액만 있던 줄 3건"));
        Assert.Contains("202512 뒤 202602 까지 이어 계산", items.Single(i => i.Key == "stock_qty").Detail);
    }
}
