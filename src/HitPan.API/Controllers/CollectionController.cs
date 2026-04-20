using HitPan.Application.DTOs.Approval;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>수금·지급 API</summary>
[ApiController]
[Route("api")]
[Authorize(Policy = "SalesOnly")]
public class CollectionController : ControllerBase
{
    private readonly ICollectionService _collectionService;

    public CollectionController(ICollectionService collectionService)
    {
        _collectionService = collectionService;
    }

    // ── 수금 ──

    /// <summary>수금 목록 조회</summary>
    [HttpGet("collections")]
    public async Task<IActionResult> GetCollections(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? partnerId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        return Ok(await _collectionService.GetCollectionsAsync(tenantId, from, to, partnerId, ct));
    }

    /// <summary>수금 등록</summary>
    [HttpPost("collections")]
    public async Task<IActionResult> CreateCollection([FromBody] CreateCollectionRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) return Forbid();
        var id = await _collectionService.CreateCollectionAsync(request, tenantId, userId, ct);
        return Created($"/api/collections/{id}", new { id });
    }

    /// <summary>수금 삭제 (비활성화)</summary>
    [HttpDelete("collections/{id}")]
    public async Task<IActionResult> DeleteCollection(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        await _collectionService.DeleteCollectionAsync(id, tenantId, ct);
        return Ok(new { message = "삭제되었습니다." });
    }

    // ── 지급 ──

    /// <summary>지급 목록 조회</summary>
    [HttpGet("payments")]
    public async Task<IActionResult> GetPayments(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? partnerId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        return Ok(await _collectionService.GetPaymentsAsync(tenantId, from, to, partnerId, ct));
    }

    /// <summary>지급 등록</summary>
    [HttpPost("payments")]
    public async Task<IActionResult> CreatePayment([FromBody] CreatePaymentRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) return Forbid();
        var id = await _collectionService.CreatePaymentAsync(request, tenantId, userId, ct);
        return Created($"/api/payments/{id}", new { id });
    }

    /// <summary>지급 삭제 (비활성화)</summary>
    [HttpDelete("payments/{id}")]
    public async Task<IActionResult> DeletePayment(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        await _collectionService.DeletePaymentAsync(id, tenantId, ct);
        return Ok(new { message = "삭제되었습니다." });
    }
}
