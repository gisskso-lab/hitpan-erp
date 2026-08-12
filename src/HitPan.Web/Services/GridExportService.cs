using System.Net.Http.Json;
using Microsoft.JSInterop;
using MudBlazor;

namespace HitPan.Web.Services;

/// <summary>
/// 자료 화면(원장·현황·통계·재고·미수 등)의 표를 엑셀로 내보내고 인쇄한다.
/// </summary>
/// <remarks>
/// <para>
/// 사장님 지시 2026-08-12: <i>"히트판에서 뽑을 수 있는 모든 자료들은 엑셀변환, 혹은 PDF, 인쇄로"</i>
/// </para>
/// <para>
/// 🔴 <b>왜 공용으로 두나</b> — 2026-08-12 조사에서 자료 화면 34개 중 실제로 되는 것이
/// <b>1개</b>뿐이었고 <b>14개</b>는 <c>"엑셀 내보내기는 준비중입니다"</c> 만 띄우는
/// 가짜 버튼이었다. 화면마다 붙이면 34가지 방식이 생기고 몇 개는 또 가짜로 남는다.
/// 화면은 <b>표만 넘기고</b> 나머지는 여기서 한다.
/// </para>
/// <para>
/// PDF 는 별도 엔진을 넣지 않고 <b>인쇄 → "PDF로 저장"</b> 으로 간다.
/// 부가세 신고자료 화면이 이미 그 방식이고 실제로 잘 된다. 엔진을 새로 넣으면
/// 화면 모양과 PDF 모양이 갈리고, 그 차이를 계속 맞춰야 한다.
/// </para>
/// </remarks>
public sealed class GridExportService
{
    private readonly HttpClient _http;
    private readonly IJSRuntime _js;
    private readonly ISnackbar _snackbar;

    public GridExportService(HttpClient http, IJSRuntime js, ISnackbar snackbar)
    {
        _http = http;
        _js = js;
        _snackbar = snackbar;
    }

    /// <summary>
    /// 표를 엑셀 파일로 내려받는다.
    /// </summary>
    /// <param name="title">문서 제목 (예: "업체별 원장"). 파일명·시트명이 된다.</param>
    /// <param name="headers">열 제목.</param>
    /// <param name="rows">행 데이터. 금액은 <c>decimal</c>, 날짜는 <c>DateTime</c> 로 넘기면
    /// 엑셀에서 숫자·날짜로 들어가 바로 합계·정렬이 된다(문자열로 넘기면 안 된다).</param>
    /// <param name="subtitle">기간·조건 (예: "2026-01-01 ~ 2026-08-12 · 거래처: 전체").</param>
    public async Task ExportExcelAsync(
        string title,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        string? subtitle = null)
    {
        // 자료가 없는데 파일을 만들면 빈 표를 받고 "고장났나" 싶어진다. 먼저 알린다.
        if (rows.Count == 0)
        {
            _snackbar.Add("내보낼 자료가 없습니다. 조회 조건을 확인해 주세요.", Severity.Info);
            return;
        }

        try
        {
            using var res = await _http.PostAsJsonAsync("api/export/excel", new
            {
                Title = title,
                Subtitle = subtitle,
                Headers = headers,
                Rows = rows
            });

            if (!res.IsSuccessStatusCode)
            {
                // 서버가 이유를 담아 보냈으면 그대로 보여준다(예: 자료가 너무 많습니다).
                var msg = await TryReadMessageAsync(res);
                _snackbar.Add(msg ?? "엑셀 파일을 만들지 못했습니다. 잠시 후 다시 시도해 주세요.", Severity.Warning);
                return;
            }

            var bytes = await res.Content.ReadAsByteArrayAsync();
            var fileName = $"{title}_{DateTime.Now:yyyyMMdd}.xlsx";

            await _js.InvokeVoidAsync("downloadFileFromBytes",
                fileName,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                Convert.ToBase64String(bytes));

            _snackbar.Add($"엑셀 파일을 내려받았습니다. ({rows.Count:N0}건)", Severity.Success);
        }
        catch (Exception ex)
        {
            // 헌법 #15 — 침묵하지 않는다. 화면에는 개발용어를 보이지 않는다.
            Console.Error.WriteLine("[GridExport] 엑셀 내보내기 실패: " + ex.Message);
            _snackbar.Add("엑셀 파일을 만들지 못했습니다. 잠시 후 다시 시도해 주세요.", Severity.Warning);
        }
    }

    /// <summary>
    /// 화면을 인쇄한다. 브라우저 인쇄창에서 "PDF로 저장" 을 고르면 PDF 가 된다.
    /// </summary>
    public async Task PrintAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("print");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[GridExport] 인쇄 실패: " + ex.Message);
            _snackbar.Add("인쇄 창을 열지 못했습니다.", Severity.Warning);
        }
    }

    private static async Task<string?> TryReadMessageAsync(HttpResponseMessage res)
    {
        try
        {
            var body = await res.Content.ReadFromJsonAsync<ErrorBody>();
            return string.IsNullOrWhiteSpace(body?.Message) ? null : body!.Message;
        }
        catch
        {
            // 본문이 JSON 이 아닐 수 있다 — 그때는 기본 문구를 쓴다(실패로 보지 않는다).
            return null;
        }
    }

    private sealed class ErrorBody
    {
        public string? Message { get; set; }
    }
}
