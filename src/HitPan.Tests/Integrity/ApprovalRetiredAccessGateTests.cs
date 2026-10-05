using System.Security.Claims;
using System.Text.RegularExpressions;
using HitPan.API.Controllers;
using HitPan.API.Middleware;
using HitPan.Application.Common;
using HitPan.Application.DTOs.Device;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 2026-10-05 작4 — 슬롯 폐기 · 「접속기기 확인」 게이트 (설계 20261005_설계_접속기기확인_슬롯폐기 §8 · 9/28 §5).
/// </summary>
/// <remarks>
/// <para>이 파일은 <b>DB 없이 도는 몫</b>(동작 · 글자 보조)이다. 각 게이트의 음성 대조군은 개발명세서 §대조군 표에 직접 돌린 결과로 남긴다.</para>
/// <para>⚠️ DB 몫(G-AR1ⓐⓒ · AR2~AR5 · AR9~AR11 · AR13 WebApplicationFactory · AR14)은 아직 이 파일에 없다 — 개발명세서 「남은 일」.
/// 로컬 초록을 그 게이트들의 통과로 적지 않는다.</para>
/// <para>🔴 출하 <c>appsettings.json</c> 에 <c>"DeviceApproval": {"Enabled": true}</c> 가 남아 있다(#21 무접촉) ⇒ 설정을 <b>true 로 넣고</b> 잰다.</para>
/// </remarks>
public sealed class ApprovalRetiredAccessGateTests
{
    private static IConfiguration ShippedConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["DeviceApproval:Enabled"] = "true" })
        .Build();

    // ── G-AR1ⓑ (DB 불필요 몫) — 인증키 없는 직원 요청이 업무 API 에서 403 을 안 받는다 ──
    [Fact(DisplayName = "G-AR1b 설정이 true 여도 인증키 없는 직원 요청이 미들웨어를 통과한다(403 없음)")]
    public async Task GAR1b_미들웨어는_설정을_안읽고_늘_통과한다()
    {
        var nextCalled = false;
        var mw = new DeviceAuthMiddleware(_ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<DeviceAuthMiddleware>.Instance, ShippedConfig());

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/sales/orders";
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("account_type", "tenant_user") }, "test"));
        ctx.Items["TenantId"] = "t-ar1";

        // db·서비스는 쓰이면 안 된다 — 통과 갈래(:83-87)는 아무것도 안 읽는다.
        await mw.InvokeAsync(ctx, null!, Mock.Of<ITenantDeviceService>(MockBehavior.Strict));

        Assert.True(nextCalled, "미들웨어가 다음 단계로 넘기지 않았다 — 인증키 없는 직원이 업무 API 에서 막힌다(9/28 §2 ④).");
        Assert.NotEqual(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
    }

    [Fact(DisplayName = "G-AR1b+ 승인 폐기 값은 하나 · false · static readonly(const 아님)")]
    public void GAR1b_승인폐기값은_하나다()
    {
        Assert.False(DeviceApprovalRetirement.ApprovalEnabled);
        var f = typeof(DeviceApprovalRetirement).GetField(nameof(DeviceApprovalRetirement.ApprovalEnabled))!;
        Assert.True(f.IsInitOnly && !f.IsLiteral, "const 로 바꾸면 그 뒤 갈래가 CS0162 경고(#19)가 된다 — static readonly 여야 한다.");
    }

    // ── G-AR7 — 직원은 못 본다 ──
    // ⬛ [봉합1 전] private static AccessStatusController Controller(string accountType, Mock<ITenantDeviceService> svc)
    //   new AccessStatusController(svc.Object, logger) · 클레임 account_type 하나.
    //   🔴 10/5 봉합1 P2-06 — 대표 판정이 DB is_parent 로 가서 DI 생성자(IUserService)로 만든다. 기대값(직원 403 · 대표 200)은 그대로.
    private static AccessStatusController Controller(string accountType, Mock<ITenantDeviceService> svc, bool isParent = true)
    {
        var users = new Mock<HitPan.Application.Interfaces.IUserService>();
        users.Setup(u => u.GetAsync("u-ar7", "t-ar7", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new HitPan.Application.DTOs.User.UserListDto { UserId = "u-ar7", IsParent = isParent });
        var c = new AccessStatusController(svc.Object, users.Object, NullLogger<AccessStatusController>.Instance);
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("account_type", accountType), new Claim("user_id", "u-ar7")
            }, "test"))
        };
        http.Items["TenantId"] = "t-ar7";
        c.ControllerContext = new ControllerContext { HttpContext = http };
        return c;
    }

    [Fact(DisplayName = "G-AR7 직원 계정은 두 조회 API 에서 403 · 대표는 200 · tenant 는 Items 에서만")]
    public async Task GAR7_직원은_못본다()
    {
        var svc = new Mock<ITenantDeviceService>();
        svc.Setup(s => s.GetAccessStatusAsync("t-ar7", It.IsAny<CancellationToken>())).ReturnsAsync(new AccessStatusDto());
        svc.Setup(s => s.GetLoginConflictAlertsAsync("t-ar7", 30, It.IsAny<CancellationToken>())).ReturnsAsync(new List<LoginConflictAlertDto>());

        var staff = Controller("tenant_user", svc);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await staff.GetCurrent(default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await staff.GetAlerts(default)).StatusCode);

        var owner = Controller("tenant_admin", svc);
        Assert.IsType<OkObjectResult>(await owner.GetCurrent(default));
        Assert.IsType<OkObjectResult>(await owner.GetAlerts(default));

        // 직원 호출에서 서비스가 한 번도 불리지 않았다 — 막는 자리가 조회 앞이다.
        svc.Verify(s => s.GetAccessStatusAsync("t-ar7", It.IsAny<CancellationToken>()), Times.Once);
        svc.Verify(s => s.GetLoginConflictAlertsAsync("t-ar7", 30, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── G-AR7p — 10/5 봉합1 P2-06 · 관리자 권한 직원(account_type=tenant_admin · is_parent=0)도 403 ──
    [Fact(DisplayName = "G-AR7p 🔴 P2-06 — 관리자 권한 직원(대표 아님)은 두 조회 403 · 대조군 대표 200 · 옛 생성자는 닫힌 쪽 403")]
    public async Task GAR7p_대표아닌관리자는_못본다()
    {
        var svc = new Mock<ITenantDeviceService>();
        svc.Setup(s => s.GetAccessStatusAsync("t-ar7", It.IsAny<CancellationToken>())).ReturnsAsync(new AccessStatusDto());
        svc.Setup(s => s.GetLoginConflictAlertsAsync("t-ar7", 30, It.IsAny<CancellationToken>())).ReturnsAsync(new List<LoginConflictAlertDto>());

        var admin = Controller("tenant_admin", svc, isParent: false);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await admin.GetCurrent(default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await admin.GetAlerts(default)).StatusCode);
        svc.Verify(s => s.GetAccessStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        var owner = Controller("tenant_admin", svc, isParent: true);   // 대조군
        Assert.IsType<OkObjectResult>(await owner.GetCurrent(default));

        var legacy = new AccessStatusController(svc.Object, NullLogger<AccessStatusController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("account_type", "tenant_admin"), new Claim("user_id", "u-ar7") }, "test")),
                Items = { ["TenantId"] = "t-ar7" }
            } }
        };
        Assert.Equal(403, Assert.IsType<ObjectResult>(await legacy.GetCurrent(default)).StatusCode);
    }

    // ── G-AR6 (DB 불필요 몫) — 경고는 고객용만 · 문장에 UTC·원문 없음 ──
    [Fact(DisplayName = "G-AR6a 고객용 경고 유형은 둘뿐 · 기술 경고(session_insert_failed) 없음")]
    public void GAR6a_고객용유형은_둘뿐()
    {
        Assert.Equal(new[] { "pc_login_blocked", "pc_session_forced_out" }, TenantDeviceService.CustomerAlertTypes);
    }

    [Theory(DisplayName = "G-AR6b 경고 문장은 서버가 고객 언어로 만든다 — UTC·개발 낱말 0")]
    [InlineData("pc_login_blocked", "이미 쓰는 컴퓨터가 있어 막았습니다")]
    [InlineData("pc_session_forced_out", "쓰던 컴퓨터의 접속이 끝났습니다")]
    public void GAR6b_문장(string kind, string expected)
    {
        var msg = TenantDeviceService.DescribeAlert(kind, "홍길동", "hong");
        Assert.StartsWith("홍길동(hong) — ", msg);
        Assert.Contains(expected, msg);
        foreach (var banned in new[] { "UTC", "세션", "슬롯", "디바이스", "승인 대기", "인증키" })
            Assert.DoesNotContain(banned, msg);
    }

    [Theory(DisplayName = "G-AR5b 브라우저 표시 이름 — 표시용 · 못 뽑으면 알 수 없음")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0 Safari/537.36 Edg/129.0", "엣지 · 윈도우")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0 Safari/537.36", "크롬 · 윈도우")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; SM-S921N) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/25.0 Chrome/121.0 Mobile Safari/537.36", "삼성 인터넷 · 안드로이드")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1", "사파리 · 아이폰")]
    [InlineData("", "알 수 없음")]
    [InlineData(null, "알 수 없음")]
    public void GAR5b_브라우저이름(string? ua, string expected)
        => Assert.Equal(expected, TenantDeviceService.DescribeBrowser(ua));

    // ── 글자 보조 (🔴 실동작 증거 아님 — 사장님 PC 확인) ──
    private static string WebRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "src", "HitPan.Web"))) d = d.Parent;
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "src", "HitPan.Web");
    }

    /// <summary>razor 주석(@* *@)과 // 줄을 뺀 글자 — 주석에 남은 옛 줄은 세지 않는다.</summary>
    private static string CodeOnly(string text)
    {
        var noRazor = Regex.Replace(text, @"@\*.*?\*@", "", RegexOptions.Singleline);
        return string.Join("\n", noRazor.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
    }

    [Fact(DisplayName = "G-AR8 (보조·글자) 관문이 화면에 없다 — MainLayout 렌더 0 · Login 대기 분기 0")]
    public void GAR8_관문이_화면에_없다()
    {
        var web = WebRoot();
        var layout = CodeOnly(File.ReadAllText(Path.Combine(web, "Layout", "MainLayout.razor")));
        Assert.DoesNotContain("<DeviceAuthGate", layout);
        var login = CodeOnly(File.ReadAllText(Path.Combine(web, "Pages", "Login.razor")));
        Assert.DoesNotContain("MarkAwaitingApproval(", login);
    }

    [Fact(DisplayName = "G-AR12 (보조·글자) 화면이 옛 쓰기 API 를 안 부른다 · KPI 0 · 메뉴 「접속기기 확인」")]
    public void GAR12_화면이_옛API를_안부른다()
    {
        var web = WebRoot();
        // ⚠️ DeviceAuthGate.razor 는 렌더 0(G-AR8)인 채 파일만 남는다 — 묶음 C(다음 판)에서 정리. 여기서는 그 파일만 뺀다.
        var files = Directory.EnumerateFiles(web, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".razor") || f.EndsWith(".cs"))
                        && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                        && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                        && !f.EndsWith("DeviceAuthGate.razor"));
        var banned = new[] { "api/devices/quota", "api/devices/approve", "api/devices/reject", "api/devices/reissue-key", "api/devices/revoke", "api/devices/mobile-token" };
        foreach (var f in files)
        {
            var code = CodeOnly(File.ReadAllText(f));
            foreach (var b in banned)
                Assert.False(code.Contains(b), $"{Path.GetFileName(f)} 가 옛 쓰기 API `{b}` 를 부른다 — 「조회만」 화면이 깨진다.");
            if (!f.EndsWith("DeviceAuthGate.razor"))
                Assert.False(code.Contains("<DeviceAuthGate"), $"{Path.GetFileName(f)} 가 관문을 그린다.");
        }

        var page = CodeOnly(File.ReadAllText(Path.Combine(web, "Pages", "Settings", "DeviceManagePage.razor")));
        Assert.DoesNotContain("KpiCard", page);
        Assert.Contains("PageTitle>접속기기 확인", page);
        Assert.Contains("Title=\"접속기기 확인\"", page);
        foreach (var w in new[] { "슬롯", "디바이스", "세션", "승인 대기", "인증키" })
            Assert.DoesNotContain(w, page);

        Assert.Contains(">접속기기 확인</MudNavLink>", CodeOnly(File.ReadAllText(Path.Combine(web, "Layout", "Sidebar.razor"))));
        Assert.Contains("new(\"접속기기 확인\"", CodeOnly(File.ReadAllText(Path.Combine(web, "Pages", "More.razor"))));

        // P2 — QR 안내 화면에 슬롯·한도 문구가 **보이지 않는다**(옛 화면은 늘 false 블록 안).
        var qr = File.ReadAllText(Path.Combine(web, "Pages", "Mobile", "MobileDeviceRegisterPage.razor"));
        Assert.Contains("이제 등록 없이 휴대폰에서 로그인하시면 됩니다.", qr);
        Assert.Contains("private static readonly bool _showRetiredQrScreen = false;", qr);
    }
}
