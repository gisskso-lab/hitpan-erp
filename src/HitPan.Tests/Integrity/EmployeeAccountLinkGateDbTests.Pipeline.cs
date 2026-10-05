using System.Data;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using HitPan.API.Controllers;
using HitPan.API.Middleware;
using HitPan.Application.DTOs.Device;
using HitPan.Application.DTOs.Permission;
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
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-E9 · G-E10 · G-E13 — 진짜 파이프라인</b>(설계 §8 · 9/22 P0 교훈 · 선례 <c>ApprovalRetiredAccessGateDbTests.Pipeline</c> G-AR13).
/// HTTP 요청 한 건을 실물 인증(JwtBearer 기본 방식)→권한→<see cref="TenantMiddleware"/>→<see cref="SessionValidityMiddleware"/>→
/// <see cref="TermsConsentMiddleware"/>→<see cref="DeviceAuthMiddleware"/>→컨트롤러에 흘린다. JWT 검증만 대역(검증 뒤 User 를 꽂는다).
/// </summary>
/// <remarks>
/// <para>서비스는 DI 로 실물 등록(<see cref="UserService"/> · <see cref="PermissionService"/> · <see cref="EmployeeService"/>) — 갈래2 가
/// 생성자 의존을 늘려도 따라간다. 새 API(<c>for-employee</c>·<c>my-level</c>·<c>linkable-employees</c>)·새 필터 <c>[RequireUsersLevel(n)]</c> 는
/// 경로 문자열로만 부른다 — <c>// 갈래2 합류 후 연결</c>. 없으면 404 ⇒ 기대 200/403 과 달라 FAIL.</para>
/// <para>🔴 [3-V] 반영(작업지시서 §8 · 23affed2): P1-01 관리자 직무 사원 409 · P2-02 권한 0 직원의 사원목록 LoginId 가림 ·
/// P2-06 직원 토큰(tenant_user)+등록 기기 · 403 은 본문 error 코드로 DeviceAuth 403(<c>forbidden_device_auth</c>)·약관 403 과 구별.</para>
/// </remarks>
public sealed partial class EmployeeAccountLinkGateDbTests
{
    private const string PcUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36";

    private sealed record Who(string UserId, string AccountType, string Sid, string? DeviceId);
    private sealed record Res(int Status, string Body);

    /// <summary>직원 한 명 — 계정 · 로그인(세션·refresh) · 약관 · 등록 기기(실물 RegisterOrRefreshAsync) · 단계 권한(실물 SaveAsync).</summary>
    private async Task<Who> StaffAsync(MySqlConnection db, string loginId, int level, bool admin = false, bool parent = false,
        bool terms = true)
    {
        var uid = parent ? _ownerId : await InsertUserAsync(db, loginId, loginId, admin: admin);
        var sid = Guid.NewGuid().ToString();
        await db.ExecuteAsync(@"INSERT INTO user_sessions (session_id, user_id, tenant_id, expires_at, device_kind)
                                VALUES (@S, @U, @T, UTC_TIMESTAMP(6) + INTERVAL 8 HOUR, 'pc')", new { S = sid, U = uid, T = _tenantA });
        await db.ExecuteAsync(@"INSERT INTO refresh_tokens (token_id, user_id, token_hash, expires_at, is_revoked, session_id)
                                VALUES (@Id, @U, @H, UTC_TIMESTAMP(6) + INTERVAL 7 DAY, 0, @S)",
            new { Id = Guid.NewGuid().ToString(), U = uid, H = Guid.NewGuid().ToString("N"), S = sid });
        if (terms)
            await db.ExecuteAsync(@"INSERT INTO user_terms_consent (consent_id, tenant_id, user_id, terms_version,
                    agree_service, agree_privacy, agree_subscription, agree_data_ownership, agreed_at, client_ip)
                    VALUES (@C, @T, @U, 'v2.0.0', 1, 1, 1, 1, NOW(3), '127.0.0.1')",
                new { C = Guid.NewGuid().ToString(), T = _tenantA, U = uid });
        var devSvc = new TenantDeviceService(db, new NoOpAudit(), new ConfigurationBuilder().Build(), NullLogger<TenantDeviceService>.Instance);
        var reg = await devSvc.RegisterOrRefreshAsync(_tenantA, uid, new RegisterDeviceRequest
        {
            Fingerprint = "HFPv2-" + Guid.NewGuid().ToString("N"), DeviceType = "pc", UserAgent = PcUa,
            DeviceName = "사무실 PC", IsLocalConsole = false
        }, "127.0.0.1");
        Assert.True(reg.allowed, $"기기 등록 단계에서 막혔다 — {reg.reason}");
        var code = level switch { 1 => "USERS", 2 => "USERS_ACCOUNT", 3 => "USERS_SEAT", _ => null };
        if (code is not null && !parent && !admin)
            await new PermissionService(db, new CurrentTenant()).SaveAsync(new SavePermissionsDto
            {
                UserId = uid,
                Permissions = { new MenuPermissionDto { MenuCode = code, CanView = true } }
            }, _tenantA);   // 갈래2 합류 후: SaveAsync 가 위→아래 채움(§5-1)
        return new Who(uid, admin || parent ? "tenant_admin" : "tenant_user", sid, reg.deviceId);
    }

    private async Task<Res> PipeAsync(MySqlConnection db, string method, string path, Who? who, string? json = null,
        bool tenantOnlyAsAdmin = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DeviceApproval:Enabled"] = "true" }).Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(new DiagnosticListener("HitPan.Tests.GE13"));
        services.AddSingleton<DiagnosticSource>(sp => sp.GetRequiredService<DiagnosticListener>());
        services.AddSingleton<IWebHostEnvironment>(new E13HostEnvironment());
        services.AddSingleton<IHostEnvironment>(sp => sp.GetRequiredService<IWebHostEnvironment>());
        services.AddRouting();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();   // 10/5 CI 6차 교훈 — 실물 기본 방식
        services.AddAuthorization(o =>
        {
            // Program.cs:383 · :394 와 같은 조건. 대조군은 TenantOnly 를 관리자 전용으로 「틀어」 같은 요청을 다시 보낸다.
            o.AddPolicy("TenantOnly", p => p.RequireAssertion(ctx => tenantOnlyAsAdmin
                ? ctx.User.HasClaim("account_type", "tenant_admin")
                : ctx.User.HasClaim("account_type", "tenant_admin") || ctx.User.HasClaim("account_type", "tenant_user")));
            o.AddPolicy("TenantAdminOnly", p => p.RequireAssertion(ctx => ctx.User.HasClaim("account_type", "tenant_admin")));
        });
        services.AddControllers().AddApplicationPart(typeof(UserController).Assembly);
        services.AddScoped<CurrentTenant>();
        services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenant>());
        services.AddSingleton<IDbConnection>(db);
        services.AddSingleton<IAuditService>(new NoOpAudit());
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddSingleton<ITenantDeviceService>(sp => new TenantDeviceService(db, new NoOpAudit(), config, NullLogger<TenantDeviceService>.Instance));

        await using var sp = services.BuildServiceProvider();
        var app = new ApplicationBuilder(sp);
        app.UseRouting();
        app.Use(async (ctx, next) =>
        {
            if (who is not null)
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("account_type", who.AccountType), new Claim("tenant_id", _tenantA),
                    new Claim("user_id", who.UserId), new Claim("sid", who.Sid),
                    new Claim("role", who.AccountType == "tenant_admin" ? "admin" : "user"),
                }, authenticationType: "GateJwt"));
            await next(ctx);
        });
        app.UseAuthorization();
        app.UseMiddleware<TenantMiddleware>();
        app.UseMiddleware<SessionValidityMiddleware>();
        app.UseMiddleware<TermsConsentMiddleware>();
        app.UseMiddleware<DeviceAuthMiddleware>();
        app.UseEndpoints(e => e.MapControllers());
        var pipeline = app.Build();

        await using var scope = sp.CreateAsyncScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var q = path.IndexOf('?');
        ctx.Request.Method = method;
        ctx.Request.Path = q < 0 ? path : path[..q];
        if (q >= 0) ctx.Request.QueryString = new QueryString(path[q..]);
        ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
        ctx.Request.Headers["CF-Connecting-IP"] = "203.0.113.7";
        if (who?.DeviceId is not null) ctx.Request.Headers["X-HitPan-Device-Id"] = who.DeviceId;
        if (json is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Request.ContentType = "application/json";
            ctx.Request.ContentLength = bytes.Length;
            ctx.Request.Body = new MemoryStream(bytes);
        }
        var body = new MemoryStream();
        ctx.Response.Body = body;
        await pipeline(ctx);
        return new Res(ctx.Response.StatusCode, Encoding.UTF8.GetString(body.ToArray()));
    }

    private static bool Ok2xx(Res r) => r.Status is >= 200 and < 300;

    /// <summary>403 의 출처 — 단계 필터/정책(Forbid · 본문 없음 또는 error 코드 없음)이어야 하고, 약관·기기 미들웨어 403 이면 안 된다(P2-06).</summary>
    private static void AssertFilter403(Res r, string what)
    {
        Assert.True(r.Status == 403, $"{what} → {r.Status} (기대 403) · 본문={r.Body}");
        Assert.DoesNotContain("forbidden_device_auth", r.Body);
        Assert.DoesNotContain("forbidden_terms_consent", r.Body);
    }

    // ⬛ [합류 전] 키를 몰라 "email" 도 함께 보냈다 — 계약(개발명세서 §4 ④) 키만 보낸다: employeeId·loginId·password·role
    private static string ForEmployeeJson(string emp, string login, string role = "User") =>
        $"{{\"employeeId\":\"{emp}\",\"loginId\":\"{login}\",\"password\":\"{Pw}\",\"role\":\"{role}\"}}";

    /// <summary>JSON 본문의 문자열 칸 하나(없으면 null) — 409 의 <c>code</c> 를 계약대로 읽는다(§4).</summary>
    private static string? JsonStr(Res r, string name)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(r.Body);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                   && doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
                ? v.GetString() : null;
        }
        catch (System.Text.Json.JsonException ex)
        {
            Console.Error.WriteLine($"[EmployeeAccountLinkGate] JSON 아님({r.Status}): {ex.Message}");
            return null;
        }
    }

    /// <summary><c>GET /api/users/my-level</c> 계약 ① <c>{ level, isAdmin }</c> 의 level.</summary>
    private static int MyLevel(Res r)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(r.Body);
        return doc.RootElement.GetProperty("level").GetInt32();
    }

    private static void Assert409Code(Res r, string code, string what)
    {
        Assert.True(r.Status == 409, $"{what} → {r.Status} (기대 409 {code}) · {r.Body}");
        Assert.Equal(code, JsonStr(r, "code"));
    }

    // ══ G-E9 · G-E10 ══

    [Fact(DisplayName = "G-E9 🔴 단계 0/1/2/3 × API 표(§5-2) 허용·403 전수 · 단계 2 role=TenantAdmin ⇒ 일반 · 관리자 직무 사원 409(P1-01) · 대표·관리자·본인 suspend 403 · G-E10 단계 3 권한설정 403 · 대조군(울타리 없는 옛 경로는 관리자 계정을 만든다)")]
    public async Task E9_Level_Matrix_And_Fence()
    {
        if (!Ready("G-E9")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db);
        var owner = await StaffAsync(db, "owner01", 3, parent: true);
        var manager = await StaffAsync(db, "mgr0900", 3, admin: true);
        var target = await InsertUserAsync(db, "t0900", "대상");

        for (var lv = 0; lv <= 3; lv++)
        {
            var s = await StaffAsync(db, $"s{lv}", lv);
            var emp = await InsertEmployeeAsync(db, $"09{lv}1", $"사원{lv}");

            // 단계 판정 함수(§5-1) — my-level 이 그 값을 돌려준다(§연결: 계약 ① { level, isAdmin })
            var my = await PipeAsync(db, "GET", "/api/users/my-level", s);
            Assert.True(my.Status == 200, $"my-level 단계{lv} → {my.Status} · {my.Body}");
            // ⬛ [합류 전] Assert.Matches(new Regex($@"(^|\D){lv}(\D|$)"), my.Body);  — 숫자가 아무 데나 있어도 통과했다
            Assert.Equal(lv, MyLevel(my));
            Assert.Contains("\"isAdmin\":false", my.Body);

            foreach (var p in new[] { "/api/users", "/api/users/seats" })
            {
                var r = await PipeAsync(db, "GET", p, s);
                if (lv >= 1) Assert.True(r.Status == 200, $"단계{lv} GET {p} → {r.Status}"); else AssertFilter403(r, $"단계0 GET {p}");
            }
            var link = await PipeAsync(db, "GET", "/api/users/linkable-employees", s);
            // ⬛ [CI 1차 G-E9] 아이디 $"fe{lv}"·$"cu{lv}"(3자) — P3-09 아이디 규칙(공백 없이 4자 이상)을 시험 입력이 어겨 400 invalid_input.
            //   규칙은 그대로 두고 입력값만 규칙에 맞춘다(5자).
            var feId = $"fe09{lv}";
            var cuId = $"cu09{lv}";
            var fe = await PipeAsync(db, "POST", "/api/users/for-employee", s, ForEmployeeJson(emp, feId, "TenantAdmin"));
            var cu = await PipeAsync(db, "POST", "/api/users", s,
                $"{{\"email\":\"{cuId}\",\"userName\":\"{cuId}\",\"password\":\"{Pw}\",\"role\":\"TenantAdmin\"}}");
            var su = await PipeAsync(db, "POST", $"/api/users/{target}/suspend", s);
            var re = await PipeAsync(db, "POST", $"/api/users/{target}/resume", s);
            if (lv >= 2)
            {
                Assert.True(link.Status == 200, $"단계{lv} linkable → {link.Status}");
                Assert.True(Ok2xx(fe), $"단계{lv} for-employee → {fe.Status} · {fe.Body}");
                Assert.True(Ok2xx(su) && Ok2xx(re), $"단계{lv} suspend/resume → {su.Status}/{re.Status}");
                // 울타리(§5-3 · P-4) — 요청 role=TenantAdmin 이어도 일반으로
                Assert.Equal("tenant_user", await db.ExecuteScalarAsync<string>(
                    "SELECT account_type FROM users WHERE tenant_id=@T AND email=@E", new { T = _tenantA, E = feId }));
            }
            else
            {
                AssertFilter403(link, $"단계{lv} linkable");
                AssertFilter403(fe, $"단계{lv} for-employee");
                AssertFilter403(su, $"단계{lv} suspend");
                AssertFilter403(re, $"단계{lv} resume");
            }
            // V5-06(작업지시서 §8-2) — 「새 사원과 함께」(POST /api/users)는 단계와 무관하게 직원 403(대표·관리자만)
            AssertFilter403(cu, $"단계{lv} POST users");
            if (lv < 2)
            {
                Assert.Equal(0L, await db.ExecuteScalarAsync<long>(
                    "SELECT COUNT(*) FROM users WHERE tenant_id=@T AND email=@E", new { T = _tenantA, E = feId }));
            }
            // P-3 — 수정·계정폐기·비번 초기화는 단계와 무관하게 대표·관리자만
            AssertFilter403(await PipeAsync(db, "PUT", $"/api/users/{target}", s, "{\"userName\":\"x\",\"role\":\"User\"}"), $"단계{lv} PUT");
            AssertFilter403(await PipeAsync(db, "DELETE", $"/api/users/{target}", s), $"단계{lv} DELETE");
            AssertFilter403(await PipeAsync(db, "POST", $"/api/users/{target}/reset-password", s), $"단계{lv} reset-password");
            // G-E10 — 권한설정은 단계 3 이어도 403(스스로 올리기 차단)
            AssertFilter403(await PipeAsync(db, "POST", "/api/permissions", s,
                $"{{\"userId\":\"{s.UserId}\",\"permissions\":[{{\"menuCode\":\"USERS_SEAT\",\"canView\":true}}]}}"), $"단계{lv} POST permissions");

            if (lv == 2)
            {
                // 대표·관리자·본인 suspend 403
                foreach (var (id, w) in new[] { (owner.UserId, "대표"), (manager.UserId, "관리자"), (s.UserId, "본인") })
                    AssertFilter403(await PipeAsync(db, "POST", $"/api/users/{id}/suspend", s), $"단계2 → {w} suspend");
                // P1-01 — 관리자 직무 사원(employees.role=tenant_admin · 로그인 role 클레임의 출처)에 비관리자가 만들면 409
                var adminJob = await InsertEmployeeAsync(db, "0929", "관리직무");
                await db.ExecuteAsync("UPDATE employees SET role='tenant_admin' WHERE employee_id=@E", new { E = adminJob });
                var aj = await PipeAsync(db, "POST", "/api/users/for-employee", s, ForEmployeeJson(adminJob, "aj0929"));
                Assert409Code(aj, "employee_role_not_general", "관리자 직무 사원 for-employee");
                Assert.Null((await EmpLinkAsync(db, adminJob)).userId);
            }
        }

        // 대표·관리자 = 늘 3 · 권한설정 200(E10 의 403 이 경로 없음 404 가 아님을 보이는 짝)
        // ⬛ [합류 전] Assert.Matches(new Regex(@"(^|\D)3(\D|$)"), (await PipeAsync(db, "GET", "/api/users/my-level", manager)).Body);
        var mgrLevel = await PipeAsync(db, "GET", "/api/users/my-level", manager);
        Assert.Equal(3, MyLevel(mgrLevel));
        Assert.Contains("\"isAdmin\":true", mgrLevel.Body);
        var ownerPerm = await PipeAsync(db, "POST", "/api/permissions", owner,
            $"{{\"userId\":\"{target}\",\"permissions\":[{{\"menuCode\":\"USERS\",\"canView\":true}}]}}");
        Assert.True(Ok2xx(ownerPerm), $"대표 POST permissions → {ownerPerm.Status}");

        // 🔴 대조군 — 울타리 없는 옛 경로(대표 권한의 CreateAsync)는 같은 role=TenantAdmin 요청으로 관리자 계정을 만든다
        //   = 로그인하면 role 클레임 TenantAdmin(Layer 0 전권). 울타리를 빼면 단계 2 가 이 자리로 간다.
        var old = await new UserService(db, new NoOpAudit()).CreateAsync(
            new HitPan.Application.DTOs.User.CreateUserDto { Email = "old-admin", UserName = "옛", Password = Pw, Role = "TenantAdmin" }, _tenantA);
        Assert.Equal("tenant_admin", await db.ExecuteScalarAsync<string>("SELECT account_type FROM users WHERE user_id=@U", new { U = old }));
        Assert.Equal("TenantAdmin", await db.ExecuteScalarAsync<string>("SELECT role FROM employees WHERE user_id=@U", new { U = old }));
    }

    // ══ G-E13 ══

    [Fact(DisplayName = "G-E13 🔴 실물 파이프라인 — 직원 토큰(tenant_user)+등록 기기 · for-employee·my-level·linkable 200/403(본문으로 출처 구별) · P2-02 권한 0 직원 사원목록 LoginId 가림 · 대조군(약관 없으면 약관 403 · TenantOnly 를 틀면 my-level 403 · 출입증 없으면 401 Bearer)")]
    public async Task E13_Real_Pipeline()
    {
        if (!Ready("G-E13")) return;
        await using var db = await OpenAsync();
        await SeedCompanyAsync(db);
        var owner = await StaffAsync(db, "owner01", 3, parent: true);
        var s1 = await StaffAsync(db, "p1301", 1);
        var s2 = await StaffAsync(db, "p1302", 2);
        var s0 = await StaffAsync(db, "p1300", 0);
        var free = await InsertEmployeeAsync(db, "1310", "고를사원");
        var leaver = await InsertEmployeeAsync(db, "MIG-1311", "이관퇴사", resigned: true);
        var linkedUser = await InsertUserAsync(db, "lk1312", "연결됨");
        await InsertEmployeeAsync(db, "1312", "연결됨", userId: linkedUser);

        Assert.Equal(200, (await PipeAsync(db, "GET", "/api/users/my-level", s1)).Status);
        var l2 = await PipeAsync(db, "GET", "/api/users/linkable-employees", s2);
        Assert.True(l2.Status == 200, $"linkable 단계2 → {l2.Status} · {l2.Body}");
        Assert.Contains("1310", l2.Body);                       // 재직 + none 만
        Assert.DoesNotContain("MIG-1311", l2.Body);             // 퇴사(C-1) 제외
        Assert.DoesNotContain("\"1312\"", l2.Body);             // 이미 연결 제외
        AssertFilter403(await PipeAsync(db, "GET", "/api/users/linkable-employees", s1), "linkable 단계1");
        AssertFilter403(await PipeAsync(db, "POST", "/api/users/for-employee", s1, ForEmployeeJson(free, "x1310")), "for-employee 단계1");
        var fe = await PipeAsync(db, "POST", "/api/users/for-employee", s2, ForEmployeeJson(free, "ok1310"));
        Assert.True(Ok2xx(fe), $"for-employee 단계2 → {fe.Status} · {fe.Body}");
        var dup = await PipeAsync(db, "POST", "/api/users/for-employee", s2, ForEmployeeJson(free, "again1310"));
        // ⬛ [합류 전] 상태 409 만 쟀다 — 계약(§4 ④)의 code 로 사유까지 가른다
        Assert409Code(dup, "employee_has_account", "같은 사원 두 번째");
        var lv = await PipeAsync(db, "POST", "/api/users/for-employee", s2, ForEmployeeJson(leaver, "lv1311"));
        Assert409Code(lv, "employee_leaver", "MDB 퇴사자");

        // P2-02 — 권한 0 직원이 사원목록을 열어도 남의 로그인 아이디(사본)는 안 보인다 · 대표는 보인다(짝)
        var e0 = await PipeAsync(db, "GET", "/api/employees?includeResigned=true", s0);
        if (e0.Status == 200) Assert.DoesNotContain("lk1312", e0.Body);
        var eo = await PipeAsync(db, "GET", "/api/employees?includeResigned=true", owner);
        Assert.True(eo.Status == 200, $"대표 사원목록 → {eo.Status}");
        Assert.Contains("ok1310", eo.Body);                     // 사본이 실제로 실린다(가림이 「원래 없음」이 아님)

        // V5-06(§8-2) — 대표가 「새 사원과 함께」로 같은 이름의 재직·미등록 사원이 있는데 만들면 409(후보 안내) · 사원 행 불변.
        //   ⬛ [합류 전] 「다른 사람 확인」 플래그로 생성되는 쪽은 요청 칸 이름이 계약 미정 ⇒ 미작성.
        //   🟢 §연결 — 계약 ⑤ `confirmDifferentPerson` 로 다시 보내면 201 · 사원 +1(동명이인 허용) — 아래에서 잰다.
        await InsertEmployeeAsync(db, "1320", "동명이인");
        var emps = await CountAsync(db, "employees");
        var usersBefore = await CountAsync(db, "users");
        string SameJson(bool confirm) =>
            $"{{\"email\":\"same1320\",\"userName\":\"동명이인\",\"empName\":\"동명이인\",\"password\":\"{Pw}\",\"role\":\"User\",\"confirmDifferentPerson\":{(confirm ? "true" : "false")}}}";
        var same = await PipeAsync(db, "POST", "/api/users", owner, SameJson(false));
        Assert409Code(same, "same_name_employee", "같은 이름 미등록 사원 있음");
        Assert.Contains("1320", same.Body);                      // 후보(사번)를 돌려준다
        Assert.Contains("\"candidates\"", same.Body);
        Assert.Equal(emps, await CountAsync(db, "employees"));
        Assert.Equal(usersBefore, await CountAsync(db, "users"));

        // 🟢 §연결 V5-06 — 「다른 사람입니다」 확인(confirmDifferentPerson:true)으로만 새 사원과 함께 만든다
        var confirmed = await PipeAsync(db, "POST", "/api/users", owner, SameJson(true));
        Assert.True(confirmed.Status == 201, $"다른 사람 확인 뒤 → {confirmed.Status} (기대 201) · {confirmed.Body}");
        Assert.Equal(emps + 1, await CountAsync(db, "employees"));                 // 동명이인 사원 행 +1
        Assert.Equal(usersBefore + 1, await CountAsync(db, "users"));
        Assert.Equal(2L, await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM employees WHERE tenant_id=@T AND emp_name='동명이인'", new { T = _tenantA }));
        Assert.Equal("same1320", await db.ExecuteScalarAsync<string?>(
            "SELECT login_id FROM employees WHERE tenant_id=@T AND emp_name='동명이인' AND emp_no<>'1320'", new { T = _tenantA }));
        Assert.Null((await db.QuerySingleAsync<(string? u, string? l)>(
            "SELECT user_id, login_id FROM employees WHERE tenant_id=@T AND emp_no='1320'", new { T = _tenantA })).u);   // 기존 사원은 그대로 미등록
        // 대조군 — 플래그 칸을 빼면(기본 false) 409 로 되돌아간다 = 201 은 플래그가 연 것이다
        await InsertEmployeeAsync(db, "1321", "동명이인둘");
        var noFlag = await PipeAsync(db, "POST", "/api/users", owner,
            $"{{\"email\":\"same1321\",\"userName\":\"동명이인둘\",\"empName\":\"동명이인둘\",\"password\":\"{Pw}\",\"role\":\"User\"}}");
        Assert409Code(noFlag, "same_name_employee", "플래그 없는 같은 이름");

        // 🔴 대조군 ① — 약관 미동의 직원 ⇒ 약관 미들웨어 403(본문 코드로 필터 403 과 구별) = 요청이 그 미들웨어를 실제로 지난다
        var noTerms = await StaffAsync(db, "p1309", 2, terms: false);
        var nt = await PipeAsync(db, "GET", "/api/users/my-level", noTerms);
        Assert.Equal(403, nt.Status);
        Assert.Contains("forbidden_terms_consent", nt.Body);
        // 대조군 ② — 정책 하나를 틀면(TenantOnly → 관리자 전용) 같은 직원·같은 요청이 403
        AssertFilter403(await PipeAsync(db, "GET", "/api/users/my-level", s1, tenantOnlyAsAdmin: true), "대조군 TenantOnly 틀기");
        // 대조군 ③ — 출입증 없음 ⇒ 권한 단계 JwtBearer Challenge 401
        Assert.Equal(401, (await PipeAsync(db, "GET", "/api/users/my-level", null)).Status);
    }

    private sealed class E13HostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = typeof(UserController).Assembly.GetName().Name!;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Development";
    }
}
