using System.Data;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text;
using Dapper;
using HitPan.API.Controllers;
using HitPan.API.Middleware;
using HitPan.API.Services.Cooltime;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-C14 — 새 API 3개가 미들웨어를 지나서 도는지</b>(설계 §7 · §E E-5 고침 · 9/22 화이트리스트 사고 재발 방지).
/// </summary>
/// <remarks>
/// <para>함수를 따로 부르지 않고 <b>HTTP 요청 한 건</b>을 실제 미들웨어·라우팅·권한·컨트롤러에 흘린다 —
/// 선례 <c>ApprovalRetiredAccessGateDbTests.Pipeline.cs</c>(G-AR13)와 같은 방식(<see cref="ApplicationBuilder"/> 로 <c>Program.cs</c> 순서를 세운다:
/// 인증 → 권한 → <see cref="TenantMiddleware"/> → <see cref="SessionValidityMiddleware"/> → <see cref="TermsConsentMiddleware"/> →
/// <see cref="DeviceAuthMiddleware"/> → 컨트롤러). 인증(JWT 검증)만 대역 — 검증을 통과한 뒤의 <c>User</c> 를 꽂는다.
/// 이 레포의 ERP 쪽 파이프라인 게이트는 <c>WebApplicationFactory</c> 가 아니라 이 방식이 관례다.</para>
/// <para>DB = 출하 DDL 격리 DB(실물) · 컨트롤러 = 실물 <see cref="CooltimeController"/> · 엔진 = 실물 <see cref="CooltimeEngine"/>.
/// 요청마다 서비스 공급자를 새로 만든다 ⇒ <see cref="SessionValidityMiddleware"/> 의 10초 캐시가 매번 비어 있다(= 「캐시 지남」).</para>
/// <para>🔴 상태별 기대값 고정(E-5): 행 있음 = 200 / 409 / <c>ended:false</c> · 행 지움 = <b>401</b>.</para>
/// <para>🔴 음성 대조군 ⓚ(시험 안) — 같은 요청을 <see cref="SessionValidityMiddleware"/> 를 뺀 파이프라인(= 세 주소를 세션 확인 면제에 넣은 것과 같은 효과)에
/// 다시 흘리면 행 지운 뒤에도 401 이 아니다(409 · <c>ended:false</c> · <c>asked:false</c>) ⇒ 위 401 은 세션 확인이 낸 것이다.</para>
/// </remarks>
public sealed partial class CooltimeDbGateTests
{
    private sealed record PipeOutcome(int Status, string Body, string WwwAuthenticate);

    [Fact(DisplayName = "G-C14 🔴 진짜 파이프라인 — 약관 전 403 · 물음 살아 있음 state 200/continue 200 · 물음 만료 continue 409 · 안 물음 logout ended:false · logout ended:true → 행 지움 → 세 주소 401 · 출입증 없음 401 · 대조 ⓚ(세션 확인 뺌) = 401 아님")]
    public async Task G_C14_새_API_는_미들웨어를_지난다()
    {
        if (!Ready("G-C14")) return;
        await using var db = await OpenAsync();
        var pusher = new CountingPusher();
        await SettingsAsync(db, _tenantA, true, new TimeSpan(22, 0, 0), new TimeSpan(18, 0, 0));
        var u = await InsertUserAsync(db, _tenantA);
        var sid = await InsertSessionAsync(db, _tenantA, u, "pc", askedAgoSec: 5);
        await InsertRefreshAsync(db, u, sid);

        // ⓪ 약관 미동의 — [계속]은 약관 면제 목록에 없다(갈래 ① 명세서 §5-3) ⇒ 약관 미들웨어가 막는다 = 요청이 그 단계를 실제로 지난다
        var noTerms = await PipeAsync(db, pusher, "POST", "/api/auth/cooltime/continue", u, sid, null, withSessionValidity: true);
        Assert.True(noTerms.Status == 403, $"약관 전 continue → {noTerms.Status} (기대 403) · 본문={noTerms.Body}");
        Assert.NotNull(await AskedAtAsync(db, sid));                                        // 컨트롤러까지 안 갔다
        await AgreeTermsAsync(db, u);

        // ① 행 있음 · 물음 살아 있음
        var st = await PipeAsync(db, pusher, "GET", "/api/auth/cooltime/state", u, sid, null, true);
        Assert.True(st.Status == 200, $"state → {st.Status} · {st.Body}");
        Assert.Contains("\"asked\":true", st.Body);
        var cont = await PipeAsync(db, pusher, "POST", "/api/auth/cooltime/continue", u, sid, null, true);
        Assert.True(cont.Status == 200, $"continue → {cont.Status} · {cont.Body}");
        Assert.Contains("\"snoozeMinutes\":60", cont.Body);
        Assert.Null(await AskedAtAsync(db, sid));
        // 🆕 11/27 고침 차례(§273 V1) — 이긴 [계속] 은 그 로그인 그룹에 「계속됨」 1회(같은 출입증의 다른 탭이 팝업을 닫는다).
        //   ⚠️ 엔진의 푸시 줄을 빼면 여기서 FAIL(대조군 ⓜ).
        Assert.Equal(1, pusher.Continued.Count(c => c == (_tenantA, sid)));

        // ② 행 있음 · 물음 만료(70초 전) → [계속] 409
        await db.ExecuteAsync("UPDATE user_sessions SET cooltime_asked_at = UTC_TIMESTAMP(6) - INTERVAL 70 SECOND, cooltime_snooze_until = NULL WHERE session_id = @S", new { S = sid });
        var late = await PipeAsync(db, pusher, "POST", "/api/auth/cooltime/continue", u, sid, null, true);
        Assert.True(late.Status == 409, $"물음 만료 continue → {late.Status} (기대 409) · {late.Body}");
        Assert.Contains("cooltime_already_ended", late.Body);
        var stLate = await PipeAsync(db, pusher, "GET", "/api/auth/cooltime/state", u, sid, null, true);
        Assert.Equal(200, stLate.Status);
        Assert.Contains("\"secondsLeft\":0", stLate.Body);
        Assert.Contains("\"active\":true", stLate.Body);   // 🆕 11/27 — 행 있음
        // 🆕 11/27 고침 차례(§275 · 설계 §E 「물음 만료 → logout ended:false」) — 만료 뒤 API 끝내기 = 안 끝냄 · 행 남음(서버 작업 ① 몫)
        //   ⚠️ API 끝내기의 65초 조건을 빼면 ended:true · 행 없음으로 FAIL(대조군 ⓛ).
        var lateBye = await PipeAsync(db, pusher, "POST", "/api/auth/cooltime/logout", u, sid, "{\"trigger\":\"countdown\"}", true);
        Assert.True(lateBye.Status == 200, $"물음 만료 logout → {lateBye.Status} · {lateBye.Body}");
        Assert.Contains("\"ended\":false", lateBye.Body);
        Assert.Equal(1, await SessionExistsAsync(db, sid));
        Assert.DoesNotContain(pusher.Ended, e => e.Sid == sid);
        Assert.Equal(1, pusher.Continued.Count(c => c == (_tenantA, sid)));   // 진 [계속](409)은 「계속됨」 을 보내지 않는다(① 의 1회 그대로)

        // ③ 행 있음 · 안 물음(예: 팝업 중 관리자가 끔 · E-3) → logout 200 ended:false · 행 남음
        await db.ExecuteAsync("UPDATE user_sessions SET cooltime_asked_at = NULL WHERE session_id = @S", new { S = sid });
        var notAsked = await PipeAsync(db, pusher, "POST", "/api/auth/cooltime/logout", u, sid, "{\"trigger\":\"button\"}", true);
        Assert.True(notAsked.Status == 200, $"안 물음 logout → {notAsked.Status} · {notAsked.Body}");
        Assert.Contains("\"ended\":false", notAsked.Body);
        Assert.Equal(1, await SessionExistsAsync(db, sid));

        // ④ 물음 있음 → logout 200 ended:true → 행 지움
        await db.ExecuteAsync("UPDATE user_sessions SET cooltime_asked_at = UTC_TIMESTAMP(6) - INTERVAL 5 SECOND WHERE session_id = @S", new { S = sid });
        var bye = await PipeAsync(db, pusher, "POST", "/api/auth/cooltime/logout", u, sid, "{\"trigger\":\"button\"}", true);
        Assert.True(bye.Status == 200, $"logout → {bye.Status} · {bye.Body}");
        Assert.Contains("\"ended\":true", bye.Body);
        Assert.Equal(0, await SessionExistsAsync(db, sid));
        Assert.Contains((_tenantA, sid, CooltimeEndCause.UserChoice), pusher.Ended);

        // ⑤ 행 지움 + 캐시 지남 → 세 주소 401(세션 확인 미들웨어 · 컨트롤러까지 안 감)
        foreach (var (method, path, body) in new[]
                 {
                     ("POST", "/api/auth/cooltime/continue", (string?)null),
                     ("POST", "/api/auth/cooltime/logout", "{\"trigger\":\"countdown\"}"),
                     ("GET", "/api/auth/cooltime/state", null),
                 })
        {
            var gone = await PipeAsync(db, pusher, method, path, u, sid, body, true);
            Assert.True(gone.Status == 401, $"행 지운 뒤 {method} {path} → {gone.Status} (기대 401) · {gone.Body}");
            Assert.True(string.IsNullOrEmpty(gone.WwwAuthenticate), "이 401 은 인증 처리기가 아니라 세션 확인이 낸 것이어야 한다");

            // 🔴 대조군 ⓚ — 세션 확인을 뺀 같은 파이프라인 ⇒ 401 아님(컨트롤러가 받는다)
            var ctl = await PipeAsync(db, pusher, method, path, u, sid, body, withSessionValidity: false);
            Assert.True(ctl.Status != 401, $"대조군 무효 — 세션 확인을 뺐는데 {method} {path} 가 401");
            Assert.True(ctl.Status is 200 or 409, $"대조군 {method} {path} → {ctl.Status} · {ctl.Body}");
            // 🆕 11/27 고침 차례(§275 E-5 (나)) — 캐시 틈에서 컨트롤러까지 간 state 는 active:false(화면이 「살아 있음」 으로 잘못 읽지 않게)
            if (path.EndsWith("/state", StringComparison.Ordinal))
                Assert.Contains("\"active\":false", ctl.Body);
        }

        // ⑥ 출입증 없음 → 권한 단계의 JwtBearer Challenge 401(WWW-Authenticate: Bearer) — [Authorize] 가 실제로 걸려 있다
        var anon = await PipeAsync(db, pusher, "GET", "/api/auth/cooltime/state", null, null, null, true);
        Assert.Equal(401, anon.Status);
        Assert.StartsWith("Bearer", anon.WwwAuthenticate);
    }

    /// <summary>약관 4건 동의 — <c>TermsConsentMiddleware.CurrentTermsVersion</c>(v2.0.0) · G-AR13 과 같은 준비.</summary>
    private async Task AgreeTermsAsync(MySqlConnection db, string userId) =>
        await db.ExecuteAsync(@"
            INSERT INTO user_terms_consent (consent_id, tenant_id, user_id, terms_version,
                agree_service, agree_privacy, agree_subscription, agree_data_ownership, agreed_at, client_ip)
            VALUES (@C, @T, @U, 'v2.0.0', 1, 1, 1, 1, NOW(3), '127.0.0.1')",
            new { C = Guid.NewGuid().ToString(), T = _tenantA, U = userId });

    private async Task<PipeOutcome> PipeAsync(MySqlConnection db, CountingPusher pusher, string method, string path,
        string? userId, string? sid, string? jsonBody, bool withSessionValidity)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DeviceApproval:Enabled"] = "true" })   // 출하 appsettings 그대로
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(new DiagnosticListener("HitPan.Tests.GC14"));
        services.AddSingleton<DiagnosticSource>(sp => sp.GetRequiredService<DiagnosticListener>());
        services.AddSingleton<IWebHostEnvironment>(new Gc14HostEnvironment());
        services.AddSingleton<IHostEnvironment>(sp => sp.GetRequiredService<IWebHostEnvironment>());
        services.AddRouting();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddAuthorization();
        services.AddControllers().AddApplicationPart(typeof(CooltimeController).Assembly);
        services.AddScoped<CurrentTenant>();
        services.AddSingleton<IDbConnection>(db);
        services.AddSingleton<IAuditService>(new NoOpAudit());
        services.AddSingleton<ITenantDeviceService>(sp => new TenantDeviceService(
            db, sp.GetRequiredService<IAuditService>(), config, sp.GetRequiredService<ILogger<TenantDeviceService>>()));
        services.AddSingleton<IUserService>(sp => new UserService(db, sp.GetRequiredService<IAuditService>()));
        services.AddSingleton<ICooltimePusher>(pusher);
        services.AddScoped<CooltimeEngine>();                        // Program.cs 와 같은 Scoped · 생성자 = (IDbConnection, ICooltimePusher, ILogger)

        await using var sp = services.BuildServiceProvider();
        var app = new ApplicationBuilder(sp);
        app.UseRouting();
        // ↓ UseAuthentication(JWT) 대역 — 검증을 통과한 User 를 꽂는다. userId 가 null 이면 출입증 없음.
        app.Use(async (ctx, next) =>
        {
            if (userId is not null)
            {
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("account_type", "tenant_user"),
                    new Claim("tenant_id", _tenantA),
                    new Claim("user_id", userId),
                    new Claim("sid", sid!),
                    new Claim("role", "user"),
                }, authenticationType: "GateJwt"));
            }
            await next(ctx);
        });
        app.UseAuthorization();
        app.UseMiddleware<TenantMiddleware>();
        if (withSessionValidity) app.UseMiddleware<SessionValidityMiddleware>();
        app.UseMiddleware<TermsConsentMiddleware>();
        app.UseMiddleware<DeviceAuthMiddleware>();
        app.UseEndpoints(e => e.MapControllers());
        var pipeline = app.Build();

        await using var scope = sp.CreateAsyncScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
        ctx.Request.Headers["User-Agent"] = PcUa;
        if (method == "POST")
        {
            var bytes = Encoding.UTF8.GetBytes(jsonBody ?? string.Empty);
            ctx.Request.ContentType = "application/json";
            ctx.Request.ContentLength = bytes.Length;
            ctx.Request.Body = new MemoryStream(bytes);
        }
        var body = new MemoryStream();
        ctx.Response.Body = body;

        await pipeline(ctx);
        return new PipeOutcome(ctx.Response.StatusCode, Encoding.UTF8.GetString(body.ToArray()),
            ctx.Response.Headers.WWWAuthenticate.ToString());
    }

    private sealed class Gc14HostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = typeof(CooltimeController).Assembly.GetName().Name!;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Development";
    }
}
