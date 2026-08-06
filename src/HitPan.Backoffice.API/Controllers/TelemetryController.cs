using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 고객사 업데이트 기록 수신 (20260806작4 — 사장님 오더 ③)
//
// ■ 왜 필요한가 (사장님 2026-07-20 §2, CS 첫 도구)
//   "고객한테 '몇 버전 쓰세요?' 물으면 그 확인 자체가 CS 업무가 됨. 고객은 자기 버전 모름.
//    본사가 백오피스에서 확인·선제 대응이 맞다."
//
// ■ 진범 (실측 2026-08-06)
//   워치독 송신부는 있는데 백오피스 수신부가 **0건**이었다. 커밋 149e604(ERP↔백오피스 분리)에서
//   WatchdogController 가 삭제됐고 백오피스로 이식되지 않았다. 그래서 3개월간 허공으로 갔다.
//     POST https://back.hitpan.kr/watchdog/ping       -> 400
//     POST https://back.hitpan.kr/아무거나없는경로     -> 400   ← 400 은 그 경로 고유 증상이 아니었다
//   ⇒ 페이로드 문제가 아니라 **라우트 자체가 없었다.**
//
// ■ 헌법 #18·#22 경계 — 운영 메타데이터만 받는다
//   받는다   : 버전·채널·성공/실패·시각
//   안 받는다 : 거래처·상품·금액·직원명·DB 내용 등 업무 데이터 일체
//   라이선스 키는 **대조에만 쓰고 저장·로깅하지 않는다**(평문 0건).
//
// ■ 인증 — DeviceRegistrationController 선례를 그대로 따른다(새 기법 금지)
//   워치독은 db.conf 에 LICENSE_KEY 원문을 갖고 있고(MetaPingClient.cs:146),
//   백오피스는 License:Pepper 로 HMAC 해 tenants.license_key_hash 와 대조할 수 있다.
//
// ■ 절대 원칙 — 본사가 죽어도 고객 업데이트는 돌아야 한다 (헌법 #20·#30)
//   이 엔드포인트의 어떤 응답도 고객 PC 의 업데이트를 막지 않는다. 워치독은 fire-and-forget 이다.
[ApiController]
[Route("api/telemetry")]
[AllowAnonymous]
public class TelemetryController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<TelemetryController> _logger;

    // 같은 고객사가 1분 내 같은 내용을 중복 전송하면 무시한다(재시도·중복 루프 방어).
    //   워치독 재시도(Polly 3회)가 네트워크 타이밍에 따라 중복 도달할 수 있다.
    private const int DuplicateWindowSeconds = 60;

    // ★ [3-V] 병렬검증 P1 봉합 — sha256("unknown").
    //   고객 PC 가 db.conf 를 못 읽으면 워치독이 tenant_id 를 "unknown" 으로 두고,
    //   그 해시는 **모든 PC 에서 동일한 상수**다. 현황 테이블 PK 가 tenant_id_hash 라
    //   여러 고객사가 한 행을 덮어써 CS 가 남의 고객사 버전을 보게 된다.
    //   송신측에서도 막지만(UpdateHistoryClient), 구버전 워치독이 있으므로 서버도 막는다(2중).
    private const string UnknownTenantHash =
        "sha256:b23a6a8439c0dde5515893e7c90c1e3233b8616e634470f20dc4928bcf3609bc";

    public TelemetryController(IConfiguration config, ILogger<TelemetryController> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// 업데이트 결과 1건 수신. 이력에 INSERT + 현황을 UPSERT 한다.
    /// 성공/실패/거부(고객이 [나중에]) 3시점 모두 이 경로로 들어온다.
    /// </summary>
    [HttpPost("update-history")]
    public async Task<IActionResult> ReportUpdateHistory([FromBody] UpdateHistoryRequest req, CancellationToken ct)
    {
        if (req is null) return BadRequest(new { success = false, message = "요청 비어있음" });
        if (string.IsNullOrWhiteSpace(req.LicenseKey))
            return Unauthorized(new { success = false, message = "인증 실패" });
        if (string.IsNullOrWhiteSpace(req.TenantIdHash) ||
            !req.TenantIdHash.StartsWith("sha256:", StringComparison.Ordinal))
            return BadRequest(new { success = false, message = "식별자 형식 오류" });

        // 모든 PC 가 공유하는 'unknown' 해시는 받지 않는다 — 받으면 현황 1행을 서로 덮어쓴다.
        if (string.Equals(req.TenantIdHash, UnknownTenantHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("[Telemetry] 미식별(unknown) 고객사 보고 — 거부(현황 행 충돌 방지).");
            return BadRequest(new { success = false, message = "고객사 식별 불가" });
        }

        // 길이 방어: DB 가 STRICT 모드면 초과 문자열이 예외를 내 500 이 된다([3-V] 지적).
        //   400 으로 명확히 돌려주는 편이 낫다.
        if (req.TenantIdHash.Length > 80)
            return BadRequest(new { success = false, message = "식별자 길이 초과" });

        // result 는 화이트리스트. 모르는 값을 그대로 적재하면 현황 집계가 오염된다.
        var result = (req.Result ?? "").Trim().ToLowerInvariant();
        if (result is not ("success" or "failed" or "rejected"))
            return BadRequest(new { success = false, message = "result 값 오류" });

        // 헌법 #22 — 업무 데이터가 섞여 들어오지 못하게 서버에서도 한 번 더 막는다.
        //   워치독 MetaPingClient.ValidateMinimalism 이 송신측 1차 방어이고, 이것이 2차다.
        //   송신측만 믿지 않는다 — 그 코드는 고객 PC 에 있고 우리가 통제하지 못하는 순간이 있다.
        if (ContainsForbiddenContent(req))
        {
            _logger.LogWarning("[Telemetry] 금지 필드 감지 — 수신 거부(헌법 #22)");
            return BadRequest(new { success = false, message = "허용되지 않은 항목 포함" });
        }

        try
        {
            await using var db = await OpenAsync(ct);

            // ── 인증: 라이선스 키를 HMAC 해 tenants 와 대조 (DeviceRegistrationController:56,70 선례) ──
            //   ⚠️ 원문은 여기서만 쓰고 절대 저장·로깅하지 않는다.
            var pepper = _config["License:Pepper"] ?? throw new InvalidOperationException("License:Pepper 미설정");
            var licHash = ComputeHmacSha256(req.LicenseKey.Trim().ToUpperInvariant().Replace(" ", ""), pepper);

            var tenantId = await db.QueryFirstOrDefaultAsync<string?>(
                "SELECT CAST(tenant_id AS CHAR) FROM tenants WHERE license_key_hash = @Hash LIMIT 1",
                new { Hash = licHash });

            if (tenantId is null)
            {
                // 라이선스가 우리 것이 아니다. 적재하지 않는다(위조 행 방지).
                //   ※ 해시는 로그에 남겨도 무방하다(원문 아님) — 조사에 필요하다.
                _logger.LogWarning("[Telemetry] 미상 라이선스 — 수신 거부 ({Hash})", Safe(req.TenantIdHash));
                return Unauthorized(new { success = false, message = "인증 실패" });
            }

            // ── 멱등: 1분 내 같은 고객사·같은 결과·같은 버전이면 중복으로 보고 무시 ──
            var dup = await db.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*) FROM tenant_update_history
                 WHERE tenant_id_hash = @Hash
                   AND result = @Result
                   AND COALESCE(to_version,'') = COALESCE(@ToVersion,'')
                   AND received_at >= (UTC_TIMESTAMP(6) - INTERVAL @Win SECOND)",
                new { Hash = req.TenantIdHash, Result = result, req.ToVersion, Win = DuplicateWindowSeconds });

            if (dup > 0)
            {
                // 중복은 정상 동작이다(워치독 재시도). 200 을 준다 — 워치독이 또 재시도하지 않도록.
                _logger.LogInformation("[Telemetry] 중복 수신 무시({Sec}초 내) — {Result} {To}",
                    DuplicateWindowSeconds, result, req.ToVersion);
                return Ok(new { success = true, duplicated = true });
            }

            var occurredAt = req.OccurredAt ?? DateTime.UtcNow;

            await db.ExecuteAsync(@"
                INSERT INTO tenant_update_history
                    (tenant_id, tenant_id_hash, from_version, to_version, channel, result, failure_reason, occurred_at)
                VALUES
                    (@TenantId, @Hash, @FromVersion, @ToVersion, @Channel, @Result, @FailureReason, @OccurredAt)",
                new
                {
                    TenantId = tenantId,
                    Hash = req.TenantIdHash,
                    req.FromVersion,
                    req.ToVersion,
                    req.Channel,
                    Result = result,
                    FailureReason = Truncate(req.FailureReason, 500),
                    OccurredAt = occurredAt
                });

            // ── 현황 UPSERT ──
            //   🔴 cs_note 는 갱신 대상에서 제외한다. 본사 직원이 손으로 적는 값이라
            //      워치독 수신이 덮어쓰면 안 된다(사장님 오더: 비고는 CS 참고용).
            //   current_version 은 성공했을 때만 올린다 — 실패했는데 새 버전으로 표시되면
            //      CS 가 "최신인데 왜 문제냐"로 오판한다(강지원 지적의 핵심).
            await db.ExecuteAsync(@"
                INSERT INTO tenant_update_state
                    (tenant_id_hash, tenant_id, current_version, last_result, last_channel, last_reported_at)
                VALUES
                    (@Hash, @TenantId, @CurrentVersion, @Result, @Channel, @ReportedAt)
                ON DUPLICATE KEY UPDATE
                    tenant_id        = VALUES(tenant_id),
                    current_version  = COALESCE(VALUES(current_version), current_version),
                    last_result      = VALUES(last_result),
                    last_channel     = VALUES(last_channel),
                    last_reported_at = VALUES(last_reported_at)",
                new
                {
                    Hash = req.TenantIdHash,
                    TenantId = tenantId,
                    CurrentVersion = result == "success" ? req.ToVersion : null,
                    Result = result,
                    req.Channel,
                    ReportedAt = occurredAt
                });

            _logger.LogInformation("[Telemetry] 업데이트 기록 수신 — {From} → {To} / {Result}",
                req.FromVersion, req.ToVersion, result);

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            // 헌법 #15 — 침묵 금지. 500 을 주더라도 고객 업데이트는 이미 끝난 뒤라 영향 없다.
            _logger.LogError(ex, "[Telemetry] 업데이트 기록 수신 실패");
            return StatusCode(500, new { success = false, message = "기록 실패" });
        }
    }

    /// <summary>
    /// 헌법 #22 2차 방어 — 업무 데이터로 보이는 내용이 섞였으면 통째로 거부한다.
    /// 버전·채널·사유 문자열만 검사한다(구조상 다른 필드는 자유 문자열이 아니다).
    /// </summary>
    private static bool ContainsForbiddenContent(UpdateHistoryRequest req)
    {
        // 업무 데이터 냄새가 나는 키워드. 운영 메타에는 나올 이유가 없다.
        string[] forbidden =
        {
            "tenant_name", "company_name", "user_email", "user_name",
            "revenue", "sales", "purchase", "invoice", "customer", "employee",
            "mac_address", "disk_serial"
        };

        // failure_reason 은 자유 문자열이라 유일한 유입 경로다.
        var reason = req.FailureReason ?? "";
        return forbidden.Any(f => reason.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    private static string Safe(string s) => s.Length <= 24 ? s : s[..24] + "…";

    private static string? Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);

    private static string ComputeHmacSha256(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
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

    /// <summary>
    /// 워치독이 보내는 업데이트 결과 1건.
    /// ⚠️ LicenseKey 는 인증 대조용이며 **저장하지 않는다**(헌법 #22).
    /// </summary>
    public class UpdateHistoryRequest
    {
        public string LicenseKey { get; set; } = "";
        public string TenantIdHash { get; set; } = "";
        public string? FromVersion { get; set; }
        public string? ToVersion { get; set; }
        public string? Channel { get; set; }
        public string? Result { get; set; }
        public string? FailureReason { get; set; }
        public DateTime? OccurredAt { get; set; }
    }
}
