using System.Reflection;
using Dapper;
using HitPan.API.Controllers;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>LegacyUnpostedViewTenantGate</b> — 20260915작1 갈래 H (「이전 프로그램 장부에 반영되지 않은 명세서」 조회 · 읽기 전용).
/// </summary>
/// <remarks>
/// 격리 DB(<c>hitpan_unposted_view_*</c>)에 출하 DDL 을 적재하고 실제 서비스·컨트롤러를 부른다. 운영 무접촉(#39).
/// <list type="bullet">
///   <item>H1 회사 A 목록에 회사 B 보관 행이 없다 · B 명세서 상세를 A 로 부르면 없음(404).</item>
///   <item>H2 🔴 함정: B 명세서의 partner_id 가 A 거래처 id 와 같아도 A 거래처 이름이 붙지 않는다(조인 tenant 조건).</item>
///   <item>H3 기간·구분·사유·거래처(LIKE 특수문자) 조건 · 쪽 나눔 · 전체 합계 · 사유별 건수.</item>
///   <item>H4 컨트롤러는 GET 만 · tenant 를 받는 파라미터 없음 · 권한 = 자료 가져오기와 같은 TenantAdminOnly.</item>
/// </list>
/// CI(<c>db-gate</c>)는 <c>HITPAN_REQUIRE_DB</c> 로 SKIP=FAIL.
/// </remarks>
[Collection("DeviceAndKeyGate")]
public sealed class LegacyUnpostedViewTenantGateTests : IDisposable
{
    private readonly string _dbName = "hitpan_unposted_view_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;

    private readonly string _tenantA = Guid.NewGuid().ToString();
    private readonly string _tenantB = Guid.NewGuid().ToString();
    private const string PartnerA = "a0000000-0000-0000-0000-00000000000a";
    private const string PartnerB = "b0000000-0000-0000-0000-00000000000b";

    // ── 준비물 (MdbLegacyBalanceGateTests 와 같은 방식) ──
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
            Console.Error.WriteLine($"[H] 임시 DB 삭제 실패 {_dbName}: {ex.Message}");
        }
    }

    // ── 합성 자료 ──
    private static async Task PartnerAsync(MySqlConnection db, string id, string tenant, string name)
        => await db.ExecuteAsync("""
            INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, is_active, is_deleted, created_at, updated_at)
            VALUES (@Id, @T, @Code, @Name, 'both', 1, 0, NOW(6), NOW(6))
            """, new { Id = id, T = tenant, Code = "C" + id[..4], Name = name });

    private static async Task DocAsync(MySqlConnection db, string docId, string tenant, string io, string date, string legacyDt,
        int seq, string? partnerId, string reason, decimal supply, decimal vat, int lines)
    {
        await db.ExecuteAsync("""
            INSERT INTO legacy_unposted_documents
              (doc_id, tenant_id, io_type, doc_date, legacy_dt, legacy_seq, legacy_buy_code, partner_id, reason,
               supply_amount, vat_amount, line_count, memo, source_type, source_id, migrated_source_hash)
            VALUES (@D, @T, @Io, @Date, @Dt, @Seq, 1, @P, @R, @S, @V, @L, NULL, 'migration', CONCAT('mig-docfb-', @D), NULL)
            """, new { D = docId, T = tenant, Io = io, Date = date, Dt = legacyDt, Seq = seq, P = partnerId, R = reason, S = supply, V = vat, L = lines });
        for (var i = 1; i <= lines; i++)
        {
            await db.ExecuteAsync("""
                INSERT INTO legacy_unposted_document_lines
                  (line_id, tenant_id, doc_id, line_no, item_id, item_name, spec, qty, unit_price, supply_amount, vat_amount, memo, stock_source_id, migrated_source_hash)
                VALUES (UUID(), @T, @D, @N, NULL, CONCAT('품목', @N), NULL, @N, 100, @S, 0, NULL, NULL, SHA2(CONCAT(@D, '#', @N), 256))
                """, new { T = tenant, D = docId, N = i, S = supply / lines });
        }
    }

    private async Task SeedAsync(MySqlConnection db)
    {
        await PartnerAsync(db, PartnerA, _tenantA, "가나상사");
        await PartnerAsync(db, PartnerB, _tenantB, "다라_100%상회");
        // 회사 A — 판매 2 · 매입 1(날짜 없음 → 기준일)
        await DocAsync(db, "doc-a-1", _tenantA, "sales", "2025-03-10", "20250310", 27, PartnerA, "assembly", 1000m, 100m, 2);
        await DocAsync(db, "doc-a-2", _tenantA, "sales", "2025-11-11", "20251111", 5, null, "other", 50.5m, -5m, 1);
        await DocAsync(db, "doc-a-3", _tenantA, "purchase", "2026-02-28", "00000000", 0, PartnerA, "danga", 0m, 1m, 3);
        // 회사 B — 🔴 partner_id 가 A 거래처 id 와 같은 행(조인 tenant 함정) + 자기 거래처 행
        await DocAsync(db, "doc-b-1", _tenantB, "sales", "2025-03-10", "20250310", 27, PartnerA, "assembly", 7777m, 0m, 1);
        await DocAsync(db, "doc-b-2", _tenantB, "purchase", "2025-06-01", "20250601", 3, PartnerB, "other", 10m, 1m, 1);
    }

    private LegacyUnpostedDocumentService Service(MySqlConnection db)
        => new(db, NullLogger<LegacyUnpostedDocumentService>.Instance);

    [Fact(DisplayName = "H1·H2 🔴 다른 회사 보관 행이 안 보인다 · 상세 404 · 다른 회사 거래처 이름이 안 붙는다")]
    public async Task H1_H2_TenantIsolation()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(H1_H2_TenantIsolation)); return; }
        SetUpFreshInstall();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SeedAsync(db);
        var svc = Service(db);

        var a = await svc.GetPagedAsync(_tenantA, new LegacyUnpostedQuery { PageSize = 50 });
        Assert.Equal(3, a.Page.TotalCount);
        Assert.Equal(new[] { "doc-a-1", "doc-a-2", "doc-a-3" }, a.Page.Items.Select(i => i.DocId).OrderBy(x => x));
        Assert.Equal(6, a.TotalLines);
        Assert.Equal(1050.5m, a.TotalSupply);
        Assert.Equal(96m, a.TotalVat);

        var b = await svc.GetPagedAsync(_tenantB, new LegacyUnpostedQuery());
        Assert.Equal(2, b.Page.TotalCount);
        Assert.DoesNotContain(b.Page.Items, i => i.DocId.StartsWith("doc-a-", StringComparison.Ordinal));
        var trap = b.Page.Items.Single(i => i.DocId == "doc-b-1");
        Assert.True(trap.PartnerName is null,
            $"🔴 회사 B 명세서에 회사 A 거래처 이름 「{trap.PartnerName}」 이 붙었다 — 거래처 조인에 tenant 조건이 없다");

        // 상세 — 다른 회사 명세서는 없음
        Assert.Null(await svc.GetDetailAsync(_tenantA, "doc-b-1"));
        var detail = await svc.GetDetailAsync(_tenantA, "doc-a-3");
        Assert.NotNull(detail);
        Assert.Equal(3, detail!.Lines.Count);
        Assert.Equal(new[] { 1, 2, 3 }, detail.Lines.Select(l => l.LineNo));
        Assert.True(detail.Document.DateMissing);
        Assert.Equal("매입", detail.Document.IoTypeText);
        Assert.Equal("단가 기록 줄", detail.Document.ReasonText);

        // 컨트롤러 — tenant 는 HttpContext.Items 에서만
        var ctl = new LegacyUnpostedDocumentsController(svc, NullLogger<LegacyUnpostedDocumentsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        ctl.HttpContext.Items["TenantId"] = _tenantA;
        Assert.IsType<NotFoundObjectResult>(await ctl.GetDetail("doc-b-2"));
        var ok = Assert.IsType<OkObjectResult>(await ctl.GetList(null, null, null, null, null));
        Assert.Equal(3, ((LegacyUnpostedListResult)ok.Value!).Page.TotalCount);

        var noTenant = new LegacyUnpostedDocumentsController(svc, NullLogger<LegacyUnpostedDocumentsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        Assert.IsType<ForbidResult>(await noTenant.GetList(null, null, null, null, null));
    }

    [Fact(DisplayName = "H3 조건 · 쪽 나눔 · 사유별 건수 · LIKE 특수문자는 글자")]
    public async Task H3_FiltersAndPaging()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(H3_FiltersAndPaging)); return; }
        SetUpFreshInstall();
        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await SeedAsync(db);
        var svc = Service(db);

        var purchase = await svc.GetPagedAsync(_tenantA, new LegacyUnpostedQuery { IoType = "purchase" });
        Assert.Equal("doc-a-3", Assert.Single(purchase.Page.Items).DocId);

        var bogusIo = await svc.GetPagedAsync(_tenantA, new LegacyUnpostedQuery { IoType = "x' OR 1=1 --" });
        Assert.Equal(3, bogusIo.Page.TotalCount);   // 모르는 값은 조건에서 뺀다

        var ranged = await svc.GetPagedAsync(_tenantA, new LegacyUnpostedQuery { From = new DateTime(2025, 4, 1), To = new DateTime(2025, 12, 31) });
        Assert.Equal("doc-a-2", Assert.Single(ranged.Page.Items).DocId);

        var byPartner = await svc.GetPagedAsync(_tenantA, new LegacyUnpostedQuery { Partner = "가나" });
        Assert.Equal(2, byPartner.Page.TotalCount);

        var pct = await svc.GetPagedAsync(_tenantA, new LegacyUnpostedQuery { Partner = "%" });
        Assert.Equal(0, pct.Page.TotalCount);   // % 가 전부 맞춤이 되면 안 된다
        var pctB = await svc.GetPagedAsync(_tenantB, new LegacyUnpostedQuery { Partner = "_100%" });
        Assert.Equal("doc-b-2", Assert.Single(pctB.Page.Items).DocId);

        var byReason = await svc.GetPagedAsync(_tenantA, new LegacyUnpostedQuery { Reason = "assembly" });
        Assert.Equal("doc-a-1", Assert.Single(byReason.Page.Items).DocId);
        // 사유별 건수는 사유 조건을 뺀 같은 조건 — 셋 다 보인다
        Assert.Equal(3, byReason.Reasons.Count);
        Assert.Equal("조립·해체", byReason.Reasons.Single(r => r.Reason == "assembly").ReasonText);

        var p1 = await svc.GetPagedAsync(_tenantA, new LegacyUnpostedQuery { Page = 1, PageSize = 2 });
        var p2 = await svc.GetPagedAsync(_tenantA, new LegacyUnpostedQuery { Page = 2, PageSize = 2 });
        Assert.Equal(2, p1.Page.Items.Count);
        Assert.Single(p2.Page.Items);
        Assert.Equal(2, p1.Page.TotalPages);
        Assert.Empty(p1.Page.Items.Select(i => i.DocId).Intersect(p2.Page.Items.Select(i => i.DocId)));
        Assert.Equal("doc-a-3", p1.Page.Items[0].DocId);   // 최근 날짜 먼저

        var huge = await svc.GetPagedAsync(_tenantA, new LegacyUnpostedQuery { PageSize = 100_000 });
        Assert.Equal(LegacyUnpostedDocumentService.MaxPageSize, huge.Page.PageSize);
    }

    [Fact(DisplayName = "H4 🔴 조회 API 는 읽기만 — GET 만 · tenant 파라미터 없음 · 자료 가져오기와 같은 권한")]
    public void H4_ReadOnlyApiShape()
    {
        var type = typeof(LegacyUnpostedDocumentsController);
        var actions = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(actions);
        foreach (var m in actions)
        {
            var verbs = m.GetCustomAttributes<HttpMethodAttribute>(true).SelectMany(a => a.HttpMethods).ToList();
            Assert.True(verbs.Count > 0 && verbs.All(v => v == "GET"), $"🔴 {m.Name} 이 GET 이 아니다: {string.Join(",", verbs)}");
            Assert.DoesNotContain(m.GetParameters(), p => p.Name!.Contains("tenant", StringComparison.OrdinalIgnoreCase));
        }

        static string? Policy(Type t) => t.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()?.Policy;
        Assert.Equal(Policy(typeof(MigrationController)), Policy(type));
        Assert.Equal("TenantAdminOnly", Policy(type));

        // 서비스 인터페이스에 쓰기 메서드가 없다
        Assert.All(typeof(ILegacyUnpostedDocumentService).GetMethods(), m => Assert.StartsWith("Get", m.Name, StringComparison.Ordinal));
    }
}
