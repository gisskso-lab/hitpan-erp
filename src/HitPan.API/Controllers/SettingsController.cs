using HitPan.Application.DTOs.Settings;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize]
public sealed class SettingsController : ControllerBase
{
    private readonly ISettingsService _settingsService;

    public SettingsController(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    [HttpGet]
    // TenantProfile 정책으로 플랫폼/대리점/고객사 계정의 조회 접근을 허용한다.
    [Authorize(Policy = "TenantProfile")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var dto = await _settingsService.GetAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(dto);
    }

    [HttpPut]
    // TenantProfile 정책으로 동일 tenant 범위 내 설정 저장 접근을 허용한다.
    [Authorize(Policy = "TenantProfile")]
    public async Task<IActionResult> Save([FromBody] UpdateTenantSettingsDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _settingsService.SaveAsync(dto, tenantId, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpPost("validate-unit-price")]
    public async Task<IActionResult> ValidateUnitPrice(
        [FromBody] ValidateUnitPriceRequestDto dto,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var result = await _settingsService.ValidateUnitPriceAsync(
            tenantId,
            dto.UnitPrice,
            dto.ReferencePrice,
            ct).ConfigureAwait(false);

        return Ok(result);
    }

    [HttpPost("verify-force-edit-password")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> VerifyForceEditPassword(
        [FromBody] VerifyForceEditPasswordRequestDto dto,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        if (string.IsNullOrEmpty(dto.Password))
        {
            return Ok(new { valid = false });
        }

        var ok = await _settingsService.VerifyForceEditPasswordAsync(
            tenantId,
            dto.Password,
            ct).ConfigureAwait(false);

        return Ok(new { valid = ok });
    }
}
