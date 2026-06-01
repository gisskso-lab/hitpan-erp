using System.Security.Claims;

namespace HitPan.API.Services.Auth;

/// <summary>
/// WS-20260601-16 (2026-06-01): 백오피스/랜딩 JWT 4계층 클레임 스키마 박제.
///
/// 8대 명제 마스터 설계 §JWT 4계층:
///   role = owner | platform_manager | platform_staff | reseller
///   reseller_serial = 대리점 시리얼 (role=reseller 일 때 필수)
///   hq_scope        = 본사 RLS 우회 방지 플래그 (true/false). owner/platform_*만 true.
///
/// 헌법 #2 (tenant_id·reseller_id 파라미터 금지) 확장:
///   reseller_serial은 오직 JWT 발급 시점에서만 채워진다. 클라이언트가 보내는 값은 폐기.
///
/// 사용처: BackofficeAuthService.IssueToken() 등 신규 백오피스/랜딩 토큰 발급 경로.
/// 기존 ERP 토큰 발급 경로(tenant_admin/sales_user 등)는 손대지 않는다 (헌법 #1 덮어쓰기 금지).
/// </summary>
public static class JwtClaimsBuilder
{
    public const string ClaimRole = "role";
    public const string ClaimResellerSerial = "reseller_serial";
    public const string ClaimHqScope = "hq_scope";

    public const string RoleOwner = "owner";
    public const string RolePlatformManager = "platform_manager";
    public const string RolePlatformStaff = "platform_staff";
    public const string RoleReseller = "reseller";

    /// <summary>
    /// 본사(owner / platform_manager / platform_staff) 토큰 클레임 박제.
    /// hq_scope = true → 본사 직원은 RLS 시리얼 필터 우회 (단, RepositoryBase가 별도 검증).
    /// </summary>
    public static IEnumerable<Claim> BuildPlatformClaims(string role, string userId, string email)
    {
        if (role is not (RoleOwner or RolePlatformManager or RolePlatformStaff))
        {
            throw new ArgumentException(
                $"BuildPlatformClaims는 owner/platform_manager/platform_staff만 허용 (입력: {role})",
                nameof(role));
        }
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("userId 필수", nameof(userId));

        yield return new Claim(ClaimRole, role);
        yield return new Claim(ClaimHqScope, "true");
        yield return new Claim(ClaimTypes.NameIdentifier, userId);
        if (!string.IsNullOrWhiteSpace(email))
        {
            yield return new Claim(ClaimTypes.Email, email);
        }
    }

    /// <summary>
    /// 대리점(role=reseller) 토큰 클레임 박제.
    /// reseller_serial은 본사 DB에서 조회한 값을 그대로 박제 (클라이언트 입력 금지).
    /// hq_scope = false → 본사 RLS 우회 불가.
    /// </summary>
    public static IEnumerable<Claim> BuildResellerClaims(string resellerSerial, string userId, string email)
    {
        if (string.IsNullOrWhiteSpace(resellerSerial))
        {
            // 빈 시리얼로 토큰을 발급하면 ResellerSelfOnly 정책을 우회한 채
            // RLS 필터가 빈 문자열로 모든 대리점을 조회하는 사고가 난다. 즉시 차단.
            throw new ArgumentException("reseller_serial 필수 (JWT 발급 거부)", nameof(resellerSerial));
        }
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("userId 필수", nameof(userId));

        yield return new Claim(ClaimRole, RoleReseller);
        yield return new Claim(ClaimResellerSerial, resellerSerial);
        yield return new Claim(ClaimHqScope, "false");
        yield return new Claim(ClaimTypes.NameIdentifier, userId);
        if (!string.IsNullOrWhiteSpace(email))
        {
            yield return new Claim(ClaimTypes.Email, email);
        }
    }

    /// <summary>
    /// ClaimsPrincipal에서 4계층 컨텍스트 추출 (Repository/Service 공용).
    /// </summary>
    public static BackofficeAuthContext Extract(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var role = user.FindFirst(ClaimRole)?.Value ?? string.Empty;
        var serial = user.FindFirst(ClaimResellerSerial)?.Value ?? string.Empty;
        var hqScope = string.Equals(user.FindFirst(ClaimHqScope)?.Value, "true", StringComparison.OrdinalIgnoreCase);
        return new BackofficeAuthContext(role, serial, hqScope);
    }
}

/// <summary>
/// 백오피스/랜딩 인증 컨텍스트 (4계층 + 시리얼 + 본사 스코프).
/// </summary>
public readonly record struct BackofficeAuthContext(string Role, string ResellerSerial, bool HqScope)
{
    public bool IsOwner => Role == JwtClaimsBuilder.RoleOwner;
    public bool IsPlatform => Role is JwtClaimsBuilder.RoleOwner
                                   or JwtClaimsBuilder.RolePlatformManager
                                   or JwtClaimsBuilder.RolePlatformStaff;
    public bool IsReseller => Role == JwtClaimsBuilder.RoleReseller;
}
