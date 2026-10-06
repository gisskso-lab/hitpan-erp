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
    private readonly Xunit.Abstractions.ITestOutputHelper _out;

    public BackofficeAnonymousAdminApiGate(Factory factory, Xunit.Abstractions.ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
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
    public void G_W11_5_관리표면은_익명도_인증만도_될_수_없다()
    {
        var offenders = new List<string>();
        var watched = 0;

        foreach (var t in AdminSurfaceTypes())
        {
            watched++;
            var classAnon = t.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
            var anonActions = PublicActions(t)
                .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                .Select(m => m.Name).ToList();

            // (가) 익명 — 작11 P0 가 바로 이 모양이었다.
            if (classAnon && !AnonymousOnAdminSurfaceBaseline.ContainsKey(t.Name))
                offenders.Add($"{t.Name}: 클래스에 [AllowAnonymous] — 토큰 0으로 열린다");
            foreach (var a in anonActions)
            {
                if (!AnonymousOnAdminSurfaceBaseline.ContainsKey($"{t.Name}.{a}")
                    && !AnonymousOnAdminSurfaceBaseline.ContainsKey(t.Name))
                    offenders.Add($"{t.Name}.{a}: 액션에 [AllowAnonymous] — 관리 표면에 예외를 뚫었다");
            }
            if (classAnon || AnonymousOnAdminSurfaceBaseline.ContainsKey(t.Name)) continue;

            // (나) 인증만 요구 — [Authorize] 단독은 **합격이 아니다.**
            //     백오피스 JWT 는 대리점에게도 발급된다(BackofficeAuthController.cs:115) ⇒ 역할까지 물어야 갈린다.
            if (!RequiresRole(t) && !BareAuthorizeBaseline.ContainsKey(t.Name))
            {
                var hasAuth = t.GetCustomAttributes().Any(a => a is IAuthorizeData);
                offenders.Add(hasAuth
                    ? $"{t.Name}: [Authorize] 단독 — 인증만 묻는다. 유효한 대리점 JWT 면 통과한다 " +
                      "(Policy/Roles 또는 액션마다 [BoPermission(\"…\")] 필요)"
                    : $"{t.Name}: 인가 표시가 없다 — 백오피스 API 에는 FallbackPolicy 가 없어 사실상 익명이다");
            }
        }

        // 감시 범위를 눈으로 본다 — 다음 사람이 「몇 개를 보고 있나」를 출력에서 바로 읽는다.
        var scope = $"[작11 게이트 범위] 백오피스 컨트롤러 전수 {AllControllerTypes().Count()}개 중 " +
                    $"관리 표면 {watched}개 감시 · 문서화 익명 예외 {AnonymousOnAdminSurfaceBaseline.Count}건 · " +
                    $"역할미요구 기지 예외 {BareAuthorizeBaseline.Count}건";
        Assert.True(offenders.Count == 0,
            scope + "\n익명·인증만으로 열린 관리 표면이 있다 (작11 P0 와 같은 모양):\n  - "
            + string.Join("\n  - ", offenders));
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

    // ════════════════════════════════════════════════════════════════════════════════
    //  M-2 (2026-10-07 [3-V] 보안상무 병렬검증 교정 2/3) — 판정식을 **리플렉션 전수**로 넓혔다.
    //
    //  종전 판정식의 두 구멍 (검증자가 「게이트는 초록인데 봉합이 뚫리는 조합」으로 찾아냈다):
    //   (1) 글자 의존 — 클래스명에 Admin / 라우트에 admin 인 것만 봤다 ⇒ 백오피스 컨트롤러 32개 중
    //       10개만 감시했다. 클래스에 인가 표시가 아예 없는 4건(PromotionController ·
    //       TossPaymentsController · LandingSignupController · BackofficeAuthController) 은
    //       **집합에 들어오지도 않았다.**
    //   (2) [Authorize] 단독을 합격으로 셌다 ⇒ 앞으로 누가 api/admin 컨트롤러를 만들며 [Authorize] 만
    //       달면 게이트는 초록인데 **대리점 JWT 에 열린다.** 백오피스 JWT 는 대리점에게도 발급되므로
    //       (BackofficeAuthController.cs:115) 인증만으로는 대리점을 못 가른다.
    //
    //  새 판정식:
    //   · 집합 = 어셈블리의 ControllerBase 파생 **전수**(추상 제외) — 이름·라우트 글자에 의존하지 않는다.
    //   · 「관리 표면」 = 라우트에 admin 조각 | 라우트가 api/backoffice/* | 클래스명에 Admin.
    //     api/backoffice/* 를 넣는 이유: TenantsAdminController 처럼 라우트에 admin 이 없는 관리 API 와,
    //     PromotionController 처럼 **이름에도 Admin 이 없는** 관리 API 가 둘 다 있다.
    //   · 관리 표면은 **역할까지 요구**해야 합격 — [Authorize(Policy/Roles=…)] 또는 액션의 [BoPermission("…")].
    //     [Authorize] 단독은 합격으로 세지 않는다.
    //
    //  🔴 넓히면서 드러난 한계 — 아래 예외표 2개가 그 전부다(침묵하지 않고 이름·사유를 적는다).
    //     G_W11_12 가 예외표의 썩음을 막는다: 고쳐지면 표를 줄여야 초록이 된다.
    //     ⚠️ 예외표에 있다 = 「안전하다」가 아니라 「별도 트랙에서 처리 중 / 설계상 역할 불요」.
    //
    //  🔴 남은 한계 (리플렉션으로 못 넓히는 부분 · 명세서 §9 와 같은 문장):
    //   (a) **본문 역할검사는 안 읽는다.** BoPermissions·CredentialsStatus 는 액션 본문에서 IsOwner() 로
    //       403 을 낸다. 속성이 아니므로 리플렉션으로는 보이지 않는다 ⇒ 예외표로만 다룬다.
    //   (b) **런타임 정책 내용은 안 읽는다.** [Authorize(Policy="X")] 가 붙어 있으면 합격으로 센다.
    //       그 X 가 Program.cs 에서 실제로 역할을 요구하는지까지는 속성 메타데이터에 없다.
    //       그 축은 G_W11_1~4 의 **실제 요청**(토큰없음 401 / 대리점 403 / 본사 통과)이 맡는다.
    //   (c) **라우트 접두어 규칙은 여전히 규약 의존.** api/backoffice·api/admin 밖에 새 관리 접두어
    //       (예: api/platform)를 만들면 관리 표면으로 안 잡힌다. 그때는 IsAdminSurface 를 함께 늘려야 한다.
    //       그 누락은 G_W11_11 의 전수 분류(미분류 0건)가 드러낸다.
    // ════════════════════════════════════════════════════════════════════════════════

    // 관리 표면인데 익명이 정당한 것 — 전건 열거 + 사유. (가입·로그인 경로는 토큰을 받기 전이다.)
    private static readonly Dictionary<string, string> AnonymousOnAdminSurfaceBaseline = new()
    {
        ["BackofficeAuthController"] =
            "로그인 — 토큰을 받기 전 경로. 토큰을 요구하면 아무도 못 들어온다 (G_W11_7·8 이 열림을 지킨다)",
        ["ResellerApplicationController"] =
            "대리점 가입 신청 공개 접수(api/backoffice/reseller-applications POST) — 신청자는 아직 계정이 없다. " +
            "심사·승인은 별 컨트롤러 ResellerApplicationsAdminController 가 맡고 그쪽은 [BoPermission] 2중이다",
        ["PromotionController.Redeem"] =
            "고객이 프로모션 코드를 입력하는 경로(PromotionController.cs:212-213) — 익명 봉합 후보 B 목록 · " +
            "사장님 결재 대기. 결재로 막히면 이 줄을 지운다"
    };

    // 관리 표면인데 역할을 안 묻는 것 — 전건 열거 + 사유. PM 판정으로 이번 차수 무접촉.
    private static readonly Dictionary<string, string> BareAuthorizeBaseline = new()
    {
        ["BoPermissionsController"] =
            "액션 본문 IsOwner() 로 403 (BoPermissionsController.cs:30·40). 단 F-1·F-2 — PlatformAdmin 이 role 을 " +
            "안 보고 IsOwner() 가 영원히 false ⇒ 작10 2차수 역할 4값 전환에서 정리",
        ["CredentialsStatusController"] =
            "액션 본문 IsOwner() 로 403 (CredentialsStatusController.cs:39·104·160). F-2 동일 ⇒ 작10 2차수",
        ["OwnerMfaController"] =
            "자기 계정 MFA 만 다룬다(sub 클레임 범위 · OwnerMfaController.cs:167) ⇒ 역할 요구가 설계상 불필요"
    };

    private static IEnumerable<Type> AllControllerTypes()
    {
        var asm = typeof(HitPan.Backoffice.API.Program).Assembly;
        return asm.GetTypes()
                  .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
                  .OrderBy(t => t.Name, StringComparer.Ordinal);
    }

    private static string RouteOf(Type t) => t.GetCustomAttribute<RouteAttribute>()?.Template ?? "";

    private static bool IsAdminSurface(Type t)
    {
        var route = RouteOf(t);
        var segs = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Any(s => s.Equals("admin", StringComparison.OrdinalIgnoreCase))) return true;
        if (segs.Length >= 2 && segs[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            && segs[1].Equals("backoffice", StringComparison.OrdinalIgnoreCase)) return true;
        return t.Name.Contains("Admin", StringComparison.Ordinal);
    }

    private static IEnumerable<Type> AdminSurfaceTypes() => AllControllerTypes().Where(IsAdminSurface);

    // 역할까지 요구하나 — 속성 메타데이터만으로 판정한다(위 한계 (a)(b) 참조).
    private static bool RequiresRole(Type t)
    {
        static bool HasRoleData(IEnumerable<object> attrs) =>
            attrs.OfType<IAuthorizeData>().Any(a =>
                !string.IsNullOrWhiteSpace(a.Policy) || !string.IsNullOrWhiteSpace(a.Roles));

        if (HasRoleData(t.GetCustomAttributes())) return true;
        if (t.GetCustomAttributes().OfType<HitPan.Backoffice.API.Filters.BoPermissionAttribute>().Any()) return true;

        var actions = PublicActions(t).ToList();
        if (actions.Count == 0) return false;
        // 액션마다 역할·권한을 묻는 경우도 합격 — 다만 **한 액션이라도 빠지면** 불합격이다.
        return actions.All(m =>
            HasRoleData(m.GetCustomAttributes())
            || m.GetCustomAttributes().OfType<HitPan.Backoffice.API.Filters.BoPermissionAttribute>().Any()
            || m.GetCustomAttribute<AllowAnonymousAttribute>() is not null);
    }

    private static IEnumerable<MethodInfo> PublicActions(Type t) =>
        t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
         .Where(m => !m.IsSpecialName);

    [Fact]
    public void G_W11_9_판정식이_실제로_무엇을_보는지_검산()
    {
        // 판정식 검산(「게이트는 글자가 아니라 동작」 · 판정식도 대조로 검산).
        // 빈 집합을 상대로 반사 점검이 돌면 영원히 초록이다.
        var all = AllControllerTypes().Select(t => t.Name).ToList();
        var admin = AdminSurfaceTypes().Select(t => t.Name).ToList();

        // 🔴 감시 범위를 **통과할 때도** 출력에 찍는다 — 다음 사람이 「몇 개를 보고 있나」를 눈으로 본다.
        //   실패 메시지에만 있으면 초록일 때 범위가 보이지 않아, 범위가 쪼그라든 걸 아무도 모른다.
        _out.WriteLine($"[작11 게이트 범위] 백오피스 컨트롤러 전수 {all.Count}개 · 관리 표면 {admin.Count}개 감시 " +
                       $"(종전 글자 판정식 10개) · 문서화 익명 예외 {AnonymousOnAdminSurfaceBaseline.Count}건 · " +
                       $"역할미요구 기지 예외 {BareAuthorizeBaseline.Count}건");
        _out.WriteLine("  관리 표면: " + string.Join(", ", admin));
        _out.WriteLine("  비관리(익명·자기범위): " + string.Join(", ", all.Except(admin)));

        // 전수 집합이 실제로 전수인가 — 32개(2026-10-07 실측)에서 줄어들면 리플렉션이 빠진 것이다.
        Assert.True(all.Count >= 32,
            $"백오피스 컨트롤러 전수가 {all.Count}건 — 2026-10-07 실측 32건보다 적다 ({string.Join(", ", all)})");

        // 작11 §1 이 적발한 둘.
        Assert.Contains("DevicesAdminController", admin);
        Assert.Contains("SerialLocksAdminController", admin);

        // M-2 (1) — 종전 글자 판정식이 **아예 못 봤던** 4건이 이제 전수 집합에 들어온다.
        foreach (var n in new[] { "PromotionController", "TossPaymentsController",
                                  "LandingSignupController", "BackofficeAuthController" })
            Assert.Contains(n, all);

        // 그중 관리 표면인 둘은 감시 대상이 됐다(종전 0건).
        Assert.Contains("PromotionController", admin);
        Assert.Contains("BackofficeAuthController", admin);

        // 감시 범위가 종전(10)보다 실제로 늘었는가.
        Assert.True(admin.Count >= 20,
            $"관리 표면 판정이 {admin.Count}건 — 종전 10건에서 늘지 않았다 ({string.Join(", ", admin)})");

        // 음성 대조군: 공개 조회·랜딩 경로는 관리 표면으로 잡히면 안 된다.
        Assert.DoesNotContain("PricingPublicController", admin);
        Assert.DoesNotContain("LandingSignupController", admin);
        Assert.DoesNotContain("LandingPublicController", admin);
        Assert.DoesNotContain("InstallerBootstrapController", admin);

        // RequiresRole 자체의 대조 — 봉합한 둘은 양성, 예외표의 셋은 음성이어야 한다.
        var byName = AllControllerTypes().ToDictionary(t => t.Name, t => t);
        Assert.True(RequiresRole(byName["DevicesAdminController"]));
        Assert.True(RequiresRole(byName["TenantsAdminController"]));
        Assert.False(RequiresRole(byName["OwnerMfaController"]));
    }

    [Fact]
    public void G_W11_11_컨트롤러_전수가_분류에서_새지_않는다()
    {
        // 한계 (c) 를 드러내는 장치. 전수 = 관리표면 + 익명 + 인증필요 로 **빠짐없이** 나뉘어야 한다.
        // 새 접두어(api/platform 등)를 쓴 컨트롤러가 들어오면 「인가 표시도 없고 익명도 아닌」
        // 미분류로 떨어져 여기서 빨간불이 된다.
        var unclassified = new List<string>();
        int adminCnt = 0, anonCnt = 0, authCnt = 0;

        foreach (var t in AllControllerTypes())
        {
            if (IsAdminSurface(t)) { adminCnt++; continue; }

            var anon = t.GetCustomAttribute<AllowAnonymousAttribute>() is not null
                       || PublicActions(t).Any(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null);
            var auth = t.GetCustomAttributes().Any(a => a is IAuthorizeData)
                       || PublicActions(t).Any(m => m.GetCustomAttributes().Any(a => a is IAuthorizeData));

            if (anon) anonCnt++;
            else if (auth) authCnt++;
            else unclassified.Add($"{t.Name} (라우트 \"{RouteOf(t)}\") — 관리 표면도 아니고 인가 표시도 익명 표시도 없다");
        }

        Assert.True(unclassified.Count == 0,
            $"[작11 게이트 범위] 전수 {AllControllerTypes().Count()} = 관리표면 {adminCnt} + 익명 {anonCnt} + 인증필요 {authCnt}" +
            "\n분류에서 샌 컨트롤러가 있다 — IsAdminSurface 의 접두어 규칙을 늘려야 한다:\n  - "
            + string.Join("\n  - ", unclassified));
    }

    [Fact]
    public void G_W11_12_예외표가_썩지_않는다()
    {
        // 예외표는 적어 두면 영원히 남는다 ⇒ 「고쳐졌는데 아직 예외」와 「이름이 바뀌었는데 그대로」를 막는다.
        var stale = new List<string>();
        var byName = AllControllerTypes().ToDictionary(t => t.Name, t => t);

        foreach (var (key, reason) in BareAuthorizeBaseline)
        {
            if (!byName.TryGetValue(key, out var t))
            { stale.Add($"{key}: 그런 컨트롤러가 없다 — 예외표에서 지워라 (사유: {reason})"); continue; }
            if (RequiresRole(t))
                stale.Add($"{key}: 이제 역할을 요구한다 — 예외표에서 지워라 (사유였던 것: {reason})");
        }

        foreach (var (key, reason) in AnonymousOnAdminSurfaceBaseline)
        {
            var typeName = key.Split('.')[0];
            if (!byName.TryGetValue(typeName, out var t))
            { stale.Add($"{key}: 그런 컨트롤러가 없다 — 예외표에서 지워라 (사유: {reason})"); continue; }
            var anon = t.GetCustomAttribute<AllowAnonymousAttribute>() is not null
                       || PublicActions(t).Any(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null);
            if (!anon)
                stale.Add($"{key}: 이제 익명이 아니다 — 예외표에서 지워라 (사유였던 것: {reason})");
        }

        Assert.True(stale.Count == 0,
            "작11 게이트 예외표가 실제 코드와 갈라졌다:\n  - " + string.Join("\n  - ", stale));
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
