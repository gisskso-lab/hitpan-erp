using System.Data;
using System.Text.Json;
using Dapper;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.API.Controllers;

// 작14 묶음 B-1·B-4 — CS 쪽지 ERP API (사장님 결재 2026-10-08)
//
// ■ 역할은 두 가지뿐 (교정②) — 쪽지를 보내는 것과 받은 답을 보는 것.
//   저장·분석·AI 는 전부 백오피스 몫. 여기는 로컬 저장 + 큐 적재 + 조회만.
//
// ■ 전송 흐름 (교정③ 부모계정 파이프라인)
//   자식계정이 쓴다 → 로컬 INSERT(created_by = 자식계정 · 로컬 전용) → cs_outbox 큐
//   → CsOutboxSenderWorker 가 부모계정(테넌트) 신원으로 송신. payload 에 자식계정 식별자 없음(#22).
//   🚫 자식계정이 백오피스로 직접 쏘는 길은 설계 금지.
//
// ■ 문① — 저장 **전** 금지필드 검사 (B-4 · CTO 조건③)
//   걸리면 400 + 「이 부분을 지워 주세요」(거부가 아니라 고칠 기회) + cs_forbidden_rejects 기록.
//   🔴 막힌 값 자체는 저장·로깅 0 — 규칙코드·시각만.
//
// ■ #2 — tenant_id 는 JWT(미들웨어 Items)에서만. 파라미터 수신 즉시 반려 대상.
//   CS 는 로그인 뒤 기능 — 익명 경로 불요(TenantMiddleware 화이트리스트 3회 재발 P0 자리 · 접근 금지).
[ApiController]
[Route("api/cs")]
[Authorize]
public class CsRequestController : ControllerBase
{
    private readonly IDbConnection _db;
    private readonly ILogger<CsRequestController> _logger;

    // 유형(7모양) 고정 목록 — 고객이 제 말로 고르는 축(설계 §6-ⓐ). 화이트리스트 밖 = 거부.
    //   8종 정본(사용·설정·통신·데이터·업데이트·기능오류·설치·기타)은 백오피스 분류 축(결-9 · C-3)이고
    //   ERP 화면은 고객 언어인 7모양을 받는다(설계 §5-ⓑ 보내기 화면).
    private static readonly Dictionary<string, string> Categories = new()
    {
        ["open_fail"] = "안 열려요",
        ["number_mismatch"] = "숫자가 안 맞아요",
        ["how_to"] = "어떻게 하는지 모르겠어요",
        ["print_fail"] = "안 찍혀요(인쇄·발행)",
        ["change_request"] = "바꿔 주세요",
        ["billing"] = "요금·결제 문의",
        ["feature_request"] = "되게 해주세요(기능 요청)",
    };

    // 세부태그 고정 목록 — 업무 영역 축. 1차 최소 목록이며 조정은 CS팀장 몫(반자동 — 거부 집계를 보고).
    //   화면 콤보도 이 목록을 그대로 받아 그린다(GET 응답에 동봉) — 목록의 주인은 이 파일 하나다.
    private static readonly Dictionary<string, string> SubTags = new()
    {
        ["estimate_sales"] = "견적·수주·판매",
        ["purchase_order"] = "발주·매입",
        ["stock"] = "재고",
        ["invoice"] = "거래명세서·세금계산서",
        ["accounting"] = "회계·경비",
        ["hr_payroll"] = "사원·급여",
        ["settings_perm"] = "설정·권한",
        ["etc"] = "기타",
    };

    public CsRequestController(IDbConnection db, ILogger<CsRequestController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // 쪽지 보내기 — 로컬 INSERT + 큐 적재 (한 트랜잭션 · 게이트 ③ 「접수됐습니다」는 INSERT 뒤에만)
    // ─────────────────────────────────────────────────────────────
    [HttpPost("requests")]
    public async Task<IActionResult> Create([FromBody] CreateCsRequest req, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) return Forbid();

        if (req is null || string.IsNullOrWhiteSpace(req.Category))
            return BadRequest(new { success = false, message = "유형을 선택해 주세요" });

        // 고정 목록 화이트리스트 — 밖의 값은 거부 + DB-142 기록(rule_code = tag_not_allowed)
        if (!Categories.ContainsKey(req.Category) || !SubTags.ContainsKey(req.SubTag ?? ""))
        {
            await RecordRejectAsync(tenantId, "tag_not_allowed", ct);
            return BadRequest(new { success = false, message = "목록에 있는 유형·태그만 보낼 수 있습니다" });
        }

        var body = req.Body?.Trim();
        if (body is { Length: > 2000 })
            return BadRequest(new { success = false, message = "본문은 2000자까지입니다" });

        // 문① — 저장 전 금지필드 검사 (걸리면 저장 0 · 고칠 기회 안내)
        var rule = ForbiddenFieldScanner.Scan(body);
        if (rule is not null)
        {
            await RecordRejectAsync(tenantId, rule, ct);
            return BadRequest(new { success = false, message = ForbiddenFieldScanner.GuideMessage(rule) });
        }

        var requestId = Guid.NewGuid().ToString();   // ERP 발급 — 멱등키의 몸통
        var erpVersion = typeof(CsRequestController).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        // 송신 payload = 화이트리스트 필드만 — created_by(자식계정) 는 싣지 않는다(교정③ · #22)
        var payload = JsonSerializer.Serialize(new
        {
            csRequestId = requestId,
            category = req.Category,
            subTag = req.SubTag,
            body,
            screenCode = Truncate(req.ScreenCode, 40),
            erpVersion,
        });
        if (payload.Length > 4000)
            return BadRequest(new { success = false, message = "본문이 너무 깁니다" });

        if (_db.State != ConnectionState.Open) _db.Open();
        using var tx = _db.BeginTransaction();
        try
        {
            await _db.ExecuteAsync(@"
                INSERT INTO cs_requests
                    (cs_request_id, tenant_id, created_by, category, sub_tag, body, screen_code, erp_version, status)
                VALUES (@Id, @TenantId, @UserId, @Category, @SubTag, @Body, @ScreenCode, @ErpVersion, '접수')",
                new
                {
                    Id = requestId,
                    TenantId = tenantId,
                    UserId = userId,
                    req.Category,
                    req.SubTag,
                    Body = body,
                    ScreenCode = Truncate(req.ScreenCode, 40),
                    ErpVersion = erpVersion,
                }, tx);

            // 🔴 next_attempt_at 을 명시적으로 UTC 로 넣는다 — 표 기본값은 서버 로컬시각이고
            //    워커 폴링은 UTC_TIMESTAMP(6) 비교라, KST 고객 PC 에서 기본값에 맡기면
            //    새 쪽지가 9시간 뒤에야 집힌다(게이트 G-CS-1 이 이 자리를 잰다).
            await _db.ExecuteAsync(@"
                INSERT INTO cs_outbox (tenant_id, cs_request_id, payload_json, next_attempt_at)
                VALUES (@TenantId, @Id, @Payload, UTC_TIMESTAMP(6))",
                new { TenantId = tenantId, Id = requestId, Payload = payload }, tx);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw; // 전역 처리기로 — 화면 「접수됐습니다」는 INSERT 성공 뒤에만(게이트 ③)
        }

        return Ok(new { success = true, csRequestId = requestId });
    }

    // ─────────────────────────────────────────────────────────────
    // 내 쪽지 목록 — 큐 상태를 함께 싣는다 (N-8 짝: 「아직 본사에 안 갔음」이 보여야 한다)
    // ─────────────────────────────────────────────────────────────
    [HttpGet("requests")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var rows = await _db.QueryAsync(@"
            SELECT r.cs_request_id   AS csRequestId,
                   r.category        AS category,
                   r.sub_tag         AS subTag,
                   r.body            AS body,
                   r.status          AS status,
                   r.created_at      AS createdAt,
                   o.sent_at         AS sentAt,
                   o.terminal_reason AS terminalReason,
                   o.attempt_count   AS attemptCount,
                   (SELECT COUNT(*) FROM cs_replies p
                     WHERE p.tenant_id = r.tenant_id AND p.cs_request_id = r.cs_request_id) AS replyCount
              FROM cs_requests r
              LEFT JOIN cs_outbox o ON o.cs_request_id = r.cs_request_id
             WHERE r.tenant_id = @TenantId
             ORDER BY r.created_at DESC
             LIMIT 200",
            new { TenantId = tenantId });

        return Ok(new { success = true, data = rows, categories = Categories, subTags = SubTags });
    }

    // ─────────────────────────────────────────────────────────────
    // 받은 답 — 쪽지 한 건의 답 목록 (결-6 전용 화면의 재료)
    // ─────────────────────────────────────────────────────────────
    [HttpGet("requests/{id}/replies")]
    public async Task<IActionResult> Replies(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        // 작15 S-1 봉합 — 평가 블록 판정에 「쓴 사람」이 필요하다(추가만 · 기존 응답 키 무변).
        var userId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var rows = await _db.QueryAsync(@"
            SELECT reply_id AS replyId, cs_request_id AS csRequestId, body,
                   replied_by_kind AS repliedByKind, replied_at AS repliedAt, read_at AS readAt
              FROM cs_replies
             WHERE tenant_id = @TenantId AND cs_request_id = @Id
             ORDER BY replied_at",
            new { TenantId = tenantId, Id = id });

        // 작15 E-1 — 평가 블록 **동봉**(추가만 · #1). 기존 키 success·data 는 한 글자도 안 바꿨다.
        //   판정은 서버가 한다 — 화면이 제 마음대로 열지 못한다(사장님 결재 Q-1).
        var rating = RatingBlock(await LoadRatingStateAsync(tenantId, id), userId);

        return Ok(new { success = true, data = rows, rating });
    }

    // ─────────────────────────────────────────────────────────────
    // 작15 E-1 — 만족도 평가 받기 (신설 · 사장님 결재 2026-10-08 Q-1·Q-2·Q-3)
    //
    // ■ Q-1 — 평가는 **답이 1건 이상** 올 때만 열린다.
    //   열림 = sent_at IS NOT NULL AND terminal_reason IS NULL AND 답 1건 이상 AND 평가 행 없음.
    //   🔴 화면만 숨기지 않는다 — 같은 판정을 이 POST 가 **서버에서 다시** 한다.
    //   왜 이 판정이 전부인가: 본사 수신구는 티켓이 없어도 200 {duplicated:true} 를 준다
    //   (CsInboundController.cs:231). 전송 전·거부된 쪽지를 평가하면 점수가 **조용히 사라진다**.
    //
    // ■ Q-2 — **수정 불가 · 첫 평가만.** 평가 행이 있으면 열지도 않고 POST 도 받지 않는다(수정·철회 0건).
    // ■ Q-3 — 좋아요 / 보통 / 아쉬워요 = 3 / 2 / 1 (본사 점수 범위 1~3 과 같은 축).
    // ■ #2 — tenant_id 는 HttpContext.Items 에서만. 파라미터 수신 0.
    // ─────────────────────────────────────────────────────────────
    [HttpPost("requests/{id}/rating")]
    public async Task<IActionResult> Rate(string id, [FromBody] RateCsRequest req, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) return Forbid();

        if (req is null)
            return BadRequest(new { success = false, reason = "bad_request", message = "평가를 선택해 주세요" });

        // ① 열림 판정 — 서버가 다시 한다(화면 우회로 들어온 평가가 사라지는 길을 닫는 자리)
        var reason = RatingReason(await LoadRatingStateAsync(tenantId, id), userId);
        if (reason == ReasonAlreadyRated)
            return Ok(new { success = true, alreadyRated = true, reason });   // Q-2 — 첫 평가가 정본
        if (reason != ReasonOpen)
            return BadRequest(new { success = false, reason, message = ClosedMessage(reason) });

        // ② 점수 범위 — 본사 계약과 같은 1~3. 여기서 걸러 본사 400 을 만들지 않는다.
        if (req.Rating is < 1 or > 3)
            return BadRequest(new { success = false, reason = "bad_rating", message = "평가를 선택해 주세요" });

        // ③ 한 줄 평 — 상한 500(본사 저장 상한과 같은 값 ⇒ 보낸 글이 잘려 들어가지 않는다)
        var comment = req.Comment?.Trim();
        if (comment is { Length: > 500 })
            return BadRequest(new { success = false, reason = "comment_too_long", message = "한 줄로 적어 주세요" });
        if (comment?.Length == 0) comment = null;

        // ④ 문① — 보내기 전 금지필드 검사. 본사는 금지필드 글을 조용히 버리고 점수만 저장하므로
        //    ERP 가 먼저 **고칠 기회**를 줘야 고객이 쓴 글이 사라지지 않는다.
        var rule = ForbiddenFieldScanner.Scan(comment);
        if (rule is not null)
        {
            await RecordRejectAsync(tenantId, rule, ct);
            return BadRequest(new { success = false, reason = "forbidden_field", message = ForbiddenFieldScanner.GuideMessage(rule) });
        }

        // ⑤ 로컬에 먼저 적는다 — 터널·본사가 내려가 있어도 평가가 사라지지 않는다(유실 0).
        //    🔴 next_attempt_at 을 UTC 로 명시한다 — 표 기본값은 서버 로컬시각이고 워커 폴링은
        //    UTC_TIMESTAMP(6) 비교라, KST 고객 PC 에서 기본값에 맡기면 9시간 뒤에야 집힌다
        //    (Create 의 cs_outbox INSERT 와 같은 함정 · 게이트 G-E1-2 가 이 자리를 잰다).
        try
        {
            await _db.ExecuteAsync(@"
                INSERT INTO cs_rating_outbox (tenant_id, cs_request_id, `rating`, `comment`, next_attempt_at)
                VALUES (@TenantId, @Id, @Rating, @Comment, UTC_TIMESTAMP(6))",
                new { TenantId = tenantId, Id = id, req.Rating, Comment = comment });
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            // uq_cs_rating_req — 두 번 눌러도 사고가 아니다(멱등). 첫 평가가 그대로 정본이다.
            _logger.LogInformation(ex, "[CS평가] 같은 쪽지 재평가 시도 — 첫 평가 유지(멱등)");
            return Ok(new { success = true, alreadyRated = true, reason = ReasonAlreadyRated });
        }

        return Ok(new { success = true });
    }

    // 작15 E-1 — 열림 판정의 재료를 **한 번의 조회**로 모은다(#16 — 한 요청 = 한 연결 · Task.WhenAll 금지).
    //   🔴 TINYINT/불리언을 dynamic 으로 받으면 캐스팅 500 이 난다(선례) ⇒ 형을 적은 DTO 로 받는다.
    private async Task<RatingStateRow?> LoadRatingStateAsync(string tenantId, string csRequestId)
        => await _db.QueryFirstOrDefaultAsync<RatingStateRow>(@"
            SELECT r.created_by      AS CreatedBy,
                   o.sent_at         AS SentAt,
                   o.terminal_reason AS TerminalReason,
                   (SELECT COUNT(*) FROM cs_replies p
                     WHERE p.tenant_id = r.tenant_id AND p.cs_request_id = r.cs_request_id) AS ReplyCount,
                   g.`rating`        AS MyRating,
                   g.`comment`       AS MyComment,
                   g.sent_at         AS RatingSentAt,
                   g.terminal_reason AS RatingTerminalReason
              FROM cs_requests r
              LEFT JOIN cs_outbox o        ON o.cs_request_id = r.cs_request_id AND o.tenant_id = r.tenant_id
              LEFT JOIN cs_rating_outbox g ON g.cs_request_id = r.cs_request_id AND g.tenant_id = r.tenant_id
             WHERE r.tenant_id = @TenantId AND r.cs_request_id = @Id",
            new { TenantId = tenantId, Id = csRequestId });

    private const string ReasonOpen = "open";
    private const string ReasonAlreadyRated = "already_rated";
    // 작15 S-1 봉합 — 「내 테넌트의 쪽지」와 「내가 쓴 쪽지」는 다르다. 목록은 회사 전체를 보여준다.
    private const string ReasonNotMine = "not_mine";

    // §4 표 그대로 — 이 함수 하나가 「조용히 사라지는 평가」를 막는 전부다.
    //   순서가 뜻이다: 이미 평가한 건이 가장 먼저(Q-2 첫 평가만) → 종결 → 미전송 → 답 0건 → 열림.
    private static string RatingReason(RatingStateRow? s, string? userId)
    {
        if (s is null) return "not_sent";                       // 내 테넌트에 그 쪽지가 없다
        // 🔴 S-1 — 이 검사가 가장 먼저다. 남이 먼저 누르면 UNIQUE 때문에 쓴 사람이 영구히 못 하고,
        //    rated_by 칸이 없어 누가 눌렀는지도 모르며, 행 삭제 금지라 되돌릴 수단이 0 이다.
        if (string.IsNullOrEmpty(userId) || !string.Equals(s.CreatedBy, userId, StringComparison.Ordinal))
            return ReasonNotMine;
        if (s.MyRating is not null) return ReasonAlreadyRated;  // Q-2 — 수정 불가
        if (s.TerminalReason == "rejected") return "rejected";
        if (s.TerminalReason == "failed") return "failed";
        if (s.SentAt is null) return "not_sent";                // 본사에 티켓이 아직 없다
        if (s.ReplyCount < 1) return "no_reply";                // Q-1 — 답이 와야 열린다
        return ReasonOpen;
    }

    // 고객에게 보이는 닫힘 사유 — 🚫 개발 용어 0건(#23)
    private static string ClosedMessage(string reason) => reason switch
    {
        "not_sent" => "아직 본사에 전달되지 않았습니다. 전달된 뒤에 평가할 수 있습니다.",
        "rejected" => "보낼 수 없는 문의입니다 — 내용을 확인해 주세요.",
        "failed" => "전달이 지연되고 있습니다. 본사가 확인 중입니다.",
        "no_reply" => "답이 오면 평가할 수 있습니다.",
        "not_mine" => "문의를 쓴 분만 평가할 수 있습니다.",
        _ => "지금은 평가할 수 없습니다.",
    };

    // 화면이 그대로 그릴 수 있는 블록 — 판정의 주인은 서버 하나다.
    private static object RatingBlock(RatingStateRow? s, string? userId)
    {
        var reason = RatingReason(s, userId);
        // 🔴 S-1 — 남의 쪽지면 그 사람의 점수·글을 내보내지 않는다(본 적 없는 사람에게 보일 길을 막는다).
        var mine = reason != ReasonNotMine;
        return new
        {
            canRate = reason == ReasonOpen,
            reason,
            myRating = mine ? s?.MyRating : null,
            myComment = mine ? s?.MyComment : null,
            deliveryState = !mine || s?.MyRating is null ? null : DeliveryState(s!),
        };
    }

    // 내 평가가 본사까지 갔나 — 「고쳤다 ≠ 갔다」 자리라 고객에게 그대로 보인다(PM 결재 D-3).
    private static string DeliveryState(RatingStateRow s)
        => s.RatingTerminalReason switch
        {
            "rejected" => "rejected",
            "failed" => "failed",
            _ => s.RatingSentAt is null ? "pending" : "sent",
        };

    // N-8 — 「안 읽은 답 N」 전역 표시의 숫자
    [HttpGet("replies/unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var n = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM cs_replies WHERE tenant_id = @TenantId AND read_at IS NULL",
            new { TenantId = tenantId });
        return Ok(new { success = true, count = n });
    }

    // 읽음 표시 — read_at 은 메타 갱신(#3 과 무충돌 · sent_at 선례와 같은 성격)
    [HttpPost("replies/{id}/read")]
    public async Task<IActionResult> MarkRead(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        await _db.ExecuteAsync(@"
            UPDATE cs_replies SET read_at = UTC_TIMESTAMP(6)
             WHERE tenant_id = @TenantId AND reply_id = @Id AND read_at IS NULL",
            new { TenantId = tenantId, Id = id });
        return Ok(new { success = true });
    }

    // DB-142 — 규칙코드·시각만 적는다. 막힌 값은 어디에도 없다(CTO 조건③).
    private async Task RecordRejectAsync(string tenantId, string ruleCode, CancellationToken ct)
    {
        try
        {
            await _db.ExecuteAsync(
                "INSERT INTO cs_forbidden_rejects (tenant_id, rule_code) VALUES (@TenantId, @RuleCode)",
                new { TenantId = tenantId, RuleCode = ruleCode });
        }
        catch (Exception ex)
        {
            // #15 — 거부 기록 실패가 거부 자체를 막으면 안 된다. 기록만 경고로 남긴다.
            _logger.LogWarning(ex, "[CS] 금지필드 거부 기록 실패 rule={Rule}", ruleCode);
        }
    }

    private static string? Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);

    public class CreateCsRequest
    {
        public string Category { get; set; } = "";
        public string? SubTag { get; set; }
        public string? Body { get; set; }
        public string? ScreenCode { get; set; }
    }

    // 작15 E-1 — 평가 요청 몸통. 점수는 금액이 아니라 정수다(#4 와 무관 · 1~3).
    public class RateCsRequest
    {
        public int Rating { get; set; }
        public string? Comment { get; set; }
    }

    // 작15 E-1 — 열림 판정 재료. dynamic 금지(TINYINT ↔ bool/int 캐스팅 500 선례).
    private sealed class RatingStateRow
    {
        // 작15 S-1 봉합(사장님 결재 2026-10-08) — 쓴 사람만 평가한다. 판정 재료가 없으면 판정을 못 한다.
        public string? CreatedBy { get; set; }
        public DateTime? SentAt { get; set; }
        public string? TerminalReason { get; set; }
        public int ReplyCount { get; set; }
        public int? MyRating { get; set; }
        public string? MyComment { get; set; }
        public DateTime? RatingSentAt { get; set; }
        public string? RatingTerminalReason { get; set; }
    }
}
