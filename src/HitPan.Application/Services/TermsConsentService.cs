using Dapper;
using HitPan.Application.DTOs.Auth;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

// 첫 로그인 약관 4건 강제 동의 (헌법 #24 책임 분산 + 전자상거래법 정합)
// INSERT ONLY — 버전 변경 시 신규 row (절대원칙 #3)
public class TermsConsentService : ITermsConsentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TermsConsentService> _logger;

    public TermsConsentService(IUnitOfWork unitOfWork, ILogger<TermsConsentService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<TermsConsentResponse> ConsentAsync(
        string tenantId,
        string userId,
        string clientIp,
        string? userAgent,
        TermsConsentRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("tenant_id required", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("user_id required", nameof(userId));

        // 필수 4건 검증 — 미동의 시 400
        if (!request.AgreeService || !request.AgreePrivacy
            || !request.AgreeSubscription || !request.AgreeDataOwnership)
        {
            throw new InvalidOperationException("필수 약관 4건에 모두 동의해야 합니다 (서비스·개인정보·구독·데이터주권)");
        }

        var consentId = Guid.NewGuid().ToString();
        var agreedAt = DateTime.UtcNow;

        var db = _unitOfWork.GetDbConnection();
        var cmd = new CommandDefinition(@"
            INSERT INTO user_terms_consent
              (consent_id, tenant_id, user_id, terms_version,
               agree_service, agree_privacy, agree_subscription, agree_data_ownership, agree_marketing,
               agreed_at, client_ip, user_agent)
            VALUES
              (@ConsentId, @TenantId, @UserId, @TermsVersion,
               @AgreeService, @AgreePrivacy, @AgreeSubscription, @AgreeDataOwnership, @AgreeMarketing,
               @AgreedAt, @ClientIp, @UserAgent)",
            new
            {
                ConsentId = consentId,
                TenantId = tenantId,
                UserId = userId,
                request.TermsVersion,
                AgreeService = request.AgreeService ? 1 : 0,
                AgreePrivacy = request.AgreePrivacy ? 1 : 0,
                AgreeSubscription = request.AgreeSubscription ? 1 : 0,
                AgreeDataOwnership = request.AgreeDataOwnership ? 1 : 0,
                AgreeMarketing = request.AgreeMarketing.HasValue ? (request.AgreeMarketing.Value ? 1 : 0) : (int?)null,
                AgreedAt = agreedAt,
                ClientIp = clientIp ?? string.Empty,
                UserAgent = userAgent
            },
            cancellationToken: ct);

        await db.ExecuteAsync(cmd);

        _logger.LogInformation("Terms consent recorded: tenant={Tenant} user={User} version={Version}",
            tenantId, userId, request.TermsVersion);

        return new TermsConsentResponse
        {
            ConsentId = consentId,
            AgreedAt = agreedAt,
            ClientIp = clientIp ?? string.Empty,
            TermsVersion = request.TermsVersion
        };
    }

    public async Task<TermsConsentStatus> GetStatusAsync(
        string tenantId,
        string userId,
        string requiredVersion,
        CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var cmd = new CommandDefinition(@"
            SELECT terms_version AS TermsVersion, agreed_at AS AgreedAt
            FROM user_terms_consent
            WHERE tenant_id = @TenantId
              AND user_id = @UserId
              AND terms_version = @Version
              AND agree_service = 1
              AND agree_privacy = 1
              AND agree_subscription = 1
              AND agree_data_ownership = 1
            ORDER BY agreed_at DESC
            LIMIT 1",
            new { TenantId = tenantId, UserId = userId, Version = requiredVersion },
            cancellationToken: ct);

        var row = await db.QueryFirstOrDefaultAsync<ConsentRow>(cmd);

        if (row is null)
        {
            return new TermsConsentStatus { HasAgreed = false };
        }

        return new TermsConsentStatus
        {
            HasAgreed = true,
            TermsVersion = row.TermsVersion,
            AgreedAt = row.AgreedAt
        };
    }

    public async Task<bool> HasAgreedAsync(string tenantId, string userId, string requiredVersion, CancellationToken ct = default)
    {
        var status = await GetStatusAsync(tenantId, userId, requiredVersion, ct);
        return status.HasAgreed;
    }

    private sealed class ConsentRow
    {
        public string TermsVersion { get; set; } = string.Empty;
        public DateTime AgreedAt { get; set; }
    }
}
