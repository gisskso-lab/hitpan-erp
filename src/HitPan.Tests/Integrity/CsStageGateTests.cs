using System.Security.Claims;
using System.Text.Json;
using Dapper;
using HitPan.Backoffice.API.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 CS 진행 4단계 · 응대 유형 3종 · 만족도 게이트 (사장님 지시 2026-10-08).
///
/// <list type="bullet">
/// <item>G-C-10 4단계가 **서로 겹치지 않고 합이 전체** — 화면 아래 띠 숫자가 서로 안 맞는 사고 방지</item>
/// <item>G-C-11 읽음은 **최초 1회만** 박힌다(두 번 열어도 첫 시각 유지 — 「며칠 방치」 사실 보존)</item>
/// <item>G-C-12 완료 전이는 **응대 유형 없으면 거부**(음성) · 고르면 통과(양성) · 소급 불가 자료를 지금 받는다</item>
/// <item>G-C-13 만족도 수신 — 1~3 밖 거부 · 두 번 보내도 첫 평가 유지(멱등) · ④단계로 옮겨간다</item>
/// <item>G-C-14 한 줄 평에 식별정보 모양이면 **점수는 살리고 글만 버린다**(평가 자체가 사라지면 안 된다)</item>
/// </list>
/// </summary>
public sealed class CsStageGateTests : IDisposable
{
    private readonly BackofficeRoleGateHarness _h =
        new($"hitpan_gate_csstage_{Guid.NewGuid():N}");

    public void Dispose() => _h.Dispose();

    private const string Pepper = "gate-pepper-cs-stage";
    private const string GoodLicense = "GATE-CS-STAGE-0001";
    private const string TenantCode = "GATESTG";

    private IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BackofficeDb"] = _h.DbConnString(),
            ["License:Pepper"] = Pepper,
        }).Build();

    private CsAdminController Cs(string actor) => new(Config(), NullLogger<CsAdminController>.Instance)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", actor) }, "gate")),
            },
        },
    };

    private CsInboundController Inbound() => new(Config(), NullLogger<CsInboundController>.Instance)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    private static string Hmac(string license)
    {
        using var h = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(Pepper));
        var normalized = license.Trim().ToUpperInvariant().Replace(" ", "");   // 컨트롤러와 같은 정규화
        return Convert.ToHexString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    /// <summary>테넌트 1개 + 티켓 n개. 티켓은 전부 「안 읽음 · 접수」 상태로 시작한다.</summary>
    private async Task<(string TenantId, List<(long Id, string Uid)> Tickets)> SeedAsync(int count)
    {
        var tenantId = Guid.NewGuid().ToString();
        await using var db = await _h.OpenAsync();
        await db.ExecuteAsync(@"
            INSERT INTO tenants (tenant_id, tenant_code, company_name, license_key_hash, owner_account_id)
            VALUES (@Id, @Code, '게이트상사', @Hash, 'owner@gate.test')",
            new { Id = tenantId, Code = TenantCode, Hash = Hmac(GoodLicense) });

        var list = new List<(long, string)>();
        for (int i = 0; i < count; i++)
        {
            var uid = Guid.NewGuid().ToString();
            var id = await db.ExecuteScalarAsync<long>(@"
                INSERT INTO bo_cs_tickets (tenant_id, client_ticket_uid, received_channel, category, sub_tag, body)
                VALUES (@T, @Uid, 'erp_message', 'use', 'etc', '저장이 안 됩니다');
                SELECT LAST_INSERT_ID();",
                new { T = tenantId, Uid = uid });
            list.Add((id, uid));
        }
        return (tenantId, list);
    }

    private static JsonElement Envelope(string uid, int rating, string? comment, string license = GoodLicense)
    {
        var data = comment is null
            ? $"{{\"csRequestId\":\"{uid}\",\"rating\":{rating}}}"
            : $"{{\"csRequestId\":\"{uid}\",\"rating\":{rating},\"comment\":\"{comment}\"}}";
        var json = $"{{\"tenantCode\":\"{TenantCode}\",\"licenseKey\":\"{license}\"," +
                   $"\"ownerAccountId\":\"owner@gate.test\",\"data\":{data}}}";
        return JsonDocument.Parse(json).RootElement;
    }

    private static long Num(object v) => Convert.ToInt64(v);

    // ── G-C-10 ───────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-10 🔴 4단계가 서로 겹치지 않고 합이 전체 — 아래 띠 숫자가 어긋나지 않는다")]
    public async Task G_C_10_4단계_상호배타()
    {
        if (!await _h.TrySetUpAsync("G-C-10")) return;
        var (_, t) = await SeedAsync(4);
        var cs = Cs("cs-1");

        // 시작: 4건 전부 ① 안 읽음
        var c0 = (OkObjectResult)await cs.Counts(default);
        var j0 = JsonSerializer.Serialize(c0.Value);
        Assert.Contains("\"unread\":4", j0);
        Assert.Contains("\"total\":4", j0);

        // ②로 하나 — 상세를 열면 읽음이 박힌다
        await cs.Detail(t[0].Id, default);

        // ③으로 하나 — 응대 유형 고르고 완료
        await cs.SetHandledVia(t[1].Id, new CsAdminController.HandledViaRequest { Via = "remote" }, default);
        await cs.ChangeStatus(t[1].Id, new CsAdminController.StatusRequest { To = "완료" }, default);

        // ④로 하나 — 완료 + 평가
        await cs.SetHandledVia(t[2].Id, new CsAdminController.HandledViaRequest { Via = "phone" }, default);
        await cs.ChangeStatus(t[2].Id, new CsAdminController.StatusRequest { To = "완료" }, default);
        await using (var db = await _h.OpenAsync())
        {
            await db.ExecuteAsync(
                "UPDATE bo_cs_tickets SET rating = 3, rated_at = UTC_TIMESTAMP(6) WHERE id = @Id",
                new { Id = t[2].Id });
        }

        var c1 = (OkObjectResult)await cs.Counts(default);
        var d = JsonSerializer.SerializeToElement(c1.Value);
        long unread = Num(d.GetProperty("unread").GetInt64());
        long open = Num(d.GetProperty("open").GetInt64());
        long done = Num(d.GetProperty("done").GetInt64());
        long rated = Num(d.GetProperty("rated").GetInt64());
        long total = Num(d.GetProperty("total").GetInt64());

        // 🔴 핵심: 네 숫자의 합이 전체와 같다(겹치면 합이 커지고, 빠뜨리면 작아진다)
        Assert.Equal(total, unread + open + done + rated);
        Assert.Equal(4, total);
        Assert.Equal(1, unread);   // 손대지 않은 1건
        Assert.Equal(1, open);     // 읽기만 한 1건
        Assert.Equal(1, done);     // 완료·평가 전
        Assert.Equal(1, rated);    // 평가까지 끝난 1건
    }

    // ── G-C-11 ───────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-11 🔴 읽음은 최초 1회만 — 두 번 열어도 첫 시각·첫 사람이 유지된다")]
    public async Task G_C_11_읽음_덮어쓰기_금지()
    {
        if (!await _h.TrySetUpAsync("G-C-11")) return;
        var (_, t) = await SeedAsync(1);

        await Cs("cs-first").Detail(t[0].Id, default);
        await using var db = await _h.OpenAsync();
        var first = await db.QueryFirstAsync(
            "SELECT read_at AS A, read_by AS B FROM bo_cs_tickets WHERE id = @Id", new { Id = t[0].Id });
        Assert.NotNull(first.A);
        Assert.Equal("cs-first", (string)first.B);

        // 다른 사람이 다시 열어도 처음 본 사람·시각은 바뀌지 않는다
        await Cs("cs-second").Detail(t[0].Id, default);
        var again = await db.QueryFirstAsync(
            "SELECT read_at AS A, read_by AS B FROM bo_cs_tickets WHERE id = @Id", new { Id = t[0].Id });
        Assert.Equal((DateTime)first.A, (DateTime)again.A);
        Assert.Equal("cs-first", (string)again.B);
    }

    // ── G-C-12 ───────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-12 🔴 완료 전이 — 응대 유형(쪽지·전화·원격) 없으면 거부(음성) · 고르면 통과(양성)")]
    public async Task G_C_12_완료전이_응대유형_필수()
    {
        if (!await _h.TrySetUpAsync("G-C-12")) return;
        var (_, t) = await SeedAsync(1);
        var cs = Cs("cs-1");

        // 음성 — 응대 유형이 빈 채 완료 시도
        Assert.IsType<BadRequestObjectResult>(
            await cs.ChangeStatus(t[0].Id, new CsAdminController.StatusRequest { To = "완료" }, default));
        await using var db = await _h.OpenAsync();
        Assert.Equal("접수", await db.ExecuteScalarAsync<string>(
            "SELECT status FROM bo_cs_tickets WHERE id = @Id", new { Id = t[0].Id }));

        // 음성 — 목록 밖 응대 유형
        Assert.IsType<BadRequestObjectResult>(
            await cs.SetHandledVia(t[0].Id, new CsAdminController.HandledViaRequest { Via = "카톡" }, default));

        // 양성 — 고르면 완료가 된다 + 완료 시각 · 읽음 보정까지
        Assert.IsType<OkObjectResult>(
            await cs.SetHandledVia(t[0].Id, new CsAdminController.HandledViaRequest { Via = "remote" }, default));
        Assert.IsType<OkObjectResult>(
            await cs.ChangeStatus(t[0].Id, new CsAdminController.StatusRequest { To = "완료" }, default));

        var row = await db.QueryFirstAsync(@"
            SELECT status AS S, handled_via AS V, completed_at AS C, read_at AS R
              FROM bo_cs_tickets WHERE id = @Id", new { Id = t[0].Id });
        Assert.Equal("완료", (string)row.S);
        Assert.Equal("remote", (string)row.V);
        Assert.NotNull(row.C);
        Assert.NotNull(row.R);   // 완료된 건이 「안 읽음」으로 남아 단계가 꼬이지 않는다
    }

    // ── G-C-13 ───────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-13 🔴 만족도 수신 — 1~3 밖 거부 · 두 번 보내도 첫 평가 유지 · ④단계로 옮겨간다")]
    public async Task G_C_13_만족도_수신()
    {
        if (!await _h.TrySetUpAsync("G-C-13")) return;
        var (_, t) = await SeedAsync(1);
        var inbound = Inbound();

        // 음성 — 범위 밖 점수
        Assert.IsType<BadRequestObjectResult>(
            await inbound.ReceiveRating(Envelope(t[0].Uid, 7, null), default));
        // 음성 — 라이선스 한 축이 틀리면 인증 거부(쪽지와 같은 문)
        Assert.IsType<UnauthorizedObjectResult>(
            await inbound.ReceiveRating(Envelope(t[0].Uid, 3, null, "WRONG-LICENSE"), default));

        // 양성 — 1~3 이면 들어온다
        Assert.IsType<OkObjectResult>(
            await inbound.ReceiveRating(Envelope(t[0].Uid, 3, "빠르게 해결해 주셨어요"), default));

        await using var db = await _h.OpenAsync();
        var row = await db.QueryFirstAsync(@"
            SELECT rating AS R, rated_at AS A, rating_comment AS C
              FROM bo_cs_tickets WHERE id = @Id", new { Id = t[0].Id });
        Assert.Equal(3, (int)(sbyte)row.R);
        Assert.NotNull(row.A);
        Assert.Equal("빠르게 해결해 주셨어요", (string)row.C);

        // 멱등 — 고객이 두 번 눌러도 첫 평가가 정본(점수가 2로 바뀌지 않는다)
        Assert.IsType<OkObjectResult>(
            await inbound.ReceiveRating(Envelope(t[0].Uid, 1, "다시 생각해보니 아쉬워요"), default));
        Assert.Equal(3, (int)(sbyte)await db.ExecuteScalarAsync<sbyte>(
            "SELECT rating FROM bo_cs_tickets WHERE id = @Id", new { Id = t[0].Id }));

        // ④ 단계로 세어진다
        var c = (OkObjectResult)await Cs("cs-1").Counts(default);
        Assert.Contains("\"rated\":1", JsonSerializer.Serialize(c.Value));
    }

    // ── G-C-14 ───────────────────────────────────────────────────────
    [Fact(DisplayName = "G-C-14 🔴 한 줄 평에 식별정보 모양 — 점수는 살리고 글만 버린다(평가가 사라지면 안 된다)")]
    public async Task G_C_14_평가_한줄평_금지필드()
    {
        if (!await _h.TrySetUpAsync("G-C-14")) return;
        var (_, t) = await SeedAsync(1);

        // 모양만 맞춘 더미(값은 전부 0)
        Assert.IsType<OkObjectResult>(
            await Inbound().ReceiveRating(Envelope(t[0].Uid, 2, "제 번호 000000-0000000 로 확인했습니다"), default));

        await using var db = await _h.OpenAsync();
        var row = await db.QueryFirstAsync(@"
            SELECT rating AS R, rated_at AS A, rating_comment AS C
              FROM bo_cs_tickets WHERE id = @Id", new { Id = t[0].Id });
        Assert.Equal(2, (int)(sbyte)row.R);     // 점수는 살았다
        Assert.NotNull(row.A);
        Assert.Null(row.C);                     // 글은 버렸다(값이 저장되지 않는다)

        // 거부 기록은 남는다(사유코드만)
        Assert.True(await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM bo_cs_reject_logs WHERE rule_code = 'forbidden_field'") >= 1);
    }
}
