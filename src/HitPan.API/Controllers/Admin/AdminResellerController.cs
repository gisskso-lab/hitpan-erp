using HitPan.Application.DTOs.Backoffice;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers.Admin;

[ApiController]
[Route("api/admin/resellers")]
[Authorize(Policy = "PlatformOnly")]
public class AdminResellerController : ControllerBase
{
    private readonly IResellerService _resellerService;
    private readonly ILogger<AdminResellerController> _logger;

    public AdminResellerController(IResellerService resellerService, ILogger<AdminResellerController> logger)
    {
        _resellerService = resellerService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetResellers(
        [FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _resellerService.GetResellersAsync(status, search, page, size, ct);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminReseller] 대리점 목록 조회 실패");
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpGet("{resellerId}")]
    public async Task<IActionResult> GetReseller(string resellerId, CancellationToken ct)
    {
        try
        {
            var result = await _resellerService.GetResellerAsync(resellerId, ct);
            return Ok(new { success = true, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminReseller] 대리점 상세 조회 실패 — {ResellerId}", resellerId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    // super_admin만 계좌 전체 조회
    [HttpGet("{resellerId}/bank-account")]
    public async Task<IActionResult> GetBankAccount(string resellerId, CancellationToken ct)
    {
        var role = User.FindFirst("role")?.Value;
        if (role != "super_admin")
            return Forbid();

        try
        {
            var account = await _resellerService.GetResellerBankAccountAsync(resellerId, ct);
            return Ok(new { success = true, data = new { BankAccount = account } });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminReseller] 계좌 조회 실패 — {ResellerId}", resellerId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateReseller([FromBody] CreateResellerRequest request, CancellationToken ct)
    {
        var role = User.FindFirst("role")?.Value;
        if (role != "super_admin")
            return Forbid();

        try
        {
            var adminId = User.FindFirst("admin_id")?.Value ?? "";
            var result = await _resellerService.CreateResellerAsync(request, adminId, ct);
            return Created($"/api/admin/resellers/{result.ResellerId}", new { success = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminReseller] 대리점 등록 실패 — {Name}", request.ResellerName);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpPut("{resellerId}")]
    public async Task<IActionResult> UpdateReseller(
        string resellerId, [FromBody] UpdateResellerRequest request, CancellationToken ct)
    {
        var role = User.FindFirst("role")?.Value;
        if (role != "super_admin")
            return Forbid();

        try
        {
            await _resellerService.UpdateResellerAsync(resellerId, request, ct);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminReseller] 대리점 수정 실패 — {ResellerId}", resellerId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpPatch("{resellerId}/status")]
    public async Task<IActionResult> UpdateResellerStatus(
        string resellerId, [FromBody] UpdateResellerStatusRequest request, CancellationToken ct)
    {
        var role = User.FindFirst("role")?.Value;
        if (role != "super_admin")
            return Forbid();

        try
        {
            await _resellerService.UpdateResellerStatusAsync(resellerId, request, ct);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminReseller] 상태 변경 실패 — {ResellerId}", resellerId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    // ── 대리점 계정 관리 ─────────────────────────────────────────────────────

    [HttpGet("{resellerId}/accounts")]
    public async Task<IActionResult> GetAccounts(string resellerId, CancellationToken ct)
    {
        try
        {
            var result = await _resellerService.GetResellerAccountsAsync(resellerId, ct);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminReseller] 계정 목록 조회 실패 — {ResellerId}", resellerId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpPost("{resellerId}/accounts")]
    public async Task<IActionResult> CreateAccount(
        string resellerId, [FromBody] CreateResellerAccountRequest request, CancellationToken ct)
    {
        var role = User.FindFirst("role")?.Value;
        if (role != "super_admin")
            return Forbid();

        try
        {
            var result = await _resellerService.CreateResellerAccountAsync(resellerId, request, ct);
            return Created("", new { success = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminReseller] 계정 생성 실패 — {Email}", request.Email);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpPatch("{resellerId}/accounts/{accountId}/toggle")]
    public async Task<IActionResult> ToggleAccount(string resellerId, string accountId, CancellationToken ct)
    {
        var role = User.FindFirst("role")?.Value;
        if (role != "super_admin")
            return Forbid();

        try
        {
            await _resellerService.ToggleResellerAccountAsync(resellerId, accountId, ct);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminReseller] 계정 토글 실패 — {AccountId}", accountId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    // ── 수수료 정책 ──────────────────────────────────────────────────────────

    [HttpGet("{resellerId}/commissions")]
    public async Task<IActionResult> GetCommissions(string resellerId, CancellationToken ct)
    {
        try
        {
            var result = await _resellerService.GetCommissionPoliciesAsync(resellerId, ct);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminReseller] 수수료 정책 조회 실패 — {ResellerId}", resellerId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }

    [HttpPost("{resellerId}/commissions")]
    public async Task<IActionResult> CreateCommission(
        string resellerId, [FromBody] CreateCommissionPolicyRequest request, CancellationToken ct)
    {
        var role = User.FindFirst("role")?.Value;
        if (role != "super_admin")
            return Forbid();

        try
        {
            var adminId = User.FindFirst("admin_id")?.Value ?? "";
            var id = await _resellerService.CreateCommissionPolicyAsync(resellerId, request, adminId, ct);
            return Created("", new { success = true, data = new { CommissionId = id } });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminReseller] 수수료 정책 등록 실패 — {ResellerId}", resellerId);
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }
}
