using System.Text.Json;
using Dapper;
using HitPan.Backoffice.API.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 작14 B-5·B-6 게이트 — CS 수신의 **게이트 ⓪(3중 일치)** 와 자물쇠를 동작으로 잰다
/// (작업지시서 B-게이트: "하나만 틀린 요청 3종 **각각** 거부 + 폐기 시리얼 거부 + 양성 대조군 ·
///  남용 게이트 2개 · 전부 CI db-gate 등록" — CTO 조건①·②).
///
/// <para>격리 DB + 실물 SchemaMigrator(installer/backoffice 전체 — 40_backoffice_cs.sql 적용 자체가 과녁).
/// 컨트롤러는 실물 설정(연결문자열+Pepper)으로 직접 돌린다(S-2 게이트와 같은 방식).
/// 라우팅·[AllowAnonymous] 표면은 BackofficeAnonymousAdminApiGateTests 가 따로 감시한다(기지 등록).</para>
/// </summary>
public sealed class CsInboundGateTests : IDisposable
{
    private const string Pepper = "gate-pepper-cs-inbound";
    private const string GoodLicense = "GATE-CS-IN-0001";
    private const string TenantCode = "GATECS";
    private const string OwnerId = "boss@gate.test";

    private readonly BackofficeRoleGateHarness _h =
        new($"hitpan_gate_csin_{Guid.NewGuid():N}");

    public void Dispose() => _h.Dispose();

    // ── 재료 ─────────────────────────────────────────────────────────

    private CsInboundController Controller() => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BackofficeDb"] = _h.DbConnString(),
            ["License:Pepper"] = Pepper,
        }).Build(),
        NullLogger<CsInboundController>.Instance);

    private static string Hmac(string data, string key)
    {
        using var h = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }

    private async Task<string> SeedTenantAsync(bool withOwner = true)
    {
        var tenantId = Guid.NewGuid().ToString();
        await using var db = await _h.OpenAsync();
        await db.ExecuteAsync(@"
            INSERT INTO tenants (tenant_id, tenant_code, company_name, license_key_hash, owner_account_id)
            VALUES (@Id, @Code, '게이트고객사', @Hash, @Owner)",
            new
            {
                Id = tenantId,
                Code = TenantCode,
                Hash = Hmac(GoodLicense.Trim().ToUpperInvariant().Replace(" ", ""), Pepper),
                Owner = withOwner ? OwnerId : null,
            });
        return tenantId;
    }

    /// <summary>봉투 생성 — 셋 중 바꿔 끼울 축만 바꾼다(「하나만 틀린 요청 3종」의 재료).</summary>
    private static string Envelope(string? code = null, string? license = null, string? owner = null, string? dataJson = null)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["tenantCode"] = code ?? TenantCode,
            ["licenseKey"] = license ?? GoodLicense,
            ["ownerAccountId"] = owner ?? OwnerId,
            ["data"] = JsonSerializer.Deserialize<JsonElement>(
                dataJson ?? """{"csRequestId":"req-1","category":"how_to","subTag":"etc","body":"저장이 안 돼요","screenCode":"gate","erpVersion":"0"}"""),
        });

    private static async Task<IActionResult> PostAsync(CsInboundController c, string envelopeJson)
        => await c.ReceiveMessage(JsonDocument.Parse(envelopeJson).RootElement, default);

    private async Task<int> RejectCountAsync(string rule)
    {
        await using var db = await _h.OpenAsync();
        return await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM bo_cs_reject_logs WHERE rule_code = @R", new { R = rule });
    }

    private async Task<int> TicketCountAsync()
    {
        await using var db = await _h.OpenAsync();
        return await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM bo_cs_tickets");
    }

    private static int StatusOf(IActionResult r) => r switch
    {
        OkObjectResult => 200,
        BadRequestObjectResult => 400,
        UnauthorizedObjectResult => 401,
        ObjectResult o => o.StatusCode ?? 0,
        _ => 0,
    };

    // ── 게이트 ⓪ — 3중 일치 ────────────────────────────────────────

    [Fact(DisplayName = "G-B5-0 🟢 양성 대조군 — 3중이 전부 맞으면 200 · 티켓 1행(멱등키·7모양 보조태그·8종 정본)")]
    public async Task G_B5_0_양성()
    {
        if (!await _h.TrySetUpAsync("G-B5-0")) return;
        await SeedTenantAsync();

        Assert.Equal(200, StatusOf(await PostAsync(Controller(), Envelope())));

        await using var db = await _h.OpenAsync();
        var row = await db.QueryFirstAsync(@"
            SELECT client_ticket_uid AS Uid, category AS Cat, shape_tag AS Shape,
                   received_channel AS Ch, body AS Body FROM bo_cs_tickets");
        Assert.Equal("req-1", (string)row.Uid);
        Assert.Equal("use", (string)row.Cat);          // how_to → 8종 정본 use (결-9)
        Assert.Equal("how_to", (string)row.Shape);     // 7모양 보조 태그
        Assert.Equal("erp_message", (string)row.Ch);

        // 멱등 — 같은 쪽지 한 번 더 = 200 + 여전히 1행(터널 복구 재전송 자리)
        Assert.Equal(200, StatusOf(await PostAsync(Controller(), Envelope())));
        Assert.Equal(1, await TicketCountAsync());
    }

    [Theory(DisplayName = "G-B5-1 🔴 하나만 틀린 요청 3종 — 각각 401 거부 + 거부로그 + 적재 0 (폐기 시리얼 = 둘째 축)")]
    [InlineData("code")]
    [InlineData("license")] // 폐기(회전된) 시리얼 = 옛 키 → 해시 불일치 — 같은 축으로 거부된다
    [InlineData("owner")]
    public async Task G_B5_1_한축틀림_거부(string axis)
    {
        if (!await _h.TrySetUpAsync($"G-B5-1-{axis}")) return;
        await SeedTenantAsync();

        var env = axis switch
        {
            "code" => Envelope(code: "WRONGCODE"),
            "license" => Envelope(license: "GATE-CS-IN-REVOKED"),
            _ => Envelope(owner: "stranger@gate.test"),
        };
        Assert.Equal(401, StatusOf(await PostAsync(Controller(), env)));
        Assert.Equal(1, await RejectCountAsync("auth_mismatch"));
        Assert.Equal(0, await TicketCountAsync());
    }

    [Fact(DisplayName = "G-B5-2 🔴 부모계정 미등록(B-0 첫 보고 전) — 401 + owner_not_registered 로그 · 보고가 채워지면 열린다")]
    public async Task G_B5_2_부모미등록()
    {
        if (!await _h.TrySetUpAsync("G-B5-2")) return;
        var tenantId = await SeedTenantAsync(withOwner: false);

        Assert.Equal(401, StatusOf(await PostAsync(Controller(), Envelope())));
        Assert.Equal(1, await RejectCountAsync("owner_not_registered"));

        // B-0 첫 보고 도착(owner 칸이 채워짐) → 같은 요청이 그대로 열린다(자가 해소 고리)
        await using (var db = await _h.OpenAsync())
            await db.ExecuteAsync("UPDATE tenants SET owner_account_id = @O WHERE tenant_id = @Id",
                new { O = OwnerId, Id = tenantId });
        Assert.Equal(200, StatusOf(await PostAsync(Controller(), Envelope())));
    }

    // ── B-6 — 남용 게이트 2개 ───────────────────────────────────────

    [Fact(DisplayName = "G-B5-3 🔴 연속 인증 실패 잠금 — 5회 뒤에는 **맞는 재료도** 429 · 거부로그 locked")]
    public async Task G_B5_3_연속실패잠금()
    {
        if (!await _h.TrySetUpAsync("G-B5-3")) return;
        await SeedTenantAsync();
        var c = Controller();

        for (var i = 0; i < 5; i++)
            Assert.Equal(401, StatusOf(await PostAsync(c, Envelope(owner: "stranger@gate.test"))));

        // 잠금 창 안에서는 맞는 재료도 받지 않는다 — 자물쇠가 진짜 잠겼다는 증거.
        Assert.Equal(429, StatusOf(await PostAsync(c, Envelope())));
        Assert.Equal(1, await RejectCountAsync("locked"));
        Assert.Equal(0, await TicketCountAsync());
    }

    [Fact(DisplayName = "G-B5-4 🔴 시간당 접수 상한 — 30건 뒤 429 rate_limited (ERP 는 재시도로 다뤄 글 유실 0)")]
    public async Task G_B5_4_시간당상한()
    {
        if (!await _h.TrySetUpAsync("G-B5-4")) return;
        var tenantId = await SeedTenantAsync();

        await using (var db = await _h.OpenAsync())
            for (var i = 0; i < 30; i++)
                await db.ExecuteAsync(@"
                    INSERT INTO bo_cs_tickets (tenant_id, client_ticket_uid, received_channel, category, sub_tag, received_at)
                    VALUES (@T, @U, 'erp_message', 'use', 'etc', UTC_TIMESTAMP(6))",
                    new { T = tenantId, U = $"seed-{i}" });

        Assert.Equal(429, StatusOf(await PostAsync(Controller(), Envelope())));
        Assert.Equal(1, await RejectCountAsync("rate_limited"));
        Assert.Equal(30, await TicketCountAsync()); // 31번째는 안 들어왔다
    }

    // ── 문③ — 수신이 최종 판정자 ────────────────────────────────────

    [Fact(DisplayName = "G-B5-5 🔴 문③ — 식별번호 모양 본문 400 + forbidden_field 로그 + 적재 0 (양성 대조군 = G-B5-0)")]
    public async Task G_B5_5_문3_본문거부()
    {
        if (!await _h.TrySetUpAsync("G-B5-5")) return;
        await SeedTenantAsync();

        var env = Envelope(dataJson:
            """{"csRequestId":"req-x","category":"how_to","subTag":"etc","body":"사업자 000-00-00000 등록이 안 돼요","screenCode":"gate","erpVersion":"0"}""");
        Assert.Equal(400, StatusOf(await PostAsync(Controller(), env)));
        Assert.Equal(1, await RejectCountAsync("forbidden_field"));
        Assert.Equal(0, await TicketCountAsync());
    }

    [Fact(DisplayName = "G-B5-6 🔴 문③ — 화이트리스트 밖 필드(비번류 키)가 실리면 통째 400 (구버전 ERP 가 최종 판정을 못 피한다)")]
    public async Task G_B5_6_문3_필드거부()
    {
        if (!await _h.TrySetUpAsync("G-B5-6")) return;
        await SeedTenantAsync();

        var env = Envelope(dataJson:
            """{"csRequestId":"req-y","category":"how_to","subTag":"etc","body":"ok","ownerPassword":"x"}""");
        Assert.Equal(400, StatusOf(await PostAsync(Controller(), env)));
        Assert.Equal(1, await RejectCountAsync("forbidden_field"));
        Assert.Equal(0, await TicketCountAsync());
    }

    // ── 답 — 반자동 ③겹 + Pull ─────────────────────────────────────

    [Fact(DisplayName = "G-B5-7 🔴 승인 없인 답의 몸통이 없다 — approved_by NULL INSERT 는 DB 가 거절(1048)")]
    public async Task G_B5_7_승인없는답_불생성()
    {
        if (!await _h.TrySetUpAsync("G-B5-7")) return;
        var tenantId = await SeedTenantAsync();

        await using var db = await _h.OpenAsync();
        var ex = await Assert.ThrowsAsync<MySqlException>(() => db.ExecuteAsync(@"
            INSERT INTO bo_cs_replies (reply_id, ticket_id, tenant_id, client_ticket_uid, body, replied_by_kind, approved_by, approved_at)
            VALUES ('r-x', 1, @T, 'req-1', '답', 'hq', NULL, UTC_TIMESTAMP(6))",
            new { T = tenantId }));
        Assert.Equal(1048, ex.Number); // Column cannot be null — 사람 확정 없인 물리적으로 못 만든다
    }

    [Fact(DisplayName = "G-B5-8 🟢 답 Pull — 3중 통과 테넌트의 승인분만 나간다 · delivered_at 기록")]
    public async Task G_B5_8_답Pull()
    {
        if (!await _h.TrySetUpAsync("G-B5-8")) return;
        var tenantId = await SeedTenantAsync();

        await using (var db = await _h.OpenAsync())
            await db.ExecuteAsync(@"
                INSERT INTO bo_cs_replies (reply_id, ticket_id, tenant_id, client_ticket_uid, body, replied_by_kind, approved_by, approved_at)
                VALUES ('r-1', 1, @T, 'req-1', '확인했습니다', 'hq', 'admin-1', UTC_TIMESTAMP(6))",
                new { T = tenantId });

        var res = await Controller().PullReplies(
            JsonDocument.Parse(Envelope(dataJson: "{}")).RootElement, default);
        var ok = Assert.IsType<OkObjectResult>(res);
        Assert.Contains("r-1", JsonSerializer.Serialize(ok.Value));

        await using (var db = await _h.OpenAsync())
            Assert.Equal(1, await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM bo_cs_replies WHERE delivered_at IS NOT NULL"));
    }
}
