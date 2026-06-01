using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using HitPan.Application.DTOs.Billing;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 토스페이먼츠 Webhook 처리 (사장님 결재 2026-06-01)
///
/// 헌법:
///  - #5 (Webhook 시크릿 환경변수, 평문 응답 금지)
///  - #23 (서명 검증 + Idempotency + 5초 응답)
///
/// 정책:
///  - HMAC-SHA256 서명 검증
///  - paymentKey 기준 멱등 처리 (billing_payment_attempts INSERT)
/// </summary>
public class TossWebhookService : ITossWebhookService
{
    private readonly IConfiguration _config;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<TossWebhookService> _logger;

    public TossWebhookService(IConfiguration config, IUnitOfWork uow, ILogger<TossWebhookService> logger)
    {
        _config = config;
        _uow = uow;
        _logger = logger;
    }

    public bool VerifySignature(string rawBody, string signatureHeader)
    {
        var secret = _config["TOSS_WEBHOOK_SECRET"]
            ?? Environment.GetEnvironmentVariable("TOSS_WEBHOOK_SECRET");
        if (string.IsNullOrEmpty(secret))
        {
            _logger.LogError("TOSS_WEBHOOK_SECRET not configured");
            return false;
        }
        if (string.IsNullOrEmpty(signatureHeader)) return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        var expected = Convert.ToHexString(computed);

        // CryptographicOperations.FixedTimeEquals 사용 (타이밍 공격 차단)
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var signatureBytes = Encoding.UTF8.GetBytes(signatureHeader);
        if (expectedBytes.Length != signatureBytes.Length) return false;
        return CryptographicOperations.FixedTimeEquals(expectedBytes, signatureBytes);
    }

    public async Task<TossWebhookResult> ProcessAsync(TossWebhookPayload payload, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(payload.PaymentKey))
            return new TossWebhookResult(false, "missing paymentKey");

        var db = _uow.GetDbConnection();
        await EnsureOpenAsync(db, ct).ConfigureAwait(false);

        // 멱등: provider_response_json 내 paymentKey 검색 (기존 DB-25 스키마 호환)
        var exists = await db.QueryFirstOrDefaultAsync<int?>(@"
            SELECT 1 FROM billing_payment_attempts
            WHERE provider = 'toss'
              AND provider_response_json LIKE @Pattern
            LIMIT 1",
            new { Pattern = $"%\"paymentKey\":\"{payload.PaymentKey}\"%" });

        if (exists.HasValue)
        {
            _logger.LogInformation("Toss webhook idempotent skip: paymentKey={Key}", payload.PaymentKey);
            return new TossWebhookResult(true, "already processed");
        }

        try
        {
            // order_id 기준으로 invoice 찾기 (랜딩 가입 시 order_id = invoice_id 정합 박제 예정)
            var invoiceId = await db.ExecuteScalarAsync<string?>(@"
                SELECT CAST(invoice_id AS CHAR) FROM billing_invoices
                WHERE invoice_no = @OrderId OR invoice_id = @OrderId
                LIMIT 1",
                new { OrderId = payload.OrderId });

            var tenantId = string.IsNullOrEmpty(invoiceId) ? "00000000-0000-0000-0000-000000000000" :
                await db.ExecuteScalarAsync<string?>(
                    "SELECT CAST(tenant_id AS CHAR) FROM billing_invoices WHERE invoice_id = @InvoiceId",
                    new { InvoiceId = invoiceId }) ?? "00000000-0000-0000-0000-000000000000";

            var providerResponse = System.Text.Json.JsonSerializer.Serialize(payload);

            await db.ExecuteAsync(@"
                INSERT INTO billing_payment_attempts
                  (attempt_id, tenant_id, invoice_id, provider, attempted_at, status, provider_response_json)
                VALUES
                  (@AttemptId, @TenantId, @InvoiceId, 'toss', @AttemptedAt, @Status, @ProviderResponse)",
                new
                {
                    AttemptId = Guid.NewGuid().ToString(),
                    TenantId = tenantId,
                    InvoiceId = invoiceId ?? "00000000-0000-0000-0000-000000000000",
                    AttemptedAt = payload.EventAt,
                    Status = payload.Status,
                    ProviderResponse = providerResponse
                });

            // PAYMENT_DONE → billing_invoices.status = 'paid' (멱등)
            if (payload.EventType == "PAYMENT_DONE" && !string.IsNullOrEmpty(invoiceId))
            {
                await db.ExecuteAsync(
                    "UPDATE billing_invoices SET status = 'paid' WHERE invoice_id = @InvoiceId AND status <> 'paid'",
                    new { InvoiceId = invoiceId });
            }

            // TODO 후속 작지:
            //  - PAYMENT_FAILED → 재시도 큐
            //  - PAYMENT_REFUNDED → 별도 환불 워크플로우 (RefundService)

            return new TossWebhookResult(true, "processed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Toss webhook insert failed for {Key}", payload.PaymentKey);
            return new TossWebhookResult(false, $"insert failed: {ex.Message}");
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
