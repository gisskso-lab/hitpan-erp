using HitPan.Application.DTOs.Landing;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 대리점 신청 (사장님 결재 2026-06-01)
/// 랜딩 /reseller/signup 에서 POST
/// </summary>
[ApiController]
[Route("api/reseller-applications")]
public class ResellerApplicationController : ControllerBase
{
    private readonly IResellerApplicationService _service;
    private readonly ILogger<ResellerApplicationController> _logger;

    public ResellerApplicationController(
        IResellerApplicationService service,
        ILogger<ResellerApplicationController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Submit([FromBody] ResellerApplicationRequest request, CancellationToken ct)
    {
        var result = await _service.SubmitAsync(request, ct);
        if (result.Status == "rejected")
            return BadRequest(new { success = false, message = result.Message });

        return Ok(new { success = true, applicationId = result.ApplicationId, message = result.Message });
    }
}
