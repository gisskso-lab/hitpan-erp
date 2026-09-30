using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using HitPan.API.Controllers;
using HitPan.API.Middleware;
using HitPan.API.Services.LocalRollback;
using HitPan.API.Services.LocalSwap;
using HitPan.API.Services.ManualUpdate;
using HitPan.Application.Interfaces;
using HitPan.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 🚨 20260930작1 I-API 5 — 병렬이슈 04 게이트. <c>/api/system/local-rollback</c> · <c>/api/manual-update/*</c> 를
/// <b>미들웨어를 실제로 지나서</b> 잰다.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 파이프라인을 통째로 세우나</b> — 9/22 <c>mainpc-proof</c> P0 는 속성만 보는 게이트 18건이 전부 초록인데
/// 실제 요청은 <c>TenantMiddleware</c> 에서 401 로 잘렸다. 여기서는 선례 G-A2(<c>UpdateConsentMainPcGateTests</c>)와 같이
/// <see cref="ApplicationBuilder"/> 로 <b>라우팅 → 인증(대역 처리기) → 권한 → <see cref="TenantMiddleware"/> → 엔드포인트</b>를 세우고,
/// 실제 <c>[Authorize(Policy="TenantAdminOnly")]</c> · 실제 <c>[MainPcOnly]</c> 필터 · 실제 컨트롤러에 HTTP 요청을 흘린다.
/// 인증만 대역이다(JWT 서명 검증 대신 머리글 한 칸으로 누구인지 정한다 — 없으면 익명).
/// 시험 csproj 무접촉(패키지 추가 0).
/// </para>
/// <para>
/// 🟢 <b>한 칸만 바꾼다</b> — 양성 = 메인PC(루프백 직접) + 관리자(<c>tenant_admin</c>). 거부 셋은 양성에서 각각 한 칸만 바꾼다:
/// 익명(사람만 뺌) · 비메인PC(터널 머리글만 붙임) · 비관리자(<c>account_type</c> 만 <c>tenant_user</c>).
/// 거부면 서비스 호출 0(다운로드·요청서·작업 등록 0 의 앞문), 양성이면 서비스 호출 1.
/// </para>
/// <para>
/// 🔴 <b>음성 대조군</b>(개발명세서 I-API §5) — <c>LocalRollbackController</c> 의 <c>[MainPcOnly]</c> 한 줄을 빼면
/// 「비메인PC 403」 줄이 FAIL(202·서비스 1). <c>[Authorize(Policy = "TenantAdminOnly")]</c> 를 빼면 익명·비관리자 줄이 FAIL.
/// </para>
/// </remarks>
public sealed class LocalSwapPipelineGateTests
{
    private const string TenantId = "GATE-IAPI5-TENANT";
    private const string WhoHeader = "X-Gate-Who";

    public enum Who { Anonymous, Admin, User }

    /// <summary>시험할 입구 다섯 — (메서드, 주소, 본문).</summary>
    public static IEnumerable<object[]> Doors() => new[]
    {
        new object[] { "GET", "/api/system/local-rollback", "" },
        new object[] { "POST", "/api/system/local-rollback", "{\"ticket\":\"" + new string('a', 32) + "\"}" },
        new object[] { "GET", "/api/manual-update/check", "" },
        new object[] { "POST", "/api/manual-update/apply", "{\"entry\":\"menu\"}" },
        new object[] { "GET", "/api/manual-update/status", "" },
    };

    // ══════════════════════════════════════════════════════════════
    // G-IN3a · G-U1 — 파이프라인 네 줄
    // ══════════════════════════════════════════════════════════════

    [Theory(DisplayName = "I-API5 G-IN3a 🟢 양성 — 메인PC(루프백 직접) + 관리자 → 컨트롤러까지 간다 · 서비스 호출 1")]
    [MemberData(nameof(Doors))]
    public async Task Positive_main_pc_admin_reaches_service(string method, string path, string body)
    {
        var r = await SendAsync(method, path, body, Who.Admin, viaTunnel: false);
        Assert.True(r.Status is 200 or 202 or 204 or 409, $"{method} {path} → {r.Status} {r.Body}");
        Assert.Equal(1, r.ServiceCalls);
    }

    [Theory(DisplayName = "I-API5 G-IN3a 🚨 익명(양성에서 사람만 뺌) → 401 · 서비스 호출 0")]
    [MemberData(nameof(Doors))]
    public async Task Anonymous_is_401(string method, string path, string body)
    {
        var r = await SendAsync(method, path, body, Who.Anonymous, viaTunnel: false);
        Assert.Equal(401, r.Status);
        Assert.Equal(0, r.ServiceCalls);
    }

    [Theory(DisplayName = "I-API5 G-IN3a 🚨 비메인PC(양성에서 터널 머리글만 붙임) → 403 main_pc_only · 서비스 호출 0")]
    [MemberData(nameof(Doors))]
    public async Task Tunnel_is_403_main_pc_only(string method, string path, string body)
    {
        var r = await SendAsync(method, path, body, Who.Admin, viaTunnel: true);
        Assert.Equal(403, r.Status);
        Assert.Contains("main_pc_only", r.Body);
        Assert.Equal(0, r.ServiceCalls);
    }

    [Theory(DisplayName = "I-API5 G-IN3a 🚨 비관리자(양성에서 account_type 만 tenant_user) → 403(권한 단계 · main_pc_only 아님) · 서비스 호출 0")]
    [MemberData(nameof(Doors))]
    public async Task Non_admin_is_403(string method, string path, string body)
    {
        var r = await SendAsync(method, path, body, Who.User, viaTunnel: false);
        Assert.Equal(403, r.Status);
        Assert.DoesNotContain("main_pc_only", r.Body);
        Assert.Equal(0, r.ServiceCalls);
    }

    [Fact(DisplayName = "I-API5 G-IN3a 대조군 — 터널 + 서버가 발급한 출입증이면 메인PC 로 본다(403 이 「터널이라서」임을 확인)")]
    public async Task Tunnel_with_valid_pass_is_main_pc()
    {
        var r = await SendAsync("GET", "/api/system/local-rollback", "", Who.Admin, viaTunnel: true, pass: ValidPass);
        Assert.Equal(200, r.Status);
        Assert.Equal(1, r.ServiceCalls);
    }

    // ══════════════════════════════════════════════════════════════
    // G-IN3b — 주소 앞머리가 TenantMiddleware 익명 통과 목록과 안 겹친다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "I-API5 G-IN3b 🔴 두 라우트 앞머리가 TenantMiddleware 익명 통과 목록 어느 항목과도 StartsWithSegments 불일치")]
    public void Routes_are_not_under_anonymous_prefixes()
    {
        var prefixes = AnonymousPrefixes();
        Assert.True(prefixes.Count >= 10, "익명 통과 목록을 못 읽었다: " + string.Join(",", prefixes));

        foreach (var route in new[] { RouteOf<LocalRollbackController>(), RouteOf<ManualUpdateController>() })
        {
            var hit = UnderPrefix("/" + route, prefixes);
            Assert.True(hit is null, $"/{route} 가 익명 통과 접두사 {hit} 밑에 있다");
        }

        // 대조군 — 라우트를 api/install/manual-update 로 옮긴 사본은 잡힌다
        Assert.Equal("/api/install", UnderPrefix("/api/install/manual-update", prefixes));
        Assert.Equal("/api/auth/update-consent-local", UnderPrefix("/api/auth/update-consent-local/rollback", prefixes));
    }

    // ══════════════════════════════════════════════════════════════
    // G-IN1 — 요청 본문으로 판·경로를 받지 않는다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "I-API5 G-IN1 🔴 두 POST 본문 형식 = 확인 번호 하나 · 입구 하나 (판·경로·주소·해시 칸 0)")]
    public void Post_bodies_have_no_version_or_path_fields()
    {
        Assert.Equal(new[] { "Ticket" }, Props<LocalRollbackStartBody>());
        Assert.Equal(new[] { "Entry" }, Props<ManualUpdateApplyRequest>());
        Assert.Empty(ForbiddenFields(typeof(LocalRollbackStartBody)));
        Assert.Empty(ForbiddenFields(typeof(ManualUpdateApplyRequest)));

        // 대조군 — current·to·path 를 받는 사본은 잡힌다
        Assert.Equal(new[] { "CurrentVersion", "MaterialPath", "To" }, ForbiddenFields(typeof(BodyWithVersionCopy)));
    }

    [Fact(DisplayName = "I-API5 G-IN1 🔴 본문에 \"to\":\"0.0.1\" · \"path\" 를 넣어도 되돌리기 서비스가 받는 것은 번호뿐 (파이프라인 통과)")]
    public async Task Extra_body_fields_do_not_reach_service()
    {
        var ticket = new string('b', 32);
        var r = await SendAsync("POST", "/api/system/local-rollback",
            "{\"ticket\":\"" + ticket + "\",\"to\":\"0.0.1\",\"path\":\"C:\\\\evil\",\"mode\":\"update\"}",
            Who.Admin, viaTunnel: false);
        Assert.Equal(1, r.ServiceCalls);
        Assert.Equal(ticket, r.LastTicket);
        Assert.Equal("gate-owner", r.LastUser); // 사람은 본문이 아니라 JWT(TenantMiddleware Items)에서
    }

    // ══════════════════════════════════════════════════════════════
    // G-IN2 — 작업 명령줄(/TR)은 고정 틀
    // ══════════════════════════════════════════════════════════════

    private static readonly Regex TaskCommandShape = new(
        "^powershell\\.exe -NoProfile -ExecutionPolicy Bypass -File \\\\\"[A-Za-z]:\\\\[^\"&|<>^%;]+\\\\local-swap\\.ps1\\\\\" -Mode (update|rollback) -Ticket [0-9a-f]{32}$",
        RegexOptions.CultureInvariant);

    [Fact(DisplayName = "I-API5 G-IN2 🔴 /TR = 고정 경로 + 모드 열거값 + 32자리 번호뿐 · cmd 경유 0 · 모양 밖 값은 예외")]
    public void Task_command_is_fixed_shape()
    {
        var ticket = LocalSwapLauncher.NewTicket();
        var cmd = LocalSwapLauncher.BuildTaskCommand(@"C:\Program Files\HitPan\rollback\run\local-swap.ps1", SwapModes.Rollback, ticket);
        Assert.Matches(TaskCommandShape, cmd);
        Assert.DoesNotContain("cmd", cmd.Replace("-Command", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LocalSwapLauncher.BuildTaskCommand(@"C:\x\local-swap.ps1", "update; calc", ticket));
        Assert.Throws<ArgumentException>(() =>
            LocalSwapLauncher.BuildTaskCommand(@"C:\x\local-swap.ps1", SwapModes.Update, "0.0.1 & calc"));

        // 대조군 — 본문 값(판)을 /TR 에 이은 사본은 모양 검사에서 잡힌다
        Assert.DoesNotMatch(TaskCommandShape, cmd + " -To 0.0.1");
        Assert.DoesNotMatch(TaskCommandShape, "cmd /c " + cmd);
    }

    // ══════════════════════════════════════════════════════════════
    // 준비물
    // ══════════════════════════════════════════════════════════════

    private const string ValidPass = "pass-issued-by-server";

    private sealed record Outcome(int Status, string Body, int ServiceCalls, string? LastTicket, string? LastUser);

    private sealed class BodyWithVersionCopy
    {
        public string? Ticket { get; set; }
        public string? CurrentVersion { get; set; }
        public string? To { get; set; }
        public string? MaterialPath { get; set; }
    }

    private static string[] Props<T>() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    private static readonly Regex ForbiddenName = new(
        "version|^to$|^from$|path|url|sha|hash|dir|folder|command|mode|material|root|slot|port",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string[] ForbiddenFields(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name)
         .Where(n => ForbiddenName.IsMatch(n)).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    private static string RouteOf<T>() =>
        typeof(T).GetCustomAttribute<RouteAttribute>()?.Template ?? throw new InvalidOperationException(typeof(T).Name + " 에 Route 없음");

    /// <summary><c>TenantMiddleware.cs</c> 원본에서 익명 통과 접두사를 읽는다(코드 글자 = 진실원).</summary>
    private static List<string> AnonymousPrefixes()
    {
        var src = File.ReadAllText(Path.Combine(LocalSwapWorkerRig.RepoRoot(), "src", "HitPan.API", "Middleware", "TenantMiddleware.cs"));
        return Regex.Matches(src, "StartsWithSegments\\(\"(/[^\"]+)\"\\)")
            .Select(m => m.Groups[1].Value)
            .Where(p => p != "/api")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? UnderPrefix(string route, List<string> prefixes) =>
        prefixes.FirstOrDefault(p => new PathString(route).StartsWithSegments(new PathString(p), StringComparison.OrdinalIgnoreCase));

    private static async Task<Outcome> SendAsync(string method, string path, string body, Who who, bool viaTunnel, string? pass = null)
    {
        var appRoot = Path.Combine(Path.GetTempPath(), "hp-iapi5-" + Guid.NewGuid().ToString("N"));
        try
        {
            var calls = 0;
            string? lastTicket = null;
            string? lastUser = null;

            var rollback = new Mock<ILocalRollbackService>();
            rollback.Setup(s => s.GetStatus(It.IsAny<string>()))
                .Callback(() => Interlocked.Increment(ref calls))
                .Returns(new LocalRollbackStatus(false, SwapReasons.NoPreviousVersion, "1.3.48", null, null, null, 10, null));
            rollback.Setup(s => s.Start(It.IsAny<string>(), It.IsAny<string?>()))
                .Callback((string u, string? t) => { Interlocked.Increment(ref calls); lastUser = u; lastTicket = t; })
                .Returns(SwapLaunchResult.Refuse(SwapReasons.NoPreviousVersion));

            var feed = new CountingFeed(() => Interlocked.Increment(ref calls));
            var launcher = new Mock<ILocalSwapLauncher>();
            launcher.Setup(l => l.CheckBusy()).Returns((string?)null);
            launcher.Setup(l => l.Launch(It.IsAny<SwapLaunchInput>()))
                .Callback(() => Interlocked.Increment(ref calls))
                .Returns(SwapLaunchResult.Refuse(SwapReasons.NoPreviousVersion));
            var scopes = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
            var folders = new ManualFolders(appRoot);
            var usage = new ManualUsageLog(folders, NullLogger<ManualUsageLog>.Instance, (_, _, _) => { });
            var manual = new CountingManualUpdateService(new ManualUpdateService(
                feed, Mock.Of<IPackageFetcher>(), new NoAutoLock(), scopes, folders, usage, launcher.Object,
                new ManualUpdateEnvironment(() => true, () => "1.3.48"), NullLogger<ManualUpdateService>.Instance));

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(new DiagnosticListener("HitPan.Tests.IAPI5"));
            services.AddSingleton<DiagnosticSource>(sp => sp.GetRequiredService<DiagnosticListener>());
            services.AddSingleton<IWebHostEnvironment>(new GateHostEnvironment());
            services.AddSingleton<IHostEnvironment>(sp => sp.GetRequiredService<IWebHostEnvironment>());
            services.AddRouting();
            services.AddAuthentication(GateAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, GateAuthHandler>(GateAuthHandler.SchemeName, _ => { });
            services.AddAuthorization(o =>
                // Program.cs:394 의 TenantAdminOnly 와 같은 조건.
                o.AddPolicy("TenantAdminOnly", p => p.RequireAssertion(ctx => ctx.User.HasClaim("account_type", "tenant_admin"))));
            services.AddControllers().AddApplicationPart(typeof(LocalRollbackController).Assembly);

            services.AddScoped<CurrentTenant>();
            services.AddSingleton(rollback.Object);
            services.AddSingleton(manual.Inner);

            var proof = new Mock<IMainPcProofService>();
            proof.Setup(p => p.IsPassValid(It.IsAny<string?>(), It.IsAny<string?>()))
                 .Returns((string? p, string? t) => p == ValidPass && t == TenantId);
            services.AddSingleton(proof.Object);

            await using var sp = services.BuildServiceProvider();

            var app = new ApplicationBuilder(sp);
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseMiddleware<TenantMiddleware>();
            app.UseEndpoints(e => e.MapControllers());
            var pipeline = app.Build();

            await using var scope = sp.CreateAsyncScope();
            var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            ctx.Request.Method = method;
            ctx.Request.Path = path;
            if (body.Length > 0)
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                ctx.Request.ContentType = "application/json";
                ctx.Request.Body = new MemoryStream(bytes);
                ctx.Request.ContentLength = bytes.Length;
            }
            // cloudflared 는 같은 PC 에서 루프백으로 붙는다 — 소켓은 루프백이고 머리글만 다르다.
            ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
            if (viaTunnel) ctx.Request.Headers["CF-Connecting-IP"] = "203.0.113.7";
            if (pass is not null) ctx.Request.Headers[HitPan.API.Security.MainPcOnlyAttribute.PassHeader] = pass;
            if (who != Who.Anonymous) ctx.Request.Headers[WhoHeader] = who == Who.Admin ? "tenant_admin" : "tenant_user";
            var resp = new MemoryStream();
            ctx.Response.Body = resp;

            await pipeline(ctx);
            await manual.Inner.LastRun;

            return new Outcome(ctx.Response.StatusCode, Encoding.UTF8.GetString(resp.ToArray()), calls + manual.Touched(ctx), lastTicket, lastUser);
        }
        finally
        {
            try
            {
                if (Directory.Exists(appRoot)) Directory.Delete(appRoot, recursive: true);
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"[I-API5] 임시 폴더 정리 실패: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// <c>ManualUpdateService</c> 는 봉인 클래스라 가로챌 수 없다 — status 는 서비스가 아무 흔적도 안 남긴다.
    /// ⇒ 컨트롤러가 돌았는지를 응답으로 센다(status 204 = 컨트롤러 본문이 돌았다 · 거부 단계는 401/403).
    /// check·apply 는 피드 호출로 센다(<see cref="CountingFeed"/>).
    /// </summary>
    private sealed class CountingManualUpdateService(ManualUpdateService inner)
    {
        public ManualUpdateService Inner { get; } = inner;

        public int Touched(HttpContext ctx) =>
            ctx.Request.Path.StartsWithSegments("/api/manual-update/status") && ctx.Response.StatusCode == 204 ? 1 : 0;
    }

    private sealed class CountingFeed(Action onCall) : IUpdateFeed
    {
        public Task<FeedCheckResult> CheckAsync(string currentVersion, CancellationToken ct)
        {
            onCall();
            return Task.FromResult(new FeedCheckResult(FeedCheckStatus.NotNewer, null, null));
        }
    }

    private sealed class NoAutoLock : IAutoUpdateLockProbe
    {
        public bool IsAutoUpdateInProgress() => false;
    }

    /// <summary>JWT 대역 — 머리글 한 칸으로 누구인지 정한다. 없으면 익명(NoResult → 권한 단계에서 401).</summary>
    private sealed class GateAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "GateJwt";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var accountType = Request.Headers[WhoHeader].ToString();
            if (string.IsNullOrEmpty(accountType)) return Task.FromResult(AuthenticateResult.NoResult());
            var id = new ClaimsIdentity(new[]
            {
                new Claim("account_type", accountType),
                new Claim("tenant_id", TenantId),
                new Claim("user_id", "gate-owner"),
                new Claim("role", accountType == "tenant_admin" ? "admin" : "user"),
            }, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(id), SchemeName)));
        }
    }

    private sealed class GateHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = typeof(LocalRollbackController).Assembly.GetName().Name!;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Development";
    }
}
