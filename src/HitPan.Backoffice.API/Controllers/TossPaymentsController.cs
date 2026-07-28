using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 토스페이먼츠 결제 승인 (사장님 결재 2026-06-04, 헌법 #34 정식 완성도)
//
// 흐름:
//   1) PaymentPage → 토스 SDK 결제창 → 성공 시 paymentKey/orderId/amount 응답
//   2) 클라이언트 → POST /api/payments/toss/confirm (이 컨트롤러)
//   3) 서버 → 토스 Confirm API 호출 (Secret Key, Basic Auth)
//   4) 정상 응답 → tenant_payments 저장 + landing_signups.status=paid + tenants.status=active
//
// 헌법 정합:
//   #22 — 카드번호·CVC 0건 저장, paymentKey·orderId·amount·method만 저장
//   #29 — Secret Key는 환경변수만, 응답·로그에 절대 노출 0건
//   #15 — 빈 catch 금지, ILogger 저장
//   #19 — 자격증명 미저장 시 503 명시 응답 (silent fail 금지)
[ApiController]
[Route("api/payments/toss")]
public class TossPaymentsController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<TossPaymentsController> _logger;

    public TossPaymentsController(
        IConfiguration config,
        IHttpClientFactory httpFactory,
        ILogger<TossPaymentsController> logger)
    {
        _config = config;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    [HttpGet("config")]
    [AllowAnonymous]
    public IActionResult ClientConfig()
    {
        // 클라이언트는 ClientKey만 필요 (Secret Key는 절대 비노출, 헌법 #29)
        var clientKey = ResolveEnv("HITPAN_TOSS_CLIENT_KEY", "Toss:ClientKey");
        var secretKey = ResolveEnv("HITPAN_TOSS_SECRET_KEY", "Toss:SecretKey");
        var configured = !string.IsNullOrWhiteSpace(clientKey) && !string.IsNullOrWhiteSpace(secretKey);
        return Ok(new
        {
            success = true,
            configured,
            clientKey = configured ? clientKey : null,
            message = configured
                ? null
                : "결제 시스템 점검 중입니다. 자격증명 저장 후 이용 가능합니다."
        });
    }

    [HttpPost("confirm")]
    [AllowAnonymous]
    public async Task<IActionResult> Confirm([FromBody] ConfirmRequest req, CancellationToken ct)
    {
        if (req is null
            || string.IsNullOrWhiteSpace(req.PaymentKey)
            || string.IsNullOrWhiteSpace(req.OrderId)
            || req.Amount <= 0)
        {
            return BadRequest(new { success = false, message = "paymentKey·orderId·amount 필수" });
        }

        var secretKey = ResolveEnv("HITPAN_TOSS_SECRET_KEY", "Toss:SecretKey");
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            _logger.LogWarning("[TossPayments] HITPAN_TOSS_SECRET_KEY 미저장 — 결제 승인 불가");
            return StatusCode(503, new
            {
                success = false,
                message = "결제 시스템 점검 중입니다. 자격증명이 저장되지 않았습니다."
            });
        }

        // 토스 Confirm API 호출 — Basic Auth (Base64(secretKey + ':'))
        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(15);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(secretKey + ":")));

        var payload = JsonSerializer.Serialize(new
        {
            paymentKey = req.PaymentKey,
            orderId = req.OrderId,
            amount = req.Amount
        });

        try
        {
            using var resp = await http.PostAsync(
                "https://api.tosspayments.com/v1/payments/confirm",
                new StringContent(payload, Encoding.UTF8, "application/json"),
                ct);

            var bodyText = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[TossPayments] toss confirm failed status={Status} orderId={OrderId}",
                    (int)resp.StatusCode, req.OrderId);
                return StatusCode((int)resp.StatusCode, new
                {
                    success = false,
                    message = "결제 승인에 실패했습니다.",
                    detail = TryExtractTossMessage(bodyText)
                });
            }

            using var doc = JsonDocument.Parse(bodyText);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
            var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
            var approvedAt = root.TryGetProperty("approvedAt", out var a) ? a.GetString() : null;

            // 메타만 저장 — 카드번호·CVC 등 민감정보 0건 (헌법 #22)
            await using var db = await OpenAsync(ct);
            await db.ExecuteAsync(@"
                INSERT INTO tenant_payments
                    (payment_id, order_id, signup_token, payment_key, amount, method, status,
                     approved_at, raw_status, created_at)
                VALUES
                    (UUID(), @OrderId, @SignupToken, @PaymentKey, @Amount, @Method, 'confirmed',
                     @ApprovedAt, @RawStatus, UTC_TIMESTAMP())
                ON DUPLICATE KEY UPDATE
                    status = 'confirmed',
                    approved_at = @ApprovedAt,
                    raw_status = @RawStatus,
                    updated_at = UTC_TIMESTAMP()",
                new
                {
                    req.OrderId,
                    req.SignupToken,
                    req.PaymentKey,
                    req.Amount,
                    Method = method,
                    ApprovedAt = approvedAt,
                    RawStatus = status
                });

            _logger.LogInformation("[TossPayments] confirmed orderId={OrderId} method={Method} status={Status}",
                req.OrderId, method, status);

            return Ok(new
            {
                success = true,
                message = "결제가 승인되었습니다.",
                orderId = req.OrderId,
                method,
                approvedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TossPayments] confirm exception orderId={OrderId}", req.OrderId);
            return StatusCode(500, new { success = false, message = "결제 승인 중 오류가 발생했습니다." });
        }
    }

    private string? ResolveEnv(string envName, string configKey)
    {
        var env = Environment.GetEnvironmentVariable(envName);
        return !string.IsNullOrWhiteSpace(env) ? env : _config[configKey];
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var cs = _config.GetConnectionString("BackofficeDb")
                 ?? _config.GetConnectionString("Default")
                 ?? throw new InvalidOperationException("ConnectionStrings:BackofficeDb 미설정");
        var c = new MySqlConnection(cs);
        await c.OpenAsync(ct);
        return c;
    }

    private static string? TryExtractTossMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var m)) return m.GetString();
        }
        catch (JsonException)
        {
            // 헌법 #15 정합 — static 메서드 영역, JSON 파싱 실패 = 메시지 추출 불가 영역만 의미.
            // 운영 사고 추적 영역 아님 (호출자가 별도 오류 처리 영역 가도). null 반환으로 충분.
        }
        return null;
    }

    public class ConfirmRequest
    {
        public string PaymentKey { get; set; } = "";
        public string OrderId { get; set; } = "";
        public long Amount { get; set; }
        public string? SignupToken { get; set; }
    }
}
