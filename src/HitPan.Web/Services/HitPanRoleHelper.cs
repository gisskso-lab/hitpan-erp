using System.Security.Claims;

namespace HitPan.Web.Services;

public static class HitPanRoleHelper
{
    public static bool IsInHitPanRole(ClaimsPrincipal user, string role)
    {
        if (user.IsInRole(role))
        {
            return true;
        }

        var alternate = role switch
        {
            "TenantAdmin" => "tenant_admin",
            "Manager" => "manager",
            "User" => "user",
            "Readonly" => "readonly",
            _ => null
        };

        return alternate != null && user.IsInRole(alternate);
    }
}
