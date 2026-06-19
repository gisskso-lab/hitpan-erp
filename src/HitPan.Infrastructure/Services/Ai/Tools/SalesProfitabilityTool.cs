using System.Globalization;
using System.Text.Json;
using HitPan.Application.DTOs.Chatbot;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services.Ai.Tools;

// ─────────────────────────────────────────────────────────────────
// [조회 Tool] 매출 수익성 분석 — 거래처별/품목별/기간별 매출·원가·이익·이익률.
//   기존 검증된 ReportService.GetSalesProfitabilityAsync 재사용(환각 0, 헌법 #23).
//   읽기 전용(데이터 변경 0, 헌법 #6). 표 + 막대차트(Analysis) 함께 반환.
//
//   첫 Tool — 이 패턴(스키마 정의 → 인자 파싱 → 기존 서비스 호출 → 결과 구조화)을
//   나머지 Tool 들이 복제한다.
// ─────────────────────────────────────────────────────────────────
public sealed class SalesProfitabilityTool : IHitpanTool
{
    private readonly IReportService _report;
    private readonly ILogger<SalesProfitabilityTool> _logger;

    public SalesProfitabilityTool(IReportService report, ILogger<SalesProfitabilityTool> logger)
    {
        _report = report;
        _logger = logger;
    }

    public string Name => "sales_profitability";

    public string Description =>
        "매출 수익성(매출·원가·이익·이익률)을 분석한다. " +
        "거래처별/품목별/기간별 집계를 지원한다. " +
        "사용자가 '수익 분석', '마진 분석', '이익 얼마' 등을 물을 때 사용. 읽기 전용.";

    public bool IsWrite => false;

    public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            view = new
            {
                type = "string",
                @enum = new[] { "partner", "item", "period" },
                description = "집계 기준. partner=거래처별, item=품목별, period=기간(월)별. 기본 partner."
            },
            partner = new
            {
                type = "string",
                description = "특정 거래처명으로 필터(선택). 예: '대한상사'."
            },
            from = new
            {
                type = "string",
                description = "시작일 YYYY-MM-DD(선택). 없으면 전체 기간."
            },
            to = new
            {
                type = "string",
                description = "종료일 YYYY-MM-DD(선택)."
            }
        }
    });

    public async Task<ToolResult> ExecuteAsync(JsonElement input, ToolContext ctx, CancellationToken ct = default)
    {
        var view = GetString(input, "view") ?? "partner";
        if (view != "partner" && view != "item" && view != "period")
        {
            view = "partner";
        }
        var partner = GetString(input, "partner");
        var from = GetDate(input, "from");
        var to = GetDate(input, "to");

        var rows = await _report.GetSalesProfitabilityAsync(view, ctx.TenantId, from, to, partner, ct)
            .ConfigureAwait(false);

        var byLabel = view switch { "item" => "품목별", "period" => "기간별", _ => "거래처별" };
        var who = string.IsNullOrEmpty(partner) ? "" : $"[{partner}] ";
        var title = $"{who}{byLabel} 수익성 분석";

        if (rows is null || rows.Count == 0)
        {
            return ToolResult.Ok($"{title}: 해당 조건의 거래 자료가 없습니다.");
        }

        // 클로드에게 줄 요약(JSON) — 상위 10개만(토큰 절약). 클로드가 이걸 해석해 말로 답.
        var summaryRows = rows.Take(10).Select(r => new
        {
            label = r.Label,
            revenue = r.Revenue,
            cost = r.Cost,
            profit = r.Profit,
            profitRate = Math.Round(r.ProfitRate, 1),
            count = r.Count
        });
        var content = JsonSerializer.Serialize(new { title, totalRows = rows.Count, top = summaryRows });

        // 사람이 보는 표 + 차트(Analysis).
        var firstCol = view switch { "item" => "품목", "period" => "기간", _ => "거래처" };
        var analysis = new AiAnalysisResultDto
        {
            Kind = "sales-profitability",
            Title = title,
            Columns = new List<string> { firstCol, "매출", "원가", "이익", "이익률", "건수" },
            Rows = rows.Take(50).Select(r => new List<string>
            {
                r.Label,
                Won(r.Revenue), Won(r.Cost), Won(r.Profit),
                $"{r.ProfitRate:0.0}%",
                r.Count.ToString("N0", CultureInfo.InvariantCulture)
            }).ToList(),
            Chart = rows.OrderByDescending(r => r.Profit).Take(10)
                .Select(r => new ChartPointDto { Label = r.Label, Value = (double)r.Profit }).ToList(),
            ChartValueLabel = "이익(원)"
        };

        return new ToolResult { Content = content, Succeeded = true, Analysis = analysis };
    }

    private static string Won(decimal v) => v.ToString("N0", CultureInfo.InvariantCulture) + "원";

    private static string? GetString(JsonElement input, string key)
        => input.ValueKind == JsonValueKind.Object && input.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static DateTime? GetDate(JsonElement input, string key)
    {
        var s = GetString(input, key);
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }
}
