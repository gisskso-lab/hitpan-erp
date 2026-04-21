using HitPan.Application.DTOs.ESign;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 전자근로계약서 API 컨트롤러.
/// 워크플로우: 생성(draft) → 발송(sent) → 서명첨부(signed)
/// - 생성/발송: TenantAdmin 전용
/// - 목록/상세: TenantOnly (본인 계약서는 일반 사원도 조회 가능)
/// </summary>
[ApiController]
[Route("api/labor-contracts")]
[Authorize(Policy = "TenantOnly")]
public sealed class LaborContractController : ControllerBase
{
    private readonly ILaborContractService _labor;

    public LaborContractController(ILaborContractService labor)
    {
        _labor = labor;
    }

    /// <summary>근로계약서 목록 조회 (?employeeId=&status=).</summary>
    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] string? employeeId,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var list = await _labor.GetListAsync(tenantId, employeeId, status, ct).ConfigureAwait(false);
        return Ok(list);
    }

    /// <summary>근로계약서 상세 조회.</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var dto = await _labor.GetAsync(id, tenantId, ct).ConfigureAwait(false);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    /// <summary>근로계약서 생성 (TenantAdmin 전용).</summary>
    [HttpPost]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreateLaborContractRequest req, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString() ?? User.FindFirst("sub")?.Value ?? "";
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            var contractId = await _labor.CreateAsync(req, tenantId, userId, ct).ConfigureAwait(false);
            return Ok(new { contractId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>근로계약서 발송 (TenantAdmin 전용).</summary>
    [HttpPost("{id}/send")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Send(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString() ?? User.FindFirst("sub")?.Value ?? "";
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            await _labor.SendAsync(id, tenantId, userId, ct).ConfigureAwait(false);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>전자서명 기록 첨부 (서명 완료 처리).</summary>
    [HttpPost("{id}/sign")]
    public async Task<IActionResult> AttachSignature(string id, [FromBody] AttachSignatureRequest req, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            await _labor.AttachSignatureAsync(id, req.EsignId, tenantId, ct).ConfigureAwait(false);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
