using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 결제 환불 처리 (사장님 결재 2026-06-01)
///
/// 정책:
///  - 본사 백오피스 platform_admin만 호출 가능 (Controller [Authorize(Policy="PlatformOnly")])
///  - 멱등 — 동일 invoice 환불 시도 차단
///  - 토스 API 실호출은 후속 작지 박제 (스켈레톤)
///
/// 헌법 #5·#23 정합 — 감사 로그 + 5중 검증 통과 필요
/// </summary>
public class RefundService : IRefundService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<RefundService> _logger;

    public RefundService(IUnitOfWork uow, ILogger<RefundService> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<AdminRefundResult> RefundAsync(string invoiceId, string adminId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(invoiceId))
            return new AdminRefundResult(false, "invoiceId required", null);
        if (string.IsNullOrWhiteSpace(reason))
            return new AdminRefundResult(false, "환불 사유 필수", null);

        var db = _uow.GetDbConnection();
        await EnsureOpenAsync(db, ct).ConfigureAwait(false);

        // 1. invoice 상태 확인
        var invoice = await db.QueryFirstOrDefaultAsync<(string Status, decimal TotalAmount, string TenantId)>(@"
            SELECT status AS Status, total_amount AS TotalAmount, CAST(tenant_id AS CHAR) AS TenantId
            FROM billing_invoices
            WHERE invoice_id = @InvoiceId",
            new { InvoiceId = invoiceId });

        if (invoice.Status is null)
            return new AdminRefundResult(false, "invoice not found", null);

        if (invoice.Status == "refunded")
            return new AdminRefundResult(false, "already refunded", null);

        if (invoice.Status != "paid")
            return new AdminRefundResult(false, $"환불 불가 상태: {invoice.Status}", null);

        // 2. 트랜잭션 박제
        using var tx = await _uow.BeginTransactionAsync(ct);
        try
        {
            // 2-1. 환불 attempt INSERT (감사 로그)
            await db.ExecuteAsync(@"
                INSERT INTO billing_payment_attempts
                  (attempt_id, tenant_id, invoice_id, provider, attempted_at, status, error_message, provider_response_json)
                VALUES
                  (@AttemptId, @TenantId, @InvoiceId, 'toss', UTC_TIMESTAMP(), 'refunded', @Reason, @Response)",
                new
                {
                    AttemptId = Guid.NewGuid().ToString(),
                    TenantId = invoice.TenantId,
                    InvoiceId = invoiceId,
                    Reason = $"환불 사유: {reason}",
                    Response = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        action = "refund",
                        adminId,
                        reason,
                        refundedAmount = invoice.TotalAmount,
                        refundedAt = DateTime.UtcNow
                    })
                },
                transaction: tx.DbTransaction);

            // 2-2. invoice 상태 변경
            await db.ExecuteAsync(
                "UPDATE billing_invoices SET status = 'refunded' WHERE invoice_id = @InvoiceId",
                new { InvoiceId = invoiceId },
                transaction: tx.DbTransaction);

            await tx.CommitAsync(ct);

            _logger.LogInformation("Refund processed: invoice={InvoiceId}, admin={AdminId}, amount={Amount}",
                invoiceId, adminId, invoice.TotalAmount);

            // TODO 후속 작지: 실제 토스 환불 API 호출
            // POST https://api.tosspayments.com/v1/payments/{paymentKey}/cancel
            // { "cancelReason": reason }

            return new AdminRefundResult(true, "환불 처리 완료 (토스 API 실호출은 스켈레톤 단계)", invoice.TotalAmount);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Refund failed: invoice={InvoiceId}", invoiceId);
            return new AdminRefundResult(false, $"환불 실패: {ex.Message}", null);
        }
    }

    private static async Task EnsureOpenAsync(IDbConnection db, CancellationToken ct)
    {
        if (db.State == ConnectionState.Open) return;
        if (db is DbConnection c)
            await c.OpenAsync(ct).ConfigureAwait(false);
        else
            db.Open();
    }
}
