using System.Data;
using System.Reflection;
using System.Security.Claims;
using HitPan.API.Middleware;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-52 갈래 A-2① — <b>복원 점검 상태 · 복원 시각 표지 비DB 게이트</b>(G17 일부 · CTO N1-a 의 표지 판정 · 설계 §15-5 ① ⓐ ~ ⓔ).
/// </summary>
/// <remarks>
/// 동작으로 잰다 — 실제 <see cref="RestoreMaintenanceState"/> · 실제 미들웨어에 <see cref="DefaultHttpContext"/> 를 태운다.
/// DB 쪽(실제 <c>restore_history</c> 에서 표지 읽기) = <see cref="RestoreMaintenanceGateDbTests"/>(CI db-gate 몫).
/// 근거 · 변이: <c>docs/개발/erp/20261128_W-G1-52_갈래A2-1_점검끊기_개발명세서.md</c>.
/// </remarks>
public sealed class RestoreMaintenanceGateTests
{
    private const string Tenant = "t-restore-a";

    private sealed class LoopWorker : BackgroundService
    {
        public int Starts;
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Interlocked.Increment(ref Starts);
            // 멈춤 요청 = 정상 끝(취소를 예외 없이 끝으로 바꿈)
            await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    private sealed class OnceService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static (RestoreMaintenanceState State, IMemoryCache Cache) NewState(params IHostedService[] hosted)
    {
        var services = new ServiceCollection();
        foreach (var h in hosted) services.AddSingleton(h);
        var sp = services.BuildServiceProvider();
        var cache = new MemoryCache(new MemoryCacheOptions());
        return (new RestoreMaintenanceState(sp, cache, NullLogger<RestoreMaintenanceState>.Instance), cache);
    }

    private static DefaultHttpContext Ctx(string path, string method = "GET")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Method = method;
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Fact(DisplayName = "G17-M1 🔴 점검 켜면 새 입장 거절 · 끄면 다시 입장 (ⓐ 깃발)")]
    public async Task M1_깃발()
    {
        var (s, _) = NewState();
        Assert.True(s.TryEnterRequest());
        s.ExitRequest();

        var begin = await s.BeginAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.True(begin.Started, begin.DenyReason);
        Assert.True(s.IsActive);
        Assert.False(s.TryEnterRequest());
        Assert.Equal(0, s.InFlight);          // 거절된 입장은 셈을 남기지 않음

        await s.EndAsync(Tenant, CancellationToken.None);
        Assert.False(s.IsActive);
        Assert.True(s.TryEnterRequest());
        s.ExitRequest();
    }

    [Fact(DisplayName = "G17-M2 🔴 도는 요청이 안 비면 점검 못 켬 · 바뀐 것 0 (ⓑ 한도)")]
    public async Task M2_비우기_한도()
    {
        var (s, _) = NewState();
        Assert.True(s.TryEnterRequest());     // 도는 쓰기 1건

        var begin = await s.BeginAsync(TimeSpan.FromMilliseconds(200), CancellationToken.None);
        Assert.False(begin.Started);
        Assert.Equal("drain_timeout", begin.DenyReason);
        Assert.False(s.IsActive);             // 깃발 되돌림
        Assert.True(s.TryEnterRequest());     // 다른 요청 그대로 들어옴
        s.ExitRequest();
        s.ExitRequest();
    }

    [Fact(DisplayName = "G17-M3 🔴 도는 요청이 끝나기를 기다린 뒤에 켬 · 그 사이 새 입장은 거절 (ⓐ 셈 먼저 · ⓑ)")]
    public async Task M3_비우기_기다림()
    {
        var (s, _) = NewState();
        Assert.True(s.TryEnterRequest());

        var beginTask = s.BeginAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        await Task.Delay(100);
        Assert.False(beginTask.IsCompleted);  // 도는 요청이 있어 아직 못 켬
        Assert.False(s.TryEnterRequest());    // 깃발은 이미 켜짐 — 새 입장 거절
        s.ExitRequest();

        var begin = await beginTask;
        Assert.True(begin.Started, begin.DenyReason);
        await s.EndAsync(Tenant, CancellationToken.None);
    }

    [Fact(DisplayName = "G17-M4 🔴 점검 두 번 켜기 거절 (복원 한 번에 하나)")]
    public async Task M4_두번()
    {
        var (s, _) = NewState();
        Assert.True((await s.BeginAsync(TimeSpan.FromSeconds(1), CancellationToken.None)).Started);
        var second = await s.BeginAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.False(second.Started);
        Assert.Equal("already_active", second.DenyReason);
        Assert.True(s.IsActive);              // 첫 점검은 그대로
        await s.EndAsync(Tenant, CancellationToken.None);
    }

    [Fact(DisplayName = "G17-M5 🔴 IHostedService 전부를 셈 · 도는 일꾼은 멈춤 → 끝에 다시 띄움 (ⓒ)")]
    public async Task M5_일꾼()
    {
        var worker = new LoopWorker();
        var once = new OnceService();
        await worker.StartAsync(CancellationToken.None);   // 호스트가 띄운 상태 흉내
        var (s, _) = NewState(worker, once);

        var begin = await s.BeginAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.True(begin.Started, begin.DenyReason);
        Assert.Equal(2, begin.HostedServices.Count);        // 몸 없는 기동 한 번짜리도 센다
        Assert.Contains(typeof(OnceService).FullName!, begin.HostedServices);
        Assert.Equal(new[] { typeof(LoopWorker).FullName! }, begin.PausedServices);
        Assert.True(worker.ExecuteTask!.IsCompleted);       // 점검 중 일꾼 멈춤

        await s.EndAsync(Tenant, CancellationToken.None);
        // 다시 띄움 — 호스팅 판에 따라 ExecuteAsync 가 스레드 풀에서 시작될 수 있어 2초까지 기다린다
        for (var i = 0; i < 40 && Volatile.Read(ref worker.Starts) < 2; i++) await Task.Delay(50);
        Assert.Equal(2, worker.Starts);
        Assert.False(worker.ExecuteTask!.IsCompleted);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact(DisplayName = "G17-M6 🔴 끝에 프로세스 안 캐시 비움 · 표지 = 초 단위 UTC (ⓓ · ⓔ)")]
    public async Task M6_캐시_표지()
    {
        var (s, cache) = NewState();
        cache.Set("session-alive:x", true);
        Assert.Null(s.GetStampUtc(Tenant));

        Assert.True((await s.BeginAsync(TimeSpan.FromSeconds(1), CancellationToken.None)).Started);
        var before = DateTime.UtcNow.AddSeconds(-1);
        await s.EndAsync(Tenant, CancellationToken.None);

        Assert.False(cache.TryGetValue("session-alive:x", out _));
        var stamp = s.GetStampUtc(Tenant);
        Assert.NotNull(stamp);
        Assert.Equal(0, stamp!.Value.Ticks % TimeSpan.TicksPerSecond);
        Assert.InRange(stamp.Value, before, DateTime.UtcNow);
        Assert.Null(s.GetStampUtc("t-other"));              // 다른 회사 표지 0
    }

    [Fact(DisplayName = "G17-M7 🔴 점검 중 /api 는 GET 도 503 · 다음 단계 안 감 · /health · 정적 파일 통과")]
    public async Task M7_미들웨어_503()
    {
        var (s, _) = NewState();
        var reached = 0;
        var mw = new RestoreMaintenanceMiddleware(_ => { reached++; return Task.CompletedTask; }, s);
        Assert.True((await s.BeginAsync(TimeSpan.FromSeconds(1), CancellationToken.None)).Started);

        foreach (var (path, method) in new[] { ("/api/items", "GET"), ("/api/approval/documents/a/process", "POST"), ("/api/auth/login", "POST") })
        {
            var ctx = Ctx(path, method);
            await mw.InvokeAsync(ctx);
            Assert.True(ctx.Response.StatusCode == 503, $"{method} {path} → {ctx.Response.StatusCode} (기대 503)");
        }
        Assert.Equal(0, reached);

        foreach (var path in new[] { "/health", "/index.html", "/_framework/x.js" })
        {
            var ctx = Ctx(path);
            await mw.InvokeAsync(ctx);
            Assert.Equal(200, ctx.Response.StatusCode);
        }
        Assert.Equal(3, reached);
        await s.EndAsync(Tenant, CancellationToken.None);

        var after = Ctx("/api/items");
        await mw.InvokeAsync(after);
        Assert.Equal(200, after.Response.StatusCode);       // 끝나면 풀림
    }

    [Fact(DisplayName = "G17-M8 🔴 허용 목록 끝점만 점검 중 통과 · 셈 밖 / 허브는 셈 안 함(연결이 영영 도는 요청이라)")]
    public async Task M8_허용목록_허브()
    {
        var (s, _) = NewState();
        var reached = 0;
        var mw = new RestoreMaintenanceMiddleware(_ => { reached++; return Task.CompletedTask; }, s);

        // 점검 꺼짐: 허브 연결은 통과하되 셈을 남기지 않음 — 셈하면 열린 화면 하나 때문에 점검이 못 켜진다
        var hubOpen = new TaskCompletionSource();
        var hubMw = new RestoreMaintenanceMiddleware(_ => hubOpen.Task, s);
        var hubTask = hubMw.InvokeAsync(Ctx("/hubs/notify"));
        Assert.Equal(0, s.InFlight);
        Assert.True((await s.BeginAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None)).Started);

        var allowed = Ctx("/api/backup/safe-restore", "POST");
        allowed.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new AllowDuringRestoreMaintenanceAttribute()), "allow"));
        await mw.InvokeAsync(allowed);
        Assert.Equal(200, allowed.Response.StatusCode);
        Assert.Equal(1, reached);

        var plain = Ctx("/api/backup/run", "POST");
        plain.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(), "plain"));
        await mw.InvokeAsync(plain);
        Assert.Equal(503, plain.Response.StatusCode);

        var hubNew = Ctx("/hubs/notify");
        await mw.InvokeAsync(hubNew);
        Assert.Equal(503, hubNew.Response.StatusCode);      // 점검 중 새 허브 연결 거절
        Assert.Equal(1, reached);

        await s.EndAsync(Tenant, CancellationToken.None);
        hubOpen.SetResult();
        await hubTask;
    }

    [Fact(DisplayName = "G17-M9 🔴 다음 단계가 예외로 끝나도 셈은 내려감 (점검이 영영 못 켜지는 일 막음)")]
    public async Task M9_예외_셈()
    {
        var (s, _) = NewState();
        var mw = new RestoreMaintenanceMiddleware(_ => throw new InvalidOperationException("boom"), s);
        await Assert.ThrowsAsync<InvalidOperationException>(() => mw.InvokeAsync(Ctx("/api/items", "POST")));
        Assert.Equal(0, s.InFlight);
    }

    // ── 복원 시각 표지 (CTO N1-a) ─────────────────────────────────────────────

    private static ClaimsPrincipal Token(DateTime issuedUtc, bool withIat = false)
    {
        var claims = new List<Claim>
        {
            new("tenant_id", Tenant), new("user_id", "u1"), new("employee_id", "e-reseed-L"), new("account_type", "tenant_admin"),
            new("exp", new DateTimeOffset(issuedUtc + RestoreStampMiddleware.AccessTokenLifetime).ToUnixTimeSeconds().ToString()),
        };
        if (withIat) claims.Add(new Claim("iat", new DateTimeOffset(issuedUtc).ToUnixTimeSeconds().ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private static Mock<IDbConnection> DbMustNotBeUsed()
    {
        var m = new Mock<IDbConnection>(MockBehavior.Strict);
        m.SetupGet(x => x.State).Returns(ConnectionState.Open);
        m.Setup(x => x.CreateCommand()).Throws(new InvalidOperationException("표지 DB 조회 실패 흉내"));
        return m;
    }

    private static async Task<(int Status, bool Reached)> RunStamp(RestoreMaintenanceState s, ClaimsPrincipal user,
        string path = "/api/approval/documents/a/process", IDbConnection? db = null, string tenant = Tenant)
    {
        var reached = false;
        var mw = new RestoreStampMiddleware(_ => { reached = true; return Task.CompletedTask; }, s, NullLogger<RestoreStampMiddleware>.Instance);
        var ctx = Ctx(path, "POST");
        ctx.User = user;
        ctx.Items["TenantId"] = tenant;
        await mw.InvokeAsync(ctx, db ?? DbMustNotBeUsed().Object);
        return (ctx.Response.StatusCode, reached);
    }

    [Fact(DisplayName = "G17-S1 🔴 N1-a — 복원 전 받은 토큰(클레임 = 재시드 L)으로 복원 뒤 쓰기 → 401 · 다음 단계 안 감 / 복원 뒤 받은 토큰 통과")]
    public async Task S1_표지_앞_401()
    {
        var (s, _) = NewState();
        var oldToken = Token(DateTime.UtcNow.AddMinutes(-30));
        Assert.True((await s.BeginAsync(TimeSpan.FromSeconds(1), CancellationToken.None)).Started);
        await s.EndAsync(Tenant, CancellationToken.None);

        var (status, reached) = await RunStamp(s, oldToken);
        Assert.Equal(401, status);
        Assert.False(reached);                              // 그 쓰기 줄 0 — 컨트롤러까지 못 감

        var (status2, reached2) = await RunStamp(s, Token(DateTime.UtcNow.AddSeconds(1)));
        Assert.Equal(200, status2);
        Assert.True(reached2);

        var (status3, _) = await RunStamp(s, Token(DateTime.UtcNow.AddMinutes(-30), withIat: true));
        Assert.Equal(401, status3);                         // iat 실린 토큰도 같은 판정
    }

    [Fact(DisplayName = "G17-S2 🔴 표지 없는 회사 · 점검 중 · 인증 없음 · /api 밖 = 표지 판정 안 함")]
    public async Task S2_건너뜀()
    {
        var (s, _) = NewState();
        s.RememberStampUtc("t-other", null);                // 복원 기록 없음 = MinValue
        var old = Token(DateTime.UtcNow.AddHours(-1));
        Assert.Equal(200, (await RunStamp(s, old, tenant: "t-other")).Status);

        s.RememberStampUtc(Tenant, DateTime.UtcNow.AddMinutes(-1));
        Assert.True((await s.BeginAsync(TimeSpan.FromSeconds(1), CancellationToken.None)).Started);
        Assert.Equal(200, (await RunStamp(s, old)).Status);  // 점검 중 = 점검 미들웨어 몫(대표 복원 요청이 옛 토큰으로 돈다)
        await s.EndAsync(Tenant, CancellationToken.None);

        Assert.Equal(200, (await RunStamp(s, new ClaimsPrincipal(new ClaimsIdentity()))).Status);
        Assert.Equal(200, (await RunStamp(s, old, path: "/health")).Status);
        Assert.Equal(401, (await RunStamp(s, old)).Status);
    }

    [Fact(DisplayName = "G17-S3 표지 DB 조회 실패 = 통과(가용성 우선) · 실패는 기억 안 함")]
    public async Task S3_조회실패()
    {
        var (s, _) = NewState();
        var (status, reached) = await RunStamp(s, Token(DateTime.UtcNow.AddHours(-1)));
        Assert.Equal(200, status);
        Assert.True(reached);
        Assert.Null(s.GetStampUtc(Tenant));
    }

    [Fact(DisplayName = "G17-S4 🔴 미래 표지는 지금으로 깎음(시계 뒤로 간 PC 의 새 로그인 잠금 막음) · 더 늦은 표지만 남김")]
    public void S4_미래표지()
    {
        var (s, _) = NewState();
        s.RememberStampUtc(Tenant, DateTime.UtcNow.AddHours(3));
        Assert.True(s.GetStampUtc(Tenant) <= DateTime.UtcNow);

        var later = DateTime.UtcNow.AddSeconds(-1);
        s.RememberStampUtc("t-b", later);
        s.RememberStampUtc("t-b", later.AddHours(-2));
        Assert.Equal(later, s.GetStampUtc("t-b"));
    }

    [Fact(DisplayName = "G17-S5 🔴 표지 판정 토큰 수명 = AuthService 발급 수명(반사 대조 — 한쪽만 바꾸면 빨강)")]
    public void S5_수명_대조()
    {
        var f = typeof(AuthService).GetField("AccessTokenLifetime", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(f);
        Assert.Equal((TimeSpan)f!.GetValue(null)!, RestoreStampMiddleware.AccessTokenLifetime);
    }

    [Fact(DisplayName = "G17-S6 발급 시각 = iat → nbf → exp − 수명 · DB 지역 시각 → UTC")]
    public void S6_발급시각()
    {
        var issued = new DateTime(2026, 11, 28, 3, 0, 0, DateTimeKind.Utc);
        Assert.Equal(issued, RestoreStampMiddleware.IssuedAtUtc(Token(issued)));
        Assert.Equal(issued, RestoreStampMiddleware.IssuedAtUtc(Token(issued, withIat: true)));
        Assert.Null(RestoreStampMiddleware.IssuedAtUtc(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("exp", "x") }))));

        var local = new DateTime(2026, 11, 28, 12, 0, 0, DateTimeKind.Unspecified);
        Assert.Equal(DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime(), RestoreStampMiddleware.LocalToUtc(local));
        Assert.Null(RestoreStampMiddleware.LocalToUtc(null));
    }

    [Fact(DisplayName = "G17-S7 🔴 배선 — 점검 = 인증 뒤 · Tenant 앞 / 표지 = SessionValidity 바로 뒤 / 상태 = Singleton")]
    public void S7_배선()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "src", "HitPan.API", "Program.cs"));
        int At(string s) { var i = program.IndexOf(s, StringComparison.Ordinal); Assert.True(i >= 0, $"Program.cs 에 없음: {s}"); return i; }

        var authz = At("app.UseAuthorization();");
        var maint = At("app.UseMiddleware<RestoreMaintenanceMiddleware>();");
        var tenant = At("app.UseMiddleware<TenantMiddleware>();");
        var validity = At("app.UseMiddleware<SessionValidityMiddleware>();");
        var stamp = At("app.UseMiddleware<RestoreStampMiddleware>();");
        var terms = At("app.UseMiddleware<TermsConsentMiddleware>();");
        Assert.True(authz < maint && maint < tenant, "점검 미들웨어 자리 = UseAuthorization 뒤 · TenantMiddleware 앞");
        Assert.True(validity < stamp && stamp < terms, "표지 미들웨어 자리 = SessionValidity 뒤 · 약관 앞");
        At("AddSingleton<HitPan.Application.Interfaces.IRestoreMaintenance, HitPan.API.Middleware.RestoreMaintenanceState>()");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "installer", "hitpan_db_clean.sql"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
