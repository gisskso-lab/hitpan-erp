using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 20260826작6 W4 게이트 — <b>급여명세서 일괄 발송</b>.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>이 기능은 되돌릴 수 없다.</b> 잘못 나가면 전 직원에게 남의 연봉이 간다. 회수 불가다.
/// 그래서 게이트가 막는 것도 <b>"안 나가야 할 것이 나가는 자리"</b> 넷이다.
/// </para>
/// <list type="number">
///   <item><b>빈 메일</b> — PDF 렌더가 실패했는데 메일만 나가는 자리</item>
///   <item><b>본문 노출</b> — 본문에 금액·항목이 실리는 자리(②결재)</item>
///   <item><b>미결재 발송</b> — 화면을 우회한 요청이 통과하는 자리(⑤결재)</item>
///   <item><b>조용한 누락</b> — 이메일 없는 직원이 사유 없이 빠지는 자리(§4)</item>
/// </list>
/// </remarks>
public sealed class PayslipMailGateTests
{
    private static string Service() =>
        ReadSource("src", "HitPan.Application", "Services", "PayslipMailService.cs");

    // ══════════════════════════════════════════════════════════════════
    //  ① 빈 메일 차단
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>PDF 를 먼저 만들고, 실패하면 보내지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>EmailService.SendDocumentAsync</c> 는 PDF 렌더가 실패해도 <b>첨부 없이 메일을 보낸다</b>
    /// (그 안의 catch 가 <i>"첨부 없이 발송 진행"</i> 이다). 거래명세서면 말이 되지만 급여명세서는
    /// <b>본문에 금액을 안 적으므로</b>(②결재) 직원은 <b>빈 메일</b>을 받고 이력엔 <c>sent</c> 로 남는다.
    /// </para>
    /// <para>
    /// ⇒ 급여 경로는 <b>스스로 먼저 렌더해서</b> 성공한 것만 발송에 넘겨야 한다.
    /// </para>
    /// </remarks>
    [Fact]
    public void PDF_를_먼저_만들고_실패하면_보내지_않는다()
    {
        var send = MethodBlock(Service(), "private async Task<PayslipSendResultItemDto> SendOneAsync");

        var renderAt = send.IndexOf("RenderDocumentAsync", StringComparison.Ordinal);
        var sendAt = send.IndexOf("SendDocumentAsync", StringComparison.Ordinal);

        Assert.True(renderAt >= 0, "발송 전에 PDF 를 직접 렌더해야 한다");
        Assert.True(sendAt >= 0, "메일 발송을 불러야 한다");

        // 🔴 렌더가 발송보다 ★앞★ 에 있어야 한다. 뒤에 있으면 이미 나간 뒤다.
        Assert.True(renderAt < sendAt,
            "PDF 렌더가 메일 발송보다 먼저 와야 한다 — 뒤에 있으면 빈 메일이 이미 나간 뒤다");

        // 렌더 실패 시 그 자리에서 빠져나가야 한다(계속 진행하면 빈 메일이 나간다).
        var renderCatch = send[renderAt..sendAt];
        Assert.Contains("catch", renderCatch);
        Assert.Contains("return", renderCatch);
    }

    /// <summary>보내지 <b>못한</b> 건도 이력에 남는가 — 안 남기면 "안 보냈다" 는 사실이 사라진다.</summary>
    [Fact]
    public void 못보낸_건도_이력에_남는다()
    {
        var code = CodeLines(Service());

        Assert.Contains("email_send_history", code);
        Assert.Contains("'failed'", code);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ② 본문 노출 차단 (②결재)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>본문에 금액·항목이 들어가지 않는다</b>(사장님 ②결재).
    /// </summary>
    /// <remarks>
    /// 사장님 2026-08-26: <i>"히트판에서 발송되는 사내메일은 무조건 본문에 자료내용을 공개하지 않음.
    /// 000 발송드립니다. 정도로."</i> — 급여만이 아니라 <b>사내메일 전체 원칙</b>이다.
    /// </remarks>
    [Fact]
    public void 메일_본문에_금액과_항목을_적지_않는다()
    {
        var send = MethodBlock(Service(), "private async Task<PayslipSendResultItemDto> SendOneAsync");

        var m = Regex.Match(send, @"Body\s*=\s*(\$?""(?:[^""\\]|\\.)*"")");
        Assert.True(m.Success, "메일 본문을 만드는 자리를 찾아야 한다");

        var body = m.Groups[1].Value;

        // 🔴 금액·명세 항목을 뜻하는 어떤 값도 본문 식에 끼어들면 안 된다.
        foreach (var forbidden in new[]
                 {
                     "NetPayment", "TotalPayment", "TotalDeduct",
                     "실수령", "지급액", "공제", "Amount"
                 })
        {
            Assert.False(body.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"메일 본문에 '{forbidden}' 이 들어가면 안 된다 — ②결재(본문에 자료내용 비공개)");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  ③ 미결재 발송 차단 (⑤결재)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>서버가 다시 판정한다</b> — 화면이 보낸 id 를 그대로 보내지 않는다.
    /// </summary>
    /// <remarks>
    /// 화면을 우회한 요청(직접 API 호출)으로 <b>미결재 명세서가 나가면 안 된다</b>.
    /// 발송 루프 안에서 <c>CanSend</c> 를 보고 아니면 건너뛰어야 한다.
    /// </remarks>
    [Fact]
    public void 서버가_발송가능여부를_다시_판정한다()
    {
        var send = MethodBlock(Service(), "public async Task<SendPayslipMailResponse> SendAsync");

        // 요청 목록을 그대로 쓰지 않고 DB 에서 다시 읽는다.
        Assert.Contains("LoadRowsAsync", send);

        // 각 건마다 판정을 다시 만든다.
        Assert.Contains("ToTarget", send);

        // 못 보낼 것이면 발송으로 넘어가지 않는다.
        Assert.Matches(@"if\s*\(\s*!\s*\w+\.CanSend\s*\)", send);
    }

    /// <summary>결재를 <b>쓰는 회사</b>는 승인 없이 못 보낸다(⑤결재).</summary>
    [Fact]
    public void 결재를_쓰는_회사는_승인된_것만_보낸다()
    {
        var judge = MethodBlock(Service(), "private static PayslipSendTargetDto ToTarget");

        Assert.Matches(@"approvalRequired\s*&&\s*!\s*r\.IsApproved", judge);
        Assert.Contains("NotApproved", judge);
    }

    /// <summary>
    /// ⚠️ 결재를 <b>안 쓰는 회사</b>가 한 통도 못 보내면 안 된다(#20).
    /// </summary>
    /// <remarks>
    /// 🔴 <c>is_enabled</c> 만 보면 <b>켜두고 결재선을 안 짠 회사</b>가 영영 못 보낸다 —
    /// 그 경우 결재 문서가 아예 안 생기기 때문이다(14차 P2 사고 자리). <b>줄 수도 같이</b> 봐야 한다.
    /// </remarks>
    [Fact]
    public void 결재기능_판정은_설정과_결재선을_모두_본다()
    {
        var judge = MethodBlock(Service(), "private async Task<bool> IsApprovalRequiredAsync");

        Assert.Contains("approval_settings", judge);
        Assert.Contains("is_enabled", judge);
        Assert.Contains("approval_doc_lines", judge);
        Assert.Matches(@"lineCount\s*>\s*0", judge);
    }

    /// <summary>승인 여부를 <b>급여표에서 읽지 않는다</b> — W2 의 급여표 무접촉 원칙.</summary>
    /// <remarks>
    /// 사장님 2026-08-26: <i>"결재승인은 급여표에 써지는 내용을 건들라는게 아니고"</i>.
    /// 승인 칸을 <c>payroll_slips</c> 에 만들면 그 원칙이 깨진다 — <c>approval_documents</c> 를 되짚는다.
    /// </remarks>
    [Fact]
    public void 승인여부는_결재표를_되짚어_판정한다()
    {
        var code = CodeLines(Service());

        Assert.Contains("approval_documents", code);
        Assert.Matches(@"a\.ref_id\s*=\s*s\.slip_id", code);

        // 🔴 급여표에 쓰지 않는다.
        Assert.DoesNotContain("UPDATE payroll_slips", code);
        Assert.DoesNotContain("INSERT INTO payroll_slips", code);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ④ 조용한 누락 차단 (§4)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 이메일 없는 직원이 <b>조용히 빠지지 않는다</b> — 사유를 달고 명단에 남는다.
    /// </summary>
    /// <remarks>
    /// 조용히 건너뛰면 경리는 <i>"전체 발송했다"</i> 고 알고 <b>그 직원만 영영 못 받는다</b>.
    /// 목록에서 걸러내면(<c>WHERE email IS NOT NULL</c>) 그게 정확히 그 사고다.
    /// </remarks>
    [Fact]
    public void 이메일_없는_직원도_명단에_사유와_함께_남는다()
    {
        var code = CodeLines(Service());

        // 🔴 조회에서 걸러내면 안 된다 — 걸러내는 순간 화면에서 사라진다.
        Assert.DoesNotContain("email IS NOT NULL", code);
        Assert.DoesNotContain("email <> ''", code);

        var judge = MethodBlock(Service(), "private static PayslipSendTargetDto ToTarget");
        Assert.Contains("NoEmail", judge);
    }

    /// <summary>불가 사유를 <b>하나로 뭉치지 않는다</b> — 경리가 무엇을 고칠지 알아야 한다.</summary>
    [Fact]
    public void 불가사유는_뭉치지_않고_구분된다()
    {
        var dto = ReadSource("src", "HitPan.Application", "DTOs", "Payroll", "PayrollDtos.cs");

        foreach (var reason in new[] { "no_email", "not_approved", "not_confirmed" })
            Assert.Contains(reason, dto);
    }

    /// <summary>결과를 <b>뭉뚱그리지 않는다</b> — 성공·실패를 사람별로 돌려준다.</summary>
    [Fact]
    public void 발송결과는_건별로_돌려준다()
    {
        var dto = CodeLines(ReadSource("src", "HitPan.Application", "DTOs", "Payroll", "PayrollDtos.cs"));

        Assert.Contains("PayslipSendResultItemDto", dto);
        Assert.Contains("FailedSlipIds", dto);   // 실패분만 재발송할 수 있어야 한다
    }

    // ══════════════════════════════════════════════════════════════════
    //  ⑤ 권한
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 발송·미리보기는 <b>쓰기 권한</b>이다 — 조회 권한만으로 전 직원 급여 명단이 나가면 안 된다.
    /// </summary>
    [Fact]
    public void 발송과_미리보기는_쓰기권한을_요구한다()
    {
        var ctrl = CodeLines(ReadSource("src", "HitPan.API", "Controllers", "PayrollController.cs"));

        foreach (var route in new[] { "slips/send-mail/preview", "slips/send-mail" })
        {
            var at = ctrl.IndexOf(route, StringComparison.Ordinal);
            Assert.True(at >= 0, $"'{route}' 라우트가 있어야 한다");

            // 라우트 바로 다음 줄에 권한 속성이 있어야 한다.
            var after = ctrl[at..Math.Min(ctrl.Length, at + 200)];
            Assert.Contains("RequirePermission(Menu, \"update\")", after);
        }
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
        var code = CodeLines(source);
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
