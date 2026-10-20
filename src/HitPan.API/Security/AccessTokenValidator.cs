using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HitPan.Infrastructure.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace HitPan.API.Security;

/// <summary>
/// Validates access JWTs (same rules as JwtBearer) for query-string flows (e.g. document downloads).
/// </summary>
public sealed class AccessTokenValidator
{
    private readonly TokenValidationParameters _parameters;

    public AccessTokenValidator()
    {
        // 봉합 2026-06-16: TenantConfigReader 영역 통일 (1.2.6 환경변수 폐기 사장님 결재 정합)
        var jwtSecret = TenantConfigReader.GetRequired("JWT_SECRET");

        // 보안 강화: issuer/audience 검증 활성화
        var jwtIssuer = TenantConfigReader.Get("JWT_ISSUER") ?? "hitpan-erp";
        var jwtAudience = TenantConfigReader.Get("JWT_AUDIENCE") ?? "hitpan-client";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        _parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    }

    public ClaimsPrincipal? ValidateAccessToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, _parameters, out var securityToken);
            if (securityToken is not JwtSecurityToken jwt
                || !string.Equals(jwt.Header.Alg, "HS256", StringComparison.Ordinal))
            {
                return null;
            }

            if (string.Equals(
                    principal.FindFirst("token_type")?.Value,
                    "refresh",
                    StringComparison.Ordinal))
            {
                return null;
            }

            return principal;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 다운로드 전용 강화 검증: 토큰 타입='download' + 최대 수명 2시간 + 문서 ID 바인딩 필수.
    /// URL 공유 위험을 차단하기 위해 일반 access 토큰은 거부한다.
    /// </summary>
    /// <param name="documentType">
    /// 🔴 20261021작 — 문서 종류 바인딩. 값을 주면 발급 때 넣어 둔 <c>doc_type</c> 과 같아야 한다.
    /// 종전에는 이 칸을 검사하지 않았다. 이번 차수가 거래명세서와 세금계산서의 <b>id 를 같게</b>
    /// 만들어, 거래명세서 토큰으로 세금계산서를 받아 갈 수 있게 된다 ⇒ 그 구멍을 메우고 간다.
    /// 값을 안 주면 종전 그대로 검사하지 않는다(#1 — 기존 호출자 무영향).
    /// </param>
    public ClaimsPrincipal? ValidateDownloadToken(string? token, string documentId, string? documentType = null)
    {
        var principal = ValidateAccessToken(token);
        if (principal is null) return null;

        // 토큰 타입 강제: 'download' 아니면 거부 (일반 access 토큰 URL 공유 차단)
        if (!string.Equals(principal.FindFirst("token_type")?.Value, "download", StringComparison.Ordinal))
        {
            return null;
        }

        // 문서 ID 바인딩 검증 (토큰이 특정 문서에만 유효)
        var boundDocId = principal.FindFirst("doc_id")?.Value;
        if (string.IsNullOrEmpty(boundDocId) || !string.Equals(boundDocId, documentId, StringComparison.Ordinal))
        {
            return null;
        }

        // 🔴 문서 종류 바인딩 검증 — 같은 id 를 쓰는 다른 종류의 종이를 받아 가지 못하게 한다.
        if (!string.IsNullOrEmpty(documentType))
        {
            var boundDocType = principal.FindFirst("doc_type")?.Value;
            if (string.IsNullOrEmpty(boundDocType) ||
                !string.Equals(boundDocType, documentType, StringComparison.Ordinal))
            {
                return null;
            }
        }

        // 최대 수명 2시간 강제 (exp - iat ≤ 7200초)
        var expClaim = principal.FindFirst("exp")?.Value;
        var iatClaim = principal.FindFirst("iat")?.Value;
        if (long.TryParse(expClaim, out var exp) && long.TryParse(iatClaim, out var iat))
        {
            if (exp - iat > 7200) return null;  // 2시간 초과 토큰 거부
        }

        return principal;
    }
}
