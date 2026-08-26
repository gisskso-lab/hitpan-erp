using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 20260826작6 W6·W7 게이트 — <b>발송 화면</b>.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 이 게이트가 막는 것은 <b>화면이 사람을 건너뛰는 자리</b>다.
/// 되돌릴 수 없는 발송이라 <b>보고 나서</b> 눌러야 한다(사장님 반자동 원칙).
/// </para>
/// <list type="number">
///   <item><b>단추 한 번에 나가는</b> 자리 — 확인 없이 발송</item>
///   <item><b>W3 의 「예」가 헛걸음</b>이 되는 자리 — 화면만 열리고 아무 일도 안 남</item>
///   <item><b>전부 재발송</b> 하는 자리 — 이미 받은 사람이 두 번 받는다</item>
///   <item><b>남의 명세서</b>를 받는 자리 — 잘못된 문(<c>preview-pdf</c>)을 쓰는 것</item>
/// </list>
/// </remarks>
public sealed class PayslipSendScreenGateTests
{
    private static string Page() =>
        ReadSource("src", "HitPan.Web", "Pages", "HR", "PayrollPage.razor");

    // ══════════════════════════════════════════════════════════════════
    //  ① 단추 한 번에 안 나간다
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>「명세서 메일발송」 단추는 발송하지 않는다</b> — 확인 화면을 열 뿐이다.
    /// </summary>
    /// <remarks>
    /// 단추에 발송을 바로 걸면 <b>한 번 눌러서 전 직원에게 나간다</b>. 회수 불가다.
    /// </remarks>
    [Fact]
    public void 발송단추는_확인화면만_연다()
    {
        var page = CodeLines(Page());

        // 단추가 여는 것은 확인 화면이다.
        Assert.Matches(@"OnClick=""OpenSendAsync""", page);

        var open = MethodBlock(Page(), "private async Task OpenSendAsync");

        // 🔴 여는 자리에서 발송을 부르면 안 된다.
        Assert.DoesNotContain("SendMailAsync", open);

        // 명단을 먼저 불러온다 — 보여줄 것이 있어야 확인이 된다.
        Assert.Contains("GetSendPreviewAsync", open);
    }

    /// <summary>확인 화면이 <b>이름·주소·사유</b>를 다 보여주는가(§4).</summary>
    /// <remarks>
    /// 🔴 숫자만 보여주면 경리는 <b>누가 못 받는지</b> 모른다. 주소를 그대로 보여줘야
    /// <b>오입력</b>을 눈으로 잡는다 — 오입력이면 남의 메일함에 그 직원 연봉이 간다.
    /// </remarks>
    [Fact]
    public void 확인화면은_이름과_주소와_사유를_보여준다()
    {
        var page = CodeLines(Page());

        Assert.Contains("t.EmployeeName", page);
        Assert.Contains("t.RecipientEmail", page);
        Assert.Contains("t.BlockReasonLabel", page);
    }

    /// <summary>결과도 <b>건별</b>로 보여주는가 — 뭉뚱그린 "완료" 금지.</summary>
    [Fact]
    public void 결과는_건별로_보여준다()
    {
        var page = CodeLines(Page());

        Assert.Contains("_sendResult.SuccessCount", page);
        Assert.Contains("_sendResult.FailedCount", page);

        // 실패 사유를 그대로 보여준다.
        Assert.Contains("r.Error", page);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ② W3 의 「예」가 헛걸음이 되지 않는다
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>W3 와 W6 를 잇는 자리.</b> 「예」로 넘어오면 확인 화면이 <b>자동으로 열린다</b>.
    /// </summary>
    /// <remarks>
    /// 이 배선이 없으면 사장님이 「예」 를 눌러도 급여 화면만 열리고 <b>아무 일도 안 일어난다</b>.
    /// 전형적인 <i>"고쳤는데 안 갔다"</i> 이고, 워크플로우가 끊긴다(헌법 #20).
    /// </remarks>
    [Fact]
    public void 결재팝업에서_예로_넘어오면_확인화면이_열린다()
    {
        var after = MethodBlock(Page(), "protected override async Task OnAfterRenderAsync");

        // W3 가 넘긴 신호를 본다.
        Assert.Contains("_sendRequested", after);

        // 그 신호가 있으면 확인 화면을 연다.
        Assert.Contains("OpenSendAsync", after);

        // 🔴 첫 렌더에서만 — 매 렌더마다 열면 닫아도 계속 뜬다.
        Assert.Contains("firstRender", after);
    }

    /// <summary>
    /// 🔴 <b>W3 의 신호를 실제로 쓰는가.</b> 담아만 두고 안 쓰면 「예」가 헛걸음이다.
    /// </summary>
    /// <remarks>
    /// ⚠️ W3 를 끝냈을 때 <c>_sendRequested</c> 는 <b>담기만 하고 아무 동작도 안 했다</b>
    /// (발송 화면이 아직 없었다). 그 상태로 두면 안 된다고 작업지시서에 남겼고, 여기서 잇는다.
    /// </remarks>
    [Fact]
    public void W3_신호는_담기만_하지_않고_쓰인다()
    {
        var page = CodeLines(Page());

        // 받는 자리(W3) 와 쓰는 자리(W6) 가 둘 다 있어야 한다.
        Assert.Contains("SupplyParameterFromQuery(Name = \"send\")", page);

        // 🔴 대입만 있고 읽는 곳이 없으면 헛걸음이다 — 조건으로 읽히는지 본다.
        Assert.Matches(@"!\s*_sendRequested|_sendRequested\s*\)", page);
    }

    /// <summary>
    /// 닫을 때 주소의 <c>?send=1</c> 을 <b>지우는가</b>.
    /// </summary>
    /// <remarks>
    /// 안 지우면 <b>새로고침할 때마다 확인 화면이 다시 뜬다</b> — 닫을 수 없는 화면이 된다.
    /// </remarks>
    [Fact]
    public void 닫으면_발송요청_표시를_지운다()
    {
        var close = MethodBlock(Page(), "private async Task CloseSendAsync");

        Assert.Matches(@"_sendRequested\s*=\s*false", close);
        Assert.Contains("NavigateTo", close);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ③ 재발송은 실패분만
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>전부 다시 보내면 이미 받은 사람이 두 번 받는다.</b> 실패한 것만 넘긴다.
    /// </summary>
    [Fact]
    public void 재발송은_실패분만_보낸다()
    {
        var resend = MethodBlock(Page(), "private async Task ResendFailedAsync");

        Assert.Contains("FailedSlipIds", resend);

        // 🔴 전체 목록을 다시 긁으면 안 된다.
        Assert.DoesNotContain("_sendPreview.Targets", resend);
        Assert.DoesNotContain("_slips", resend);
    }

    /// <summary>발송은 <b>보낼 수 있는 사람만</b> 대상으로 삼는가.</summary>
    [Fact]
    public void 발송대상은_가능한_사람만_고른다()
    {
        var confirm = MethodBlock(Page(), "private async Task ConfirmSendAsync");

        Assert.Matches(@"Where\s*\(\s*t\s*=>\s*t\.CanSend\s*\)", confirm);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ④ 명세서는 옳은 문으로만
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 화면이 <c>preview-pdf</c> 로 급여명세서를 받으면 <b>안 된다</b>(W5).
    /// </summary>
    /// <remarks>
    /// 그 문에는 사원 확인이 없다. 서버가 막아도 화면이 그 길을 쓰면 <b>안 열린다</b> —
    /// 직원이 자기 명세서도 못 받는다.
    /// </remarks>
    [Fact]
    public void 화면은_급여명세서를_전용문으로_받는다()
    {
        var page = CodeLines(Page());

        Assert.DoesNotContain("preview-pdf", page);
        Assert.Contains("DownloadSlipPdfAsync", page);

        var svc = CodeLines(ReadSource("src", "HitPan.Web", "Services", "PayrollService.cs"));
        Assert.Matches(@"api/payroll/slips/.*?/pdf", svc);
    }

    /// <summary>
    /// 🔴 PDF 를 <c>&lt;a href&gt;</c> 로 걸지 <b>않는다</b> — 토큰이 안 실려 401 이 온다.
    /// </summary>
    /// <remarks>
    /// ⚠️ 처음엔 <c>Href</c> 로 걸었다. 브라우저가 그 주소를 직접 부르면 <b>로그인 토큰이
    /// 안 실린다</b>. 이 레포가 PDF 를 받는 방식은 <b>인증된 HttpClient 로 바이트를 받아</b>
    /// 브라우저에 넘기는 것이다.
    /// </remarks>
    [Fact]
    public void PDF_는_인증된_경로로_받는다()
    {
        var download = MethodBlock(
            ReadSource("src", "HitPan.Web", "Services", "PayrollService.cs"),
            "public async Task<(byte[]? Bytes, string? Error)> DownloadSlipPdfAsync");

        // 인증이 붙은 HttpClient 로 받는다.
        Assert.Contains("http.GetAsync", download);
        Assert.Contains("ReadAsByteArrayAsync", download);

        // 🔴 서버가 준 사유를 그대로 전한다 — 결재 대기인지 권한 문제인지 알아야 한다.
        Assert.Contains("TryReadMessageAsync", download);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ⑤ 다이얼로그는 인증 캐스케이드 밖
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⚠️ 다이얼로그 안에 <c>AuthorizeView</c> 를 쓰면 <b>화면이 통째로 죽는다</b>(20260825작11).
    /// </summary>
    [Fact]
    public void 다이얼로그에_AuthorizeView_를_쓰지_않는다()
    {
        var page = CodeLines(Page());

        Assert.DoesNotContain("AuthorizeView", page);
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

    /// <summary>주석·빈 줄을 걸러낸 실제 코드만 남긴다(Razor 주석 <c>@* *@</c> 포함).</summary>
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
