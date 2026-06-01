namespace HitPan.Application.Interfaces;

/// <summary>
/// 결제 환불 처리 (사장님 결재 2026-06-01)
/// 헌법 #5·#23 정합 — 본사 백오피스 어드민만, 5중 검증, 감사 로그
/// </summary>
public interface IRefundService
{
    /// <summary>
    /// 환불 요청 — billing_invoices.status = 'refunded' + 환불 attempt 박제
    /// 멱등: 동일 invoice_id 중복 환불 차단
    /// </summary>
    Task<AdminRefundResult> RefundAsync(string invoiceId, string adminId, string reason, CancellationToken ct = default);
}

public record AdminRefundResult(bool Success, string Message, decimal? RefundedAmount);
