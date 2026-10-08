using System.Security.Claims;
using Dapper;
using HitPan.Backoffice.API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 작14 C-3·C-4 — CS 누리집 (사장님 결재 2026-10-08)
//
// ■ 왜: 해결한 일이 사람 머릿속에서 끝나면 같은 일을 100번 다시 푼다.
//   누리집은 「이 증상엔 이렇게」가 쌓이는 창고이고, 다음 답을 빨리 만드는 재료다.
//
// ■ 안 바꾼 뼈대(설계 §2-ⓑ 결재본)
//   · 저장 형식은 md — 쓰는 사람에겐 파일과 똑같다. 단 **정본은 DB**(파일은 내보내기용 · §2-ⓔ).
//   · 🔴 UPDATE 0건 — 고치면 version+1 **새 행**. 옛 판이 남아야 「누가 언제 왜」가 남는다.
//   · 🔴 승격 초안 쿼리는 tenant_id·회사명·담당자를 **SELECT 자체를 안 한다**
//     (화이트리스트 — 가져오지 않은 값은 잊을 수도 없다).
//   · 🔴 승인 전 **본문 재스캔** — 화면 검사만으론 API 직접 호출로 우회된다.
//   · 🔴 승인 없으면 승인 판 **행 자체가 안 생긴다** — DB CHECK(ck_bo_kb_approved)가 최종 판정자.
//   · 재사용 재료(= 나중에 AI 가 읽을 근거)는 **WHERE status='승인'** 으로만 나간다(if 분기 금지).
[ApiController]
[Route("api/admin/kb")]
[Authorize(Policy = "PlatformAdmin")]
public class KbController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<KbController> _logger;

    // 8종 정본(결-9) ↔ 이슈코드 머리글(C-3). 이 표가 두 축의 유일한 연결이다.
    private static readonly Dictionary<string, string> CategoryPrefix = new(StringComparer.Ordinal)
    {
        ["use"] = "USE", ["set"] = "SET", ["net"] = "NET", ["dat"] = "DAT",
        ["upd"] = "UPD", ["bug"] = "BUG", ["ins"] = "INS", ["etc"] = "ETC",
    };

    // 8폴더 .MD 미러 경로(C-3) — 레포 docs/CS/누리집/ 아래 폴더 이름과 **글자까지 같아야** 한다.
    private static readonly Dictionary<string, string> CategoryFolder = new(StringComparer.Ordinal)
    {
        ["use"] = "1.사용", ["set"] = "2.설정", ["net"] = "3.통신", ["dat"] = "4.데이터",
        ["upd"] = "5.업데이트", ["bug"] = "6.기능오류", ["ins"] = "7.설치", ["etc"] = "8.기타",
    };

    private const int MaxTitle = 200;
    private const int MaxBodyMd = 8000;

    public KbController(IConfiguration config, ILogger<KbController> logger)
    {
        _config = config;
        _logger = logger;
    }

    // ── 목록 — 이슈코드별 **최신 판**만 ─────────────────────────────
    [HttpGet("docs")]
    public async Task<IActionResult> List([FromQuery] string? category, [FromQuery] string? status,
                                          [FromQuery] string? q, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync(@"
            SELECT d.doc_id AS docId, d.issue_code AS issueCode, d.category, d.title,
                   d.status, d.version, d.approved_by AS approvedBy, d.approved_at AS approvedAt,
                   d.source_ticket_id AS sourceTicketId, d.created_at AS createdAt
              FROM bo_kb_docs d
              JOIN (SELECT issue_code, MAX(version) AS v FROM bo_kb_docs GROUP BY issue_code) m
                ON m.issue_code = d.issue_code AND m.v = d.version
             WHERE (@Category IS NULL OR d.category = @Category)
               AND (@Status   IS NULL OR d.status   = @Status)
               AND (@Q IS NULL OR d.title LIKE CONCAT('%', @Q, '%') OR d.issue_code LIKE CONCAT('%', @Q, '%'))
             ORDER BY d.created_at DESC
             LIMIT 300",
            new
            {
                Category = Nullify(category),
                Status = Nullify(status),
                Q = Nullify(q),
            });
        return Ok(new { success = true, data = rows });
    }

    // ── 한 문서의 **모든 판**(이력) — version 이 올라도 옛 판이 남는 것을 화면이 보여준다 ──
    [HttpGet("docs/{issueCode}")]
    public async Task<IActionResult> History(string issueCode, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync(@"
            SELECT doc_id AS docId, issue_code AS issueCode, category, title, body_md AS bodyMd,
                   status, version, approved_by AS approvedBy, approved_at AS approvedAt,
                   source_ticket_id AS sourceTicketId, created_by AS createdBy, created_at AS createdAt
              FROM bo_kb_docs WHERE issue_code = @Code ORDER BY version DESC",
            new { Code = issueCode });
        return Ok(new { success = true, data = rows });
    }

    // ── 🔴 재사용 재료 — 승인 판만. WHERE 절에서 배제한다(if 분기 금지 · 설계 ⓓ①) ──
    //    묶음 D 의 AI 가 읽을 근거는 이 한 경로로만 나간다.
    [HttpGet("reference")]
    public async Task<IActionResult> Reference([FromQuery] string? category, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync(@"
            SELECT d.issue_code AS issueCode, d.category, d.title, d.body_md AS bodyMd, d.version
              FROM bo_kb_docs d
              -- 🔴 최신 판은 **상태와 무관하게** 고르고, 그 최신 판이 '승인' 일 때만 내보낸다.
              --    안쪽에서 status='승인' 으로 먼저 걸면 폐기된 문서의 **옛 승인 판**이 되살아난다
              --    (G-C-5 가 실측으로 잡음 — 2026-10-08 CI).
              JOIN (SELECT issue_code, MAX(version) AS v FROM bo_kb_docs GROUP BY issue_code) m
                ON m.issue_code = d.issue_code AND m.v = d.version
             WHERE d.status = '승인'
               AND (@Category IS NULL OR d.category = @Category)
             ORDER BY d.issue_code
             LIMIT 200",
            new { Category = Nullify(category) });
        return Ok(new { success = true, data = rows });
    }

    // ── 승격 초안 — 🔴 식별정보를 **SELECT 하지 않는** 쿼리 하나가 안전핀이다 ──
    [HttpPost("promote/{ticketId:long}")]
    public async Task<IActionResult> PromoteDraft(long ticketId, CancellationToken ct)
    {
        var actor = Actor();
        if (actor is null) return Forbid();

        await using var db = await OpenAsync(ct);

        // 🔴 가져오는 칸은 이 7개뿐이다. tenant_id·회사명·담당자·연락처는 **적지 않았다**.
        //    (게이트 G-C-1 이 「티켓엔 그 값이 실제로 있는데도 초안엔 0건」을 확인한다)
        var t = await db.QueryFirstOrDefaultAsync<TicketDraftRow>(@"
            SELECT category AS Category, shape_tag AS ShapeTag, sub_tag AS SubTag,
                   body AS Body, screen_code AS ScreenCode, erp_version AS ErpVersion, status AS Status
              FROM bo_cs_tickets WHERE id = @Id", new { Id = ticketId });
        if (t is null) return NotFound(new { success = false, message = "티켓이 없습니다" });

        // 본문 인용은 재스캔을 통과한 것만. 걸리면 **자리 표시**로 바꾼다(설계 §2-ⓑ).
        var rule = BoForbiddenFieldScanner.Scan(t.Body);
        var quoted = rule is null
            ? (t.Body ?? "")
            : "(식별정보 모양이 있어 본문 인용을 생략했습니다 — 증상만 적어 주세요)";

        var category = CategoryPrefix.ContainsKey(t.Category ?? "") ? t.Category! : "etc";
        var bodyMd = BuildPrdTemplate(quoted, t.ScreenCode, t.ErpVersion, t.ShapeTag, t.SubTag);

        await LogAsync(db, null, null, "promote_draft", actor, rule);

        return Ok(new
        {
            success = true,
            draft = new
            {
                category,
                title = "",                 // 제목은 사람이 적는다(초안이 제목까지 지으면 검토가 형식이 된다)
                bodyMd,
                sourceTicketId = ticketId,
                quoteOmitted = rule is not null,
            },
        });
    }

    // ── 등재 — 초안 저장 또는 승인. 승인은 **쓴 사람이 승인자**다 ──
    [HttpPost("docs")]
    public async Task<IActionResult> Create([FromBody] DocRequest req, CancellationToken ct)
    {
        var actor = Actor();
        if (actor is null) return Forbid();

        var category = req?.Category?.Trim() ?? "";
        var title = req?.Title?.Trim() ?? "";
        var bodyMd = req?.BodyMd ?? "";
        var status = string.IsNullOrWhiteSpace(req?.Status) ? "초안" : req!.Status!.Trim();

        if (!CategoryPrefix.ContainsKey(category))
            return BadRequest(new { success = false, message = "분류를 8종 중에서 골라 주세요" });
        if (status is not ("초안" or "승인"))
            return BadRequest(new { success = false, message = "등재 상태는 초안 또는 승인입니다" });
        if (title.Length is 0 or > MaxTitle)
            return BadRequest(new { success = false, message = $"제목은 1~{MaxTitle}자입니다" });
        if (bodyMd.Trim().Length == 0 || bodyMd.Length > MaxBodyMd)
            return BadRequest(new { success = false, message = $"본문은 1~{MaxBodyMd}자입니다" });

        await using var db = await OpenAsync(ct);

        // 🔴 승인 전 재스캔 — 화면이 아니라 **여기**가 판정자다(API 직접 호출 우회 차단).
        var rule = BoForbiddenFieldScanner.Scan(bodyMd);
        if (rule is not null)
        {
            await LogAsync(db, null, null, "reject", actor, rule);
            return BadRequest(new { success = false, message = "본문에 식별정보 모양이 있습니다 — 그 줄을 지워 주세요" });
        }

        var issueCode = await NextIssueCodeAsync(db, category);
        var approved = status == "승인";

        var docId = await db.ExecuteScalarAsync<long>(@"
            INSERT INTO bo_kb_docs
                (issue_code, category, title, body_md, status, version,
                 approved_by, approved_at, source_ticket_id, created_by)
            VALUES (@Code, @Category, @Title, @Body, @Status, 1,
                    @ApprovedBy, @ApprovedAt, @Src, @Actor);
            SELECT LAST_INSERT_ID();",
            new
            {
                Code = issueCode, Category = category, Title = title, Body = bodyMd, Status = status,
                ApprovedBy = approved ? actor : null,
                ApprovedAt = approved ? (DateTime?)DateTime.UtcNow : null,
                Src = req?.SourceTicketId, Actor = actor,
            });

        await LogAsync(db, docId, issueCode, approved ? "approve" : "save_draft", actor, null);
        _logger.LogInformation("[누리집] 등재 code={Code} status={Status}", issueCode, status);
        return Ok(new { success = true, docId, issueCode, version = 1, status });
    }

    // ── 고침 — UPDATE 가 아니라 version+1 **새 행**(이력 보존) ──
    [HttpPost("docs/{issueCode}/revise")]
    public async Task<IActionResult> Revise(string issueCode, [FromBody] DocRequest req, CancellationToken ct)
    {
        var actor = Actor();
        if (actor is null) return Forbid();

        var title = req?.Title?.Trim() ?? "";
        var bodyMd = req?.BodyMd ?? "";
        var status = string.IsNullOrWhiteSpace(req?.Status) ? "초안" : req!.Status!.Trim();
        if (status is not ("초안" or "승인" or "폐기"))
            return BadRequest(new { success = false, message = "상태는 초안·승인·폐기입니다" });
        if (title.Length is 0 or > MaxTitle)
            return BadRequest(new { success = false, message = $"제목은 1~{MaxTitle}자입니다" });
        if (bodyMd.Trim().Length == 0 || bodyMd.Length > MaxBodyMd)
            return BadRequest(new { success = false, message = $"본문은 1~{MaxBodyMd}자입니다" });

        await using var db = await OpenAsync(ct);
        var cur = await db.QueryFirstOrDefaultAsync<DocHeadRow>(@"
            SELECT category AS Category, MAX(version) AS Version
              FROM bo_kb_docs WHERE issue_code = @Code GROUP BY category",
            new { Code = issueCode });
        if (cur is null) return NotFound(new { success = false, message = "문서가 없습니다" });

        var rule = BoForbiddenFieldScanner.Scan(bodyMd);
        if (rule is not null)
        {
            await LogAsync(db, null, issueCode, "reject", actor, rule);
            return BadRequest(new { success = false, message = "본문에 식별정보 모양이 있습니다 — 그 줄을 지워 주세요" });
        }

        var approved = status == "승인";
        var nextVer = cur.Version + 1;
        var docId = await db.ExecuteScalarAsync<long>(@"
            INSERT INTO bo_kb_docs
                (issue_code, category, title, body_md, status, version,
                 approved_by, approved_at, source_ticket_id, created_by)
            VALUES (@Code, @Category, @Title, @Body, @Status, @Ver,
                    @ApprovedBy, @ApprovedAt, @Src, @Actor);
            SELECT LAST_INSERT_ID();",
            new
            {
                Code = issueCode, cur.Category, Title = title, Body = bodyMd, Status = status, Ver = nextVer,
                ApprovedBy = approved ? actor : null,
                ApprovedAt = approved ? (DateTime?)DateTime.UtcNow : null,
                Src = req?.SourceTicketId, Actor = actor,
            });

        var action = status == "폐기" ? "discard" : (approved ? "approve" : "revise");
        await LogAsync(db, docId, issueCode, action, actor, null);
        return Ok(new { success = true, docId, issueCode, version = nextVer, status });
    }

    // ── .MD 미러 내보내기 — 8폴더 경로 + md 본문(승인 판만) ──
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<ExportRow>(@"
            SELECT d.issue_code AS IssueCode, d.category AS Category, d.title AS Title,
                   d.body_md AS BodyMd, d.version AS Version
              FROM bo_kb_docs d
              -- 🔴 재사용 재료와 **같은 규칙**이어야 한다(미러에 폐기된 옛 판이 남으면 안 된다).
              JOIN (SELECT issue_code, MAX(version) AS v FROM bo_kb_docs GROUP BY issue_code) m
                ON m.issue_code = d.issue_code AND m.v = d.version
             WHERE d.status = '승인'
             ORDER BY d.issue_code");

        var files = rows.Select(r => new
        {
            path = $"docs/CS/누리집/{Folder(r.Category)}/{r.IssueCode}.md",
            markdown = $"# {r.IssueCode} {r.Title}\n\n> 판 v{r.Version} · 정본은 백오피스 DB(이 파일은 미러)\n\n{r.BodyMd}\n",
        }).ToList();

        return Ok(new { success = true, count = files.Count, files });
    }

    // ── 안쪽 ────────────────────────────────────────────────────────
    private static string Folder(string? category)
        => CategoryFolder.TryGetValue(category ?? "", out var f) ? f : CategoryFolder["etc"];

    /// <summary>문제점/해결점 PRD 틀(C-3) — 빈 칸은 사람이 채운다(반자동).</summary>
    private static string BuildPrdTemplate(string quoted, string? screen, string? version, string? shape, string? subTag)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## 문제점").AppendLine();
        sb.AppendLine($"- 고객이 한 말: {quoted}");
        sb.AppendLine($"- 화면: {Or(screen)}");
        sb.AppendLine($"- 히트판 판: {Or(version)}");
        sb.AppendLine($"- 고객 말 모양 / 업무 영역: {Or(shape)} / {Or(subTag)}");
        sb.AppendLine("- 재현 조건(적어 주세요):").AppendLine("  1. ").AppendLine();
        sb.AppendLine("## 해결점").AppendLine();
        sb.AppendLine("1. (손으로 짚는 순서대로 적어 주세요)").AppendLine();
        sb.AppendLine("## 확인").AppendLine();
        sb.AppendLine("- 고객이 「됐다」고 말한 지점:");
        sb.AppendLine("- 같은 증상 재발 시 먼저 볼 것:").AppendLine();
        sb.AppendLine("> 🔴 이 문서에 적지 않는 것: 회사 이름·사람 이름·연락처·");
        sb.AppendLine("> 그 회사만의 희귀 조합(직원 수·창고 수 같은 모양도 신분이 된다).");
        return sb.ToString();
    }

    private static string Or(string? s) => string.IsNullOrWhiteSpace(s) ? "(없음)" : s;

    private static async Task<string> NextIssueCodeAsync(MySqlConnection db, string category)
    {
        var prefix = CategoryPrefix[category];
        var max = await db.ExecuteScalarAsync<long?>(@"
            SELECT MAX(CAST(SUBSTRING_INDEX(issue_code, '-', -1) AS UNSIGNED))
              FROM bo_kb_docs WHERE issue_code LIKE CONCAT(@Prefix, '-%')",
            new { Prefix = prefix });
        return $"{prefix}-{(max ?? 0) + 1}";
    }

    private async Task LogAsync(MySqlConnection db, long? docId, string? issueCode,
                                string action, string actor, string? ruleCode)
    {
        try
        {
            await db.ExecuteAsync(@"
                INSERT INTO bo_kb_doc_logs (doc_id, issue_code, action, actor_id, rule_code)
                VALUES (@DocId, @Code, @Action, @Actor, @Rule)",
                new { DocId = docId, Code = issueCode, Action = action, Actor = actor, Rule = ruleCode });
        }
        catch (MySqlException ex)
        {
            _logger.LogWarning(ex, "[누리집] 기록 실패 action={Action}", action); // #15 — 본 작업은 막지 않는다
        }
    }

    private string? Actor() => User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

    private static string? Nullify(string? s)
        => string.IsNullOrWhiteSpace(s) || s == "all" ? null : s.Trim();

    private sealed class TicketDraftRow
    {
        public string? Category { get; set; }
        public string? ShapeTag { get; set; }
        public string? SubTag { get; set; }
        public string? Body { get; set; }
        public string? ScreenCode { get; set; }
        public string? ErpVersion { get; set; }
        public string? Status { get; set; }
    }

    private sealed class DocHeadRow
    {
        public string Category { get; set; } = "";
        public int Version { get; set; }
    }

    private sealed class ExportRow
    {
        public string IssueCode { get; set; } = "";
        public string? Category { get; set; }
        public string Title { get; set; } = "";
        public string BodyMd { get; set; } = "";
        public int Version { get; set; }
    }

    public class DocRequest
    {
        public string? Category { get; set; }
        public string? Title { get; set; }
        public string? BodyMd { get; set; }
        public string? Status { get; set; }
        public long? SourceTicketId { get; set; }
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
