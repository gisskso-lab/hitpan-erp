using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using HitPan.Backoffice.API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 작14 B-5·B-6 — CS 쪽지 수신 + 답 Pull (사장님 결재 2026-10-08)
//
// ■ 인증 = 3중 일치(사장님 10/8) — 테넌트넘버 + 시리얼넘버 + 부모계정 셋 전부 대조.
//   하나만 틀려도 거부 + 거부로그. 어느 축이 틀렸는지는 응답에 밝히지 않는다(공격자에게 지도 금지).
//   · 시리얼 = 라이선스 HMAC ↔ tenants.license_key_hash (TelemetryController 선례 · 원문 저장·로깅 0)
//   · 부모계정 ↔ tenants.owner_account_id — B-0(S-2 첫 보고)가 채운 값. 아직 비어 있으면
//     401 로 거부하되 사유코드 owner_not_registered 를 **로그에만** 적는다 — ERP 워커는 401 을
//     보존·재시도로 다루므로(B-3 설계) 첫 보고가 도착하는 즉시 자동으로 뚫린다(유실 0).
//   · 폐기 시리얼 = 해시 불일치로 자연 거부(발급·회전 로직 무접촉).
//
// ■ B-6 — 문과 자물쇠 같은 커밋(CTO 조건②): 이 문이 백오피스 rate limit 1호다.
//   ①연속 인증 실패 잠금(같은 테넌트넘버 60분 5회) ②시간당 접수 상한(테넌트당 30건)
//   ③본문 크기 상한 ④수신측 거부로그(bo_cs_reject_logs · INSERT ONLY · 값은 저장 안 함).
//
// ■ 문③ — 서버가 최종 판정자(설계 §5-1): 필드 화이트리스트 + 본문 모양 검사.
//   ERP 문①·②가 있어도 구버전 ERP 가 돌아다닐 수 있으므로 여기서 또 막는다.
//   BOAPI 는 ProjectReference 0(독립 경계)이라 검사기를 복제한다 — ERP 쪽과 규칙코드 어휘 동일.
//
// ■ #22 — 받는 것은 쪽지 메타+본문뿐. 업무 데이터(거래처·금액·재고)는 금지필드 집합이 막는다.
[ApiController]
[Route("api/backoffice/cs")]
[AllowAnonymous]
public class CsInboundController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<CsInboundController> _logger;

    private const int LockWindowMinutes = 60;
    private const int LockThreshold = 5;        // 연속 인증 실패 잠금(B-6 ①)
    private const int HourlyTicketLimit = 30;   // 테넌트당 시간당 접수 상한(B-6 ②)
    private const int MaxBodyLength = 2000;     // ERP cs_requests.body 와 같은 기준

    public CsInboundController(IConfiguration config, ILogger<CsInboundController> logger)
    {
        _config = config;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // 쪽지 수신 — POST api/backoffice/cs/messages
    // ─────────────────────────────────────────────────────────────
    [HttpPost("messages")]
    public async Task<IActionResult> ReceiveMessage([FromBody] JsonElement envelope, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);

        var auth = await AuthenticateAsync(db, envelope, ct);
        if (auth.Fail is not null) return auth.Fail;

        // ── data 필드 화이트리스트(문③ 1겹) — 밖의 키 하나라도 실리면 통째 거부 ──
        if (!envelope.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return await RejectAsync(db, auth.TenantCode, "bad_payload", BadRequest(Msg("요청 형식 오류")));

        string? csRequestId = null, category = null, subTag = null, body = null, screenCode = null, erpVersion = null;
        foreach (var p in data.EnumerateObject())
        {
            var v = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
            switch (p.Name)
            {
                case "csRequestId": csRequestId = v; break;
                case "category": category = v; break;
                case "subTag": subTag = v; break;
                case "body": body = v; break;
                case "screenCode": screenCode = v; break;
                case "erpVersion": erpVersion = v; break;
                default:
                    _logger.LogWarning("[CS수신] 화이트리스트 밖 필드 거부 — key={Key}", Safe(p.Name));
                    return await RejectAsync(db, auth.TenantCode, "forbidden_field", BadRequest(Msg("허용되지 않은 항목 포함")));
            }
        }

        if (string.IsNullOrWhiteSpace(csRequestId) || csRequestId.Length > 36)
            return await RejectAsync(db, auth.TenantCode, "bad_payload", BadRequest(Msg("쪽지 식별자 오류")));

        // 선택형 코드 칸 — 목록 밖 값 거부(DB명세서 §4 · 구버전 ERP 가 최종 판정을 못 피한다)
        if (category is null || !ShapeToCategory.ContainsKey(category) || subTag is null || !AllowedSubTags.Contains(subTag))
            return await RejectAsync(db, auth.TenantCode, "tag_not_allowed", BadRequest(Msg("목록에 있는 유형·태그만 받습니다")));

        if (body is { Length: > MaxBodyLength })
            return await RejectAsync(db, auth.TenantCode, "body_too_long", BadRequest(Msg($"본문은 {MaxBodyLength}자까지입니다")));

        // ── 문③ 2겹 — 본문 모양 검사(최종 판정자) · 걸리면 저장 0 ──
        var rule = ScanBody(body);
        if (rule is not null)
        {
            _logger.LogWarning("[CS수신] 금지필드 모양 거부(문③) — rule={Rule}", rule);
            return await RejectAsync(db, auth.TenantCode, "forbidden_field", BadRequest(Msg("허용되지 않은 항목 포함")));
        }

        // ── B-6 ② — 시간당 접수 상한(429 = ERP 워커가 재시도로 다룬다 · 글은 큐에 보존) ──
        var lastHour = await db.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM bo_cs_tickets
             WHERE tenant_id = @TenantId AND received_at >= DATE_SUB(UTC_TIMESTAMP(6), INTERVAL 1 HOUR)",
            new { auth.TenantId });
        if (lastHour >= HourlyTicketLimit)
            return await RejectAsync(db, auth.TenantCode, "rate_limited",
                StatusCode(429, Msg("접수량이 많습니다. 잠시 후 자동으로 다시 보냅니다")));

        // ── 멱등 적재 — (tenant, client_ticket_uid) UNIQUE. 같은 쪽지 2번 = 1건 + 200 ──
        try
        {
            await db.ExecuteAsync(@"
                INSERT INTO bo_cs_tickets
                    (tenant_id, reseller_id, client_ticket_uid, received_channel,
                     category, sub_tag, shape_tag, body, status, screen_code, erp_version, auto_class, received_at)
                SELECT t.tenant_id, t.reseller_id, @Uid, 'erp_message',
                       @Category8, @SubTag, @Shape, @Body, '접수', @ScreenCode, @ErpVersion, @Category8, UTC_TIMESTAMP(6)
                  FROM tenants t WHERE t.tenant_id = @TenantId",
                new
                {
                    auth.TenantId,
                    Uid = csRequestId,
                    Category8 = ShapeToCategory[category],   // 8종 정본(결-9) — 규칙 1차 분류 · 사람이 재분류 가능
                    SubTag = subTag,
                    Shape = category,                        // 7모양 보조 태그(결-9 보조축)
                    Body = body,
                    ScreenCode = Truncate(screenCode, 40),
                    ErpVersion = Truncate(erpVersion, 20),
                });
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            // 멱등 — 터널 복구 재전송이 여기로 온다. 거부가 아니라 "이미 받았다"(ERP 는 전송완료 처리).
            return Ok(new { success = true, duplicated = true });
        }

        return Ok(new { success = true });
    }

    // ─────────────────────────────────────────────────────────────
    // 답 Pull — POST api/backoffice/cs/replies/pull (B-9 의 본사측)
    //   승인된 답(bo_cs_replies — approved_by NOT NULL 이라 전부 사람 확정분)만 나간다.
    // ─────────────────────────────────────────────────────────────
    [HttpPost("replies/pull")]
    public async Task<IActionResult> PullReplies([FromBody] JsonElement envelope, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);

        var auth = await AuthenticateAsync(db, envelope, ct);
        if (auth.Fail is not null) return auth.Fail;

        var rows = (await db.QueryAsync(@"
            SELECT reply_id          AS replyId,
                   client_ticket_uid AS csRequestId,
                   body,
                   replied_by_kind   AS repliedByKind,
                   approved_at       AS repliedAt
              FROM bo_cs_replies
             WHERE tenant_id = @TenantId
               AND created_at >= DATE_SUB(UTC_TIMESTAMP(6), INTERVAL 14 DAY)
             ORDER BY created_at
             LIMIT 100",
            new { auth.TenantId })).ToList();

        // 전달 관찰 — 멱등은 ERP 쪽 reply_id PK 가 보장하므로 여기서는 시각만 남긴다.
        await db.ExecuteAsync(@"
            UPDATE bo_cs_replies SET delivered_at = UTC_TIMESTAMP(6)
             WHERE tenant_id = @TenantId AND delivered_at IS NULL",
            new { auth.TenantId });

        return Ok(new { success = true, replies = rows });
    }

    // ─────────────────────────────────────────────────────────────
    // 3중 일치 인증 — 봉투 화이트리스트 + 잠금(B-6 ①)
    // ─────────────────────────────────────────────────────────────
    private sealed record AuthResult(string TenantId, string TenantCode, IActionResult? Fail)
    {
        public static AuthResult Rejected(IActionResult fail) => new("", "", fail);
    }

    private async Task<AuthResult> AuthenticateAsync(MySqlConnection db, JsonElement envelope, CancellationToken ct)
    {
        if (envelope.ValueKind != JsonValueKind.Object)
            return AuthResult.Rejected(BadRequest(Msg("요청 비어있음")));

        string? tenantCode = null, licenseKey = null, ownerAccountId = null;
        foreach (var p in envelope.EnumerateObject())
        {
            switch (p.Name)
            {
                case "tenantCode": tenantCode = Str(p.Value); break;
                case "licenseKey": licenseKey = Str(p.Value); break;
                case "ownerAccountId": ownerAccountId = Str(p.Value); break;
                case "data": break; // 본문은 각 액션이 따로 검사
                default:
                    return AuthResult.Rejected(
                        await RejectAsync(db, tenantCode, "forbidden_field", BadRequest(Msg("허용되지 않은 항목 포함"))));
            }
        }

        if (string.IsNullOrWhiteSpace(tenantCode) || string.IsNullOrWhiteSpace(licenseKey) || string.IsNullOrWhiteSpace(ownerAccountId))
            return AuthResult.Rejected(
                await RejectAsync(db, tenantCode, "auth_mismatch", Unauthorized(Msg("인증 실패"))));

        // B-6 ① — 연속 인증 실패 잠금: 같은 테넌트넘버로 60분 내 실패 5회면 그 창 동안 받지 않는다.
        var recentFails = await db.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM bo_cs_reject_logs
             WHERE tenant_code = @Code AND rule_code = 'auth_mismatch'
               AND occurred_at >= DATE_SUB(UTC_TIMESTAMP(6), INTERVAL @Win MINUTE)",
            new { Code = Truncate(tenantCode, 20), Win = LockWindowMinutes });
        if (recentFails >= LockThreshold)
            return AuthResult.Rejected(
                await RejectAsync(db, tenantCode, "locked", StatusCode(429, Msg("잠시 후 다시 시도해 주세요"))));

        var row = await db.QueryFirstOrDefaultAsync<TenantAuthRow>(@"
            SELECT CAST(tenant_id AS CHAR) AS TenantId, license_key_hash AS LicenseHash, owner_account_id AS OwnerId
              FROM tenants WHERE tenant_code = @Code LIMIT 1",
            new { Code = tenantCode });

        // 3중 일치 — 하나만 틀려도 같은 얼굴로 거부(어느 축인지는 로그에만).
        var pepper = _config["License:Pepper"] ?? throw new InvalidOperationException("License:Pepper 미설정");
        var licHash = Hmac(licenseKey.Trim().ToUpperInvariant().Replace(" ", ""), pepper);

        if (row is null || string.IsNullOrEmpty(row.LicenseHash) ||
            !string.Equals(row.LicenseHash, licHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("[CS수신] 3중 일치 실패(테넌트/시리얼 축) — code={Code}", Safe(tenantCode));
            return AuthResult.Rejected(
                await RejectAsync(db, tenantCode, "auth_mismatch", Unauthorized(Msg("인증 실패"))));
        }

        if (string.IsNullOrEmpty(row.OwnerId))
        {
            // B-0 첫 보고가 아직 — ERP 워커는 401 을 보존·재시도로 다루므로 보고 도착 즉시 자동 해소.
            _logger.LogInformation("[CS수신] 부모계정 미등록(owner_not_registered) — code={Code}", Safe(tenantCode));
            return AuthResult.Rejected(
                await RejectAsync(db, tenantCode, "owner_not_registered", Unauthorized(Msg("인증 실패"))));
        }

        if (!string.Equals(row.OwnerId, ownerAccountId, StringComparison.Ordinal))
        {
            _logger.LogWarning("[CS수신] 3중 일치 실패(부모계정 축) — code={Code}", Safe(tenantCode));
            return AuthResult.Rejected(
                await RejectAsync(db, tenantCode, "auth_mismatch", Unauthorized(Msg("인증 실패"))));
        }

        return new AuthResult(row.TenantId, tenantCode!, null);
    }

    // ── 거부 = 기록이 먼저다(INSERT ONLY · 값은 저장 안 함 — 사유코드·주장된 테넌트넘버만) ──
    private async Task<IActionResult> RejectAsync(MySqlConnection db, string? tenantCode, string ruleCode, IActionResult result)
    {
        try
        {
            await db.ExecuteAsync(
                "INSERT INTO bo_cs_reject_logs (tenant_code, rule_code) VALUES (@Code, @Rule)",
                new { Code = Truncate(tenantCode, 20), Rule = ruleCode });
        }
        catch (MySqlException ex)
        {
            _logger.LogWarning(ex, "[CS수신] 거부로그 기록 실패 rule={Rule}", ruleCode); // #15 — 거부 자체는 막지 않는다
        }
        return result;
    }

    // ── 문③ 본문 모양 검사 — 규칙 한 벌은 BoForbiddenFieldScanner 하나다 ──
    //    🔴 왜 한 벌인가(작14 C-4 와 같은 커밋): 누리집 승인 재스캔이 두 번째 호출자다.
    //    같은 규칙을 백오피스 안에서 두 벌 쓰면 검사 **순서**가 갈라진다
    //    (2026-10-08 CI G-CS-5 실측: 사업자번호가 계좌로 잡혔다). 복제는 ERP↔백오피스 경계 한 번만.
    private static string? ScanBody(string? body) => BoForbiddenFieldScanner.Scan(body);

    // ── 분류 — 7모양(고객 언어 · ERP) → 8종 정본(결-9) 규칙 1차 · 사람이 재분류 가능 ──
    private static readonly Dictionary<string, string> ShapeToCategory = new()
    {
        ["open_fail"] = "bug",          // 안 열려요 → 기능오류
        ["number_mismatch"] = "dat",    // 숫자 안 맞아요 → 데이터
        ["how_to"] = "use",             // 어떻게 해요 → 사용
        ["print_fail"] = "bug",         // 안 찍혀요 → 기능오류
        ["change_request"] = "set",     // 바꿔주세요 → 설정
        ["billing"] = "etc",            // 돈 얘기 → 기타(과금·해지 자동판단 배제 — 사람 축)
        ["feature_request"] = "etc",    // 되게 해주세요 → 기타(영업·개발 넘김 표시는 후속)
    };

    private static readonly HashSet<string> AllowedSubTags = new(StringComparer.Ordinal)
    {
        "estimate_sales", "purchase_order", "stock", "invoice",
        "accounting", "hr_payroll", "settings_perm", "etc",
    };

    // ── 공통 ──
    private sealed class TenantAuthRow
    {
        public string TenantId { get; set; } = "";
        public string? LicenseHash { get; set; }
        public string? OwnerId { get; set; }
    }

    private static object Msg(string m) => new { success = false, message = m };
    private static string? Str(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    private static string Safe(string? s) => string.IsNullOrEmpty(s) ? "(없음)" : (s.Length <= 24 ? s : s[..24] + "…");
    private static string? Truncate(string? s, int max) => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);

    private static string Hmac(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
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
}
