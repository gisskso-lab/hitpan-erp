using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Contracts.Idempotency;
using HitPan.Contracts.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 세금계산서 1계층 (내부 마킹) — DESIGN_PRINCIPLES §7 / 작업지시서 20260425작2
/// 2/3계층 (전자세금계산서)은 별도 컨트롤러·라운드.
/// </summary>
[ApiController]
[Route("api/sales/tax-invoices")]
[Authorize(Policy = "SalesOnly")]
public sealed class TaxInvoiceController : ControllerBase
{
    private readonly ITaxInvoiceService _service;

    public TaxInvoiceController(ITaxInvoiceService service)
    {
        _service = service;
    }

    /// <summary>거래명세서 → 계산서 발행 (1계층). Idempotency-Key 헤더 필수.</summary>
    [HttpPost]
    [IdempotencyKey]
    public async Task<IActionResult> Issue(
        [FromBody] IssueTaxInvoiceRequest request,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) return Forbid();

        var idempotencyKey = Request.Headers[IdempotencyConstants.HeaderName].ToString();

        try
        {
            var result = await _service.IssueAsync(request, tenantId, userId, idempotencyKey, ct);
            return Created($"/api/sales/tax-invoices/{result.InvoiceId}", result);
        }
        catch (TaxInvoiceException ex)
        {
            return ex.ErrorCode switch
            {
                "delivery_not_found"     => NotFound(new TaxInvoiceErrorResponse(ex.ErrorCode, ex.Message)),
                "delivery_not_confirmed" => Conflict(new TaxInvoiceErrorResponse(ex.ErrorCode, ex.Message)),
                "already_issued"         => Conflict(new TaxInvoiceErrorResponse(ex.ErrorCode, ex.Message)),
                _                        => BadRequest(new TaxInvoiceErrorResponse(ex.ErrorCode, ex.Message))
            };
        }
    }

    /// <summary>마이너스 계산서(매출반품) 발행 — 20260828작13. Idempotency-Key 헤더 필수.</summary>
    /// <remarks>
    /// 사장님 오더: 매출반품은 마이너스 전표·마이너스 계산서로 끊는다.
    /// 사슬연결도는 tax_invoices.source_type/source_id 축으로 남는다.
    /// </remarks>
    [HttpPost("credit-notes")]
    [IdempotencyKey]
    public async Task<IActionResult> IssueCreditNote(
        [FromBody] IssueCreditNoteRequest request,
        CancellationToken ct)
    {
        // 헌법 #2 — tenant_id 는 JWT 클레임에서만. 파라미터로 받지 않는다.
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) return Forbid();

        var idempotencyKey = Request.Headers[IdempotencyConstants.HeaderName].ToString();

        try
        {
            var result = await _service.IssueCreditNoteAsync(request, tenantId, userId, idempotencyKey, ct);
            return Created($"/api/sales/tax-invoices/{result.InvoiceId}", result);
        }
        catch (TaxInvoiceException ex)
        {
            return ex.ErrorCode switch
            {
                "return_not_found"     => NotFound(new TaxInvoiceErrorResponse(ex.ErrorCode, ex.Message)),
                "return_not_confirmed" => Conflict(new TaxInvoiceErrorResponse(ex.ErrorCode, ex.Message)),
                "already_issued"       => Conflict(new TaxInvoiceErrorResponse(ex.ErrorCode, ex.Message)),
                _                      => BadRequest(new TaxInvoiceErrorResponse(ex.ErrorCode, ex.Message))
            };
        }
    }

    /// <summary>계산서 단건 조회</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var result = await _service.GetAsync(id, tenantId, ct);
        return result is null
            ? NotFound(TaxInvoiceErrorResponse.DeliveryNotFound())
            : Ok(result);
    }

    /// <summary>목록 조회 (필터: 기간·거래처)</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? partnerId,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var result = await _service.ListAsync(tenantId, from, to, partnerId, ct);
        return Ok(result);
    }

    /// <summary>계산서 취소 (역분개·summary 차감은 별도 라운드)</summary>
    [HttpPost("{id}/cancel")]
    [IdempotencyKey]
    public async Task<IActionResult> Cancel(
        string id,
        [FromBody] CancelTaxInvoiceRequest request,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) return Forbid();

        try
        {
            var result = await _service.CancelAsync(id, request, tenantId, userId, ct);
            return Ok(result);
        }
        catch (TaxInvoiceException ex)
        {
            return ex.ErrorCode switch
            {
                "not_found"        => NotFound(new TaxInvoiceErrorResponse(ex.ErrorCode, ex.Message)),
                "already_canceled" => Conflict(new TaxInvoiceErrorResponse(ex.ErrorCode, ex.Message)),
                _                  => BadRequest(new TaxInvoiceErrorResponse(ex.ErrorCode, ex.Message))
            };
        }
    }
}
