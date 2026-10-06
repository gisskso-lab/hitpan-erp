using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HitPan.Tests;

// 🔴 20261007작11 게이트 3 — 「익명으로 열린 **관리** API 가 생기면 빨간불」
//
// 왜 이 게이트가 필요했나 (사고 사실 · 작11 §1):
//   DevicesAdminController(api/admin/devices) · SerialLocksAdminController(api/admin/serial-locks) 가
//   클래스에 [AllowAnonymous] 를 달고 있었다. 토큰 0으로 임의 고객사 기기 지문·OS 조회 + 임의 기기 차단 +
//   시리얼 잠금 해제가 됐다. serial-locks 쪽은 주석이 「본사 잠금 해제 API (super_admin 권한)」이라 적혀 있었는데
//   **바로 다음 줄이 [AllowAnonymous]** 였다. 주석은 코드가 아니다.
//   기존 CI 「권한 메뉴 정합」 게이트는 ERP 전용이라 백오피스를 보지 않았다(선행검증 §2-5).
//
// 왜 글자 검사가 아니라 실제 요청인가:
//   라우트·속성을 문자열로 훑는 게이트는 미들웨어를 지나가 보지 않는다. 메인PC ② 사고(9/22)가 그랬다 —
//   [AllowAnonymous] 가 멀쩡히 붙어 있는데도 TenantMiddleware 가 먼저 401 로 잘랐고, 게이트 18건이 전부 통과했다.
//   그래서 이 게이트는 WebApplicationFactory<Program> 으로 **백오피스 API 의 실제 인증·인가 파이프라인**을 띄우고
//   토큰없음 / 대리점 / 본사관리자 세 경우를 **실제로 보내서 상태코드를 본다.**
//
// DB 불필요: 401·403 은 컨트롤러(Dapper 쿼리) 앞에서 갈린다. 본사관리자 양성 경우는
//   「401·403 이 아니다」(= 인가를 통과해 컨트롤러에 닿았다)로 판정한다 — DB 없는 환경에서 500 이어도 성립한다.
//   실제 200 은 로컬 격리 실측으로 확인했다(아래 전후 대조).
//
// ── 봉합 전 FAIL 재현 (전후 대조 · 2026-10-07 로컬 · 개발명세서 §4) ────────────────────────────
//   두 컨트롤러의 [Authorize(Policy="PlatformAdmin")] 를 [AllowAnonymous] 로 되돌린 상태에서 이 게이트를 돌리면
//   G-W11-1·2·4·5·6·7 이 FAIL 한다(토큰없음이 401 대신 200·500, 대리점이 403 대신 통과, 반사 점검이 2건 적발).
//   되돌림을 원복하면 전건 PASS 로 바뀐다. 「초록불이라서 등록」이 아니다.
//   음성 대조군(G-W11-8·9)은 되돌림 여부와 무관하게 PASS 였다 — 정당한 익명 경로를 안 막는다는 증거.
public class BackofficeAnonymousAdminApiGate : IClassFixture<BackofficeAnonymousAdminApiGate.Factory>
{
    // 로컬·CI 공통 시험 전용 서명키. Program.cs:54 가 "DEV-" 접두어를 거부하므로 그 모양을 피한다.
    private const string TestSecret = "W11-GATE-TEST-ONLY-SECRET-32CHARS-MINIMUM-abcdef";

    private readonly HttpClient _http;
    private readonly Factory _factory;

    public BackofficeAnonymousAdminApiGate(Factory factory)
    {
        _factory = factory;
        _http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    public class Factory : WebApplicationFactory<HitPan.Backoffice.API.Program>
    {
        // 🔴 M-1 (2026-10-07 [3-V] 보안상무 병렬검증 교정) — 「시험은 밖으로 나가지 않는다」를 못 박는다.
        //   이 게이트는 글자 검사가 아니라 실제 호스트를 띄운다(위 주석). 그러면 Program.cs:31 이 등록한
        //   WebhookDispatcher(BackgroundService) 도 함께 깨어난다. 그 ExecuteAsync 는 **첫 틱을 지연 없이**
        //   돈다 — Task.Delay 가 TickAsync **뒤에** 있다(WebhookDispatcher.cs:46-57).
        //   TickAsync 는 ConnectionStrings:BackofficeDb 로 접속해 status='pending' 행을 읽고
        //   그 행의 target_url 로 **실제 HTTP POST** 를 보낸 다음 행을 UPDATE 한다.
        //   appsettings.json:12-14 의 기본값이 localhost/hitpan_backoffice 이므로, 그 DB 가 있는 PC·CI 에서는
        //   시험이 외부 웹훅을 실제 발송하고 데이터를 갱신한다 — 작9 게이트가 지킨 「외부 실호출 0」 과
        //   헌법 #39(운영 무접촉) 를 깨는 모양이다.
        //   ⇒ 연결문자열을 **시험 쪽에서** 없는 DB·닫힌 포트로 덮는다. appsettings.json 은 안 고친다(#21).
        //   Port=1 은 loopback 에서 즉시 거절되므로 바깥으로 나가는 패킷이 0이고, 틱은 OpenAsync 에서
        //   끊겨 SELECT·POST·UPDATE 어디에도 닿지 못한다. 그 예외는 Dispatcher 의 catch 가 로그로 남긴다(#15).
        //   이 덮개가 실제로 먹었는지는 G_W11_10 이 돌아가는 앱의 IConfiguration 을 읽어 검산한다.
        //   🔴 자격증명(Uid·Pwd)은 **가짜여도 적지 않는다.** Port=1 에서 TCP 가 먼저 거절되므로
        //      인증 단계에 애초에 닿지 않아 불필요하고, TruffleHog SQLServer 탐지기는 가짜 값도
        //      1건으로 잡는다(실측 run 37518580451). 「가짜니까 괜찮다」로 자격증명 모양을 남기면
        //      다음 사람이 같은 모양을 복사한다.
        internal const string NoDbConnectionString =
            "Server=127.0.0.1;Port=1;Database=hitpan_w11_gate_nonexistent";

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Program.cs:51-57 — 시크릿 미설정·DEV- 접두어면 기동 중단. 게이트 전용 값을 넣는다.
            Environment.SetEnvironmentVariable("HITPAN_BO_JWT_SECRET", TestSecret);
            // 스키마 자동 적용은 끈다(#29·#39 — 게이트가 DB 를 건드리지 않는다).
            Environment.SetEnvironmentVariable("HITPAN_BO_AUTO_MIGRATE", "0");
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            // 앱 자신의 설정 원천(appsettings.json) **뒤에** 얹히므로 이 값이 이긴다.
            builder.ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:BackofficeDb"] = NoDbConnectionString,
                    ["ConnectionStrings:Default"] = NoDbConnectionString
                }));
        }
    }

    // ── 토큰 발급 (의존성 0 — HS256 을 직접 만든다) ──────────────────────────────
    private static string B64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Jwt(string accountType)
    {
        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var header = B64Url(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var payload = B64Url(Encoding.UTF8.GetBytes(
            $"{{\"sub\":\"w11-gate\",\"account_type\":\"{accountType}\"," +
            $"\"iss\":\"hitpan-backoffice\",\"aud\":\"backoffice\",\"exp\":{exp}}}"));
        using var mac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        var sig = B64Url(mac.ComputeHash(Encoding.UTF8.GetBytes($"{header}.{payload}")));
        return $"{header}.{payload}.{sig}";
    }

    private async Task<HttpStatusCode> Send(HttpMethod method, string url, string? accountType, string? body = null)
    {
        using var req = new HttpRequestMessage(method, url);
        if (accountType is not null)
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Jwt(accountType));
        if (body is not null)
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var res = await _http.SendAsync(req);
        return res.StatusCode;
    }

    // 작11 §1 이 적발한 두 라우트 · 읽기(GET)와 쓰기(POST) 양쪽.
    public static TheoryData<string, string, string?> AdminRoutes() => new()
    {
        { "GET",  "/api/admin/devices/tenant/w11-gate-tenant", null },
        { "POST", "/api/admin/devices/w11-gate-dev/revoke",    "{\"reason\":\"w11-gate\"}" },
        { "GET",  "/api/admin/serial-locks",                   null },
        { "POST", "/api/admin/serial-locks/1/unlock",          "{\"reason\":\"w11-gate\",\"unlockedBy\":\"w11-gate\"}" },
    };

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task G_W11_1_토큰없음이면_401(string method, string url, string? body)
    {
        var code = await Send(new HttpMethod(method), url, accountType: null, body);
        Assert.Equal(HttpStatusCode.Unauthorized, code);
    }

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task G_W11_2_대리점토큰이면_403(string method, string url, string? body)
    {
        // 백오피스 JWT 는 대리점에게도 발급된다(BackofficeAuthController.cs:115 account_type=reseller_admin).
        // 「유효한 JWT 면 누구나」로 막아 놓으면 대리점이 남의 고객사 기기를 본다 — 2026-08-02 와 같은 사고 모양.
        var code = await Send(new HttpMethod(method), url, accountType: "reseller_admin", body);
        Assert.Equal(HttpStatusCode.Forbidden, code);
    }

    [Theory]
    [InlineData("platform_admin")]
    [InlineData("platform_owner")]
    public async Task G_W11_3_본사관리자토큰은_인가를_통과한다(string accountType)
    {
        // 양성 경우. DB 없는 환경에서도 성립하도록 「401·403 이 아니다」로 판정한다.
        // 로컬 격리 실측(2026-10-07 · hitpan_trgtest)에서는 실제 200 + 데이터 응답이었다.
        var code = await Send(HttpMethod.Get, "/api/admin/serial-locks", accountType);
        Assert.NotEqual(HttpStatusCode.Unauthorized, code);
        Assert.NotEqual(HttpStatusCode.Forbidden, code);
    }

    [Theory]
    [InlineData("platform_admin")]
    [InlineData("platform_owner")]
    public async Task G_W11_4_본사관리자토큰은_기기조회도_통과한다(string accountType)
    {
        var code = await Send(HttpMethod.Get, "/api/admin/devices/tenant/w11-gate-tenant", accountType);
        Assert.NotEqual(HttpStatusCode.Unauthorized, code);
        Assert.NotEqual(HttpStatusCode.Forbidden, code);
    }

    // ── 반사 점검 — **앞으로 새로 생기는** 관리 컨트롤러까지 문다 ────────────────────────
    //   위 4개 라우트만 재면 「내일 추가되는 api/admin/xxx」는 또 못 잡는다.
    //   글자 검사가 아니라 어셈블리 메타데이터(실제 컴파일된 속성)를 읽는다.
    [Fact]
    public void G_W11_5_관리컨트롤러는_익명이_될_수_없다()
    {
        var offenders = new List<string>();
        foreach (var t in AdminControllerTypes())
        {
            var classAnon = t.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
            var classAuth = t.GetCustomAttributes().Any(a => a is IAuthorizeData);
            if (classAnon)
                offenders.Add($"{t.Name}: 클래스에 [AllowAnonymous] — 토큰 0으로 열린다");
            else if (!classAuth)
                offenders.Add($"{t.Name}: 클래스에 [Authorize] 가 없다 — 백오피스 API 에는 FallbackPolicy 가 없어 사실상 익명이다");

            foreach (var m in PublicActions(t))
            {
                if (m.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                    offenders.Add($"{t.Name}.{m.Name}: 액션에 [AllowAnonymous] — 관리 API 에서 예외를 뚫었다");
            }
        }

        Assert.True(offenders.Count == 0,
            "익명으로 열린 관리 API 가 있다 (작11 P0 와 같은 모양):\n  - " + string.Join("\n  - ", offenders));
    }

    // ── 음성 대조군 — 정당한 익명 경로는 계속 열려 있어야 한다 ───────────────────────────
    //   게이트가 로그인·가입·설치 부트스트랩·헬스체크까지 빨간불로 만들면 쓸 수 없다.
    //   「전부 [Authorize] 로 덮는」 과잉 봉합을 이 두 건이 막는다.
    [Fact]
    public async Task G_W11_6_헬스체크는_토큰없이_200()
    {
        var code = await Send(HttpMethod.Get, "/healthz", accountType: null);
        Assert.Equal(HttpStatusCode.OK, code);
    }

    [Fact]
    public async Task G_W11_7_정당한_익명경로는_인증미들웨어에_막히지_않는다()
    {
        // 로그인·랜딩가입·설치 부트스트랩은 토큰이 없는 것이 정상이다.
        // 로그인은 자격증명이 틀리면 컨트롤러가 401 을 돌려주므로 상태코드만으로는 미들웨어 401 과 구별되지 않는다.
        // ⇒ WWW-Authenticate: Bearer 헤더 유무로 가른다. 미들웨어가 막은 401 에는 그 헤더가 붙고,
        //    컨트롤러까지 닿은 응답에는 붙지 않는다.
        foreach (var (method, url, body) in new (HttpMethod, string, string?)[]
        {
            (HttpMethod.Post, "/api/backoffice/auth/admin/login", "{\"email\":\"w11-gate@nobody.invalid\",\"password\":\"x\"}"),
            (HttpMethod.Post, "/api/landing/signup", "{}"),
        })
        {
            using var req = new HttpRequestMessage(method, url);
            if (body is not null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var res = await _http.SendAsync(req);
            Assert.False(res.Headers.WwwAuthenticate.Any(h => h.Scheme == "Bearer"),
                $"{method} {url} 이 인증 미들웨어에 막혔다 — 정당한 익명 경로를 과잉 봉합했다 (상태 {(int)res.StatusCode})");
            Assert.NotEqual(HttpStatusCode.Forbidden, res.StatusCode);
        }
    }

    [Fact]
    public void G_W11_8_정당한_익명_컨트롤러는_익명으로_남아있다()
    {
        // 반사 점검의 음성 대조군. PM 이 사유를 적은 정당한 익명 경로(로그인·랜딩가입·설치 부트스트랩)가
        // 익명에서 빠지면 고객이 가입·설치를 못 한다 ⇒ 그때도 빨간불이 돼야 한다.
        var asm = typeof(HitPan.Backoffice.API.Program).Assembly;
        foreach (var name in new[] { "BackofficeAuthController", "LandingSignupController", "InstallerBootstrapController" })
        {
            var t = asm.GetTypes().SingleOrDefault(x => x.Name == name);
            Assert.NotNull(t);
            var anon = t!.GetCustomAttribute<AllowAnonymousAttribute>() is not null
                       || PublicActions(t).Any(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null);
            Assert.True(anon, $"{name} 에서 [AllowAnonymous] 가 사라졌다 — 로그인·가입·설치 경로가 막힌다");
        }
    }

    // 「관리 API」 판정: 라우트에 admin 조각이 있거나, 컨트롤러 이름에 Admin 이 들어간 것.
    //   후자가 필요한 이유: api/backoffice/tenants(TenantsAdminController) 처럼 라우트에 admin 이 없는 관리 API 가 있다.
    private static IEnumerable<Type> AdminControllerTypes()
    {
        var asm = typeof(HitPan.Backoffice.API.Program).Assembly;
        foreach (var t in asm.GetTypes())
        {
            if (t.IsAbstract || !typeof(ControllerBase).IsAssignableFrom(t)) continue;
            var route = t.GetCustomAttribute<RouteAttribute>()?.Template ?? "";
            var isAdminRoute = route.Split('/').Any(s => s.Equals("admin", StringComparison.OrdinalIgnoreCase));
            var isAdminName = t.Name.Contains("Admin", StringComparison.Ordinal);
            if (isAdminRoute || isAdminName) yield return t;
        }
    }

    private static IEnumerable<MethodInfo> PublicActions(Type t) =>
        t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
         .Where(m => !m.IsSpecialName);

    [Fact]
    public void G_W11_9_관리컨트롤러_판정이_실제로_무엇인가를_물었다()
    {
        // 판정식 검산(「게이트는 글자가 아니라 동작」 · 판정식도 대조로 검산).
        // 빈 집합을 상대로 반사 점검이 돌면 영원히 초록이다. 적발 대상이 실제로 잡혀 있는지 센다.
        var names = AdminControllerTypes().Select(t => t.Name).ToList();
        Assert.Contains("DevicesAdminController", names);
        Assert.Contains("SerialLocksAdminController", names);
        Assert.True(names.Count >= 10, $"관리 컨트롤러 판정이 너무 적게 잡았다: {names.Count}건 ({string.Join(", ", names)})");
        // 음성 대조군: 공개 조회·로그인은 관리 API 로 잡히면 안 된다(잡히면 G_W11_5 가 정당한 익명을 빨간불로 만든다).
        Assert.DoesNotContain("PricingPublicController", names);
        Assert.DoesNotContain("BackofficeAuthController", names);
        Assert.DoesNotContain("LandingSignupController", names);
    }

    // ── M-1 음성 대조군 — 「시험이 외부로 실제 송신할 수 없다」를 돌아가는 앱에서 검산한다 ──────────
    //   Factory 의 덮개(위 주석)는 적어 두기만 하면 다음 사람이 지운다. 이 시험은 **실제로 기동한 호스트**의
    //   IConfiguration 을 읽어, WebhookDispatcher 가 보게 될 연결문자열이 실 DB 를 가리키지 않음을 못 박는다.
    //   덮개가 빠지거나 먹지 않으면 이 시험이 빨간불이 된다.
    [Fact]
    public void G_W11_10_게이트가_실DB를_가리키지_않는다()
    {
        var cfg = _factory.Services.GetRequiredService<IConfiguration>();
        foreach (var key in new[] { "BackofficeDb", "Default" })
        {
            var cs = cfg.GetConnectionString(key) ?? "";
            Assert.Equal(Factory.NoDbConnectionString, cs);
            // 실 DB 이름·기본 포트가 남아 있으면 WebhookDispatcher 가 첫 틱에 pending 행을 읽고 밖으로 POST 한다.
            Assert.DoesNotContain("hitpan_backoffice", cs, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hitpan_erp", cs, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("3306", cs);
        }
    }
}
