using HitPan.Application.DTOs.Billing;

namespace HitPan.Tests.Workflow;

// 토스페이먼츠 Webhook DTO 검증 (사장님 결재 2026-06-01)
// 헌법 #5·#23 정합 — 멱등성·서명·5초 응답
public class TossWebhookDtoTests
{
    [Fact(DisplayName = "TW-01: TossWebhookPayload 필수 필드")]
    public void TossWebhookPayload_should_carry_required_fields()
    {
        var payload = new TossWebhookPayload(
            EventType: "PAYMENT_DONE",
            PaymentKey: "test_payment_key_abc",
            OrderId: "order_2026_06_01_001",
            Status: "DONE",
            Amount: 59000,
            Method: "CARD",
            EventAt: new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal("PAYMENT_DONE", payload.EventType);
        Assert.Equal("test_payment_key_abc", payload.PaymentKey);
        Assert.Equal(59000, payload.Amount);
        Assert.Equal("CARD", payload.Method);
    }

    [Fact(DisplayName = "TW-02: TossWebhookResult Accepted=true")]
    public void TossWebhookResult_should_accept_processed()
    {
        var ok = new TossWebhookResult(true, "processed");
        Assert.True(ok.Accepted);
        Assert.Equal("processed", ok.Reason);
    }

    [Fact(DisplayName = "TW-03: TossWebhookResult 거부 사유 저장")]
    public void TossWebhookResult_should_reject_with_reason()
    {
        var fail = new TossWebhookResult(false, "invalid signature");
        Assert.False(fail.Accepted);
        Assert.Equal("invalid signature", fail.Reason);
    }

    [Theory(DisplayName = "TW-04: 3가지 이벤트 타입 지원")]
    [InlineData("PAYMENT_DONE", "DONE")]
    [InlineData("PAYMENT_FAILED", "FAILED")]
    [InlineData("PAYMENT_REFUNDED", "CANCELED")]
    public void TossWebhookPayload_should_support_3_event_types(string eventType, string status)
    {
        var payload = new TossWebhookPayload(eventType, "k", "o", status, 100, "CARD", DateTime.UtcNow);
        Assert.Equal(eventType, payload.EventType);
        Assert.Equal(status, payload.Status);
    }
}
