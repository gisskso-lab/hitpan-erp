using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using MySqlConnector;

namespace HitPan.Backoffice.API.Services;

// 백오피스 → ERP webhook 발행 (사장님 결재 2026-06-04, W10)
//
// 흐름:
//   1) 백오피스에서 구독·기기 슬롯 변경 → 본 서비스 호출
//   2) webhook_outbox INSERT (pending, HMAC 서명 포함)
//   3) WebhookDispatcher가 1분 주기로 pending 큐 발송
//
// 헌법 정합:
//   #18·#22 — payload은 메타만, 업무 데이터 0건
//   #20 — 발송 실패해도 INSERT 보존, 재시도 큐로 끊김 0
//   #29 — 환경변수 신규 0건 (W2 키 재사용)
//   #35 — ERP는 백오피스 의존 0, 백오피스→ERP 단방향만
public interface IWebhookOutboundService
{
    Task EmitSubscriptionChangedAsync(string tenantId, CancellationToken ct = default);
    Task EmitDeviceSlotChangedAsync(string tenantId, CancellationToken ct = default);
}

public class WebhookOutboundService : IWebhookOutboundService
{
    private readonly IConfiguration _config;
    private readonly ILogger<WebhookOutboundService> _logger;

    public WebhookOutboundService(IConfiguration config, ILogger<WebhookOutboundService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public Task EmitSubscriptionChangedAsync(string tenantId, CancellationToken ct = default) =>
        EmitAsync(tenantId, "subscription_changed", ct);

    public Task EmitDeviceSlotChangedAsync(string tenantId, CancellationToken ct = default) =>
        EmitAsync(tenantId, "device_slot_changed", ct);

    private async Task EmitAsync(string tenantId, string eventType, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);

            // 1) 백오피스 tenants에서 현재 메타 조회 (구독 + 기기)
            var tenant = await db.QueryFirstOrDefaultAsync<TenantMeta>(@"
                SELECT
                    CAST(tenant_id AS CHAR) AS TenantId,
                    tenant_code AS TenantCode,
                    subscription_tier AS SubscriptionTier,
                    status AS Status,
                    trial_ends_at AS TrialEndsAt,
                    ai_mode AS AiMode,
                    ai_token_monthly_limit AS AiTokenMonthlyLimit,
                    ai_token_extra AS AiTokenExtra,
                    max_users AS MaxUsers,
                    extra_device_slots AS ExtraDeviceSlots,
                    CAST(reseller_id AS CHAR) AS ResellerId,
                    reseller_tier AS ResellerTier
                FROM tenants WHERE tenant_id = @TenantId",
                new { TenantId = tenantId });

            if (tenant is null)
            {
                _logger.LogWarning("[Webhook] tenant 미존재 — 발행 스킵 tenant_id={Id}", tenantId);
                return;
            }

            // 2) 헌법 #35 §35: ERP 도메인 = {tenant_code}.hitpan.kr
            // 저장 완료 2026-06-08 (브라운킴 PM) — 베타 시연 저장할 영역 모든 저장 저장 api-demo.hitpan.kr 저장
            // (베타 영역 저장할 영역 = 단일 시연 영역, 고객사별 도메인 저장할 영역 = 정식 영역 저장)
            var webhookHost = Environment.GetEnvironmentVariable("WEBHOOK_TARGET_HOST")
                              ?? $"{tenant.TenantCode}.hitpan.kr";
            var targetUrl = $"https://{webhookHost}/api/internal/webhook/{(eventType == "subscription_changed" ? "subscription" : "device-slot")}";

            var nonce = Guid.NewGuid().ToString();
            var payload = new
            {
                eventType,
                tenantId,
                tenant.TenantCode,
                tenant.SubscriptionTier,
                tenant.Status,
                tenant.TrialEndsAt,
                tenant.AiMode,
                tenant.AiTokenMonthlyLimit,
                tenant.AiTokenExtra,
                tenant.MaxUsers,
                tenant.ExtraDeviceSlots,
                tenant.ResellerId,
                tenant.ResellerTier,
                nonce,
                iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            var json = JsonSerializer.Serialize(payload);
            var signature = ComputeSignature(json);

            await db.ExecuteAsync(@"
                INSERT INTO webhook_outbox
                    (tenant_id, event_type, target_url, payload_json, signature, nonce,
                     status, retry_count, next_retry_at, created_at)
                VALUES
                    (@TenantId, @EventType, @TargetUrl, @Payload, @Signature, @Nonce,
                     'pending', 0, NOW(6), NOW(6))",
                new
                {
                    TenantId = tenantId,
                    EventType = eventType,
                    TargetUrl = targetUrl,
                    Payload = json,
                    Signature = signature,
                    Nonce = nonce
                });

            _logger.LogInformation("[Webhook] 발행 enqueued tenant={Code} event={Event}",
                tenant.TenantCode, eventType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Webhook] 발행 실패 tenant_id={Id} event={Event}", tenantId, eventType);
            // 호출자에게 예외 전파 금지 — 메인 트랜잭션 보호 (헌법 #15: 로그만 남김)
        }
    }

    private string ComputeSignature(string payloadJson)
    {
        var key = Environment.GetEnvironmentVariable("HITPAN_BOOTSTRAP_TOKEN_KEY")
                 ?? _config["Bootstrap:TokenKey"]
                 ?? throw new InvalidOperationException("Bootstrap:TokenKey 또는 HITPAN_BOOTSTRAP_TOKEN_KEY 환경변수 미설정");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadJson));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
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

    private class TenantMeta
    {
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
    }
}
