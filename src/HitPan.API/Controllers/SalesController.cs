using HitPan.Application.DTOs.Sales;
using HitPan.Application.Interfaces;
using HitPan.Contracts.Idempotency;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/sales")]
[Authorize(Policy = "SalesOnly")]
public class SalesController : ControllerBase
{
    private readonly ISalesService _salesService;

    public SalesController(ISalesService salesService)
    {
        _salesService = salesService;
    }

    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateSalesOrderRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var id = await _salesService.CreateOrderAsync(request, ct);
        return Created($"/api/sales/orders/{id}", new { id });
    }

    [HttpGet("orders")]
    public async Task<IActionResult> GetOrders(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var result = await _salesService.GetOrdersAsync(tenantId, from, to, status, ct);
        return Ok(result);
    }

    [HttpGet("deliveries/{id}/auto-order-candidates")]
    public async Task<IActionResult> GetAutoOrderCandidates(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var list = await _salesService.GetAutoOrderCandidatesAsync(id, tenantId, ct);
        return Ok(list);
    }

    [HttpPost("auto-orders")]
    public async Task<IActionResult> CreateAutoOrders(
        [FromBody] List<AutoOrderCandidateDto> candidates,
        [FromQuery] bool autoReceive,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var results = await _salesService.CreateAutoOrdersAsync(candidates ?? new(), tenantId, autoReceive, ct);
        return Ok(results);
    }

    [HttpGet("orders/{id}")]
    public async Task<IActionResult> GetOrder(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var detail = await _salesService.GetOrderDetailAsync(id, tenantId, ct);
        if (detail is null) return NotFound();
        return Ok(detail);
    }

    // 봉합 (2026-06-22, 11차전 수주재편집): 수주(draft) 재편집 엔드포인트.
    //   부재 시 프론트가 PUT api/sales/deliveries로 잘못 흘러 거래명세서 조회 실패
    //   → "거래명세서를 찾을 수 없습니다" 발생. CreateOrder/GetOrder 패턴 동일.
    [HttpPut("orders/{id}")]
    public async Task<IActionResult> UpdateOrder(string id, [FromBody] UpdateSalesOrderRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        try
        {
            await _salesService.UpdateOrderAsync(id, request, tenantId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            // "찾을 수 없습니다"는 404, 그 외(draft 아님·품목 0)는 400.
            if (ex.Message.Contains("찾을 수 없습니다"))
            {
                return NotFound(new { message = ex.Message });
            }
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("orders/{id}")]
    public async Task<IActionResult> DeleteOrder(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        try
        {
            await _salesService.DeleteSalesOrderAsync(id, tenantId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("orders/{id}/convert-to-delivery")]
    public async Task<IActionResult> ConvertOrderToDelivery(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var (deliveryId, documentNumber) = await _salesService.ConvertOrderToDeliveryAsync(id, tenantId, ct);
        return Ok(new { deliveryId, documentNumber });
    }

    [HttpPost("deliveries")]
    public async Task<IActionResult> CreateDelivery([FromBody] CreateDeliveryRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var (id, documentNumber) = await _salesService.CreateDeliveryAsync(request, ct);
        return Created($"/api/sales/deliveries/{id}", new { id, documentNumber });
    }

    [HttpPost("deliveries/{id}/confirm")]
    [IdempotencyKey]
    public async Task<IActionResult> ConfirmDelivery(string id, [FromBody] ConfirmDeliveryRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        await _salesService.ConfirmDeliveryAsync(id, request, ct);
        return Ok(new { id, status = "confirmed" });
    }

    /// <summary>거래명세서 일괄 확정 — 프론트 SalesListDialog "계산서 발행" 버튼의 백엔드 엔드포인트.</summary>
    /// 성공/실패를 건별로 분리 반환해 UI가 정직하게 집계한다 (헌법 #20).
    [HttpPost("deliveries/bulk-confirm")]
    public async Task<IActionResult> BulkConfirmDeliveries([FromBody] BulkConfirmDeliveriesRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var success = new List<string>();
        var failed = new List<BulkConfirmFailureItem>();
        var ids = request?.DeliveryIds ?? new List<string>();

        foreach (var id in ids)
        {
            try
            {
                await _salesService.ConfirmDeliveryAsync(id, new ConfirmDeliveryRequest(), ct);
                success.Add(id);
            }
            catch (Exception ex)
            {
                failed.Add(new BulkConfirmFailureItem { Id = id, Reason = ex.Message });
            }
        }

        return Ok(new { success, failed });
    }

    [HttpGet("deliveries")]
    public async Task<IActionResult> GetDeliveries(
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

        var result = await _salesService.GetDeliveriesAsync(tenantId, from, to, partner, status, ct);
        return Ok(result);
    }

    [HttpGet("deliveries/{id}")]
    public async Task<IActionResult> GetDelivery(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var result = await _salesService.GetDeliveryAsync(id, tenantId, ct);
        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpPut("deliveries/{id}")]
    [Authorize(Policy = "SalesManager")]
    public async Task<IActionResult> UpdateDelivery(string id, [FromBody] UpdateDeliveryDto dto, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = User.FindFirst("employee_id")?.Value;
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _salesService.UpdateDeliveryAsync(id, dto, tenantId, userId ?? string.Empty, ct);
        return Ok();
    }

    [HttpDelete("deliveries/{id}")]
    [Authorize(Policy = "SalesManager")]
    public async Task<IActionResult> DeleteDelivery(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        await _salesService.DeleteDeliveryAsync(id, tenantId, ct);
        return Ok();
    }

    /// <summary>
    /// 확정된 거래명세서 취소 — Reverse 원장 발행으로 재고·잔액·수금 전체 복귀.
    /// draft는 DELETE 엔드포인트 사용.
    /// </summary>
    [HttpPost("deliveries/{id}/cancel")]
    [Authorize(Policy = "SalesManager")]
    public async Task<IActionResult> CancelConfirmedDelivery(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }
        var employeeId = HttpContext.Items["EmployeeId"]?.ToString();
        try
        {
            await _salesService.CancelConfirmedDeliveryAsync(id, tenantId, employeeId, ct);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 매출반품 — 13차 후순위 봉합(2026-06-22, A 매입반품 대칭 풀스택).
    // 매입반품(PurchaseController returns/*) 엔드포인트의 거울. status 필터 GET 포함.
    // ─────────────────────────────────────────────────────────────────────

    [HttpGet("returns")]
    public async Task<IActionResult> GetSalesReturns(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? status, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        var list = await _salesService.GetSalesReturnsAsync(tenantId, from, to, status, ct);
        return Ok(list);
    }

    [HttpGet("returns/{id}")]
    public async Task<IActionResult> GetSalesReturnDetail(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        var detail = await _salesService.GetSalesReturnDetailAsync(id, tenantId, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost("returns")]
    [Authorize(Policy = "SalesManager")]
    public async Task<IActionResult> CreateSalesReturn([FromBody] CreateSalesReturnRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        try
        {
            var (returnId, returnNo) = await _salesService.CreateSalesReturnAsync(request, tenantId, ct);
            return Ok(new { returnId, returnNo });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("returns/{id}")]
    [Authorize(Policy = "SalesManager")]
    public async Task<IActionResult> UpdateSalesReturn(string id, [FromBody] UpdateSalesReturnRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        try
        {
            await _salesService.UpdateSalesReturnAsync(id, request, tenantId, ct);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("returns/{id}/confirm")]
    [Authorize(Policy = "SalesManager")]
    public async Task<IActionResult> ConfirmSalesReturn(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        var employeeId = HttpContext.Items["EmployeeId"]?.ToString();
        try
        {
            await _salesService.ConfirmSalesReturnAsync(id, tenantId, employeeId, ct);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // 매출반품 취소 — confirmed → canceled (15차 적대검증 15-P1 봉합). confirm 대칭.
    [HttpPost("returns/{id}/cancel")]
    [Authorize(Policy = "SalesManager")]
    public async Task<IActionResult> CancelSalesReturn(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        var employeeId = HttpContext.Items["EmployeeId"]?.ToString();
        try
        {
            await _salesService.CancelSalesReturnAsync(id, tenantId, employeeId, ct);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("returns/{id}")]
    [Authorize(Policy = "SalesManager")]
    public async Task<IActionResult> DeleteSalesReturn(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        try
        {
            await _salesService.DeleteSalesReturnAsync(id, tenantId, ct);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

public sealed class BulkConfirmDeliveriesRequest
{
    public List<string> DeliveryIds { get; set; } = new();
}

public sealed class BulkConfirmFailureItem
{
    public string Id { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
