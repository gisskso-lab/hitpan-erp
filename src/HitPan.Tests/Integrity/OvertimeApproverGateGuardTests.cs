using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(2026-08-21) 초과근무 승인 권한 게이트 — 김삼성 상무 실무 판정.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>상무님 지적</b>: <i>"HrOvertimePage 의 승인 버튼은 관리자 여부를 안 본다.
/// AbsencePage 는 <c>_isAdmin &amp;&amp; context.CanApprove</c> 로 거르는데,
/// 초과근무는 <c>context.Status == "pending"</c> 만 본다. …
/// <b>본인이 신청한 초과근무를 본인이 승인하는 버튼이 보인다</b> —
/// 이건 인사 감사에서 바로 지적당하는 자리다."</i>
/// </para>
/// <para>
/// 🔴 <b>대조가 결정적이다</b> — 같은 팀이 휴직에는 게이트를 넣고 초과근무에는 안 넣었다.
/// 설계가 아니라 <b>실수</b>다.
/// </para>
/// <para>
/// 서버는 <c>[RequirePermission("HR","update")]</c> 로 막으므로 <b>데이터가 새지는 않는다.</b>
/// 그러나 화면에 버튼이 보이고 누르면 403 이 뜬다 — 사이드바 주석이
/// <i>"못 쓸 메뉴를 보여주는 것 자체가 히트판 정신에 걸린다"</i> 고 적어둔 그 자리를 어긴 것이다.
/// </para>
/// <para>
/// ⚠️ 주석 안의 문구를 코드로 오인하지 않도록 판정 전에 주석 줄을 걸러낸다.
/// </para>
/// </remarks>
public sealed class OvertimeApproverGateGuardTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.True(dir is not null && Directory.Exists(Path.Combine(dir, "src")),
            "레포 루트를 찾아야 한다");
        return dir!;
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>주석·빈 줄을 걸러낸 실제 코드만 남긴다(거짓 경보 방지).</summary>
    private static string CodeLines(string source)
    {
        var noRazorComment = Regex.Replace(source, @"@\*.*?\*@", string.Empty, RegexOptions.Singleline);
        var noBlock = Regex.Replace(noRazorComment, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return string.Join("\n", noBlock
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l =>
            {
                var t = l.TrimStart();
                return !t.StartsWith("//") && !t.StartsWith("///") && t.Length > 0;
            }));
    }

    private static string OvertimePage() =>
        CodeLines(ReadSource("src", "HitPan.Web", "Pages", "HR", "HrOvertimePage.razor"));

    /// <summary>
    /// 🔴 초과근무 승인/반려 버튼은 <b>관리자에게만</b> 보여야 한다.
    /// </summary>
    /// <remarks>
    /// 안 막으면 <b>본인이 신청한 건을 본인이 승인</b>하는 버튼이 뜬다(인사 감사 지적 자리).
    /// </remarks>
    [Fact]
    public void 초과근무_승인버튼은_관리자에게만_보인다()
    {
        var page = OvertimePage();

        // 승인/반려를 실제로 부르는 자리가 있어야 이 시험의 전제가 선다.
        Assert.Contains("ProcessAsync", page);

        Assert.True(
            page.Contains("_isAdmin"),
            """
            HrOvertimePage 에 관리자 판정(_isAdmin)이 없다.
            → 일반 직원에게도 승인/반려 버튼이 보이고,
              본인이 신청한 초과근무를 본인이 승인하는 버튼까지 뜬다.
            휴직 화면(AbsencePage)은 _isAdmin 으로 거르고 있다 — 같은 기준을 쓴다.
            """);

        // 승인 버튼을 감싸는 조건에 관리자 판정이 실제로 걸려 있어야 한다.
        // (필드만 선언하고 조건에 안 쓰면 막은 적이 없는 것과 같다.)
        Assert.True(
            Regex.IsMatch(page, @"@if\s*\([^)]*_isAdmin[^)]*\)"),
            """
            _isAdmin 이 선언만 되어 있고 승인 버튼 조건에 쓰이지 않는다.
            → 버튼은 여전히 모두에게 보인다. 선언은 게이트가 아니다.
            """);
    }

    /// <summary>
    /// 🔴 관리자 판정에 실패하면 <b>관리자가 아닌 것으로</b> 본다(fail-closed).
    /// </summary>
    /// <remarks>
    /// 휴직 화면 주석: <i>"판정에 실패하면 관리자로 보지 않는다 —
    /// <b>못 읽었는데 열어주면 막은 적이 없는 것과 같다.</b>"</i>
    /// 같은 원칙을 초과근무에도 건다.
    /// </remarks>
    [Fact]
    public void 관리자_판정_실패시_열어주지_않는다()
    {
        var page = OvertimePage();

        if (!page.Contains("_isAdmin"))
        {
            return; // 앞 시험이 이미 잡는다.
        }

        // catch 안에서 true 로 두면 판정 실패가 곧 통과가 된다.
        Assert.False(
            Regex.IsMatch(page, @"catch[\s\S]{0,400}?_isAdmin\s*=\s*true"),
            """
            관리자 판정에 실패했을 때 _isAdmin 을 true 로 두고 있다.
            → 못 읽었는데 열어주면 막은 적이 없는 것과 같다(fail-open).
            """);
    }
}
