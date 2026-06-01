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

        // 멱등: payment_key 중복 차단
        var exists = await db.QueryFirstOrDefaultAsync<int?>(
            "SELECT 1 FROM billing_payment_attempts WHERE payment_key = @Key LIMIT 1",
            new { Key = payload.PaymentKey });

        if (exists.HasValue)
        {
            _logger.LogInformation("Toss webhook idempotent skip: paymentKey={Key}", payload.PaymentKey);
            return new TossWebhookResult(true, "already processed");
        }

        try
        {
            await db.ExecuteAsync(@"
                INSERT INTO billing_payment_attempts
                  (attempt_id, payment_key, order_id, event_type, status, amount, method, event_at, processed_at)
                VALUES
                  (@AttemptId, @PaymentKey, @OrderId, @EventType, @Status, @Amount, @Method, @EventAt, UTC_TIMESTAMP())",
                new
                {
                    AttemptId = Guid.NewGuid().ToString(),
                    PaymentKey = payload.PaymentKey,
                    OrderId = payload.OrderId,
                    EventType = payload.EventType,
                    Status = payload.Status,
                    Amount = payload.Amount,
                    Method = payload.Method,
                    EventAt = payload.EventAt
                });

            // TODO 후속 작지:
            //  - PAYMENT_DONE → billing_invoices.status = 'paid'
            //  - PAYMENT_FAILED → 재시도 큐
            //  - PAYMENT_REFUNDED → 환불 워크플로우

            return new TossWebhookResult(true, "processed");
        }
        catch (Exception ex)
        {
            // billing_payment_attempts 컬럼이 일부 미박제일 수 있음 (스키마는 DB-25 기준)
            // 운영 사고 추적 가능하게 LogWarning (헌법 #15)
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
