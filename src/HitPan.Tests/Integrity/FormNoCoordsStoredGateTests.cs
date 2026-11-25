using System.Data;
using System.Reflection;
using HitPan.API.Controllers;
using HitPan.Application.DTOs.Documents;
using HitPan.Application.DTOs.Settings;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-51 갈래 §59 「고침 1」 P2-1 — 「칸 0」 = <b>저장된 칸 0</b>(<see cref="FormCoordsStored.IsEmpty"/>) 시험(DB 없음).
/// </summary>
/// <remarks>
/// 작업지시서 `docs/운영기록/20261120작_WG1-51_갈래59_고침1_작업지시서.md` §2-1 표 · §3 U1~U5.
/// 🔴 <b>동작 시험</b> — 제품 함수 · 제품 판정(반사) · 제품 렌더(PdfPig 판독)를 그대로 부른다. 그리는 꼴 = <c>FormNoCoordsImageOnlyGateTests</c> 선례.
/// 🔴 기존 시험 파일은 한 줄도 안 고쳤다(#1) — 새 파일로 더한다.
/// </remarks>
public sealed class FormNoCoordsStoredGateTests
{
    private const string Tenant = "gate-59f1-tenant";

    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private const string OneCoordJson = "[{\"key\":\"partner_business_no\",\"x_mm\":30,\"y_mm\":40}]";

    /// <summary>E9 — 칸 1개인데 종이 밖(x &gt; 420).</summary>
    private const string OffPaperJson = "[{\"key\":\"partner_business_no\",\"x_mm\":500,\"y_mm\":40}]";

    /// <summary>§2-1 「칸 0」 집합 표 — 이 표가 정답이다(작업지시서 글자 그대로 · 17 글자).</summary>
    public static TheoryData<string, string?, bool> Table => new()
    {
        { "E1 NULL", null, true },
        { "E2 빈 글자", "", true },
        { "E3 공백", " ", true },
        { "E3 탭·CR·LF", "\t\r\n", true },
        { "E4 빈 목록", "[]", true },
        { "E5 빈 목록+공백", " [ ] ", true },
        { "E5 빈 목록+LF", "[\n]", true },
        { "E6 JSON null", "null", false },
        { "E7 객체", "{}", false },
        { "E7 목록 아님", "abc", false },
        { "E8 빈 객체 칸", "[{}]", false },
        { "E8 key 없음", "[{\"x_mm\":30,\"y_mm\":40}]", false },
        { "E8 x/y 없음", "[{\"key\":\"partner_business_no\"}]", false },
        { "E9 종이 밖 1칸", OffPaperJson, false },
        { "E10 깨진 [", "[", false },
        { "E10 깨진 글자", "not json", false },
        { "E11 정상 1칸", OneCoordJson, false },
    };

    // ═════════════════════════════════════════════════════════════
    // 자 — 그림 · 양식 · 서비스 · 판독 (FormNoCoordsImageOnlyGateTests 꼴)
    // ═════════════════════════════════════════════════════════════

    private static string WriteValidPng()
    {
        Directory.CreateDirectory(FormBackgroundPaths.Root);
        var p = Path.Combine(FormBackgroundPaths.Root, $"gate59f1-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(p, Convert.FromBase64String(OnePixelPngBase64));
        return p;
    }

    private static void Remove(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"[gate59f1] 시험 그림을 못 지웠다(시험 판정과 무관): {ex.Message}");
        }
    }

    /// <summary>K3 차림 — 빈 A4 · 저장된 그림 찍기 켬 · 그림 경로 · 견적서.</summary>
    private static FormTemplateDto K3(string? background, string? coordsJson) => new()
    {
        FormType = "estimate",
        TemplateName = "gate59f1",
        PaperMode = "plain",
        PrintBackground = true,
        BackgroundImageUrl = background,
        FieldCoordsJson = coordsJson,
        StyleKey = "basic",
        ShowWatermark = false,
    };

    private static PdfRenderService MakeService(FormTemplateDto? template)
    {
        var forms = new Mock<IFormTemplateService>();
        forms.Setup(f => f.GetDefaultAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        return new PdfRenderService(new Mock<IDbConnection>().Object, NullLogger<PdfRenderService>.Instance, forms.Object);
    }

    /// <summary>전표 [인쇄] 문 꼴 — 예시 자료 · 인쇄 문 표지 참 · 설정 미리보기 아님.</summary>
    private static PdfPreviewOverrides PrintDoor() => new()
    {
        UseSampleData = true,
        NoCoordsImageOnly = true,
        IsFormDesignPreview = false,
    };

    private sealed record Paper(int Pages, int Images, int Glyphs, string Text)
    {
        public bool HasPageNumber => Text.Contains("Page", StringComparison.Ordinal);
    }

    private static Paper Read(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var pages = doc.GetPages().ToList();
        return new Paper(
            pages.Count,
            pages.Sum(p => p.GetImages().Count()),
            pages.Sum(p => p.Letters.Count),
            string.Concat(pages.Select(p => p.Text)));
    }

    private static string Describe(Paper p) =>
        $"쪽 {p.Pages} · 그림 {p.Images} · 글리프 {p.Glyphs} · 쪽번호 {(p.HasPageNumber ? "있음" : "없음")}";

    private static MethodInfo Judge =>
        typeof(PdfRenderService).GetMethod("IsNoCoordsImageOnly", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("IsNoCoordsImageOnly 가 없다 — §59 판정 자리가 사라졌다.");

    private static object TemplateInfo(FormTemplateDto dto) =>
        (typeof(PdfRenderService).GetMethod("ToTemplateInfo", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ToTemplateInfo 가 없다 — 배선이 바뀌었다."))
        .Invoke(MakeService(null), new object?[] { dto })!;

    // ═════════════════════════════════════════════════════════════
    // U1 · U2 — 표
    // ═════════════════════════════════════════════════════════════

    [Theory(DisplayName = "§59 고침 1 U1 🔴 FormCoordsStored.IsEmpty = §2-1 표 그대로(E1~E5 참 · E6~E11 거짓)")]
    [MemberData(nameof(Table))]
    public void U1_IsEmpty_Table(string label, string? stored, bool expected)
    {
        Assert.True(FormCoordsStored.IsEmpty(stored) == expected,
            $"{label}: IsEmpty 가 {(expected ? "참" : "거짓")} 이어야 한다 — 「저장된 칸 0」 뜻이 바뀌었다.");
    }

    [Theory(DisplayName = "§59 고침 1 U2 🔴 K3 차림 + 표 각 줄 → ToTemplateInfo → IsNoCoordsImageOnly = U1 과 같은 답")]
    [MemberData(nameof(Table))]
    public void U2_Judge_Follows_StoredEmpty(string label, string? stored, bool expected)
    {
        // isYTemplate = 참(빈 A4 · 찍기 켬 · 그림 경로 · 그림 읽힘) — 판정 칸 ⑤ 만 표가 바꾼다.
        var info = TemplateInfo(K3("gate59f1-judge.png", stored));
        var actual = (bool)Judge.Invoke(null, new object?[] { "quotation", PrintDoor(), true, info })!;
        Assert.True(actual == expected,
            $"{label}: 인쇄 문 그림만 판정이 {(expected ? "참" : "거짓")} 이어야 한다 — 판정이 「저장된 칸 0」 을 안 본다.");
    }

    // ═════════════════════════════════════════════════════════════
    // U3 — 모두 무효 칸(E9) = 종전 기본 배치 + 데이터
    // ═════════════════════════════════════════════════════════════

    [Fact(DisplayName = "§59 고침 1 U3 🔴 E9(종이 밖 1칸) 인쇄 문 = 그림만 아님 — 글리프 > 0 · 「Page」 있음 · 그림 ≥1")]
    public async Task U3_OffPaperCoord_PrintsData()
    {
        var png = WriteValidPng();
        try
        {
            var (bytes, _) = await MakeService(K3(png, OffPaperJson))
                .RenderDocumentAsync(Tenant, "quotation", "gate59f1-doc", PrintDoor());
            var paper = Read(bytes);
            Assert.True(paper.Glyphs > 0 && paper.HasPageNumber && paper.Images >= 1,
                $"모두 무효 칸 양식이 그림만으로 나갔다(데이터 빠짐 · #20) — {Describe(paper)}");
        }
        finally { Remove(png); }
    }

    // ═════════════════════════════════════════════════════════════
    // U4 · U5 — 메일 · 팩스 음성
    // ═════════════════════════════════════════════════════════════

    [Fact(DisplayName = "§59 고침 1 U4 🔴 K3(E1) 차림을 메일 · 팩스 판정 입력(인쇄 문 표지 없음)으로 그리면 데이터 있음")]
    public async Task U4_MailFaxInput_K3_PrintsData()
    {
        var png = WriteValidPng();
        try
        {
            // 메일 · 팩스는 4인자 판을 부른다 → 인쇄 문 표지(NoCoordsImageOnly)가 서지 않는다.
            //   4인자 판은 실제 전표를 DB 에서 읽어 이 시험(DB 없음)에서 못 그린다 ⇒ 같은 판정 입력(표지 없음) + 예시 자료로 그린다.
            var (bytes, _) = await MakeService(K3(png, null))
                .RenderDocumentAsync(Tenant, "quotation", "gate59f1-doc", new PdfPreviewOverrides { UseSampleData = true });
            var paper = Read(bytes);
            Assert.True(paper.Glyphs > 0, $"메일 · 팩스 꼴 종이에서 데이터가 빠졌다 — {Describe(paper)}");
        }
        finally { Remove(png); }
    }

    private sealed class RecordingPdf : IPdfRenderService
    {
        public int FourArgCalls { get; private set; }
        public int FiveArgCalls { get; private set; }

        public Task<(byte[] Bytes, string FileName)> RenderDocumentAsync(string tenantId, string documentType,
            string documentId, CancellationToken ct = default)
        {
            FourArgCalls++;
            return Task.FromResult((new byte[] { 1 }, "gate59f1.pdf"));
        }

        public Task<(byte[] Bytes, string FileName)> RenderDocumentAsync(string tenantId, string documentType,
            string documentId, PdfPreviewOverrides? overrides, CancellationToken ct = default)
        {
            FiveArgCalls++;
            return Task.FromResult((new byte[] { 1 }, "gate59f1.pdf"));
        }

        public Task<bool> IsNoCoordsImageOnlyAsync(string tenantId, string documentType, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    [Fact(DisplayName = "§59 고침 1 U5 🔴 메일 미리보기 문(EmailController.PreviewPdf) = 4인자 판 1회 · 인쇄 문 표지 판 0회")]
    public async Task U5_EmailControllerPreview_UsesFourArg()
    {
        var pdf = new RecordingPdf();
        var controller = new EmailController(new Mock<IEmailService>().Object, pdf);
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = Tenant;
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        var result = await controller.PreviewPdf("quotation", "gate59f1-doc", CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        Assert.Equal(1, pdf.FourArgCalls);
        Assert.Equal(0, pdf.FiveArgCalls);
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "HitPan.Application")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    [Theory(DisplayName = "§59 고침 1 U5 🔴 메일 · 팩스 보내기는 4인자 판을 부르고 인쇄 문 표지를 안 세운다(글자 대조)")]
    [InlineData("HitPan.Application/Services/EmailService.cs", "_pdf.RenderDocumentAsync(tenantId, req.DocumentType, req.DocumentId, ct)")]
    [InlineData("HitPan.Application/Services/FaxService.cs", "_pdf.RenderDocumentAsync(tenantId, req.DocumentType, req.DocumentId, ct)")]
    [InlineData("HitPan.API/Controllers/EmailController.cs", "_pdf.RenderDocumentAsync(TenantId!, documentType, documentId, ct)")]
    public void U5_MailFax_FourArgCall_NoPrintDoorFlag(string relative, string fourArgCall)
    {
        // 🔴 보내기 몸통은 렌더 전에 SMTP · 팩스 설정을 DB 에서 읽는다 — DB 없는 이 시험에서는 실제 호출로 렌더까지 못 간다.
        //   그래서 이 둘은 글자 대조로 잰다(개발명세서 §6 · PM 확인 요청). 미리보기 문은 위 시험이 실제 호출로 잰다.
        var code = RepoFile(new[] { "src" }.Concat(relative.Split('/')).ToArray());
        Assert.Contains(fourArgCall, code, StringComparison.Ordinal);
        Assert.DoesNotContain("NoCoordsImageOnly", code, StringComparison.Ordinal);
    }
}
