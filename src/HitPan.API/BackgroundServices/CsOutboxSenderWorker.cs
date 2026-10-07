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
                    await SendPendingAsync(db, hq, ownerAccountId, _logger, st);

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
        try
        {
            var doc = JsonSerializer.Deserialize<PullResponse>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            replies = doc?.Replies;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "[CS수신] Pull 응답 해석 실패 — 버리지 않고 다음 주기 재시도");
            return;
        }
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
        return new MySqlConnection(
            $"Server={host};Port={port};Database={name};Uid={user};Pwd={pass};CharSet=utf8mb4;AllowUserVariables=true");
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

    private sealed class PullResponse { public List<ReplyDto>? Replies { get; set; } }

    private sealed class ReplyDto
    {
        public string ReplyId { get; set; } = "";
        public string CsRequestId { get; set; } = "";
        public string? Body { get; set; }
        public string? RepliedByKind { get; set; }
        public DateTime RepliedAt { get; set; }
    }
}
