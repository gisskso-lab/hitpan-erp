using HitPan.Application.DTOs.Tenant;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/tenants")]
public class TenantsController : ControllerBase
{
    private readonly ITenantService _tenantService;

    public TenantsController(ITenantService tenantService)
    {
        _tenantService = tenantService;
    }

    [HttpPost("setup")]
    [AllowAnonymous]
    public async Task<IActionResult> Setup([FromBody] CreateTenantRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _tenantService.CreateAsync(request, ct);
            return Created("/api/tenants/setup", result);
        }
        catch (InvalidOperationException ex) when (ex.Message == "이미 등록된 회사가 있습니다")
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var me = await _tenantService.GetCurrentAsync(ct);
        if (me is null)
        {
            return NotFound();
        }

        return Ok(me);
    }
}
