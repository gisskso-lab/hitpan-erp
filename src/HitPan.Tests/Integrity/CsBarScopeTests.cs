using System.Text.RegularExpressions;
using HitPan.Backoffice.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261107작 갈래 B §7-2 — 아래 CS 표시줄의 주소 해석기(<see cref="CsBarScope"/>) 순수 시험 · DB 0 · CI build 잡.
///
/// <para>해석기 파일은 HitPan.Tests.csproj 에 **링크**로 들어온다(제품이 쓰는 그 코드를 불러서 잰다).</para>
/// <para>🔴 실화면 꼴이 기본: 표시줄은 <c>Nav.ToBaseRelativePath(Nav.Uri)</c> 를 넘기고 그 값은
/// **앞 슬래시가 없다**(<c>admin/tenants/x</c> · 3V-B1). 그래서 칸마다 슬래시 없는 꼴을 먼저 잰다.</para>
/// <para>가짜 값만 쓴다(실제 고객사 값 0 · C4).</para>
/// </summary>
public sealed class CsBarScopeTests
{
    private const string FakeId = "00000000-0000-4000-8000-0000000000aa";

    [Theory(DisplayName = "CSBAR-1 고객 상세 주소 → 그 고객사값 (슬래시 없는 실화면 꼴 · 있는 꼴 · 질의·조각·끝 슬래시)")]
    [InlineData("admin/tenants/" + FakeId)]
    [InlineData("/admin/tenants/" + FakeId)]
    [InlineData("admin/tenants/" + FakeId + "?tab=1")]
    [InlineData("admin/tenants/" + FakeId + "#top")]
    [InlineData("admin/tenants/" + FakeId + "/")]
    [InlineData("ADMIN/Tenants/" + FakeId)]
    public void CSBAR_1_고객상세(string uri) => Assert.Equal(FakeId, CsBarScope.Resolve(uri));

    [Theory(DisplayName = "CSBAR-2 전체로 떨어지는 주소 → null")]
    [InlineData("admin/tenants/issue")]
    [InlineData("/admin/tenants/issue")]
    [InlineData("admin/tenants/ISSUE")]
    [InlineData("admin/tenants")]
    [InlineData("/admin/tenants")]
    [InlineData("admin/tenants/" + FakeId + "/x")]
    [InlineData("admin/cs?stage=open")]
    [InlineData("admin/cs?tenant=")]
    [InlineData("admin/cs?tenant=%20")]
    [InlineData("admin/cs/notice?tenant=" + FakeId)]
    [InlineData("/admin/cs/notice?tenant=" + FakeId)]
    [InlineData("admin/resellers/" + FakeId)]
    [InlineData("admin/dashboard")]
    [InlineData("")]
    [InlineData(null)]
    public void CSBAR_2_전체(string? uri) => Assert.Null(CsBarScope.Resolve(uri));

    [Theory(DisplayName = "CSBAR-3 걸러진 고객A/S창구 주소 → 그 고객사값")]
    [InlineData("admin/cs?tenant=" + FakeId + "&stage=open")]
    [InlineData("/admin/cs?tenant=" + FakeId + "&stage=open")]
    [InlineData("admin/cs?stage=done&tenant=" + FakeId)]
    [InlineData("Admin/CS?tenant=" + FakeId)]
    [InlineData("admin/cs/?tenant=" + FakeId)]
    public void CSBAR_3_걸러진창구(string uri) => Assert.Equal(FakeId, CsBarScope.Resolve(uri));

    [Fact(DisplayName = "CSBAR-4 🔴 C3 감싼 값 왕복 — EscapeDataString 으로 감싼 값을 해석기가 풀어 원래 값으로 돌려준다")]
    public void CSBAR_4_감싼값_왕복()
    {
        const string odd = "가짜 값/&=?#+%";   // 주소 글자와 부딪히는 글자를 일부러 섞은 가짜 값
        var wrapped = Uri.EscapeDataString(odd);
        Assert.NotEqual(odd, wrapped);   // 대조: 감싸기가 실제로 글자를 바꿨다

        Assert.Equal(odd, CsBarScope.Resolve($"admin/cs?tenant={wrapped}&stage=open"));
        // 경로 칸은 「/」 를 품을 수 없으므로 「/」 없는 가짜 값으로 잰다
        const string oddPath = "가짜 값+&=";
        Assert.Equal(oddPath, CsBarScope.Resolve($"admin/tenants/{Uri.EscapeDataString(oddPath)}"));
    }

    [Fact(DisplayName = "CSBAR-5 🔴 글자 대조(화면 동작 증거 아님) — /admin/tenants/ 아래 글자 @page 전부가 제외 목록에 있다 (+대조군)")]
    public void CSBAR_5_글자주소_전수대조_글자대조()
    {
        var root = FindRepoRoot();
        var dir = Path.Combine(root, "src", "HitPan.Backoffice");
        Assert.True(Directory.Exists(dir), $"백오피스 폴더를 찾아야 한다: {dir}");

        // @page "/admin/tenants/{글자}" — 중괄호 매개변수({...})로 시작하지 않는 한 칸짜리만
        var rx = new Regex("@page\\s+\"/admin/tenants/([^\"/{}]+)\"", RegexOptions.IgnoreCase);
        var literals = Directory.EnumerateFiles(dir, "*.razor", SearchOption.AllDirectories)
            .SelectMany(f => rx.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.NotEmpty(literals);   // 대조: 지금 적어도 「issue」 1개는 찾혀야 한다(못 찾으면 검사가 눈을 감은 것)
        var missing = literals.Where(l => !CsBarScope.LiteralTenantSegments.Contains(l, StringComparer.OrdinalIgnoreCase)).ToList();
        Assert.True(missing.Count == 0,
            $"/admin/tenants/ 아래 새 글자 주소가 해석기 제외 목록에 없다: {string.Join(", ", missing)} — CsBarScope.LiteralTenantSegments 에 더하라");

        // 🔴 음성 대조군 — 제외 목록이 비면 그 글자 주소를 고객사값으로 읽는다(= 이 검사가 잡아야 할 사고 모양)
        var empty = Array.Empty<string>();
        foreach (var l in literals)
            Assert.Equal(l, CsBarScope.Resolve($"admin/tenants/{l}", empty));
        // 가짜 글자 주소 목록을 넣으면 빠진 것이 나온다(검사 식이 실제로 문다)
        var fakePages = new[] { "issue", "gate-fake-literal" };
        Assert.Contains("gate-fake-literal",
            fakePages.Where(l => !CsBarScope.LiteralTenantSegments.Contains(l, StringComparer.OrdinalIgnoreCase)));
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
            dir = Directory.GetParent(dir)?.FullName;
        Assert.True(dir is not null, "레포 루트를 찾아야 한다");
        return dir!;
    }
}
