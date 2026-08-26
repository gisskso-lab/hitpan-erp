using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 20260826작7 게이트 — <b>급여 화면 두 개는 서로 다른 일을 한다</b>.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 사장님(2026-08-26): <i>"회계관리에 급여관리 메뉴를 만들어서 대표계정과 권한자만 접근하여
/// 급여명세서 생성, 대표에게 결재, 메일발송을 하고, 그룹웨어의 급여메뉴에선 계정로그인자
/// 개인의 급여명세서 확인·조회 정도 기능만"</i>
/// </para>
/// <para>
/// 🔴 <b>이 게이트가 생긴 이유</b> — 20260826작6 이 회계 화면을 만들지 않고
/// <b>그룹웨어 직원 화면을 개조</b>했다. 경리가 쓸 발송 기능이 직원 화면에 올라갔고,
/// 직원이 자기 급여를 보러 들어오는 진입 로직(<c>OnInitializedAsync</c> 한 줄)까지
/// 주소창 파라미터를 읽는 코드로 바뀌었다.
/// </para>
/// <para>
/// ⚠️ <b>왜 빌드·시험이 못 잡았나</b> — 작6 의 게이트는 <b>새로 만든 기능이 있는지</b>만 봤다.
/// 그 기능이 <b>어느 화면에 붙었는지</b>는 아무도 안 봤다. 그래서 전부 초록불이었다.
/// ⇒ 이 게이트는 <b>붙은 자리</b>를 본다.
/// </para>
/// </remarks>
public sealed class PayrollScreenSeparationGateTests
{
    /// <summary>그룹웨어 — 직원이 <b>본인 급여만</b> 보는 화면.</summary>
    private static string StaffPage() =>
        ReadSource("src", "HitPan.Web", "Pages", "HR", "PayrollPage.razor");

    /// <summary>회계 — 경리가 명세서를 만들고 결재·발송하는 화면.</summary>
    private static string FinancePage() =>
        ReadSource("src", "HitPan.Web", "Pages", "Finance", "PayrollManagePage.razor");

    // ══════════════════════════════════════════════════════════════════
    //  ① 두 화면은 각자 제자리에 있다
    // ══════════════════════════════════════════════════════════════════

    /// <summary>화면이 둘 다 있고, 라우트가 갈려 있다.</summary>
    [Fact]
    public void 급여화면은_그룹웨어와_회계에_각각_있다()
    {
        Assert.Contains("@page \"/hr/payroll\"", StaffPage());
        Assert.Contains("@page \"/accounting/payroll\"", FinancePage());
    }

    /// <summary>사이드바 진입점도 갈려 있다 — 이름까지.</summary>
    /// <remarks>🔴 사장님: 그룹웨어는 <b>「급여 조회」</b> 다. <b>「급여 관리」</b> 가 아니다.</remarks>
    [Fact]
    public void 사이드바_두_진입점이_각각_있다()
    {
        var sidebar = CodeLines(ReadSource("src", "HitPan.Web", "Layout", "Sidebar.razor"));

        Assert.Matches(new Regex(@"Href=""/hr/payroll""[^>]*>\s*급여\s*조회\s*<"), sidebar);
        Assert.Matches(new Regex(@"Href=""/accounting/payroll""[^>]*>\s*급여\s*관리\s*<"), sidebar);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ② 🔴 직원 화면에 경리 기능이 없다 — 작6 이 저지른 자리
    // ══════════════════════════════════════════════════════════════════

    /// <summary>🔴 <b>발송은 경리 일이다.</b> 직원 화면에 발송이 붙으면 FAIL.</summary>
    /// <remarks>
    /// ⚠️ 낱말 하나로 검사하지 않는다 — 발송은 <b>단추·다이얼로그·서비스 호출</b> 세 모양으로 붙는다.
    /// 하나만 막으면 나머지 모양으로 다시 올라온다.
    /// </remarks>
    [Fact]
    public void 직원화면에_메일발송이_없다()
    {
        var staff = CodeLines(StaffPage());

        Assert.DoesNotContain("명세서 메일발송", staff);
        Assert.DoesNotContain("OpenSendAsync", staff);
        Assert.DoesNotContain("SendPayslipMail", staff);
        Assert.DoesNotContain("_sendPreview", staff);
        Assert.DoesNotContain("_sendResult", staff);
        Assert.DoesNotContain("send-mail", staff);
    }

    /// <summary>🔴 직원 화면은 <b>남의 명세서 명단</b>을 그리지 않는다.</summary>
    /// <remarks>
    /// 발송 대상 명단표에는 <b>전 직원의 이름·부서·수신주소</b>가 뜬다.
    /// 그 표가 직원 화면에 있으면 <b>남의 급여 수신처가 직원에게 보인다.</b>
    /// </remarks>
    [Fact]
    public void 직원화면에_발송대상_명단이_없다()
    {
        var staff = CodeLines(StaffPage());

        Assert.DoesNotContain("RecipientEmail", staff);
        Assert.DoesNotContain("SendableCount", staff);
        Assert.DoesNotContain("BlockedCount", staff);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ③ 🔴 직원이 들어오는 길을 건드리지 않는다
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 직원 화면 진입은 <b>바로 목록을 부르는 것</b>이다.
    /// 주소창 파라미터를 읽는 코드가 앞에 끼면 FAIL.
    /// </summary>
    /// <remarks>
    /// ⚠️ 작6 이 이 자리를 갈아엎었다. 직원이 자기 급여를 보러 들어오는 길에
    /// <b>API 호출 하나가 앞에 끼어들었다</b> — 그게 느리거나 실패하면 진입이 그만큼 늦어진다.
    /// 직원은 결재 팝업에서 넘어오지 않는다. 읽을 이유가 없는 값이다.
    /// </remarks>
    [Fact]
    public void 직원화면_진입에_주소창_파라미터가_없다()
    {
        var staff = CodeLines(StaffPage());

        Assert.DoesNotContain("SupplyParameterFromQuery", staff);
        Assert.DoesNotContain("_sendRequested", staff);
        Assert.DoesNotContain("OnAfterRenderAsync", staff);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ④ 대조군 — 회계 화면에는 그 기능이 ★있다★
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>대조군</b>. ②③ 이 "전부 지우기" 로도 통과하면 가짜다.
    /// 걷어낸 기능이 <b>회계 화면에 살아 있어야</b> 진짜 분리다.
    /// </summary>
    /// <remarks>
    /// ⚠️ 이게 없으면 급여 발송 기능을 <b>통째로 삭제</b>해도 게이트가 초록불이다.
    /// </remarks>
    [Fact]
    public void 대조군_회계화면에는_발송기능이_있다()
    {
        var finance = CodeLines(FinancePage());

        Assert.Contains("명세서 메일발송", finance);
        Assert.Contains("OpenSendAsync", finance);
        Assert.Contains("_sendPreview", finance);
        Assert.Contains("SupplyParameterFromQuery", finance);
        Assert.Contains("OnAfterRenderAsync", finance);
    }

    /// <summary>🔴 결재 팝업의 「예」는 <b>회계 화면</b>으로 간다 — 직원 화면이 아니다.</summary>
    [Fact]
    public void 결재팝업은_회계화면으로_보낸다()
    {
        var approval = CodeLines(ReadSource("src", "HitPan.Web", "Pages", "Approval", "ApprovalManagePage.razor"));

        Assert.Contains("/accounting/payroll?send=1", approval);
        Assert.DoesNotContain("/hr/payroll?send=1", approval);
    }

    // ══════════════════════════════════════════════════════════════════
    //  헬퍼
    // ══════════════════════════════════════════════════════════════════

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>주석을 걷어낸다 — 주석에 적힌 낱말로 통과하면 가짜다.</summary>
    private static string CodeLines(string source)
    {
        var noRazor = Regex.Replace(source, @"@\*.*?\*@", string.Empty, RegexOptions.Singleline);
        var noBlock = Regex.Replace(noRazor, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        var kept = noBlock
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l =>
            {
                var t = l.TrimStart();
                return !t.StartsWith("//") && !t.StartsWith("///") && t.Length > 0;
            });
        return string.Join("\n", kept);
    }
}
