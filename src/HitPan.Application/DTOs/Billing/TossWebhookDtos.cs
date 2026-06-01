namespace HitPan.Application.DTOs.Billing;

/// <summary>
/// 토스페이먼츠 Webhook 페이로드 (스켈레톤)
/// 사장님 결재 2026-06-01
/// </summary>
public record TossWebhookPayload(
    string EventType,    // PAYMENT_DONE / PAYMENT_FAILED / PAYMENT_REFUNDED
    string PaymentKey,   // 토스 결제 키
    string OrderId,      // 우리 주문 ID
    string Status,       // DONE / CANCELED / FAILED
    long? Amount,
    string? Method,      // CARD / TRANSFER / PHONE
    DateTime EventAt
);

public record TossWebhookResult(bool Accepted, string Reason);
