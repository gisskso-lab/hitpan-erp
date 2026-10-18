using System.Security.Claims;

namespace HitPan.Backoffice.API.Auth;

// 사장님(Owner) 영역 판정 한 자리 — 2026-10-18 작B2-A
//
// 🔴 왜 생겼나 (2026-10-18 실측 P0)
//   컨트롤러들이 각자 `role == "owner"` 로 막고 있었는데,
//   installer/backoffice/00_backoffice_core.sql:55 의
//     platform_admins.role enum = super_admin | billing_admin | cs_admin | readonly
//   에는 **owner 가 없다.** 로그인 토큰의 role 은 그 칸을 그대로 싣는다
//   (BackofficeAuthController.cs:60) ⇒ **role=owner claim 은 발급될 수 없다.**
//   ⇒ 운영 계정(master@hitpan.kr · role=super_admin · 실측)으로 호출해도
//      403 "사장님(Owner)만 접근할 수 있습니다." 가 돌아왔다 — 화면·API 양쪽 모두.
//
// 🔴 정본은 레포가 이미 적어 뒀다
//   installer/backoffice/90_seed_permissions.sql:78-85 의 owner.* 권한
//     allowed_roles = 'owner,platform_owner,super_admin'
//   그 세 축을 그대로 쓴다. **DB enum 은 늘리지 않는다**(스키마 무접촉).
//
// 🔴 판정을 한 자리에만 둔다. 컨트롤러마다 복사하면 다음에 또 한쪽만 고친다.
public static class OwnerClaims
{
    private static readonly string[] OwnerRoles = { "owner", "super_admin" };
    private static readonly string[] OwnerAccountTypes = { "platform_owner" };

    /// <summary>사장님 영역(자격증명·권한·BO계정·시스템설정)에 들어올 수 있는가.</summary>
    public static bool IsOwner(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return false;

        foreach (var c in user.Claims)
        {
            if (c.Type == "role"
                && OwnerRoles.Contains(c.Value, StringComparer.OrdinalIgnoreCase)) return true;
            if (c.Type == "account_type"
                && OwnerAccountTypes.Contains(c.Value, StringComparer.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
