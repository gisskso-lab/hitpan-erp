using System.Security.Claims;
using Dapper;
using HitPan.Backoffice.API.Security;
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

    // ── 작14 C-1·C-2 추가분 ───────────────────────────────────────────
    // 분류 8종 정본(결-9) — 1차는 **사람이 고른다**(AI 분류는 묶음 D). 이 목록 밖은 거부.
    private static readonly HashSet<string> AllowedCategory = new(StringComparer.Ordinal)
    {
        "use", "set", "net", "dat", "upd", "bug", "ins", "etc",
    };

    // 업무 영역 보조태그 — 자유입력 불가(쟁점-3). 수신측(CsInboundController)과 같은 목록이다.
    private static readonly HashSet<string> AllowedSubTags = new(StringComparer.Ordinal)
    {
        "estimate_sales", "purchase_order", "stock", "invoice",
        "accounting", "hr_payroll", "settings_perm", "etc",
    };

    // 응대 주체 — ERP 계정 계층(#38)과 무관한 본사/대리점 축이다.
    private static readonly HashSet<string> AllowedHandlerType = new(StringComparer.Ordinal)
    {
        "본사2차", "대리점1차",
    };

    // 답 약속 시계 기준값(C-2). 🔴 **정본은 9.설정 몫**이다 — 설정 화면이 생기면 거기서 읽는다.
    //   지금은 서버 제안값 하나뿐이고, 사람이 화면에서 고칠 수 있다(반자동 — 자동 확정 0건).
    private const int DefaultPromiseHours = 24;

    // ── 사장님 지시 2026-10-08 — 응대 유형 3종 ────────────────────────
    //   🔴 `received_channel`(들어온 길)과 **다른 축**이다. 전화로 들어와 원격지원으로 끝날 수 있다.
    private static readonly HashSet<string> AllowedHandledVia = new(StringComparer.Ordinal)
    {
        "message",   // 쪽지로 응대
        "phone",     // 전화로 응대
        "remote",    // 원격지원
    };

    // ── 진행 4단계 — 파생값이다(칸을 새로 만들지 않는다) ──────────────
    //   ① 안 읽음      read_at IS NULL
    //   ② 처리 안 됨   읽었고 status <> '완료'
    //   ③ 처리 완료    status = '완료' 이고 평가 전
    //   ④ 평가 완료    rated_at IS NOT NULL
    //   🔴 네 단계는 **서로 겹치지 않고 합이 전체**다(G-C-10 이 그걸 문다).
    //      겹치면 화면 아래 숫자가 서로 안 맞아 「어느 게 맞나」를 사람이 판단하게 된다.
    //      그래서 조건을 네 번 따로 쓰지 않고 **단계를 한 번 정해서** 센다(겹칠 길이 없다).
    private const string StageExpr = @"
        CASE WHEN rated_at IS NOT NULL THEN 4
             WHEN status = '완료'      THEN 3
             WHEN read_at IS NULL      THEN 1
             ELSE 2 END";

    private const string StageCountSql = @"
        SELECT SUM(stage = 1) AS unread,
               SUM(stage = 2) AS open,
               SUM(stage = 3) AS done,
               SUM(stage = 4) AS rated,
               COUNT(*)       AS total
          FROM (SELECT CASE WHEN rated_at IS NOT NULL THEN 4
                            WHEN status = '완료'      THEN 3
                            WHEN read_at IS NULL      THEN 1
                            ELSE 2 END AS stage
                  FROM bo_cs_tickets) s";

    public CsAdminController(IConfiguration config, ILogger<CsAdminController> logger)
    {
        _config = config;
        _logger = logger;
    }

    // ── 목록 — 상태·유형 필터 + 회사명 검색 + 「내 응대함」(C-2 · mine=1) ──
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? q,
                                          [FromQuery] bool mine, [FromQuery] string? stage,
                                          CancellationToken ct)
    {
        var adminId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
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
                   k.handler_type    AS handlerType,
                   k.handler_id      AS handlerId,
                   k.handled_via     AS handledVia,
                   k.promised_at     AS promisedAt,
                   k.read_at         AS readAt,
                   k.rating          AS rating,
                   k.rated_at        AS ratedAt,
                   k.received_at     AS receivedAt,
                   (SELECT COUNT(*) FROM bo_cs_replies r WHERE r.ticket_id = k.id) AS replyCount
              FROM bo_cs_tickets k
              JOIN tenants t ON t.tenant_id = k.tenant_id
             WHERE (@Status IS NULL OR k.status = @Status)
               AND (@Mine = 0 OR k.handler_id = @AdminId)
               -- 4단계 필터 — 화면 아래 띠의 숫자를 누르면 그 단계만 보인다(세는 식과 **같은 식**)
               AND (@Stage IS NULL OR @Stage = 'all' OR
                    (CASE WHEN k.rated_at IS NOT NULL THEN 'rated'
                          WHEN k.status = '완료'      THEN 'done'
                          WHEN k.read_at IS NULL      THEN 'unread'
                          ELSE 'open' END) = @Stage)
               AND (@Q IS NULL OR t.company_name LIKE CONCAT('%', @Q, '%') OR t.tenant_code LIKE CONCAT('%', @Q, '%'))
             ORDER BY k.received_at DESC
             LIMIT 300",
            new
            {
                Status = string.IsNullOrWhiteSpace(status) || status == "all" ? null : status,
                Q = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
                Mine = mine ? 1 : 0,
                AdminId = adminId,
                Stage = string.IsNullOrWhiteSpace(stage) || stage == "all" ? null : stage.Trim(),
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
                   k.handler_type AS handlerType, k.handler_id AS handlerId, k.promised_at AS promisedAt,
                   k.received_channel AS channel, k.received_at AS receivedAt, k.completed_at AS completedAt
              FROM bo_cs_tickets k JOIN tenants t ON t.tenant_id = k.tenant_id
             WHERE k.id = @Id", new { Id = id });
        if (ticket is null) return NotFound(new { success = false, message = "티켓이 없습니다" });

        var replies = await db.QueryAsync(@"
            SELECT reply_id AS replyId, body, replied_by_kind AS repliedByKind,
                   approved_by AS approvedBy, approved_at AS approvedAt, delivered_at AS deliveredAt
              FROM bo_cs_replies WHERE ticket_id = @Id ORDER BY approved_at", new { Id = id });

        // 🔴 여는 순간이 「읽음」이다 — 단 **최초 1회만** 기록한다(WHERE read_at IS NULL).
        //    덮어쓰면 「아무도 안 본 채 며칠 지났다」는 사실이 사라진다. 그 사실이 CS 의 핵심 자료다.
        var reader = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        await db.ExecuteAsync(@"
            UPDATE bo_cs_tickets
               SET read_at = UTC_TIMESTAMP(6), read_by = @Reader
             WHERE id = @Id AND read_at IS NULL",
            new { Id = id, Reader = reader });

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

        // 🔴 완료로 넘길 때는 **응대 유형**이 있어야 한다(쪽지·전화·원격지원 중 하나).
        //    나중에 CS 실적을 만들 때 이 칸이 비어 있으면 **소급이 안 된다** — 그래서 지금 막는다.
        if (to == "완료")
        {
            var via = await db.ExecuteScalarAsync<string?>(
                "SELECT handled_via FROM bo_cs_tickets WHERE id = @Id", new { Id = id });
            if (string.IsNullOrEmpty(via))
                return BadRequest(new { success = false, message = "어떻게 응대했는지(쪽지·전화·원격지원) 먼저 골라 주세요" });
        }

        await db.ExecuteAsync(@"
            UPDATE bo_cs_tickets
               SET status = @To,
                   completed_at = CASE WHEN @To = '완료' THEN UTC_TIMESTAMP(6) ELSE completed_at END,
                   read_at = COALESCE(read_at, UTC_TIMESTAMP(6)),
                   read_by = COALESCE(read_by, @AdminId)
             WHERE id = @Id;
            INSERT INTO bo_cs_ticket_logs (ticket_id, from_status, to_status, actor_id)
            VALUES (@Id, @From, @To, @AdminId);",
            new { Id = id, From = from, To = to, AdminId = adminId });

        return Ok(new { success = true });
    }

    // ── 진행 4단계 숫자 — 사이드바 빨간 숫자와 화면 아래 띠가 같은 값을 읽는다 ──
    //    (두 곳이 각자 세면 숫자가 어긋나고, 그 순간 사람이 화면을 못 믿는다)
    [HttpGet("counts")]
    public async Task<IActionResult> Counts(CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        var row = await db.QueryFirstAsync(StageCountSql);
        return Ok(new
        {
            success = true,
            unread = (long?)row.unread ?? 0,   // ① 읽지 않음
            open = (long?)row.open ?? 0,       // ② 처리되지 않음
            done = (long?)row.done ?? 0,       // ③ 처리 완료
            rated = (long?)row.rated ?? 0,     // ④ CS평가 완료
            total = (long)row.total,
        });
    }

    // ── 응대 유형 — 쪽지/전화/원격지원 (들어온 길과 다른 축) ──────────
    [HttpPost("{id:long}/handled-via")]
    public async Task<IActionResult> SetHandledVia(long id, [FromBody] HandledViaRequest req, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(adminId)) return Forbid();

        var via = req?.Via?.Trim() ?? "";
        if (!AllowedHandledVia.Contains(via))
            return BadRequest(new { success = false, message = "응대 유형은 쪽지·전화·원격지원 중 하나입니다" });

        await using var db = await OpenAsync(ct);
        var n = await db.ExecuteAsync(
            "UPDATE bo_cs_tickets SET handled_via = @Via WHERE id = @Id", new { Id = id, Via = via });
        if (n == 0) return NotFound(new { success = false, message = "티켓이 없습니다" });

        await db.ExecuteAsync(@"
            INSERT INTO bo_cs_ticket_logs (ticket_id, from_status, to_status, actor_id)
            VALUES (@Id, '응대', @Via, @AdminId)",
            new { Id = id, Via = via, AdminId = adminId });
        return Ok(new { success = true, handledVia = via });
    }

    // ── C-1 분류 다시 고르기 — 8종 정본 밖은 거부 · 전이 기록은 상태 로그와 같은 표 ──
    //    사람이 고치는 것이 1차 설계값이다(규칙 1차 분류는 수신측이 넣고, 최종은 사람).
    [HttpPost("{id:long}/category")]
    public async Task<IActionResult> Recategorize(long id, [FromBody] CategoryRequest req, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(adminId)) return Forbid();

        var to = req?.To?.Trim() ?? "";
        if (!AllowedCategory.Contains(to))
            return BadRequest(new { success = false, message = "분류를 8종 중에서 골라 주세요" });

        await using var db = await OpenAsync(ct);
        var from = await db.ExecuteScalarAsync<string?>(
            "SELECT category FROM bo_cs_tickets WHERE id = @Id", new { Id = id });
        if (from is null) return NotFound(new { success = false, message = "티켓이 없습니다" });
        if (from == to) return Ok(new { success = true, unchanged = true });

        await db.ExecuteAsync(@"
            UPDATE bo_cs_tickets SET category = @To WHERE id = @Id;
            INSERT INTO bo_cs_ticket_logs (ticket_id, from_status, to_status, actor_id)
            VALUES (@Id, CONCAT('분류:', @From), CONCAT('분류:', @To), @AdminId);",
            new { Id = id, From = from, To = to, AdminId = adminId });

        return Ok(new { success = true });
    }

    // ── C-2 내 응대함 — 배정. 누가 잡았는지가 남아야 「아무도 안 본 글」이 안 생긴다 ──
    [HttpPost("{id:long}/assign")]
    public async Task<IActionResult> Assign(long id, [FromBody] AssignRequest req, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(adminId)) return Forbid();

        var kind = string.IsNullOrWhiteSpace(req?.HandlerType) ? "본사2차" : req!.HandlerType!.Trim();
        if (!AllowedHandlerType.Contains(kind))
            return BadRequest(new { success = false, message = "응대 주체가 목록 밖입니다" });

        await using var db = await OpenAsync(ct);
        var exists = await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM bo_cs_tickets WHERE id = @Id", new { Id = id });
        if (exists == 0) return NotFound(new { success = false, message = "티켓이 없습니다" });

        await db.ExecuteAsync(@"
            UPDATE bo_cs_tickets SET handler_id = @AdminId, handler_type = @Kind WHERE id = @Id;
            INSERT INTO bo_cs_ticket_logs (ticket_id, from_status, to_status, actor_id)
            VALUES (@Id, '배정', @Kind, @AdminId);",
            new { Id = id, AdminId = adminId, Kind = kind });

        return Ok(new { success = true, handlerId = adminId, handlerType = kind });
    }

    // ── C-2 답 약속 시계 — 서버가 제안하고 사람이 고친다(기준값 정본은 9.설정) ──
    [HttpPost("{id:long}/promise")]
    public async Task<IActionResult> SetPromise(long id, [FromBody] PromiseRequest req, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(adminId)) return Forbid();

        await using var db = await OpenAsync(ct);
        var exists = await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM bo_cs_tickets WHERE id = @Id", new { Id = id });
        if (exists == 0) return NotFound(new { success = false, message = "티켓이 없습니다" });

        // 제안값 = 지금 + 기준 시간. 사람이 보낸 값이 있으면 그것을 쓴다(반자동).
        var at = req?.At ?? DateTime.UtcNow.AddHours(DefaultPromiseHours);
        await db.ExecuteAsync(@"
            UPDATE bo_cs_tickets SET promised_at = @At WHERE id = @Id;
            INSERT INTO bo_cs_ticket_logs (ticket_id, from_status, to_status, actor_id)
            VALUES (@Id, '약속', '약속설정', @AdminId);",
            new { Id = id, At = at, AdminId = adminId });

        return Ok(new { success = true, promisedAt = at });
    }

    // ── C-2 전화 접수 30초 틀 — 전화로 들어온 것도 같은 표에 쌓인다 ──
    //    🔴 금지필드 검사는 쪽지와 **같은 규칙 한 벌**(BoForbiddenFieldScanner)을 쓴다 —
    //       전화 받아 적다가 식별정보가 들어가는 길이 열려 있으면 문③이 반쪽이다.
    [HttpPost("phone")]
    public async Task<IActionResult> PhoneIntake([FromBody] PhoneRequest req, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(adminId)) return Forbid();

        var code = req?.TenantCode?.Trim() ?? "";
        var category = req?.Category?.Trim() ?? "";
        var subTag = string.IsNullOrWhiteSpace(req?.SubTag) ? "etc" : req!.SubTag!.Trim();
        var body = req?.Body?.Trim() ?? "";

        if (string.IsNullOrEmpty(code))
            return BadRequest(new { success = false, message = "고객사(테넌트넘버)를 골라 주세요" });
        if (!AllowedCategory.Contains(category))
            return BadRequest(new { success = false, message = "분류를 8종 중에서 골라 주세요" });
        if (!AllowedSubTags.Contains(subTag))
            return BadRequest(new { success = false, message = "업무 영역이 목록 밖입니다" });
        if (body.Length is 0 or > 2000)
            return BadRequest(new { success = false, message = "통화 내용은 1~2000자입니다" });

        var rule = BoForbiddenFieldScanner.Scan(body);
        if (rule is not null)
            return BadRequest(new { success = false, message = "적으신 내용에 식별정보 모양이 있습니다 — 그 줄을 지워 주세요" });

        await using var db = await OpenAsync(ct);
        var tenantId = await db.ExecuteScalarAsync<string?>(
            "SELECT CAST(tenant_id AS CHAR) FROM tenants WHERE tenant_code = @Code", new { Code = code });
        if (tenantId is null) return NotFound(new { success = false, message = "그 테넌트넘버의 고객사가 없습니다" });

        var uid = Guid.NewGuid().ToString();
        var id = await db.ExecuteScalarAsync<long>(@"
            INSERT INTO bo_cs_tickets
                (tenant_id, client_ticket_uid, received_channel, category, sub_tag, body,
                 status, handler_type, handler_id, promised_at)
            VALUES (@TenantId, @Uid, 'phone', @Category, @SubTag, @Body,
                    '접수', '본사2차', @AdminId, @Promise);
            SELECT LAST_INSERT_ID();",
            new
            {
                TenantId = tenantId, Uid = uid, Category = category, SubTag = subTag, Body = body,
                AdminId = adminId, Promise = DateTime.UtcNow.AddHours(DefaultPromiseHours),
            });

        _logger.LogInformation("[CS어드민] 전화 접수 — ticket={Id}", id);
        return Ok(new { success = true, id });
    }

    private sealed class TicketKey { public string TenantId { get; set; } = ""; public string Uid { get; set; } = ""; }
    public class ReplyRequest { public string? Body { get; set; } }
    public class StatusRequest { public string? To { get; set; } }
    public class CategoryRequest { public string? To { get; set; } }
    public class HandledViaRequest { public string? Via { get; set; } }
    public class AssignRequest { public string? HandlerType { get; set; } }
    public class PromiseRequest { public DateTime? At { get; set; } }

    public class PhoneRequest
    {
        public string? TenantCode { get; set; }
        public string? Category { get; set; }
        public string? SubTag { get; set; }
        public string? Body { get; set; }
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
