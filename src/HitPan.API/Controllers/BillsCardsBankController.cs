using HitPan.Application.DTOs.Finance;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 어음·카드결제·은행거래 API (사장님 결재 2026-04-29).
/// 회계관리(§6 재무) 영역 — TenantOnly (어드민 + 일반 직원 모두 조회·등록 가능).
/// </summary>
[ApiController]
[Route("api/finance")]
[Authorize(Policy = "TenantOnly")]
public sealed class BillsCardsBankController : HitPanControllerBase
{
    private readonly IBillsCardsBankService _service;

    public BillsCardsBankController(IBillsCardsBankService service)
    {
        _service = service;
    }

    // ─── Bills ────────────────────────────────────────────
    [HttpGet("bills")]
    public async Task<IActionResult> ListBills(
        [FromQuery] string? type, [FromQuery] string? status,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        if (EnsureTenant() is { } err) return err;
        var rows = await _service.ListBillsAsync(TenantId!, type, status, from, to, ct).ConfigureAwait(false);
        return Ok(rows);
    }

    [HttpPost("bills")]
    public async Task<IActionResult> CreateBill([FromBody] CreateBillRequest req, CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        var id = await _service.CreateBillAsync(TenantId!, req, ct).ConfigureAwait(false);
        return Ok(new { billId = id });
    }

    [HttpPut("bills/{billId}/status")]
    public async Task<IActionResult> UpdateBillStatus(string billId, [FromBody] UpdateBillStatusRequest req, CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        await _service.UpdateBillStatusAsync(TenantId!, billId, req, ct).ConfigureAwait(false);
        return Ok();
    }

    // ─── Card Payments ───────────────────────────────────
    [HttpGet("card-payments")]
    public async Task<IActionResult> ListCardPayments(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct = default)
    {
        if (EnsureTenant() is { } err) return err;
        var rows = await _service.ListCardPaymentsAsync(TenantId!, from, to, ct).ConfigureAwait(false);
        return Ok(rows);
    }

    [HttpGet("card-payments/{cardPaymentId}")]
    public async Task<IActionResult> GetCardPayment(string cardPaymentId, CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        var dto = await _service.GetCardPaymentAsync(TenantId!, cardPaymentId, ct).ConfigureAwait(false);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost("card-payments")]
    public async Task<IActionResult> CreateCardPayment([FromBody] CreateCardPaymentRequest req, CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        var id = await _service.CreateCardPaymentAsync(TenantId!, req, ct).ConfigureAwait(false);
        return Ok(new { cardPaymentId = id });
    }

    // ─── Bank Transactions ──────────────────────────────
    [HttpGet("bank-transactions")]
    public async Task<IActionResult> ListBankTx(
        [FromQuery] string? accountNo,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int? limit,
        CancellationToken ct = default)
    {
        if (EnsureTenant() is { } err) return err;
        // 헌법 #19 정합 — limit 미지정 시 500 (5/27 P1-4: 3,770행 폭탄 봉합)
        var rows = await _service.ListBankTxAsync(TenantId!, accountNo, from, to, limit ?? 500, ct).ConfigureAwait(false);
        return Ok(rows);
    }

    [HttpPost("bank-transactions")]
    public async Task<IActionResult> CreateBankTx([FromBody] CreateBankTxRequest req, CancellationToken ct)
    {
        if (EnsureTenant() is { } err) return err;
        var id = await _service.CreateBankTxAsync(TenantId!, req, ct).ConfigureAwait(false);
        return Ok(new { bankTxId = id });
    }
}
