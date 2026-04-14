using HitPan.Application.DTOs.Permission;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/permissions")]
// TenantProfile 정책으로 플랫폼/대리점/고객사 계정 접근을 허용하되 tenant 경계는 서비스에서 유지한다.
[Authorize(Policy = "TenantProfile")]
public class PermissionController : ControllerBase
{
    private readonly IPermissionService _svc;

    public PermissionController(IPermissionService svc)
    {
        _svc = svc;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid))
        {
            return Forbid();
        }

        return Ok(await _svc.GetAllUsersPermissionsAsync(tid, ct).ConfigureAwait(false));
    }

    [HttpGet("{userId}")]
    public async Task<IActionResult> Get(string userId, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid))
        {
            return Forbid();
        }

        var result = await _svc.GetAsync(userId, tid, ct).ConfigureAwait(false);
        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SavePermissionsDto dto, CancellationToken ct)
    {
        var tid = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tid))
        {
            return Forbid();
        }

        await _svc.SaveAsync(dto, tid, ct).ConfigureAwait(false);
        return Ok(new { message = "권한이 저장됐습니다." });
    }
}
