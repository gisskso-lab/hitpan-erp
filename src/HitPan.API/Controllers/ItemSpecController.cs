using HitPan.Application.DTOs.Item;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

// 상품 규격 1:N API (사장님 작업지시 2026-05-31)
// 그리드 콤보박스 옵션 + 마스터 CRUD 모두 지원
[ApiController]
[Route("api/items/{itemId}/specs")]
[Authorize]
public class ItemSpecController : ControllerBase
{
    private readonly IItemSpecService _service;
    private readonly ILogger<ItemSpecController> _logger;

    public ItemSpecController(IItemSpecService service, ILogger<ItemSpecController> logger)
    {
        _service = service;
        _logger = logger;
    }

    // 콤보박스 옵션 조회 (그리드에서 매 행마다 호출)
    [HttpGet]
    public async Task<IActionResult> Get(string itemId, [FromQuery] bool activeOnly = true, CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var specs = await _service.GetByItemAsync(tenantId, itemId, activeOnly, ct);
        return Ok(specs);
    }

    // 신규 규격 추가 (상품마스터 화면에서 호출)
    [HttpPost]
    public async Task<IActionResult> Create(string itemId, [FromBody] CreateItemSpecRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            var spec = await _service.CreateAsync(tenantId, itemId, request, ct);
            return Created($"/api/items/{itemId}/specs/{spec.SpecId}", spec);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "ItemSpec create rejected: tenant={Tenant} item={Item}", tenantId, itemId);
            return BadRequest(new { error = ex.Message });
        }
    }

    // 규격 수정
    [HttpPut("{specId}")]
    public async Task<IActionResult> Update(string itemId, string specId, [FromBody] UpdateItemSpecRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            var spec = await _service.UpdateAsync(tenantId, itemId, specId, request, ct);
            return Ok(spec);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "ItemSpec update failed: tenant={Tenant} item={Item} spec={Spec}", tenantId, itemId, specId);
            return BadRequest(new { error = ex.Message });
        }
    }

    // 규격 비활성화 (SOFT DELETE)
    [HttpDelete("{specId}")]
    public async Task<IActionResult> Deactivate(string itemId, string specId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        await _service.DeactivateAsync(tenantId, itemId, specId, ct);
        return NoContent();
    }
}
