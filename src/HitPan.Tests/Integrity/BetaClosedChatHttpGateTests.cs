using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using HitPan.API.Controllers;
using HitPan.Application.DTOs.Chat;
using HitPan.Application.Interfaces;
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
/// 🔴 W-S-04 고침 1 F1 (2026-11-28 · 작업지시서 20261128작_W-S-04_고침1 · [3-V] P2-2) — 06-9 <c>before</c> 거절을
/// <b>HTTP 로</b> 잰다(모델 바인딩 · [ApiController] 자동 400 포함). DB 불필요.
/// </summary>
/// <remarks>
/// <para>
/// 왜 HTTP 인가: <see cref="BetaClosedChatGateTests"/> G-S4-2 는 메서드를 직접 불러 <c>?Before=</c>(대문자) ·
/// <c>?before=abc</c>(형식 틀림) · <c>?before=</c>(빈 값)이 실제로 어떤 값으로 묶이는지는 시험 밖이었다.
/// </para>
/// <para>
/// 꼴: 선례 <c>BackofficeAnonymousAdminApiGateTests</c> 의 WebApplicationFactory 는 Program.cs 전체를 띄운다.
/// ERP <c>Program.cs</c> 는 인증 뒤 TenantMiddleware · SessionValidityMiddleware · TermsConsentMiddleware ·
/// DeviceAuthMiddleware 가 모두 DB 를 읽어(:805 ~ :830) DB 없이 컨트롤러까지 닿지 못한다. 그래서 같은
/// TestHost 위에 <b>MVC 만</b> 얹는다 — <c>AddControllers()</c> 는 Program.cs:415 와 같은 기본값(옵션 0 ·
/// <c>ApiBehaviorOptions</c> 손 0), 컨트롤러 · 모델 바인딩 · [Authorize] 는 <b>실물 그대로</b>, 인증만 시험 스킴
/// (헤더 <c>X-Gate-Employee</c> 가 있으면 <c>employee_id</c> 클레임)으로 바꾼다. 서비스 · 테넌트는 Strict 가짜 —
/// 설정하지 않은 것을 부르면 던진다(= 계정 확인 · 처리기에 닿으면 빨강).
/// </para>
/// <para>
/// 한계: 앞 미들웨어(테넌트 · 세션 · 약관 · 기기)는 이 길에 없다 — 그것들은 MVC 바인딩 <b>앞</b>에서 통과/차단만
/// 가르므로 「404 가 계정 확인보다 먼저」 성질과 바인딩 값엔 영향이 없다. 실제 설치본 HTTP 는 CI 화면 시험 · 테스트 환경 몫.
/// </para>
/// </remarks>
public sealed class BetaClosedChatHttpGateTests
{
    private const string Tenant = "t-gate";
    private const string Employee = "e-gate";
    private const string Room = "r-gate";
    private const string EmployeeHeader = "X-Gate-Employee";
    private const string GateScheme = "gate";

    /// <summary>시험 인증 — 늘 인증됨 · 헤더가 있으면 <c>employee_id</c> 클레임을 단다.</summary>
    private sealed class GateAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public GateAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "u-gate") };
            if (Request.Headers.TryGetValue(EmployeeHeader, out var emp) && !string.IsNullOrWhiteSpace(emp))
            {
                claims.Add(new Claim("employee_id", emp.ToString()));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, GateScheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, GateScheme)));
        }
    }

    /// <summary>MVC 만 얹은 TestHost 를 띄운다. 호출자가 <c>using</c> 으로 닫는다.</summary>
    private static async Task<IHost> StartHostAsync(Mock<IChatService> service, Mock<ICurrentTenant> tenant)
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
                    services.AddAuthorization();
                    services.AddControllers().AddApplicationPart(typeof(ChatController).Assembly);   // Program.cs:415 와 같은 기본값
                    services.AddSingleton(service.Object);
                    services.AddSingleton(tenant.Object);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e => e.MapControllers());
                });
            })
            .StartAsync();
        return host;
    }

    private static async Task<(HttpStatusCode Code, string Body)> GetAsync(IHost host, string pathAndQuery, string? employeeId)
    {
        using var client = host.GetTestClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
        if (employeeId is not null)
        {
            req.Headers.Add(EmployeeHeader, employeeId);
        }

        using var res = await client.SendAsync(req);
        return (res.StatusCode, await res.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "G-S4-2-5a HTTP ?Before=(대문자) → 404(틀 글 ProblemDetails · 처리기 글 0) · 계정 확인 0 · 서비스 0(사원 클레임 없는 계정으로)")]
    public async Task G_S4_2_5a_대문자_Before_404()
    {
        var service = new Mock<IChatService>(MockBehavior.Strict);
        var tenant = new Mock<ICurrentTenant>(MockBehavior.Strict);   // 계정 안내 갈래로 가면 UserId 를 읽다가 던진다
        using var host = await StartHostAsync(service, tenant);

        var (code, body) = await GetAsync(host, $"/api/chat/rooms/{Room}/messages?Before=2026-11-01T09:00:00", employeeId: null);

        Assert.True(code == HttpStatusCode.NotFound,
            $"G-S4-2-5a ?Before= 가 {(int)code} 로 나갔다 — 바인딩이 대소문자를 가렸거나 거절 줄이 계정 확인 뒤로 밀렸다(W-S-04 06-9). 본문: {body}");
        // 🔴 실측(고침 1 F1 · 2026-11-28): HTTP 본문은 「빈 본문」 이 아니다. [ApiController] 의 클라이언트 오류 꼴
        //   (ClientErrorResultFilter · Program.cs 에 SuppressMapClientErrors 설정 0)이 NotFoundResult 를 틀 글 ProblemDetails
        //   ({"type":…,"title":"Not Found","status":404,"traceId":…})로 바꾼다. 대화 · 방 · 처리기 글은 없다 — 그것만 단언한다.
        //   설계 P2 「404 빈 본문」 과 다른 점은 개발명세서 고침 1 절에 PM 보고로 적었다(제품 코드 손 0).
        using (var doc = System.Text.Json.JsonDocument.Parse(body))
        {
            Assert.Equal(System.Text.Json.JsonValueKind.Object, doc.RootElement.ValueKind);
            Assert.Equal(404, doc.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("Not Found", doc.RootElement.GetProperty("title").GetString());
            Assert.False(doc.RootElement.TryGetProperty("message", out _), $"G-S4-2-5a 404 본문에 처리기 글이 있다: {body}");
        }

        Assert.Empty(service.Invocations);
        Assert.Empty(tenant.Invocations);
    }

    [Fact(DisplayName = "G-S4-2-5b HTTP ?before=abc(형식 틀림) → 200 옛 대화 아님(실측 400) · 서비스 0")]
    public async Task G_S4_2_5b_형식_틀린_before는_200_아님()
    {
        var service = new Mock<IChatService>(MockBehavior.Strict);
        var tenant = new Mock<ICurrentTenant>(MockBehavior.Strict);
        using var host = await StartHostAsync(service, tenant);

        var (code, body) = await GetAsync(host, $"/api/chat/rooms/{Room}/messages?before=abc", Employee);

        Assert.NotEqual(HttpStatusCode.OK, code);
        Assert.True(code is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            $"G-S4-2-5b ?before=abc 가 {(int)code} 로 나갔다 — 400(바인딩 실패) 또는 404 여야 한다. 본문: {body}");
        Assert.Equal(HttpStatusCode.BadRequest, code);   // 실측 기록값 — [ApiController] 자동 400(액션 전)
        Assert.Empty(service.Invocations);
        Assert.Empty(tenant.Invocations);
    }

    [Fact(DisplayName = "G-S4-2-5c HTTP ?before=(빈 값) → 닫힘 아님 · before = null 로 지금과 같은 길(200 · 서비스 1회)")]
    public async Task G_S4_2_5c_빈_before는_닫힘_아님()
    {
        var expected = new List<ChatMessageDto>();
        var service = new Mock<IChatService>(MockBehavior.Strict);
        service.Setup(s => s.GetMessagesAsync(Tenant, Employee, Room, 50, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var tenant = new Mock<ICurrentTenant>(MockBehavior.Strict);
        tenant.SetupGet(t => t.TenantId).Returns(Tenant);
        using var host = await StartHostAsync(service, tenant);

        var (code, body) = await GetAsync(host, $"/api/chat/rooms/{Room}/messages?before=", Employee);

        Assert.True(code != HttpStatusCode.NotFound,
            $"G-S4-2-5c ?before= (빈 값)이 404 로 닫혔다 — 빈 값은 null 길(지금과 같은 대화 읽기)이어야 한다(#20). 본문: {body}");
        Assert.Equal(HttpStatusCode.OK, code);
        service.Verify(s => s.GetMessagesAsync(Tenant, Employee, Room, 50, null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
