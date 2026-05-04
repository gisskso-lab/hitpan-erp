using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers.Admin;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "PlatformOnly")]
public class AdminDashboardController : ControllerBase
{
    private readonly IResellerService _resellerService;
    private readonly ILogger<AdminDashboardController> _logger;

    public AdminDashboardController(IResellerService resellerService, ILogger<AdminDashboardController> logger)
    {
        _resellerService = resellerService;
        _logger = logger;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(CancellationToken ct)
    {
        try
        {
            var result = await _resellerService.GetAdminDashboardAsync(ct);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminDashboard] 대시보드 조회 실패");
            return StatusCode(500, new { success = false, message = "서버 오류가 발생했습니다" });
        }
    }
}
