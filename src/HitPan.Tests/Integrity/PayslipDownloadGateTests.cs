using HitPan.API.Controllers;
using HitPan.Application.DTOs.Email;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 20260826작6 W5 게이트 — <b>급여명세서 PDF 를 남이 못 받게</b>.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>이 게이트가 왜 생겼나 — 우리가 구멍을 냈다.</b>
/// </para>
/// <para>
/// <c>/api/email/preview-pdf</c> 는 <c>[Authorize(TenantOnly)]</c> 뿐이라 <b>같은 회사 직원이면
/// 누구나</b> 부를 수 있고, <c>documentType</c>·<c>documentId</c> 를 <b>그대로 받아</b>
/// <c>RenderDocumentAsync</c> 로 넘긴다. 그런데 그 렌더는 <c>tenant_id</c> 만 보고
/// <b>사원 확인을 하지 않는다</b>(거래문서에는 사원 개념이 없으니 그럴 이유가 없었다).
/// </para>
/// <para>
/// ⇒ W1 에서 <c>payslip</c> 을 문서타입으로 <b>등록하는 순간</b> 그 길이 열렸다.
/// 실측했다 — 평직원이 사장님 명세서 id 로 불러 <b>PDF 27KB 가 그대로 나왔다</b>
/// (<i>급여명세서_사장님_2026-08.pdf</i>). <b>기능을 더한 것이 다른 문을 열었다.</b>
/// </para>
/// <para>
/// 🔴 사장님 ⑥결재: <i>"급여명세서는 이메일로도 받고, <b>본인것만</b> 그룹웨어에서도
/// 확인, 다운로드 가능함."</i>
/// </para>
/// </remarks>
public sealed class PayslipDownloadGateTests
{
    private const string PayslipDocType = "payslip";

    // ══════════════════════════════════════════════════════════════════
    //  ① 옛 문 봉인
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <c>preview-pdf</c> 로는 급여명세서가 <b>안 나간다</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 그 문에는 <b>사원 확인이 없다</b>. 거래문서는 그래도 되지만 급여명세서는 안 된다.
    /// 봉인을 빼면 평직원이 사장님 연봉을 받아간다.
    /// </para>
    /// <para>
    /// 🔴 <b>이 시험은 컨트롤러를 실제로 부른다.</b> 처음엔 글자로 검사했는데
    /// <c>if (false &amp;&amp; ...)</c> 로 봉인을 죽여도 <b>그대로 통과했다</b> — 낱말은 그 자리에
    /// 남아 있기 때문이다. <b>글자검사로는 못 잡는다.</b> 그래서 동작으로 바꿨다.
    /// </para>
    /// <para>
    /// ⚠️ PDF 렌더 대역은 <b>불리면 터진다</b>. 봉인이 살아 있으면 렌더까지 가지 않으므로
    /// 안 터지고, 봉인이 죽으면 렌더로 넘어가 <b>터진다</b> — 그 자체가 판정이다.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("payslip")]
    [InlineData("PAYSLIP")]   // 대소문자로 우회할 수 없어야 한다
    public async Task 메일_미리보기_문으로는_급여명세서가_안나간다(string documentType)
    {
        var controller = MakePreviewController();

        var result = await controller.PreviewPdf(documentType, "SLIP-BOSS", CancellationToken.None);

        // 🔴 파일이 나오면 사고다.
        Assert.IsNotType<FileContentResult>(result);
        Assert.IsType<ForbidResult>(result);
    }

    /// <summary>거래문서는 <b>그대로 나가야 한다</b> — 봉인이 넓게 잡으면 8종이 죽는다.</summary>
    /// <remarks>
    /// 🔴 <b>대조군이다.</b> 이게 없으면 "전부 막아버리기" 로도 위 시험을 통과할 수 있다.
    /// </remarks>
    [Theory]
    [InlineData("quotation")]
    [InlineData("tax_invoice")]
    public async Task 거래문서는_미리보기_문으로_그대로_나간다(string documentType)
    {
        var controller = MakePreviewController();

        var result = await controller.PreviewPdf(documentType, "DOC-1", CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
    }

    /// <summary>테넌트가 실린 <see cref="EmailController"/> 를 만든다.</summary>
    private static EmailController MakePreviewController()
    {
        var controller = new EmailController(new UnusedEmail(), new StubPdf())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.Items["TenantId"] = "T-GATE";
        return controller;
    }

    /// <summary>급여명세서를 부르면 <b>터진다</b> — 봉인이 살아 있으면 여기까지 안 온다.</summary>
    private sealed class StubPdf : IPdfRenderService
    {
        public Task<(byte[] Bytes, string FileName)> RenderDocumentAsync(string tenantId,
            string documentType, string documentId, CancellationToken ct = default)
        {
            if (string.Equals(documentType, PayslipDocType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "🔴 급여명세서가 preview-pdf 로 렌더까지 왔다 — 봉인이 죽었다.");
            }

            return Task.FromResult((new byte[] { 1, 2, 3 }, $"{documentId}.pdf"));
        }
    }

    /// <summary>이 시험에서는 안 쓰인다.</summary>
    private sealed class UnusedEmail : IEmailService
    {
        public Task<EmailSettingsDto> GetSettingsAsync(string t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateSettingsAsync(string t, UpdateEmailSettingsRequest r, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TestSmtpResponse> TestSmtpAsync(string t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SendEmailResponse> SendDocumentAsync(string t, string? u, SendDocumentEmailRequest r, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<EmailHistoryDto>> GetHistoryAsync(string t, string? d, int l = 100, CancellationToken ct = default) => throw new NotSupportedException();
    }

    // ══════════════════════════════════════════════════════════════════
    //  ② 새 문 — 본인 것만
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 급여명세서 PDF 문이 <b>본인 것인지 확인</b>하는가.
    /// </summary>
    /// <remarks>
    /// 🔴 조회(<c>GetSlip</c>)와 <b>같은 판정</b>이어야 한다. 조회만 막고 PDF 를 열어두면
    /// <b>막은 것이 아니다</b> — 목록에 안 보여도 id 를 넣으면 받아진다.
    /// </remarks>
    [Fact]
    public void 급여명세서_PDF_는_본인것인지_확인한다()
    {
        var block = MethodBlock(
            ReadSource("src", "HitPan.API", "Controllers", "PayrollController.cs"),
            "GetSlipPdf");

        // 남의 것을 볼 수 있는 사람이거나, 본인이어야 한다 — GetSlip 과 같은 조건.
        Assert.Contains("CanSeeOthersAsync", block);
        Assert.Matches(@"dto\.EmployeeId\s*!=\s*CurrentEmployeeId\(\)", block);
        Assert.Contains("Forbid()", block);
    }

    /// <summary>
    /// 🔴 <b>메일과 그룹웨어가 같은 관문</b>을 쓴다(§6①).
    /// </summary>
    /// <remarks>
    /// 한쪽만 열면 축이 갈린다 — <b>메일은 안 갔는데 그룹웨어에서는 받아지는</b> 상태가 되고,
    /// 금액이 바뀔 수 있는 명세서가 직원에게 간다.
    /// ⚠️ 판정을 <b>여기에 다시 적으면 안 된다</b> — 두 곳에 같은 규칙을 적으면
    /// 언젠가 한쪽만 고쳐진다. 발송이 쓰는 판정을 <b>불러서</b> 쓴다.
    /// </remarks>
    [Fact]
    public void 그룹웨어_다운로드는_발송과_같은_관문을_쓴다()
    {
        var block = MethodBlock(
            ReadSource("src", "HitPan.API", "Controllers", "PayrollController.cs"),
            "GetSlipPdf");

        Assert.Contains("CanDeliverAsync", block);

        // 🔴 부르기만 하면 소용없다 — ★그 답을 보고 막아야★ 한다.
        //    처음엔 "부르는가" 만 봤는데, 판정 결과를 무시하도록 고쳐도 그대로 통과했다.
        //    부르는 것과 따르는 것은 다르다.
        var answer = Regex.Match(block, @"var\s*\(\s*(\w+)\s*,\s*(\w+)\s*\)\s*=\s*await\s+_payslipMail");
        Assert.True(answer.Success, "CanDeliverAsync 의 답을 변수로 받아야 한다");

        var canFlag = answer.Groups[1].Value;

        // 그 변수가 거짓일 때 거절해야 한다.
        Assert.Matches($@"if\s*\(\s*!\s*{canFlag}\s*\)", block);
        Assert.Matches(@"return\s+BadRequest|return\s+Forbid", block);

        // 🔴 컨트롤러가 스스로 상태·결재를 판정하면 안 된다(그게 축이 갈리는 자리다).
        Assert.DoesNotContain("approval_documents", block);
        Assert.DoesNotContain("\"confirmed\"", block);
    }

    /// <summary>공통 관문이 <b>발송 판정을 그대로</b> 쓰는가 — 규칙을 두 번 적지 않는다.</summary>
    [Fact]
    public void 공통관문은_발송판정을_그대로_쓴다()
    {
        var block = MethodBlock(
            ReadSource("src", "HitPan.Application", "Services", "PayslipMailService.cs"),
            "public async Task<(bool CanDeliver, string? Reason)> CanDeliverAsync");

        // 발송이 쓰는 그 판정을 부른다.
        Assert.Contains("ToTarget", block);
        Assert.Contains("IsApprovalRequiredAsync", block);
    }

    /// <summary>
    /// ⚠️ <b>이메일 없는 직원도 그룹웨어에서는 받을 수 있어야 한다</b>(#20).
    /// </summary>
    /// <remarks>
    /// 🔴 이메일 없음은 <i>메일을 못 보내는</i> 사유일 뿐 <i>그룹웨어에서 못 받을</i> 사유가 아니다.
    /// 여기서 같이 막으면 <b>이메일 없는 직원만 자기 명세서를 영영 못 본다</b> —
    /// 워크플로우가 끊긴다.
    /// </remarks>
    [Fact]
    public void 이메일_없는_직원도_그룹웨어에서는_받는다()
    {
        var block = MethodBlock(
            ReadSource("src", "HitPan.Application", "Services", "PayslipMailService.cs"),
            "public async Task<(bool CanDeliver, string? Reason)> CanDeliverAsync");

        // 이메일 없음은 예외로 통과시켜야 한다.
        Assert.Contains("NoEmail", block);
    }

    /// <summary>
    /// 🔴 <b>결재 승인 전에는 못 받는다</b> — 메일과 같은 기준(⑤결재).
    /// </summary>
    /// <remarks>
    /// 그룹웨어만 열어두면 <b>결재 없이 급여명세서가 직원에게 간다</b>. 메일을 막은 의미가 없어진다.
    /// </remarks>
    [Fact]
    public void 결재_승인전에는_그룹웨어에서도_못받는다()
    {
        var block = MethodBlock(
            ReadSource("src", "HitPan.Application", "Services", "PayslipMailService.cs"),
            "public async Task<(bool CanDeliver, string? Reason)> CanDeliverAsync");

        // 승인 여부를 결재표에서 되짚어 본다.
        Assert.Contains("approval_documents", block);
        Assert.Contains("'approved'", block);

        // 못 내보낼 것이면 사유와 함께 거절한다.
        Assert.Matches(@"return\s*\(\s*false\s*,", block);
    }

    /// <summary>
    /// 테넌트 격리 — 남의 회사 명세서는 <b>조회 자체가 안 된다</b>(헌법 #2).
    /// </summary>
    [Fact]
    public void 공통관문도_테넌트로_막는다()
    {
        var block = MethodBlock(
            ReadSource("src", "HitPan.Application", "Services", "PayslipMailService.cs"),
            "public async Task<(bool CanDeliver, string? Reason)> CanDeliverAsync");

        Assert.Matches(@"s\.tenant_id\s*=\s*@TenantId", block);
    }

    /// <summary>
    /// PDF 문에 <c>[RequirePermission]</c> 을 <b>걸지 않았는가</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 걸면 <b>일반 직원이 자기 명세서도 못 받는다</b> — 사장님 ⑥결재가 죽는다.
    /// 범위는 권한 속성이 아니라 <c>employee_id</c> 대조로 좁힌다(조회와 같은 방식).
    /// </remarks>
    [Fact]
    public void PDF_문에는_메뉴권한을_걸지_않는다()
    {
        var ctrl = CodeLines(ReadSource("src", "HitPan.API", "Controllers", "PayrollController.cs"));

        var at = ctrl.IndexOf("slips/{slipId}/pdf", StringComparison.Ordinal);
        Assert.True(at >= 0, "급여명세서 PDF 라우트가 있어야 한다");

        // 라우트와 메서드 사이에 권한 속성이 끼면 안 된다.
        var between = ctrl[at..Math.Min(ctrl.Length, at + 160)];
        Assert.DoesNotContain("RequirePermission", between);
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
