using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using Dapper;
using HitPan.Backoffice.API.Controllers;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261007작10 ①사이클 갈래 ㄹ — <b>작10 게이트 공용 시험환경</b> (설계 §5-1 · C-7 · PM 결재 P-7).
///
/// <para><b>글자가 아니라 동작을 잰다.</b> 이 harness 는 판정식을 품지 않는다. 품는 것은 <b>실물</b>뿐이다:
/// <list type="bullet">
/// <item>스키마 = 실물 <see cref="SchemaMigrator"/> 가 <c>installer/backoffice/*.sql</c> 번호순 적용
///       (백오피스 DDL 진실원 · 설계 C-10). ⇒ 로컬에 없는 <c>reseller_accounts</c> 가 격리 DB 에는 생긴다.</item>
/// <item>토큰 = 실물 <see cref="BackofficeAuthController"/> 로그인 API 가 발급한 <b>진짜 JWT</b>.
///       시험이 손으로 만든 <c>ClaimsPrincipal</c> 이 아니다.</item>
/// <item>요청 = 실물 <c>UseAuthentication → UseAuthorization → MVC 필터 → 실물 컨트롤러</c> 파이프라인.
///       <c>Authorization: Bearer</c> 헤더를 실제 <see cref="JwtBearerHandler"/> 가 검증한다.
///       ⇒ 「필터가 그 요청에 실제로 닿는가」를 지나가 보고 안다(9/22 <c>mainpc-proof</c> P0 교훈).</item>
/// </list></para>
///
/// <para>DB 자리(선례 <c>BoSignupTenantKeyGateTests</c>):
/// CI <c>db-gate</c> 잡(root)은 격리 DB 를 만들었다 지운다 · 로컬 <c>hitpan</c> 계정은
/// <c>CREATE DATABASE</c> 거부라 <c>HITPAN_BO_GATE_DB</c> 로 시험 DB 를 지정해 실측한다.
/// 둘 다 안 되면 <see cref="DbGateEnvironment.SkipOrFail"/> — CI 에서는 던진다(건너뛴 게이트는 통과가 아니다).</para>
/// </summary>
internal sealed class BackofficeRoleGateHarness : IDisposable
{
    // 시험용 JWT 시크릿 — "DEV-" 로 시작하면 실물 코드가 기동을 거부한다(Program.cs:54). 그 규칙을 지킨 값.
    private const string JwtSecret = "work10-gate-bo-jwt-secret-0123456789abcdef";

    /// <summary>
    /// 🔴 실물 <c>IssueTokens</c> 와 <b>같은 우선순위</b>로 시크릿을 고른다
    /// (<c>BackofficeAuthController.cs:162</c> — 환경변수 <c>HITPAN_BO_JWT_SECRET</c> 가 <c>Jwt:Secret</c> 를 <b>이긴다</b>).
    /// <para>⚠️ 이 줄이 없으면 발급과 검증이 서로 다른 열쇠를 써서 모든 요청이
    /// <c>401 invalid_token "The signature key was not found"</c> 이 된다 — 실제로 그렇게 한 번 틀렸다.
    /// 이 PC 에는 그 환경변수가 실제로 들어 있고(정식설치 실물), CI 에는 없어 설정값으로 떨어진다.
    /// 값은 어디에도 찍지 않는다(자격증명 문서화 금지).</para>
    /// </summary>
    private static string EffectiveSecret()
    {
        var env = Environment.GetEnvironmentVariable("HITPAN_BO_JWT_SECRET");
        return string.IsNullOrWhiteSpace(env) ? JwtSecret : env;
    }
    private const string Issuer = "hitpan-backoffice";
    private const string Audience = "backoffice";
    public const string Password = "GatePass1234!";

    public string ResellerA { get; } = Guid.NewGuid().ToString();
    public string ResellerB { get; } = Guid.NewGuid().ToString();
    public string AdminId { get; } = Guid.NewGuid().ToString();
    public string AccountAId { get; } = Guid.NewGuid().ToString();
    public string AccountBId { get; } = Guid.NewGuid().ToString();
    public string EmailA => "a@gate.test";
    public string EmailB => "b@gate.test";
    public string EmailHq => "hq@gate.test";

    private readonly string _dbName;
    private readonly bool _overrideDb;
    private bool _createdDb;

    public BackofficeRoleGateHarness(string defaultDbName)
    {
        var overrideName = Environment.GetEnvironmentVariable("HITPAN_BO_GATE_DB");
        _overrideDb = !string.IsNullOrWhiteSpace(overrideName);
        _dbName = _overrideDb ? overrideName!.Trim() : defaultDbName;

        if (_overrideDb)
        {
            // 지정 DB 는 표를 전부 비우므로 시험 성격의 이름만 허용 — 운영·실데이터 축 오폭 차단(헌법 #39).
            var lower = _dbName.ToLowerInvariant();
            if (lower is "hitpan_erp" or "hitpan_erp_t004" or "hitpan_backoffice"
                || (!lower.Contains("test") && !lower.Contains("gate")))
            {
                throw new Xunit.Sdk.XunitException(
                    $"HITPAN_BO_GATE_DB={_dbName} — 시험 DB 가 아니다(이름에 test/gate 필요 · 운영 축 금지).");
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // DB 자리
    // ══════════════════════════════════════════════════════════════

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — installer/backoffice 를 읽을 수 없다.");
    }

    private static string BoScriptsDir() => Path.Combine(RepoRoot(), "installer", "backoffice");

    private static string ServerConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};User={user};Password={pass};"
             + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
    }

    public string DbConnString() => ServerConnString().Replace("User=", $"Database={_dbName};User=");

    /// <summary>격리 DB 를 세우고 실물 마이그레이터로 출하 DDL 을 적재한다. false = 이 자리는 DB 가 없다(로컬 SKIP).</summary>
    public async Task<bool> TrySetUpAsync(string gateName)
    {
        try
        {
            await using (var admin = new MySqlConnection(ServerConnString()))
            {
                await admin.OpenAsync();
                if (_overrideDb)
                {
                    var tables = (await admin.QueryAsync<string>(
                        "SELECT table_name FROM information_schema.tables WHERE table_schema = @Db AND table_type = 'BASE TABLE'",
                        new { Db = _dbName })).ToList();
                    if (tables.Count > 0)
                    {
                        await admin.ExecuteAsync($"USE `{_dbName}`; SET FOREIGN_KEY_CHECKS = 0; "
                            + string.Join(" ", tables.Select(t => $"DROP TABLE IF EXISTS `{t}`;"))
                            + " SET FOREIGN_KEY_CHECKS = 1;");
                    }
                }
                else
                {
                    await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{_dbName}`; "
                        + $"CREATE DATABASE `{_dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
                    _createdDb = true;
                }
            }
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[{gateName}] DB 준비 실패({ex.Number}): {ex.Message}");
            return DbGateEnvironment.SkipOrFailStrict(gateName) && false;
        }

        // 실물 적용 경로 — SQL 복사본 판정 금지(작14 F-2 재발 방지).
        await new SchemaMigrator(DbConnString(), BoScriptsDir(), NullLogger<SchemaMigrator>.Instance).ApplyAsync();

        // 표를 다시 깔았으니 표 모양 판별 기억을 비운다(그러지 않으면 판독이 낡은 모양을 쓴다).
        BoLiveAccountReader.ForgetSchemaProbe();

        await SeedAsync();
        return true;
    }

    public void Dispose()
    {
        if (!_createdDb) return;
        try
        {
            using var admin = new MySqlConnection(ServerConnString());
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`;");
        }
        catch (MySqlException ex)
        {
            // #15 — 뒷정리 실패를 삼키지 않는다(다음 실행의 DROP IF EXISTS 가 지운다).
            Console.Error.WriteLine($"[작10게이트] 격리 DB 뒷정리 실패: {ex.Message}");
        }
    }

    public async Task<MySqlConnection> OpenAsync()
    {
        var c = new MySqlConnection(DbConnString());
        await c.OpenAsync();
        return c;
    }

    /// <summary>설계 §5-1 2) — 대리점 A·B · 각 계정 1행 · 고객사 A 2행/B 2행 · 정산 각 1행 · 본사 계정 1행.</summary>
    private async Task SeedAsync()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(Password);
        await using var db = await OpenAsync();

        foreach (var (rid, code, name) in new[] { (ResellerA, "GATEA", "게이트대리점A"), (ResellerB, "GATEB", "게이트대리점B") })
        {
            await db.ExecuteAsync(@"
                INSERT INTO resellers (reseller_id, reseller_code, reseller_name, biz_no, ceo_name, join_date, status)
                VALUES (@rid, @code, @name, @biz, '대표', CURDATE(), 'active')",
                new { rid, code, name, biz = code == "GATEA" ? "1110000001" : "1110000002" });
        }

        await db.ExecuteAsync(@"
            INSERT INTO reseller_accounts (account_id, reseller_id, email, password_hash, account_name, role, is_active)
            VALUES (@idA, @ridA, @emA, @h, 'A관리자', 'reseller_admin', 1),
                   (@idB, @ridB, @emB, @h, 'B관리자', 'reseller_admin', 1)",
            new { idA = AccountAId, ridA = ResellerA, emA = EmailA,
                  idB = AccountBId, ridB = ResellerB, emB = EmailB, h = hash });

        await db.ExecuteAsync(@"
            INSERT INTO platform_admins (admin_id, email, password_hash, admin_name, role, is_active)
            VALUES (@id, @em, @h, '본사관리자', 'super_admin', 1)",
            new { id = AdminId, em = EmailHq, h = hash });

        // 고객사 — A 2행 / B 2행. tenant_id 는 기본값이 없어 직접 채운다(출하 DDL 실측 — varchar(36) NOT NULL).
        for (var i = 1; i <= 2; i++)
        {
            foreach (var (rid, tag) in new[] { (ResellerA, "A"), (ResellerB, "B") })
            {
                await db.ExecuteAsync(@"
                    INSERT INTO tenants (tenant_id, tenant_code, company_name, reseller_id, status, subscription_tier)
                    VALUES (@tid, @code, @nm, @rid, 'active', 'basic')",
                    new { tid = Guid.NewGuid().ToString(), code = $"GT{tag}{i}", nm = $"고객사{tag}{i}", rid });
            }
        }

        // 🔴 commission_rate 는 decimal(5,4) — 10.00 은 넘친다(실측 DDL). 비율은 0.1000 = 10%.
        await db.ExecuteAsync(@"
            INSERT INTO reseller_settlements
                (reseller_id, settlement_month, tenant_count, gross_amount, commission_rate,
                 commission_amount, incentive_amount, total_payable, status)
            VALUES (@ridA, '2026-09', 2, 200000.00, 0.1000, 20000.00, 0.00, 20000.00, 'confirmed'),
                   (@ridB, '2026-09', 2, 300000.00, 0.1000, 30000.00, 0.00, 30000.00, 'confirmed')",
            new { ridA = ResellerA, ridB = ResellerB });

        // 권한 CSV — 90_seed_permissions.sql 이 깔아 둔 값을 쓴다. G-4 가 이 값을 강제로 바꿔 본다.
    }

    // ══════════════════════════════════════════════════════════════
    // 실물 로그인 — 진짜 토큰
    // ══════════════════════════════════════════════════════════════

    public IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BackofficeDb"] = DbConnString(),
            ["Jwt:Secret"] = JwtSecret,
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
            ["Jwt:ExpiresHours"] = "8",
        })
        .Build();

    /// <summary>실물 <see cref="BackofficeAuthController"/> 로 로그인해 <b>진짜 access token</b> 을 받는다.</summary>
    public async Task<string> LoginResellerAsync(string email)
    {
        var c = new BackofficeAuthController(Config(), NullLogger<BackofficeAuthController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var res = await c.ResellerLogin(new BackofficeAuthController.LoginRequest(email, Password), default);
        return ExtractToken(res, $"대리점 로그인({email})");
    }

    /// <summary>본사 토큰 — 음성 대조군(G-2음)이 쓴다.</summary>
    public async Task<string> LoginHqAsync()
    {
        var c = new BackofficeAuthController(Config(), NullLogger<BackofficeAuthController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var res = await c.AdminLogin(new BackofficeAuthController.LoginRequest(EmailHq, Password), default);
        return ExtractToken(res, "본사 로그인");
    }

    private static string ExtractToken(IActionResult res, string what)
    {
        var ok = res as OkObjectResult
            ?? throw new Xunit.Sdk.XunitException(
                $"{what} 실패 — {res.GetType().Name} "
              + $"(상태 {(res as ObjectResult)?.StatusCode?.ToString() ?? "?"}). 게이트가 토큰 없이 돌 수는 없다.");
        var val = ok.Value ?? throw new Xunit.Sdk.XunitException($"{what} 응답 본문이 비었다.");
        var data = val.GetType().GetProperty("data")?.GetValue(val)
            ?? throw new Xunit.Sdk.XunitException($"{what} 응답에 data 가 없다.");
        return data.GetType().GetProperty("accessToken")?.GetValue(data) as string
            ?? throw new Xunit.Sdk.XunitException($"{what} 응답에 accessToken 이 없다.");
    }

    /// <summary>토큰 안의 클레임을 읽는다(토큰이 정말 그 값을 담았는지 보고에 쓰려고).</summary>
    public static string? ClaimOf(string token, string name) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .FirstOrDefault(c => c.Type == name)?.Value;

    // ══════════════════════════════════════════════════════════════
    // 실물 파이프라인
    // ══════════════════════════════════════════════════════════════

    public sealed record Outcome(int Status, string Body);

    /// <summary>
    /// <b>HTTP 요청 한 건</b>을 실물 인증·권한·전역필터·컨트롤러에 흘린다.
    /// <paramref name="withGuard"/> = false 면 <b>봉합 전 상태</b>(전역 필터 2개 미등록)를 세운다 — FAIL 재현용.
    /// </summary>
    /// <param name="includeGateRoutes">
    /// 🔴 true 면 <b>시험 어셈블리의 컨트롤러</b>(<c>BoGateLadderController</c>)도 라우팅에 올린다.
    /// <para>왜 필요한가: 이번 차수에 <b>대리점 관리자 전용 제품 라우트가 아직 없다</b>(설계가 사다리 등급을
    /// 쓸 자리를 정하지 않았다 — 개발명세서 §5 보고). 그래서 G-8ⓐ(역할 낮춤 → 상위 전용 403)는
    /// <b>실물 가드·실물 표식·실물 파이프라인</b>에 시험용 라우트 하나를 얹어 잰다.
    /// 가드가 시험 대상이고 컨트롤러는 과녁일 뿐이므로 Fake 가 아니다 — 단,
    /// <b>이 통과는 「제품 라우트가 보호된다」의 증거가 아니다.</b> 기본값은 false 라 다른 게이트는 영향 없다.</para>
    /// </param>
    public async Task<Outcome> SendAsync(string? bearer, string pathAndQuery, bool withGuard = true,
                                         string method = "GET", bool includeGateRoutes = false)
    {
        var config = Config();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpClient();
        services.AddSingleton(config);
        services.AddSingleton<IWebHostEnvironment>(new GateHostEnv());
        services.AddSingleton<IHostEnvironment>(sp => sp.GetRequiredService<IWebHostEnvironment>());
        // MVC 엔드포인트 경로(ControllerRequestDelegateFactory)가 요구한다 — 선례 Pipeline.cs:142-143 과 같은 이유.
        services.AddSingleton(new System.Diagnostics.DiagnosticListener("HitPan.Tests.Work10Gate"));
        services.AddSingleton<System.Diagnostics.DiagnosticSource>(
            sp => sp.GetRequiredService<System.Diagnostics.DiagnosticListener>());
        services.AddSingleton<Microsoft.AspNetCore.Mvc.Infrastructure.IActionContextAccessor,
                              Microsoft.AspNetCore.Mvc.Infrastructure.ActionContextAccessor>();
        services.AddRouting();

        // 실물 JWT 검증 — Program.cs:61-75 와 같은 조건.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidateAudience = true,
                    ValidateLifetime = true, ValidateIssuerSigningKey = true,
                    ValidIssuer = Issuer, ValidAudience = Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(EffectiveSecret())),
                };
            });
        services.AddAuthorization(o =>
        {
            // Program.cs:78-80 과 같은 정책.
            o.AddPolicy("PlatformAdmin", p => p.RequireClaim("account_type", "platform_admin", "platform_owner"));
            o.AddPolicy("Reseller", p => p.RequireClaim("account_type", "reseller_admin"));
            o.AddPolicy("Any", p => p.RequireAuthenticatedUser());
        });

        // 🔴 여기가 전후 대조의 유일한 차이다 — Program.cs:22 의 등록 줄.
        var mvc = services.AddControllers(o =>
        {
            if (withGuard)
            {
                o.Filters.Add<HitPan.Backoffice.API.Filters.BoAccessGuard>();
                o.Filters.Add<HitPan.Backoffice.API.Filters.ResellerScopeArgumentFilter>();
            }
        }).AddApplicationPart(typeof(ResellerPortalController).Assembly);
        if (includeGateRoutes) mvc.AddApplicationPart(typeof(BoGateLadderController).Assembly);

        // 실물 서비스 — Program.cs 등록 그대로(컨트롤러가 요구하는 것만).
        services.AddSingleton<IBoLiveAccountReader, BoLiveAccountReader>();
        services.AddHttpContextAccessor();
        services.AddScoped<IResellerScope, ResellerScope>();
        services.AddSingleton<IBoPermissionService, BoPermissionService>();
        services.AddScoped<IBoAuditService, BoAuditService>();
        services.AddScoped<IResellerSettlementCalculator, ResellerSettlementCalculator>();
        services.AddSingleton<ICloudflareDomainService, CloudflareDomainService>();
        services.AddScoped<IWebhookOutboundService, WebhookOutboundService>();
        services.AddSingleton<ISerialSignatureService, SerialSignatureService>();
        services.AddScoped<IDomainAliasService, DomainAliasService>();
        services.AddSingleton<IMfaService, MfaService>();

        await using var sp = services.BuildServiceProvider();
        var app = new ApplicationBuilder(sp);
        app.UseRouting();
        app.UseAuthentication();     // 실물 JwtBearer 가 Bearer 헤더를 검증한다
        app.UseAuthorization();
        app.UseEndpoints(e => e.MapControllers());
        var pipeline = app.Build();

        await using var scope = sp.CreateAsyncScope();
        var q = pathAndQuery.IndexOf('?');
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        ctx.Request.Method = method;
        ctx.Request.Path = q < 0 ? pathAndQuery : pathAndQuery[..q];
        if (q >= 0) ctx.Request.QueryString = new QueryString(pathAndQuery[q..]);
        ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
        if (bearer is not null) ctx.Request.Headers.Authorization = $"Bearer {bearer}";
        var body = new MemoryStream();
        ctx.Response.Body = body;

        // 🔴 호스팅 계층 대역 — 실제 운영에서는 HostingApplication.CreateContext 가 이 한 줄을 해 준다
        //    (IHttpContextAccessor 가 등록돼 있으면). 맨손 ApplicationBuilder 파이프라인에는 그 계층이 없어
        //    IResellerScope 가 null 을 보고 컨트롤러가 Forbid() 를 냈다 — 실제로 그렇게 한 번 틀렸다.
        //    ⚠️ 이것은 판정을 대역하는 것이 아니다(가드·범위서비스·컨트롤러는 전부 실물).
        sp.GetRequiredService<IHttpContextAccessor>().HttpContext = ctx;

        await pipeline(ctx);
        var wwwAuth = ctx.Response.Headers.WWWAuthenticate.ToString();
        if (ctx.Response.StatusCode == 401 && bearer is not null)
        {
            // 401 은 이 게이트가 재려는 것이 아니다(403 을 재려는 것이다) — 왜 인증이 안 됐는지 바로 보여 준다.
            Console.WriteLine($"[harness] 401 {pathAndQuery} WWW-Authenticate: {wwwAuth}");
        }
        return new Outcome(ctx.Response.StatusCode, Encoding.UTF8.GetString(body.ToArray()));
    }

    /// <summary>엔드포인트 전수 열거(G-1) — 실물 라우팅이 보는 그 목록.</summary>
    public IReadOnlyList<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor> AllActions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Config());
        services.AddSingleton<IWebHostEnvironment>(new GateHostEnv());
        services.AddSingleton<IHostEnvironment>(sp => sp.GetRequiredService<IWebHostEnvironment>());
        services.AddRouting();
        services.AddControllers().AddApplicationPart(typeof(ResellerPortalController).Assembly);
        using var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<Microsoft.AspNetCore.Mvc.Infrastructure.IActionDescriptorCollectionProvider>()
                 .ActionDescriptors.Items
                 .OfType<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>()
                 .ToList();
    }

    private sealed class GateHostEnv : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = typeof(ResellerPortalController).Assembly.GetName().Name!;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Development";
    }
}
