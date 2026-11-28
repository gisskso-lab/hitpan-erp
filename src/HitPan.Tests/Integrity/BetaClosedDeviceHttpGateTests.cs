using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using HitPan.API.Controllers;
using HitPan.API.Middleware;
using HitPan.Application.DTOs.Device;
using HitPan.Application.Interfaces;
using HitPan.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-S-03 (2026-11-28 · 작업지시서 20261128작 §5 · 설계 §5 G-S3-2) — 1A·07-1 옛 휴대폰 QR 등록 입구 2 닫힘을
/// <b>HTTP 로</b> 잰다(라우팅 · 인증 · 실물 TenantMiddleware · [Authorize] 포함). DB 불필요.
/// </summary>
/// <remarks>
/// <para>
/// 꼴: W-S-04 <see cref="BetaClosedChatHttpGateTests"/> 와 같은 TestHost 위 <b>MVC 만</b> — <c>AddControllers()</c> 는
/// Program.cs:415 와 같은 기본값, 컨트롤러 · [Authorize] · [AllowAnonymous] 는 실물 그대로, 인증만 시험 스킴(헤더 <c>X-Gate-Account</c> 가
/// 있으면 <c>account_type</c> · <c>tenant_id</c> · <c>user_id</c> 클레임 · 없으면 로그인 없음)으로 바꾼다.
/// 덧붙여 <b>실물 <see cref="TenantMiddleware"/></b> 를 Program.cs:805 와 같은 자리(인증 · 권한 뒤 · 컨트롤러 앞)에 얹는다 —
/// DB 를 읽지 않고, 컨트롤러가 읽는 <c>HttpContext.Items["TenantId"]</c> · <c>["UserId"]</c> 를 이것이 채운다
/// (없으면 닫힘을 빼도 처리기가 <c>Forbid()</c> 로 먼저 나가 변이가 빨강이 되지 않는다).
/// <c>TenantOnly</c> 정책은 Program.cs:396 ~ :399 와 같은 판정을 옮겨 적었다. 기기 서비스 · 메인PC 증명 서비스는 Strict 가짜.
/// </para>
/// <para>
/// 한계: 세션 · 약관 · 기기 · 멱등 미들웨어(DB)와 Program.cs:842 전역 fallback(<c>MapFallbackToFile</c>)은 이 길에 없다 —
/// 실제 설치본의 상태 숫자 · 본문은 [4] 화면 실측 · 테스트 환경 몫(설계 §8-1 · W-S-01 과 같음). 여기서 단언하는 성질 =
/// 「200 아님 · 400 업무 글 아님 · 처리기(서비스) 도달 0」 + 실측한 숫자 기록.
/// </para>
/// </remarks>
public sealed class BetaClosedDeviceHttpGateTests
{
    private const string Tenant = "t-gate";
    private const string GateUser = "u-gate";
    private const string AccountHeader = "X-Gate-Account";
    private const string GateScheme = "gate";

    /// <summary>시험 인증 — 헤더가 있으면 그 값을 <c>account_type</c> 으로 한 회사 계정 · 없으면 로그인 없음.</summary>
    private sealed class GateAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public GateAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(AccountHeader, out var account) || string.IsNullOrWhiteSpace(account))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new("account_type", account.ToString()),
                new("tenant_id", Tenant),
                new("user_id", GateUser),
                new(ClaimTypes.NameIdentifier, GateUser),
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, GateScheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, GateScheme)));
        }
    }

    /// <summary>MVC + 실물 TenantMiddleware 를 얹은 TestHost 를 띄운다. 호출자가 <c>using</c> 으로 닫는다.</summary>
    private static async Task<IHost> StartHostAsync(Mock<ITenantDeviceService> device, Mock<IMainPcProofService> proof)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddAuthentication(GateScheme).AddScheme<AuthenticationSchemeOptions, GateAuthHandler>(GateScheme, _ => { });
                    services.AddAuthorization(options =>
                    {
                        // Program.cs:396 ~ :399 와 같은 판정.
                        options.AddPolicy("TenantOnly", policy =>
                            policy.RequireAssertion(ctx =>
                                ctx.User.HasClaim("account_type", "tenant_admin") ||
                                ctx.User.HasClaim("account_type", "tenant_user")));
                    });
                    services.AddControllers().AddApplicationPart(typeof(DeviceController).Assembly);   // Program.cs:415 와 같은 기본값
                    services.AddScoped<CurrentTenant>();
                    services.AddSingleton(device.Object);
                    services.AddSingleton(proof.Object);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseMiddleware<TenantMiddleware>();   // Program.cs:805 와 같은 자리(실물)
                    app.UseEndpoints(e => e.MapControllers());
                });
            })
            .StartAsync();
        return host;
    }

    private static async Task<(HttpStatusCode Code, string Body)> SendAsync(IHost host, HttpMethod method, string path, string? account, string? json = null)
    {
        using var client = host.GetTestClient();
        using var req = new HttpRequestMessage(method, path);
        if (account is not null)
        {
            req.Headers.Add(AccountHeader, account);
        }

        if (json is not null)
        {
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var res = await client.SendAsync(req);
        return (res.StatusCode, await res.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// 닫힌 두 함수를 「부르면 값을 주는」 Strict 가짜 — 닫힘이 빠지면 처리기가 200 으로 끝나 빨강이 <b>상태 숫자로</b> 보인다
    /// (설정하지 않은 다른 함수를 부르면 던진다). 호출 횟수는 <c>Invocations</c> 로 센다.
    /// </summary>
    private static Mock<ITenantDeviceService> DeviceFake()
    {
        var device = new Mock<ITenantDeviceService>(MockBehavior.Strict);
        device.Setup(s => s.IssueMobileRegisterTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("tok-gate");
        device.Setup(s => s.RegisterMobileByTokenAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, "등록되었습니다.", (string?)"d-gate", (AdminContactDto?)null));
        return device;
    }

    private const string RegisterBody = "{\"token\":\"tok-gate\",\"deviceName\":\"gate phone\",\"fingerprint\":\"fp-gate\"}";

    [Fact(DisplayName = "G-S3-2-1 HTTP 대표 계정 POST mobile-token → 200 아님(실측 404) · 서비스 0")]
    public async Task G_S3_2_1_대표_mobile_token_닫힘()
    {
        var device = DeviceFake();
        var proof = new Mock<IMainPcProofService>(MockBehavior.Strict);
        using var host = await StartHostAsync(device, proof);

        var (code, body) = await SendAsync(host, HttpMethod.Post, "/api/devices/mobile-token", "tenant_admin");

        Assert.True(code != HttpStatusCode.OK,
            $"G-S3-2-1 대표 계정 mobile-token 이 200 으로 QR 을 냈다 — [NonAction] 한 줄이 빠졌는지 본다(W-S-03 07-1). 본문: {body}");
        Assert.True(device.Invocations.Count == 0,
            $"G-S3-2-1 기기 서비스를 {device.Invocations.Count} 번 불렀다 — 처리기에 닿았다(W-S-03 07-1).");
        Assert.DoesNotContain("qrImage", body);
        // 🔴 실측(2026-11-28 · 이 TestHost · fallback 없음): 404 · 본문 0 바이트(MVC 끝점이 없어 [ApiController] ProblemDetails 꼴도 아님).
        //   설치본(Program.cs:842 MapFallbackToFile) 의 숫자 · 본문은 [4] 몫 — 여기 값은 「이 길의 기록」 이다.
        Assert.Equal(HttpStatusCode.NotFound, code);
        Assert.Equal(string.Empty, body);
        Assert.Empty(proof.Invocations);
    }

    [Fact(DisplayName = "G-S3-2-2 HTTP 로그인 없음 POST mobile-register → 401 · 서비스 0(바닥 확인 · 닫힘 증거 아님)")]
    public async Task G_S3_2_2_로그인_없는_mobile_register_401()
    {
        var device = DeviceFake();
        var proof = new Mock<IMainPcProofService>(MockBehavior.Strict);
        using var host = await StartHostAsync(device, proof);

        var (code, body) = await SendAsync(host, HttpMethod.Post, "/api/devices/mobile-register", account: null, json: RegisterBody);

        // ⚠️ 닫기 전에도 401 이었다(TenantMiddleware 면제 목록에 없음 · 설계 §0 사실 고침) — 이 칸은 「지금과 같다」 는 바닥 확인이다.
        Assert.True(code == HttpStatusCode.Unauthorized,
            $"G-S3-2-2 로그인 없는 mobile-register 가 {(int)code} 로 나갔다 — TenantMiddleware 면제 목록이 바뀌었는지 본다. 본문: {body}");
        Assert.Empty(device.Invocations);
        Assert.Empty(proof.Invocations);
    }

    [Fact(DisplayName = "G-S3-2-3 HTTP 로그인 계정 POST mobile-register(토큰 실음) → 200 · 400 아님(실측 404) · 서비스 0")]
    public async Task G_S3_2_3_로그인_mobile_register_닫힘()
    {
        var device = DeviceFake();
        var proof = new Mock<IMainPcProofService>(MockBehavior.Strict);
        using var host = await StartHostAsync(device, proof);

        var (code, body) = await SendAsync(host, HttpMethod.Post, "/api/devices/mobile-register", "tenant_user", RegisterBody);

        Assert.True(code != HttpStatusCode.OK,
            $"G-S3-2-3 로그인 계정 mobile-register 가 200 으로 기기를 등록했다 — [NonAction] 한 줄이 빠졌는지 본다(W-S-03 07-1). 본문: {body}");
        Assert.True(code != HttpStatusCode.BadRequest,
            $"G-S3-2-3 mobile-register 가 400 업무 글로 나갔다 — 처리기에 닿았다(W-S-03 07-1). 본문: {body}");
        Assert.True(device.Invocations.Count == 0,
            $"G-S3-2-3 기기 서비스를 {device.Invocations.Count} 번 불렀다 — 처리기에 닿았다(W-S-03 07-1).");
        Assert.DoesNotContain("adminContact", body);
        // 🔴 실측(2026-11-28 · 이 TestHost · fallback 없음): 404 · 본문 0 바이트 — 설치본 숫자 · 본문은 [4] 몫.
        Assert.Equal(HttpStatusCode.NotFound, code);
        Assert.Equal(string.Empty, body);
        Assert.Empty(proof.Invocations);
    }

    [Fact(DisplayName = "G-S3-2-4 HTTP 대조군 GET is-main-pc → 처리기 도달(실측 200 · isMainPc 칸)")]
    public async Task G_S3_2_4_대조군_is_main_pc_도달()
    {
        var device = DeviceFake();
        var proof = new Mock<IMainPcProofService>(MockBehavior.Strict);
        using var host = await StartHostAsync(device, proof);

        var (code, body) = await SendAsync(host, HttpMethod.Get, "/api/devices/is-main-pc", "tenant_user");

        // 이 길이 닿지 않으면 위 세 칸의 「404 · 서비스 0」 은 시험 길이 막혀서일 수 있다 — 같은 컨트롤러 · 같은 미들웨어로 닿는지 본다.
        Assert.True(code == HttpStatusCode.OK,
            $"G-S3-2-4 대조군 is-main-pc 가 {(int)code} 로 나갔다 — 시험 길(인증 · TenantMiddleware · 컨트롤러)이 막혔다. 본문: {body}");
        Assert.Contains("isMainPc", body);
        Assert.Empty(device.Invocations);
    }
}
