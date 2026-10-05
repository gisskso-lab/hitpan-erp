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

    // ⬛ [HttpPost("subscription")]
    // ⬛ public Task<IActionResult> Subscription(CancellationToken ct) => HandleAsync(ct);
    // 20261005작3 P-5·§10⑥ — subscription 만 ExtraAccounts 를 받고 응답에 activeChildAccounts 한 칸. device-slot 길은 그대로.
    [HttpPost("subscription")]
    public Task<IActionResult> Subscription(CancellationToken ct) => HandleAsync(ct, isSubscription: true);

    /// <summary>
    /// 테스트 전용 연결 문자열(null 이면 db.conf). 컨트롤러 속성은 모델 바인딩되지 않는다(<c>[BindProperty]</c> 없음).
    /// </summary>
    public string? ConnectionStringForTests { get; set; }

    /// <summary>
    /// 🔴 20261005작3 §10⑥ (D-17) — 본사가 당겨 가는 「활성 자식계정 수 N」. 응답 키는 <c>activeChildAccounts</c>·<c>asOf</c> 둘뿐.
    /// </summary>
    /// <remarks>
    /// 서명 검증은 subscription 과 같은 <see cref="VerifySignature"/>(새 비밀 0). tenant 는 <b>서명된 본문</b>의 TenantId 이고,
    /// 이 PC DB(<c>local_company</c>)에 그 회사가 없으면 403(반증 F2). 이름·아이디·사원 정보 0(#18·#22).
    /// 읽기만 하므로 nonce 를 적지 않는다(재전송돼도 숫자만 다시 나간다) — 시각 창(±10분)은 본다.
    /// </remarks>
    [HttpPost("account-count")]
    public async Task<IActionResult> AccountCount(CancellationToken ct)
    {
        try
        {
            string body;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
                body = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
                return BadRequest(new { success = false, message = "빈 본문" });

            var sigHeader = Request.Headers["X-Hitpan-Signature"].ToString();
            var nonceHeader = Request.Headers["X-Hitpan-Nonce"].ToString();
            if (string.IsNullOrWhiteSpace(sigHeader) || string.IsNullOrWhiteSpace(nonceHeader))
                return Unauthorized(new { success = false, message = "서명·nonce 헤더 누락" });
            if (!VerifySignature(body, sigHeader))
            {
                _logger.LogWarning("[WebhookInbound] account-count 서명 불일치");
                return Unauthorized(new { success = false, message = "서명 불일치" });
            }

            var payload = JsonSerializer.Deserialize<WebhookPayload>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (payload is null || string.IsNullOrEmpty(payload.TenantId))
                return BadRequest(new { success = false, message = "페이로드 파싱 실패" });
            if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - payload.Iat) > MaxClockSkewSeconds)
                return Unauthorized(new { success = false, message = "타임스탬프 만료" });

            await using var db = new MySqlConnection(ConnectionStringForTests ?? BuildConnectionString());
            await db.OpenAsync(ct);

            var here = await db.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM local_company WHERE tenant_id = @TenantId", new { payload.TenantId });
            if (here == 0)
                return StatusCode(403, new { success = false, message = "이 PC 의 회사가 아닙니다" });

            var n = await HitPan.Application.Services.AccountSeatGuard.CountActiveChildrenAsync(db, payload.TenantId, ct);
            return Ok(new { activeChildAccounts = n, asOf = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WebhookInbound] account-count 처리 중 예외");
            return StatusCode(500, new { success = false, message = "내부 오류" });
        }
    }

    [HttpPost("device-slot")]
    public Task<IActionResult> DeviceSlot(CancellationToken ct) => HandleAsync(ct);

    // ⬛ private async Task<IActionResult> HandleAsync(CancellationToken ct)
    private async Task<IActionResult> HandleAsync(CancellationToken ct, bool isSubscription = false)
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

            if (isSubscription)
            {
                // 20261005작3 P-5 — 없으면(null) 덮지 않는다. 위 UPSERT 는 무접촉(#1).
                await db.ExecuteAsync(
                    "UPDATE local_subscription SET extra_accounts = COALESCE(@ExtraAccounts, extra_accounts) WHERE tenant_id = @TenantId",
                    new { payload.TenantId, payload.ExtraAccounts });
                // §10⑥ 덤 — 결제 직후 본사가 바로 안다(숫자 한 칸 · 기존 필드 무접촉)
                var n = await HitPan.Application.Services.AccountSeatGuard.CountActiveChildrenAsync(db, payload.TenantId, ct);
                return Ok(new { success = true, message = "동기화 완료", activeChildAccounts = n });
            }
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
        // ⬛ [봉합1 전] 세 번째 대안 = 레포에 적힌 개발용 키 문자열(SerialProofVerifier 의 개발 갈래와 같은 값) — 값은 옮겨 적지 않는다.
        //   🔴 10/5 봉합1 P1-03 — 키 설정이 없는 설치는 누구나 그 공개된 값으로 서명할 수 있었다(터널로 인터넷 노출 · [AllowAnonymous]).
        //   SerialProofVerifier 와 같은 방향 — 키가 없으면 대신 검증하지 않고 거절(호출부가 401).
        var key = ResolveSigningKey(TenantConfigReader.Get("HITPAN_BOOTSTRAP_TOKEN_KEY"), _config["Bootstrap:TokenKey"]);
        if (key is null)
        {
            _logger.LogWarning("[WebhookInbound] 서명 키 설정 없음(db.conf HITPAN_BOOTSTRAP_TOKEN_KEY · Bootstrap:TokenKey) — 서명 검증 거절");
            return false;
        }
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        var expected = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(sigHeader);
        if (expectedBytes.Length != actualBytes.Length) return false;
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    /// <summary>
    /// 서명 키 고르기 — db.conf 값 → 설정 값 순. 둘 다 비면 <c>null</c>(대신 쓰는 키 없음 · 10/5 봉합1 P1-03).
    /// </summary>
    public static string? ResolveSigningKey(string? fromDbConf, string? fromConfig)
    {
        if (!string.IsNullOrWhiteSpace(fromDbConf)) return fromDbConf;
        if (!string.IsNullOrWhiteSpace(fromConfig)) return fromConfig;
        return null;
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
        // 20261005작3 P-5 — 추가 구매 계정 수. 본사가 아직 안 보낸다 ⇒ null = 덮지 않음
        public int? ExtraAccounts { get; set; }
        public string? ResellerId { get; set; }
        public int ResellerTier { get; set; }
        public string Nonce { get; set; } = "";
        public long Iat { get; set; }
    }
}
