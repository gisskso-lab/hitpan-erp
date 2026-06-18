using System.Security.Claims;

namespace HitPan.Web.Helpers;

public static class AccountTypeHelper
{
    // 보안 격벽 (2026-06-18): ERP 계정은 부모(tenant_admin)/자식(tenant_user) 둘뿐.
    //   PlatformAdmin·ResellerAdmin 상수 + IsPlatformAdmin·IsResellerAdmin 메서드 제거 — 본사·대리점 계층은 백오피스 전담.
    public const string TenantAdmin = "tenant_admin";
    public const string TenantUser = "tenant_user";

    public static string GetAccountType(ClaimsPrincipal user)
        => user.FindFirst("account_type")?.Value ?? TenantUser;

    public static bool IsTenantUser(ClaimsPrincipal user)
        => GetAccountType(user) == TenantAdmin
           || GetAccountType(user) == TenantUser
           || GetAccountType(user) == "TenantAdmin";
}
