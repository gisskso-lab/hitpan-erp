using System.Security.Claims;
using Dapper;
using HitPan.Backoffice.API.Security;
using HitPan.Backoffice.API.Services;
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

    // 🔴 20261013작1 S-1 — A/S 접수경로(손으로 적는 접수). 자유입력 불가.
    //   왜 칸만 더하면 안 되나: 요청에 경로 칸을 열면 아무 글자나 그대로 `received_channel` 에 앉는다
    //   ⇒ 목록·상세의 경로 낱말이 뜻을 잃는다. 칸과 화이트리스트는 **한 몸**이다(설계 §6).
    //   🔴 `erp_message`(고객사에서 올라온 쪽지)는 여기에 **넣지 않는다** — 그 값은 수신 통로
    //      (CsInboundController)가 앉히는 값이고, 사람이 손으로 고를 값이 아니다.
    //      여기에 넣으면 본사 담당자가 「고객사가 보낸 것」으로 꾸밀 수 있는 길이 열린다.
    private static readonly HashSet<string> AllowedIntakeChannel = new(StringComparer.Ordinal)
    {
        "phone", "email", "sns", "etc",
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
    //   🔴 20261013작2 S-1 — 사장님 실측 반려로 값을 **넓혔다**. 옛 3값은 그대로 산다(#1 추가만).
    //      옛 값 `message` 를 지우지 않는 이유: 이미 그 값으로 앉은 행을 읽어야 한다.
    private static readonly HashSet<string> AllowedHandledVia = new(StringComparer.Ordinal)
    {
        "message",   // 쪽지로 응대 (옛 값 — 읽기 전용으로 산다)
        "phone",     // 전화로 응대
        "remote",    // 원격지원
        // ↓ 20261013작2 추가분
        "as_center", // 히트판 A/S센터 — 🔴 **서버만 앉힌다**(사람 입력 목록에 없다)
        "email",     // 이메일로 응대
        "sns",       // SNS 로 응대
        "etc",       // 기타
    };

    // 🔴 20261013작2 S-1 — **사람이 고를 수 있는** 응대 유형(사장님 결재 2026-10-13).
    //   왜 집합을 둘로 두나: `as_center` 를 여기에 넣으면 본사 담당자가 **손으로 적은 A/S 를
    //   「히트판 A/S센터가 받은 것」으로 꾸밀 수 있다.** 쪽지로 들어온 건만 그 값을 가져야 한다
    //   (작1 S-1 에서 `erp_message` 를 접수경로 입력에서 뺀 것과 **같은 선**이다).
    //   옛 값 `message` 도 여기엔 없다 — 쪽지 응대의 정본은 이제 `as_center` 다.
    private static readonly HashSet<string> AllowedHandledViaManual = new(StringComparer.Ordinal)
    {
        "phone", "remote", "email", "sns", "etc",
    };

    /// <summary>쪽지로 들어온 A/S 의 응대 유형 — 고정값(사장님 결재 2026-10-13).</summary>
    private const string AsCenterHandledVia = "as_center";

    /// <summary>고객사 ERP 쪽지로 들어온 건의 <c>received_channel</c> — 수신 통로가 앉히는 값.</summary>
    private const string ErpMessageChannel = "erp_message";

    // 🔴 20261013작2 S-4 — 손으로 적은 A/S 가 **기록할 수 있는 상태**(사장님 지시).
    //   「처리상태를 고객사에게 보여주지 않으므로 접수와 완료만 기록한다」
    //   ⇒ 중간 상태는 고객사가 볼 수 없는데 본사만 적어 두면 **아무도 안 읽는 기록**이 된다.
    private static readonly HashSet<string> AllowedManualStatus = new(StringComparer.Ordinal)
    {
        "접수", "완료",
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

    // 🔴 작5 — ILoggerFactory 는 「지금 준비하기」가 적용기에 넘길 로거용(추가만 · 기존 인자 무변).
    private readonly ILoggerFactory _loggerFactory;

    public CsAdminController(IConfiguration config, ILogger<CsAdminController> logger, ILoggerFactory loggerFactory)
    {
        _config = config;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    // ── 목록 — 상태·유형 필터 + 회사명 검색 + 「내 응대함」(C-2 · mine=1) ──
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? q,
                                          [FromQuery] bool mine, [FromQuery] string? stage,
                                          CancellationToken ct)
    {
        var adminId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        await using var db = await OpenAsync(ct);
        // 🔴 [4] V-9 봉합 — PRD C-13 은 「**목록**·상세에 「폐기됨」 표시」다. 목록이 `purged_at` 을
        //   아예 안 돌려줘서 목록에서는 폐기된 쪽지가 보통 쪽지와 구별되지 않았다(반쪽 구현).
        //   V-2 와 같은 이유로 **칸이 있을 때만** 읽는다.
        var hasPurge = await HasPurgeColumnsAsync(db);
        var rows = await db.QueryAsync($@"
            SELECT k.id              AS id,
                   {(hasPurge ? "k.purged_at" : "NULL")} AS purgedAt,
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
        var hasPurge = await HasPurgeColumnsAsync(db);
        var ticket = await db.QueryFirstOrDefaultAsync($@"
            SELECT k.id, t.company_name AS companyName, t.tenant_code AS tenantCode,
                   k.category, k.shape_tag AS shapeTag, k.sub_tag AS subTag, k.body,
                   k.status, k.screen_code AS screenCode, k.erp_version AS erpVersion,
                   k.handler_type AS handlerType, k.handler_id AS handlerId, k.promised_at AS promisedAt,
                   k.received_channel AS channel, k.received_at AS receivedAt, k.completed_at AS completedAt,
                   k.handled_via AS handledVia,
                   -- 🔴 20261013작2 S-5 — 오늘까지 이 SELECT 가 **안 읽던 네 칸**(사장님 반려 2)).
                   --   그래서 상세에서 고객평가 ★ 가 **영영 안 떴고**, 「안 읽음」이 늘 붙었다.
                   --   칸은 `42_backoffice_cs_stages.sql` 에 이미 있었다 — 읽는 쪽이 없었을 뿐이다.
                   k.rating, k.rated_at AS ratedAt,
                   -- 🔴 20261013작2 [3-V] V-6 봉합 — **폐기된 건의 고객 자유입력은 안 내보낸다.**
                   --   사고 모양: 폐기는 `body`(와 답 본문)만 비우는데, S-5 가 고객이 평가에 쓴 말
                   --   (`rating_comment` · varchar(500))을 **처음으로** 상세에 내려주기 시작했다
                   --   ⇒ 「폐기됨」 칩이 뜬 건을 열면 고객이 쓴 말이 그대로 보였다.
                   --   작1 V-3 와 **같은 선**이다: 몸통은 비우고 사실은 남긴다. 평가 점수·시각은 사실이라 남긴다.
                   {(hasPurge ? "CASE WHEN k.purged_at IS NULL THEN k.rating_comment ELSE NULL END AS ratingComment"
                              : "k.rating_comment AS ratingComment")},
                   k.read_at AS readAt,
                   -- 🔴 20261011작2 C-13 — 폐기 표식. 화면이 「폐기됨」을 보여주고 두 번 지우지 않게 한다.
                   --   [4] V-2 봉합: 45번 미적용 서버에서는 이 두 칸을 **안 읽는다**(위 HasPurgeColumnsAsync).
                   {(hasPurge ? "k.purged_at AS purgedAt, k.purge_reason AS purgeReason"
                              : "NULL AS purgedAt, NULL AS purgeReason")}
              FROM bo_cs_tickets k JOIN tenants t ON t.tenant_id = k.tenant_id
             WHERE k.id = @Id", new { Id = id });
        if (ticket is null) return NotFound(new { success = false, message = "티켓이 없습니다" });

        // 🔴 [4] V-3 봉합 — 폐기된 티켓이면 **답장 본문도 안 내보낸다.**
        //   종전엔 `bo_cs_tickets.body` 만 비웠고 `bo_cs_replies.body` 는 그대로 나갔다 ⇒
        //   「폐기됨」 칩이 뜬 티켓을 열면 **본사 답장 전문이 보였다.** 설계가 선언한
        //   「몸통은 비우고 사실은 남긴다」가 대화의 절반에서 깨진 자리다(개인정보·법무 축).
        //   폐기 경로가 `bo_cs_replies.body` 를 이미 비우지만(아래 PurgeTicket), 그 전에 들어온
        //   행·다른 경로가 남길 수 있으므로 **읽는 쪽에서도 한 번 더 막는다**(두 겹).
        var purgedNow = hasPurge
            && ((IDictionary<string, object?>)ticket!).TryGetValue("purgedAt", out var pv) && pv is not null;

        var replies = await db.QueryAsync($@"
            SELECT reply_id AS replyId,
                   {(purgedNow ? "NULL AS body" : "body")},
                   replied_by_kind AS repliedByKind,
                   approved_by AS approvedBy, approved_at AS approvedAt, delivered_at AS deliveredAt
              FROM bo_cs_replies WHERE ticket_id = @Id ORDER BY approved_at", new { Id = id });

        // 🔴 20261013작2 S-6 — **처리 과정**(사장님 반려 1)).
        //   이 표는 상태 전이마다 5곳에서 INSERT 해 왔는데 **읽는 SELECT 가 레포 전체에 0건**이었다.
        //   쌓아 두고 아무도 안 보여준 자료다. 엔드포인트를 새로 내지 않고 **상세 응답에 얹는다.**
        //   🔴 폐기된 건에도 과정은 내보낸다 — 여기엔 몸통이 없다(상태값·시각·누가). 작1 V-3 와 같은 선:
        //      「몸통은 비우고 사실은 남긴다」.
        //   🔴 `bo_cs_ticket_logs` 는 INSERT ONLY — 여기서는 **읽기만** 한다.
        var logs = await db.QueryAsync(@"
            SELECT from_status AS fromStatus, to_status AS toStatus,
                   actor_id AS actorId, acted_at AS actedAt
              FROM bo_cs_ticket_logs WHERE ticket_id = @Id ORDER BY acted_at", new { Id = id });

        // 🔴 여는 순간이 「읽음」이다 — 단 **최초 1회만** 기록한다(WHERE read_at IS NULL).
        //    덮어쓰면 「아무도 안 본 채 며칠 지났다」는 사실이 사라진다. 그 사실이 CS 의 핵심 자료다.
        var reader = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        await db.ExecuteAsync(@"
            UPDATE bo_cs_tickets
               SET read_at = UTC_TIMESTAMP(6), read_by = @Reader
             WHERE id = @Id AND read_at IS NULL",
            new { Id = id, Reader = reader });

        // 🔴 기존 두 칸(`ticket`·`replies`)의 모양은 **한 글자도 안 바뀐다** — `logs` 는 더한 칸이다(하위호환).
        return Ok(new { success = true, ticket, replies, logs });
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
        // 🔴 20261013작2 — 상태와 **들어온 길**을 같이 읽는다. 길에 따라 규칙이 갈린다(S-3·S-4).
        var cur = await db.QueryFirstOrDefaultAsync<TicketRuleRow>(
            @"SELECT status AS Status, received_channel AS Channel, handled_via AS HandledVia
                FROM bo_cs_tickets WHERE id = @Id", new { Id = id });
        var from = cur?.Status;
        if (from is null) return NotFound(new { success = false, message = "티켓이 없습니다" });
        if (from == to) return Ok(new { success = true, unchanged = true });

        var isErpMessage = string.Equals(cur!.Channel, ErpMessageChannel, StringComparison.Ordinal);

        // 🔴 20261013작2 S-4 — 손으로 적은 A/S 는 **접수·완료만** 기록한다(사장님 지시 2026-10-13).
        //   🔴 막는 것은 **새로 고르는 것**뿐이다. 이미 다른 값으로 앉은 행은 위 `from == to` 에서
        //      그대로 통과하고, 이 컨트롤러는 그 값을 **고치지 않는다**(#1 · 지우면 사실이 사라진다).
        if (!isErpMessage && !AllowedManualStatus.Contains(to))
            return BadRequest(new
            {
                success = false,
                message = "손으로 적은 A/S 는 접수와 완료만 기록합니다 — 중간 과정은 고객사에 보이지 않습니다",
            });

        // 🔴 완료로 넘길 때는 **응대 유형**이 있어야 한다.
        //    나중에 CS 실적을 만들 때 이 칸이 비어 있으면 **소급이 안 된다** — 그래서 지금 막는다.
        //    🔴 20261013작2 S-3 — 단, **쪽지로 들어온 건은 묻지 않는다.** 그 건의 응대 창구는
        //       처음부터 히트판 A/S센터 하나다(사장님 결재). 사람 손을 요구한 것이 반려 사유 4) 였다.
        if (to == "완료")
        {
            var via = cur.HandledVia;
            if (string.IsNullOrEmpty(via))
            {
                if (isErpMessage)
                {
                    // 🔴 **빈 칸만 채운다**(조건 C-2) — `AND handled_via IS NULL` 이 옛 값을 지킨다.
                    var filled = await db.ExecuteAsync(
                        @"UPDATE bo_cs_tickets SET handled_via = @Via
                           WHERE id = @Id AND (handled_via IS NULL OR handled_via = '')",
                        new { Id = id, Via = AsCenterHandledVia });

                    // 🔴 20261013작2 [3-V] V-7 봉합 — 서버가 앉힌 사실도 **처리 과정에 남긴다.**
                    //   다른 다섯 자리는 전부 기록을 남기는데 이 자리만 조용히 칸을 바꿨다 ⇒
                    //   「응대유형이 언제 생겼나」가 사장님이 보는 과정에서 빠졌다(반려 1) 이 물은 바로 그 칸).
                    if (filled > 0)
                    {
                        await db.ExecuteAsync(@"
                            INSERT INTO bo_cs_ticket_logs (ticket_id, from_status, to_status, actor_id)
                            VALUES (@Id, '응대', @Via, @AdminId)",
                            new { Id = id, Via = AsCenterHandledVia, AdminId = adminId });
                    }
                }
                else
                {
                    return BadRequest(new { success = false, message = "어떻게 응대했는지 먼저 골라 주세요" });
                }
            }
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

        // 🔴 20261013작2 S-2 — **사람이 고를 수 있는 목록**으로만 받는다(`as_center` 는 여기 없다).
        var via = req?.Via?.Trim() ?? "";
        if (!AllowedHandledViaManual.Contains(via))
            return BadRequest(new
            {
                success = false,
                message = "응대 유형은 전화·원격지원·이메일·SNS·기타 중 하나입니다",
            });

        await using var db = await OpenAsync(ct);

        // 🔴 20261013작2 S-2 — 쪽지로 들어온 건의 응대 유형은 **사람이 바꾸지 못한다**(사장님 결재).
        //   그 건의 창구는 히트판 A/S센터 하나다. 열어 두면 「전화로 응대했다」로 바꿔 적을 수 있고,
        //   그 순간 쪽지가 전화를 얼마나 줄였는지를 세는 자료가 뜻을 잃는다.
        var channel = await db.ExecuteScalarAsync<string?>(
            "SELECT received_channel FROM bo_cs_tickets WHERE id = @Id", new { Id = id });
        if (channel is null) return NotFound(new { success = false, message = "티켓이 없습니다" });
        if (string.Equals(channel, ErpMessageChannel, StringComparison.Ordinal))
            return BadRequest(new
            {
                success = false,
                message = "쪽지로 들어온 A/S 는 히트판 A/S센터 응대로 고정입니다",
            });

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
        // 🔴 20261013작1 S-2 — 안 보내면 '전화'로 떨어뜨린다. 종전 호출자(경로 칸을 모르는 화면)는
        //   그대로 돌아야 한다(#1 — 덮어쓰기 금지 · 하위호환).
        var channel = string.IsNullOrWhiteSpace(req?.Channel) ? "phone" : req!.Channel!.Trim();

        if (string.IsNullOrEmpty(code))
            return BadRequest(new { success = false, message = "고객사(테넌트넘버)를 골라 주세요" });
        if (!AllowedCategory.Contains(category))
            return BadRequest(new { success = false, message = "분류를 8종 중에서 골라 주세요" });
        if (!AllowedSubTags.Contains(subTag))
            return BadRequest(new { success = false, message = "업무 영역이 목록 밖입니다" });
        if (!AllowedIntakeChannel.Contains(channel))
            return BadRequest(new { success = false, message = "접수경로를 목록에서 골라 주세요(전화·이메일·SNS·기타)" });
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
            -- 🔴 [4] V-6 봉합 — `received_at` 을 **명시**한다. DDL 의 `DEFAULT current_timestamp(6)` 는
            --   세션 시간대(이 서버는 KST)를 쓰고, 이 레포의 코드는 전부 `UTC_TIMESTAMP(6)` 로 쓴다.
            --   섞이면 같은 표에 **9시간 어긋난 줄**이 생긴다(로컬 실측 540분). 그러면
            --   보관기한 판정(`received_at < DATE_SUB(UTC_TIMESTAMP(6), …)`)이 전화접수 건만
            --   9시간 늦게 넘긴다. ERP 수신 경로(`CsInboundController`)는 이미 UTC 를 명시한다 —
            --   전화접수만 DEFAULT 에 맡기고 있었다. 같은 모양으로 맞춘다.
            INSERT INTO bo_cs_tickets
                (tenant_id, client_ticket_uid, received_channel, category, sub_tag, body,
                 status, handler_type, handler_id, promised_at, received_at)
            VALUES (@TenantId, @Uid, @Channel, @Category, @SubTag, @Body,
                    '접수', '본사2차', @AdminId, @Promise, UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();",
            new
            {
                TenantId = tenantId, Uid = uid, Channel = channel, Category = category, SubTag = subTag, Body = body,
                AdminId = adminId, Promise = DateTime.UtcNow.AddHours(DefaultPromiseHours),
            });

        _logger.LogInformation("[CS어드민] 전화 접수 — ticket={Id}", id);
        return Ok(new { success = true, id });
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 🔴 20261011작2 — 사장님 오더 3건 (작업지시서 20261011작2 §1·§3)
    //
    //   ⓞ 전화접수 창에서 「고객사 코드」 대신 **고객사명**으로 찾게 한다 (A-1)
    //   ③ 본사가 고객사에 **먼저 쪽지를 보낸다** — 프로모션·이벤트·과금 (C-2·C-3)
    //      🔴 지금 ERP 수신부는 원본 티켓이 없는 답을 **조용히 버린다**
    //         (CsOutboxSenderWorker.cs:316 — `FROM cs_requests r WHERE r.cs_request_id=@Uid` 0행 ⇒ INSERT 0건).
    //         그래서 **보내는 척하지 않는다.** 큐에 쌓고 「전달 대기」로 정직하게 보인다.
    //         ERP CS 파이프라인(다음 작업)이 열리면 쌓인 것이 그대로 나간다(#20).
    //      ③ 보관기한 설정 + 임의 폐기 (C-6~C-13) — **자동 삭제 0**, 폐기는 사실을 남긴다.
    //
    //   권한: 이 컨트롤러는 [Authorize(Policy="PlatformAdmin")] 이라 새 입구도 같은 정책을 물려받는다
    //         ⇒ 권한 시드(90번) 변경 0.
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A-1 — 고객사명으로 찾아 코드를 돌려준다. **두 칸만** 나간다(#22 본사 데이터 최소주의).
    /// <para>🔴 기존 `GET admin/tenants?search=` 를 쓰지 않는 이유: 그 입구는
    /// <c>[BoPermission("tenants.list")]</c> 라 CS 담당 계정이면 403 이 될 수 있고, 고객사 9칸을 다 돌려준다.
    /// CS 화면에 필요한 것은 이름과 코드 둘뿐이다.</para>
    /// </summary>
    [HttpGet("tenant-lookup")]
    public async Task<IActionResult> TenantLookup([FromQuery] string? q, CancellationToken ct)
    {
        var keyword = q?.Trim() ?? "";
        if (keyword.Length < 1)
        {
            return Ok(new { success = true, items = Array.Empty<object>() });
        }

        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<TenantLookupRow>(@"
            SELECT tenant_code AS TenantCode, company_name AS CompanyName
              FROM tenants
             WHERE company_name LIKE @Q OR tenant_code LIKE @Q
             ORDER BY company_name
             LIMIT 20",
            new { Q = $"%{keyword}%" });

        return Ok(new { success = true, items = rows });
    }

    /// <summary>🔴 20261013작1 보강 B-1 — 「전체」를 고르면 **몇 곳에 나가는지 먼저 보여준다**.
    /// <para>왜 필요한가: 전체 발송은 누르면 되돌리기가 고객사 수만큼 드는 동작이다.
    /// 숫자를 보고 누르는 것과 모르고 누르는 것은 다르다. 읽기만 하고 아무것도 쓰지 않는다.</para></summary>
    [HttpGet("outbound/target-count")]
    public async Task<IActionResult> OutboundTargetCount(CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        var count = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM tenants WHERE status IN ('active','trial')");
        return Ok(new { success = true, count });
    }

    /// <summary>C-2 — 본사가 먼저 보내는 쪽지를 **전달 대기 큐**에 넣는다.</summary>
    [HttpPost("outbound")]
    public async Task<IActionResult> CreateOutbound([FromBody] OutboundRequest req, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(adminId)) return Forbid();

        var code = req?.TenantCode?.Trim() ?? "";
        var toAll = req?.All == true;
        var purpose = string.IsNullOrWhiteSpace(req?.Purpose) ? "etc" : req!.Purpose!.Trim();
        var title = req?.Title?.Trim();
        var body = req?.Body?.Trim() ?? "";

        // 🔴 20261013작1 보강 B-1 — 「전체」는 고객사 코드를 비우고 All=true 로 온다.
        //   한 곳 보내기 경로는 한 글자도 바뀌지 않는다(#1 — 추가만).
        if (!toAll && string.IsNullOrEmpty(code))
            return BadRequest(new { success = false, message = "어느 고객사에 보낼지 골라 주세요" });
        if (!AllowedOutboundPurpose.Contains(purpose))
            return BadRequest(new { success = false, message = "용도를 목록에서 골라 주세요(업데이트공지사항·히트판 공지사항·프로모션 안내·기타 공지사항)" });
        if (body.Length is 0 or > 2000)
            return BadRequest(new { success = false, message = "보낼 내용은 1~2000자입니다" });
        if (title is { Length: > 100 })
            return BadRequest(new { success = false, message = "제목은 100자까지입니다" });

        // 🔴 금지필드 검사는 쪽지·전화접수와 **같은 한 벌**을 쓴다 — 내보내는 글에도 같은 문이 서야 한다.
        if (BoForbiddenFieldScanner.Scan(body) is not null
            || (title is not null && BoForbiddenFieldScanner.Scan(title) is not null))
        {
            return BadRequest(new { success = false, message = "적으신 내용에 식별정보 모양이 있습니다 — 그 줄을 지워 주세요" });
        }

        await using var db = await OpenAsync(ct);
        // 🔴 작4 A-1 — 45번 미적용 서버에서 500 대신 사람이 읽을 답(선행검증서 §2-1).
        if (!await HasOutboundSchemaAsync(db)) return SchemaPendingResult();

        // ── 🔴 20261013작1 보강 B-1 — 「전체」 발송 ───────────────────────────────
        //   사장님 결재(2026-10-13): 「지금 쓰고 있는 고객사만」 = status ∈ {active, trial}.
        //   🔴 정지·해지·가입대기 고객사에는 **보내지 않는다** — 해지한 곳에 본사 공지가
        //      들어가는 것이 이 결재가 막은 사고다.
        //   왜 고객사마다 한 행인가: `bo_cs_outbound.tenant_id` 가 NOT NULL 이고
        //   `outbound_id` 가 ERP 전달의 멱등 열쇠다(45번 DDL). 한 행으로 묶으면
        //   ① 고객사별 취소·폐기가 불가능해지고 ② ERP 가 고객사별로 가져가는 길이 끊긴다(#20).
        //   ⇒ DDL 0건으로 가는 유일한 모양이 **N행**이다.
        if (toAll)
        {
            var targets = (await db.QueryAsync<string>(
                @"SELECT CAST(tenant_id AS CHAR)
                    FROM tenants
                   WHERE status IN ('active','trial')
                   ORDER BY company_name")).ToList();

            if (targets.Count == 0)
                return BadRequest(new { success = false, message = "지금 쓰고 있는 고객사가 없습니다 — 보낼 곳이 없습니다" });

            var rows = targets.Select(t => new
            {
                Id = Guid.NewGuid().ToString(),
                TenantId = t,
                Purpose = purpose,
                Title = title,
                Body = body,
                AdminId = adminId,
            }).ToList();

            // 🔴 한 거래로 묶는다 — 중간에 끊기면 「절반만 나간 공지」가 남는다.
            await using var tx = await db.BeginTransactionAsync(ct);
            await db.ExecuteAsync(@"
                INSERT INTO bo_cs_outbound
                    (outbound_id, tenant_id, purpose, title, body, created_by, created_at)
                VALUES (@Id, @TenantId, @Purpose, @Title, @Body, @AdminId, UTC_TIMESTAMP(6))",
                rows, tx);
            await tx.CommitAsync(ct);

            _logger.LogInformation("[CS어드민] 선발신 전체 발송 — 고객사 {Count}곳 purpose={P}", rows.Count, purpose);

            // 🔴 「보냈습니다」라고 말하지 않는다 — 전달은 ERP 통로가 열린 뒤다.
            return Ok(new
            {
                success = true,
                sentCount = rows.Count,
                delivered = false,
                message = $"고객사 {rows.Count}곳의 전달 대기함에 넣었습니다 — ERP 수신 통로가 열리면 자동으로 나갑니다",
            });
        }

        var tenantId = await db.ExecuteScalarAsync<string?>(
            "SELECT CAST(tenant_id AS CHAR) FROM tenants WHERE tenant_code = @Code", new { Code = code });
        if (tenantId is null) return NotFound(new { success = false, message = "그 코드의 고객사가 없습니다" });

        var outboundId = Guid.NewGuid().ToString();
        await db.ExecuteAsync(@"
            -- 🔴 [4] V-6 봉합 — `created_at` 명시(UTC). DEFAULT 에 맡기면 세션 시간대(KST)로 들어가
            --   같은 표의 `canceled_at`·`delivered_at`(코드가 UTC 로 쓴다)보다 **9시간 뒤**가 된다 ⇒
            --   넣고 바로 취소하면 「취소가 작성보다 이르다」가 화면에 그대로 보였다.
            INSERT INTO bo_cs_outbound
                (outbound_id, tenant_id, purpose, title, body, created_by, created_at)
            VALUES (@Id, @TenantId, @Purpose, @Title, @Body, @AdminId, UTC_TIMESTAMP(6))",
            new { Id = outboundId, TenantId = tenantId, Purpose = purpose, Title = title, Body = body, AdminId = adminId });

        _logger.LogInformation("[CS어드민] 선발신 쪽지 큐 적재 — outbound={Id} purpose={P}", outboundId, purpose);

        // 🔴 「보냈습니다」라고 말하지 않는다. 전달은 ERP 통로가 열린 뒤다.
        return Ok(new
        {
            success = true,
            outboundId,
            delivered = false,
            message = "전달 대기함에 넣었습니다 — ERP 수신 통로가 열리면 자동으로 나갑니다",
        });
    }

    /// <summary>C-3 — 선발신 쪽지 목록 + 전달 상태.</summary>
    [HttpGet("outbound")]
    public async Task<IActionResult> ListOutbound([FromQuery] string? state, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        // 🔴 작4 A-1 — 표가 없으면 빈 목록. 목록 화면이 500 으로 죽지 않는다.
        if (!await HasOutboundSchemaAsync(db))
            return Ok(new { success = true, items = Array.Empty<object>(), schemaPending = true });
        var where = state switch
        {
            "pending" => "AND o.delivered_at IS NULL AND o.canceled_at IS NULL",
            "delivered" => "AND o.delivered_at IS NOT NULL",
            "canceled" => "AND o.canceled_at IS NOT NULL",
            _ => "",
        };

        var rows = await db.QueryAsync($@"
            SELECT o.outbound_id   AS outboundId,
                   t.company_name  AS companyName,
                   t.tenant_code   AS tenantCode,
                   o.purpose       AS purpose,
                   o.title         AS title,
                   o.body          AS body,
                   o.created_at    AS createdAt,
                   o.delivered_at  AS deliveredAt,
                   o.canceled_at   AS canceledAt,
                   -- 🔴 [4] V-4 봉합 — 선발신도 폐기 축 안에 있다. 화면이 「폐기됨」을 보여준다.
                   o.purged_at     AS purgedAt,
                   o.purge_reason  AS purgeReason
              FROM bo_cs_outbound o
              -- 🔴 CAST 를 쓰지 않는다(2026-10-11 게이트 G-W2-2 실측).
              --   `CAST(t.tenant_id AS CHAR)` 는 결과를 연결 기본 콜레이션(utf8mb4_general_ci)으로
              --   내놓고, `o.tenant_id` 는 표 콜레이션(utf8mb4_unicode_ci)이다 ⇒ `=` 가
              --   Illegal mix of collations 로 **1267 을 던져 목록 조회가 500** 이 됐다.
              --   두 칸 모두 varchar(36) 같은 모양이라 CAST 자체가 애초에 필요 없었다.
              --   같은 파일 :124·:160 이 쓰는 모양이 정본이다.
              LEFT JOIN tenants t ON t.tenant_id = o.tenant_id
             WHERE 1 = 1 {where}
             ORDER BY o.created_at DESC
             LIMIT 200");

        return Ok(new { success = true, items = rows });
    }

    /// <summary>C-3 — 아직 안 나간 선발신 쪽지를 취소한다(임의 삭제의 발신측).</summary>
    [HttpPost("outbound/{outboundId}/cancel")]
    public async Task<IActionResult> CancelOutbound(string outboundId, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(adminId)) return Forbid();

        await using var db = await OpenAsync(ct);
        // 🔴 작4 A-1 — 45번 미적용 서버에서 500 대신 사람이 읽을 답(선행검증서 §2-1).
        if (!await HasOutboundSchemaAsync(db)) return SchemaPendingResult();
        // 이미 나간 것은 취소할 수 없다 — 상대가 이미 받았다.
        var rows = await db.ExecuteAsync(@"
            UPDATE bo_cs_outbound
               SET canceled_at = UTC_TIMESTAMP(6), canceled_by = @AdminId
             WHERE outbound_id = @Id AND delivered_at IS NULL AND canceled_at IS NULL",
            new { Id = outboundId, AdminId = adminId });

        if (rows == 0)
            return BadRequest(new { success = false, message = "이미 전달됐거나 취소된 쪽지입니다" });

        _logger.LogInformation("[CS어드민] 선발신 취소 — outbound={Id}", outboundId);
        return Ok(new { success = true });
    }

    /// <summary>C-7 — 보관기한 조회. <c>retainDays = null</c> 이면 무기한(기본값).</summary>
    [HttpGet("retention")]
    public async Task<IActionResult> GetRetention(CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        // 🔴 작4 A-1 — 표가 없으면 **기본값 무기한**으로 화면을 세운다. 500 이 아니다 ⇒
        //   「쪽지보내기」 창이 열리기는 하고, 보관기한 칸만 「설정 불가」로 정직하게 멈춘다.
        if (!await HasOutboundSchemaAsync(db))
            return Ok(new { success = true, policy = (object?)null, schemaPending = true });
        var row = await db.QueryFirstOrDefaultAsync(@"
            SELECT retain_days AS retainDays, updated_by AS updatedBy, updated_at AS updatedAt
              FROM bo_cs_retention_policy WHERE policy_id = 1");
        return Ok(new { success = true, policy = row });
    }

    /// <summary>
    /// C-7 — 보관기한 설정. 🔴 <b>이 값이 아무것도 지우지 않는다.</b>
    /// 기한이 지난 쪽지는 <c>purge-candidates</c> 에서 **후보로만** 보이고, 지우는 것은 사람이 확정한다.
    /// </summary>
    [HttpPost("retention")]
    public async Task<IActionResult> SetRetention([FromBody] RetentionRequest req, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(adminId)) return Forbid();

        var days = req?.RetainDays;
        if (days is <= 0 or > 3650)
            return BadRequest(new { success = false, message = "보관기한은 1~3650일 사이로 적어 주세요(비우면 무기한)" });

        await using var db = await OpenAsync(ct);
        // 🔴 작4 A-1 — 45번 미적용 서버에서 500 대신 사람이 읽을 답(선행검증서 §2-1).
        if (!await HasOutboundSchemaAsync(db)) return SchemaPendingResult();
        await db.ExecuteAsync(@"
            INSERT INTO bo_cs_retention_policy (policy_id, retain_days, updated_by, updated_at)
            VALUES (1, @Days, @AdminId, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE retain_days = @Days, updated_by = @AdminId, updated_at = UTC_TIMESTAMP(6)",
            new { Days = days, AdminId = adminId });

        _logger.LogInformation("[CS어드민] 보관기한 설정 — {Days}", days is null ? "무기한" : $"{days}일");
        return Ok(new { success = true, retainDays = days, autoDelete = false });
    }

    /// <summary>
    /// C-8 — 폐기 후보. 보관기한이 지난 **아직 안 지운** 쪽지만 센다.
    /// <para>🔴 이 입구는 **읽기만** 한다. 자동 삭제는 어디에도 없다(C-9).</para>
    /// </summary>
    [HttpGet("purge-candidates")]
    public async Task<IActionResult> PurgeCandidates(CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        // 🔴 작4 A-1 — 표가 없으면 폐기 후보 0건. 자동 삭제는 어디에도 없다(C-9) — 그대로 유지.
        if (!await HasOutboundSchemaAsync(db))
            return Ok(new { success = true, retainDays = (int?)null, candidates = 0, schemaPending = true });
        var days = await db.ExecuteScalarAsync<int?>(
            "SELECT retain_days FROM bo_cs_retention_policy WHERE policy_id = 1");
        if (days is null)
        {
            // 무기한 = 후보 없음. 설정하지 않은 상태에서 목록이 뜨면 사람이 오해한다.
            return Ok(new { success = true, retainDays = (int?)null, items = Array.Empty<object>(), autoDelete = false });
        }

        // 🔴 [4] V-4 봉합 — 「주고받은 쪽지」는 표가 **셋**이다:
        //     받은 것 = `bo_cs_tickets` · 답 = `bo_cs_replies`(티켓을 따라 비워진다) ·
        //     본사 선발신 = `bo_cs_outbound`.
        //   종전엔 **첫 표만** 셌다 ⇒ 이미 전달·취소된 선발신 쪽지는 보관기한으로도,
        //   임의 폐기로도 지울 길이 **0건**이었다(사장님 오더가 「주고받은」을 못 박았다).
        var outboundRows = await db.QueryAsync(@"
            SELECT outbound_id AS outboundId, tenant_id AS tenantId, purpose, title,
                   created_at AS createdAt
              FROM bo_cs_outbound
             WHERE purged_at IS NULL
               AND created_at < DATE_SUB(UTC_TIMESTAMP(6), INTERVAL @Days DAY)
             ORDER BY created_at
             LIMIT 200",
            new { Days = days });

        var rows = await db.QueryAsync(@"
            -- 🔴 `bo_cs_tickets` 의 접수시각 칸 이름은 `received_at` 이다 — `created_at` 은 없다
            --   (40번 DDL `:37`). 게이트 G-W2-7 이 실측으로 잡았다: 이 입구는 호출하는 순간
            --   Unknown column 'created_at' 로 **500** 이었다(#13 DESCRIBE 의무를 내가 건너뛴 자리).
            --   화면(`PurgeCandidateRow.CreatedAt`)이 쓰는 이름을 지키려고 **별칭만 createdAt** 으로 둔다.
            SELECT id, tenant_id AS tenantId, category, status, received_at AS createdAt
              FROM bo_cs_tickets
             WHERE purged_at IS NULL
               AND received_at < DATE_SUB(UTC_TIMESTAMP(6), INTERVAL @Days DAY)
             ORDER BY received_at
             LIMIT 200",
            new { Days = days });

        return Ok(new
        {
            success = true,
            retainDays = days,
            items = rows,
            outboundItems = outboundRows,   // [4] V-4 — 선발신 쪽지 후보
            autoDelete = false,
        });
    }

    /// <summary>
    /// C-11·C-12 — 쪽지 폐기. **몸통은 지우고, 지웠다는 사실은 남긴다.**
    /// <para>🔴 행을 통째로 지우지 않는 이유: <c>bo_cs_ticket_logs</c>·<c>bo_cs_replies</c> 가 그 티켓을 가리키고,
    /// 「언제 누가 무엇을 지웠나」가 사라지면 고객 분쟁에서 우리 쪽 증거가 없다.</para>
    /// </summary>
    [HttpPost("{id:long}/purge")]
    public async Task<IActionResult> PurgeTicket(long id, [FromBody] PurgeRequest req, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(adminId)) return Forbid();

        var reason = req?.Reason?.Trim() ?? "";
        if (reason.Length is 0 or > 200)
            return BadRequest(new { success = false, message = "폐기 사유를 적어 주세요(1~200자)" });

        await using var db = await OpenAsync(ct);

        // 🔴 [4] V-5 봉합 — **거래로 묶는다.**
        //   종전엔 ① 본문 비우기 ② 감사기록 INSERT 가 **별건**이었다. ②가 실패하면
        //   「본문은 이미 지워졌고 누가 왜 지웠는지는 없다」 — 설계가 선언한
        //   「지웠다는 사실은 안 지운다」가 바로 그 자리에서 깨진다.
        //   ⇒ 셋(티켓 본문 · 답장 본문 · 감사기록)이 **다 되거나 다 안 된다.**
        await using var tx = await db.BeginTransactionAsync(ct);

        // 이미 폐기된 것은 두 번 지우지 않는다(멱등 · 사유가 덮이면 첫 기록이 사라진다).
        var rows = await db.ExecuteAsync(@"
            UPDATE bo_cs_tickets
               SET body = NULL,
                   -- 🔴 20261013작2 [3-V] V-6 봉합 — 고객이 평가에 **쓴 말**도 같이 비운다.
                   --   점수(`rating`)·시각(`rated_at`)은 사실이라 남긴다. 몸통만 비운다(작1 과 같은 선).
                   --   읽는 쪽(Detail)에서도 한 번 더 가린다 — **두 겹**이다(옛 폐기건에는 값이 남아 있다).
                   rating_comment = NULL,
                   purged_at = UTC_TIMESTAMP(6),
                   purged_by = @AdminId,
                   purge_reason = @Reason
             WHERE id = @Id AND purged_at IS NULL",
            new { Id = id, AdminId = adminId, Reason = reason }, tx);

        if (rows == 0)
        {
            await tx.RollbackAsync(ct);
            return BadRequest(new { success = false, message = "없는 쪽지이거나 이미 폐기된 쪽지입니다" });
        }

        // 🔴 [4] V-3 봉합 — **답장 본문도 비운다.** 대화의 절반만 비우면 「폐기」가 아니다.
        //   `bo_cs_replies.body` 는 `varchar(4000) NOT NULL` 이라 `NULL` 을 넣을 수 없다
        //   (40번 DDL 은 출하된 파일이라 고치지 않는다 — #1·#36) ⇒ **빈 문자열**로 비운다.
        //   행·`approved_by`·`approved_at`·`delivered_at` 은 **남긴다**(누가 언제 답했나는 사실이다).
        var repliesCleared = await db.ExecuteAsync(
            "UPDATE bo_cs_replies SET body = '' WHERE ticket_id = @Id AND body <> ''",
            new { Id = id }, tx);

        // 🔴 지웠다는 사실은 안 지운다 — 감사기록 1줄(INSERT ONLY · 같은 거래 안).
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email") ?? "unknown";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role") ?? "platform_admin";
        await db.ExecuteAsync(@"
            INSERT INTO bo_audit_log (actor_user_id, actor_email, actor_role, action, target_type, target_id, detail_json)
            VALUES (@AdminId, @Email, @Role, 'cs.purge', 'bo_cs_tickets', @Target, @Detail)",
            new
            {
                AdminId = adminId, Email = email, Role = role,
                Target = id.ToString(),
                Detail = $"{{\"reason\":{System.Text.Json.JsonSerializer.Serialize(reason)},\"repliesCleared\":{repliesCleared}}}",
            }, tx);

        await tx.CommitAsync(ct);

        _logger.LogInformation("[CS어드민] 쪽지 폐기 — ticket={Id} 답장 {N}건 함께 비움 by={By}",
            id, repliesCleared, adminId);
        return Ok(new { success = true, repliesCleared });
    }

    /// <summary>
    /// 🔴 [4] V-4 봉합 — **선발신 쪽지 폐기.** 사장님 오더는 「주고받은 쪽지 … 폐기기한설정 혹은
    /// 임의 삭제」다. 취소(<c>cancel</c>)는 **나가기 전에만** 듣기 때문에, 이미 전달·취소된
    /// 선발신 쪽지를 지울 길이 없었다. 티켓 쪽과 **같은 규칙**이다 — 본문만 비우고 사실은 남긴다.
    /// </summary>
    [HttpPost("outbound/{outboundId}/purge")]
    public async Task<IActionResult> PurgeOutbound(string outboundId, [FromBody] PurgeRequest req, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(adminId)) return Forbid();

        var reason = req?.Reason?.Trim() ?? "";
        if (reason.Length is 0 or > 200)
            return BadRequest(new { success = false, message = "폐기 사유를 적어 주세요(1~200자)" });

        await using var db = await OpenAsync(ct);
        // 🔴 작4 A-1 — 45번 미적용 서버에서 500 대신 사람이 읽을 답(선행검증서 §2-1).
        if (!await HasOutboundSchemaAsync(db)) return SchemaPendingResult();
        await using var tx = await db.BeginTransactionAsync(ct);

        var rows = await db.ExecuteAsync(@"
            UPDATE bo_cs_outbound
               SET body = NULL,
                   purged_at = UTC_TIMESTAMP(6),
                   purged_by = @AdminId,
                   purge_reason = @Reason
             WHERE outbound_id = @Id AND purged_at IS NULL",
            new { Id = outboundId, AdminId = adminId, Reason = reason }, tx);

        if (rows == 0)
        {
            await tx.RollbackAsync(ct);
            return BadRequest(new { success = false, message = "없는 쪽지이거나 이미 폐기된 쪽지입니다" });
        }

        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email") ?? "unknown";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role") ?? "platform_admin";
        await db.ExecuteAsync(@"
            INSERT INTO bo_audit_log (actor_user_id, actor_email, actor_role, action, target_type, target_id, detail_json)
            VALUES (@AdminId, @Email, @Role, 'cs.purge', 'bo_cs_outbound', @Target, @Detail)",
            new
            {
                AdminId = adminId, Email = email, Role = role,
                Target = outboundId,
                Detail = $"{{\"reason\":{System.Text.Json.JsonSerializer.Serialize(reason)}}}",
            }, tx);

        await tx.CommitAsync(ct);

        _logger.LogInformation("[CS어드민] 선발신 폐기 — outbound={Id} by={By}", outboundId, adminId);
        return Ok(new { success = true });
    }

    /// <summary>선발신 용도 — 사장님 오더의 세 가지 + 기타. 목록 밖은 거부(자유입력 금지).
    /// <para>🔴 20261013작1 S-3 — 사장님 낱말 4종에 맞춰 <c>update</c>(업데이트공지사항)·
    /// <c>notice</c>(히트판 공지사항)를 <b>더했다</b>. 옛 값 <c>event</c>·<c>billing</c> 은
    /// <b>하나도 지우지 않는다</b>(#1) — 이미 그 값으로 들어간 줄이 있고, 지우면 그 줄을 다시
    /// 저장·재전송할 길이 막힌다. 새 화면의 쓰기 콤보는 4종만 보여 주고, 옛 값은 조회에서만 보인다
    /// (설계 §5-1). 게이트 G-W7-4 가 이 자리를 지킨다.</para></summary>
    private static readonly HashSet<string> AllowedOutboundPurpose = new(StringComparer.Ordinal)
    {
        "promo", "event", "billing", "etc",
        "update", "notice",
    };

    public class TenantLookupRow
    {
        public string TenantCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
    }

    public class OutboundRequest
    {
        public string? TenantCode { get; set; }
        public string? Purpose { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }

        /// <summary>🔴 20261013작1 보강 B-1 — <c>true</c> 면 <b>지금 쓰고 있는 고객사 전부</b>
        /// (<c>status ∈ {active, trial}</c>)에게 각각 한 건씩 넣는다(사장님 결재 2026-10-13).
        /// <para>비거나 <c>false</c> 면 종전 그대로 <see cref="TenantCode"/> 한 곳만 — 이 칸을 모르는
        /// 종전 호출자가 그대로 돌아야 한다(#1).</para></summary>
        public bool? All { get; set; }
    }

    public class RetentionRequest { public int? RetainDays { get; set; } }
    public class PurgeRequest { public string? Reason { get; set; } }

    private sealed class TicketKey { public string TenantId { get; set; } = ""; public string Uid { get; set; } = ""; }
    public class ReplyRequest { public string? Body { get; set; } }
    public class StatusRequest { public string? To { get; set; } }

    /// <summary>
    /// 🔴 20261013작2 — 상태를 바꾸기 전에 **규칙을 가르는 데 필요한 세 칸**만 읽는 모양.
    /// (들어온 길에 따라 상태 목록과 응대 유형 규칙이 갈린다 — 설계 §3 S-3·S-4)
    /// </summary>
    public class TicketRuleRow
    {
        public string? Status { get; set; }
        public string? Channel { get; set; }
        public string? HandledVia { get; set; }
    }
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

        /// <summary>🔴 20261013작1 S-1 — 접수경로(<c>phone</c>·<c>email</c>·<c>sns</c>·<c>etc</c>).
        /// <para>비거나 null 이면 <c>phone</c> 으로 떨어진다 — 이 칸을 모르는 종전 호출자가 그대로 돌아야 한다(#1).
        /// 목록 밖 값은 <see cref="AllowedIntakeChannel"/> 가 400 으로 거부한다(자유입력 금지).</para></summary>
        public string? Channel { get; set; }
    }

    // ══════════════════════════════════════════════════════════════
    // 🔴 [4] V-2 봉합 — 45번이 **아직 안 들어간 서버**에서도 종전 화면이 살아 있어야 한다
    //
    // 사고 모양: 목록·상세가 `purged_at` 을 **조건 없이** SELECT 하면, 45번이 적용되지 않은
    //   DB 에서 `Unknown column` → **500**. 그건 신규 기능 실패가 아니라 **되던 화면의 회귀**다.
    //   백오피스 스키마 적용기는 `HITPAN_BO_AUTO_MIGRATE` 를 **켤 때만** 돈다(#29) ⇒
    //   「코드는 갔는데 칸은 없는」 구간이 실제로 존재한다. 10/11 운영 사고가 정확히 그 모양이었다.
    //
    // ⇒ 칸이 있는지 **DB 에 직접 물어보고** 조회문을 고른다. 없으면 폐기 칸을 **안 읽는다**
    //   (폐기 기능만 조용히 비어 보이고, 종전 기능은 그대로 돈다).
    // 🔴 캐시는 **DB 이름별**이다. 게이트가 시험 DB 를 여러 개 돌리고, 45번을 일부러 뺀 대조군도
    //   돌린다 — 하나로 캐시하면 앞 측정이 뒤를 오염시킨다(9/25 권한 축 교훈과 같은 모양).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _purgeColumnsByDb = new();

    private static async Task<bool> HasPurgeColumnsAsync(MySqlConnection db)
    {
        var key = db.Database ?? "";
        if (_purgeColumnsByDb.TryGetValue(key, out var cached)) return cached;

        var found = await db.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM information_schema.columns
             WHERE table_schema = DATABASE()
               AND table_name = 'bo_cs_tickets'
               AND column_name IN ('purged_at', 'purged_by', 'purge_reason')");

        var present = found == 3;
        _purgeColumnsByDb[key] = present;
        return present;
    }

    /// <summary>시험이 같은 프로세스에서 DB 를 바꿔 쓸 때 판정을 다시 묻게 한다.</summary>
    internal static void ResetPurgeColumnCache() => _purgeColumnsByDb.Clear();

    // ══════════════════════════════════════════════════════════════
    // 🔴 20261012작4 A-1 — **표**에도 같은 가드를 세운다 (사장님 실측 반려 ①②③)
    //
    // 10/11 작2 [4] V-2 는 45번이 더한 **칸**(`bo_cs_tickets.purged_*`)에만 가드를 걸었다.
    //   같은 파일이 더한 **표 2개**(`bo_cs_outbound`·`bo_cs_retention_policy`)는 조건 없이 읽혔다 ⇒
    //   45번이 안 들어간 서버에서 아래 셋이 전부 500 이었다(10/12 사장님 실측):
    //     ① `GET  retention`     → 「보관기한 조회 실패: 500」
    //     ② `POST outbound`      → 500 + 빈 body ⇒ 화면이 JSON 파싱 예외를 그대로 뱉었다
    //     ③ `POST replies/pull`  → 🔴 **답까지 같이 못 내려갔다**(CsInboundController — 하행 전체가 죽는다)
    //
    // 가드가 하는 일은 「안 터지게」 까지다. **기능은 45번이 적용돼야 돈다** —
    //   둘을 같은 것으로 적지 않는다(선행검증서 §2-3).
    // 🔴 캐시는 칸 가드와 같은 이유로 **DB 이름별**이다(게이트가 45번 뺀 대조군을 함께 돌린다).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _outboundTablesByDb = new();

    /// <summary>45번이 더한 표 2개가 이 DB 에 실재하나. 없으면 선발신·보관기한은 비활성이다.</summary>
    internal static async Task<bool> HasOutboundSchemaAsync(MySqlConnection db)
    {
        var key = db.Database ?? "";
        if (_outboundTablesByDb.TryGetValue(key, out var cached)) return cached;

        var found = await db.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM information_schema.tables
             WHERE table_schema = DATABASE()
               AND table_name IN ('bo_cs_outbound', 'bo_cs_retention_policy')");

        var present = found == 2;
        _outboundTablesByDb[key] = present;
        return present;
    }

    /// <summary>시험이 같은 프로세스에서 DB 를 바꿔 쓸 때 판정을 다시 묻게 한다.</summary>
    internal static void ResetOutboundSchemaCache() => _outboundTablesByDb.Clear();

    /// <summary>
    /// 45번 미적용 서버에서 사람이 읽을 답. 🔴 500 으로 떨어뜨리지 않는 것이 핵심이다 —
    /// 500 은 화면이 해석하지 못하고(② 가 그 모양), 사람에게 아무것도 알려주지 않는다.
    /// </summary>
    private IActionResult SchemaPendingResult() =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            success = false,
            schemaPending = true,
            message = "이 서버에는 쪽지 보관기한·선발신 기능의 자료방이 아직 준비되지 않았습니다. "
                    + "본사 담당자에게 알려 주세요(백오피스 스키마 45번 적용 필요).",
        });


    // ══════════════════════════════════════════════════════════════
    // 🔴 20261012작5 — **「지금 준비하기」** (사장님 실측: 메시지는 뜨는데 동작 안 됨)
    //
    // 작4 가 500 을 사람이 읽을 문장으로 바꿨다. 그런데 사장님이 보신 것은
    //   「자료방이 아직 준비되지 않았습니다」 + **동작 안 됨** 이었다 ⇒ 봉합이 **막다른 길**을 만들었다.
    //   안 터지는 것과 되는 것은 다른 사실이고, 사장님이 원한 건 **되는 것**이다.
    //
    // 왜 기동 자동적용으로 풀지 않았나
    //   `HITPAN_BO_AUTO_MIGRATE` 를 켜거나 기본 on 으로 바꾸는 것은 **사장님 통제권**이다
    //   (#29 · 10/11 작1 작업지시서 §금지에 명시). 그 스위치는 **한 글자도 안 건드린다** —
    //   켜면 그 서버의 **모든** 미적용 스키마가 한 번에 돈다. 그건 CS 한 기능의 결정이 아니다.
    //
    // 그래서 이렇게 한다 — 🔴 **반자동**(사장님 헌법 「100% 자동화 없음」)
    //   · 본사 어드민이 **직접 누른다**(이 컨트롤러는 `PlatformAdmin` 정책 뒤에 있다)
    //   · 적용 범위는 **CS 자료방 파일 하나**(`45_backoffice_cs_outbound_retention.sql`)뿐 —
    //     다른 미적용 스키마는 **건드리지 않는다**. 전체 적용은 여전히 사장님 스위치다.
    //   · 멱등 — 이미 있으면 아무것도 안 바뀐다(`CREATE TABLE IF NOT EXISTS` · `INSERT IGNORE`)
    //   · 기존 자료를 바꾸는 DML **0건**(45번 실독 — 선행검증서 §2-3 보강)
    //   · MySQL 8 보정 경로를 **그대로 탄다**(운영은 MySQL 8 · CI `bo-mysql8-gate` 가 문다)
    //   · 전후를 **직접 세어** 돌려준다 — 「했다」가 아니라 「생겼다」를 말한다
    // ══════════════════════════════════════════════════════════════

    /// <summary>작5 — CS 자료방(45번)만 지금 준비한다. 본사 어드민이 누르는 반자동 입구.</summary>
    [HttpPost("schema/prepare")]
    public async Task<IActionResult> PrepareCsSchema(CancellationToken ct)
    {
        var adminId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(adminId)) return Forbid();

        var scriptsDir = Environment.GetEnvironmentVariable("HITPAN_BO_SCHEMA_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "installer", "backoffice");
        var target = "45_backoffice_cs_outbound_retention.sql";

        if (!System.IO.File.Exists(Path.Combine(scriptsDir, target)))
        {
            // 파일이 없으면 준비할 수 없다 — 추측하지 않고 그대로 말한다.
            _logger.LogError("[CS어드민] 준비하기 — 스키마 파일 없음 {Dir}/{File}", scriptsDir, target);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "자료방 설계 파일을 이 서버에서 찾지 못했습니다 — 배포가 끝났는지 확인이 필요합니다.",
            });
        }

        var cs = _config.GetConnectionString("BackofficeDb")
                 ?? _config.GetConnectionString("Default")
                 ?? throw new InvalidOperationException("ConnectionStrings:BackofficeDb 미설정");

        int before, after;
        await using (var db = await OpenAsync(ct)) before = await CountCsTablesAsync(db);

        try
        {
            var migLogger = _loggerFactory.CreateLogger<SchemaMigrator>();
            await new SchemaMigrator(cs, scriptsDir, migLogger)
                .ApplyAsync(ct, f => string.Equals(f, target, StringComparison.Ordinal));
        }
        catch (Exception ex)
        {
            // 빈 catch 금지(#15). 실패는 숨기지 않는다 — 숨기면 다음 사람이 「됐다」로 읽는다.
            _logger.LogError(ex, "[CS어드민] 준비하기 실패 — 적용기 예외");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "자료방을 준비하지 못했습니다. 본사 담당자에게 알려 주세요(적용 기록에 사유가 남았습니다).",
            });
        }

        ResetOutboundSchemaCache();
        ResetPurgeColumnCache();
        await using (var db = await OpenAsync(ct)) after = await CountCsTablesAsync(db);

        var ready = after == 2;
        _logger.LogInformation(
            "[CS어드민] 준비하기 — 표 {Before}→{After}건 · 결과 {R} · 누른 사람 있음",
            before, after, ready ? "준비됨" : "미완");

        return Ok(new
        {
            success = ready,
            ready,
            tablesBefore = before,
            tablesAfter = after,
            message = ready
                ? "자료방을 준비했습니다 — 이제 보관기한 설정과 쪽지 보내기가 됩니다."
                : "준비를 돌렸으나 자료방이 아직 없습니다 — 본사 담당자에게 알려 주세요.",
        });
    }

    /// <summary>CS 자료방 표가 몇 개 있나(0~2). 「했다」 대신 「생겼다」를 세는 눈.</summary>
    private static async Task<int> CountCsTablesAsync(MySqlConnection db) =>
        await db.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM information_schema.tables
             WHERE table_schema = DATABASE()
               AND table_name IN ('bo_cs_outbound', 'bo_cs_retention_policy')");
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
