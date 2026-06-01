using System.Text.Json;
using HitPan.Application.DTOs.Billing;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

/// <summary>
/// 토스페이먼츠 Webhook 수신 (사장님 결재 2026-06-01)
///
/// 보안:
///  - 헌법 #5: HMAC-SHA256 서명 검증 (TossPayments-Signature 헤더)
///  - 헌법 #23: paymentKey 멱등 처리
///  - 5초 이내 응답 (토스 정책)
/// </summary>
[ApiController]
[Route("api/billing/webhook")]
public class TossWebhookController : ControllerBase
{
    private readonly ITossWebhookService _webhookService;
    private readonly ILogger<TossWebhookController> _logger;

    public TossWebhookController(ITossWebhookService webhookService, ILogger<TossWebhookController> logger)
    {
        _webhookService = webhookService;
        _logger = logger;
    }

    [HttpPost("toss")]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveToss(CancellationToken ct)
    {
        Request.EnableBuffering();

        string rawBody;
        Request.Body.Position = 0;
        using (var reader = new StreamReader(Request.Body, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync(ct);
            Request.Body.Position = 0;
        }

        var signature = Request.Headers["TossPayments-Signature"].ToString();
        if (string.IsNullOrEmpty(signature))
        {
            _logger.LogWarning("Toss webhook missing signature header");
            return BadRequest(new { error = "missing signature" });
        }

        if (!_webhookService.VerifySignature(rawBody, signature))
        {
            _logger.LogWarning("Toss webhook signature invalid");
            return Unauthorized(new { error = "invalid signature" });
        }

        TossWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TossWebhookPayload>(rawBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Toss webhook payload parse failed");
            return BadRequest(new { error = "invalid payload" });
        }

        if (payload is null) return BadRequest(new { error = "empty payload" });

        var result = await _webhookService.ProcessAsync(payload, ct);
        if (!result.Accepted)
        {
            _logger.LogWarning("Toss webhook processing failed: {Reason}", result.Reason);
            return StatusCode(500, new { error = result.Reason });
        }

        return Ok(new { accepted = true, reason = result.Reason });
    }
}
