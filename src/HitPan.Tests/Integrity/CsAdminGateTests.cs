using System.Security.Claims;
using Dapper;
using HitPan.Backoffice.API.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 작14 B-8 게이트 — /admin/cs 결선의 서버 몫을 동작으로 잰다.
/// ① 상태 전이는 서버 1곳 + 고정 목록 밖 거부 + INSERT ONLY 로그 실존(F-15)
/// ② 답 작성 = 쓴 사람이 승인자(approved_by = 호출자 · 반자동 ②겹의 문)
/// 표면 정책(PlatformAdmin)은 BackofficeAnonymousAdminApiGateTests 반사 점검이 자동으로 문다.
/// </summary>
public sealed class CsAdminGateTests : IDisposable
{
    private readonly BackofficeRoleGateHarness _h =
        new($"hitpan_gate_csadm_{Guid.NewGuid():N}");

    public void Dispose() => _h.Dispose();

    private CsAdminController Controller(string adminId)
    {
        var ctrl = new CsAdminController(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BackofficeDb"] = _h.DbConnString(),
            }).Build(),
            NullLogger<CsAdminController>.Instance, NullLoggerFactory.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim("sub", adminId) }, "gate")),
                },
            },
        };
        return ctrl;
    }

    private async Task<long> SeedTicketAsync()
    {
        var tenantId = Guid.NewGuid().ToString();
        await using var db = await _h.OpenAsync();
        await db.ExecuteAsync(@"
            INSERT INTO tenants (tenant_id, tenant_code, company_name, license_key_hash)
            VALUES (@Id, @Code, '게이트고객사', 'x');
            INSERT INTO bo_cs_tickets (tenant_id, client_ticket_uid, received_channel, category, sub_tag)
            VALUES (@Id, 'req-1', 'erp_message', 'use', 'etc');",
            new { Id = tenantId, Code = $"G{Guid.NewGuid():N}"[..12] });
        return await db.ExecuteScalarAsync<long>("SELECT id FROM bo_cs_tickets LIMIT 1");
    }

    [Fact(DisplayName = "G-B8-1 🔴 상태 전이 — 고정 목록 밖 400 · 목록 안 전이는 로그 행이 실존(INSERT ONLY)")]
    public async Task G_B8_1_상태전이_서버1곳()
    {
        if (!await _h.TrySetUpAsync("G-B8-1")) return;
        var id = await SeedTicketAsync();
        var c = Controller("admin-1");

        // 음성 — 목록 밖 값은 거부 · 로그 0
        var bad = await c.ChangeStatus(id, new CsAdminController.StatusRequest { To = "폭파" }, default);
        Assert.IsType<BadRequestObjectResult>(bad);

        // 양성 — 전이 + 로그 실존(누가·무엇에서·무엇으로)
        Assert.IsType<OkObjectResult>(
            await c.ChangeStatus(id, new CsAdminController.StatusRequest { To = "처리중" }, default));

        await using var db = await _h.OpenAsync();
        Assert.Equal("처리중", await db.ExecuteScalarAsync<string>("SELECT status FROM bo_cs_tickets WHERE id = @Id", new { Id = id }));
        var log = await db.QueryFirstAsync(
            "SELECT from_status AS F, to_status AS T, actor_id AS A FROM bo_cs_ticket_logs WHERE ticket_id = @Id", new { Id = id });
        Assert.Equal("접수", (string)log.F);
        Assert.Equal("처리중", (string)log.T);
        Assert.Equal("admin-1", (string)log.A);
    }

    [Fact(DisplayName = "G-B8-2 🔴 완료 전이 — completed_at 이 그때만 박힌다")]
    public async Task G_B8_2_완료시각()
    {
        if (!await _h.TrySetUpAsync("G-B8-2")) return;
        var id = await SeedTicketAsync();
        var c = Controller("admin-1");

        await using var db = await _h.OpenAsync();
        Assert.Null(await db.ExecuteScalarAsync<DateTime?>("SELECT completed_at FROM bo_cs_tickets WHERE id = @Id", new { Id = id }));

        // 🔴 2026-10-08 사장님 지시로 **규칙이 하나 늘었다**: 완료로 넘기려면 응대 유형이 있어야 한다
        //   (CS 실적 자료는 소급이 안 되므로 그 자리에서 받는다).
        //   그래서 이 게이트의 준비 단계에 응대 유형 한 줄이 생겼다. 재는 대상은 그대로
        //   **completed_at 이 「완료일 때만」 박히는가** 이고, 그 축은 바뀌지 않았다.
        //
        // 🔴 **2026-10-13 작2 — 그 준비 줄을 뺐다.** 사장님 실측 반려 2) 로 규칙이 다시 갈렸다:
        //   「응대유형은 **쪽지 건은 「히트판a/s센터」로 고정**, 손으로 적은 건만 받는다」
        //   ⇒ 이 게이트가 쓰는 티켓은 `erp_message`(위 SeedTicketAsync) 이므로
        //     ① 사람이 응대 유형을 **꽂을 수 없고**(서버가 400) ② 완료는 **묻지 않고 통과**한다.
        //   재는 축(`completed_at` 이 그때만 박히나)은 **바뀌지 않았다** — 준비 줄만 없어졌다.
        //   🔴 응대 유형을 **요구하는** 규칙은 사라진 게 아니라 **수기 건으로 옮겨졌다**:
        //     음성·양성 쌍은 `G-C-12` 와 `G-W8-2` 가 수기 건으로 문다(규칙이 풀린 게 아니다).
        Assert.IsType<OkObjectResult>(
            await c.ChangeStatus(id, new CsAdminController.StatusRequest { To = "완료" }, default));
        Assert.NotNull(await db.ExecuteScalarAsync<DateTime?>("SELECT completed_at FROM bo_cs_tickets WHERE id = @Id", new { Id = id }));
    }

    [Fact(DisplayName = "G-B8-3 🔴 답 작성 — 쓴 사람이 승인자(approved_by = 호출자) · 빈 본문 거부")]
    public async Task G_B8_3_답작성_승인자()
    {
        if (!await _h.TrySetUpAsync("G-B8-3")) return;
        var id = await SeedTicketAsync();
        var c = Controller("admin-7");

        Assert.IsType<BadRequestObjectResult>(
            await c.Reply(id, new CsAdminController.ReplyRequest { Body = "  " }, default));

        Assert.IsType<OkObjectResult>(
            await c.Reply(id, new CsAdminController.ReplyRequest { Body = "확인했습니다" }, default));

        await using var db = await _h.OpenAsync();
        var row = await db.QueryFirstAsync(
            "SELECT approved_by AS A, client_ticket_uid AS U, replied_by_kind AS K FROM bo_cs_replies WHERE ticket_id = @Id", new { Id = id });
        Assert.Equal("admin-7", (string)row.A);  // 반자동 ②겹 — 승인자 없는 몸통은 없다
        Assert.Equal("req-1", (string)row.U);    // ERP 멱등 연결 고리
        Assert.Equal("hq", (string)row.K);
    }
}
