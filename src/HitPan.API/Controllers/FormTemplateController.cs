using HitPan.Application.DTOs.Settings;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

// 양식정보설정 API (사장님 작업지시 2026-05-31 작지②·③)
[ApiController]
[Route("api/settings/form-templates")]
[Authorize]
public class FormTemplateController : ControllerBase
{
    private readonly IFormTemplateService _service;
    private readonly ILogger<FormTemplateController> _logger;

    public FormTemplateController(IFormTemplateService service, ILogger<FormTemplateController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? formType, [FromQuery] bool activeOnly = true, CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var list = await _service.ListAsync(tenantId, formType, activeOnly, ct);
        return Ok(list);
    }

    [HttpGet("default/{formType}")]
    public async Task<IActionResult> GetDefault(string formType, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var dto = await _service.GetDefaultAsync(tenantId, formType, ct);
        if (dto is null) return NotFound(new { error = $"기본 양식이 없습니다. ({formType})" });
        return Ok(dto);
    }

    [HttpPost]
    [Authorize(Roles = "system_admin,tenant_admin")]
    public async Task<IActionResult> Create([FromBody] CreateFormTemplateRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            var dto = await _service.CreateAsync(tenantId, request, ct);
            return Created($"/api/settings/form-templates/{dto.TemplateId}", dto);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "FormTemplate create rejected: tenant={Tenant}", tenantId);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{templateId}")]
    [Authorize(Roles = "system_admin,tenant_admin")]
    public async Task<IActionResult> Update(string templateId, [FromBody] UpdateFormTemplateRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            var dto = await _service.UpdateAsync(tenantId, templateId, request, ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "FormTemplate update failed: tenant={Tenant} template={Template}", tenantId, templateId);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{templateId}")]
    [Authorize(Roles = "system_admin,tenant_admin")]
    public async Task<IActionResult> Deactivate(string templateId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        await _service.DeactivateAsync(tenantId, templateId, ct);
        return NoContent();
    }

    [HttpPost("seed-defaults")]
    [Authorize(Roles = "system_admin,tenant_admin")]
    public async Task<IActionResult> SeedDefaults(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        await _service.SeedDefaultsAsync(tenantId, ct);
        return Ok(new { message = "6대 양식 기본 템플릿이 박제되었습니다." });
    }
}
