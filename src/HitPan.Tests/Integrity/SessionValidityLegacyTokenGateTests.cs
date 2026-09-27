using System.Security.Claims;
using HitPan.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-7a</b> — <c>sid</c> 가 <b>없는 옛 토큰은 통과한다</b> (20260927작1 절G).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>이 게이트가 막는 사고</b> — 배포 순간 <b>전 고객이 튕기는 것</b>.
/// 배포 전에 발급된 access 토큰에는 <c>sid</c> 클레임이 없다(access 8시간 · refresh 7일).
/// 없는 것을 <i>"세션 없음"</i> 으로 읽으면, 멀쩡히 일하던 사람이 전부 401 을 맞는다.
/// 업데이트 직후 <b>전원 로그아웃</b>은 P0 다.
/// </para>
///
/// <para>
/// 🟢 <b>무엇을 재나</b> — 실제 <c>SessionValidityMiddleware</c> 를 태워
/// <b>다음 단계가 불렸는가</b>를 본다. 주석이나 문자열을 세지 않는다.
/// </para>
///
/// <para>
/// ⚠️ 이 게이트는 <b>DB 가 없어도 돈다</b> — 판정이 DB 앞에서 끝나기 때문이다.
/// (테넌트를 모르는 상태라 축 A 킬스위치 조회도 건너뛴다.) 그래서 CI 의 모든 잡에서 실제로 실행된다.
/// </para>
/// </remarks>
public sealed class SessionValidityLegacyTokenGateTests
{
    private static HttpContext AuthenticatedContext(params Claim[] claims)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/employees";
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth"));
        return ctx;
    }

    private static SessionValidityMiddleware Build(RequestDelegate next) =>
        new(next,
            NullLogger<SessionValidityMiddleware>.Instance,
            new MemoryCache(new MemoryCacheOptions()));

    /// <summary>🔴 <b>G-7a</b> — <c>sid</c> 없는 토큰은 막히지 않는다.</summary>
    [Fact]
    public async Task G7a_sid_없는_옛토큰은_통과한다()
    {
        var called = false;
        var mw = Build(_ => { called = true; return Task.CompletedTask; });

        // 옛 토큰 모양 — user_id·tenant_id 는 있고 sid 만 없다
        var ctx = AuthenticatedContext(
            new Claim("user_id", "u-1"),
            new Claim("tenant_id", "t-1"));

        // 🔴 DB 에 닿으면 이 시험은 그 자체로 실패다 — 닿지 않는 것이 이 게이트의 내용이다.
        //   열지 않은 연결을 준다. 만져지면 예외가 난다.
        using var neverOpened = new MySqlConnection("Server=127.0.0.1;Port=1;User=none;Password=none;");

        await mw.InvokeAsync(ctx, neverOpened);

        Assert.True(called, "sid 없는 옛 토큰이 막혔다 — 배포 순간 전 고객이 튕긴다.");
        Assert.Equal(200, ctx.Response.StatusCode);   // 401 로 바뀌지 않았다
    }

    /// <summary>
    /// 🔴 <b>G-7a 대조군</b> — 인증되지 않은 요청과 <c>/api</c> 밖 요청도 통과한다.
    /// </summary>
    /// <remarks>
    /// 로그인 화면·정적 파일이 이 미들웨어에 걸리면 <b>로그인 자체가 불가능해진다.</b>
    /// 통과 경로를 같이 고정해 둔다.
    /// </remarks>
    [Fact]
    public async Task G7a_대조군_비인증과_api밖_요청도_통과한다()
    {
        using var neverOpened = new MySqlConnection("Server=127.0.0.1;Port=1;User=none;Password=none;");

        var anonCalled = false;
        var anonCtx = new DefaultHttpContext();
        anonCtx.Request.Path = "/api/auth/login";
        await Build(_ => { anonCalled = true; return Task.CompletedTask; }).InvokeAsync(anonCtx, neverOpened);
        Assert.True(anonCalled, "비인증 요청이 막혔다 — 로그인 자체가 불가능해진다.");

        var staticCalled = false;
        var staticCtx = AuthenticatedContext(new Claim("sid", "whatever"));
        staticCtx.Request.Path = "/_framework/blazor.webassembly.js";
        await Build(_ => { staticCalled = true; return Task.CompletedTask; }).InvokeAsync(staticCtx, neverOpened);
        Assert.True(staticCalled, "/api 밖 요청이 막혔다 — 화면이 안 뜬다.");
    }
}
