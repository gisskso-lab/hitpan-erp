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

    // ══════════════════════════════════════════════════════════════
    // 🔴 20261012작3 — `cs_requests.origin`(DB-144)이 **아직 없는 DB** 에서도 종전 화면이 살아 있어야 한다
    //
    // 사고 모양(작2 [4] V-2 와 같다): 새 칸을 **조건 없이** SELECT 하면 마이그가 안 들어간 DB 에서
    //   `Unknown column` → **500**. 그건 신규 기능 실패가 아니라 **되던 CS 쪽지 화면의 회귀**다.
    //
    // 🔴 그리고 이 위험은 가설이 아니다 — ERP 마이그 적용기는 **실패해도 기동을 계속**한다
    //   (`Program.cs:535~550` 가 직접 적어 뒤었다: *「새 컬럼을 쓰는 화면은 오류가 날 수 있으나
    //   나머지 업무는 계속 사용할 수 있습니다」*). 디스크 부족·락·권한으로 DB-144 가 한 번 실패하면
    //   그 상태로 새 코드가 선다(9/29 C: 디스크 부족으로 업데이트가 막힌 전례).
    //
    // ⇒ 칸이 있는지 **DB 에 직접 물어보고** 조회문을 고른다. 없으면 모두 'local' 로 다룬다
    //   (선발신 기능만 조용히 비어 보이고, 종전 기능은 그대로 돈다).
    // 🔴 캐시는 **DB 이름별**이다 — 게이트가 시험 DB 를 여러 개 돌린다(작2 와 같은 규율).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _originColByDb = new();

    private async Task<bool> HasOriginColumnAsync()
    {
        var key = _db.Database ?? "";
        if (_originColByDb.TryGetValue(key, out var cached)) return cached;

        var found = await _db.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM information_schema.columns
             WHERE table_schema = DATABASE() AND table_name = 'cs_requests' AND column_name = 'origin'");

        var present = found == 1;
        if (!present)
            _logger.LogWarning("[CS쪽지] cs_requests.origin 칸 없음(DB-144 미적용) — 본사 안내 구분 없이 돈다");
        _originColByDb[key] = present;
        return present;
    }

    // 🔴 20261012작4 B-3 — 본사 진행 상태 칸(DB-145)도 같은 모양으로 묻는다.
    //   캐시가 **DB 이름별**인 이유는 origin 과 같다 — 게이트가 칸을 실제로 떼고 다시 묻는다.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _hqStatusColByDb = new();

    private async Task<bool> HasHqStatusColumnsAsync()
    {
        var key = _db.Database ?? "";
        if (_hqStatusColByDb.TryGetValue(key, out var cached)) return cached;

        var found = await _db.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM information_schema.columns
             WHERE table_schema = DATABASE() AND table_name = 'cs_requests'
               AND column_name IN ('hq_status', 'hq_status_at')");

        var present = found == 2;
        if (!present)
            _logger.LogWarning("[CS쪽지] cs_requests.hq_status 칸 없음(DB-145 미적용) — 본사 진행 상태 없이 돈다");
        _hqStatusColByDb[key] = present;
        return present;
    }

    /// <summary>게이트가 칸을 떼고 다시 묻게 한다(제품 경로에서는 부르지 않는다).</summary>
    public static void ResetHqStatusColumnCache() => _hqStatusColByDb.Clear();

    /// <summary>
    /// 시험이 같은 프로세스에서 DB 를 바꿔 쓸 때 판정을 다시 묻게 한다.
    /// <para>🔴 <c>public</c> 인 이유: 게이트 G-W3-5 가 **칸을 실제로 떼고** 다시 물어야 한다
    /// (글자로 흉내내면 그 게이트는 아무것도 재지 않는다). 제품 경로에서는 부르지 않는다.</para>
    /// </summary>
    public static void ResetOriginColumnCache() => _originColByDb.Clear();

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
                -- 🔴 [4] V-3 봉합(작3 4차수) — `created_at` 을 **명시**한다(UTC).
                --   DDL 의 `DEFAULT current_timestamp(6)`(DB-139:33)은 **세션 시간대**(이 서버는 KST)이고,
                --   본사가 보낸 안내는 본사가 찍은 **UTC** 로 들어온다(`CsAdminController:525` → 워커가 그대로 삽입).
                --   한 목록(`ORDER BY created_at DESC`)에 기준이 둘이면 **갓 온 본사 안내가 9시간 과거로 묻힌다**
                --   — 고객은 새 안내를 못 본다. 검증팀 실측 **540분**. 작2 V-6 과 같은 모양이다.
                INSERT INTO cs_requests
                    (cs_request_id, tenant_id, created_by, category, sub_tag, body, screen_code, erp_version, status, created_at)
                VALUES (@Id, @TenantId, @UserId, @Category, @SubTag, @Body, @ScreenCode, @ErpVersion, '접수', UTC_TIMESTAMP(6))",
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
    // 🔴 20261014작1 T-1 — 쪽 가르기 질의칸 `origin` 을 **더한다**(#1 추가만).
    //   `origin` 없는 종전 호출은 null 로 들어와 **종전 그대로 전체**를 준다(회귀 0 · G-W9-3).
    //   왜 서버에서 가르나: 화면만 둘로 갈라도 쿼리가 하나면 **본사 공지가 쌓일 때 고객이 쓴 문의가
    //   LIMIT 밖으로 밀려 사라진다**(1V A-1). 쪽마다 자기 몫을 세야 그 사고가 안 난다(G-W9-4).
    [HttpGet("requests")]
    //   🔴 둘 다 **기본값**을 준다 — 기존 게이트가 `List(default)` 한 칸으로 부르고 있어서
    //   기본값이 없으면 그 파일들을 고쳐야 한다. 그 파일은 이 갈래 소유가 아니다(작지서 §2).
    public async Task<IActionResult> List(string? origin = null, CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var hasOrigin = await HasOriginColumnAsync();
        // 🔴 20261012작4 B-3 — 본사 진행 상태(DB-145)도 **같은 모양**으로 조건부로 읽는다.
        //   칸이 없는 DB(업데이트 직후 마이그 전 1회)에서 종전 화면이 죽지 않게 — 작3 origin 선례 그대로.
        var hasHqStatus = await HasHqStatusColumnsAsync();

        // 🔴 쪽 조건은 **칸이 있을 때만** 단다. 칸이 없는 DB 에서 `WHERE r.origin` 을 넣으면
        //   1054(Unknown column)로 목록이 통째로 죽는다 — SELECT 쪽 `'local'` 대입은 칸이 없어도
        //   되지만 WHERE 는 안 된다. 그래서 가드를 둘로 쓰지 않고 이 한 자리에서 가른다(1V C-1).
        //   낯선 값(`?origin=zzz`)은 **전체**로 떨어뜨린다 — 화면을 비우지 않는다.
        var sideWhere = hasOrigin
            ? origin switch
            {
                "hq"    => " AND r.origin = 'hq'",
                "local" => " AND r.origin <> 'hq'",   // 'hq' 가 아닌 것은 전부 고객이 쓴 것이다
                _       => "",
            }
            // 🔴 칸이 없는 DB 에서 본사 쪽을 물으면 **빈 결과**를 준다 — 「전체」가 아니다.
            //   근거: 본사 선발신을 앉히는 INSERT 가 `origin` 을 **명시**한다
            //   (CsOutboxSenderWorker.ReceiveOutboundAsync) ⇒ 칸이 없으면 그 행은 애초에 못 들어온다
            //   ⇒ 본사 쪽 행은 **원리적으로 0건**이다. 여기서 전체를 주면 공지 화면에 **내 문의가
            //   섞여** 10/12 반려(섞여 보임)가 그대로 되살아난다. 빈 화면이 맞다(설계 §8).
            : origin == "hq" ? " AND 1 = 0" : "";

        var rows = await _db.QueryAsync($@"
            SELECT r.cs_request_id   AS csRequestId,
                   r.category        AS category,
                   r.sub_tag         AS subTag,
                   r.body            AS body,
                   r.status          AS status,
                   r.created_at      AS createdAt,
                   -- 🔴 20261012작3 B-4 — 출처(DB-144). 'hq' = 본사가 먼저 보낸 안내다.
                   --   고객이 **자기가 쓰지 않은 쪽지**를 자기 문의와 구별해야 한다.
                   --   칸이 아직 없는 DB 에서는 'local' 로 대체한다(위 HasOriginColumnAsync).
                   {(hasOrigin ? "r.origin" : "'local'")} AS origin,
                   -- 🔴 20261012작4 B-3 — 본사 진행 상태(DB-145). `status` 와 **다른 축**이다:
                   --   status = 이 쪽지가 본사까지 갔나 · hqStatus = 본사가 어디까지 처리했나.
                   --   NULL = 본사 미착수(종전 모양 그대로 보인다).
                   {(hasHqStatus ? "r.hq_status" : "NULL")}    AS hqStatus,
                   {(hasHqStatus ? "r.hq_status_at" : "NULL")} AS hqStatusAt,
                   o.sent_at         AS sentAt,
                   o.terminal_reason AS terminalReason,
                   -- 🔴 20261012작6 — **`COALESCE` 가 없으면 목록이 통째로 깨진다** (사장님 실측)
                   --
                   -- 사고: 본사 선발신 쪽지(`origin='hq'`)는 **올려보낼 일이 없어 `cs_outbox` 행이 없다.**
                   --   그래서 이 LEFT JOIN 이 NULL 을 주고, 화면 모델의 `public int AttemptCount`
                   --   (비널러블)에 JSON `null` 이 들어가 **역직렬화가 터진다** ⇒ 목록 전체가
                   --   「문의 목록을 불러오지 못했습니다」 로 바뀐다. 쪽지 한 건이 화면 전부를 가린다.
                   --
                   -- 🔴 왜 게이트가 못 잡았나: G-W3-2 는 **컨트롤러를 바로 불러** 사전으로 읽었다.
                   --   화면이 쓰는 **JSON → 비널러블 모델** 경로를 아무도 안 지나갔다
                   --   (「만드는 쪽만 재면 전부 통과하고도 반려난다」 — 8/27 작7 교훈과 같은 모양).
                   --   ⇒ G-W3-12 가 화면 모델 모양으로 역직렬화해서 이 자리를 문다.
                   --
                   -- 숫자 칸은 **서버가 0 을 보장한다** — 클라이언트 모양에 기대지 않는다.
                   COALESCE(o.attempt_count, 0) AS attemptCount,
                   (SELECT COUNT(*) FROM cs_replies p
                     WHERE p.tenant_id = r.tenant_id AND p.cs_request_id = r.cs_request_id) AS replyCount
              FROM cs_requests r
              LEFT JOIN cs_outbox o ON o.cs_request_id = r.cs_request_id
             -- 🔴 테넌트는 토큰 클레임에서 온 값 하나뿐이다 — 매개변수로 받지 않는다(#2).
             --   쪽 가르기 질의칸은 받지만, 그것은 **같은 테넌트 안에서** 쪽을 고를 뿐이다.
             WHERE r.tenant_id = @TenantId{sideWhere}
             ORDER BY r.created_at DESC
             -- 🔴 쪽별 LIMIT — 쪽 조건이 걸린 호출은 **그 쪽에서** 200건을 센다.
             --   한 쪽이 다른 쪽을 밀어내지 못한다(1V A-1 · G-W9-4). 전체 호출은 종전 200 그대로.
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

        // 🔴 [4] V-4 봉합(4차수) — 화면이 **무엇을 보고 있는지** 알아야 한다.
        //   본사가 먼저 보낸 안내는 「내가 보낸 문의에 대한 답」이 아니다. 그 사실을 안 주면
        //   화면이 「보낸 문의에 대한 담당 상담원의 답입니다」라고 **사실과 다른 말**을 한다(#23).
        var origin = await _db.ExecuteScalarAsync<string?>(
            $@"SELECT {(await HasOriginColumnAsync() ? "origin" : "'local'")}
                 FROM cs_requests WHERE tenant_id = @TenantId AND cs_request_id = @Id",
            new { TenantId = tenantId, Id = id }) ?? "local";

        return Ok(new { success = true, data = rows, rating, origin });
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
        => await _db.QueryFirstOrDefaultAsync<RatingStateRow>($@"
            SELECT r.created_by      AS CreatedBy,
                   -- 칸이 아직 없는 DB 에서는 'local' — 종전 평가 판정이 그대로 돈다(작3 봉합).
                   {(await HasOriginColumnAsync() ? "r.origin" : "'local'")} AS Origin,
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
    // 🔴 작3 B-5 — 본사가 먼저 보낸 안내는 **질문이 아니다** ⇒ 평가 대상이 아니다.
    //   이 갈래가 없으면 `not_mine` 으로 떨어져 「문의를 쓴 분만 평가할 수 있습니다」가 뜬다 —
    //   고객이 쓴 문의가 아예 아닌데 그 문장은 사실과 다르다(#23 고객 노출 문구는 사실만).
    private const string ReasonHqNotice = "hq_notice";

    // §4 표 그대로 — 이 함수 하나가 「조용히 사라지는 평가」를 막는 전부다.
    //   순서가 뜻이다: 이미 평가한 건이 가장 먼저(Q-2 첫 평가만) → 종결 → 미전송 → 답 0건 → 열림.
    private static string RatingReason(RatingStateRow? s, string? userId)
    {
        if (s is null) return "not_sent";                       // 내 테넌트에 그 쪽지가 없다
        // 🔴 작3 B-5 — 가장 먼저 가른다. 본사 안내는 평가·재전송 축에 아예 들어오지 않는다.
        if (string.Equals(s.Origin, "hq", StringComparison.Ordinal)) return ReasonHqNotice;
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
        // 🔴 20261012작3 — 종전 「전달이 지연되고 있습니다. **본사가 확인 중입니다.**」는 **거짓**이었다.
        //   `failed` 는 재시도 50회가 다 실패해 **멈춘** 상태다(백오프 상한 10분 ⇒ 약 8시간 뒤).
        //   그 문의는 **본사에 도착한 적이 없어** 본사는 존재 자체를 모른다 — 확인할 사람이 없다.
        //   게다가 `terminal_reason` 을 비우는 코드가 레포 전체에 **0건**이라 **영구히 멈춘다**
        //   ⇒ 「기다리면 된다」고 말하면 고객은 영원히 기다린다. **할 수 있는 일**을 말한다.
        "failed" => "문의가 본사에 전달되지 않았습니다. 다시 보내 주세요.",
        "no_reply" => "답이 오면 평가할 수 있습니다.",
        "not_mine" => "문의를 쓴 분만 평가할 수 있습니다.",
        "hq_notice" => "본사에서 보낸 안내입니다. 평가 대상이 아닙니다.",
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

        // 🔴 20261012 사장님 실측 반려 — 「백오피스에서 보낸 메시지를 어디서 보는지, 메뉴가 없음」
        //   숫자는 종전부터 있었는데 **그 숫자를 쓰는 배너가 전부 「CS 답변」이라고 말했다.**
        //   본사 안내는 **답변이 아니다** — 고객이 「내가 뭘 물었지?」 하며 답을 찾다가 못 찾는다
        //   ([4] V-4 와 같은 종류가 전역 배너에 남아 있었다).
        //   ⇒ 출처별로 나눠 내려준다. `count`(합계)는 **그대로** 둬서 옛 화면이 안 깨진다(추가만 · #1).
        //   칸이 없는 DB 에서도 살아야 하므로 `origin` 은 가드를 지난다.
        int hq = 0;
        if (await HasOriginColumnAsync())
        {
            hq = await _db.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*) FROM cs_replies p
                  JOIN cs_requests r ON r.cs_request_id = p.cs_request_id
                 WHERE p.tenant_id = @TenantId AND p.read_at IS NULL AND r.origin = 'hq'",
                new { TenantId = tenantId });
        }
        return Ok(new { success = true, count = n, hqCount = hq, replyCount = n - hq });
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
        /// <summary>작3 B-5 — 'hq' 면 본사가 먼저 보낸 안내다(질문이 아니라 평가 대상이 아니다).</summary>
        public string? Origin { get; set; }
        public DateTime? SentAt { get; set; }
        public string? TerminalReason { get; set; }
        public int ReplyCount { get; set; }
        public int? MyRating { get; set; }
        public string? MyComment { get; set; }
        public DateTime? RatingSentAt { get; set; }
        public string? RatingTerminalReason { get; set; }
    }
}
