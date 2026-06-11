using Dapper;
using MySqlConnector;

namespace HitPan.API.BackgroundServices;

/// <summary>
/// 본사 ERP 측 Outbox 폴러 워커 (WS-20260601-20).
///
/// 동작
///   - 5분 주기 (Outbox:PollIntervalMinutes, 기본 5) 로 백오피스 messaging_outbox SELECT.
///   - processed_at IS NULL ORDER BY occurred_at LIMIT 100 (idx_outbox_unprocessed 활용).
///   - 처리 성공 시 processed_at = NOW(6) 갱신 (INSERT ONLY 원장 외 메타 영역 갱신 — 헌법 #3 정합).
///   - 처리 실패 시 retry_count++, last_error 저장 (헌법 #15 빈 catch 금지).
///
/// 절대 원칙
///   - 단방향: 백오피스(클라우드) → 본사 ERP(로컬) Push 만 받는다. 본사 ERP → 백오피스 INSERT 금지.
///   - Pull 0: 본사 ERP 는 백오피스 outbox 만 SELECT. 백오피스 가 본사 ERP DB 를 SELECT 하는 코드 절대 금지.
///   - 헌법 #16: 단일 MySqlConnection (Task.WhenAll 금지).
///   - 설정 누락 시 자동 비활성 (IdempotencyCleanupService 동일 패턴).
///
/// appsettings.json
///   "Outbox": {
///     "Enabled": false,
///     "PollIntervalMinutes": 5,
///     "BackofficeConnectionString": "Server=...;Database=hitpan_backoffice;..."
///   }
/// </summary>
public sealed class OutboxPollerWorker : BackgroundService
{
    private readonly ILogger<OutboxPollerWorker> _logger;
    private readonly IConfiguration _config;

    public OutboxPollerWorker(
        ILogger<OutboxPollerWorker> logger,
        IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _config.GetValue("Outbox:Enabled", false);
        if (!enabled)
        {
            _logger.LogInformation("OutboxPollerWorker 비활성 (Outbox:Enabled=false)");
            return;
        }

        var connStr = _config["Outbox:BackofficeConnectionString"];
        if (string.IsNullOrWhiteSpace(connStr))
        {
            _logger.LogWarning("OutboxPollerWorker: Outbox:BackofficeConnectionString 누락 → 폴러 비활성");
            return;
        }

        var intervalMinutes = _config.GetValue("Outbox:PollIntervalMinutes", 5);
        if (intervalMinutes < 1) intervalMinutes = 5;
        var interval = TimeSpan.FromMinutes(intervalMinutes);

        // 시작 후 1분 대기 (API 완전 기동 이후, IntegrityCheckService 동일 패턴).
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (TaskCanceledException) { return; }

        _logger.LogInformation("OutboxPollerWorker 시작 (interval={Minutes}분)", intervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            await PollOnceAsync(connStr, stoppingToken);

            try { await Task.Delay(interval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task PollOnceAsync(string connStr, CancellationToken ct)
    {
        // 헌법 #16: 단일 MySqlConnection (Task.WhenAll 금지).
        try
        {
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync(ct);

            const string selectSql = @"
SELECT outbox_id, event_type, target_serial, payload, occurred_at, retry_count
FROM messaging_outbox
WHERE processed_at IS NULL
ORDER BY occurred_at ASC
LIMIT 100;";

            var rows = (await conn.QueryAsync<OutboxRow>(
                new CommandDefinition(selectSql, cancellationToken: ct))).ToList();

            if (rows.Count == 0)
            {
                return;
            }

            _logger.LogInformation("Outbox 미처리 {Count}건 수신", rows.Count);

            foreach (var row in rows)
            {
                if (ct.IsCancellationRequested) break;
                await ProcessOneAsync(conn, row, ct);
            }
        }
        catch (Exception ex)
        {
            // 헌법 #15: 빈 catch 금지. 다음 주기 재시도.
            _logger.LogWarning(ex, "OutboxPollerWorker 폴 실패 (다음 주기 재시도)");
        }
    }

    private async Task ProcessOneAsync(MySqlConnection conn, OutboxRow row, CancellationToken ct)
    {
        try
        {
            // 본사 ERP 측 핸들러 (현재는 로그 저장만, 실제 거래처 자동 등록은 본사 회계팀 수동 — WS-20260601-20).
            // 향후 핸들러 디스패처 도입 시 event_type 별 Strategy 패턴 확장.
            _logger.LogInformation(
                "Outbox 수신: outbox_id={OutboxId} event={EventType} serial={Serial} occurred_at={OccurredAt}",
                row.outbox_id, row.event_type, row.target_serial, row.occurred_at);

            const string updateSql = @"
UPDATE messaging_outbox
SET processed_at = NOW(6)
WHERE outbox_id = @OutboxId AND processed_at IS NULL;";

            await conn.ExecuteAsync(new CommandDefinition(
                updateSql,
                new { OutboxId = row.outbox_id },
                cancellationToken: ct));
        }
        catch (Exception ex)
        {
            // 헌법 #15: 빈 catch 금지. retry_count++ + last_error 저장.
            _logger.LogWarning(ex,
                "Outbox 처리 실패: outbox_id={OutboxId} event={EventType} retry={Retry}",
                row.outbox_id, row.event_type, row.retry_count);

            const string failSql = @"
UPDATE messaging_outbox
SET retry_count = retry_count + 1,
    last_error  = LEFT(@LastError, 500)
WHERE outbox_id = @OutboxId AND processed_at IS NULL;";

            try
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    failSql,
                    new { OutboxId = row.outbox_id, LastError = ex.Message },
                    cancellationToken: ct));
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx,
                    "Outbox 실패 저장도 실패: outbox_id={OutboxId}", row.outbox_id);
            }
        }
    }

    // Dapper 매핑용 행 (소문자 컬럼명 그대로).
    private sealed class OutboxRow
    {
        public long outbox_id { get; set; }
        public string event_type { get; set; } = string.Empty;
        public string target_serial { get; set; } = string.Empty;
        public string payload { get; set; } = string.Empty;
        public DateTime occurred_at { get; set; }
        public int retry_count { get; set; }
    }
}
