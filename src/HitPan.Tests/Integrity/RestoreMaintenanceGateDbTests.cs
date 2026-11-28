using System.Security.Claims;
using Dapper;
using HitPan.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-52 갈래 A-2① — <b>복원 시각 표지 DB 게이트</b>(G17 「복원 전 토큰 = 401」 · CTO N1-a 의 표지 자리 · CI db-gate 전용).
/// </summary>
/// <remarks>
/// <para>출하 DDL 신규설치본(<see cref="MigrationGateDb"/> · #36)의 <b>실제</b> <c>restore_history</c> 에 복원 기록을 넣고,
/// 프로세스 안 표지가 <b>없는</b>(= 재기동 뒤) 상태에서 실제 <see cref="RestoreStampMiddleware"/> 가 DB 에서 표지를 읽어 판정하는지 잰다.</para>
/// <para>🔴 이 PC 3306 은 실물 DB 라 로컬에서 돌리지 않는다 — CI db-gate(HITPAN_REQUIRE_DB)에서만.</para>
/// <para>⚠️ 복원 몸(걸음 ④-끝 · ⑤)을 실제로 돌린 뒤의 G21 ⑩ N1-a 칸(재시드 L 클레임 토큰 → 401 · 쓰기 줄 0)은 A-2③ 몫 —
/// 여기는 그 401 을 내는 자리(표지)가 DB 값으로 서는지까지다.</para>
/// </remarks>
public sealed class RestoreMaintenanceGateDbTests : IDisposable
{
    private readonly MigrationGateDb _db = new("HITPAN_G17_ERP_DB", "g17stamp");
    private readonly string _tenantA = Guid.NewGuid().ToString();
    private readonly string _tenantB = Guid.NewGuid().ToString();

    public void Dispose() => _db.Dispose();

    private static ClaimsPrincipal Token(string tenant, DateTime issuedUtc) => new(new ClaimsIdentity(new[]
    {
        new Claim("tenant_id", tenant), new Claim("user_id", "u1"), new Claim("employee_id", "e-reseed-L"),
        new Claim("account_type", "tenant_admin"),
        new Claim("exp", new DateTimeOffset(issuedUtc + RestoreStampMiddleware.AccessTokenLifetime).ToUnixTimeSeconds().ToString()),
    }, "Bearer"));

    private static async Task<(int Status, bool Reached)> RunAsync(RestoreMaintenanceState state, MySqlConnector.MySqlConnection db,
        string tenant, ClaimsPrincipal user)
    {
        var reached = false;
        var mw = new RestoreStampMiddleware(_ => { reached = true; return Task.CompletedTask; }, state, NullLogger<RestoreStampMiddleware>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/approval/documents/a/process";
        ctx.Request.Method = "POST";
        ctx.Response.Body = new MemoryStream();
        ctx.User = user;
        ctx.Items["TenantId"] = tenant;
        await mw.InvokeAsync(ctx, db);
        return (ctx.Response.StatusCode, reached);
    }

    private static RestoreMaintenanceState NewState() =>
        new(new ServiceCollection().BuildServiceProvider(), new MemoryCache(new MemoryCacheOptions()), NullLogger<RestoreMaintenanceState>.Instance);

    [Fact(DisplayName = "G17-D1 🔴 재기동 뒤(프로세스 안 표지 없음) — restore_history 표지 앞 토큰 401 · 뒤 토큰 통과 · 다른 회사 영향 0")]
    public async Task G17_D1_DB표지()
    {
        const string gate = "G17-D1 DB 표지";
        if (!await _db.ReadyAsync(gate)) return;
        await using var db = await _db.OpenAsync();

        // 복원 한 번 — 옛 BackupService 와 같은 계약(서버 지역 시각)으로 적음
        var finishedLocal = DateTime.Now.AddSeconds(-5);
        await db.ExecuteAsync(@"
            INSERT INTO restore_history (restore_id, tenant_id, started_at, finished_at, source_file, status, triggered_by_user)
            VALUES (@Id, @T, @S, @F, 'g17.sql', 'success', 'u1')",
            new { Id = Guid.NewGuid().ToString(), T = _tenantA, S = finishedLocal.AddMinutes(-2), F = finishedLocal });

        var state = NewState();
        var oldToken = Token(_tenantA, DateTime.UtcNow.AddMinutes(-30));
        var (s1, r1) = await RunAsync(state, db, _tenantA, oldToken);
        Assert.True(s1 == 401, $"[{gate}] 복원 전 토큰 → {s1} (기대 401)");
        Assert.False(r1);

        var (s2, r2) = await RunAsync(state, db, _tenantA, Token(_tenantA, DateTime.UtcNow));
        Assert.True(s2 == 200 && r2, $"[{gate}] 복원 뒤 토큰 → {s2} (기대 통과)");

        var (s3, r3) = await RunAsync(state, db, _tenantB, Token(_tenantB, DateTime.UtcNow.AddMinutes(-30)));
        Assert.True(s3 == 200 && r3, $"[{gate}] 복원 안 한 회사 옛 토큰 → {s3} (기대 통과 · @t 외 tenant 0)");
    }

    [Fact(DisplayName = "G17-D2 🔴 복원 도중 죽음(finished_at 없음) = 시작 시각이 표지 — 그 앞 토큰 401")]
    public async Task G17_D2_도중죽음()
    {
        const string gate = "G17-D2 도중 죽음";
        if (!await _db.ReadyAsync(gate)) return;
        await using var db = await _db.OpenAsync();

        var startedLocal = DateTime.Now.AddMinutes(-1);
        await db.ExecuteAsync(@"
            INSERT INTO restore_history (restore_id, tenant_id, started_at, source_file, status, triggered_by_user)
            VALUES (@Id, @T, @S, 'g17.sql', 'running', 'u1')",
            new { Id = Guid.NewGuid().ToString(), T = _tenantA, S = startedLocal });

        var (s1, r1) = await RunAsync(NewState(), db, _tenantA, Token(_tenantA, DateTime.UtcNow.AddMinutes(-10)));
        Assert.True(s1 == 401 && !r1, $"[{gate}] 시작 전 토큰 → {s1} (기대 401)");
    }
}
