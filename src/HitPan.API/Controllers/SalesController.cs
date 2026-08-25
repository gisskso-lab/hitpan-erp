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

        // 20260825작5: autoCreatedOrderNo 는 수주 없이 들어와 수주서를 자동 생성했을 때만 채워진다.
        // 화면이 진짜 수주번호를 알아야 브레드크럼에 지어낸 번호 대신 실물을 띄울 수 있다.
        var (id, documentNumber, autoCreatedOrderNo) = await _salesService.CreateDeliveryAsync(request, ct);
        return Created($"/api/sales/deliveries/{id}", new { id, documentNumber, autoCreatedOrderNo });
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


    /// <summary>
    /// 지금까지 쓰인 매출반품 사유 목록을 조회한다 — 자율 입력값의 재사용 (20260825작6).
    /// </summary>
    [HttpGet("returns/reasons")]
    public async Task<IActionResult> GetSalesReturnReasons(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var reasons = await _salesService.GetSalesReturnReasonsAsync(tenantId, ct);
        return Ok(reasons);
    }
    [HttpGet("returns/{id}")]
    public async Task<IActionResult> GetSalesReturnDetail(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        try
        {
            var detail = await _salesService.GetSalesReturnDetailAsync(id, tenantId, ct);
            return detail is null ? NotFound() : Ok(detail);
        }
        // 🔴 20260825작12 — 여기엔 try/catch 자체가 없었다.
        //   작10 이 confirm·cancel 두 액션에만 이 catch 를 달았는데,
        //   사용자는 확정을 누르기 **전에** 이 상세조회를 먼저 지나간다.
        //   마이그 안 들어간 DB 에서 문서를 **여는 순간** 1054 → 미들웨어 마지막
        //   catch(Exception) → **500**. 사장님이 받으신 그 모양이다.
        catch (MySqlConnector.MySqlException ex) when (ex.Number is 1054 or 1146)
        {
            return BadRequest(new
            {
                message = "업데이트가 아직 다 적용되지 않아 반품 내용을 불러올 수 없습니다. "
                        + "히트판을 껐다 켜 주시고, 그래도 같으면 관리자에게 알려주세요."
            });
        }
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
        // 20260825작12 — 저장 경로도 확정·취소와 같은 안내를 준다(작10 이 여기까지 안 왔다).
        catch (MySqlConnector.MySqlException ex) when (ex.Number is 1054 or 1146)
        {
            return BadRequest(new
            {
                message = "업데이트가 아직 다 적용되지 않아 반품을 저장할 수 없습니다. "
                        + "히트판을 껐다 켜 주시고, 그래도 같으면 관리자에게 알려주세요."
            });
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
        // 20260825작12 — 생성과 대칭. 한쪽만 달면 "새로 만들면 되는데 고치면 500" 이 된다.
        catch (MySqlConnector.MySqlException ex) when (ex.Number is 1054 or 1146)
        {
            return BadRequest(new
            {
                message = "업데이트가 아직 다 적용되지 않아 반품을 저장할 수 없습니다. "
                        + "히트판을 껐다 켜 주시고, 그래도 같으면 관리자에게 알려주세요."
            });
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
        // 20260825작10 — 사장님 실측: 확정이 {"error":"서버 오류가 발생했습니다"} 500 으로 죽었다.
        //   실제 원인은 "Unknown column 'is_loss'"(마이그 미적용)였는데, 화면에는 아무 단서도 없었다.
        //   🔴 원인을 알 수 없는 것이 진짜 결함이다 — 오늘 하루를 여기에 썼다.
        //   스키마 부재(1054/1146)만 골라 "무엇이 문제인지" 를 돌려준다.
        //   ⚠️ 고객 화면이라 개발용어는 쓰지 않는다(컬럼명·SQL 노출 금지).
        catch (MySqlConnector.MySqlException ex) when (ex.Number is 1054 or 1146)
        {
            return BadRequest(new
            {
                message = "업데이트가 아직 다 적용되지 않아 반품확정을 할 수 없습니다. "
                        + "히트판을 껐다 켜 주시고, 그래도 같으면 관리자에게 알려주세요."
            });
        }
        // 🔴 20260825작13 — 사장님 실측 반려(1.3.15): 확정이 여전히 500 이었다.
        //   원장(stock_ledger·journal_entries)의 UNIQUE 제약 위반 = MySQL 1062.
        //   레포 전체에서 1062 를 잡는 곳이 **한 군데도 없어**(grep 0건) 500 으로 샜다.
        //   서비스 진입 가드가 1차로 막지만, 동시 클릭 등 경쟁 상황에서 여기까지 올 수 있다.
        //   ⇒ 안전망을 둔다. 이미 반영된 것이므로 사용자에겐 실패가 아니라 **상태 안내**다.
        catch (MySqlConnector.MySqlException ex) when (ex.Number == 1062)
        {
            return BadRequest(new
            {
                message = "이 반품은 이미 재고에 반영되어 있습니다. "
                        + "목록을 새로고침해 상태를 확인해주세요."
            });
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
        // 20260825작10: 확정과 대칭 — 한쪽만 고치면 되돌릴 때 또 "서버 오류"만 뜬다.
        catch (MySqlConnector.MySqlException ex) when (ex.Number is 1054 or 1146)
        {
            return BadRequest(new
            {
                message = "업데이트가 아직 다 적용되지 않아 반품취소를 할 수 없습니다. "
                        + "히트판을 껐다 켜 주시고, 그래도 같으면 관리자에게 알려주세요."
            });
        }
        // 20260825작13: 확정과 대칭 — 취소도 원장을 쓰므로 1062 가 날 수 있다.
        catch (MySqlConnector.MySqlException ex) when (ex.Number == 1062)
        {
            return BadRequest(new
            {
                message = "이 반품은 이미 처리되어 있습니다. 목록을 새로고침해 상태를 확인해주세요."
            });
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
