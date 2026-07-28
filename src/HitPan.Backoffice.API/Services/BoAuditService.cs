using System.Text.Json;
using Dapper;
using MySqlConnector;

namespace HitPan.Backoffice.API.Services;

// 백오피스 감사로그 (사장님 결재 2026-06-04, W11)
//
// INSERT ONLY — UPDATE/DELETE 금지 (헌법 #3 정신)
// 모든 Owner 액션 기록: bo_users CRUD, 승인 결재, 위험 조작
public interface IBoAuditService
{
    Task LogAsync(string actorUserId, string actorEmail, string actorRole, string action,
        string? targetType = null, string? targetId = null, object? detail = null,
        string? ip = null, string? userAgent = null, CancellationToken ct = default);
}

public class BoAuditService : IBoAuditService
{
    private readonly IConfiguration _config;
    private readonly ILogger<BoAuditService> _logger;

    public BoAuditService(IConfiguration config, ILogger<BoAuditService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task LogAsync(string actorUserId, string actorEmail, string actorRole, string action,
        string? targetType = null, string? targetId = null, object? detail = null,
        string? ip = null, string? userAgent = null, CancellationToken ct = default)
    {
        try
        {
            var detailJson = detail is null ? null : JsonSerializer.Serialize(detail);
            var cs = _config.GetConnectionString("BackofficeDb")
                     ?? _config.GetConnectionString("Default")
                     ?? throw new InvalidOperationException("ConnectionStrings:BackofficeDb 미설정");
            await using var db = new MySqlConnection(cs);
            await db.OpenAsync(ct);
            await db.ExecuteAsync(@"
                INSERT INTO bo_audit_log
                    (actor_user_id, actor_email, actor_role, action,
                     target_type, target_id, detail_json, ip_address, user_agent, created_at)
                VALUES
                    (@ActorUserId, @ActorEmail, @ActorRole, @Action,
                     @TargetType, @TargetId, @DetailJson, @Ip, @UserAgent, NOW(6))",
                new { ActorUserId = actorUserId, ActorEmail = actorEmail, ActorRole = actorRole,
                      Action = action, TargetType = targetType, TargetId = targetId,
                      DetailJson = detailJson, Ip = ip, UserAgent = userAgent });
        }
        catch (Exception ex)
        {
            // 감사로그 실패해도 본 트랜잭션은 보호 (헌법 #15 — 로그만)
            _logger.LogError(ex, "[BoAudit] 감사로그 INSERT 실패 action={Action}", action);
        }
    }
}
