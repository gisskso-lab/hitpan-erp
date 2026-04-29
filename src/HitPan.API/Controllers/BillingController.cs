using HitPan.Application.DTOs.Billing;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>구독결제 관리 API (사장님 결재 2026-04-29).</summary>
[ApiController]
[Route("api/billing")]
[Authorize(Policy = "TenantAdminOnly")]
public sealed class BillingController : ControllerBase
{
    private readonly IBillingService _service;

    public BillingController(IBillingService service)
    {
        _service = service;
    }

    // ─── 운영 설정 ───
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        var dto = await _service.GetSettingsAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(dto);
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateBillingSettingsRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        await _service.UpdateSettingsAsync(tenantId, request, ct).ConfigureAwait(false);
        return Ok();
    }

    // ─── 결제수단 ───
    [HttpGet("payment-methods")]
    public async Task<IActionResult> GetPaymentMethods(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        var list = await _service.GetPaymentMethodsAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(list);
    }

    [HttpPost("payment-methods")]
    public async Task<IActionResult> RegisterPaymentMethod(
        [FromBody] RegisterPaymentMethodRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        var id = await _service.RegisterPaymentMethodAsync(tenantId, request, ct).ConfigureAwait(false);
        return Ok(new { paymentMethodId = id });
    }

    [HttpPost("payment-methods/{id}/default")]
    public async Task<IActionResult> SetDefault(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        await _service.SetDefaultPaymentMethodAsync(tenantId, id, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpDelete("payment-methods/{id}")]
    public async Task<IActionResult> DeletePaymentMethod(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        await _service.DeletePaymentMethodAsync(tenantId, id, ct).ConfigureAwait(false);
        return Ok();
    }

    // ─── 구독 ───
    [HttpGet("subscription")]
    public async Task<IActionResult> GetSubscription(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        var dto = await _service.GetCurrentSubscriptionAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(dto);
    }

    // ─── 인보이스 ───
    [HttpGet("invoices")]
    public async Task<IActionResult> GetInvoices(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        var list = await _service.GetInvoicesAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(list);
    }

    [HttpGet("invoices/{id}")]
    public async Task<IActionResult> GetInvoice(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        var dto = await _service.GetInvoiceAsync(tenantId, id, ct).ConfigureAwait(false);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpPost("invoices/{id}/pay")]
    public async Task<IActionResult> Pay(string id, [FromBody] PayInvoiceRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        var ok = await _service.PayInvoiceAsync(tenantId, id, request, ct).ConfigureAwait(false);
        return Ok(new { success = ok });
    }

    [HttpPost("invoices/{id}/mark-paid")]
    public async Task<IActionResult> MarkPaid(string id, [FromBody] MarkPaidRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        await _service.MarkInvoicePaidManuallyAsync(tenantId, id, request, ct).ConfigureAwait(false);
        return Ok();
    }
}
