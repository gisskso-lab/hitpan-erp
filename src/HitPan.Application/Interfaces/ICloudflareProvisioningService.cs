namespace HitPan.Application.Interfaces;

/// <summary>
/// 프로비저닝 (회원가입 → DNS·터널·라이선스·이메일 자동) 스켈레톤
/// 사장님 결재 2026-06-01 / 헌법 #18·#29 정합
///
/// 본 인터페이스는 본사 서버에서만 호출. 고객 PC 절대 호출 금지 (헌법 #18).
/// 실제 Cloudflare API 호출은 후속 작지에서 구현 박제.
/// 현재는 스켈레톤 + 정책 명세.
/// </summary>
public interface ICloudflareProvisioningService
{
    /// <summary>
    /// 가입 후 자동 트리거: DNS 생성 + 터널 발급 + credentials 발급
    /// </summary>
    Task<ProvisioningResult> ProvisionAsync(string tenantId, string subdomain, CancellationToken ct = default);

    /// <summary>
    /// 상태 조회 (백오피스 화면용)
    /// </summary>
    Task<ProvisioningStatus> GetStatusAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// 실패 시 재시도
    /// </summary>
    Task<ProvisioningResult> RetryAsync(string tenantId, CancellationToken ct = default);
}

public record ProvisioningResult(
    string TenantId,
    bool Success,
    string? FullDomain,
    string? TunnelId,
    string? ErrorMessage
);

public enum ProvisioningStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3
}
