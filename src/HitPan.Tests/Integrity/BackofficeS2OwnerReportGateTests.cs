using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using HitPan.Backoffice.API.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 작14 B-0 게이트 — S-2 대표 아이디 수집 (F-10 완료 기준을 글자가 아니라 **동작으로** 잰다).
///
/// <para>근거: 작업지시서 20261008작14 §3 B-0 · DB명세서(F-10 · 사장님 결재 10/6) ·
/// 10/5 사장님 「명심」(백오피스는 대표 아이디·이메일 반드시 수집).</para>
///
/// <para>무엇을 재나:
/// ① 양성 — 정상 보고(licenseKey + ownerAccountId)가 4칸(아이디·시각·경로)을 실제로 채운다.
///    이 시험이 통과하려면 <c>installer/backoffice/32_backoffice_s2_owner.sql</c> 이
///    실물 SchemaMigrator 번호순 적용으로 깔려 있어야 한다 — DDL 적용 자체가 과녁에 포함된다.
/// ② 음성(금지 필드) — 허용 키(licenseKey·ownerAccountId) 밖 키가 하나라도 실리면 400 + 적재 0.
///    ①과 같은 payload 에 키 하나만 더한 것이라 ①이 양성 대조군이다(거부가 「원래 안 되는 것」이 아님을 증명).
/// ③ 음성(미상 라이선스) — 우리 키가 아니면 401 + 적재 0 (위조 행 방지).
/// ④ 음성(#40) — 비번·해시·토큰 모양 값은 아이디 자리라도 400 (본사 비번 0건 원칙).
/// </para>
///
/// <para>DB 없는 로컬 = SKIP 선언 · CI db-gate(HITPAN_REQUIRE_DB)에서는 SKIP 이 곧 FAIL
/// (<see cref="DbGateEnvironment.SkipOrFailStrict"/> — 하네스 TrySetUpAsync 내장).</para>
/// </summary>
public sealed class BackofficeS2OwnerReportGateTests : IDisposable
{
    private const string Pepper = "gate-pepper-s2-owner";
    private const string GoodLicense = "GATE-S2-OWNER-0001";

    private readonly BackofficeRoleGateHarness _h =
        new($"hitpan_gate_s2owner_{Guid.NewGuid():N}");

    public void Dispose() => _h.Dispose();

    // ── 재료 ─────────────────────────────────────────────────────────

    private IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BackofficeDb"] = _h.DbConnString(),
            ["License:Pepper"] = Pepper,
        })
        .Build();

    private TelemetryController Controller() =>
        new(Config(), NullLogger<TelemetryController>.Instance);

    /// <summary>제품(TelemetryController)과 같은 식 — 대조가 어긋나면 ③이 모든 시험을 깨뜨려 드러난다.</summary>
    private static string Hmac(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }

    private async Task<string> SeedTenantAsync()
    {
        var tenantId = Guid.NewGuid().ToString();
        var licHash = Hmac(GoodLicense.Trim().ToUpperInvariant().Replace(" ", ""), Pepper);
        await using var db = await _h.OpenAsync();
        await db.ExecuteAsync(@"
            INSERT INTO tenants (tenant_id, tenant_code, company_name, license_key_hash)
            VALUES (@Id, @Code, '게이트고객사', @Hash)",
            new { Id = tenantId, Code = $"G{Guid.NewGuid():N}"[..12], Hash = licHash });
        return tenantId;
    }

    private static async Task<IActionResult> PostAsync(TelemetryController c, string json) =>
        await c.ReportOwnerAccount(JsonDocument.Parse(json).RootElement, default);

    private async Task<(string? AccountId, DateTime? CollectedAt, string? Path)> RowAsync(string tenantId)
    {
        await using var db = await _h.OpenAsync();
        return await db.QueryFirstAsync<(string?, DateTime?, string?)>(
            "SELECT owner_account_id, owner_collected_at, owner_collect_path FROM tenants WHERE tenant_id = @Id",
            new { Id = tenantId });
    }

    // ── 게이트 ───────────────────────────────────────────────────────

    [Fact(DisplayName = "G-B0-1 🟢 양성 — 정상 보고가 대표 아이디·시각·경로 3칸을 실제로 채운다 (32번 DDL 적용 포함)")]
    public async Task G_B0_1_정상보고_적재()
    {
        if (!await _h.TrySetUpAsync("G-B0-1")) return;
        var tenantId = await SeedTenantAsync();

        var res = await PostAsync(Controller(),
            $$"""{"licenseKey":"{{GoodLicense}}","ownerAccountId":"boss01"}""");

        Assert.IsType<OkObjectResult>(res);
        var row = await RowAsync(tenantId);
        Assert.Equal("boss01", row.AccountId);
        Assert.NotNull(row.CollectedAt);
        Assert.Equal("erp_first_report", row.Path);
    }

    [Fact(DisplayName = "G-B0-2 🔴 음성 — 허용 키 밖 필드(비번류)가 실리면 400 + 적재 0 (양성 대조군 = G-B0-1 과 같은 payload)")]
    public async Task G_B0_2_금지필드_거부()
    {
        if (!await _h.TrySetUpAsync("G-B0-2")) return;
        var tenantId = await SeedTenantAsync();

        // G-B0-1 과 똑같은 payload 에 키 하나만 더했다 — 거부되면 그 키 때문이다.
        var res = await PostAsync(Controller(),
            $$"""{"licenseKey":"{{GoodLicense}}","ownerAccountId":"boss01","ownerPassword":"x"}""");

        var bad = Assert.IsType<BadRequestObjectResult>(res);
        Assert.Contains("허용되지 않은", bad.Value!.ToString());
        var row = await RowAsync(tenantId);
        Assert.Null(row.AccountId);      // 통째 거부 — 부분 적재도 없어야 한다
        Assert.Null(row.CollectedAt);
    }

    [Fact(DisplayName = "G-B0-3 🔴 음성 — 미상 라이선스는 401 + 적재 0 (위조 행 방지)")]
    public async Task G_B0_3_미상라이선스_거부()
    {
        if (!await _h.TrySetUpAsync("G-B0-3")) return;
        var tenantId = await SeedTenantAsync();

        var res = await PostAsync(Controller(),
            """{"licenseKey":"GATE-S2-WRONG-9999","ownerAccountId":"boss01"}""");

        Assert.IsType<UnauthorizedObjectResult>(res);
        var row = await RowAsync(tenantId);
        Assert.Null(row.AccountId);
    }

    [Fact(DisplayName = "G-B0-4 🔴 음성 — 비번·해시·토큰 모양 값은 아이디 자리라도 400 (#40 본사 비번 0건)")]
    public async Task G_B0_4_자격증명모양_거부()
    {
        if (!await _h.TrySetUpAsync("G-B0-4")) return;
        var tenantId = await SeedTenantAsync();

        // BCrypt 접두 모양(가짜 값) — 진짜 해시가 아니라 모양 검사 과녁이다.
        var res = await PostAsync(Controller(),
            $$"""{"licenseKey":"{{GoodLicense}}","ownerAccountId":"$2b$10$gate-shape-only"}""");

        Assert.IsType<BadRequestObjectResult>(res);
        var row = await RowAsync(tenantId);
        Assert.Null(row.AccountId);
    }

    [Fact(DisplayName = "G-B0-5 🟢 변경 보고 — 최신 보고가 이긴다(값 교체 + 시각 갱신)")]
    public async Task G_B0_5_변경보고_최신승()
    {
        if (!await _h.TrySetUpAsync("G-B0-5")) return;
        var tenantId = await SeedTenantAsync();

        Assert.IsType<OkObjectResult>(await PostAsync(Controller(),
            $$"""{"licenseKey":"{{GoodLicense}}","ownerAccountId":"boss01"}"""));
        Assert.IsType<OkObjectResult>(await PostAsync(Controller(),
            $$"""{"licenseKey":"{{GoodLicense}}","ownerAccountId":"boss02"}"""));

        var row = await RowAsync(tenantId);
        Assert.Equal("boss02", row.AccountId);
    }
}
