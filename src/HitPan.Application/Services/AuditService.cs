using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>도메인 감사로그 구현 — audit_trail (DB-16) INSERT</summary>
public sealed class AuditService : IAuditService
{
    private readonly IDbConnection _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<AuditService> _logger;

    // MariaDB: 1146 = Table doesn't exist — DB-16 미실행 환경에서 예외 대신 스킵
    private const int MariaDbTableNotFoundCode = 1146;

    public AuditService(IDbConnection db, ICurrentTenant currentTenant, ILogger<AuditService> logger)
    {
        _db = db;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    public async Task LogAsync(
        string actionType,
        string entityType,
        string? entityId = null,
        string? beforeJson = null,
        string? afterJson = null,
        string? reason = null,
        IDbTransaction? tx = null,
        CancellationToken ct = default)
    {
        // 테넌트 컨텍스트 없는 백그라운드 호출은 스킵 (배치 등)
        if (string.IsNullOrEmpty(_currentTenant.TenantId)) return;

        try
        {
            await EnsureOpenAsync(ct).ConfigureAwait(false);

            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO audit_trail
                  (log_id, tenant_id, user_id, action_type, entity_type, entity_id,
                   before_value, after_value, reason)
                VALUES
                  (UUID(), @TenantId, @UserId, @ActionType, @EntityType, @EntityId,
                   @Before, @After, @Reason)
                """,
                new
                {
                    TenantId = _currentTenant.TenantId,
                    UserId = string.IsNullOrEmpty(_currentTenant.UserId) ? null : _currentTenant.UserId,
                    ActionType = actionType,
                    EntityType = entityType,
                    EntityId = entityId,
                    Before = beforeJson,
                    After = afterJson,
                    Reason = reason
                }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        }
        catch (DbException ex) when (ex.ErrorCode == MariaDbTableNotFoundCode
                                     || ex.Message.Contains("audit_trail", StringComparison.OrdinalIgnoreCase)
                                        && ex.Message.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase))
        {
            // DB-16 미실행 환경 — 로그만 남기고 통과 (주 로직 보호)
            _logger.LogWarning(
                "audit_trail 테이블 미존재 — 감사로그 스킵. DB-16 마이그레이션 실행 필요. Entity={Entity} Action={Action}",
                entityType, actionType);
        }
        catch (Exception ex)
        {
            // 기타 예외는 주 로직 보호를 위해 삼키되 경고 로그 남김
            _logger.LogError(ex,
                "감사로그 INSERT 실패 — 주 로직은 계속 진행. Entity={Entity} Action={Action}",
                entityType, actionType);
        }
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection dbConn)
        {
            await dbConn.OpenAsync(ct).ConfigureAwait(false);
            return;
        }
        _db.Open();
    }
}
