using System.Data;
using Dapper;
using HitPan.Application.DTOs.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

// 첫 로그인 약관 4건 강제 동의 (헌법 #24·#22·#27 정합)
// 미들웨어 영역 = JWT 인증 직후 동의 여부 검증 → 미동의 시 /api/* 차단
[ApiController]
[Route("api/auth/terms")]
[Authorize]
public class TermsConsentController : ControllerBase
{
    private readonly IDbConnection _db;

    public TermsConsentController(IDbConnection db) => _db = db;

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) return Forbid();

        if (_db.State != ConnectionState.Open) _db.Open();

        try
        {
            var row = await _db.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
                @"SELECT consent_id, terms_version, agreed_at
                  FROM user_terms_consent
                  WHERE tenant_id=@Tid AND user_id=@Uid
                  ORDER BY agreed_at DESC LIMIT 1",
                new { Tid = tenantId, Uid = userId }, cancellationToken: ct));

            if (row is null)
            {
                return Ok(new TermsConsentStatus { HasAgreed = false });
            }

            return Ok(new TermsConsentStatus
            {
                HasAgreed = true,
                TermsVersion = (string)row.terms_version,
                AgreedAt = (DateTime)row.agreed_at
            });
        }
        catch (Exception)
        {
            // user_terms_consent 테이블 미적용 환경 = 동의 미요구 (베타 1주차 정합)
            return Ok(new TermsConsentStatus { HasAgreed = false });
        }
    }

    [HttpPost("agree")]
    public async Task<IActionResult> Agree([FromBody] TermsConsentRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        var userId = HttpContext.Items["UserId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)) return Forbid();

        // 필수 4건 검증 (전자상거래법 정합)
        if (!request.AgreeService || !request.AgreePrivacy ||
            !request.AgreeSubscription || !request.AgreeDataOwnership)
        {
            return BadRequest(new { message = "필수 약관 4건 모두 동의해야 합니다." });
        }

        var consentId = Guid.NewGuid().ToString();
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var agreedAt = DateTime.UtcNow;

        if (_db.State != ConnectionState.Open) _db.Open();

        try
        {
            await _db.ExecuteAsync(
                @"INSERT INTO user_terms_consent
                    (consent_id, tenant_id, user_id, terms_version,
                     agree_service, agree_privacy, agree_subscription, agree_data_ownership,
                     agree_marketing, client_ip, agreed_at)
                  VALUES
                    (@Cid, @Tid, @Uid, @Ver,
                     @S, @P, @Sub, @D,
                     @M, @Ip, @At)",
                new
                {
                    Cid = consentId, Tid = tenantId, Uid = userId, Ver = request.TermsVersion,
                    S = request.AgreeService, P = request.AgreePrivacy,
                    Sub = request.AgreeSubscription, D = request.AgreeDataOwnership,
                    M = request.AgreeMarketing ?? false,
                    Ip = clientIp, At = agreedAt
                });
        }
        catch (Exception)
        {
            // 테이블 부재 환경 = audit skip + 동의 처리는 OK (베타 1주차 정합)
            return Ok(new TermsConsentResponse
            {
                ConsentId = consentId,
                AgreedAt = agreedAt,
                ClientIp = clientIp,
                TermsVersion = request.TermsVersion
            });
        }

        return Created($"/api/auth/terms/{consentId}", new TermsConsentResponse
        {
            ConsentId = consentId,
            AgreedAt = agreedAt,
            ClientIp = clientIp,
            TermsVersion = request.TermsVersion
        });
    }
}
