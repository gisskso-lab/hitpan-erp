using HitPan.Application.DTOs.ESign;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 전자서명 API 컨트롤러.
/// - 간편인증 4종 (kakao/toss/finance/pass): 현재 Mock 모드로 동작
/// - 수동 3종 (manual_upload/handwritten/admin_verified): 파일/이미지/사유 기반 기록
/// </summary>
[ApiController]
[Route("api/esign")]
[Authorize(Policy = "TenantOnly")]
public sealed class ESignController : ControllerBase
{
    private readonly IESignatureService _esign;

    public ESignController(IESignatureService esign)
    {
        _esign = esign;
    }

    /// <summary>전자서명 기록 목록 조회.</summary>
    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] string? documentType,
        [FromQuery] string? documentId,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var list = await _esign.GetListAsync(tenantId, documentType, documentId, ct).ConfigureAwait(false);
        return Ok(list);
    }

    /// <summary>전자서명 기록 상세 조회.</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var dto = await _esign.GetAsync(id, tenantId, ct).ConfigureAwait(false);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    /// <summary>전자서명 기록 생성.</summary>
    [HttpPost("sign")]
    public async Task<IActionResult> Sign([FromBody] ESignRequestDto req, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString() ?? User.FindFirst("sub")?.Value ?? "";
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        // 네트워크 메타 수집 (감사·위변조 방지)
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var ua = HttpContext.Request.Headers.UserAgent.ToString();
        var deviceId = HttpContext.Request.Headers["X-Device-Id"].ToString();

        try
        {
            var result = await _esign.SignAsync(req, tenantId, userId, ip, ua, deviceId, ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>전자서명 무효화 (TenantAdmin 전용).</summary>
    [HttpPost("{id}/void")]
    [Authorize(Policy = "TenantAdminOnly")]
    public async Task<IActionResult> Void(string id, [FromBody] VoidESignRequest req, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString() ?? User.FindFirst("sub")?.Value ?? "";
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        try
        {
            await _esign.VoidAsync(id, tenantId, userId, req.Reason, ct).ConfigureAwait(false);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
