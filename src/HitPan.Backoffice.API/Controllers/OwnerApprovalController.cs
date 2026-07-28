using System.Text.Json;
using Dapper;
using HitPan.Backoffice.API.Filters;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 4-eyes 승인 큐 (사장님 결재 2026-06-04, W11)
//
// 승인자 규칙:
//   - 요청자와 동일 인물 불가 (4-eyes 본질)
//   - 승인 즉시 실제 액션 실행 (트랜잭션 안)
//   - 24시간 만료
//
// 헌법 정합:
//   #15·#18·#22·#25
[ApiController]
[Route("api/backoffice/owner/approvals")]
[Authorize]
public class OwnerApprovalController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<OwnerApprovalController> _logger;
    private readonly IBoAuditService _audit;

    public OwnerApprovalController(IConfiguration config, ILogger<OwnerApprovalController> logger, IBoAuditService audit)
    {
        _config = config;
        _logger = logger;
        _audit = audit;
    }

    [HttpGet]
    [BoPermission("owner.approvals.list")]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
    {
        try
        {
            await using var db = await OpenAsync(ct);
            var where = "";
            object? param = null;
            if (!string.IsNullOrWhiteSpace(status) && status != "all")
            {
                where = "WHERE status = @Status";
                param = new { Status = status };
            }
            var rows = await db.QueryAsync<ApprovalRow>($@"
                SELECT
                    approval_id AS ApprovalId,
                    requester_id AS RequesterId,
                    requester_email AS RequesterEmail,
                    action AS Action,
                    target_type AS TargetType,
                    target_id AS TargetId,
                    status AS Status,
                    approver_id AS ApproverId,
                    approver_email AS ApproverEmail,
                    rejection_reason AS RejectionReason,
                    requested_at AS RequestedAt,
                    decided_at AS DecidedAt,
                    executed_at AS ExecutedAt,
                    expires_at AS ExpiresAt
                FROM bo_approval_queue
                {where}
                ORDER BY approval_id DESC
                LIMIT 200", param);
            return Ok(new { success = true, items = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OwnerApproval] 목록 조회 실패");
            return StatusCode(500, new { success = false, message = "목록 조회 중 오류가 발생했습니다." });
        }
    }

    [HttpPost("{id:long}/approve")]
    [BoPermission("owner.approvals.decide")]
    public async Task<IActionResult> Approve(long id, CancellationToken ct)
    {
        var (actorId, actorEmail, _) = GetActor();
        try
        {
            await using var db = await OpenAsync(ct);
            var item = await db.QueryFirstOrDefaultAsync<ApprovalFull>(@"
                SELECT approval_id AS ApprovalId, requester_id AS RequesterId,
                       requester_email AS RequesterEmail, action AS Action,
                       target_type AS TargetType, target_id AS TargetId,
                       payload_json AS PayloadJson, status AS Status, expires_at AS ExpiresAt
                FROM bo_approval_queue WHERE approval_id = @Id", new { Id = id });
            if (item is null) return NotFound(new { success = false, message = "결재 항목을 찾을 수 없습니다." });
            if (item.Status != "pending") return BadRequest(new { success = false, message = $"이미 {item.Status} 상태입니다." });
            if (item.ExpiresAt < DateTime.UtcNow) return BadRequest(new { success = false, message = "만료된 결재입니다." });
            if (item.RequesterId == actorId)
                return BadRequest(new { success = false, message = "요청자 본인은 승인할 수 없습니다 (4-eyes)." });

            // 트랜잭션: 결재 상태 + 실제 액션 동시 저장
            using var tx = await db.BeginTransactionAsync(ct);
            try
            {
                await db.ExecuteAsync(@"
                    UPDATE bo_approval_queue
                    SET status = 'approved', approver_id = @ApproverId,
                        approver_email = @ApproverEmail, decided_at = NOW(6)
                    WHERE approval_id = @Id",
                    new { ApproverId = actorId, ApproverEmail = actorEmail, Id = id }, tx);

                await ExecuteActionAsync(db, tx, item, ct);

                await db.ExecuteAsync(@"
                    UPDATE bo_approval_queue SET status = 'executed', executed_at = NOW(6)
                    WHERE approval_id = @Id", new { Id = id }, tx);

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            await _audit.LogAsync(actorId, actorEmail, "owner",
                $"approval.approve:{item.Action}", item.TargetType, item.TargetId,
                new { item.RequesterEmail }, GetIp(), GetUa(), ct);

            return Ok(new { success = true, message = "승인·실행이 완료되었습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OwnerApproval] 승인 실패 id={Id}", id);
            return StatusCode(500, new { success = false, message = "승인 처리 중 오류가 발생했습니다." });
        }
    }

    [HttpPost("{id:long}/reject")]
    [BoPermission("owner.approvals.decide")]
    public async Task<IActionResult> Reject(long id, [FromBody] RejectRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, message = "반려 사유를 입력해주세요." });

        var (actorId, actorEmail, _) = GetActor();
        try
        {
            await using var db = await OpenAsync(ct);
            var affected = await db.ExecuteAsync(@"
                UPDATE bo_approval_queue
                SET status = 'rejected', approver_id = @ApproverId,
                    approver_email = @ApproverEmail, decided_at = NOW(6),
                    rejection_reason = @Reason
                WHERE approval_id = @Id AND status = 'pending'
                  AND requester_id != @ApproverId",
                new { ApproverId = actorId, ApproverEmail = actorEmail, Id = id, req.Reason });
            if (affected == 0)
                return BadRequest(new { success = false, message = "반려할 수 있는 결재가 없습니다 (이미 처리됐거나 본인 요청)." });

            await _audit.LogAsync(actorId, actorEmail, "owner",
                "approval.reject", "approval", id.ToString(),
                new { req.Reason }, GetIp(), GetUa(), ct);

            return Ok(new { success = true, message = "반려되었습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OwnerApproval] 반려 실패 id={Id}", id);
            return StatusCode(500, new { success = false, message = "반려 처리 중 오류가 발생했습니다." });
        }
    }

    private async Task ExecuteActionAsync(MySqlConnection db, System.Data.Common.DbTransaction tx,
        ApprovalFull item, CancellationToken ct)
    {
        switch (item.Action)
        {
            case "bo_user.create":
            {
                var p = JsonSerializer.Deserialize<CreatePayload>(item.PayloadJson)
                        ?? throw new InvalidOperationException("payload 파싱 실패");
                var userId = Guid.NewGuid().ToString();
                await db.ExecuteAsync(@"
                    INSERT INTO bo_users
                        (user_id, email, password_hash, name, role, reseller_id, is_active, created_at)
                    VALUES (@UserId, @Email, @PasswordHash, @Name, @Role, @ResellerId, 1, NOW())",
                    new { UserId = userId, p.Email, p.PasswordHash, p.Name, p.Role, p.ResellerId }, tx);
                break;
            }
            case "bo_user.delete":
            {
                await db.ExecuteAsync(
                    "UPDATE bo_users SET is_active = 0, updated_at = NOW() WHERE user_id = @Id",
                    new { Id = item.TargetId }, tx);
                break;
            }
            default:
                throw new InvalidOperationException($"미지원 액션: {item.Action}");
        }
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

    public record RejectRequest(string Reason);

    private class ApprovalRow
    {
        public long ApprovalId { get; set; }
        public string RequesterId { get; set; } = "";
        public string RequesterEmail { get; set; } = "";
        public string Action { get; set; } = "";
        public string? TargetType { get; set; }
        public string? TargetId { get; set; }
        public string Status { get; set; } = "";
        public string? ApproverId { get; set; }
        public string? ApproverEmail { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime RequestedAt { get; set; }
        public DateTime? DecidedAt { get; set; }
        public DateTime? ExecutedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    private class ApprovalFull
    {
        public long ApprovalId { get; set; }
        public string RequesterId { get; set; } = "";
        public string RequesterEmail { get; set; } = "";
        public string Action { get; set; } = "";
        public string? TargetType { get; set; }
        public string? TargetId { get; set; }
        public string PayloadJson { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
    }

    private class CreatePayload
    {
        public string Email { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string Name { get; set; } = "";
        public string Role { get; set; } = "";
        public string? ResellerId { get; set; }
    }
}
