using System.Text.Json;
using Dapper;
using HitPan.Application.Common;
using HitPan.Application.Services;
using HitPan.API.Services.Headquarters;
using MySqlConnector;

namespace HitPan.API.BackgroundServices;

/// <summary>
/// 🔴 작14 B-3 — CS 쪽지 송출 + 답 Pull 워커 (설계 §1-2 F·G 합침 · 등록 = Program.cs OutboxPollerWorker 옆).
///
/// <para>■ 폴링은 **<c>sent_at IS NULL AND next_attempt_at &lt;= NOW(6)</c> 로만** 집는다
/// (설계 §3-3 — status 글자로 찾으면 인덱스를 못 탄다). 기동 시 큐 선소급 — 재부팅 전에 쌓인 것부터.</para>
///
/// <para>■ 4xx/5xx 구분(설계 §2-2):
///   · 400·422 = <c>terminal_reason='rejected'</c> 종결 — 재시도 없음 · 고객 화면에 사유(상태 「거부」)
///   · 5xx·타임아웃·연결 실패 = 지수백오프 재시도(30초→…→10분 고정 상한)
///   · 🔴 **어떤 경우에도 행 삭제 금지** — 상한(50회) 뒤 <c>failed</c> 로 멈추고 사람이 본다(#26 정신)</para>
///
/// <para>■ 문②(B-4) — 송신 **직전** 금지필드 검사 한 번 더: 문①을 우회한 경로(수동 INSERT·마이그)를 막는다.
/// 걸리면 rejected + <c>cs_forbidden_rejects</c> 기록(값은 저장 안 함).</para>
///
/// <para>■ 답 Pull(B-9) — 5분마다. <c>reply_id</c> 는 본사 발급 그대로 = INSERT 멱등(같은 답 2번 = 1건).
/// 수신 즉시 해당 쪽지 상태 「답변도착」 — N-8 「안 읽은 답 N」 이 이 행을 센다.</para>
///
/// <para>■ #16 — 한 사이클 = 단일 연결 · <c>Task.WhenAll</c> 금지(OutboxPollerWorker:86 과 같은 규칙).
/// ■ 본사가 죽어도 ERP 는 돈다(#30) — 이 워커의 어떤 실패도 ERP 기능을 막지 않는다.</para>
/// </summary>
public sealed class CsOutboxSenderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CsOutboxSenderWorker> _logger;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PullInterval = TimeSpan.FromMinutes(5);
    private const int MaxAttempts = 50;              // 상한 — 그 뒤 failed 로 멈춤(삭제 아님)
    private DateTime _lastPullUtc = DateTime.MinValue;
    private bool _ownerReported;

    public CsOutboxSenderWorker(IServiceScopeFactory scopeFactory, ILogger<CsOutboxSenderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken st)
    {
        _logger.LogInformation("[CS송신] 워커 기동 — 큐 선소급부터");
        while (!st.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var hq = scope.ServiceProvider.GetRequiredService<IHeadquartersClient>();

                await using var db = OpenLocal();
                await db.OpenAsync(st);

                var ownerAccountId = await db.ExecuteScalarAsync<string?>(
                    "SELECT email FROM users WHERE is_parent = 1 AND is_active = 1 LIMIT 1");

                if (string.IsNullOrEmpty(ownerAccountId))
                {
                    // 부모계정이 없으면 3중 인증 재료가 안 모인다 — 보내지 않고 큐에 둔다(유실 0).
                    _logger.LogWarning("[CS송신] 부모계정(is_parent=1) 0건 — 이번 사이클 송신 보류(큐 보존)");
                }
                else
                {
                    // S-2 첫 보고(B-0 송신측) — 기동 후 성공할 때까지 사이클마다 1회 시도.
                    //   백오피스 tenants.owner_account_id 가 채워져야 3중 일치(부모계정 축)가 열린다.
                    //   실패해도 쪽지는 큐에 보존(수신이 owner_not_registered 401 → 재시도)이라 유실 0.
                    if (!_ownerReported)
                    {
                        var (code, _) = await hq.PostOwnerReportAsync(ownerAccountId, st);
                        _ownerReported = code is >= 200 and < 300;
                        if (_ownerReported)
                            _logger.LogInformation("[CS송신] S-2 대표 아이디 첫 보고 완료");
                    }

                    await SendPendingAsync(db, hq, ownerAccountId, _logger, st);

                    // 작15 E-1 — 만족도 평가 갈래. 같은 사이클·같은 연결(#16) · 30초 주기.
                    //   위 함수 본문은 한 글자도 안 바꿨다(#1) — 호출 한 줄만 더했다.
                    await SendPendingRatingsAsync(db, hq, ownerAccountId, _logger, st);

                    if (DateTime.UtcNow - _lastPullUtc >= PullInterval)
                    {
                        await PullRepliesAsync(db, hq, ownerAccountId, _logger, st);
                        _lastPullUtc = DateTime.UtcNow;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // #15 — 워커는 죽지 않는다. 사이클 실패는 로그로 남고 다음 사이클이 잇는다.
                _logger.LogWarning(ex, "[CS송신] 사이클 실패 — 다음 사이클에 계속");
            }

            try { await Task.Delay(PollInterval, st); }
            catch (OperationCanceledException) { break; }
        }
    }

    // ── 송신 — 한 건씩 순서대로 (#16 단일 연결 · WhenAll 금지) ──────────────
    //   public static 인 이유: 게이트(CsMessagePipelineGateTests)가 격리 DB + 가짜 본사로
    //   이 사이클을 **직접** 돌려 동작을 잰다 — 글자가 아니라 동작(누적 26회 지적 자리).
    public static async Task SendPendingAsync(MySqlConnection db, IHeadquartersClient hq, string ownerAccountId, ILogger logger, CancellationToken st)
    {
        var rows = (await db.QueryAsync<OutboxRow>(@"
            SELECT outbox_id AS OutboxId, tenant_id AS TenantId, cs_request_id AS CsRequestId,
                   payload_json AS PayloadJson, attempt_count AS AttemptCount
              FROM cs_outbox
             WHERE sent_at IS NULL AND terminal_reason IS NULL AND next_attempt_at <= UTC_TIMESTAMP(6)
             ORDER BY outbox_id
             LIMIT 20")).ToList();

        foreach (var row in rows)
        {
            if (st.IsCancellationRequested) return;

            // 문② — 송신 직전 한 번 더 (문① 우회 경로 차단)
            var rule = ScanPayloadBody(row.PayloadJson);
            if (rule is not null)
            {
                await db.ExecuteAsync(
                    "INSERT INTO cs_forbidden_rejects (tenant_id, rule_code) VALUES (@TenantId, @Rule)",
                    new { row.TenantId, Rule = rule });
                await MarkTerminalAsync(db, row, "rejected", 400, "forbidden-field(door2)");
                continue;
            }

            var (code, body) = await hq.PostAsync("/api/backoffice/cs/messages", row.PayloadJson, ownerAccountId, st);

            if (code is >= 200 and < 300)
            {
                await db.ExecuteAsync(@"
                    UPDATE cs_outbox SET sent_at = UTC_TIMESTAMP(6), last_status_code = @Code WHERE outbox_id = @OutboxId;
                    UPDATE cs_requests SET status = '전송완료' WHERE cs_request_id = @CsRequestId AND status IN ('접수','전송중');",
                    new { Code = code, row.OutboxId, row.CsRequestId });
            }
            else if (code is 400 or 422)
            {
                // 본사가 내용으로 거부 — 재시도해도 같은 답이다. 종결 + 고객 화면 사유.
                await MarkTerminalAsync(db, row, "rejected", code, Trim(body));
            }
            else
            {
                // 0(타임아웃·연결) · 5xx · 401/403(재인증 대기 — B-10 전까지 보존) = 재시도. 글 삭제는 없다.
                var attempt = row.AttemptCount + 1;
                var reason = attempt >= MaxAttempts ? "failed" : null;
                var backoffSec = Math.Min(30 * Math.Pow(2, Math.Min(attempt, 10)), 600); // 상한 10분
                await db.ExecuteAsync(@"
                    UPDATE cs_outbox
                       SET attempt_count = @Attempt, last_status_code = @Code, last_error = @Err,
                           next_attempt_at = DATE_ADD(UTC_TIMESTAMP(6), INTERVAL @Backoff SECOND),
                           terminal_reason = @Reason
                     WHERE outbox_id = @OutboxId",
                    new { Attempt = attempt, Code = code, Err = Trim(body), Backoff = (int)backoffSec, Reason = reason, row.OutboxId });
                if (reason == "failed")
                    logger.LogError("[CS송신] 상한 도달 — failed 로 멈춤(삭제 아님 · 사람이 본다) outbox={Id}", row.OutboxId);
            }
        }
    }

    // ── 작15 E-1 — 만족도 평가 송신 (cs_rating_outbox · 기존 두 함수와 **같은 모양**) ────
    //   public static 인 이유는 SendPendingAsync 와 같다 — 게이트가 격리 DB + 가짜 본사로
    //   이 사이클을 직접 돌려 **동작**을 잰다(글자가 아니라 동작 · 누적 26회 지적 자리).
    //
    //   🔴 cs_requests.status 는 건드리지 않는다 — 평가는 쪽지 상태축이 아니다.
    //      섞으면 「답변 도착」 글자가 평가에 덮인다(설계 §6 · R-5).
    //   🔴 행 삭제 0 — 상한(50회) 뒤 failed 로 멈추고 사람이 본다. 평가는 고객이 쓴 글이다.
    public static async Task SendPendingRatingsAsync(MySqlConnection db, IHeadquartersClient hq, string ownerAccountId, ILogger logger, CancellationToken st)
    {
        var rows = (await db.QueryAsync<RatingRow>(@"
            SELECT outbox_id AS OutboxId, tenant_id AS TenantId, cs_request_id AS CsRequestId,
                   `rating` AS Rating, `comment` AS Comment, attempt_count AS AttemptCount
              FROM cs_rating_outbox
             WHERE sent_at IS NULL AND terminal_reason IS NULL AND next_attempt_at <= UTC_TIMESTAMP(6)
             ORDER BY outbox_id
             LIMIT 20")).ToList();

        foreach (var row in rows)
        {
            if (st.IsCancellationRequested) return;

            // 문② — 송신 직전 한 줄 평 재검. 기존 ScanPayloadBody 는 payload.body 만 보므로
            //   평가는 그 그물에 안 걸린다 ⇒ 같은 스캐너를 쓰는 검사를 하나 더 둔다.
            //   (ScanPayloadBody 본문은 한 글자도 바꾸지 않았다 — messages 쪽 불변 · #1)
            var rule = ScanRatingComment(row.Comment);
            if (rule is not null)
            {
                await db.ExecuteAsync(
                    "INSERT INTO cs_forbidden_rejects (tenant_id, rule_code) VALUES (@TenantId, @Rule)",
                    new { row.TenantId, Rule = rule });
                await MarkRatingTerminalAsync(db, row.OutboxId, "rejected", 400, "forbidden-field(door2)");
                continue;
            }

            // 몸통은 **정확히 3키** — 4번째 키가 있으면 본사가 통째 400 으로 거부한다
            //   (CsInboundController.cs:200-205 화이트리스트). 봉투(3중 재료)는 HeadquartersClient 가 싼다.
            var payload = JsonSerializer.Serialize(new
            {
                csRequestId = row.CsRequestId,
                rating = row.Rating,
                comment = row.Comment,
            });

            var (code, body) = await hq.PostAsync("/api/backoffice/cs/ratings", payload, ownerAccountId, st);

            if (code is >= 200 and < 300)
            {
                // 🔴 2xx 라도 본사에 티켓이 없었으면 duplicated 가 온다(CsInboundController.cs:231).
                //    그때 평가는 본사 어디에도 안 남는다 ⇒ 그 사실의 **유일한 흔적**을 last_error 에 적는다.
                //    고객 화면은 「보냈다」로 둔다 — 실패로 되돌리면 또 누르고 또 사라진다(설계 §8).
                var dup = LooksDuplicated(body);
                await db.ExecuteAsync(@"
                    UPDATE cs_rating_outbox
                       SET sent_at = UTC_TIMESTAMP(6), last_status_code = @Code, last_error = @Err
                     WHERE outbox_id = @OutboxId",
                    new { Code = code, Err = dup ? "duplicated" : null, row.OutboxId });
                if (dup)
                    logger.LogWarning("[CS평가] 본사에 해당 티켓이 없었다(duplicated) — 흔적만 남긴다 outbox={Id}", row.OutboxId);
            }
            else if (code is 400 or 422)
            {
                // 내용으로 거부 — 재시도해도 같은 답이다. 종결 + 화면에 사유. 행 삭제 0.
                await MarkRatingTerminalAsync(db, row.OutboxId, "rejected", code, Trim(body));
            }
            else
            {
                // 0(타임아웃·연결) · 5xx · 401/403 · 🔴 **429(60분 잠금)** = 재시도.
                //   429 를 rejected 로 종결하면 그 창에 눌린 평가가 **영구 소실**된다(G-E1-4 가 이 자리를 문다).
                var attempt = row.AttemptCount + 1;
                var reason = attempt >= MaxAttempts ? "failed" : null;
                var backoffSec = Math.Min(30 * Math.Pow(2, Math.Min(attempt, 10)), 600); // 상한 10분
                await db.ExecuteAsync(@"
                    UPDATE cs_rating_outbox
                       SET attempt_count = @Attempt, last_status_code = @Code, last_error = @Err,
                           next_attempt_at = DATE_ADD(UTC_TIMESTAMP(6), INTERVAL @Backoff SECOND),
                           terminal_reason = @Reason
                     WHERE outbox_id = @OutboxId",
                    new { Attempt = attempt, Code = code, Err = Trim(body), Backoff = (int)backoffSec, Reason = reason, row.OutboxId });
                if (reason == "failed")
                    logger.LogError("[CS평가] 상한 도달 — failed 로 멈춤(삭제 아님 · 사람이 본다) outbox={Id}", row.OutboxId);
            }
        }
    }

    // 평가 종결 — cs_requests 는 건드리지 않는다(쪽지 상태축과 분리 · R-5).
    private static async Task MarkRatingTerminalAsync(MySqlConnection db, long outboxId, string reason, int code, string? err)
        => await db.ExecuteAsync(@"
            UPDATE cs_rating_outbox
               SET terminal_reason = @Reason, last_status_code = @Code, last_error = @Err
             WHERE outbox_id = @OutboxId",
            new { Reason = reason, Code = code, Err = err, OutboxId = outboxId });

    /// <summary>문② 평가판 — 한 줄 평 글만 본다(기존 <c>ScanPayloadBody</c> 는 payload.body 전용이라 평가를 못 본다).</summary>
    internal static string? ScanRatingComment(string? comment) => ForbiddenFieldScanner.Scan(comment);

    /// <summary>
    /// 본사가 2xx 와 함께 <c>duplicated:true</c> 를 줬나 — 티켓이 없어서 아무것도 저장되지 않았다는 뜻이다.
    /// <para>🔴 ASCII 키·불리언만 본다. 한글 글자 비교는 쓰지 않는다(JSON 직렬화가 한글을 <c>\uXXXX</c> 로 쓴다).</para>
    /// </summary>
    internal static bool LooksDuplicated(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("duplicated", out var d)
                && d.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            // 해석 못 하는 2xx 본문은 「평범한 성공」으로 본다 — 흔적을 거짓으로 남기지 않는다.
            return false;
        }
    }

    private static async Task MarkTerminalAsync(MySqlConnection db, OutboxRow row, string reason, int code, string? err)
        => await db.ExecuteAsync(@"
            UPDATE cs_outbox SET terminal_reason = @Reason, last_status_code = @Code, last_error = @Err WHERE outbox_id = @OutboxId;
            UPDATE cs_requests SET status = '거부' WHERE cs_request_id = @CsRequestId;",
            new { Reason = reason, Code = code, Err = err, row.OutboxId, row.CsRequestId });

    // ── 답 Pull (B-9) — reply_id 멱등 · 수신 즉시 「답변도착」 ─────────────────
    /// <summary>답 Pull — public static 인 이유는 SendPendingAsync 와 같다(게이트가 직접 돌린다).</summary>
    public static async Task PullRepliesAsync(MySqlConnection db, IHeadquartersClient hq, string ownerAccountId, ILogger logger, CancellationToken st)
    {
        var (code, body) = await hq.PullAsync("/api/backoffice/cs/replies/pull", ownerAccountId, st);
        if (code is < 200 or >= 300)
        {
            // B-5 수신부가 서기 전에는 404 가 정상이다 — 경고 한 줄만, 기능 영향 0.
            logger.LogDebug("[CS수신] Pull 미응답 code={Code} — 다음 주기에 재시도", code);
            return;
        }

        List<ReplyDto>? replies;
        List<OutboundDto>? outbound;
        List<string>? doc_purged;
        List<StatusDto>? statuses;
        try
        {
            var doc = JsonSerializer.Deserialize<PullResponse>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            replies = doc?.Replies;
            outbound = doc?.Outbound;
            doc_purged = doc?.PurgedOutbound;
            statuses = doc?.Statuses;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "[CS수신] Pull 응답 해석 실패 — 버리지 않고 다음 주기 재시도");
            return;
        }

        // 🔴 20261012작3 B-2·B-3 — **본사가 먼저 보낸 쪽지**를 앉힌다.
        //   답(replies)보다 먼저 처리한다: 선발신은 원본 문의가 없어서 아래 답 저장문
        //   (`FROM cs_requests r`)에 걸리면 **조용히 사라진다**. 그 자리가 10/11 에 발견된 구멍이다.
        if (outbound is { Count: > 0 })
            await ReceiveOutboundAsync(db, hq, ownerAccountId, outbound, logger, st);

        // 🔴 [4] V-1 봉합(4차수) — 본사가 **폐기한** 선발신은 고객 사본의 본문도 비운다.
        //   본사 화면엔 「폐기됨」인데 고객 쪽지함엔 본문이 남아 있으면 그건 폐기가 아니다.
        //   열쇠만 내려온다(본문 0) · 멱등(이미 빈 것을 또 비워도 같다).
        if (doc_purged is { Count: > 0 })
            await PurgeOutboundCopiesAsync(db, doc_purged, logger, st);

        // 🔴 20261012작4 B-2 — 본사 진행 상태를 **별도 칸**에 앉힌다 (사장님 실측 반려 ④).
        //   답(replies)과 **독립**으로 처리한다: 답이 0건이어도 상태는 바뀔 수 있고
        //   (예 — 전화로 응대하고 완료로 닫은 건), 그때도 고객 화면이 움직여야 한다.
        //   종전엔 아래 `if (replies is null …) return;` 뒤에 뭘 붙이면 그 경우가 조용히 빠졌다.
        if (statuses is { Count: > 0 })
            await ApplyHqStatusesAsync(db, statuses, logger, st);

        if (replies is null || replies.Count == 0) return;

        foreach (var r in replies)
        {
            if (string.IsNullOrWhiteSpace(r.ReplyId) || string.IsNullOrWhiteSpace(r.CsRequestId)) continue;
            // INSERT IGNORE = reply_id PK 멱등 (같은 답 2번 = 1건)
            await db.ExecuteAsync(@"
                INSERT IGNORE INTO cs_replies (reply_id, tenant_id, cs_request_id, body, replied_by_kind, replied_at)
                SELECT @ReplyId, r.tenant_id, r.cs_request_id, @Body, @Kind, @RepliedAt
                  FROM cs_requests r WHERE r.cs_request_id = @CsRequestId;
                UPDATE cs_requests SET status = '답변도착' WHERE cs_request_id = @CsRequestId AND status <> '종결';",
                new
                {
                    r.ReplyId,
                    r.CsRequestId,
                    Body = r.Body is { Length: > 4000 } ? r.Body[..4000] : r.Body ?? "",
                    Kind = r.RepliedByKind == "reseller" ? "대리점" : "본사",
                    RepliedAt = r.RepliedAt == default ? DateTime.UtcNow : r.RepliedAt,
                });
        }
        logger.LogInformation("[CS수신] 답 {N}건 수신", replies.Count);
    }

    // payload_json 안의 body 글만 다시 검사한다(문②) — 키 이름이 아니라 내용.
    private static string? ScanPayloadBody(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String
                ? ForbiddenFieldScanner.Scan(b.GetString())
                : null;
        }
        catch (JsonException)
        {
            return "payload_invalid"; // 해석 불가 payload 는 보내지 않는다(fail-closed)
        }
    }

    private static MySqlConnection OpenLocal()
    {
        var host = TenantConfigReader.Get("DB_HOST") ?? "localhost";
        var port = TenantConfigReader.Get("DB_PORT") ?? "3306";
        var name = TenantConfigReader.GetRequired("DB_NAME");
        var user = TenantConfigReader.GetRequired("DB_USER");
        var pass = TenantConfigReader.GetRequired("DB_PASSWORD");
        // GuidFormat=None — char(36) 이 Guid 로 와서 string DTO 매핑이 500 으로 터진다
        // (2026-08-12 양식템플릿 사고 · ConnectionStringGuidGuardTests 가 전수 강제).
        return new MySqlConnection(
            $"Server={host};Port={port};Database={name};Uid={user};Pwd={pass};CharSet=utf8mb4;AllowUserVariables=true;GuidFormat=None");
    }

    private static string? Trim(string? s) => string.IsNullOrEmpty(s) ? s : (s.Length <= 500 ? s : s[..500]);

    private sealed class OutboxRow
    {
        public long OutboxId { get; set; }
        public string TenantId { get; set; } = "";
        public string CsRequestId { get; set; } = "";
        public string PayloadJson { get; set; } = "";
        public int AttemptCount { get; set; }
    }

    // 작15 E-1 — 평가 큐 한 행. 점수는 금액이 아니라 정수다(1~3).
    private sealed class RatingRow
    {
        public long OutboxId { get; set; }
        public string TenantId { get; set; } = "";
        public string CsRequestId { get; set; } = "";
        public int Rating { get; set; }
        public string? Comment { get; set; }
        public int AttemptCount { get; set; }
    }

    private sealed class PullResponse
    {
        public List<ReplyDto>? Replies { get; set; }

        /// <summary>🔴 20261012작3 — 본사 선발신. 옛 판 응답에는 없다 ⇒ null 이면 그냥 건너뛴다.</summary>
        public List<OutboundDto>? Outbound { get; set; }

        /// <summary>🔴 [4] V-1 — 본사가 폐기한 선발신의 **열쇠만**. 고객 사본 본문을 비우라는 지시다.</summary>
        public List<string>? PurgedOutbound { get; set; }

        /// <summary>🔴 20261012작4 B-2 — 본사 진행 상태(처리중·완료). 옛 판 응답에는 없다 ⇒ null 이면 건너뛴다.</summary>
        public List<StatusDto>? Statuses { get; set; }
    }

    /// <summary>본사 진행 상태 — 열쇠·상태·시각 셋뿐. 담당자 등 본사 내부 값은 안 내려온다(#18·#22).</summary>
    public sealed class StatusDto
    {
        public string CsRequestId { get; set; } = "";
        public string? HqStatus { get; set; }
        public DateTime? ChangedAt { get; set; }
    }

    private sealed class ReplyDto
    {
        public string ReplyId { get; set; } = "";
        public string CsRequestId { get; set; } = "";
        public string? Body { get; set; }
        public string? RepliedByKind { get; set; }
        public DateTime RepliedAt { get; set; }
    }

    /// <summary>본사가 먼저 보낸 쪽지 — 받는 칸은 다섯뿐이다(본사 내부 식별자는 안 내려온다).
    /// <para>게이트가 이 모양으로 수신 사이클을 직접 돌린다 ⇒ public.</para></summary>
    public sealed class OutboundDto
    {
        public string OutboundId { get; set; } = "";
        public string? Purpose { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // ══════════════════════════════════════════════════════════════
    // 🔴 20261012작3 B-2·B-3 — 본사 선발신 수신
    //
    // 왜 `cs_requests` 에 앉히나 (설계 D-1)
    //   ERP 의 목록(`CsRequestController.cs:178`)과 쪽지함(`CsInboxPage.razor:133`)이
    //   **둘 다 `cs_requests` 기준**이다. 새 표를 만들면 화면 둘을 새로 짜야 하고
    //   흐름이 끊긴다(#20). 기존 자리에 앉히면 **화면 변경 없이** 그대로 보인다.
    //
    // 🔴 `origin='hq'` 로 **반드시 표시**한다(DB-144). 고객이 「내가 쓰지 않은 쪽지」를
    //   자기 문의와 구별해야 한다.
    // 🔴 멱등 = `outbound_id` 를 `cs_request_id` 로 **그대로** 쓴다(양쪽 varchar(36) · 실측).
    //   두 번 내려와도 `INSERT IGNORE` 로 1건이다.
    // 🔴 에코 없음 — `cs_outbox` 에 **넣지 않는다**(`cs_outbox` INSERT 는 `CsRequestController:141`
    //   한 곳뿐이다 · 실측). 본사발신이 본사로 되돌아가는 고리가 구조적으로 안 생긴다.
    // 🔴 ack 는 **저장에 성공한 것만** 보낸다. 못 보내면 `delivered_at` 이 안 채워져
    //   다음 주기에 또 내려오고, 멱등이 중복을 막는다 ⇒ **유실 0 · 중복 0**.
    // ══════════════════════════════════════════════════════════════
    public static async Task ReceiveOutboundAsync(MySqlConnection db, IHeadquartersClient hq,
        string ownerAccountId, List<OutboundDto> items, ILogger logger, CancellationToken st)
    {
        // 🔴 테넌트는 **부모계정 행에서** 꺼낸다 — 송신 경로가 이미 그 행으로 3중 재료를 모은다
        //   (같은 파일 위쪽 `is_parent = 1 AND is_active = 1`). 다른 데서 추측하면 축이 둘이 된다.
        var tenantId = await db.ExecuteScalarAsync<string?>(
            "SELECT tenant_id FROM users WHERE is_parent = 1 AND is_active = 1 LIMIT 1");

        if (string.IsNullOrEmpty(tenantId))
        {
            // 테넌트를 모르면 앉힐 수 없다 — 버리지 않고 다음 주기에 다시 받는다(ack 를 안 보낸다).
            logger.LogWarning("[CS수신] 선발신 보류 — 로컬 부모계정에서 tenant_id 확정 불가");
            return;
        }

        var saved = new List<string>();

        // 🔴 20261014작3 T-3 — 용도 칸(DB-146)이 **이 DB 에 있나**를 한 주기에 한 번 묻는다.
        //
        //   왜 캐시를 안 두나: 칸은 **업데이트 직후 마이그가 돌 때** 생긴다. 캐시를 두면
        //   그 사이에 「없다」로 굳은 판정이 남아 **칸이 생긴 뒤에도 안 적는** 창이 열린다
        //   (컨트롤러 쪽은 그래서 `ResetPurposeColumnCache` 같은 손잡이를 둬야 했다).
        //   이 사이클은 30초마다 돌고 내려올 쪽지가 있을 때만 여기 오므로, 한 줄 질의 한 번이
        //   그 위험을 아예 없애는 값으로 싸다.
        //
        //   🔴 칸이 없으면 **적지 않고 그대로 앉힌다** — 종전과 똑같이 돈다. 본문 머리 한 줄
        //   (`[업데이트공지사항] 제목`)이 그대로 있으므로 그 쪽지도 화면에서 종류를 읽을 수 있다.
        var hasPurpose = await db.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'cs_requests' AND COLUMN_NAME = 'purpose'") > 0;

        if (!hasPurpose)
            logger.LogInformation(
                "[CS수신] 용도 칸이 아직 없다(DB-146 미적용) — 종류를 적지 않고 받는다. 본문 머리 한 줄은 그대로다.");

        foreach (var o in items)
        {
            if (string.IsNullOrWhiteSpace(o.OutboundId) || string.IsNullOrWhiteSpace(o.Body)) continue;

            var title = (o.Title ?? "").Trim();
            // 🔴 용도·제목을 **본문 머리 한 줄**로 얹는다 — 칸을 새로 만들지 않는다.
            //   용도는 사람 말로 적는다 — 화면에 개발 용어 금지(#23). 낱말 정본은 `PurposeLabel` 하나다.
            //   🔴 20261014작2 — 여기에 낱말을 나열해 두면 표가 늘 때 이 주석만 낡는다(실제로 그랬다:
            //     「프로모션·이벤트·요금안내」 3개만 적혀 있었고 표는 6종이 됐다). 그래서 메서드를 가리킨다.
            var head = title.Length > 0 ? $"[{PurposeLabel(o.Purpose)}] {title}" : $"[{PurposeLabel(o.Purpose)}]";
            var shown = head + "\n" + o.Body;
            if (shown.Length > 2000)
            {
                // 🔴 [4] V-5 봉합 — **말없이 자르지 않는다.** 본사가 쓴 글이 고객 화면에서 끊기는데
                //   기록이 0건이면 나중에 「왜 중간에 끊겼나」를 아무도 못 찾는다.
                logger.LogWarning("[CS수신] 본사 안내가 길어 잘렸다 outbound={Id} 원본 {N}자 → 2000자",
                    o.OutboundId, shown.Length);
                shown = shown[..2000];
            }

            await using var tx = await db.BeginTransactionAsync(st);
            try
            {
                // ① 본사발신 원본 1줄 — 고객이 쓴 것이 아니므로 created_by 는 'headquarters' 표식이다.
                //   🔴 [4] V-7 봉합 — **영향 행수를 센다.** 0 이면 그 열쇠가 이미 있다는 뜻이고,
                //   그게 **고객 자기 문의**일 수도 있다(열쇠 공간이 한 표를 공유한다).
                //   그 상태에서 ②를 그냥 넣으면 **본사 글이 고객 문의의 답으로 붙는다** —
                //   조용한 오배치다. 그래서 ①의 결과를 보고 ②를 할지 정한다.
                // 🔴 20261014작3 T-3 — 용도를 **값으로** 적는다(낱말이 아니다).
                //   낱말로 적으면 사장님이 결-D 에서 ⓑ 를 고른 이유(「낱말을 바꿔도 필터가 안 깨진다」)가
                //   무너진다 — 실제로 작2 가 낱말 6종을 고쳤고, 그때 값으로 적어 둔 행은 멀쩡했다.
                //   칸이 없는 DB 에서는 칸 이름을 아예 넣지 않는다(1054 를 피한다).
                var inserted = await db.ExecuteAsync($@"
                    INSERT IGNORE INTO cs_requests
                        (cs_request_id, tenant_id, created_by, category, sub_tag, body,
                         screen_code, erp_version, status, origin, created_at{(hasPurpose ? ", purpose" : "")})
                    VALUES
                        (@Id, @TenantId, 'headquarters', 'etc', 'etc', @Body,
                         NULL, @Ver, '답변도착', 'hq', @CreatedAt{(hasPurpose ? ", @Purpose" : "")})",
                    new
                    {
                        Id = o.OutboundId, TenantId = tenantId, Body = shown,
                        Ver = "hq",   // erp_version 은 NOT NULL — 본사발신은 ERP 판이 없다는 표식
                        CreatedAt = o.CreatedAt == default ? DateTime.UtcNow : o.CreatedAt,
                        // 🔴 빈 문자열은 NULL 로 눕힌다 — 「골랐지만 비었다」와 「모른다」를 한 값으로 모은다.
                        Purpose = string.IsNullOrWhiteSpace(o.Purpose) ? null : o.Purpose!.Trim(),
                    }, tx);

                if (inserted == 0)
                {
                    // 이미 있는 행이 **본사발신인지** 확인한다. 아니면 남의 자리다 — 손대지 않는다.
                    var existingOrigin = await db.ExecuteScalarAsync<string?>(
                        "SELECT origin FROM cs_requests WHERE cs_request_id = @Id", new { Id = o.OutboundId }, tx);
                    if (!string.Equals(existingOrigin, "hq", StringComparison.Ordinal))
                    {
                        await tx.RollbackAsync(st);
                        logger.LogError(
                            "[CS수신] 🔴 선발신 열쇠가 **고객 문의와 충돌**한다 outbound={Id} 기존 origin={Origin} "
                          + "— 본사 글을 남의 문의에 붙이지 않고 건너뛴다(ack 도 안 보낸다). 본사 확인 필요.",
                            o.OutboundId, existingOrigin ?? "(없음)");
                        continue;   // ack 를 안 보내므로 사라지지 않는다 — 사람이 볼 때까지 남는다
                    }
                    // 본사발신 재수신 = 멱등 경로다(정상). 아래 ②도 멱등이라 그대로 진행한다.
                }

                // ② 같은 글을 답 한 줄로도 둔다 — 쪽지함이 답을 읽어 그리기 때문이다(화면 변경 0).
                await db.ExecuteAsync(@"
                    INSERT IGNORE INTO cs_replies (reply_id, tenant_id, cs_request_id, body, replied_by_kind, replied_at)
                    VALUES (@ReplyId, @TenantId, @Id, @Body, '본사', @At)",
                    new
                    {
                        ReplyId = o.OutboundId, TenantId = tenantId, Id = o.OutboundId, Body = shown,
                        At = o.CreatedAt == default ? DateTime.UtcNow : o.CreatedAt,
                    }, tx);

                await tx.CommitAsync(st);
                saved.Add(o.OutboundId);
            }
            catch (MySqlException ex)
            {
                // #15 — 삼키지 않는다. ack 를 안 보내므로 다음 주기에 다시 내려온다(유실 0).
                await tx.RollbackAsync(st);
                logger.LogWarning(ex, "[CS수신] 선발신 저장 실패 outbound={Id} — 다음 주기 재시도", o.OutboundId);
            }
        }

        if (saved.Count == 0) return;

        // 🔴 **저장이 끝난 뒤에만** 도착을 알린다 — 이 ack 가 본사 화면의 「전달됨」 근거다.
        var payload = "{\"outboundIds\":["
            + string.Join(",", saved.Select(id => JsonSerializer.Serialize(id))) + "]}";
        var (code, ackBody) = await hq.PostAsync("/api/backoffice/cs/outbound/ack", payload, ownerAccountId, st);
        if (code is >= 200 and < 300)
        {
            // 🔴 [4] V-6 봉합 — 2xx 만 보고 끝내지 않는다. 본사가 **몇 건을 받아들였나**를 읽는다.
            //   내려받은 뒤 본사가 취소·폐기했으면 ack 가 **no-op** 이 되고, 그러면
            //   본사는 「전달 안 됨」인데 고객은 보고 있다 — 두 쪽이 다른 사실을 들고 있게 된다.
            //   숫자가 어긋나면 로그로 드러낸다(사람이 볼 수 있어야 한다).
            var acked = -1;
            try
            {
                using var ad = JsonDocument.Parse(ackBody);
                if (ad.RootElement.TryGetProperty("acked", out var a) && a.TryGetInt32(out var n)) acked = n;
            }
            catch (JsonException ex)
            {
                // #15 — 삼키지 않는다. 저장은 이미 끝났으니 기능 영향은 0, 다만 숫자 대조만 못 한다.
                logger.LogWarning(ex, "[CS수신] 도착확인 응답을 못 읽었다 — 저장은 완료, 건수 대조만 생략");
            }

            if (acked >= 0 && acked != saved.Count)
                logger.LogWarning(
                    "[CS수신] ⚠️ 본사 안내 {N}건 저장했는데 본사가 {A}건만 전달됨으로 표시했다 "
                  + "— 내려온 뒤 본사가 거둬들인 건이 있을 수 있다(고객 화면에는 보인다). 본사 확인 필요.",
                    saved.Count, acked);
            else
                logger.LogInformation("[CS수신] 본사 안내 {N}건 수신·도착확인 완료", saved.Count);
        }
        else
        {
            logger.LogWarning("[CS수신] 본사 안내 {N}건 저장됐으나 도착확인 실패 code={Code} — 다음 주기에 재시도", saved.Count, code);
        }
    }

    // ══════════════════════════════════════════════════════════════

    // ══════════════════════════════════════════════════════════════
    // 🔴 20261012작4 B-2 — 본사 진행 상태를 고객 ERP 에 앉힌다 (사장님 실측 반려 ④)
    //
    // 사고: 백오피스에서 「처리중」·「완료」로 바꿔도 ERP 는 영원히 몰랐다. 버그가 아니라
    //   **하행 계약에 상태 칸이 없었다** — 상태를 받을 코드도 0건이었다(선행검증서 §2-4).
    //
    // 🔴 사장님 결재(10/12): **별도 칸**이다. `cs_requests.status`(접수·전송중·전송완료·
    //   답변도착·종결)는 **한 글자도 안 덮는다**(#1). 본사 진행은 `hq_status` 에만 쓴다 ⇒
    //   「답변도착」이 「완료」에 덮여 어느 쪽 사실인지 사라지는 일이 없다.
    // 🔴 멱등 — 같은 값을 다시 받아도 결과가 같다. Pull 은 14일 창을 매 주기 통째로 내려보낸다.
    // 🔴 뒤로 가지 않는다 — 더 이른 `changedAt` 은 무시한다. 응답 순서가 어긋나도
    //   「완료」가 「처리중」으로 되돌아가 보이지 않는다.
    // 🔴 칸이 없으면 **조용히 건너뛴다.** 오늘 사고가 「나중에 더한 칸을 조건 없이 읽어서」 났다 —
    //   마이그가 아직 안 돈 순간(업데이트 직후 한 번)에 답 수신까지 같이 죽이지 않는다.
    // ══════════════════════════════════════════════════════════════
    public static async Task ApplyHqStatusesAsync(MySqlConnection db, List<StatusDto> items,
        ILogger logger, CancellationToken st)
    {
        if (!await HasHqStatusColumnAsync(db))
        {
            logger.LogWarning("[CS수신] hq_status 칸 미적용 — 본사 상태 {N}건 보류(다음 주기 재시도)", items.Count);
            return;
        }

        var applied = 0;
        foreach (var s in items)
        {
            if (st.IsCancellationRequested) break;
            if (string.IsNullOrWhiteSpace(s.CsRequestId) || string.IsNullOrWhiteSpace(s.HqStatus)) continue;
            // 본사 어휘 그대로 보관하되 길이는 막는다(화면이 깨지지 않게).
            var status = s.HqStatus.Trim();
            if (status.Length > 10) continue;

            var n = await db.ExecuteAsync(@"
                UPDATE cs_requests
                   SET hq_status    = @Status,
                       hq_status_at = @ChangedAt
                 WHERE cs_request_id = @Id
                   AND (hq_status IS NULL
                     OR hq_status <> @Status
                     OR hq_status_at IS NULL)
                   AND (hq_status_at IS NULL
                     OR @ChangedAt IS NULL
                     OR @ChangedAt >= hq_status_at)",
                new { Id = s.CsRequestId, Status = status, ChangedAt = s.ChangedAt });

            if (n > 0) applied++;
        }

        if (applied > 0)
            logger.LogInformation("[CS수신] 본사 상태 {N}건 반영", applied);
    }

    /// <summary>`hq_status` 칸이 이 DB 에 실재하나. 업데이트 직후 마이그 전 1회를 위해 캐시하지 않는다.</summary>
    private static async Task<bool> HasHqStatusColumnAsync(MySqlConnection db)
    {
        var found = await db.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM information_schema.columns
             WHERE table_schema = DATABASE()
               AND table_name = 'cs_requests'
               AND column_name IN ('hq_status', 'hq_status_at')");
        return found == 2;
    }
    // 🔴 [4] V-1 봉합 — 본사가 폐기한 선발신의 **고객 사본 본문을 비운다**
    //
    // 왜: `PurgeOutbound` 는 전달 뒤에도 듣는다(설계대로 — 「관리 후 임의 폐기」). 그런데
    //   고객 사본을 회수할 길이 없으면 본사 화면엔 「폐기됨」이고 고객 쪽지함엔 **본문이 영구히 남는다.**
    //   작2 V-3(「대화의 절반만 비우면 폐기가 아니다」)가 시스템 경계를 넘어 재발한 자리다.
    //
    // 🔴 **본사발신(`origin='hq'`)만** 건드린다 — 고객이 쓴 문의는 본사가 지울 수 없다.
    // 🔴 행은 남긴다. 「본사가 거둬간 안내입니다」를 화면이 말할 수 있어야 한다(사실은 남긴다).
    // 멱등 — 이미 비운 것을 또 비워도 결과가 같다(`WHERE body IS NOT NULL`).
    // ══════════════════════════════════════════════════════════════
    public static async Task PurgeOutboundCopiesAsync(MySqlConnection db, List<string> ids,
        ILogger logger, CancellationToken st)
    {
        var keys = ids.Where(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 36).Take(50).ToList();
        if (keys.Count == 0) return;

        await using var tx = await db.BeginTransactionAsync(st);
        try
        {
            var n1 = await db.ExecuteAsync(@"
                UPDATE cs_requests
                   SET body = NULL
                 WHERE cs_request_id IN @Ids AND origin = 'hq' AND body IS NOT NULL",
                new { Ids = keys }, tx);

            var n2 = await db.ExecuteAsync(@"
                UPDATE cs_replies r
                   JOIN cs_requests q ON q.cs_request_id = r.cs_request_id
                   SET r.body = ''
                 WHERE r.cs_request_id IN @Ids AND q.origin = 'hq' AND r.body <> ''",
                new { Ids = keys }, tx);

            await tx.CommitAsync(st);

            if (n1 + n2 > 0)
                logger.LogInformation("[CS수신] 본사가 거둬간 안내 {N}건의 내용을 비웠다(쪽지 {A} · 답 {B})",
                    keys.Count, n1, n2);
        }
        catch (MySqlException ex)
        {
            await tx.RollbackAsync(st);
            // #15 — 삼키지 않는다. 다음 주기에 같은 열쇠가 또 내려와 재시도된다(멱등).
            logger.LogWarning(ex, "[CS수신] 본사 거둬감 반영 실패 {N}건 — 다음 주기 재시도", keys.Count);
        }
    }

    /// <summary>
    /// 용도를 사람 말로 — 화면에 개발 용어를 내지 않는다(#23).
    ///
    /// <para>🔴 20261014작2 T-1·T-2 — <b>본사발신 낱말 정합</b>(일정표 §3 순서 2 · M-2 + 결-A 흡수).
    /// 종전 표는 <c>promo·event·billing</c> 3값만 알았고, 백오피스가 실제로 허용하는 6값 중
    /// <c>etc·update·notice</c> 셋이 전부 <c>_ => "본사안내"</c> 로 뭉개졌다 ⇒ 고객 화면에
    /// 「업데이트공지사항」이 <b>「본사안내」로 보였다.</b> 10/13 백오피스 설계 §5-3 이 이 사실을
    /// 스스로 「ERP 쪽 문구 보강은 범위 밖 · 결-12」로 올려 뒀고, 이 차수가 그 결-12 를 닫는다.</para>
    ///
    /// <para>낱말 정본 = <c>docs/설계/백오피스/20261013_설계_고객지원센터_3화면_프론트재구성.md</c> §5-1.
    /// <c>billing</c> 만 <b>사장님 결-A 의 낱말</b>(「과금결재 안내」)을 쓴다 — §5-1 의 「(옛 값)」 꼬리표는
    /// <b>본사 직원이 보는 조회용</b>이고, 고객 쪽지 제목에 「옛 값」이 서면 그게 내부 용어 노출(#23)이다.</para>
    ///
    /// <para>🔴 <b>값은 하나도 지우지 않았다</b>(#1) — 옛 두 값(<c>event</c>·<c>billing</c>)의 분기가 그대로 있다.
    /// 이미 그 값으로 내려간 쪽지가 있고, 분기를 빼면 그 종류가 「본사안내」로 되돌아간다.</para>
    ///
    /// <para>🔴🔴 <b><c>_ => "본사안내"</c> 를 지우지 마라.</b> 안 쓰이는 줄처럼 보이지만 아니다.
    /// 백오피스가 <b>7번째 값</b>을 더하는 순간 이 <c>switch</c> 식은 맞는 분기가 없어
    /// <see cref="System.Runtime.CompilerServices.SwitchExpressionException"/> 을 던진다 ⇒ 수신 사이클이
    /// 죽고 <b>같은 응답에 실린 본사 답변까지 못 내려온다.</b> 10/12 사고(담은 응답이 죽으면 같이
    /// 실려야 할 것도 못 간다)와 <b>같은 모양</b>이다. 게이트 G-W10-3 이 이 자리를 지킨다.</para>
    ///
    /// <para>🔴 <b>옛 행은 이 표로 안 바뀐다.</b> 낱말은 수신할 때 본문 머리에 <b>글자로 박혀</b> 저장된다
    /// (<c>:495</c>). 이미 내려간 쪽지는 <c>[본사안내]</c> 그대로다 — 되돌아가 고쳐 적으면
    /// 「이미 보낸 글의 종류를 다른 이름으로 덮어 적는」 거짓말이고 #1 위반이다. 게이트 G-W10-4 가 지킨다.</para>
    /// </summary>
    /// <remarks>
    /// 🔴 20261014작3 T-4 — 접근성을 <c>private</c> → <c>internal</c> 로 **한 낱말** 올렸다.
    /// 목록 API 가 화면에 **낱말을 함께** 내려보내야 하는데(설계 §3-1), <c>HitPan.Web</c> 은
    /// 서버 코드를 참조하지 않아서(`HitPan.Contracts` 만) 화면이 자기 표를 들면 **낱말 사본이 둘**이 된다.
    /// 작2 가 바로 그 자리에서 어긋났다(본사 「요금 안내(옛 값)」 vs 고객 「과금결재 안내」).
    /// <para>🔴 <b>이 메서드를 다른 자리로 옮기지 마라.</b> 작2 게이트 <c>G-W10-1·7</c> 이
    /// <c>typeof(CsOutboxSenderWorker).GetMethod("PurposeLabel", NonPublic|Static)</c> 로 찾는다.
    /// 이름·클래스를 그대로 두고 접근성만 올렸으므로 그 리플렉션은 그대로 맞는다
    /// (<c>NonPublic</c> 은 <c>internal</c> 도 포함한다 — 작3 에서 8/8 로 확인했다).</para>
    /// </remarks>
    internal static string PurposeLabel(string? purpose) => purpose switch
    {
        // ── 사장님 낱말 4종 (10/13 설계 §5-1 ①~④) ──
        "update" => "업데이트공지사항",
        "notice" => "히트판 공지사항",
        "promo" => "프로모션 안내",
        "etc" => "기타 공지사항",

        // ── 옛 값 — 값·표시 모두 유지한다(#1). 고객 쪽에는 「(옛 값)」 꼬리표를 안 붙인다 ──
        "event" => "이벤트",
        "billing" => "과금결재 안내",   // 🔴 사장님 결-A · 결재거리 결-A′ (되돌리기 = 이 한 줄)

        // 🔴 지우지 마라 — 위 설명 참조. 모르는 값이 와도 수신 사이클이 죽지 않게 하는 안전망이다.
        _ => "본사안내",
    };

}
