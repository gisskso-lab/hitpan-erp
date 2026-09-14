using System.Data;
using Dapper;
using HitPan.API.Controllers;
using HitPan.Application.Common;
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

/// <summary>
/// 🔴 <b>G4 MdbLegacyBalanceGate</b> — 20260915작1 갈래 E (「이전 프로그램 최종잔액 맞춤」 · 설계 §17 · R-A2 (나)).
/// </summary>
/// <remarks>
/// 격리 DB(<c>hitpan_legacy_bal_*</c>)에 출하 DDL 을 적재하고 실제 서비스·컨트롤러를 부른다. 운영 무접촉(#39).
/// 합성 DOCF5 F3: 옛 코드 10(150,000)+11(20,000) → 거래처 P1 170,000 · 20 → P2 −80,000 · 30(판매·매입 양쪽) → P4 6,000 · 99(매핑 없음) → 폴백 7,000.
/// 이관 행: P1 명세서 1,000,000 · 이관 수금 200,000 · P2 이관 매입 500,000 · 이관 지급 100,000 (F3 에 이미 들어 있는 이력 — 다시 세면 FAIL).
/// 사람 입력 대조군: P3 명세서 300,000 + 연결 수금 100,000 · 매입 40,000 (source_type NULL 수금 포함).
/// 기대: 미수 = 183,000 + 200,000 = 383,000 · 미지급 = 80,000 + 40,000 = 120,000.
/// CI(<c>db-gate</c>)는 <c>HITPAN_REQUIRE_DB</c> 로 SKIP=FAIL.
/// </remarks>
[Collection("DeviceAndKeyGate")]
public sealed class MdbLegacyBalanceGateTests : IDisposable
{
    private readonly string _dbName = "hitpan_legacy_bal_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;

    private readonly string _tenant = Guid.NewGuid().ToString();
    private const string P1 = "e0000000-0000-0000-0000-000000000001";
    private const string P2 = "e0000000-0000-0000-0000-000000000002";
    private const string P3 = "e0000000-0000-0000-0000-000000000003";
    private const string P4 = "e0000000-0000-0000-0000-000000000004";
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
            Console.Error.WriteLine($"[G4] 임시 DB 삭제 실패 {_dbName}: {ex.Message}");
        }
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
        R(10, "20260100", "0", 90_000m, 0m, 0);          // 옛 이월(마지막 아님)
        R(10, "20260200", "0", 100_000m, 0m, 0);         // 마지막 이월
        R(10, "20260210", "0", 50_000m, 0m, 3);          // 그 뒤 판매
        R(11, "20260100", "0", 30_000m, 0m, 0);
        R(11, "20260105", "1", 0m, 10_000m, 4);          // 그 뒤 수금
        R(20, "20260200", "0", -80_000m, 0m, 0);         // 미지급
        R(30, "20260200", "0", 5_000m, 0m, 0);           // 양쪽 거래처
        R(30, "20260215", "A", 0m, 3_000m, 5);
        R(30, "20260216", "0", 4_000m, 0m, 6);
        R(99, "20260200", "0", 7_000m, 0m, 0);           // 매핑 없음 → 폴백
        return t;
    }

    private static readonly IReadOnlyDictionary<int, string> PartnerMap =
        new Dictionary<int, string> { [10] = P1, [11] = P1, [20] = P2, [30] = P4 };

    private async Task SeedAsync(MySqlConnection db)
    {
        var now = DateTime.Now;
        foreach (var (id, code) in new[] { (P1, "E1"), (P2, "E2"), (P3, "E3"), (P4, "E4") })
        {
            await db.ExecuteAsync("""
                INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, is_active, is_deleted, created_at, updated_at)
                VALUES (@Id, @T, @Code, CONCAT('거래처', @Code), 'both', 1, 0, @Now, @Now)
                """, new { Id = id, T = _tenant, Code = code, Now = now });
        }

        async Task Delivery(string id, string partner, decimal amt, string source)
            => await db.ExecuteAsync("""
                INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, partner_id, delivery_date, source_type, status, total_amount, vat_amount, is_deleted, created_at, updated_at)
                VALUES (@Id, @T, @Id, @P, '2026-01-10', @S, 'confirmed', @A, 0, 0, @Now, @Now)
                """, new { Id = id, T = _tenant, P = partner, A = amt, S = source, Now = now });
        async Task Receipt(string id, string partner, decimal amt, string source)
            => await db.ExecuteAsync("""
                INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, partner_id, receipt_date, source_type, status, total_amount, vat_amount, created_at)
                VALUES (@Id, @T, @Id, @P, '2026-01-10', @S, 'confirmed', @A, 0, @Now)
                """, new { Id = id, T = _tenant, P = partner, A = amt, S = source, Now = now });

        // 이관 이력 (F3 에 이미 들어 있음)
        await Delivery("mig-d-1", P1, 1_000_000m, "migration");
        // 수금이 안 붙은 이관 명세서 — 연체 버킷 뷰 식은 수금 붙은 명세서를 원래 빼므로, 이 줄이 없으면 aging 의 이관 제외를 못 잰다(M8 실측).
        await Delivery("mig-d-1b", P1, 50_000m, "migration");
        await db.ExecuteAsync("""
            INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active, source_type, source_id)
            VALUES (UUID(), @T, @P, '2026-01-20', 200000, 'sales_delivery', 'mig-d-1', 1, 'migration', 'mig-c-1')
            """, new { T = _tenant, P = P1 });
        await Receipt("mig-r-2", P2, 500_000m, "migration");
        await db.ExecuteAsync("""
            INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, ref_order_id, is_active, source_type, source_id)
            VALUES (UUID(), @T, @P, 'purchase', 100000, '2026-01-20', 'mig-r-2', 1, 'migration', 'mig-p-2')
            """, new { T = _tenant, P = P2 });

        // 사람 입력 대조군 (source_type NULL 수금 — COALESCE 함정)
        await Delivery("hum-d-3", P3, 300_000m, "direct");
        await db.ExecuteAsync("""
            INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active)
            VALUES (UUID(), @T, @P, '2026-03-02', 100000, 'sales_delivery', 'hum-d-3', 1)
            """, new { T = _tenant, P = P3 });
        await Receipt("hum-r-3", P3, 40_000m, "direct");
    }

    private static void ClearDashboardCache()
    {
        var f = typeof(FinanceService).GetField("_dashCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        ((System.Collections.IDictionary)f.GetValue(null)!).Clear();
    }

    private async Task<(decimal Rec, decimal Pay)> KpiAsync(MySqlConnection db)
    {
        ClearDashboardCache();
        var svc = new FinanceService(db, Mock.Of<IAuditService>(), Mock.Of<INotificationService>());
        var d = await svc.GetDashboardAsync(_tenant);
        return (d.UnpaidReceivable, d.UnpaidPayable);
    }

    [Fact(DisplayName = "G4 🔴 이월잔액 맞춤 — KPI=F3 · 옛 코드 2개 합산 · 재실행 0 · 사람 입력 누적 · 8곳 각 식 반영 · 두 번 안 더함")]
    public async Task G4_LegacyBalance_AllFormulas()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(G4_LegacyBalance_AllFormulas)); return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SeedAsync(db);

        // 봉합 전 식: 이관 이력만 보인다(대조 — 식이 이월잔액 없이도 도는지)
        var before = await KpiAsync(db);
        Assert.Equal(1_050_000m, before.Rec);   // 1,050,000 − 200,000 + 300,000 − 100,000
        Assert.Equal(440_000m, before.Pay);     // 500,000 − 100,000 + 40,000

        // ① 적재 · 옛 코드 2개 → 한 거래처 1행 · 폴백
        var n1 = await MdbLegacyPartnerBalance.ApplyAsync(db, _tenant, Docf5(), PartnerMap, BaseDate, CancellationToken.None);
        Assert.Equal(4, n1);
        var rows = (await db.QueryAsync<(string PartnerId, decimal Amt, DateTime Base)>(
            "SELECT partner_id, balance_amount, base_date FROM partner_legacy_balances WHERE tenant_id=@T", new { T = _tenant })).ToList();
        Assert.Equal(4, rows.Count);
        Assert.Equal(170_000m, rows.Single(r => r.PartnerId == P1).Amt);
        Assert.Equal(-80_000m, rows.Single(r => r.PartnerId == P2).Amt);
        Assert.Equal(6_000m, rows.Single(r => r.PartnerId == P4).Amt);
        Assert.All(rows, r => Assert.Equal(BaseDate, r.Base));
        var fallback = await db.ExecuteScalarAsync<string>(
            "SELECT partner_id FROM partners WHERE tenant_id=@T AND partner_code='LEGACY_UNKNOWN_PTNR'", new { T = _tenant });
        Assert.Equal(7_000m, rows.Single(r => r.PartnerId == fallback).Amt);

        // ② KPI(식 1·2) = F3 + 사람 입력 · 이관 이력은 두 번 안 센다
        var after = await KpiAsync(db);
        Assert.True(after.Rec == 383_000m, $"🔴 KPI 미수 {after.Rec:N0} ≠ 383,000 (F3 183,000 + 사람 200,000) — 이관 이력 이중 계상 또는 잔액 누락");
        Assert.True(after.Pay == 120_000m, $"🔴 KPI 미지급 {after.Pay:N0} ≠ 120,000 (F3 80,000 + 사람 40,000)");

        // ③ 재실행 0 변화
        var n2 = await MdbLegacyPartnerBalance.ApplyAsync(db, _tenant, Docf5(), PartnerMap, BaseDate, CancellationToken.None);
        Assert.Equal(4, n2);
        Assert.Equal(4, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM partner_legacy_balances WHERE tenant_id=@T", new { T = _tenant }));
        Assert.Equal(after, await KpiAsync(db));

        // ④ 식 3·4 명세서별 미수·미지급 요약
        var col = new CollectionService(db, Mock.Of<IAuditService>());
        var recv = await col.GetReceivablesAsync(_tenant);
        Assert.Equal(170_000m, recv.Summary.Single(s => s.PartnerId == P1).Outstanding);
        Assert.Equal(200_000m, recv.Summary.Single(s => s.PartnerId == P3).Outstanding);
        Assert.Equal(383_000m, recv.Summary.Sum(s => s.Outstanding));
        Assert.DoesNotContain(recv.Documents, d => d.DeliveryId.StartsWith("mig-d-"));
        var pays = await col.GetPayablesAsync(_tenant);
        Assert.Equal(80_000m, pays.Summary.Single(s => s.PartnerId == P2).Outstanding);
        Assert.Equal(120_000m, pays.Summary.Sum(s => s.Outstanding));

        // ⑤ 식 5 partner_balance 읽기 3곳 (표 비어 있음 → 이월 미수만)
        var tenantMock = new Mock<ICurrentTenant>();
        tenantMock.SetupGet(x => x.TenantId).Returns(_tenant);
        var ctxOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(DbConnString(), new MariaDbServerVersion(new Version(11, 4, 0))).Options;
        await using var ef = new AppDbContext(ctxOptions, tenantMock.Object, Mock.Of<IEncryptionService>());
        var partnerSvc = new PartnerService(tenantMock.Object, new PartnerBalanceRepository(ef), db, Mock.Of<IGeocodingService>());
        var list = await partnerSvc.GetPartnerListAsync(_tenant);
        Assert.Equal(170_000m, list.Single(p => p.PartnerId == P1).Balance);
        Assert.Equal(0m, list.Single(p => p.PartnerId == P2).Balance);   // − 잔액은 미수 칸에 안 들어간다
        var paged = await partnerSvc.GetPartnerListPagedAsync(_tenant, new PagedRequest { Page = 1, PageSize = 50 });
        Assert.Equal(170_000m, paged.Items.Single(p => p.PartnerId == P1).Balance);
        Assert.Equal(6_000m, (await partnerSvc.GetPartnerDetailAsync(P4, _tenant))!.Balance);

        // ⑥ 식 6 v_partner_balance → 잔액 API
        var b2 = await partnerSvc.GetBalanceAsync(P2);
        Assert.NotNull(b2);
        Assert.Equal(80_000m, b2!.PayableBalance);
        Assert.Equal(0m, b2.ReceivableBalance);
        Assert.Equal(170_000m, (await partnerSvc.GetBalanceAsync(P1))!.ReceivableBalance);

        // ⑦ 식 8 연체 버킷 API
        var ctl = new PartnerController(partnerSvc) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        ctl.HttpContext.Items["TenantId"] = _tenant;
        var ok = Assert.IsType<OkObjectResult>(await ctl.GetAging(db, CancellationToken.None));
        var aging = ((IEnumerable<object>)ok.Value!).Cast<IDictionary<string, object>>().ToList();
        var p1 = aging.Single(r => (string)r["PartnerId"] == P1);
        Assert.True(Convert.ToDecimal(p1["TotalUnpaid"]) == 170_000m,
            $"🔴 연체 버킷 P1 {p1["TotalUnpaid"]} ≠ 170,000 — 이관 명세서 1,000,000 이 다시 들어갔거나 이월잔액 누락");
        Assert.DoesNotContain(aging, r => (string)r["PartnerId"] == P2);   // 미지급 거래처는 미수 연체에 없다
    }

    [Fact(DisplayName = "G4 대조 — DOCF5 없음이면 적재 0 · 식은 종전 그대로")]
    public async Task G4_NoDocf5_Unchanged()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(G4_NoDocf5_Unchanged)); return; }
        SetUpFreshInstall();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SeedAsync(db);
        Assert.Equal(0, await MdbLegacyPartnerBalance.ApplyAsync(db, _tenant, null, PartnerMap, BaseDate, CancellationToken.None));
        Assert.Equal(0, await MdbLegacyPartnerBalance.ApplyAsync(db, _tenant, new DataTable(), PartnerMap, BaseDate, CancellationToken.None));
        var k = await KpiAsync(db);
        Assert.Equal(1_050_000m, k.Rec);
        Assert.Equal(440_000m, k.Pay);
    }
}
