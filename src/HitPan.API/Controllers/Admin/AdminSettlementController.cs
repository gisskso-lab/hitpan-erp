using HitPan.Application.DTOs.Backoffice;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers.Admin;

[ApiController]
[Route("api/admin/settlements")]
[Authorize(Policy = "PlatformOnly")]
public class AdminSettlementController : ControllerBase
{
    private readonly IResellerService _resellerService;
    private readonly ILogger<AdminSettlementController> _logger;

    public AdminSettlementController(IResellerService resellerService, ILogger<AdminSettlementController> logger)
    {
        _resellerService = resellerService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetSettlements(
        [FromQuery] string? settlementMonth, [FromQuery] string? status,
        [FromQuery] string? resellerId,
        [FromQuery] int page = 1, [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _resellerService.GetSettlementsAsync(settlementMonth, status, resellerId, page, size, ct);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminSettlement] 정산 목록 조회 실패");
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpGet("{settlementId}")]
    public async Task<IActionResult> GetSettlement(string settlementId, CancellationToken ct)
    {
        try
        {
            var result = await _resellerService.GetSettlementAsync(settlementId, ct);
            return Ok(new { success = true, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminSettlement] 정산 상세 조회 실패 — {SettlementId}", settlementId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateSettlementRequest request, CancellationToken ct)
    {
        var role = User.FindFirst("role")?.Value;
        if (role is not ("super_admin" or "billing_admin"))
            return Forbid();

        try
        {
            var result = await _resellerService.GenerateSettlementsAsync(request, ct);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminSettlement] 정산 생성 실패 — {Month}", request.SettlementMonth);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpPost("{settlementId}/approve")]
    public async Task<IActionResult> Approve(
        string settlementId, [FromBody] ApproveSettlementRequest request, CancellationToken ct)
    {
        var role = User.FindFirst("role")?.Value;
        if (role is not ("super_admin" or "billing_admin"))
            return Forbid();

        try
        {
            var adminId = User.FindFirst("admin_id")?.Value ?? "";
            await _resellerService.ApproveSettlementAsync(settlementId, adminId, request, ct);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminSettlement] 정산 승인 실패 — {SettlementId}", settlementId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpPost("{settlementId}/pay")]
    public async Task<IActionResult> Pay(
        string settlementId, [FromBody] PaySettlementRequest request, CancellationToken ct)
    {
        var role = User.FindFirst("role")?.Value;
        if (role is not ("super_admin" or "billing_admin"))
            return Forbid();

        try
        {
            await _resellerService.PaySettlementAsync(settlementId, request, ct);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminSettlement] 정산 지급 실패 — {SettlementId}", settlementId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpPost("{settlementId}/cancel")]
    public async Task<IActionResult> Cancel(
        string settlementId, [FromBody] CancelSettlementRequest request, CancellationToken ct)
    {
        var role = User.FindFirst("role")?.Value;
        if (role != "super_admin")
            return Forbid();

        try
        {
            await _resellerService.CancelSettlementAsync(settlementId, request, ct);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminSettlement] 정산 취소 실패 — {SettlementId}", settlementId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }
}
