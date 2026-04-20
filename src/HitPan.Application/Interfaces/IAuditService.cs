using System.Data;

namespace HitPan.Application.Interfaces;

/// <summary>도메인 감사로그 — audit_trail 테이블에 비즈니스 이벤트 기록</summary>
public interface IAuditService
{
    Task LogAsync(
        string actionType,
        string entityType,
        string? entityId = null,
        string? beforeJson = null,
        string? afterJson = null,
        string? reason = null,
        IDbTransaction? tx = null,
        CancellationToken ct = default);
}
