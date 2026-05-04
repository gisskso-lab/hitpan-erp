using HitPan.Application.DTOs.Backoffice;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers.Reseller;

[ApiController]
[Route("api/reseller")]
[Authorize(Policy = "ResellerOnly")]
public class ResellerPortalController : ControllerBase
{
    private readonly IResellerService _resellerService;
    private readonly ILogger<ResellerPortalController> _logger;

    public ResellerPortalController(IResellerService resellerService, ILogger<ResellerPortalController> logger)
    {
        _resellerService = resellerService;
        _logger = logger;
    }

    // JWT reseller_id 추출 — 타 대리점 데이터 접근 원천 차단
    private string? ResellerIdFromJwt => User.FindFirst("reseller_id")?.Value;

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(CancellationToken ct)
    {
        var resellerId = ResellerIdFromJwt;
        if (string.IsNullOrWhiteSpace(resellerId))
            return Unauthorized(new { success = false, message = "대리점 정보가 없습니다" });

        try
        {
            var result = await _resellerService.GetResellerDashboardAsync(resellerId, ct);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ResellerPortal] 대시보드 조회 실패 — {ResellerId}", resellerId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    // ── 내 고객사 ────────────────────────────────────────────────────────────

    [HttpGet("tenants")]
    public async Task<IActionResult> GetMyTenants(
        [FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        var resellerId = ResellerIdFromJwt;
        if (string.IsNullOrWhiteSpace(resellerId))
            return Unauthorized(new { success = false, message = "대리점 정보가 없습니다" });

        try
        {
            var result = await _resellerService.GetMyTenantsAsync(resellerId, status, search, page, size, ct);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ResellerPortal] 내 고객사 목록 조회 실패 — {ResellerId}", resellerId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpGet("tenants/{tenantId}")]
    public async Task<IActionResult> GetMyTenant(string tenantId, CancellationToken ct)
    {
        var resellerId = ResellerIdFromJwt;
        if (string.IsNullOrWhiteSpace(resellerId))
            return Unauthorized(new { success = false, message = "대리점 정보가 없습니다" });

        try
        {
            var result = await _resellerService.GetMyTenantAsync(resellerId, tenantId, ct);
            return Ok(new { success = true, data = result });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ResellerPortal] 내 고객사 상세 조회 실패 — {TenantId}", tenantId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    // ── 내 수수료 ────────────────────────────────────────────────────────────

    [HttpGet("commissions/policies")]
    public async Task<IActionResult> GetMyCommissionPolicies(CancellationToken ct)
    {
        var resellerId = ResellerIdFromJwt;
        if (string.IsNullOrWhiteSpace(resellerId))
            return Unauthorized(new { success = false, message = "대리점 정보가 없습니다" });

        try
        {
            var result = await _resellerService.GetMyCommissionPoliciesAsync(resellerId, ct);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ResellerPortal] 수수료 정책 조회 실패 — {ResellerId}", resellerId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpGet("commissions/settlements")]
    public async Task<IActionResult> GetMySettlements(
        [FromQuery] int page = 1, [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        var resellerId = ResellerIdFromJwt;
        if (string.IsNullOrWhiteSpace(resellerId))
            return Unauthorized(new { success = false, message = "대리점 정보가 없습니다" });

        try
        {
            var result = await _resellerService.GetMySettlementsAsync(resellerId, page, size, ct);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ResellerPortal] 정산 이력 조회 실패 — {ResellerId}", resellerId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }
}
