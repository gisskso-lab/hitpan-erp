using System.Data;
using System.Text.Json;
using Dapper;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

            await _db.ExecuteAsync(@"
                INSERT INTO cs_outbox (tenant_id, cs_request_id, payload_json)
                VALUES (@TenantId, @Id, @Payload)",
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
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var rows = await _db.QueryAsync(@"
            SELECT reply_id AS replyId, cs_request_id AS csRequestId, body,
                   replied_by_kind AS repliedByKind, replied_at AS repliedAt, read_at AS readAt
              FROM cs_replies
             WHERE tenant_id = @TenantId AND cs_request_id = @Id
             ORDER BY replied_at",
            new { TenantId = tenantId, Id = id });

        return Ok(new { success = true, data = rows });
    }

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
}
