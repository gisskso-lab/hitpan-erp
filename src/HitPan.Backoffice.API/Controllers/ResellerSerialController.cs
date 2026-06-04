using System.Security.Cryptography;
using Dapper;
using HitPan.Backoffice.API.Filters;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 대리점 시리얼 발급 풀 (사장님 결재 2026-06-04, W9)
//
// 흐름:
//   1) POST /issue — 본사 owner가 대리점에 시리얼 N개 발급 (평문 응답 1회만)
//   2) GET / — 대리점별 시리얼 목록
//   3) POST /{id}/revoke — 회수 (available 한정)
//
// 헌법 정합:
//   #18·#22 — 평문 시리얼은 발급 응답 1회만, DB는 BCrypt 박제
//   #25 — 안전 (감사로그 + 회수 가능)
[ApiController]
[Route("api/backoffice/reseller-serials")]
[Authorize]
public class ResellerSerialController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<ResellerSerialController> _logger;
    private readonly IBoAuditService _audit;

    public ResellerSerialController(IConfiguration config, ILogger<ResellerSerialController> logger, IBoAuditService audit)
    {
        _config = config;
        _logger = logger;
        _audit = audit;
    }

    [HttpGet]
    [BoPermission("reseller_serial.list")]
    public async Task<IActionResult> List([FromQuery] string? resellerId, [FromQuery] string? status, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var where = new List<string>();
            var p = new DynamicParameters();
            if (!string.IsNullOrWhiteSpace(resellerId)) { where.Add("s.reseller_id = @Rid"); p.Add("Rid", resellerId); }
            if (!string.IsNullOrWhiteSpace(status) && status != "all") { where.Add("s.status = @Status"); p.Add("Status", status); }

            var sql = @"
                SELECT
                    s.serial_id AS SerialId,
                    LEFT(s.serial_code, 8) AS SerialPrefix,
                    CAST(s.reseller_id AS CHAR) AS ResellerId,
                    r.reseller_name AS ResellerName,
                    s.subscription_tier AS SubscriptionTier,
                    s.max_users AS MaxUsers,
                    s.valid_until AS ValidUntil,
                    s.status AS Status,
                    CAST(s.claimed_tenant_id AS CHAR) AS ClaimedTenantId,
                    s.claimed_at AS ClaimedAt,
                    s.revoked_at AS RevokedAt,
                    s.revoked_reason AS RevokedReason,
                    s.issued_at AS IssuedAt
                FROM reseller_serials s
                LEFT JOIN resellers r ON r.reseller_id = s.reseller_id
                " + (where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "") + @"
                ORDER BY s.serial_id DESC
                LIMIT 500";
            var rows = await db.QueryAsync<SerialRow>(sql, p);
            return Ok(new { success = true, items = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ResellerSerial] 목록 조회 실패");
            return StatusCode(500, new { success = false, message = "목록 조회 중 오류가 발생했습니다." });
        }
    }

    [HttpPost("issue")]
    [BoPermission("reseller_serial.issue")]
    public async Task<IActionResult> Issue([FromBody] IssueRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.ResellerId) || req.Quantity <= 0 || req.Quantity > 100)
            return BadRequest(new { success = false, message = "resellerId 필수, quantity 1~100" });
        var tier = string.IsNullOrWhiteSpace(req.SubscriptionTier) ? "basic" : req.SubscriptionTier;
        if (tier != "basic" && tier != "pro" && tier != "enterprise")
            return BadRequest(new { success = false, message = "tier는 basic/pro/enterprise" });

        var (actorId, actorEmail, _) = GetActor();

        try
        {
            await using var db = await OpenAsync(ct);

            // 대리점 존재 검증
            var resellerExists = await db.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM resellers WHERE reseller_id = @Rid AND status = 'active'",
                new { Rid = req.ResellerId });
            if (resellerExists == 0)
                return BadRequest(new { success = false, message = "활성 대리점을 찾을 수 없습니다." });

            var plainSerials = new List<string>();
            for (var i = 0; i < req.Quantity; i++)
            {
                var serial = GenerateSerial();
                var hash = BCrypt.Net.BCrypt.HashPassword(serial);
                await db.ExecuteAsync(@"
                    INSERT INTO reseller_serials
                        (serial_code, serial_hash, reseller_id, issued_by,
                         subscription_tier, max_users, valid_until, status, issued_at)
                    VALUES
                        (@Code, @Hash, @Rid, @IssuedBy,
                         @Tier, @MaxUsers, @ValidUntil, 'available', NOW(6))",
                    new
                    {
                        Code = serial,
                        Hash = hash,
                        Rid = req.ResellerId,
                        IssuedBy = actorId,
                        Tier = tier,
                        MaxUsers = req.MaxUsers <= 0 ? 3 : req.MaxUsers,
                        ValidUntil = req.ValidUntil
                    });
                plainSerials.Add(serial);
            }

            await _audit.LogAsync(actorId, actorEmail, "owner",
                "reseller_serial.issue", "reseller", req.ResellerId,
                new { req.Quantity, tier, req.MaxUsers }, GetIp(), GetUa(), ct);

            return Ok(new
            {
                success = true,
                message = $"{plainSerials.Count}개 시리얼이 발급되었습니다.",
                serials = plainSerials,
                hint = "평문 시리얼은 본 응답에서만 표시됩니다. 대리점에 안전하게 전달하세요."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ResellerSerial] 발급 실패 reseller={Rid}", req.ResellerId);
            return StatusCode(500, new { success = false, message = "발급 처리 중 오류가 발생했습니다." });
        }
    }

    [HttpPost("{id:long}/revoke")]
    [BoPermission("reseller_serial.revoke")]
    public async Task<IActionResult> Revoke(long id, [FromBody] RevokeRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, message = "회수 사유를 입력해주세요." });

        var (actorId, actorEmail, _) = GetActor();
        try
        {
            await using var db = await OpenAsync(ct);
            var affected = await db.ExecuteAsync(@"
                UPDATE reseller_serials
                SET status = 'revoked', revoked_at = NOW(6), revoked_reason = @Reason
                WHERE serial_id = @Id AND status = 'available'",
                new { Id = id, req.Reason });
            if (affected == 0)
                return BadRequest(new { success = false, message = "available 상태인 시리얼만 회수할 수 있습니다." });

            await _audit.LogAsync(actorId, actorEmail, "owner",
                "reseller_serial.revoke", "serial", id.ToString(),
                new { req.Reason }, GetIp(), GetUa(), ct);

            return Ok(new { success = true, message = "시리얼이 회수되었습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ResellerSerial] 회수 실패 id={Id}", id);
            return StatusCode(500, new { success = false, message = "회수 처리 중 오류가 발생했습니다." });
        }
    }

    private static string GenerateSerial()
    {
        // 32자 대문자+숫자, 4자리씩 4-4-4-4-4-4-4-4 (대시 포함 39자)
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < 32; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append('-');
            sb.Append(alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]);
        }
        return sb.ToString();
    }

    private (string id, string email, string role) GetActor()
    {
        var id = User.FindFirst("sub")?.Value ?? "";
        var email = User.FindFirst("email")?.Value ?? "";
        var role = User.FindFirst("role")?.Value ?? "";
        return (id, email, role);
    }

    private string? GetIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
    private string? GetUa()
    {
        var ua = Request.Headers["User-Agent"].ToString();
        return string.IsNullOrEmpty(ua) ? null : (ua.Length > 255 ? ua.Substring(0, 255) : ua);
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

    public record IssueRequest(string ResellerId, int Quantity, string? SubscriptionTier, int MaxUsers, DateTime? ValidUntil);
    public record RevokeRequest(string Reason);

    public class SerialRow
    {
        public long SerialId { get; set; }
        public string SerialPrefix { get; set; } = "";
        public string ResellerId { get; set; } = "";
        public string? ResellerName { get; set; }
        public string SubscriptionTier { get; set; } = "";
        public int MaxUsers { get; set; }
        public DateTime? ValidUntil { get; set; }
        public string Status { get; set; } = "";
        public string? ClaimedTenantId { get; set; }
        public DateTime? ClaimedAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? RevokedReason { get; set; }
        public DateTime IssuedAt { get; set; }
    }
}
