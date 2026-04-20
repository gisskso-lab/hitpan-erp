using HitPan.Application.DTOs.Approval;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>월마감 API — 마감/재오픈/조회</summary>
[ApiController]
[Route("api/monthly-closing")]
[Authorize(Policy = "TenantAdminOnly")]
public class MonthlyClosingController : ControllerBase
{
    private readonly IMonthlyClosingService _svc;

    public MonthlyClosingController(IMonthlyClosingService svc) => _svc = svc;

    /// <summary>최근 12개월 마감 현황</summary>
    [HttpGet]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        return Ok(await _svc.GetStatusAsync(tenantId, ct));
    }

    /// <summary>월마감 처리</summary>
    [HttpPost("close")]
    public async Task<IActionResult> Close([FromBody] CloseMonthRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) return Forbid();
        await _svc.CloseAsync(request, tenantId, userId, ct);
        return Ok(new { message = $"{request.YearMonth} 마감 완료" });
    }

    /// <summary>월마감 재오픈 (관리자 전용)</summary>
    [HttpPost("reopen")]
    public async Task<IActionResult> Reopen([FromBody] ReopenMonthRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) return Forbid();
        await _svc.ReopenAsync(request, tenantId, userId, ct);
        return Ok(new { message = $"{request.YearMonth} 재오픈 완료" });
    }

    /// <summary>특정 날짜가 마감 상태인지 확인</summary>
    [HttpGet("check")]
    [Authorize(Policy = "TenantOnly")]
    public async Task<IActionResult> Check([FromQuery] DateTime date, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        var closed = await _svc.IsClosedAsync(tenantId, date, ct);
        return Ok(new { date, closed });
    }
}
