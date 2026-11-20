using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HitPan.Web.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261120 W-G1-51 갈래 「옛 /pdf」(대표 §76 · PM 결재 §81) — 전표 5화면 「PDF」 단추가
/// <b>[인쇄] 미리보기와 같은 전표 인쇄 문</b>을 부르는지 게이트.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>잰다</b>: 「PDF」 단추가 쓰는 <c>RenderedPdfDownload</c> 를 <b>실제로 불러</b> 가짜 처리기가 받은 요청을 본다
///   (O1 요청 꼴 · O2 이름 · O3 실패 · O4 안내 문). 기대 = 서버 문 계약(<c>DocumentController</c> 전표 인쇄 문 ·
///   안내 문)에서 온다 — 제품 식을 베끼지 않는다.
/// </para>
/// <para>
/// 🔴 <b>O6 는 보조</b> — 5화면 파일 글자로 배선만 대조한다. 글자 일치는 동작 증거가 아니다.
/// </para>
/// <para>
/// 🔴 <b>못 잰다</b>: 브라우저 내려받기 사건 · 받은 PDF 가 [인쇄] 종이와 같은지 · K3 상자를 사람이 읽고 고르는지
///   — 갈래 D 실측 · 장치 Z15-4(내려받기 사건) 몫.
/// </para>
/// <para>
/// 🔴 <b>음성 대조(O5)</b>: <c>FetchAsync</c> 요청 줄을 옛 <c>GET …/{id}/pdf</c> 로 바꾸면 O1 6건이 빨개져야 한다
///   (개발명세서 1-5 · 방법 기록).
/// </para>
/// </remarks>
public sealed class LegacyPdfButtonRenderRouteGateTests
{
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n% gate O2\n%%EOF");

    // ═══════════════ 대역 처리기 — 실제 요청을 센다 ═══════════════

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Method, Uri Uri, string? Body)> Calls { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.Method.Method, request.RequestUri!, body));
            return respond(request);
        }
    }

    private static HttpClient Client(HttpMessageHandler h) => new(h) { BaseAddress = new Uri("http://localhost/") };

    private static HttpResponseMessage Pdf(string? contentDisposition)
    {
        var content = new ByteArrayContent(PdfBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        if (contentDisposition is not null)
        {
            Assert.True(content.Headers.TryAddWithoutValidation("Content-Disposition", contentDisposition));
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // ═══════════════ O1 — 6키 × 요청 꼴 ═══════════════

    [Theory(DisplayName = "O1 「PDF」 = 전표 인쇄 문 — POST …/{id}/preview-pdf 1건 · 옵션 null · 내려받기 표 0 · 옛 /pdf 0")]
    [InlineData("quotation")]
    [InlineData("delivery")]
    [InlineData("purchase-order")]
    [InlineData("purchase-receipt")]
    [InlineData("return")]
    [InlineData("sales-return")]
    public async Task O1_FetchHitsPreviewPdfOnly(string key)
    {
        var h = new FakeHandler(_ => Pdf("inline"));
        var got = await new RenderedPdfDownload(Client(h)).FetchAsync(key, "doc-123", CancellationToken.None);

        Assert.True(got.Ok);
        var call = Assert.Single(h.Calls);
        Assert.Equal("POST", call.Method);
        Assert.Equal($"/api/documents/{key}/doc-123/preview-pdf", call.Uri.AbsolutePath);
        Assert.DoesNotContain("download-token", call.Uri.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(h.Calls, c => c.Uri.AbsolutePath.EndsWith("/pdf", StringComparison.Ordinal));

        // 본문 = { Options: null } — 서버는 대소문자 무관으로 받는다(웹 기본값 직렬화면 선 위 글자는 "options").
        Assert.False(string.IsNullOrWhiteSpace(call.Body));
        using var doc = JsonDocument.Parse(call.Body!);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        var options = doc.RootElement.EnumerateObject()
            .Where(p => string.Equals(p.Name, "Options", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var only = Assert.Single(options);
        Assert.Equal(JsonValueKind.Null, only.Value.ValueKind);
    }

    // ═══════════════ O2 — 200 + 바이트 · 이름 ═══════════════

    [Fact(DisplayName = "O2ⓐ attachment + filename* → Ok · 바이트 같음 · 이름 = filename*(디코딩)")]
    public async Task O2a_AttachmentWithFileNameStar()
    {
        var name = "견적서_Q-0001.pdf";
        var h = new FakeHandler(_ => Pdf($"attachment; filename=\"x.pdf\"; filename*=UTF-8''{Uri.EscapeDataString(name)}"));
        var got = await new RenderedPdfDownload(Client(h)).FetchAsync("quotation", "q-1", CancellationToken.None);

        Assert.True(got.Ok);
        Assert.Equal(200, got.StatusCode);
        Assert.Equal(PdfBytes, got.Bytes);
        Assert.Equal(name, got.FileName);
        Assert.Null(got.Message);
    }

    [Fact(DisplayName = "O2ⓐ' attachment + filename 만 → 이름 = filename(따옴표 · 인코딩 벗김)")]
    public async Task O2a2_AttachmentWithFileNameOnly()
    {
        var h = new FakeHandler(_ =>
        {
            var resp = Pdf(null);
            // 한글 이름을 filename 하나로만 실을 때의 유효한 꼴(형식 지정자가 인코딩) — 받는 쪽이 벗겨야 한다.
            resp.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "거래명세서_D-7.pdf" };
            return resp;
        });
        var got = await new RenderedPdfDownload(Client(h)).FetchAsync("delivery", "d-7", CancellationToken.None);

        Assert.True(got.Ok);
        Assert.Equal(PdfBytes, got.Bytes);
        Assert.Equal("거래명세서_D-7.pdf", got.FileName);
    }

    [Fact(DisplayName = "O2ⓑ inline 만 → Ok · 바이트 같음 · 이름 null(화면이 창 제목으로 짓는다)")]
    public async Task O2b_InlineOnly()
    {
        var h = new FakeHandler(_ => Pdf("inline"));
        var got = await new RenderedPdfDownload(Client(h)).FetchAsync("purchase-order", "p-1", CancellationToken.None);

        Assert.True(got.Ok);
        Assert.Equal(PdfBytes, got.Bytes);
        Assert.Null(got.FileName);
    }

    // ═══════════════ O3 — 실패 ═══════════════

    [Fact(DisplayName = "O3 404 + 사람 말 → Ok 거짓 · 그 글자 그대로 · 404")]
    public async Task O3_NotFoundCarriesServerMessage()
    {
        var h = new FakeHandler(_ => Json(HttpStatusCode.NotFound, "{\"message\":\"저장된 전표가 아닙니다\"}"));
        var got = await new RenderedPdfDownload(Client(h)).FetchAsync("return", "r-1", CancellationToken.None);

        Assert.False(got.Ok);
        Assert.Equal("저장된 전표가 아닙니다", got.Message);
        Assert.Equal(404, got.StatusCode);
        Assert.Empty(got.Bytes);
    }

    [Fact(DisplayName = "O3 500 본문 없음 → Ok 거짓 · 말 null · 500")]
    public async Task O3_ServerErrorWithoutBody()
    {
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var got = await new RenderedPdfDownload(Client(h)).FetchAsync("sales-return", "s-1", CancellationToken.None);

        Assert.False(got.Ok);
        Assert.Null(got.Message);
        Assert.Equal(500, got.StatusCode);
    }

    [Fact(DisplayName = "O3 연결 예외 → 밖으로 안 나감 · Ok 거짓 · 말 null · 0")]
    public async Task O3_TransportExceptionIsSwallowedWithResult()
    {
        var h = new FakeHandler(_ => throw new HttpRequestException("끊김"));
        var got = await new RenderedPdfDownload(Client(h)).FetchAsync("purchase-receipt", "m-1", CancellationToken.None);

        Assert.False(got.Ok);
        Assert.Null(got.Message);
        Assert.Equal(0, got.StatusCode);
    }

    // ═══════════════ O4 — 안내 문 ═══════════════

    [Theory(DisplayName = "O4 안내 문 — 참 · 거짓 · 500 → 거짓 / GET …/print-notice 1건")]
    [InlineData(200, "{\"noCoordsImageOnly\":true}", true)]
    [InlineData(200, "{\"noCoordsImageOnly\":false}", false)]
    [InlineData(500, "", false)]
    public async Task O4_NeedsNotice(int code, string body, bool expected)
    {
        var h = new FakeHandler(_ => Json((HttpStatusCode)code, body));
        var got = await new RenderedPdfDownload(Client(h)).NeedsNoticeAsync("delivery", CancellationToken.None);

        Assert.Equal(expected, got);
        var call = Assert.Single(h.Calls);
        Assert.Equal("GET", call.Method);
        Assert.Equal("/api/documents/delivery/print-notice", call.Uri.AbsolutePath);
    }

    [Fact(DisplayName = "O4 안내 문 예외 → 거짓 · 밖으로 안 나감(받기를 막지 않는다 · #20)")]
    public async Task O4_NeedsNoticeExceptionIsFalse()
    {
        var h = new FakeHandler(_ => throw new HttpRequestException("끊김"));
        var got = await new RenderedPdfDownload(Client(h)).NeedsNoticeAsync("quotation", CancellationToken.None);

        Assert.False(got);
    }

    // ═══════════════ O6 — 배선 대조(보조 · 글자) ═══════════════

    private static string WebRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "src", "HitPan.Web")))
        {
            d = d.Parent;
        }
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "src", "HitPan.Web");
    }

    /// <summary>주석 줄(<c>//</c> 로 시작)을 뺀다 — 종전 줄을 주석으로 남겼기 때문(#1).</summary>
    private static string WithoutLineComments(string text) =>
        string.Join('\n', text.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [Theory(DisplayName = "O6 (보조) 5화면 「PDF」 키 모음 = 같은 화면 [인쇄] 키 모음 · 옛 DocService.DownloadPdfAsync 0")]
    [InlineData("Pages/Sales/QuotationPage.razor.cs", "quotation")]
    [InlineData("Pages/Sales/DeliveryPage.razor", "delivery,sales-return")]
    [InlineData("Pages/Purchase/PurchaseOrderPage.razor.cs", "purchase-order")]
    [InlineData("Pages/Purchase/PurchaseReceiptPage.razor.cs", "purchase-receipt")]
    [InlineData("Pages/Purchase/ReturnPage.razor.cs", "return,sales-return")]
    public void O6_PdfKeysMatchPrintKeys(string relPath, string expectedCsv)
    {
        var path = Path.Combine(WebRoot(), relPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"화면 파일이 없다: {relPath}");
        var raw = File.ReadAllText(path);
        var code = WithoutLineComments(raw);

        Assert.False(Regex.IsMatch(code, @"DocService\.DownloadPdfAsync\("), $"옛 DocService.DownloadPdfAsync 가 남았다: {relPath}");

        var pdfKeys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(code,
                     @"PrintPreview\.DownloadPdfAsync\(\s*(?:""([a-z0-9\-_]+)""|([A-Za-z_][A-Za-z0-9_]*))"))
        {
            if (m.Groups[1].Success)
            {
                pdfKeys.Add(m.Groups[1].Value);
                continue;
            }

            // 첫 인자가 이름이면 같은 파일의 상수 값을 읽는다 — 못 읽으면 빨강이 되도록 표시를 넣는다.
            var name = m.Groups[2].Value;
            var c = Regex.Match(code, @"const\s+string\s+" + Regex.Escape(name) + @"\s*=\s*""([a-z0-9\-_]+)""");
            pdfKeys.Add(c.Success ? c.Groups[1].Value : $"(못 읽음:{name})");
        }

        var printKeys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(code, @"PrintPreview\.ShowAsync\(\s*""([a-z0-9\-_]+)"""))
        {
            printKeys.Add(m.Groups[1].Value);
        }

        var expected = new SortedSet<string>(expectedCsv.Split(','), StringComparer.Ordinal);
        Assert.NotEmpty(pdfKeys);
        Assert.NotEmpty(printKeys);
        Assert.Equal(printKeys, pdfKeys);
        Assert.Equal(expected, pdfKeys);
    }
}
