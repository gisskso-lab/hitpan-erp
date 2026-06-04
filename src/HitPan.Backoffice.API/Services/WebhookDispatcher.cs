using System.Net.Http.Headers;
using System.Text;
using Dapper;
using MySqlConnector;

namespace HitPan.Backoffice.API.Services;

// webhook 재시도 큐 발송 (사장님 결재 2026-06-04, W10)
//
// 1분 주기:
//   - status='pending' AND (next_retry_at IS NULL OR next_retry_at <= NOW())
//   - HTTP POST → 2xx 시 status='sent'
//   - 실패 시 retry_count++, next_retry_at = 지수 백오프 (1m, 5m, 15m, 1h, 6h)
//   - 5회 초과 시 status='dead' (Owner 영역 수동 재발송)
//
// 헌법 #20 — 발송 실패해도 INSERT 보존, 끊김 0
public class WebhookDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebhookDispatcher> _logger;
    private readonly IHttpClientFactory _httpFactory;

    private static readonly TimeSpan[] BackoffSteps =
    {
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6)
    };
    private const int MaxRetry = 5;

    public WebhookDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<WebhookDispatcher> logger,
        IHttpClientFactory httpFactory)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _httpFactory = httpFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[WebhookDispatcher] 가동");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WebhookDispatcher] 주기 처리 예외");
            }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var cs = config.GetConnectionString("BackofficeDb")
                 ?? config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(cs)) return;

        await using var db = new MySqlConnection(cs);
        await db.OpenAsync(ct);

        var pending = (await db.QueryAsync<OutboxRow>(@"
            SELECT
                outbox_id AS OutboxId,
                tenant_id AS TenantId,
                event_type AS EventType,
                target_url AS TargetUrl,
                payload_json AS PayloadJson,
                signature AS Signature,
                nonce AS Nonce,
                retry_count AS RetryCount
            FROM webhook_outbox
            WHERE status = 'pending'
              AND (next_retry_at IS NULL OR next_retry_at <= NOW(6))
            ORDER BY outbox_id ASC
            LIMIT 50")).ToList();

        if (pending.Count == 0) return;

        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(15);

        foreach (var row in pending)
        {
            await DeliverAsync(db, http, row, ct);
        }
    }

    private async Task DeliverAsync(MySqlConnection db, HttpClient http, OutboxRow row, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(row.PayloadJson, Encoding.UTF8, "application/json");
            using var req = new HttpRequestMessage(HttpMethod.Post, row.TargetUrl) { Content = content };
            req.Headers.TryAddWithoutValidation("X-Hitpan-Signature", row.Signature);
            req.Headers.TryAddWithoutValidation("X-Hitpan-Nonce", row.Nonce);
            req.Headers.TryAddWithoutValidation("X-Hitpan-Event", row.EventType);

            using var res = await http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode)
            {
                await db.ExecuteAsync(@"
                    UPDATE webhook_outbox
                    SET status = 'sent', sent_at = NOW(6), last_error = NULL
                    WHERE outbox_id = @Id",
                    new { Id = row.OutboxId });
                _logger.LogInformation("[Webhook] 발송 OK outbox_id={Id} tenant={Tid}",
                    row.OutboxId, row.TenantId);
                return;
            }

            var body = await SafeReadAsync(res);
            await MarkFailureAsync(db, row, $"HTTP {(int)res.StatusCode} {body}");
        }
        catch (Exception ex)
        {
            await MarkFailureAsync(db, row, ex.Message);
        }
    }

    private async Task MarkFailureAsync(MySqlConnection db, OutboxRow row, string error)
    {
        var newRetry = row.RetryCount + 1;
        if (newRetry > MaxRetry)
        {
            await db.ExecuteAsync(@"
                UPDATE webhook_outbox
                SET status = 'dead', retry_count = @Retry, last_error = @Err
                WHERE outbox_id = @Id",
                new { row.OutboxId, Retry = newRetry, Err = Truncate(error, 500) });
            _logger.LogWarning("[Webhook] 발송 dead (최대 재시도 초과) outbox_id={Id} err={Err}",
                row.OutboxId, error);
            return;
        }

        var step = BackoffSteps[Math.Min(newRetry - 1, BackoffSteps.Length - 1)];
        await db.ExecuteAsync(@"
            UPDATE webhook_outbox
            SET retry_count = @Retry,
                last_error = @Err,
                next_retry_at = DATE_ADD(NOW(6), INTERVAL @Sec SECOND)
            WHERE outbox_id = @Id",
            new
            {
                row.OutboxId,
                Retry = newRetry,
                Err = Truncate(error, 500),
                Sec = (int)step.TotalSeconds
            });
        _logger.LogInformation("[Webhook] 발송 실패 → 재시도 예약 outbox_id={Id} retry={Retry} after={Step}",
            row.OutboxId, newRetry, step);
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage res)
    {
        try { return await res.Content.ReadAsStringAsync(); }
        catch { return ""; }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max));

    private class OutboxRow
    {
        public long OutboxId { get; set; }
        public string TenantId { get; set; } = "";
        public string EventType { get; set; } = "";
        public string TargetUrl { get; set; } = "";
        public string PayloadJson { get; set; } = "";
        public string Signature { get; set; } = "";
        public string Nonce { get; set; } = "";
        public int RetryCount { get; set; }
    }
}
