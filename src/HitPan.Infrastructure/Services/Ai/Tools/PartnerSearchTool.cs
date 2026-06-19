using System.Text.Json;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services.Ai.Tools;

// ─────────────────────────────────────────────────────────────────
// [조회 Tool] 거래처 검색 — 이름으로 거래처를 찾아 ID·기본정보 반환.
//   AI 직원이 "대한상사 수익 분석" 같은 명령에서 거래처명→ID 변환할 때 먼저 호출.
//   System Prompt 규칙 "추측 말고 검색 먼저"의 핵심 도구. 읽기 전용.
// ─────────────────────────────────────────────────────────────────
public sealed class PartnerSearchTool : IHitpanTool
{
    private readonly IPartnerService _partner;
    private readonly ILogger<PartnerSearchTool> _logger;

    public PartnerSearchTool(IPartnerService partner, ILogger<PartnerSearchTool> logger)
    {
        _partner = partner;
        _logger = logger;
    }

    public string Name => "partner_search";

    public string Description =>
        "거래처(업체)를 이름으로 검색해 거래처ID·사업자번호·연락처를 반환한다. " +
        "거래처명이 포함된 명령(수익 분석, 거래명세서 작성 등)을 처리하기 전에 " +
        "정확한 거래처ID를 얻기 위해 먼저 호출한다. 읽기 전용.";

    public bool IsWrite => false;

    public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            keyword = new
            {
                type = "string",
                description = "검색할 거래처명(부분 일치). 예: '대한상사', '대한'."
            }
        },
        required = new[] { "keyword" }
    });

    public async Task<ToolResult> ExecuteAsync(JsonElement input, ToolContext ctx, CancellationToken ct = default)
    {
        var keyword = input.ValueKind == JsonValueKind.Object && input.TryGetProperty("keyword", out var k)
            ? k.GetString() : null;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return ToolResult.Fail("검색할 거래처명이 비어 있습니다.");
        }

        var results = await _partner.SearchPartnersAsync(ctx.TenantId, keyword, ct).ConfigureAwait(false);

        if (results is null || results.Count == 0)
        {
            return ToolResult.Ok(JsonSerializer.Serialize(new { keyword, found = 0, partners = Array.Empty<object>() }));
        }

        // 상위 10개만 — 동명이 여럿이면 클로드가 사용자에게 되물을 수 있게.
        var top = results.Take(10).Select(p => new
        {
            partnerId = p.PartnerId,
            partnerName = p.PartnerName,
            bizNo = p.BizNo,
            tel = p.Tel
        });

        return ToolResult.Ok(JsonSerializer.Serialize(new { keyword, found = results.Count, partners = top }));
    }
}
