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
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(ISettingsService settingsService, ILogger<SettingsController> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Policy = "TenantAdminOnly")]
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
    [Authorize(Policy = "TenantAdminOnly")]
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

    /// <summary>
    /// 사업장 기본정보(tenants)를 조회한다.
    /// </summary>
    [HttpGet("company")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> GetCompany(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var dto = await _settingsService.GetCompanyAsync(tenantId, ct).ConfigureAwait(false);
        if (dto is null)
        {
            return NotFound();
        }

        return Ok(dto);
    }

    /// <summary>
    /// 사업장 기본정보(tenants)를 갱신한다.
    /// </summary>
    [HttpPut("company")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> SaveCompany([FromBody] UpdateTenantCompanyDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        // 🔴 2026-08-10 ([3-V] 병렬검증 I-6) — 잠긴 항목은 조용히 무시하지 않는다.
        //   종전엔 잠금 위반이 와도 그대로 200 "적용되었습니다" 였다. 고객은 고쳤다고 믿는데
        //   값은 그대로라 원인을 알 수 없었다(2026-08-09 권한 화면과 같은 구조).
        //   ⇒ 저장은 하되(나머지 항목은 정상 반영) 무엇이 반영되지 않았는지 함께 돌려준다.
        var rejected = await _settingsService.SaveCompanyAsync(dto, tenantId, ct).ConfigureAwait(false);

        if (rejected.Count > 0)
        {
            _logger.LogWarning(
                "[SaveCompany] 잠긴 항목 변경 시도 거부 tenant={TenantId} fields={Fields}",
                tenantId, string.Join(",", rejected));

            return Ok(new
            {
                saved = true,
                rejectedFields = rejected,
                message = $"{string.Join(" · ", rejected)} 항목은 사업자등록증에서 자동으로 채워진 정보라 "
                          + "변경되지 않았습니다. 변경하시려면 [기본 사업장 정보 변경 요청] 을 이용해주세요."
            });
        }

        return Ok(new { saved = true, rejectedFields = Array.Empty<string>() });
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
