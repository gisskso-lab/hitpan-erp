using System.Data;
using HitPan.API.Services.Auth;
using Microsoft.AspNetCore.Http;

namespace HitPan.API.Repositories;

/// <summary>
/// WS-20260601-16 (2026-06-01): 백오피스/랜딩 Repository 베이스 (RLS 강제 박제).
///
/// 8대 명제 마스터 설계 §RLS 강제:
///   - 모든 백오피스/랜딩 SELECT는 ApplyRls()를 거쳐야 한다.
///   - reseller_serial은 오직 JWT 클레임(HttpContext.User)에서만 추출한다.
///   - 컨트롤러/서비스가 reseller_id·tenant_id를 파라미터로 받는 즉시 ArgumentException.
///
/// 헌법 #2 (tenant_id 파라미터 절대 금지) 확장판.
/// 헌법 #15 (빈 catch 금지) 정합.
/// </summary>
public abstract class BackofficeRepositoryBase
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    protected BackofficeRepositoryBase(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor
            ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    /// <summary>
    /// 현재 요청의 JWT 클레임에서 4계층 컨텍스트 추출.
    /// HttpContext.User가 없으면 인증 미통과 → InvalidOperationException.
    /// </summary>
    protected BackofficeAuthContext GetAuthContext()
    {
        var user = _httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException(
                "HttpContext.User 없음 — 백오피스 Repository는 인증된 컨텍스트에서만 호출 가능");
        return JwtClaimsBuilder.Extract(user);
    }

    /// <summary>
    /// RLS 강제 — SELECT 쿼리에 WHERE reseller_serial=@__rls_serial 추가용 파라미터를 주입한다.
    ///
    /// 본사 직원(IsPlatform + HqScope=true): 시리얼 필터 우회 (모든 대리점 조회 가능).
    /// 대리점(IsReseller): 자기 시리얼만. 빈 시리얼이면 즉시 차단.
    ///
    /// 사용 예:
    ///   var p = new DynamicParameters();
    ///   ApplyRls(p, out var resellerFilterSql);
    ///   var sql = $"SELECT * FROM reseller_settlements {resellerFilterSql}";
    /// </summary>
    /// <param name="parameters">Dapper DynamicParameters (또는 동등 객체)</param>
    /// <param name="resellerFilterSql">
    /// 본사 = " WHERE 1=1 " / 대리점 = " WHERE reseller_serial=@__rls_serial "
    /// 호출자가 추가 WHERE를 붙일 땐 " AND ... " 형태로 이어붙이면 된다.
    /// </param>
    protected BackofficeAuthContext ApplyRls(IDbDataParameter? _ /* reserved */, out string resellerFilterSql)
    {
        var ctx = GetAuthContext();

        if (ctx.IsPlatform && ctx.HqScope)
        {
            resellerFilterSql = " WHERE 1=1 ";
            return ctx;
        }

        if (ctx.IsReseller)
        {
            if (string.IsNullOrWhiteSpace(ctx.ResellerSerial))
            {
                // 빈 시리얼로 RLS를 통과시키면 전체 대리점 데이터가 노출된다. 즉시 차단.
                throw new UnauthorizedAccessException(
                    "reseller_serial 클레임 없음 — RLS 차단 (JWT 발급 경로 점검 필요)");
            }
            resellerFilterSql = " WHERE reseller_serial=@__rls_serial ";
            return ctx;
        }

        // 그 외(인증되지 않았거나 알 수 없는 role) — 백오피스 데이터 접근 불가.
        throw new UnauthorizedAccessException(
            $"백오피스 RLS: 허용되지 않은 role='{ctx.Role}' (owner/platform_*/reseller 만 허용)");
    }

    /// <summary>
    /// Dapper용 오버로드 — DynamicParameters에 __rls_serial을 자동 박제한다.
    /// </summary>
    protected BackofficeAuthContext ApplyRls(Dapper.DynamicParameters parameters, out string resellerFilterSql)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var ctx = ApplyRls((IDbDataParameter?)null, out resellerFilterSql);
        if (ctx.IsReseller)
        {
            parameters.Add("__rls_serial", ctx.ResellerSerial);
        }
        return ctx;
    }

    /// <summary>
    /// 헌법 #2 확장: 컨트롤러/서비스가 reseller_id·tenant_id·reseller_serial을
    /// 메서드 파라미터로 받는 즉시 차단. Repository 진입점에서 가드 호출 강제.
    /// </summary>
    /// <example>
    /// public Task GetXxxAsync(string? tenantId)
    /// {
    ///     ThrowIfTenantOrResellerProvided(tenantId);  // 즉시 ArgumentException
    ///     ...
    /// }
    /// </example>
    protected static void ThrowIfTenantOrResellerProvided(
        string? tenantId = null,
        string? resellerId = null,
        string? resellerSerial = null)
    {
        if (!string.IsNullOrEmpty(tenantId))
        {
            throw new ArgumentException(
                "헌법 #2 위반: tenant_id를 파라미터로 받을 수 없습니다. JWT 클레임에서만 추출하십시오.",
                nameof(tenantId));
        }
        if (!string.IsNullOrEmpty(resellerId))
        {
            throw new ArgumentException(
                "헌법 #2 확장: reseller_id를 파라미터로 받을 수 없습니다. JWT 클레임(reseller_serial)에서만 추출하십시오.",
                nameof(resellerId));
        }
        if (!string.IsNullOrEmpty(resellerSerial))
        {
            throw new ArgumentException(
                "헌법 #2 확장: reseller_serial을 파라미터로 받을 수 없습니다. JWT 클레임에서만 추출하십시오.",
                nameof(resellerSerial));
        }
    }
}
