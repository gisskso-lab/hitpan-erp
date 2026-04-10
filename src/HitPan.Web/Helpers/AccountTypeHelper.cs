using System.Security.Claims;

namespace HitPan.Web.Helpers;

public static class AccountTypeHelper
{
    public const string PlatformAdmin = "platform_admin";
    public const string ResellerAdmin = "reseller_admin";
    public const string TenantAdmin = "tenant_admin";
    public const string TenantUser = "tenant_user";

    public static string GetAccountType(ClaimsPrincipal user)
        => user.FindFirst("account_type")?.Value ?? TenantUser;

    public static bool IsPlatformAdmin(ClaimsPrincipal user)
        => GetAccountType(user) == PlatformAdmin;

    public static bool IsResellerAdmin(ClaimsPrincipal user)
        => GetAccountType(user) == ResellerAdmin;

    public static bool IsTenantUser(ClaimsPrincipal user)
        => GetAccountType(user) == TenantAdmin
           || GetAccountType(user) == TenantUser
           || GetAccountType(user) == "TenantAdmin";
}
