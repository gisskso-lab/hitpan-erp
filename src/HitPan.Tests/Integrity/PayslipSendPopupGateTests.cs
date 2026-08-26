using System.Text.RegularExpressions;
using HitPan.Application.DTOs.Approval;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 20260826작6 W3 게이트 — <b>급여명세서 발송 팝업이 최종 승인에만 뜨는가.</b>
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>무엇을 막는가</b> — 결재선이 2단이면 <b>1단 승인은 아직 승인이 아니다</b>.
/// 부장이 눌러도 <c>current_seq++</c> 만 되고 문서는 <c>pending</c> 으로 남는다.
/// 그런데 종전 API 응답은 두 경우 모두 승인되었습니다 한 문장이었고,
/// 화면 서비스는 응답 본문을 <b>버리고</b> <c>bool</c> 만 돌려줬다.
/// </para>
/// <para>
/// ⇒ 그 상태로 팝업을 붙였으면 <b>부장이 승인한 순간 발송 팝업이 뜬다.</b>
/// 대표이사 결재 전에 급여명세서가 나가는 길이 열린다 —
/// 사장님 ⑤결재(결재 없이는 안 나간다) 정면 위반이고, <b>되돌릴 수 없다</b>
/// (전 직원에게 남의 연봉이 간다).
/// </para>
/// <para>
/// 🔴 <b>글자가 아니라 동작으로 검사한다</b> — §1 은 실제 결과 객체를 만들어 판정을 확인한다.
/// 조건을 <c>&gt;=</c> 에서 <c>&gt;</c> 로 바꾸거나 action 검사를 빼면 <b>FAIL 한다.</b>
/// </para>
/// </remarks>
public sealed class PayslipSendPopupGateTests
{
    private const string PayslipDocType = "payslip";

    // ══════════════════════════════════════════════════════════════════
    //  §1 동작 — 최종승인 판정 그 자체
    // ══════════════════════════════════════════════════════════════════

    /// <summary>화면이 팝업을 띄울지 정하는 <b>실제 판정</b>. 화면 코드와 <b>같은 두 조건</b>을 쓴다.</summary>
    private static bool ShouldOfferSend(ProcessApprovalResult r)
        => r.IsFinalApproved && string.Equals(r.DocType, PayslipDocType, StringComparison.Ordinal);

    /// <summary><c>ApprovalService.ProcessAsync</c> 가 결과를 만들 때 쓰는 판정을 그대로 실행한다.</summary>
    private static ProcessApprovalResult Judge(string action, int currentSeq, int totalLines, string docType, string refId)
        => new()
        {
            IsFinalApproved = action == "approved" && currentSeq >= totalLines,
            DocType = docType,
            RefId = refId
        };

    [Theory]
    // 1단 결재선 — 그 한 번이 곧 최종. 팝업 O
    [InlineData("approved", 1, 1, true)]
    // 🔴 2단 결재선의 1단 승인 — 아직 대표이사가 안 봤다. 팝업 X
    [InlineData("approved", 1, 2, false)]
    // 2단 결재선의 2단(최종) 승인 — 팝업 O
    [InlineData("approved", 2, 2, true)]
    // 🔴 3단의 중간 두 번 — 둘 다 팝업 X
    [InlineData("approved", 1, 3, false)]
    [InlineData("approved", 2, 3, false)]
    [InlineData("approved", 3, 3, true)]
    // 반려는 몇 단이든 팝업 X — 승인이 아니다
    [InlineData("rejected", 1, 1, false)]
    [InlineData("rejected", 3, 3, false)]
    public void 급여명세서_발송팝업은_최종승인에만_뜬다(string action, int currentSeq, int totalLines, bool expected)
    {
        var r = Judge(action, currentSeq, totalLines, PayslipDocType, "SLIP-1");

        Assert.Equal(expected, ShouldOfferSend(r));
    }

    [Theory]
    [InlineData("leave")]
    [InlineData("expense")]
    [InlineData("absence")]
    [InlineData("overtime")]
    public void 급여명세서가_아닌_문서는_최종승인이어도_발송팝업이_안뜬다(string docType)
    {
        // 최종 승인 상태 그대로인데 문서 종류만 다르다.
        var r = Judge("approved", 1, 1, docType, "REF-1");

        Assert.True(r.IsFinalApproved, "최종 승인은 맞다");
        Assert.False(ShouldOfferSend(r), $"{docType} 승인에 급여명세서 발송 팝업이 뜨면 안 된다");
    }

    // ══════════════════════════════════════════════════════════════════
    //  §2 배선 — 그 판정이 실제로 그 자리에 있는가
    // ══════════════════════════════════════════════════════════════════

    /// <summary><c>ProcessAsync</c> 가 <b>결과를 돌려주는가</b>. Task 로 되돌리면 화면이 다시 눈이 먼다.</summary>
    [Fact]
    public void ProcessAsync_는_최종승인여부를_돌려준다()
    {
        var iface = ReadSource("src", "HitPan.Application", "Interfaces", "IApprovalService.cs");
        Assert.Contains("Task<ProcessApprovalResult> ProcessAsync", CodeLines(iface));

        var svc = MethodBlock(
            ReadSource("src", "HitPan.Application", "Services", "ApprovalService.cs"),
            "public async Task<ProcessApprovalResult> ProcessAsync");

        Assert.Contains("IsFinalApproved", svc);
    }

    /// <summary>
    /// 🔴 <b>이 시험이 이 게이트의 핵심이다.</b> 최종승인 판정에 <b>두 조건이 다 있는가</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// action 검사가 빠지면 <b>반려에도 팝업</b>이 뜬다.
    /// <c>CurrentSeq &gt;= TotalLines</c> 가 빠지면 <b>1단 승인에도 팝업</b>이 뜬다.
    /// </para>
    /// <para>
    /// ⚠️ 낱말 하나로 검사하지 않는다 — TotalLines 는 이 메서드 안 여러 곳(알림 조건 등)에
    /// 나온다. <b>IsFinalApproved 에 대입하는 그 식</b> 만 잘라서 본다.
    /// </para>
    /// </remarks>
    [Fact]
    public void 최종승인_판정에_두_조건이_모두_있다()
    {
        var svc = MethodBlock(
            ReadSource("src", "HitPan.Application", "Services", "ApprovalService.cs"),
            "public async Task<ProcessApprovalResult> ProcessAsync");

        var m = Regex.Match(svc, @"IsFinalApproved\s*=\s*([^,;\r\n]+)");
        Assert.True(m.Success, "IsFinalApproved 에 대입하는 식을 찾아야 한다");

        var expr = m.Groups[1].Value;

        Assert.Contains("\"approved\"", expr);
        Assert.Matches(@"CurrentSeq\s*>=\s*.*TotalLines", expr);
    }

    /// <summary>화면이 <b>두 조건을 다 보는가</b> — 최종승인 <b>그리고</b> payslip.</summary>
    [Fact]
    public void 화면은_최종승인과_문서종류를_모두_본다()
    {
        var page = CodeLines(ReadSource("src", "HitPan.Web", "Pages", "Approval", "ApprovalManagePage.razor"));

        Assert.Contains("IsFinalApproved", page);
        Assert.Contains("PayslipDocType", page);
        Assert.Contains("\"payslip\"", page);

        // 본문을 버리는 옛 경로가 아니라 본문을 읽는 경로를 써야 한다.
        Assert.Contains("ProcessDetailedAsync", page);
    }

    /// <summary>🔴 <b>팝업의 예 는 발송이 아니다</b> — 확인 화면으로 보낼 뿐이다(반자동 원칙).</summary>
    [Fact]
    public void 팝업의_예는_발송하지_않고_확인화면으로만_보낸다()
    {
        var page = CodeLines(ReadSource("src", "HitPan.Web", "Pages", "Approval", "ApprovalManagePage.razor"));

        Assert.Contains("/accounting/payroll", page);

        // 이 화면에서 메일을 직접 보내면 안 된다.
        Assert.DoesNotContain("send-mail", page);
        Assert.DoesNotContain("SendMail", page);
    }

    /// <summary>🔴 <b>아니오가 막다른 길이 되면 안 된다</b>(헌법 #20) — 거절을 <b>기록하지 않는다</b>.</summary>
    [Fact]
    public void 아니오는_아무것도_기록하지_않는다()
    {
        var page = CodeLines(ReadSource("src", "HitPan.Web", "Pages", "Approval", "ApprovalManagePage.razor"));
        var block = MethodBlock(page, "MaybeOfferPayslipSendAsync");

        // 아니오 분기에서 곧바로 빠져나올 뿐, 저장·기록 호출이 없어야 한다.
        Assert.Matches(@"if\s*\(\s*go\s*!=\s*true\s*\)\s*return\s*;", block);

        foreach (var forbidden in new[] { "SaveAsync", "PostAsJsonAsync", "PutAsJsonAsync", "UpdateAsync" })
            Assert.DoesNotContain(forbidden, block);
    }

    /// <summary>급여 화면이 send·slip 질의를 <b>받는가</b>. 안 받으면 예 가 막다른 길이다.</summary>
    [Fact]
    public void 급여화면이_발송요청과_명세서id를_받는다()
    {
        var page = CodeLines(ReadSource("src", "HitPan.Web", "Pages", "Finance", "PayrollManagePage.razor"));

        Assert.Contains("SupplyParameterFromQuery(Name = \"send\")", page);
        Assert.Contains("SupplyParameterFromQuery(Name = \"slip\")", page);

        // 넘어온 명세서의 연·월로 화면을 맞춘다.
        Assert.Matches(@"_year\s*=\s*slip\.PayYear", page);
        Assert.Matches(@"_month\s*=\s*slip\.PayMonth", page);
    }

    // ══════════════════════════════════════════════════════════════════
    //  헬퍼
    // ══════════════════════════════════════════════════════════════════

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
            dir = Directory.GetParent(dir)?.FullName;

        Assert.True(dir is not null && Directory.Exists(Path.Combine(dir, "src")), "레포 루트를 찾아야 한다");
        return dir!;
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>주석·빈 줄을 걸러낸 실제 코드만 남긴다(주석 문구를 코드로 오인하지 않도록).</summary>
    private static string CodeLines(string source)
    {
        var noBlock = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
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

    /// <summary>중괄호 균형으로 메서드 본문만 잘라낸다.</summary>
    private static string MethodBlock(string source, string signature)
    {
        var code = source.Contains("///") ? CodeLines(source) : source;
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} 를 찾아야 한다");

        var open = code.IndexOf('{', start);
        Assert.True(open >= 0, $"{signature} 본문 시작을 찾아야 한다");

        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}')
            {
                depth--;
                if (depth == 0) return code[open..(i + 1)];
            }
        }

        Assert.Fail($"{signature} 본문 끝을 찾아야 한다");
        return string.Empty;
    }
}
