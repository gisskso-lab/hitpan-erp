using System.Data;
using System.Security.Claims;
using HitPan.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
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
    /// <summary>
    /// 🔴 <b>만지면 터지는 연결</b> — 이 게이트가 DB 에 닿지 않는다는 것을 <b>증명</b>한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 종전엔 <i>"닿지 않는 포트로 된 연결문자열"</i> 을 넘겼는데 두 가지가 잘못이었다:
    /// ① 안 닿는 것과 <b>안 만지는 것</b>은 다르다 — 연결을 시도했는지 알 수 없다.
    /// ② 그 문자열이 <b>접속정보 모양</b>이라 <b>비밀 검사(TruffleHog)가 잡았다</b> —
    /// 2026-09-27 CI 실측(<i>Found unverified SQLServer result</i>), 이 파일 때문에 빨간불이 났다.
    /// ⇒ 시험 코드에도 접속정보 모양 문자열을 적지 않는다.
    /// </para>
    /// <para>⇒ 어느 멤버든 건드리는 순간 던진다. 통과했다면 <b>정말로 안 만진 것</b>이다.</para>
    /// </remarks>
    private sealed class MustNotBeTouchedConnection : IDbConnection
    {
        private static InvalidOperationException Boom([System.Runtime.CompilerServices.CallerMemberName] string m = "")
            => new($"이 게이트는 DB 에 닿으면 안 된다 — '{m}' 이 불렸다. "
                 + "sid 없는 옛 토큰은 DB 조회 **전에** 통과해야 한다(G-7a).");

        // 🔴 [AllowNull] — IDbConnection.ConnectionString 의 setter 는 null 을 허용한다고 표기돼 있다.
        //   맞추지 않으면 CS8767/CS8769 경고가 나고, 경고 0개(#19)가 깨진다.
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public string ConnectionString { get => throw Boom(); set => throw Boom(); }
        public int ConnectionTimeout => throw Boom();
        public string Database => throw Boom();
        public ConnectionState State => throw Boom();
        public IDbTransaction BeginTransaction() => throw Boom();
        public IDbTransaction BeginTransaction(IsolationLevel il) => throw Boom();
        public void ChangeDatabase(string databaseName) => throw Boom();
        public void Close() => throw Boom();
        public IDbCommand CreateCommand() => throw Boom();
        public void Open() => throw Boom();
        public void Dispose() { }   // using 정리만 허용 — 아무 일도 하지 않는다
    }

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
        //   만지는 순간 던지는 연결을 준다.
        using var neverOpened = new MustNotBeTouchedConnection();

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
        using var neverOpened = new MustNotBeTouchedConnection();

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
