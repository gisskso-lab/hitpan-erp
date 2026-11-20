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

    public RenderedPdfDownload(HttpClient http)
    {
        _http = http;
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
