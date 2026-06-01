namespace HitPan.Application.Interfaces;

/// <summary>
/// 백오피스 Pull 동기화 전용 토큰 서비스
/// 사장님 결재 2026-06-01 / 헌법 #5·#7·#18·#23 정합
/// </summary>
public interface ISyncTokenService
{
    /// <summary>
    /// Sync 토큰 발급 — 24시간 만료, 읽기 전용, 발급 시 이전 토큰 즉시 무효화 (회전)
    /// </summary>
    Task<SyncTokenResult> IssueAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// 토큰 검증 — 유효한 tenant_id 반환, 만료/무효 시 null
    /// </summary>
    Task<string?> ValidateAsync(string token, CancellationToken ct = default);
}

public record SyncTokenResult(string Token, DateTime ExpiresAt);
