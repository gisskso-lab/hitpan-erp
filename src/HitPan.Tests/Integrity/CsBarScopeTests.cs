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

    // ══════════════════════════════════════════════════════════════════════
    // 🔴 20261107작 외부후보 U1 · I-1a 고침 — CSBAR-6 ~ CSBAR-9 (추가만 · 위 칸 무수정)
    //   기대값 출처 = 내부 대조 카드 §1 의 .NET 8 길 찾기 표 실측 판정을 **상수로** 적은 것
    //   (IssuePage · NotFound → null · DetailPage → 그 값). 시험 안 프레임워크 내부 반사 호출 0.
    //   CSBAR-8b 의 두 칸은 [3] 이 레포 밖 재현 프로젝트로 길 찾기 판정을 먼저 잰 값이다.
    //   🔴 화면 실측 아님 — 브라우저가 넘기는 주소 꼴 · 선렌더 주소 꼴은 미측정.
    // ══════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "CSBAR-6 🔴 U1-A 대조군 — 인코딩된 글자 주소는 길 찾기가 고정 화면(IssuePage)으로 연다 → null (길 찾기 표 기준 · 화면 실측 아님)")]
    [InlineData("admin/tenants/%69ssue")]
    [InlineData("admin/tenants/iss%75e")]
    [InlineData("admin/tenants/%49SSUE")]
    public void CSBAR_6_인코딩글자주소_전체(string uri) => Assert.Null(CsBarScope.Resolve(uri));

    [Theory(DisplayName = "CSBAR-7 🔴 U1-B 대조군 — 가운데 빈 칸은 길 찾기가 없는 화면(NotFound) → null (길 찾기 표 기준 · 화면 실측 아님)")]
    [InlineData("admin//tenants/x")]
    [InlineData("admin/tenants//x")]
    [InlineData("admin/tenants/x//")]
    [InlineData("admin//cs?tenant=a")]
    public void CSBAR_7_빈칸_전체(string uri) => Assert.Null(CsBarScope.Resolve(uri));

    [Theory(DisplayName = "CSBAR-8 정상 대조 — 길 찾기가 고객사 상세(DetailPage)로 여는 꼴은 그 값 · 이중 풀기 없음 (길 찾기 표 기준 · 화면 실측 아님)")]
    [InlineData("admin/tenants/%2569ssue", "%69ssue")]
    [InlineData("admin/tenants/a%2Fb", "a/b")]
    [InlineData("admin/tenants/%20issue", " issue")]
    public void CSBAR_8_상세주소_정상대조(string uri, string expected) => Assert.Equal(expected, CsBarScope.Resolve(uri));

    [Theory(DisplayName = "CSBAR-8b 정상 대조 — 추가 실측 2건: 끝에 인코딩 슬래시 · 틀린 인코딩은 길 찾기가 상세(DetailPage)로 연다 (길 찾기 표 기준 · 화면 실측 아님)")]
    [InlineData("admin/tenants/issue%2F", "issue/")]
    [InlineData("admin/tenants/%zz", "%zz")]
    public void CSBAR_8b_추가실측_정상대조(string uri, string expected) => Assert.Equal(expected, CsBarScope.Resolve(uri));

    /// <summary>
    /// CSBAR-9 — 판정 함수는 이름으로 찾는다(우리 제품 함수 · 프레임워크 내부 아님).
    /// 🔴 함수가 없으면(바닥 판) 「늘 서버를 부른다」 = 바닥 동작으로 잰다 → 37자 대조군 칸만 빨강이 된다.
    ///   이름이 바뀌어 못 찾아도 37자 칸이 빨강이라 이 검사는 눈을 감지 않는다.
    /// </summary>
    private static bool ExceedsByProduct(string value)
    {
        var m = typeof(CsBarScope).GetMethod("ExceedsServerTenantIdLength",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            null, new[] { typeof(string) }, null);
        if (m is null) return false;
        return (bool)m.Invoke(null, new object?[] { value })!;
    }

    [Theory(DisplayName = "CSBAR-9 🔴 I-1a 서버를 부르지 않는 길이 — Trim 뒤 36자 보냄 · 37자 안 보냄(대조군) · 앞뒤 공백 붙은 36자 보냄 (서버 Trim 뒤 Length > 36 과 같게)")]
    [InlineData(36, 0, false)]
    [InlineData(37, 0, true)]
    [InlineData(36, 1, false)]
    public void CSBAR_9_서버상한_관문(int coreLength, int padEachSide, bool expectedExceeds)
    {
        var pad = new string(' ', padEachSide);
        var value = pad + new string('a', coreLength) + pad;
        Assert.Equal(coreLength + 2 * padEachSide, value.Length);   // 대조: 공백 칸은 날 길이로는 37자 이상이다
        Assert.Equal(expectedExceeds, ExceedsByProduct(value));
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
