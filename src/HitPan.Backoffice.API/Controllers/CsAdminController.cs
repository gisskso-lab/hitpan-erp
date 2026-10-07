using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 작14 B-8 — /admin/cs 결선용 본사 CS API (사장님 결재 2026-10-08)
//
// ■ 역할: 티켓 목록·상세·답 작성·상태 전이. 본사 전권(F-14 2차·전체 조회).
// ■ 반자동 ②겹(설계 §4-5): 답은 bo_cs_replies 에 approved_by·approved_at NOT NULL —
//   이 API 가 유일한 작성 경로이고 작성자가 곧 승인자다(사람이 쓴 글만 몸통이 생긴다).
//   AI 초안(D 묶음)이 생겨도 발송 몸통은 여기서만 만들어진다.
// ■ 상태 전이는 서버 1곳(F-15) — 이 컨트롤러의 status 액션뿐. 전이마다 INSERT ONLY 로그.
// ■ #18·#22: 이 화면이 다루는 것은 고객이 보낸 쪽지와 본사의 답뿐 — 업무 데이터 0.
[ApiController]
[Route("api/admin/cs")]
[Authorize(Policy = "PlatformAdmin")]
public class CsAdminController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<CsAdminController> _logger;

    // 상태 고정 목록(F-15) + 「고객 답 대기」(결-1 · 사장님 10/8 추가).
    private static readonly HashSet<string> AllowedStatus = new(StringComparer.Ordinal)
    {
        "접수", "처리중", "보류", "고객답대기", "완료",
    };

    public CsAdminController(IConfiguration config, ILogger<CsAdminController> logger)
    {
        _config = config;
        _logger = logger;
    }

    // ── 목록 — 상태·유형 필터 + 회사명 검색 ─────────────────────────
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? q, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync(@"
            SELECT k.id              AS id,
                   t.company_name    AS companyName,
                   t.tenant_code     AS tenantCode,
                   k.category        AS category,
                   k.shape_tag       AS shapeTag,
                   k.sub_tag         AS subTag,
                   k.received_channel AS channel,
                   k.status          AS status,
                   k.received_at     AS receivedAt,
                   (SELECT COUNT(*) FROM bo_cs_replies r WHERE r.ticket_id = k.id) AS replyCount
              FROM bo_cs_tickets k
              JOIN tenants t ON t.tenant_id = k.tenant_id
             WHERE (@Status IS NULL OR k.status = @Status)
               AND (@Q IS NULL OR t.company_name LIKE CONCAT('%', @Q, '%') OR t.tenant_code LIKE CONCAT('%', @Q, '%'))
             ORDER BY k.received_at DESC
             LIMIT 300",
            new
            {
                Status = string.IsNullOrWhiteSpace(status) || status == "all" ? null : status,
                Q = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
            });
        return Ok(new { success = true, data = rows });
    }

    // ── 상세 — 본문 + 답 목록 ───────────────────────────────────────
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Detail(long id, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        var ticket = await db.QueryFirstOrDefaultAsync(@"
            SELECT k.id, t.company_name AS companyName, t.tenant_code AS tenantCode,
                   k.category, k.shape_tag AS shapeTag, k.sub_tag AS subTag, k.body,
                   k.status, k.screen_code AS screenCode, k.erp_version AS erpVersion,
                   k.received_channel AS channel, k.received_at AS receivedAt, k.completed_at AS completedAt
              FROM bo_cs_tickets k JOIN tenants t ON t.tenant_id = k.tenant_id
             WHERE k.id = @Id", new { Id = id });
        if (ticket is null) return NotFound(new { success = false, message = "티켓이 없습니다" });

        var replies = await db.QueryAsync(@"
            SELECT reply_id AS replyId, body, replied_by_kind AS repliedByKind,
                   approved_by AS approvedBy, approved_at AS approvedAt, delivered_at AS deliveredAt
              FROM bo_cs_replies WHERE ticket_id = @Id ORDER BY approved_at", new { Id = id });

        return Ok(new { success = true, ticket, replies });
    }

    // ── 답 작성 — 사람이 쓰고 그 사람이 승인자다(반자동 ②겹의 문) ────
    [HttpPost("{id:long}/reply")]
    public async Task<IActionResult> Reply(long id, [FromBody] ReplyRequest req, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(adminId)) return Forbid();

        var body = req?.Body?.Trim();
        if (string.IsNullOrWhiteSpace(body))
            return BadRequest(new { success = false, message = "답 내용을 적어 주세요" });
        if (body.Length > 4000)
            return BadRequest(new { success = false, message = "답은 4000자까지입니다" });

        await using var db = await OpenAsync(ct);
        var ticket = await db.QueryFirstOrDefaultAsync<TicketKey>(@"
            SELECT CAST(tenant_id AS CHAR) AS TenantId, client_ticket_uid AS Uid
              FROM bo_cs_tickets WHERE id = @Id", new { Id = id });
        if (ticket is null) return NotFound(new { success = false, message = "티켓이 없습니다" });

        var replyId = Guid.NewGuid().ToString();
        await db.ExecuteAsync(@"
            INSERT INTO bo_cs_replies
                (reply_id, ticket_id, tenant_id, client_ticket_uid, body, replied_by_kind, approved_by, approved_at)
            VALUES (@ReplyId, @Id, @TenantId, @Uid, @Body, 'hq', @AdminId, UTC_TIMESTAMP(6))",
            new { ReplyId = replyId, Id = id, ticket.TenantId, ticket.Uid, Body = body, AdminId = adminId });

        _logger.LogInformation("[CS어드민] 답 작성 — ticket={Id}", id);
        return Ok(new { success = true, replyId });
    }

    // ── 상태 전이 — 서버 1곳(F-15) · 고정 목록 밖 거부 · INSERT ONLY 로그 ──
    [HttpPost("{id:long}/status")]
    public async Task<IActionResult> ChangeStatus(long id, [FromBody] StatusRequest req, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(adminId)) return Forbid();

        var to = req?.To?.Trim() ?? "";
        if (!AllowedStatus.Contains(to))
            return BadRequest(new { success = false, message = "허용되지 않은 상태입니다" });

        await using var db = await OpenAsync(ct);
        var from = await db.ExecuteScalarAsync<string?>(
            "SELECT status FROM bo_cs_tickets WHERE id = @Id", new { Id = id });
        if (from is null) return NotFound(new { success = false, message = "티켓이 없습니다" });
        if (from == to) return Ok(new { success = true, unchanged = true });

        await db.ExecuteAsync(@"
            UPDATE bo_cs_tickets
               SET status = @To,
                   completed_at = CASE WHEN @To = '완료' THEN UTC_TIMESTAMP(6) ELSE completed_at END
             WHERE id = @Id;
            INSERT INTO bo_cs_ticket_logs (ticket_id, from_status, to_status, actor_id)
            VALUES (@Id, @From, @To, @AdminId);",
            new { Id = id, From = from, To = to, AdminId = adminId });

        return Ok(new { success = true });
    }

    private sealed class TicketKey { public string TenantId { get; set; } = ""; public string Uid { get; set; } = ""; }
    public class ReplyRequest { public string? Body { get; set; } }
    public class StatusRequest { public string? To { get; set; } }

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
