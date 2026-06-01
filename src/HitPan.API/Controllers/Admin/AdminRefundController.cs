using System.Security.Claims;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers.Admin;

/// <summary>
/// 백오피스 환불 처리 (사장님 결재 2026-06-01)
///
/// 권한: PlatformOnly (본사 platform_admin)
/// 헌법 #5·#23 — 감사 로그 + 5중 검증
/// </summary>
[ApiController]
[Route("api/admin/billing")]
[Authorize(Policy = "PlatformOnly")]
public class AdminRefundController : ControllerBase
{
    private readonly IRefundService _refundService;
    private readonly ILogger<AdminRefundController> _logger;

    public AdminRefundController(IRefundService refundService, ILogger<AdminRefundController> logger)
    {
        _refundService = refundService;
        _logger = logger;
    }

    /// <summary>
    /// 환불 요청
    /// Body: { reason }
    /// </summary>
    [HttpPost("invoices/{invoiceId}/refund")]
    public async Task<IActionResult> Refund(string invoiceId, [FromBody] RefundRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { success = false, message = "환불 사유를 입력해주세요" });

        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("admin_id")
            ?? "unknown";

        var result = await _refundService.RefundAsync(invoiceId, adminId, request.Reason, ct);
        if (!result.Success)
        {
            return BadRequest(new { success = false, message = result.Message });
        }

        return Ok(new { success = true, refundedAmount = result.RefundedAmount, message = result.Message });
    }

    public record RefundRequest(string Reason);
}
