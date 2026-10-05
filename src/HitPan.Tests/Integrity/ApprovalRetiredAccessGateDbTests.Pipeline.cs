using System.Data;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using Dapper;
using HitPan.API.Controllers;
using HitPan.API.Middleware;
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
/// 🔴 <b>G-AR13 — 진짜 파이프라인</b>(설계 §8 · 9/22 P0 교훈). 함수를 따로 부르지 않고 <b>HTTP 요청 한 건</b>을
/// 실제 미들웨어·라우팅·권한·컨트롤러에 흘린다.
/// </summary>
/// <remarks>
/// <para>🔴 <b>왜</b> — 9/22 <c>mainpc-proof</c> P0 는 게이트 18건이 초록인데 실제 요청이 <c>TenantMiddleware</c> 에서 401 로 잘렸다.
/// 그 요청이 그 함수까지 닿는가는 파이프라인을 지나가 봐야 안다.</para>
/// <para>⚠️ <c>WebApplicationFactory</c>·<c>TestServer</c> 가 아니다 — 시험 프로젝트에 그 참조가 없다(선례 <c>UpdateConsentMainPcGateTests:35</c> ·
/// <c>SessionRecordConcurrentPcGateTests:547</c>). 같은 선례대로 <see cref="ApplicationBuilder"/> 로 <c>Program.cs</c> 의 순서
/// (:666 인증 → :667 권한 → :671 <see cref="TenantMiddleware"/> → :676 <see cref="SessionValidityMiddleware"/> →
/// :689 <see cref="TermsConsentMiddleware"/> → :694 <see cref="DeviceAuthMiddleware"/> → :698 컨트롤러)를 세운다.
/// 빠진 것: 예외·IP 화이트리스트·감사·속도제한·멱등 미들웨어(판정과 무관 · 이 게이트의 관심사는 위 넷). 인증(JWT 검증)만 대역 —
/// 검증을 통과한 뒤의 <c>User</c> 를 꽂는다. 기본 인증 방식은 실물 JwtBearer(<c>AuthExtensions.cs:51</c>) — 출입증 없는 요청의
/// 401 은 권한 단계의 Challenge 가 낸다(10/5 CI 6차 교정: 이 등록이 빠져 예외가 났고, 「TenantMiddleware 401」 서술도 틀렸다).</para>
/// <para>DB 는 출하 DDL 격리 DB(실물) — 세션·약관·기기 줄·사람이 전부 실제 표에서 판정된다. 서비스도 실물
/// (<see cref="TenantDeviceService"/> · <see cref="UserService"/>). 컨트롤러도 실물(<see cref="AccessStatusController"/> ·
/// 업무 API 대표로 <see cref="UnifiedCalendarController"/> <c>GET /api/dashboard/unified-calendar</c> — DeviceAuth 통과 목록에 없는 업무 경로).</para>
/// <para>「인증키 없는 새 직원 PC 로그인」은 <c>/api/auth/login</c> HTTP 가 아니라 그 로그인이 부르는 실물
/// <c>RegisterOrRefreshAsync</c> + 세션·refresh 줄(로그인 흉내 · Seal1 관례)로 세운다 — <c>AuthService</c> 전체는 이 게이트 밖.</para>
/// <para>🔴 <b>음성 대조군</b> — 같은 시험에서 <see cref="DeviceAuthMiddleware"/> 의 판정 값만 <b>옛 판</b>(설정 <c>DeviceApproval:Enabled=true</c>
/// 를 읽던 :77)으로 되돌린 파이프라인을 다시 세운다 ⇒ 같은 직원·같은 요청이 403 <c>forbidden_device_auth</c>. 되돌림은 시험 안 리플렉션
/// (제품 코드 0줄). 이 403 이 안 나면 「200」은 미들웨어를 지나간 증거가 아니다.</para>
/// </remarks>
public sealed partial class ApprovalRetiredAccessGateDbTests
{
    private const string BusinessPath = "/api/dashboard/unified-calendar";

    private sealed record PipeOutcome(int Status, string Body, string WwwAuthenticate);

    [Fact(DisplayName = "G-AR13 🔴 진짜 파이프라인(인증→권한→Tenant→SessionValidity→약관→DeviceAuth→컨트롤러) — 인증키 없는 새 직원 PC 업무 API 200 · 직원 access-status 403 · 대표 200 · 출입증 없음 401 · 대조군(DeviceAuth 옛 판정 → 같은 직원 403 forbidden_device_auth)")]
    public async Task AR13_Real_Pipeline()
    {
        if (!Ready("G-AR13")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db, _tenantA, pcLimit: 1, mobileLimit: 1);
        var owner = await InsertUserAsync(db, _tenantA, "owner_a", "대표", parent: true);
        var staff = await InsertUserAsync(db, _tenantA, "staff_new", "새직원");
        await InsertDeviceAsync(db, _tenantA, owner, "pc", "approved");   // 컴퓨터 칸은 이미 찼다

        // 새 직원 PC 첫 로그인 — 로그인이 부르는 실물 기기 등록(인증키 발급 없음)
        var reg = await NewService(db).RegisterOrRefreshAsync(_tenantA, staff, Pc(NewFp()), "127.0.0.1");
        Assert.True(reg.allowed, $"로그인 단계에서 막혔다 — {reg.reason}");
        Assert.Null(await db.ExecuteScalarAsync<string?>(
            "SELECT auth_key_hash FROM tenant_devices WHERE device_id = @Id", new { Id = reg.deviceId }));

        var staffSid = await PipeLoginAsync(db, staff);
        var ownerSid = await PipeLoginAsync(db, owner);
        await AgreeTermsAsync(db, staff);
        await AgreeTermsAsync(db, owner);

        // ① 인증키 없는 새 직원 PC — 업무 API 200 (장비넘버만 실린다 · 인증키 헤더 없음)
        var biz = await PipeAsync(db, BusinessPath + "?year=2026&month=10", staff, "tenant_user", staffSid, reg.deviceId, oldDeviceJudge: false);
        Assert.True(biz.Status == 200, $"새 직원 업무 API → {biz.Status} (기대 200) · 본문={biz.Body}");

        // ② 직원 → access-status 둘 다 403(대표만) · ③ 대표 → 200
        foreach (var p in new[] { "/api/access-status/current", "/api/access-status/alerts" })
        {
            var s = await PipeAsync(db, p, staff, "tenant_user", staffSid, reg.deviceId, oldDeviceJudge: false);
            Assert.True(s.Status == 403, $"직원 {p} → {s.Status} (기대 403) · 본문={s.Body}");
            Assert.Contains("대표 계정에서만", s.Body);
            var o = await PipeAsync(db, p, owner, "tenant_admin", ownerSid, null, oldDeviceJudge: false);
            Assert.True(o.Status == 200, $"대표 {p} → {o.Status} (기대 200) · 본문={o.Body}");
        }
        var cur = await PipeAsync(db, "/api/access-status/current", owner, "tenant_admin", ownerSid, null, oldDeviceJudge: false);
        Assert.Contains(staff, cur.Body);                                  // 실물 서비스가 실제 표를 읽었다

        // ④ 출입증 없음 → 권한 단계(:667)가 JwtBearer Challenge 로 401 — TenantMiddleware 까지 안 간다(실제 앱과 같은 길).
        //    WWW-Authenticate: Bearer = 그 401 을 낸 것이 인증 처리기라는 표식(TenantMiddleware 401 은 이 머리글이 없다)
        var anon = await PipeAsync(db, "/api/access-status/current", null, null, null, null, oldDeviceJudge: false);
        Assert.Equal(401, anon.Status);
        Assert.StartsWith("Bearer", anon.WwwAuthenticate);

        // 🔴 음성 대조군 — DeviceAuth 판정만 옛 판(설정 true 를 읽음)으로 되돌린 같은 파이프라인 ⇒ 같은 직원·같은 요청 403
        var ctl = await PipeAsync(db, BusinessPath + "?year=2026&month=10", staff, "tenant_user", staffSid, reg.deviceId, oldDeviceJudge: true);
        Assert.True(ctl.Status == 403, $"대조군 무효 — 옛 판정인데 {ctl.Status} · 본문={ctl.Body}");
        Assert.Contains("forbidden_device_auth", ctl.Body);
        // 대조군 짝 — 옛 판정에서도 대표의 탈출 경로(/api/devices·/api/auth)가 아닌 access-status 는 막혔다 = 판정이 실제로 돈다
        var ctlOwner = await PipeAsync(db, "/api/access-status/current", owner, "tenant_admin", ownerSid, null, oldDeviceJudge: true);
        Assert.Equal(403, ctlOwner.Status);
    }

    /// <summary>로그인 한 번 흉내 — 세션 행(pc) + refresh 행(같은 sid). Seal1 FakeLoginAsync 관례.</summary>
    private async Task<string> PipeLoginAsync(MySqlConnection db, string userId)
    {
        var sid = Guid.NewGuid().ToString();
        await db.ExecuteAsync(@"
            INSERT INTO user_sessions (session_id, user_id, tenant_id, expires_at, device_kind)
            VALUES (@S, @U, @T, UTC_TIMESTAMP(6) + INTERVAL 8 HOUR, 'pc')",
            new { S = sid, U = userId, T = _tenantA });
        await db.ExecuteAsync(@"
            INSERT INTO refresh_tokens (token_id, user_id, token_hash, expires_at, is_revoked, session_id)
            VALUES (@Id, @U, @H, UTC_TIMESTAMP(6) + INTERVAL 7 DAY, 0, @S)",
            new { Id = Guid.NewGuid().ToString(), U = userId, H = Guid.NewGuid().ToString("N"), S = sid });
        return sid;
    }

    /// <summary>약관 4건 동의 — <c>TermsConsentMiddleware.CurrentTermsVersion</c>(v2.0.0). 안 넣으면 약관 미들웨어가 먼저 막는다.</summary>
    private async Task AgreeTermsAsync(MySqlConnection db, string userId) =>
        await db.ExecuteAsync(@"
            INSERT INTO user_terms_consent (consent_id, tenant_id, user_id, terms_version,
                agree_service, agree_privacy, agree_subscription, agree_data_ownership, agreed_at, client_ip)
            VALUES (@C, @T, @U, 'v2.0.0', 1, 1, 1, 1, NOW(3), '127.0.0.1')",
            new { C = Guid.NewGuid().ToString(), T = _tenantA, U = userId });

    private async Task<PipeOutcome> PipeAsync(MySqlConnection db, string pathAndQuery, string? userId, string? accountType,
        string? sid, string? deviceId, bool oldDeviceJudge)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DeviceApproval:Enabled"] = "true" })   // 출하 appsettings 그대로
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(new DiagnosticListener("HitPan.Tests.GAR13"));
        services.AddSingleton<DiagnosticSource>(sp => sp.GetRequiredService<DiagnosticListener>());
        services.AddSingleton<IWebHostEnvironment>(new Ar13HostEnvironment());
        services.AddSingleton<IHostEnvironment>(sp => sp.GetRequiredService<IWebHostEnvironment>());
        services.AddRouting();
        // AuthExtensions.cs:51 과 같은 기본 방식(JwtBearer 실물) — 출입증 없는 요청을 권한 단계가 401 로 돌려보내는 주체.
        // 검증(토큰 읽기)은 아래 대역이 대신하므로 이 처리기는 Challenge·Forbid 만 맡는다. (10/5 CI 6차: 이게 빠져 ④ 에서 예외)
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddAuthorization(o =>
        {
            // Program.cs:383 · :394 와 같은 조건
            o.AddPolicy("TenantOnly", p => p.RequireAssertion(ctx =>
                ctx.User.HasClaim("account_type", "tenant_admin") || ctx.User.HasClaim("account_type", "tenant_user")));
            o.AddPolicy("TenantAdminOnly", p => p.RequireAssertion(ctx => ctx.User.HasClaim("account_type", "tenant_admin")));
        });
        services.AddControllers().AddApplicationPart(typeof(AccessStatusController).Assembly);
        services.AddScoped<CurrentTenant>();
        services.AddSingleton<IDbConnection>(db);
        services.AddSingleton<IAuditService>(new NoOpAudit());
        services.AddSingleton<ITenantDeviceService>(sp => new TenantDeviceService(
            db, sp.GetRequiredService<IAuditService>(), config, sp.GetRequiredService<ILogger<TenantDeviceService>>()));
        services.AddSingleton<IUserService>(sp => new UserService(db, sp.GetRequiredService<IAuditService>()));

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
                    new Claim("account_type", accountType!),
                    new Claim("tenant_id", _tenantA),
                    new Claim("user_id", userId),
                    new Claim("sid", sid!),
                    new Claim("role", accountType == "tenant_admin" ? "admin" : "user"),
                }, authenticationType: "GateJwt"));
            }
            await next(ctx);
        });
        app.UseAuthorization();
        app.UseMiddleware<TenantMiddleware>();
        app.UseMiddleware<SessionValidityMiddleware>();
        app.UseMiddleware<TermsConsentMiddleware>();
        if (!oldDeviceJudge)
        {
            app.UseMiddleware<DeviceAuthMiddleware>();
        }
        else
        {
            // 대조군 — 실물 클래스 · 판정 값만 옛 판(:77 설정 읽기 = true)으로. 제품 코드 0줄.
            app.Use(next =>
            {
                var mw = new DeviceAuthMiddleware(next, sp.GetRequiredService<ILogger<DeviceAuthMiddleware>>(), config);
                var f = typeof(DeviceAuthMiddleware).GetField("_enabled", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new Xunit.Sdk.XunitException("DeviceAuthMiddleware._enabled 가 없다 — 대조군을 같이 고쳐라");
                f.SetValue(mw, config.GetValue<bool>("DeviceApproval:Enabled"));
                return ctx => mw.InvokeAsync(ctx, ctx.RequestServices.GetRequiredService<IDbConnection>(),
                    ctx.RequestServices.GetRequiredService<ITenantDeviceService>());
            });
        }
        app.UseEndpoints(e => e.MapControllers());
        var pipeline = app.Build();

        await using var scope = sp.CreateAsyncScope();
        var q = pathAndQuery.IndexOf('?');
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        ctx.Request.Method = "GET";
        ctx.Request.Path = q < 0 ? pathAndQuery : pathAndQuery[..q];
        if (q >= 0) ctx.Request.QueryString = new QueryString(pathAndQuery[q..]);
        ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
        ctx.Request.Headers["CF-Connecting-IP"] = "203.0.113.7";        // 터널로 들어온 직원 PC
        if (deviceId is not null) ctx.Request.Headers["X-HitPan-Device-Id"] = deviceId;   // 인증키 헤더는 없다
        var body = new MemoryStream();
        ctx.Response.Body = body;

        await pipeline(ctx);
        return new PipeOutcome(ctx.Response.StatusCode, Encoding.UTF8.GetString(body.ToArray()),
            ctx.Response.Headers.WWWAuthenticate.ToString());
    }

    private sealed class Ar13HostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = typeof(AccessStatusController).Assembly.GetName().Name!;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Development";
    }
}
