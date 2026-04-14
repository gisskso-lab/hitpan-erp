using HitPan.Application.DTOs.Sales;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 견적서 API를 제공한다.
/// </summary>
[ApiController]
[Route("api/quotations")]
[Authorize(Policy = "SalesOnly")]
public class QuotationController : ControllerBase
{
    // 견적서 서비스다.
    private readonly IQuotationService _quotationService;

    /// <summary>
    /// 생성자를 초기화한다.
    /// </summary>
    /// <param name="quotationService">견적서 서비스다.</param>
    public QuotationController(IQuotationService quotationService)
    {
        _quotationService = quotationService;
    }

    /// <summary>
    /// 견적서 목록을 조회한다.
    /// </summary>
    /// <param name="from">시작일 필터다.</param>
    /// <param name="to">종료일 필터다.</param>
    /// <param name="partner">거래처명 필터다.</param>
    /// <param name="status">상태 필터다.</param>
    /// <param name="ct">취소 토큰이다.</param>
    /// <returns>견적서 목록이다.</returns>
    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? partner,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var result = await _quotationService.GetListAsync(tenantId, from, to, partner, status, ct);
        return Ok(result);
    }

    /// <summary>
    /// 견적서 상세를 조회한다.
    /// </summary>
    /// <param name="id">견적서 식별자다.</param>
    /// <param name="ct">취소 토큰이다.</param>
    /// <returns>견적서 상세다.</returns>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var result = await _quotationService.GetAsync(tenantId, id, ct);
        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    /// <summary>
    /// 견적서를 생성한다.
    /// </summary>
    /// <param name="request">생성 요청 데이터다.</param>
    /// <param name="ct">취소 토큰이다.</param>
    /// <returns>생성된 견적서 식별자다.</returns>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateQuotationRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var id = await _quotationService.CreateAsync(tenantId, request, ct);
        return Created($"/api/quotations/{id}", new { id });
    }

    /// <summary>
    /// 견적서를 수정한다.
    /// </summary>
    /// <param name="id">견적서 식별자다.</param>
    /// <param name="request">수정 요청 데이터다.</param>
    /// <param name="ct">취소 토큰이다.</param>
    /// <returns>수정 결과다.</returns>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateQuotationRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _quotationService.UpdateAsync(tenantId, id, request, ct);
        return Ok();
    }

    /// <summary>
    /// 견적서를 소프트 삭제한다.
    /// </summary>
    /// <param name="id">견적서 식별자다.</param>
    /// <param name="ct">취소 토큰이다.</param>
    /// <returns>삭제 결과다.</returns>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _quotationService.DeleteAsync(tenantId, id, ct);
        return Ok();
    }

    /// <summary>
    /// 견적서를 수주서로 전환한다.
    /// </summary>
    /// <param name="id">견적서 식별자다.</param>
    /// <param name="ct">취소 토큰이다.</param>
    /// <returns>전환된 수주서 식별자다.</returns>
    [HttpPost("{id}/convert")]
    public async Task<IActionResult> ConvertToSalesOrder(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var convertedOrderId = await _quotationService.ConvertToSalesOrderAsync(tenantId, id, ct);
        return Ok(new { id, convertedOrderId, status = "converted" });
    }
}
