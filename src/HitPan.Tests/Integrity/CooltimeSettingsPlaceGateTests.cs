using System.Reflection;
using HitPan.API.Controllers;
using HitPan.Application.DTOs.Settings;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🆕 2026-11-28 쿨타임1b — 자동 로그아웃 설정 자리 옮김 <b>비DB 게이트 G-P1 ~ G-P5</b>.
/// </summary>
/// <remarks>
/// 근거: 설계 <c>docs/설계/erp/쿨타임_설정자리옮김_설계_20261128.md</c> §5 · 작업지시서 <c>docs/운영기록/20261128작_쿨타임1b_설정자리옮김_작업지시서.md</c> ④.
/// <list type="bullet">
/// <item><b>G-P1</b> MVC 실제 탐색 — 좁은 주소 둘 <b>있음</b> + 루트 GET/PUT <b>없음</b>(같은 판에서 함께).</item>
/// <item><b>G-P2</b> 반사 — 두 액션 <c>TenantAdminOnly</c> · 두 DTO 에 Tenant/User 칸 0(#2).</item>
/// <item><b>G-P3</b> 화면 — <c>@page "/settings/auto-logout"</c> 가 Web 전체에 정확히 1 · 부모 역할 <c>[Authorize]</c>.</item>
/// <item><b>G-P4</b> 메뉴 — Sidebar 줄이 <c>@if (_isTenantAdmin)</c> 블록 안 · 닫힘 규칙에 안 걸림 · More 줄 1 + 부모 판정 거름([3-V] 보안상무 🟡).</item>
/// <item><b>G-P5</b> 저장 배선 — <c>SaveCooltimeOnlyAsync</c> 본문 = Resolve + SaveCooltime · 루트 <c>SaveAsync(</c> 0.</item>
/// </list>
/// 변이 대조(빼면 빨강)는 개발명세서 <c>docs/개발/erp/20261128_쿨타임1b_설정자리옮김_개발명세서.md</c> 변이 표.
/// </remarks>
public sealed class CooltimeSettingsPlaceGateTests
{
    private const string Href = "/settings/auto-logout";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다.");
    }

    /// <summary>주석 줄(<c>//</c> · <c>///</c>)을 뺀 살아 있는 줄.</summary>
    private static string Live(string path) => string.Join('\n', File.ReadAllText(path).Replace("\r\n", "\n").Split('\n')
        .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    /// <summary>Razor 주석(<c>@* … *@</c>)을 뺀 글.</summary>
    private static string StripRazorComments(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"@\*.*?\*@", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    // ─────────────────────────────────────────────────────────────
    //  G-P1 — MVC 실제 탐색
    // ─────────────────────────────────────────────────────────────

    private static HashSet<string> DiscoverRoutes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(SettingsController).Assembly);
        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IActionDescriptorCollectionProvider>();

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ad in provider.ActionDescriptors.Items)
        {
            var template = ad.AttributeRouteInfo?.Template;
            if (string.IsNullOrEmpty(template)) continue;
            var methods = (ad.ActionConstraints ?? new List<IActionConstraintMetadata>())
                .OfType<HttpMethodActionConstraint>()
                .SelectMany(c => c.HttpMethods);
            foreach (var m in methods) set.Add($"{m.ToUpperInvariant()} {template.Trim('/')}");
        }
        return set;
    }

    [Fact(DisplayName = "G-P1 MVC 탐색 — GET/PUT api/settings/cooltime 있음 + 루트 GET/PUT api/settings 없음(같은 판)")]
    public void G_P1_좁은_주소는_열리고_루트는_닫힘()
    {
        var found = DiscoverRoutes();
        Assert.True(found.Count >= 100, $"G-P1 MVC 가 찾은 액션이 {found.Count} 개뿐이다 — 탐색이 안 돈다.");
        Assert.Contains("GET api/settings/cooltime", found);
        Assert.Contains("PUT api/settings/cooltime", found);
        Assert.DoesNotContain("GET api/settings", found);
        Assert.DoesNotContain("PUT api/settings", found);
    }

    // ─────────────────────────────────────────────────────────────
    //  G-P2 — 권한 · tenant 칸 반사
    // ─────────────────────────────────────────────────────────────

    [Theory(DisplayName = "G-P2 반사 — 좁은 액션 둘 = [Authorize(Policy=\"TenantAdminOnly\")]")]
    [InlineData(nameof(SettingsController.GetCooltime))]
    [InlineData(nameof(SettingsController.SaveCooltime))]
    public void G_P2_부모계정만(string action)
    {
        var m = typeof(SettingsController).GetMethod(action, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(m);
        var policies = m!.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).ToList();
        Assert.Contains("TenantAdminOnly", policies);
    }

    [Theory(DisplayName = "G-P2 반사 — 두 DTO 에 이름에 Tenant/User 든 속성 0(#2 · tenant = JWT 만)")]
    [InlineData(typeof(CooltimeSettingsDto))]
    [InlineData(typeof(UpdateCooltimeSettingsDto))]
    public void G_P2_DTO에_tenant칸_없음(Type dto)
    {
        var bad = dto.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.Contains("Tenant", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("User", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name).ToList();
        Assert.True(bad.Count == 0, $"G-P2 {dto.Name} 에 tenant/user 칸: {string.Join(",", bad)}");
        Assert.Equal(3, dto.GetProperties().Length);   // 세 칸만 오간다
    }

    // ─────────────────────────────────────────────────────────────
    //  G-P3 — 화면
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "G-P3 화면 — @page \"/settings/auto-logout\" Web 전체에 정확히 1 · 부모 역할 [Authorize] · 좁은 API 만 부름")]
    public void G_P3_화면_한_자리()
    {
        var webRoot = Path.Combine(RepoRoot(), "src", "HitPan.Web");
        var pageLine = "@page \"" + Href + "\"";
        var hits = Directory.EnumerateFiles(webRoot, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Where(f => StripRazorComments(File.ReadAllText(f)).Contains(pageLine, StringComparison.Ordinal))
            .ToList();
        Assert.Single(hits);

        var page = StripRazorComments(File.ReadAllText(hits[0]));
        Assert.Contains("@attribute [Authorize(Roles = \"system_admin,tenant_admin,TenantAdmin\")]", page);
        Assert.Contains("SettingsSvc.SaveCooltimeAsync(", page);
        Assert.Contains("SettingsSvc.GetCooltimeAsync(", page);
        Assert.DoesNotContain("SettingsSvc.SaveAsync(", page);
        Assert.DoesNotContain("SettingsSvc.SaveWithMessageAsync(", page);

        var svc = Live(Path.Combine(webRoot, "Services", "SettingsService.cs"));
        Assert.Contains("\"api/settings/cooltime\"", svc);
    }

    // ─────────────────────────────────────────────────────────────
    //  G-P4 — 메뉴(부모만)
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "G-P4 메뉴 — Sidebar 줄 1 이 @if (_isTenantAdmin) 블록 안 · 닫힘 규칙에 안 걸림")]
    public void G_P4_사이드바는_부모블록_안()
    {
        var sidebar = StripRazorComments(File.ReadAllText(Path.Combine(RepoRoot(), "src", "HitPan.Web", "Layout", "Sidebar.razor")));
        var link = "Href=\"" + Href + "\"";
        Assert.Equal(1, Count(sidebar, link));

        // 설정관리 무리 = 첫 「@if (_isTenantAdmin)」 부터 짝 「else if (_usersLevel >= 1)」 앞까지(직원 갈래는 그 뒤)
        var start = sidebar.IndexOf("@if (_isTenantAdmin)", StringComparison.Ordinal);
        Assert.True(start >= 0, "G-P4 Sidebar 에 @if (_isTenantAdmin) 이 없다.");
        var end = sidebar.IndexOf("else if (_usersLevel >= 1)", start, StringComparison.Ordinal);
        Assert.True(end > start, "G-P4 설정관리 부모 블록의 끝(직원 갈래)을 못 찾았다.");
        var at = sidebar.IndexOf(link, StringComparison.Ordinal);
        Assert.True(at > start && at < end, "G-P4 「자동 로그아웃 설정」 줄이 부모(_isTenantAdmin) 블록 밖에 있다.");

        // 그 줄이 다른 조건(닫힘 규칙 등)에 갇혀 있지 않다 — 블록 머리와 줄 사이에 새 @if 0
        var between = sidebar.Substring(start + "@if (_isTenantAdmin)".Length, at - start - "@if (_isTenantAdmin)".Length);
        var openIfs = Count(between, "@if (");
        var closes = Count(between, "        }");
        Assert.True(openIfs <= closes, "G-P4 「자동 로그아웃 설정」 줄이 다른 @if 안에 들어 있다.");

        Assert.True(BetaClosedRoutes.IsOpen(Href), "G-P4 /settings/auto-logout 이 닫힘 규칙(BetaClosedRoutes)에 걸린다.");
    }

    [Fact(DisplayName = "G-P4 메뉴 — More 줄 1 · 부모 판정(AccountTypeHelper.IsTenantAdmin)으로 거름 · 판정 실패 = 감춤([3-V] 🟡)")]
    public void G_P4_더보기도_부모만()
    {
        var more = Live(Path.Combine(RepoRoot(), "src", "HitPan.Web", "Pages", "More.razor"));
        var moreNoRazor = StripRazorComments(more);
        Assert.Equal(1, Count(moreNoRazor, "Icons.Material.Filled.TimerOff,      \"" + Href + "\")"));
        Assert.Contains("private const string AutoLogoutHref = \"" + Href + "\";", moreNoRazor);
        Assert.Contains("if (item.Href == AutoLogoutHref && !_isTenantAdmin)", moreNoRazor);
        Assert.Contains("_isTenantAdmin = AccountTypeHelper.IsTenantAdmin(auth.User);", moreNoRazor);
        // 거름이 그리기 반복 안(닫힘 거름 뒤 · 단추 앞)에 있다
        var loop = moreNoRazor.IndexOf("@foreach (var item in group.Items)", StringComparison.Ordinal);
        var guard = moreNoRazor.IndexOf("if (item.Href == AutoLogoutHref && !_isTenantAdmin)", StringComparison.Ordinal);
        var button = moreNoRazor.IndexOf("<button type=\"button\" class=\"hp-menu-item\"", loop, StringComparison.Ordinal);
        Assert.True(loop >= 0 && guard > loop && guard < button, "G-P4 More 부모 거름이 그리기 반복 안에 없다.");
        // 판정 실패 = 감춤
        var catchAt = moreNoRazor.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        Assert.True(catchAt >= 0 && moreNoRazor.IndexOf("_isTenantAdmin = false;", catchAt, StringComparison.Ordinal) > catchAt,
            "G-P4 More 부모 판정 실패 때 감추지 않는다.");
    }

    // ─────────────────────────────────────────────────────────────
    //  G-P5 — 저장 배선
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "G-P5 SaveCooltimeOnlyAsync 본문 = ResolveCooltimeAsync + SaveCooltimeAsync · 루트 SaveAsync( 0")]
    public void G_P5_좁은_저장은_루트를_안_탄다()
    {
        var svc = Live(Path.Combine(RepoRoot(), "src", "HitPan.Application", "Services", "SettingsService.cs"));
        const string sig = "public async Task SaveCooltimeOnlyAsync(";
        var start = svc.IndexOf(sig, StringComparison.Ordinal);
        Assert.True(start >= 0, "G-P5 SaveCooltimeOnlyAsync 를 못 찾았다.");
        var end = svc.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var body = svc.Substring(start, end - start);
        Assert.Contains("ResolveCooltimeAsync(", body);
        Assert.Contains("SaveCooltimeAsync(tenantId, cooltime, ct)", body);
        Assert.DoesNotContain("SaveAsync(", body);
    }
}
