using Dapper;
using MySqlConnector;

namespace HitPan.API.HostedServices;

// 정합성 자동감지 백그라운드 서비스
//   - 6시간 주기로 전 테넌트 데이터 이상 감지
//   - 이상 발견 시 audit_trail에 'integrity_alert' 기록
//   - 감지 항목: ① 재고 음수 ② monthly_summary 불일치
//   - IdempotencyCleanupService 동일 패턴 (자체 커넥션, 싱글톤)
public sealed class IntegrityCheckService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(10);

    private readonly ILogger<IntegrityCheckService> _logger;

    public IntegrityCheckService(ILogger<IntegrityCheckService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connStr = BuildConnectionString();
        if (string.IsNullOrEmpty(connStr))
        {
            _logger.LogWarning("IntegrityCheckService: DB 환경변수 누락 → 정합성 감지 비활성");
            return;
        }

        // 시작 후 1분 대기 (API 완전 기동 이후)
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunChecksAsync(connStr, stoppingToken);

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task RunChecksAsync(string connStr, CancellationToken ct)
    {
        try
        {
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync(ct);

            // ① 재고 음수 감지
            var negativeStocks = await conn.QueryAsync<(string TenantId, string ItemId, decimal Qty)>(
                new CommandDefinition(
                    """
                    SELECT tenant_id AS TenantId, item_id AS ItemId, current_qty AS Qty
                    FROM item_stock
                    WHERE current_qty < 0
                    LIMIT 100
                    """, cancellationToken: ct));

            foreach (var row in negativeStocks)
            {
                _logger.LogWarning("[정합성] 재고 음수 — tenant={TenantId}, item={ItemId}, qty={Qty}",
                    row.TenantId, row.ItemId, row.Qty);
                await WriteAlertAsync(conn,
                    row.TenantId, "item_stock", row.ItemId,
                    $"재고 음수: {row.Qty}", ct);
            }

            // ② monthly_summary vs journal_lines 매출 불일치 감지 (당월)
            var ym = DateTime.UtcNow.ToString("yyyy-MM");
            var mismatches = await conn.QueryAsync<(string TenantId, decimal SummaryAmt, decimal JournalAmt)>(
                new CommandDefinition(
                    """
                    SELECT ms.tenant_id AS TenantId,
                           ms.total_sales AS SummaryAmt,
                           COALESCE(SUM(jl.credit), 0) AS JournalAmt
                    FROM monthly_summary ms
                    LEFT JOIN journal_lines jl
                        ON jl.tenant_id = ms.tenant_id
                        AND DATE_FORMAT(jl.entry_date, '%Y-%m') = ms.ym
                        AND jl.account_code = '4010'
                    WHERE ms.ym = @Ym
                    GROUP BY ms.tenant_id, ms.total_sales
                    HAVING ABS(ms.total_sales - COALESCE(SUM(jl.credit), 0)) > 1
                    LIMIT 50
                    """,
                    new { Ym = ym },
                    cancellationToken: ct));

            foreach (var row in mismatches)
            {
                _logger.LogWarning(
                    "[정합성] monthly_summary 매출 불일치 — tenant={TenantId}, summary={SummaryAmt}, journal={JournalAmt}",
                    row.TenantId, row.SummaryAmt, row.JournalAmt);
                await WriteAlertAsync(conn,
                    row.TenantId, "monthly_summary", ym,
                    $"매출 불일치: summary={row.SummaryAmt}, journal={row.JournalAmt}", ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IntegrityCheckService: 정합성 감지 실패 (다음 주기 재시도)");
        }
    }

    private static async Task WriteAlertAsync(
        MySqlConnection conn,
        string tenantId, string entityType, string entityId,
        string reason, CancellationToken ct)
    {
        try
        {
            await conn.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO audit_trail
                      (log_id, tenant_id, user_id, action_type, entity_type, entity_id, reason, created_at)
                    VALUES
                      (UUID(), @TenantId, 'system', 'integrity_alert', @EntityType, @EntityId, @Reason, NOW(6))
                    """,
                    new { TenantId = tenantId, EntityType = entityType, EntityId = entityId, Reason = reason },
                    cancellationToken: ct));
        }
        catch
        {
            // audit_trail 기록 실패는 감지 흐름 자체를 멈추지 않는다
        }
    }

    private static string BuildConnectionString()
    {
        var host = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("DB_PORT") ?? "3306";
        var db = Environment.GetEnvironmentVariable("DB_NAME");
        var user = Environment.GetEnvironmentVariable("DB_USER");
        var pwd = Environment.GetEnvironmentVariable("DB_PASSWORD");
        if (string.IsNullOrEmpty(db) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pwd))
            return string.Empty;
        return $"Server={host};Port={port};Database={db};User={user};Password={pwd};DefaultCommandTimeout=30;";
    }
}
