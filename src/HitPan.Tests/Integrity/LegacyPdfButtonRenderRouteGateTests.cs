using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HitPan.API.Controllers;
using HitPan.Application.Interfaces;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

    // 종전(#1 · 고침 1 §86 ② 로 아래 WithoutComments — 주석 꼴 셋 — 로 바뀜):
    // /// <summary>주석 줄(<c>//</c> 로 시작)을 뺀다 — 종전 줄을 주석으로 남겼기 때문(#1).</summary>
    // private static string WithoutLineComments(string text) =>
    //     string.Join('\n', text.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

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
        // 종전(#1): var code = WithoutLineComments(raw);
        // 🔴 고침 1 §86 ② — 주석 꼴 셋(// · /* */ · @* *@)을 걷고 셈 · 상수 읽기도 걷은 글자에서(주석 속 옛 값 0).
        var code = WithoutComments(raw);

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

    // ════════════════════════════════════════════════════════════════════════
    // 🔴 고침 1 (결재 기록 §86 PM 판단) — ① 흐름 실제 호출 · 처리부별 쌍 / ② 주석 꼴 셋 / ③ 서버 안내 문 묶음 / P3 잠금
    // ════════════════════════════════════════════════════════════════════════

    // ═══════════════ 흐름 대역 — 요청 · 화면 손잡이를 센다 ═══════════════

    /// <summary>경로 · 방법을 세는 대역 처리기 — 응답은 시험마다(비동기 가능 · 붙잡아 두기용).</summary>
    private sealed class FlowHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly List<(string Method, string Path)> _calls = new();

        public FlowHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this(r => Task.FromResult(respond(r)))
        {
        }

        public List<(string Method, string Path)> Calls
        {
            get { lock (_gate) return _calls.ToList(); }
        }

        public int Count(string method, Func<string, bool> path)
        {
            lock (_gate) return _calls.Count(c => c.Method == method && path(c.Path));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (_gate) _calls.Add((request.Method.Method, request.RequestUri!.AbsolutePath));
            return respond(request);
        }
    }

    private static Func<string, bool> EndsWith(string tail) => p => p.EndsWith(tail, StringComparison.Ordinal);

    /// <summary>화면 손잡이 대역 — 상자 · 스낵바 · 저장을 센다.</summary>
    private sealed class UiLog
    {
        private readonly object _gate = new();
        public bool ConfirmAnswer { get; set; }
        public Exception? SaveThrows { get; set; }
        public int Confirms;
        public List<string> Warns { get; } = new();
        public List<string> Infos { get; } = new();
        public List<string> Errors { get; } = new();
        public List<(string Name, byte[] Bytes)> Saves { get; } = new();

        public PdfDownloadUi Ui => new()
        {
            ConfirmImageOnlyAsync = () =>
            {
                Interlocked.Increment(ref Confirms);
                return Task.FromResult(ConfirmAnswer);
            },
            Warn = m => { lock (_gate) Warns.Add(m); },
            Info = m => { lock (_gate) Infos.Add(m); },
            Error = m => { lock (_gate) Errors.Add(m); },
            SaveAsync = (name, bytes) =>
            {
                if (SaveThrows is not null) throw SaveThrows;
                lock (_gate) Saves.Add((name, bytes));
                return Task.CompletedTask;
            },
        };
    }

    private static HttpResponseMessage Notice(bool says) =>
        Json(HttpStatusCode.OK, says ? "{\"noCoordsImageOnly\":true}" : "{\"noCoordsImageOnly\":false}");

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    // ═══════════════ O7 — ① 흐름(실제 DownloadPdfAsync) ═══════════════

    [Theory(DisplayName = "O7 ① 흐름(실제 DownloadPdfAsync) — 안내 거짓 · GET print-notice 1 → POST preview-pdf 1 · 상자 0 · 저장 1 = {title}.pdf (§86)")]
    [InlineData("quotation", "견적서")]
    [InlineData("delivery", "거래명세서")]
    [InlineData("purchase-order", "발주서")]
    [InlineData("purchase-receipt", "매입명세서")]
    [InlineData("return", "반품서")]
    [InlineData("sales-return", "판매반품")]
    public async Task O7_FlowAsksNoticeThenFetchesThenSaves(string key, string title)
    {
        var h = new FlowHandler(r => r.Method == HttpMethod.Get ? Notice(false) : Pdf("inline"));
        var ui = new UiLog();

        await new RenderedPdfDownload(Client(h)).DownloadPdfAsync(key, "doc-1", title, ui.Ui);

        Assert.Equal(
            new[] { ("GET", $"/api/documents/{key}/print-notice"), ("POST", $"/api/documents/{key}/doc-1/preview-pdf") },
            h.Calls);
        Assert.Equal(0, ui.Confirms);
        var save = Assert.Single(ui.Saves);
        Assert.Equal($"{title}.pdf", save.Name);
        Assert.Equal(PdfBytes, save.Bytes);
        Assert.Empty(ui.Warns);
        Assert.Empty(ui.Infos);
        Assert.Empty(ui.Errors);
    }

    [Fact(DisplayName = "O7 ① 흐름 — 서버가 이름을 주면 그 이름으로 저장(filename*)")]
    public async Task O7_FlowSavesWithServerName()
    {
        var name = "견적서_Q-0001.pdf";
        var h = new FlowHandler(r => r.Method == HttpMethod.Get
            ? Notice(false)
            : Pdf($"attachment; filename=\"x.pdf\"; filename*=UTF-8''{Uri.EscapeDataString(name)}"));
        var ui = new UiLog();

        await new RenderedPdfDownload(Client(h)).DownloadPdfAsync("quotation", "q-1", "견적서", ui.Ui);

        Assert.Equal(name, Assert.Single(ui.Saves).Name);
    }

    [Theory(DisplayName = "O7 ① 안내 갈래 — 참+취소 = POST 0 · 참+그래도 받기 = POST 1 · 거짓 = 상자 0 · 안내 문 500 = 상자 0 · POST 1 (§86 · #20)")]
    [InlineData(200, true, false, 1, 0)]
    [InlineData(200, true, true, 1, 1)]
    [InlineData(200, false, false, 0, 1)]
    [InlineData(500, true, false, 0, 1)]
    public async Task O7_NoticeBranches(int noticeCode, bool noticeSays, bool answer, int confirms, int posts)
    {
        var h = new FlowHandler(r => r.Method != HttpMethod.Get
            ? Pdf("inline")
            : noticeCode == 200 ? Notice(noticeSays) : Json((HttpStatusCode)noticeCode, ""));
        var ui = new UiLog { ConfirmAnswer = answer };

        await new RenderedPdfDownload(Client(h)).DownloadPdfAsync("delivery", "d-1", "거래명세서", ui.Ui);

        Assert.Equal(1, h.Count("GET", EndsWith("/print-notice")));
        Assert.Equal(confirms, ui.Confirms);
        Assert.Equal(posts, h.Count("POST", EndsWith("/preview-pdf")));
        Assert.Equal(posts, ui.Saves.Count);
        Assert.Empty(ui.Errors);
    }

    [Theory(DisplayName = "O7 ① 빈 id — 요청 0 · 상자 0 · 경고 1(ShowAsync 와 같은 글자)")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task O7_EmptyIdWarnsWithoutRequest(string? id)
    {
        var h = new FlowHandler(_ => Pdf("inline"));
        var ui = new UiLog { ConfirmAnswer = true };

        await new RenderedPdfDownload(Client(h)).DownloadPdfAsync("quotation", id, "견적서", ui.Ui);

        Assert.Empty(h.Calls);
        Assert.Equal(0, ui.Confirms);
        Assert.Equal(new[] { "먼저 전표를 저장하거나 목록에서 불러오십시오." }, ui.Warns);
        Assert.Empty(ui.Saves);
    }

    [Theory(DisplayName = "O7 ① 받기 실패 — 서버 말 그대로 · 말 없으면 「PDF 를 만들지 못했습니다. (코드)」 · 저장 0")]
    [InlineData(404, "{\"message\":\"저장된 전표가 아닙니다\"}", "저장된 전표가 아닙니다")]
    [InlineData(500, "", "PDF 를 만들지 못했습니다. (500)")]
    public async Task O7_FailureShowsError(int code, string body, string expected)
    {
        var h = new FlowHandler(r => r.Method == HttpMethod.Get ? Notice(false) : Json((HttpStatusCode)code, body));
        var ui = new UiLog();

        await new RenderedPdfDownload(Client(h)).DownloadPdfAsync("return", "r-1", "반품서", ui.Ui);

        Assert.Equal(new[] { expected }, ui.Errors);
        Assert.Empty(ui.Saves);
    }

    [Fact(DisplayName = "O7 ① 연결 예외 — 「PDF 를 만들지 못했습니다.」(코드 없음) · 밖으로 안 나감 · 저장 0")]
    public async Task O7_TransportExceptionShowsError()
    {
        var h = new FlowHandler(r => r.Method == HttpMethod.Get ? Notice(false) : throw new HttpRequestException("끊김"));
        var ui = new UiLog();

        await new RenderedPdfDownload(Client(h)).DownloadPdfAsync("purchase-receipt", "m-1", "매입명세서", ui.Ui);

        Assert.Equal(new[] { "PDF 를 만들지 못했습니다." }, ui.Errors);
        Assert.Empty(ui.Saves);
    }

    [Fact(DisplayName = "O7 ① 저장 예외 — 「PDF 를 저장하지 못했습니다.」 · 밖으로 안 나감")]
    public async Task O7_SaveExceptionShowsError()
    {
        var h = new FlowHandler(r => r.Method == HttpMethod.Get ? Notice(false) : Pdf("inline"));
        var ui = new UiLog { SaveThrows = new InvalidOperationException("JS 실패 대역") };

        var ex = await Record.ExceptionAsync(() =>
            new RenderedPdfDownload(Client(h)).DownloadPdfAsync("purchase-order", "p-1", "발주서", ui.Ui));

        Assert.Null(ex);
        Assert.Equal(new[] { "PDF 를 저장하지 못했습니다." }, ui.Errors);
    }

    // ═══════════════ O8 — P3 두 번 누름 잠금 ═══════════════

    [Fact(DisplayName = "O8 P3 두 번 누름 잠금 — 첫 POST 붙잡은 채 둘째 누름 = 요청 0 · Info 1 · 첫째 끝나면 저장 1 · 셋째 = 풀림 (§86)")]
    public async Task O8_SecondPressWhileBusyMakesNoRequest()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstPostSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var posts = 0;
        var h = new FlowHandler(async r =>
        {
            if (r.Method == HttpMethod.Get) return Notice(false);
            if (Interlocked.Increment(ref posts) == 1)
            {
                firstPostSeen.TrySetResult();
                await release.Task;
            }
            return Pdf("inline");
        });
        var ui = new UiLog();
        var pdf = new RenderedPdfDownload(Client(h));

        try
        {
            var first = pdf.DownloadPdfAsync("return", "r-1", "반품서", ui.Ui);
            await firstPostSeen.Task.WaitAsync(Wait);

            await pdf.DownloadPdfAsync("return", "r-1", "반품서", ui.Ui).WaitAsync(Wait);

            Assert.Equal(1, h.Count("POST", EndsWith("/preview-pdf")));
            Assert.Equal(1, h.Count("GET", EndsWith("/print-notice")));
            Assert.Equal(new[] { "PDF 를 만드는 중입니다. 잠시만 기다려 주십시오." }, ui.Infos);
            Assert.Empty(ui.Saves);

            release.TrySetResult();
            await first.WaitAsync(Wait);
            Assert.Single(ui.Saves);

            await pdf.DownloadPdfAsync("return", "r-1", "반품서", ui.Ui).WaitAsync(Wait);
            Assert.Equal(2, h.Count("POST", EndsWith("/preview-pdf")));
            Assert.Equal(2, ui.Saves.Count);
            Assert.Single(ui.Infos);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact(DisplayName = "O8 P3 잠금 — 「취소」 뒤에 풀린다(다시 누르면 받음)")]
    public async Task O8_LockReleasedAfterCancel()
    {
        var h = new FlowHandler(r => r.Method == HttpMethod.Get ? Notice(true) : Pdf("inline"));
        var ui = new UiLog { ConfirmAnswer = false };
        var pdf = new RenderedPdfDownload(Client(h));

        await pdf.DownloadPdfAsync("sales-return", "s-1", "판매반품", ui.Ui).WaitAsync(Wait);
        Assert.Equal(0, h.Count("POST", EndsWith("/preview-pdf")));

        ui.ConfirmAnswer = true;
        await pdf.DownloadPdfAsync("sales-return", "s-1", "판매반품", ui.Ui).WaitAsync(Wait);

        Assert.Equal(1, h.Count("POST", EndsWith("/preview-pdf")));
        Assert.Single(ui.Saves);
        Assert.Empty(ui.Infos);
    }

    // ═══════════════ 글자 걷기 · 처리부 읽기 도구(② · ①) ═══════════════

    /// <summary>
    /// 🔴 [고침 1 · §86 ②] 주석 꼴 셋(<c>//</c> 줄 끝 · <c>/* */</c> · <c>@* *@</c>)을 걷는다 — 글자 · 문자 상수 안은 그대로 ·
    /// 걷은 자리는 빈칸 하나(줄 바꿈은 남김).
    /// </summary>
    private static string WithoutComments(string text)
    {
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var d = i + 1 < text.Length ? text[i + 1] : '\0';
            if ((c == '@' || c == '/') && d == '*')
            {
                var close = text.IndexOf(c == '@' ? "*@" : "*/", i + 2, StringComparison.Ordinal);
                var stop = close < 0 ? text.Length : close + 2;
                Blank(text, i, stop, sb);
                i = stop;
                continue;
            }
            if (c == '/' && d == '/')
            {
                var nl = text.IndexOf('\n', i);
                var stop = nl < 0 ? text.Length : nl;
                Blank(text, i, stop, sb);
                i = stop;
                continue;
            }
            var lit = LiteralEnd(text, i);
            if (lit > i)
            {
                sb.Append(text, i, lit - i);
                i = lit;
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    private static void Blank(string text, int from, int to, StringBuilder sb)
    {
        sb.Append(' ');
        for (var k = from; k < to; k++)
        {
            if (text[k] == '\n') sb.Append('\n');
        }
    }

    /// <summary>
    /// i 에서 글자 상수(<c>"…"</c> · <c>$"…"</c> · <c>@"…"</c> · <c>"""…"""</c>) 또는 짧은 문자 상수(<c>'x'</c> · <c>'\x'</c>)가
    /// 시작하면 그 끝 다음 자리 · 아니면 i. 보통 글자 상수는 줄 끝에서 멈춘다(Razor 글 속 따옴표 하나가 뒤를 삼키지 않게).
    /// </summary>
    private static int LiteralEnd(string text, int i)
    {
        var n = text.Length;
        if (text[i] == '\'')
        {
            if (i + 2 < n && text[i + 1] != '\\' && text[i + 1] != '\n' && text[i + 2] == '\'') return i + 3;
            if (i + 3 < n && text[i + 1] == '\\')
            {
                var close = text.IndexOf('\'', i + 3);
                if (close > 0 && close - i <= 8) return close + 1;
            }
            return i;
        }

        var j = i;
        var verbatim = false;
        while (j < n && (text[j] == '$' || text[j] == '@'))
        {
            if (text[j] == '@') verbatim = true;
            j++;
        }
        if (j >= n || text[j] != '"') return i;

        if (j + 2 < n && text[j + 1] == '"' && text[j + 2] == '"')
        {
            var close = text.IndexOf("\"\"\"", j + 3, StringComparison.Ordinal);
            return close < 0 ? n : close + 3;
        }

        for (var k = j + 1; k < n; k++)
        {
            var ch = text[k];
            if (verbatim)
            {
                if (ch != '"') continue;
                if (k + 1 < n && text[k + 1] == '"') { k++; continue; }
                return k + 1;
            }
            if (ch == '\\') { k++; continue; }
            if (ch == '"') return k + 1;
            if (ch == '\n') return k;
        }
        return n;
    }

    /// <summary>여는 괄호 자리 → 짝 닫는 괄호 자리(글자 상수 안은 건너뜀) · 없으면 −1.</summary>
    private static int MatchClose(string code, int open)
    {
        var o = code[open];
        var cl = o == '(' ? ')' : o == '{' ? '}' : ']';
        var depth = 0;
        for (var k = open; k < code.Length; k++)
        {
            var lit = LiteralEnd(code, k);
            if (lit > k) { k = lit - 1; continue; }
            if (code[k] == o) depth++;
            else if (code[k] == cl && --depth == 0) return k;
        }
        return -1;
    }

    /// <summary>문장 끝(<c>;</c> · 바깥 깊이) 자리 · 없으면 −1.</summary>
    private static int StatementEnd(string code, int from)
    {
        var depth = 0;
        for (var k = from; k < code.Length; k++)
        {
            var lit = LiteralEnd(code, k);
            if (lit > k) { k = lit - 1; continue; }
            var ch = code[k];
            if (ch is '(' or '[' or '{') depth++;
            else if (ch is ')' or ']' or '}') { if (--depth < 0) return k; }
            else if (ch == ';' && depth == 0) return k;
        }
        return -1;
    }

    /// <summary>바깥 깊이 쉼표로 인자를 가른다(글자 상수 안 쉼표는 그대로).</summary>
    private static List<string> SplitTopLevel(string args)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var k = 0; k < args.Length; k++)
        {
            var lit = LiteralEnd(args, k);
            if (lit > k) { k = lit - 1; continue; }
            var ch = args[k];
            if (ch is '(' or '[' or '{') depth++;
            else if (ch is ')' or ']' or '}') depth--;
            else if (ch == ',' && depth == 0)
            {
                parts.Add(args[start..k].Trim());
                start = k + 1;
            }
        }
        if (args.Trim().Length > 0) parts.Add(args[start..].Trim());
        return parts;
    }

    private sealed record MethodSpan(string Name, int Open, int Close);

    /// <summary>메서드 몸통(<c>Task</c> · <c>void</c> 돌림) — 여는 · 닫는 중괄호 자리.</summary>
    private static List<MethodSpan> Methods(string code)
    {
        var list = new List<MethodSpan>();
        foreach (Match m in Regex.Matches(code,
                     @"\b(?:private|public|protected|internal)\s+(?:static\s+)?(?:async\s+)?(?:Task|void)\s+(\w+)\s*\([^()]*\)\s*\{"))
        {
            var open = m.Index + m.Length - 1;
            var close = MatchClose(code, open);
            if (close > open) list.Add(new MethodSpan(m.Groups[1].Value, open, close));
        }
        return list;
    }

    /// <summary>한 번 부름의 (가지 조건 · 종 · id).</summary>
    private sealed record Wire(string Branch, string Type, string Id)
    {
        public override string ToString() => $"{Branch} | {Type} | {Id}";
    }

    /// <summary>
    /// <paramref name="callee"/> 를 부르는 자리마다 (가지 조건 · 종 · id) 와 그 자리를 품은 메서드 이름.
    /// 가지 조건 = 그 자리를 품은 <c>if (…)</c> 글자(겹치면 <c>&amp;&amp;</c>) · 없으면 <c>(그 밖)</c>.
    /// 종 = 첫 인자 글자 · 이름이면 같은 파일 <c>const string</c> 값(정확히 한 곳) · id = 둘째 인자(공백 · <c>?.</c> 맞춤).
    /// </summary>
    private static (List<string> Owners, List<Wire> Wires) WiresOf(string code, string callee)
    {
        var methods = Methods(code);
        var owners = new List<string>();
        var wires = new List<Wire>();
        foreach (Match m in Regex.Matches(code, @"\b" + Regex.Escape(callee) + @"\s*\("))
        {
            var at = m.Index;
            var owner = methods.Where(b => b.Open < at && at < b.Close).OrderByDescending(b => b.Open).FirstOrDefault();
            owners.Add(owner?.Name ?? "(처리부 못 찾음)");

            var open = m.Index + m.Length - 1;
            var close = MatchClose(code, open);
            var args = close > open ? SplitTopLevel(code.Substring(open + 1, close - open - 1)) : new List<string>();
            var type = args.Count > 0 ? ResolveType(code, args[0]) : "(인자 없음)";
            var id = args.Count > 1 ? NormalizeId(args[1]) : "(인자 없음)";
            var branch = owner is null ? "(처리부 못 찾음)" : BranchOf(code, owner, at);
            wires.Add(new Wire(branch, type, id));
        }
        return (owners.Distinct(StringComparer.Ordinal).ToList(), wires);
    }

    private static string ResolveType(string code, string arg)
    {
        var a = arg.Trim();
        var lit = Regex.Match(a, @"^""([a-z0-9\-_]+)""$");
        if (lit.Success) return lit.Groups[1].Value;
        if (!Regex.IsMatch(a, @"^[A-Za-z_][A-Za-z0-9_]*$")) return $"(못 읽음:{a})";

        var defs = Regex.Matches(code, @"\bconst\s+string\s+" + Regex.Escape(a) + @"\s*=\s*""([a-z0-9\-_]+)""\s*;");
        return defs.Count switch
        {
            1 => defs[0].Groups[1].Value,
            0 => $"(못 읽음:{a})",
            _ => $"(두 곳:{a})",
        };
    }

    private static string NormalizeId(string arg) =>
        Regex.Replace(arg, @"\s+", string.Empty).Replace("?.", ".", StringComparison.Ordinal).TrimEnd('!');

    private static string BranchOf(string code, MethodSpan owner, int at)
    {
        var conds = new List<string>();
        var body = code.Substring(owner.Open, owner.Close - owner.Open + 1);
        foreach (Match m in Regex.Matches(body, @"\bif\s*\("))
        {
            var po = owner.Open + m.Index + m.Length - 1;
            var pc = MatchClose(code, po);
            if (pc < 0) continue;
            var k = pc + 1;
            while (k < code.Length && char.IsWhiteSpace(code[k])) k++;
            var to = k < code.Length && code[k] == '{' ? MatchClose(code, k) : StatementEnd(code, k);
            if (to < 0 || at < k || at > to) continue;
            conds.Add(Regex.Replace(code.Substring(po + 1, pc - po - 1), @"\s+", " ").Trim());
        }
        return conds.Count == 0 ? "(그 밖)" : string.Join(" && ", conds);
    }

    private static string ReadWeb(string relPath)
    {
        var path = Path.Combine(WebRoot(), relPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"파일이 없다: {relPath}");
        return File.ReadAllText(path);
    }

    // ═══════════════ O9 — ① 처리부별 (가지 조건 · 종 · id) 쌍 ═══════════════

    [Theory(DisplayName = "O9 ① 처리부별 (가지 조건 · 종 · id) 쌍 — 「PDF」 = 같은 화면 [인쇄] = 기대 · 처리부 각 1 · 옛 DocService 0 (§86)")]
    [InlineData("Pages/Sales/QuotationPage.razor.cs", "(그 밖)|quotation|_draft.Id")]
    [InlineData("Pages/Sales/DeliveryPage.razor", "IsOpenedSalesReturnForPrint|sales-return|_draft.Id;(그 밖)|delivery|_draft.Id")]
    [InlineData("Pages/Purchase/PurchaseOrderPage.razor.cs", "(그 밖)|purchase-order|_draft.Id")]
    [InlineData("Pages/Purchase/PurchaseReceiptPage.razor.cs", "(그 밖)|purchase-receipt|_draft.Id")]
    [InlineData("Pages/Purchase/ReturnPage.razor.cs", "IsLoadedSalesReturnForPrint|sales-return|_draft.Id;(그 밖)|return|_draft.Id")]
    public void O9_HandlerPairsMatchPrint(string relPath, string expected)
    {
        var code = WithoutComments(ReadWeb(relPath));
        Assert.False(Regex.IsMatch(code, @"DocService\.DownloadPdfAsync\("), $"옛 DocService.DownloadPdfAsync 가 남았다: {relPath}");

        var (printOwners, printWires) = WiresOf(code, "PrintPreview.ShowAsync");
        var (pdfOwners, pdfWires) = WiresOf(code, "PrintPreview.DownloadPdfAsync");
        Assert.True(printOwners.Count == 1 && printOwners[0] != "(처리부 못 찾음)",
            $"[인쇄] 처리부가 하나가 아니다({string.Join(", ", printOwners)}): {relPath}");
        Assert.True(pdfOwners.Count == 1 && pdfOwners[0] != "(처리부 못 찾음)",
            $"「PDF」 처리부가 하나가 아니다({string.Join(", ", pdfOwners)}): {relPath}");

        static List<Wire> Sorted(IEnumerable<Wire> ws) => ws.OrderBy(w => w.ToString(), StringComparer.Ordinal).ToList();
        var want = Sorted(expected.Split(';').Select(s => s.Split('|')).Select(p => new Wire(p[0], p[1], p[2])));
        Assert.Equal(want, Sorted(printWires));
        Assert.Equal(want, Sorted(pdfWires));
    }

    [Fact(DisplayName = "O7w (보조 · 글자) DocumentPrintPreview.DownloadPdfAsync = 흐름 한 줄 위임 · 화면 쪽 받기 · 안내 · 저장 직접 호출 0 (§86 ①)")]
    public void O7w_WrapperDelegatesToFlow()
    {
        var code = WithoutComments(ReadWeb("Services/DocumentPrintPreview.cs"));
        var one = Assert.Single(Methods(code), b => b.Name == "DownloadPdfAsync");
        var body = code.Substring(one.Open, one.Close - one.Open + 1);

        Assert.Single(Regex.Matches(body, @"\b_pdf\.DownloadPdfAsync\(\s*docType\s*,\s*docId\s*,\s*title\s*,\s*_ui\s*\)"));
        Assert.DoesNotMatch(@"FetchAsync\(|NeedsNoticeAsync\(|InvokeVoidAsync\(|ShowMessageBoxAsync\(", body);
    }

    // ═══════════════ O10 — ② 주석 꼴 셋 걷기 ═══════════════

    [Theory(DisplayName = "O10 ② 주석 꼴 셋 걷기 — // · /* */ · @* *@ 안 호출 0 · 진짜 호출 · 글자 상수 그대로 (§86)")]
    [InlineData("// await PrintPreview.DownloadPdfAsync(\"x\", id, \"t\");\nawait PrintPreview.DownloadPdfAsync(\"y\", id, \"t\");")]
    [InlineData("await PrintPreview.DownloadPdfAsync(\"y\", id, \"t\"); // await PrintPreview.DownloadPdfAsync(\"x\", id, \"t\");")]
    [InlineData("/* await PrintPreview.DownloadPdfAsync(\"x\", id, \"t\");\n   다음 줄 */ await PrintPreview.DownloadPdfAsync(\"y\", id, \"t\");")]
    [InlineData("@* await PrintPreview.DownloadPdfAsync(\"x\", id, \"t\");\n *@\nawait PrintPreview.DownloadPdfAsync(\"y\", id, \"t\");")]
    [InlineData("var u = \"http://a/*b\"; await PrintPreview.DownloadPdfAsync(\"y\", id, \"t\");")]
    [InlineData("var c = '\"'; // await PrintPreview.DownloadPdfAsync(\"x\", id, \"t\");\nawait PrintPreview.DownloadPdfAsync(\"y\", id, \"t\");")]
    public void O10_StripsThreeCommentForms(string src)
    {
        var code = WithoutComments(src);
        var keys = Regex.Matches(code, @"PrintPreview\.DownloadPdfAsync\(\s*""([a-z\-]+)""")
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.Equal(new[] { "y" }, keys);
        Assert.Equal(src.Count(ch => ch == '\n'), code.Count(ch => ch == '\n'));
        if (src.Contains("\"http://a/*b\"", StringComparison.Ordinal))
        {
            Assert.Contains("\"http://a/*b\"", code, StringComparison.Ordinal);
        }
    }

    // ═══════════════ O11 — ③ 서버 안내 문 응답 형 · 경로 ↔ 화면 사본 ═══════════════

    /// <summary>안내 판정만 돌려주는 렌더 서비스 대역 — 렌더는 부르지 않는다.</summary>
    private sealed class NoticeOnlyPdf(bool says) : IPdfRenderService
    {
        public Task<(byte[] Bytes, string FileName)> RenderDocumentAsync(string tenantId, string documentType,
            string documentId, CancellationToken ct = default)
            => throw new InvalidOperationException("O11 은 렌더를 부르지 않는다");

        public Task<bool> IsNoCoordsImageOnlyAsync(string tenantId, string documentType, CancellationToken ct = default)
            => Task.FromResult(says);
    }

    /// <summary>서버 안내 문 경로 — 컨트롤러 <c>[Route]</c> + <c>PrintNotice</c> 의 <c>[HttpGet]</c> 틀에서 꺼낸다(글자 복제 0).</summary>
    private static string ServerNoticeRoute()
    {
        var cls = typeof(DocumentController).GetCustomAttribute<RouteAttribute>();
        var act = typeof(DocumentController).GetMethod(nameof(DocumentController.PrintNotice))?.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(cls);
        Assert.NotNull(act);
        Assert.False(string.IsNullOrWhiteSpace(act!.Template), "PrintNotice 의 [HttpGet] 틀이 비었다");
        return "/" + cls!.Template.Trim('/') + "/" + act.Template!.Trim('/');
    }

    [Theory(DisplayName = "O11 ③ 서버 안내 문(PrintNotice 응답 형 · 경로) ↔ 화면 사본 묶음 — 참 = 상자 1 · POST 0 / 거짓 = 상자 0 · POST 1 (§86)")]
    [InlineData("quotation", true)]
    [InlineData("delivery", true)]
    [InlineData("purchase-order", true)]
    [InlineData("purchase-receipt", true)]
    [InlineData("return", true)]
    [InlineData("sales-return", true)]
    [InlineData("delivery", false)]
    public async Task O11_ServerNoticeShapeBindsClientCopy(string key, bool serverSays)
    {
        // ⑴ 서버 안내 문을 실제로 부른다(§59 고침 1 합친 머리 · tenant = JWT 자리 HttpContext.Items).
        var controller = new DocumentController(null!, null!, null!, null!, null!, null!, null!, new NoticeOnlyPdf(serverSays));
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = "o11-tenant";
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        var ok = Assert.IsType<OkObjectResult>(await controller.PrintNotice(key));
        Assert.NotNull(ok.Value);

        // ⑵ 선 위 글자 = 서버 기본 JSON 설정(AddControllers 기본 · Mvc JsonOptions)으로 직렬화한 그 값.
        var wire = JsonSerializer.Serialize(ok.Value, ok.Value!.GetType(),
            new Microsoft.AspNetCore.Mvc.JsonOptions().JsonSerializerOptions);

        // ⑶ 서버 틀 경로에서만 그 글자를 준다 — 화면이 다른 경로를 부르면 404(= 안내 없이 받기 · 소리 없이 사라짐).
        var route = ServerNoticeRoute().Replace("{type}", key, StringComparison.Ordinal);
        var h = new FlowHandler(r =>
            r.Method != HttpMethod.Get ? Pdf("inline")
            : r.RequestUri!.AbsolutePath == route ? Json(HttpStatusCode.OK, wire)
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var ui = new UiLog { ConfirmAnswer = false };

        await new RenderedPdfDownload(Client(h)).DownloadPdfAsync(key, "doc-1", "o11", ui.Ui);

        Assert.Equal(1, h.Count("GET", p => p == route));
        Assert.True((serverSays ? 1 : 0) == ui.Confirms,
            $"서버 안내 문 = {wire} 인데 화면 상자 {ui.Confirms}회 — 응답 이름 · 경로가 화면 사본과 어긋났다.");
        Assert.Equal(serverSays ? 0 : 1, h.Count("POST", EndsWith("/preview-pdf")));
    }
}
