using System.Text.Json;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 자료 화면(원장·현황·통계·재고·미수 등)의 표를 엑셀로 내려준다.
/// </summary>
/// <remarks>
/// <para>
/// 사장님 지시 2026-08-12: <i>"원장, 마스타자료, 목록, 현황, 분석, 통계자료, 재고자료,
/// 미수업체, 미수자료 등 히트판에서 뽑을 수 있는 모든 자료들은 엑셀변환, 혹은 PDF, 인쇄로"</i>
/// </para>
/// <para>
/// 🔴 <b>왜 화면마다 만들지 않고 이 하나로 두나</b> — 2026-08-12 조사 결과,
/// 자료 화면 34개 중 실제로 내보내기가 되는 것은 <b>1개</b>뿐이었고
/// <b>14개</b>는 <c>"엑셀 내보내기는 준비중입니다"</c> 만 띄우는 가짜 버튼이었다.
/// 화면마다 따로 만들면 34곳에 34가지가 생기고 그중 몇은 또 가짜로 남는다.
/// </para>
/// <para>
/// ⚠️ <b>왜 화면이 표를 보내나(서버가 다시 조회하지 않고)</b>:
/// 화면에 보이는 것과 파일이 <b>반드시 같아야</b> 하기 때문이다. 서버가 같은 조건으로
/// 다시 조회하면 그 사이 자료가 바뀌어 <b>화면과 다른 파일</b>이 나갈 수 있고,
/// 화면마다 다른 필터·정렬·계산을 서버에서 재현해야 해 34벌의 쿼리가 다시 필요해진다.
/// ⇒ 표를 그대로 받아 <b>모양만</b> 입힌다. 자료의 출처는 이미 인증된 화면이다.
/// </para>
/// <para>
/// 테넌트 격리(헌법 #2): 이 API 는 DB 를 읽지 않는다. 화면이 이미 자기 테넌트 자료만
/// 조회해 온 것을 받아 엑셀로 바꿔줄 뿐이라 <c>tenant_id</c> 를 받지도, 쓰지도 않는다.
/// 인증만 확인한다.
/// </para>
/// </remarks>
[ApiController]
[Route("api/export")]
[Authorize]
public class GridExportController : ControllerBase
{
    /// <summary>
    /// 한 번에 받을 수 있는 행 수 상한.
    /// </summary>
    /// <remarks>
    /// 없으면 브라우저가 수십만 행을 JSON 으로 올리다 메모리를 다 쓴다.
    /// 상한에 걸리면 <b>조용히 자르지 않고</b> 명확히 거절한다 —
    /// 잘린 자료를 그대로 주면 고객은 그게 전부인 줄 알고 대사에 쓴다.
    /// </remarks>
    private const int MaxRows = 100_000;

    private readonly ExcelExportService _excel;
    private readonly ILogger<GridExportController> _logger;

    public GridExportController(ExcelExportService excel, ILogger<GridExportController> logger)
    {
        _excel = excel;
        _logger = logger;
    }

    [HttpPost("excel")]
    public IActionResult ExportExcel([FromBody] GridExportRequest request)
    {
        if (request is null || request.Headers is null || request.Headers.Count == 0)
        {
            return BadRequest(new { message = "내보낼 자료가 없습니다." });
        }

        var rows = request.Rows ?? new List<List<JsonElement>>();
        if (rows.Count > MaxRows)
        {
            _logger.LogWarning("[GridExport] 행 수 초과: {Count}건 (상한 {Max})", rows.Count, MaxRows);
            return BadRequest(new
            {
                message = $"자료가 너무 많습니다({rows.Count:N0}건). 기간이나 조건을 좁혀서 다시 내보내 주세요."
            });
        }

        try
        {
            // JSON 값을 .NET 형으로 되돌린다 — 숫자를 숫자로 넣어야 엑셀에서 합계가 된다.
            var converted = rows
                .Select(r => (IReadOnlyList<object?>)r.Select(ToCellValue).ToList())
                .ToList();

            var title = string.IsNullOrWhiteSpace(request.Title) ? "자료" : request.Title!;
            var bytes = _excel.GenerateGridExcel(title, request.Headers, converted, request.Subtitle);

            // 파일명에 날짜를 넣는다 — 여러 번 받아도 서로 덮어쓰지 않는다.
            var fileName = $"{SanitizeFileName(title)}_{DateTime.Now:yyyyMMdd}.xlsx";
            var encoded = Uri.EscapeDataString(fileName);
            Response.Headers.Append("Content-Disposition", $"attachment; filename*=UTF-8''{encoded}");

            _logger.LogInformation("[GridExport] {Title} — {Rows}행 내보내기", title, converted.Count);

            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        }
        catch (Exception ex)
        {
            // 헌법 #15 — 침묵하지 않는다. 다만 고객에게는 개발용어를 보이지 않는다.
            _logger.LogError(ex, "[GridExport] 엑셀 생성 실패: {Title}", request.Title);
            return StatusCode(500, new { message = "엑셀 파일을 만들지 못했습니다. 잠시 후 다시 시도해 주세요." });
        }
    }

    /// <summary>
    /// JSON 값을 엑셀 셀에 넣을 형으로 바꾼다.
    /// </summary>
    /// <remarks>
    /// 금액은 <c>decimal</c> 로 받는다(헌법 #4 — float/double 금지).
    /// 날짜는 문자열로 오므로 ISO 형태만 날짜로 되돌리고, 아니면 문자열 그대로 둔다
    /// (예: "2026-08" 같은 기간 표기를 억지로 날짜로 만들면 화면과 달라진다).
    /// </remarks>
    private static object? ToCellValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => el.TryGetDecimal(out var d) ? d : el.GetDouble(),
        JsonValueKind.String => ParseStringCell(el.GetString()),
        _ => el.ToString()
    };

    private static object? ParseStringCell(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        // "2026-08-12" / "2026-08-12T09:30:00" 형태만 날짜로 본다.
        if (s.Length is >= 10 and <= 33
            && s[4] == '-' && s[7] == '-'
            && DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                                 System.Globalization.DateTimeStyles.None, out var dt))
        {
            return dt;
        }
        return s;
    }

    /// <summary>파일명에 쓸 수 없는 문자를 걷어낸다 — 없으면 저장이 실패한다.</summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "자료" : cleaned;
    }
}

/// <summary>자료 화면이 보내는 표.</summary>
public sealed class GridExportRequest
{
    /// <summary>문서 제목 (예: "업체별 원장"). 시트 이름·파일명이 된다.</summary>
    public string? Title { get; set; }

    /// <summary>기간·조건 등 부제. 무엇을 뽑은 자료인지 파일만 봐도 알게 한다.</summary>
    public string? Subtitle { get; set; }

    /// <summary>열 제목.</summary>
    public List<string> Headers { get; set; } = new();

    /// <summary>행 데이터.</summary>
    public List<List<JsonElement>> Rows { get; set; } = new();
}
