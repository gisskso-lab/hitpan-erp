using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HitPan.Web.Services;

/// <summary>
/// 🔴 [2026-11-20 W-G1-51 갈래 「옛 /pdf」 · 대표 §76 · PM 결재 §81] 전표 「PDF」 단추가 받는 PDF 를
/// <b>전표 [인쇄] 미리보기와 같은 문</b>(<c>POST api/documents/{type}/{id}/preview-pdf</c>)에서 받아 오는 한 곳.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜</b> — 종전 「PDF」 는 <c>GET …/{id}/pdf</c>(익명 + 내려받기 표) → 세금계산서 외 <b>옛 렌더러</b>로 가서
///   양식 · 스타일 · 워터마크 · 좌표 · 「그림만」(§59) 을 하나도 타지 않았다. 같은 전표가 [인쇄] 와 「PDF」 에서
///   다른 종이로 나왔다. <b>한 전표 = 렌더러 하나</b>(CTO §2 까닭 ②).
/// </para>
/// <para>
/// 🔴 옵션은 <c>null</c> — 저장된 양식 그대로 그린다(서버가 저장하지 않는 한 장 옵션 자리).
/// 🔴 로그인 세션 <c>HttpClient</c> 그대로 — tenant 는 서버가 JWT 에서만 꺼낸다(#2). 화면은 tenant 를 보내지 않는다.
/// 🔴 화면 쪽 두 번째 판정 0 — 양식 행을 읽거나 좌표를 세지 않는다. 「그림만」 안내 여부는 서버 안내 문에 참/거짓만 묻는다.
/// 🔴 Blazor · MudBlazor · JS 의존 0 — 시험 프로젝트가 이 파일을 링크해 <b>실제로 불러</b> 잰다.
/// </para>
/// </remarks>
public sealed class RenderedPdfDownload
{
    private readonly HttpClient _http;

    // 🔴 [고침 1 · 결재 기록 §86 P3] 두 번 누름 잠금 — 받는 중(안내 묻기 · 상자 · 그리기 · 저장)엔 다시 눌러도 요청 0.
    //   0 = 비었음 · 1 = 받는 중. 단위 = 이 객체 하나(DocumentPrintPreview 하나 · Scoped = WASM 앱 하나).
    private int _busy;

    /// <summary>잠긴 동안 다시 누르면 띄우는 글자 — 조용히 아무 일도 안 일어나면 고객은 고장으로 읽는다.</summary>
    private const string BusyText = "PDF 를 만드는 중입니다. 잠시만 기다려 주십시오.";

    public RenderedPdfDownload(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// 🔴 [고침 1 · 결재 기록 §86 ①] 전표 「PDF」 단추 한 번의 흐름 ①~④ 한 자리 —
    /// <c>DocumentPrintPreview.DownloadPdfAsync</c> 가 화면 손잡이(<paramref name="ui"/>)만 붙여 부른다.
    /// </summary>
    /// <remarks>
    /// 🔴 왜 여기로 옮겼나 — 화면 쪽(<c>DocumentPrintPreview</c>)은 MudBlazor · JS · 미리보기 창에 기대어 시험이 부를 수 없다.
    ///   흐름을 Blazor 의존 0 인 이 파일에 두어 시험이 <b>실제로 불러</b> 요청 · 상자 · 저장을 센다(「취소」 뒤 받기 0 등).
    /// 🔴 흐름 · 글자는 종전 <c>DocumentPrintPreview.DownloadPdfAsync</c> ①~④ 그대로 — 두 번 누름 잠금만 보탰다(§86 P3).
    /// </remarks>
    /// <param name="docType">같은 화면 [인쇄] 단추의 종과 같은 종(핵심 규칙).</param>
    /// <param name="docId">전표 id. 🔴 비어 있으면 받지 않는다 — 그릴 것이 없다.</param>
    /// <param name="title">[인쇄] 창 제목과 같은 이름 — 서버가 파일 이름을 안 주면 <c>{title}.pdf</c>.</param>
    /// <param name="ui">화면 손잡이 — 상자 · 스낵바 · 파일 저장.</param>
    public async Task DownloadPdfAsync(string docType, string? docId, string title, PdfDownloadUi ui)
    {
        if (string.IsNullOrWhiteSpace(docId))
        {
            // 🔴 ShowAsync 와 같은 글자 — 조용히 아무 일도 안 일어나면 고객은 고장으로 읽는다.
            ui.Warn("먼저 전표를 저장하거나 목록에서 불러오십시오.");
            return;
        }

        // 🔴 P3 두 번 누름 잠금 — 첫 누름이 끝나기 전 누름은 요청 · 상자 · 저장 0 + 안내 1줄.
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            ui.Info(BusyText);
            return;
        }

        try
        {
            // ② K3(빈 A4 + 그림 + 칸 0) 이면 받기 전에 알린다 — 받은 PDF 는 거래처로 갈 수 있다.
            if (await NeedsNoticeAsync(docType, CancellationToken.None))
            {
                var go = await ui.ConfirmImageOnlyAsync();
                if (!go) return;
            }

            // ③ 전표 인쇄 문 — 옵션 null = 저장된 양식 그대로.
            var got = await FetchAsync(docType, docId!, CancellationToken.None);
            if (!got.Ok)
            {
                var fallback = got.StatusCode > 0
                    ? $"PDF 를 만들지 못했습니다. ({got.StatusCode})"
                    : "PDF 를 만들지 못했습니다.";
                ui.Error(got.Message ?? fallback);
                return;
            }

            // ④ 받은 바이트를 파일로 — 화면 손잡이(기존 JS 함수 downloadFileFromBytes) · 새 JS 0.
            try
            {
                await ui.SaveAsync(got.FileName ?? $"{title}.pdf", got.Bytes);
            }
            catch (Exception ex)
            {
                ui.Error("PDF 를 저장하지 못했습니다.");
                Console.Error.WriteLine($"[DocPdf] 내려받기 실패 — type={docType}: {ex.Message}");
            }
        }
        finally
        {
            // 🔴 취소 · 실패 · 저장 예외 뒤에도 푼다 — 잠금이 남으면 「PDF」 가 영영 안 눌린다.
            Volatile.Write(ref _busy, 0);
        }
    }

    /// <summary>
    /// 전표 인쇄 문에 PDF 를 그려 달라고 한다.
    /// </summary>
    /// <param name="docType">화면이 쓰는 문서 종류(하이픈 꼴) — 같은 화면 [인쇄] 단추의 종과 같아야 한다.</param>
    /// <param name="docId">전표 id.</param>
    /// <param name="ct">취소 표.</param>
    /// <returns>성공 = 바이트 · 서버가 준 파일 이름(없으면 null) / 실패 = 서버 사람 말(없으면 null) · 상태 코드(연결 예외 = 0).</returns>
    public async Task<RenderedPdfResult> FetchAsync(string docType, string docId, CancellationToken ct)
    {
        try
        {
            // 🔴 옵션 null = 저장된 양식 그대로. 미리보기 창과 같은 직렬화(PostAsJsonAsync 웹 기본값).
            var body = new { Options = (object?)null };
            using var resp = await _http.PostAsJsonAsync(
                $"api/documents/{docType}/{docId}/preview-pdf", body, ct).ConfigureAwait(false);

            var code = (int)resp.StatusCode;
            if (!resp.IsSuccessStatusCode)
            {
                var said = await ReadServerMessageAsync(resp, ct).ConfigureAwait(false);
                return new RenderedPdfResult(false, Array.Empty<byte>(), null, said, code);
            }

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            return new RenderedPdfResult(true, bytes, ReadFileName(resp.Content.Headers.ContentDisposition), null, code);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[RenderedPdf] PDF 를 받지 못했습니다 — type={docType}: {ex.Message}");
            return new RenderedPdfResult(false, Array.Empty<byte>(), null, null, 0);
        }
    }

    /// <summary>
    /// 받기 전에 「아직 칸을 안 맞췄습니다」 안내를 띄울지 서버 안내 문(<c>GET api/documents/{type}/print-notice</c>)에 묻는다.
    /// </summary>
    /// <remarks>🔴 실패 · 예외 = 거짓(안내 없이 받기 · #20) + 화면 콘솔 한 줄(#15).</remarks>
    public async Task<bool> NeedsNoticeAsync(string docType, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync($"api/documents/{docType}/print-notice", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[RenderedPdf.Notice] 안내를 묻지 못했습니다({(int)resp.StatusCode}) — 안내 없이 받습니다. type={docType}");
                return false;
            }

            var said = await resp.Content.ReadFromJsonAsync<NoticeSaid>(cancellationToken: ct).ConfigureAwait(false);
            return said?.NoCoordsImageOnly == true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[RenderedPdf.Notice] 안내를 묻지 못했습니다 — 안내 없이 받습니다. type={docType}: {ex.Message}");
            return false;
        }
    }

    /// <summary>머리의 이름 — <c>filename*</c> 우선 · 없으면 <c>filename</c>(따옴표 벗김) · 둘 다 없으면 null.</summary>
    private static string? ReadFileName(ContentDispositionHeaderValue? cd)
    {
        if (cd is null) return null;
        var name = !string.IsNullOrWhiteSpace(cd.FileNameStar) ? cd.FileNameStar : cd.FileName;
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim().Trim('"');
        return name.Length == 0 ? null : name;
    }

    /// <summary>서버가 사람 말(<c>{ message }</c>)을 보냈으면 그 말 · 아니면 null.</summary>
    private static async Task<string?> ReadServerMessageAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text)) return null;
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(p.Name, "message", StringComparison.OrdinalIgnoreCase)
                    && p.Value.ValueKind == JsonValueKind.String)
                {
                    var m = p.Value.GetString();
                    return string.IsNullOrWhiteSpace(m) ? null : m;
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[RenderedPdf] 서버 안내문을 읽지 못했습니다: {ex.Message}");
            return null;
        }
    }

    private sealed class NoticeSaid
    {
        public bool NoCoordsImageOnly { get; set; }
    }
}

/// <summary>전표 인쇄 문에서 받은 결과.</summary>
/// <param name="Ok">성공(2xx)이면 참.</param>
/// <param name="Bytes">PDF 바이트 — 실패면 빈 배열.</param>
/// <param name="FileName">서버가 머리에 실은 파일 이름 — 없으면 null.</param>
/// <param name="Message">실패 때 서버 사람 말 — 없으면 null.</param>
/// <param name="StatusCode">HTTP 상태 코드 — 연결 예외면 0.</param>
public sealed record RenderedPdfResult(bool Ok, byte[] Bytes, string? FileName, string? Message, int StatusCode);

/// <summary>
/// 🔴 [고침 1 · 결재 기록 §86 ①] 「PDF 받기」 흐름이 쓰는 화면 손잡이 — 화면(<c>DocumentPrintPreview</c>)은
/// MudBlazor 상자 · 스낵바 · JS 를 붙이고, 시험은 세는 대역을 붙인다. Blazor 의존 0.
/// </summary>
public sealed class PdfDownloadUi
{
    /// <summary>「아직 칸을 안 맞췄습니다」 상자 — 「그래도 받기」 면 참 · 「취소」 · 닫기면 거짓.</summary>
    public required Func<Task<bool>> ConfirmImageOnlyAsync { get; init; }

    /// <summary>경고 스낵바(빈 id).</summary>
    public required Action<string> Warn { get; init; }

    /// <summary>안내 스낵바(받는 중 다시 누름).</summary>
    public required Action<string> Info { get; init; }

    /// <summary>오류 스낵바(받기 · 저장 실패).</summary>
    public required Action<string> Error { get; init; }

    /// <summary>받은 바이트를 파일로 저장(이름 · 바이트).</summary>
    public required Func<string, byte[], Task> SaveAsync { get; init; }
}
