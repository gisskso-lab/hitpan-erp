using System.Text.Json;
using Dapper;
using HitPan.Backoffice.API.Filters;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// Owner 영역 bo_users CRUD + 4-eyes 승인 큐 (사장님 결재 2026-06-04, W11)
//
// 흐름:
//   - List/Get: 즉시 실행
//   - Create/Delete: 4-eyes — 요청자 1명 + 다른 Owner 1명 승인 필요
//   - Update (role 변경): 4-eyes
//   - Update (name/is_active 토글): 즉시 실행, 감사로그 남김
//
// 헌법 정합:
//   #15 — 빈 catch 0
//   #18·#22 — 비밀번호 BCrypt 저장, 평문 0건, MFA 시크릿 0건
//   #25 — 안전하게 (4-eyes + 감사로그 + MFA)
[ApiController]
[Route("api/backoffice/owner/bo-users")]
[Authorize]
public class OwnerBoUsersController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<OwnerBoUsersController> _logger;
    private readonly IBoAuditService _audit;

    public OwnerBoUsersController(IConfiguration config, ILogger<OwnerBoUsersController> logger, IBoAuditService audit)
    {
        _config = config;
        _logger = logger;
        _audit = audit;
    }

    [HttpGet]
    [BoPermission("owner.bo_users.list")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var rows = await db.QueryAsync<BoUserRow>(@"
                SELECT
                    user_id AS UserId,
                    email AS Email,
                    name AS Name,
                    role AS Role,
                    CAST(reseller_id AS CHAR) AS ResellerId,
                    is_active AS IsActive,
                    failed_login_cnt AS FailedLoginCnt,
                    last_login_at AS LastLoginAt,
                    created_at AS CreatedAt
                FROM bo_users
                ORDER BY created_at DESC");
            return Ok(new { success = true, items = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OwnerBoUsers] 목록 조회 실패");
            return StatusCode(500, new { success = false, message = "목록 조회 중 오류가 발생했습니다." });
        }
    }

    // 4-eyes 요청 — Create
    [HttpPost("request-create")]
    [BoPermission("owner.bo_users.create")]
    public async Task<IActionResult> RequestCreate([FromBody] CreateRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password)
            || string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Role))
            return BadRequest(new { success = false, message = "이메일·비밀번호·이름·역할 필수" });
        if (req.Password.Length < 12)
            return BadRequest(new { success = false, message = "Owner 영역은 비밀번호 12자 이상 필수" });
        if (req.Role != "owner" && req.Role != "admin" && req.Role != "reseller")
            return BadRequest(new { success = false, message = "역할은 owner / admin / reseller 중 하나" });

        var (actorId, actorEmail, _) = GetActor();

        try
        {
            await using var db = await OpenAsync(ct);
            var dup = await db.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM bo_users WHERE email = @Email", new { req.Email });
            if (dup > 0)
                return BadRequest(new { success = false, message = "이미 사용 중인 이메일입니다." });

            // 비밀번호는 즉시 BCrypt 저장 (평문 결재 큐 저장 금지, 헌법 #18)
            var hash = BCrypt.Net.BCrypt.HashPassword(req.Password);
            var payload = JsonSerializer.Serialize(new
            {
                req.Email,
                PasswordHash = hash,
                req.Name,
                req.Role,
                req.ResellerId
            });

            var approvalId = await db.ExecuteScalarAsync<long>(@"
                INSERT INTO bo_approval_queue
                    (requester_id, requester_email, action, target_type, target_id,
                     payload_json, status, requested_at, expires_at)
                VALUES
                    (@Rid, @Remail, 'bo_user.create', 'bo_user', NULL,
                     @Payload, 'pending', NOW(6), DATE_ADD(NOW(6), INTERVAL 24 HOUR));
                SELECT LAST_INSERT_ID();",
                new { Rid = actorId, Remail = actorEmail, Payload = payload });

            await _audit.LogAsync(actorId, actorEmail, "owner",
                "bo_user.request_create", "bo_user", null,
                new { req.Email, req.Role }, GetIp(), GetUa(), ct);

            return Ok(new { success = true, approvalId, message = "4-eyes 승인 큐에 추가했습니다. 다른 Owner 1명 결재 후 실행됩니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OwnerBoUsers] 생성 요청 실패");
            return StatusCode(500, new { success = false, message = "생성 요청 중 오류가 발생했습니다." });
        }
    }

    // 4-eyes 요청 — Delete
    [HttpPost("{id}/request-delete")]
    [BoPermission("owner.bo_users.delete")]
    public async Task<IActionResult> RequestDelete(string id, CancellationToken ct)
    {
        var (actorId, actorEmail, _) = GetActor();
        if (id == actorId)
            return BadRequest(new { success = false, message = "본인 계정은 삭제할 수 없습니다." });

        try
        {
            await using var db = await OpenAsync(ct);
            var target = await db.QueryFirstOrDefaultAsync<string?>(
                "SELECT email FROM bo_users WHERE user_id = @Id", new { Id = id });
            if (target is null)
                return NotFound(new { success = false, message = "대상 계정을 찾을 수 없습니다." });

            await db.ExecuteAsync(@"
                INSERT INTO bo_approval_queue
                    (requester_id, requester_email, action, target_type, target_id,
                     payload_json, status, requested_at, expires_at)
                VALUES
                    (@Rid, @Remail, 'bo_user.delete', 'bo_user', @TargetId,
                     @Payload, 'pending', NOW(6), DATE_ADD(NOW(6), INTERVAL 24 HOUR))",
                new { Rid = actorId, Remail = actorEmail, TargetId = id,
                      Payload = JsonSerializer.Serialize(new { targetEmail = target }) });

            await _audit.LogAsync(actorId, actorEmail, "owner",
                "bo_user.request_delete", "bo_user", id,
                new { targetEmail = target }, GetIp(), GetUa(), ct);

            return Ok(new { success = true, message = "삭제 요청을 4-eyes 큐에 추가했습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OwnerBoUsers] 삭제 요청 실패 id={Id}", id);
            return StatusCode(500, new { success = false, message = "삭제 요청 중 오류가 발생했습니다." });
        }
    }

    // 즉시 토글 (4-eyes 불필요 — name/is_active만)
    [HttpPut("{id}/activate")]
    [BoPermission("owner.bo_users.update")]
    public Task<IActionResult> Activate(string id, CancellationToken ct) => SetActiveAsync(id, true, ct);

    [HttpPut("{id}/deactivate")]
    [BoPermission("owner.bo_users.update")]
    public Task<IActionResult> Deactivate(string id, CancellationToken ct) => SetActiveAsync(id, false, ct);

    private async Task<IActionResult> SetActiveAsync(string id, bool active, CancellationToken ct)
    {
        var (actorId, actorEmail, _) = GetActor();
        if (id == actorId && !active)
            return BadRequest(new { success = false, message = "본인 계정은 비활성화할 수 없습니다." });

        try
        {
            await using var db = await OpenAsync(ct);
            var affected = await db.ExecuteAsync(@"
                UPDATE bo_users SET is_active = @Active, updated_at = NOW() WHERE user_id = @Id",
                new { Active = active ? 1 : 0, Id = id });
            if (affected == 0)
                return NotFound(new { success = false, message = "대상 계정을 찾을 수 없습니다." });

            await _audit.LogAsync(actorId, actorEmail, "owner",
                active ? "bo_user.activate" : "bo_user.deactivate",
                "bo_user", id, null, GetIp(), GetUa(), ct);

            return Ok(new { success = true, message = active ? "활성화되었습니다." : "비활성화되었습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OwnerBoUsers] 토글 실패 id={Id}", id);
            return StatusCode(500, new { success = false, message = "처리 중 오류가 발생했습니다." });
        }
    }

    private (string id, string email, string role) GetActor()
    {
        var id = User.FindFirst("sub")?.Value ?? "";
        var email = User.FindFirst("email")?.Value ?? "";
        var role = User.FindFirst("role")?.Value ?? "";
        return (id, email, role);
    }

    private string? GetIp() =>
        HttpContext.Connection.RemoteIpAddress?.ToString();

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

    public record CreateRequest(string Email, string Password, string Name, string Role, string? ResellerId);

    public class BoUserRow
    {
        public string UserId { get; set; } = "";
        public string Email { get; set; } = "";
        public string Name { get; set; } = "";
        public string Role { get; set; } = "";
        public string? ResellerId { get; set; }
        public int IsActive { get; set; }
        public int FailedLoginCnt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
