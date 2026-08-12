using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using HitPan.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.API.Controllers;

// 백오피스 → ERP webhook 수신 (사장님 결재 2026-06-04, W10)
//
// 흐름:
//   1) 백오피스 WebhookDispatcher가 본 엔드포인트로 POST
//   2) HMAC-SHA256 서명 검증 (W2 키 재사용)
//   3) timestamp(iat) ±10분 검증 + nonce 중복 차단 (멱등성)
//   4) local_subscription UPSERT
//
// 헌법 정합:
//   #15 — 빈 catch 금지
//   #18·#22 — 페이로드 메타만, 업무 데이터 0건 (소비도 동일)
//   #20 — 끊김 0
//   #29 — 환경변수 신규 0건 (HITPAN_BOOTSTRAP_TOKEN_KEY 재사용)
//   #35 — ERP는 백오피스 URL 의존 0, 수신만
[ApiController]
[Route("api/internal/webhook")]
[AllowAnonymous]
public class WebhookInboundController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<WebhookInboundController> _logger;

    private const int MaxClockSkewSeconds = 600;

    public WebhookInboundController(IConfiguration config, ILogger<WebhookInboundController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpPost("subscription")]
    public Task<IActionResult> Subscription(CancellationToken ct) => HandleAsync(ct);

    [HttpPost("device-slot")]
    public Task<IActionResult> DeviceSlot(CancellationToken ct) => HandleAsync(ct);

    private async Task<IActionResult> HandleAsync(CancellationToken ct)
    {
        try
        {
            Request.EnableBuffering();
            string body;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
            {
                body = await reader.ReadToEndAsync();
                Request.Body.Position = 0;
            }

            if (string.IsNullOrWhiteSpace(body))
                return BadRequest(new { success = false, message = "빈 본문" });

            var sigHeader = Request.Headers["X-Hitpan-Signature"].ToString();
            var nonceHeader = Request.Headers["X-Hitpan-Nonce"].ToString();
            if (string.IsNullOrWhiteSpace(sigHeader) || string.IsNullOrWhiteSpace(nonceHeader))
                return Unauthorized(new { success = false, message = "서명·nonce 헤더 누락" });

            if (!VerifySignature(body, sigHeader))
            {
                _logger.LogWarning("[WebhookInbound] 서명 불일치");
                return Unauthorized(new { success = false, message = "서명 불일치" });
            }

            var payload = JsonSerializer.Deserialize<WebhookPayload>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (payload is null || string.IsNullOrEmpty(payload.TenantId))
                return BadRequest(new { success = false, message = "페이로드 파싱 실패" });

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (Math.Abs(now - payload.Iat) > MaxClockSkewSeconds)
                return Unauthorized(new { success = false, message = "타임스탬프 만료" });

            var cs = BuildConnectionString();
            await using var db = new MySqlConnection(cs);
            await db.OpenAsync(ct);

            // 멱등성 — nonce 중복 차단 (테이블 신설 없이 last_sync 메타 활용)
            var existingNonce = await db.QueryFirstOrDefaultAsync<string?>(@"
                SELECT sync_source FROM local_subscription
                WHERE tenant_id = @TenantId AND sync_source = @Nonce",
                new { TenantId = payload.TenantId, Nonce = $"webhook:{payload.Nonce}" });
            if (!string.IsNullOrEmpty(existingNonce))
            {
                _logger.LogInformation("[WebhookInbound] 중복 nonce 무시 tenant={Tid} nonce={N}",
                    payload.TenantId, payload.Nonce);
                return Ok(new { success = true, message = "이미 처리된 이벤트" });
            }

            await db.ExecuteAsync(@"
                INSERT INTO local_subscription
                    (tenant_id, subscription_tier, status, trial_ends_at,
                     ai_mode, ai_token_monthly_limit, ai_token_extra,
                     max_users, extra_device_slots,
                     reseller_id, reseller_tier,
                     last_sync_at, sync_source, created_at, updated_at)
                VALUES
                    (@TenantId, @SubscriptionTier, @Status, @TrialEndsAt,
                     @AiMode, @AiTokenMonthlyLimit, @AiTokenExtra,
                     @MaxUsers, @ExtraDeviceSlots,
                     @ResellerId, @ResellerTier,
                     NOW(6), @SyncSource, NOW(6), NOW(6))
                ON DUPLICATE KEY UPDATE
                    subscription_tier = @SubscriptionTier,
                    status = @Status,
                    trial_ends_at = @TrialEndsAt,
                    ai_mode = @AiMode,
                    ai_token_monthly_limit = @AiTokenMonthlyLimit,
                    ai_token_extra = @AiTokenExtra,
                    max_users = @MaxUsers,
                    extra_device_slots = @ExtraDeviceSlots,
                    reseller_id = @ResellerId,
                    reseller_tier = @ResellerTier,
                    last_sync_at = NOW(6),
                    sync_source = @SyncSource,
                    updated_at = NOW(6)",
                new
                {
                    payload.TenantId,
                    payload.SubscriptionTier,
                    payload.Status,
                    payload.TrialEndsAt,
                    payload.AiMode,
                    payload.AiTokenMonthlyLimit,
                    payload.AiTokenExtra,
                    payload.MaxUsers,
                    payload.ExtraDeviceSlots,
                    payload.ResellerId,
                    payload.ResellerTier,
                    SyncSource = $"webhook:{payload.Nonce}"
                });

            _logger.LogInformation("[WebhookInbound] {Event} 동기화 완료 tenant={Tid}",
                payload.EventType, payload.TenantId);
            return Ok(new { success = true, message = "동기화 완료" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WebhookInbound] 처리 중 예외");
            return StatusCode(500, new { success = false, message = "내부 오류" });
        }
    }

    private bool VerifySignature(string body, string sigHeader)
    {
        // 봉합 2026-06-17 1.2.12 — TenantConfigReader 정합
        var key = TenantConfigReader.Get("HITPAN_BOOTSTRAP_TOKEN_KEY")
                 ?? _config["Bootstrap:TokenKey"]
                 ?? "DEV-bootstrap-token-key-change-in-production-32+chars";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        var expected = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(sigHeader);
        if (expectedBytes.Length != actualBytes.Length) return false;
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    // 봉합 2026-06-16: TenantConfigReader 영역 통일 (db.conf 직접 읽음)
    //   1.2.6 환경변수 폐기 사장님 결재 정합. AuditLogMiddleware와 함께 누락된 영역.
    private static string BuildConnectionString()
    {
        var host = TenantConfigReader.Get("DB_HOST") ?? "localhost";
        var port = TenantConfigReader.Get("DB_PORT") ?? "3306";
        var db   = TenantConfigReader.Get("DB_NAME") ?? "hitpan_erp";
        var user = TenantConfigReader.Get("DB_USER") ?? "hitpan";
        var pwd  = TenantConfigReader.GetRequired("DB_PASSWORD");
        // GuidFormat=None — char(36) 을 Guid 로 돌려주면 string DTO 매핑이 터진다 (봉합 2026-08-12, PI-07).
        return $"Server={host};Port={port};Database={db};Uid={user};Pwd={pwd};CharSet=utf8mb4;AllowUserVariables=true;GuidFormat=None";
    }

    private class WebhookPayload
    {
        public string EventType { get; set; } = "";
        public string TenantId { get; set; } = "";
        public string TenantCode { get; set; } = "";
        public string SubscriptionTier { get; set; } = "basic";
        public string Status { get; set; } = "active";
        public DateTime? TrialEndsAt { get; set; }
        public string AiMode { get; set; } = "hitpan_pool";
        public int AiTokenMonthlyLimit { get; set; }
        public int AiTokenExtra { get; set; }
        public int MaxUsers { get; set; }
        public int ExtraDeviceSlots { get; set; }
        public string? ResellerId { get; set; }
        public int ResellerTier { get; set; }
        public string Nonce { get; set; } = "";
        public long Iat { get; set; }
    }
}
