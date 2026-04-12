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

        // Login JWT maps legacy TenantAdmin → system_admin (AuthService.MapLegacyRole).
        if (role == "TenantAdmin" && user.IsInRole("system_admin"))
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
