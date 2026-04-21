using HitPan.API.Authorization;
using HitPan.Application.DTOs.User;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UserController : ControllerBase
{
    private readonly IUserService _userService;

    public UserController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    [RequirePermission("USERS", "view")]
    public async Task<IActionResult> GetList(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var result = await _userService.GetListAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpGet("{id}")]
    [RequirePermission("USERS", "view")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var result = await _userService.GetAsync(id, tenantId, ct).ConfigureAwait(false);
        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "TenantAdminOnly")]
    [RequirePermission("USERS", "create")]
    public async Task<IActionResult> Create([FromBody] CreateUserDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        try
        {
            var id = await _userService.CreateAsync(dto, tenantId, ct).ConfigureAwait(false);
            return CreatedAtAction(nameof(Get), new { id }, new { id });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "TenantAdminOnly")]
    [RequirePermission("USERS", "update")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateUserDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _userService.UpdateAsync(id, dto, tenantId, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "TenantAdminOnly")]
    [RequirePermission("USERS", "delete")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _userService.DeactivateAsync(id, tenantId, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpPost("{id}/reset-password")]
    [Authorize(Policy = "TenantAdminOnly")]
    [RequirePermission("USERS", "update")]
    public async Task<IActionResult> ResetPassword(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        try
        {
            var tempPassword = await _userService.ResetPasswordAsync(id, tenantId, ct).ConfigureAwait(false);
            return Ok(new { tempPassword, message = "임시 비밀번호가 발급됐습니다." });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
