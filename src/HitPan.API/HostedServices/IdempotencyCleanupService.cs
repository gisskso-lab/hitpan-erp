using System.Data;
using Dapper;
using HitPan.Contracts.Idempotency;
using HitPan.Infrastructure.Configuration;
using MySqlConnector;

namespace HitPan.API.HostedServices;

// idempotency_keys 만료 정리 백그라운드 서비스 (작업지시서 20260425작4 §2.2 B-4)
//   - 기본 1h 주기 (IdempotencyConstants.CleanupInterval)
//   - DELETE WHERE expires_at < NOW(6)
//   - Scoped 의존성 회피 위해 자체 커넥션 생성 (싱글톤 호스티드 서비스 패턴)
public sealed class IdempotencyCleanupService : BackgroundService
{
    private readonly ILogger<IdempotencyCleanupService> _logger;

    public IdempotencyCleanupService(ILogger<IdempotencyCleanupService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connStr = BuildConnectionString();
        if (string.IsNullOrEmpty(connStr))
        {
            _logger.LogWarning("IdempotencyCleanupService: DB 환경변수 누락 → 정리 작업 비활성");
            return;
        }

        // 시작 즉시 1회 + 이후 주기적
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync(stoppingToken);
                var deleted = await conn.ExecuteAsync(
                    "DELETE FROM idempotency_keys WHERE expires_at < NOW(6) LIMIT 10000");
                if (deleted > 0)
                {
                    _logger.LogInformation("Idempotency 정리: {Count}건 만료 삭제", deleted);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Idempotency 정리 실패 (다음 주기 재시도)");
            }

            try
            {
                await Task.Delay(IdempotencyConstants.CleanupInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private static string BuildConnectionString()
    {
        // 사고 #46 봉합 (WS-20260612-01 2026-06-12): TenantConfigReader 영역 db.conf 영역
        var host = TenantConfigReader.Get("DB_HOST") ?? "localhost";
        var port = TenantConfigReader.Get("DB_PORT") ?? "3306";
        var db = TenantConfigReader.Get("DB_NAME");
        var user = TenantConfigReader.Get("DB_USER");
        var pwd = TenantConfigReader.Get("DB_PASSWORD");
        if (string.IsNullOrEmpty(db) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pwd))
        {
            return string.Empty;
        }
        // GuidFormat=None — char(36) 을 Guid 로 돌려주면 string DTO 매핑이 터진다 (봉합 2026-08-12, PI-07).
        return $"Server={host};Port={port};Database={db};User={user};Password={pwd};DefaultCommandTimeout=30;GuidFormat=None;";
    }
}
