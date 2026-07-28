using HitPan.Application.DTOs.Billing;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 토스페이먼츠 Webhook 처리 (사장님 결재 2026-06-01)
/// 헌법 #5·#23 정합: 서명 검증 + 멱등성
/// </summary>
public interface ITossWebhookService
{
    /// <summary>
    /// 서명 검증 (HMAC-SHA256)
    /// </summary>
    bool VerifySignature(string rawBody, string signatureHeader);

    /// <summary>
    /// 페이로드 처리 (멱등 — paymentKey 기준)
    /// </summary>
    Task<TossWebhookResult> ProcessAsync(TossWebhookPayload payload, CancellationToken ct = default);
}
