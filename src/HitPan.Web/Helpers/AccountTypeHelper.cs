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

    /// 이 사람이 **부모계정(대표)** 인가.
    ///
    /// 🔴 2026-08-11 (사장님 지시):
    ///   *"기본적으로 **설정관리·자료관리 대메뉴의 접근권한은 부모계정만** 가능하도록 하는 게 안전할듯."*
    ///
    ///   [왜 대메뉴 단위인가] 화면 하나하나에 자물쇠를 다는 방식은 **새 화면을 만들 때마다
    ///     빠뜨린다.** 실제로 그랬다 — 설정관리 9화면 중 자물쇠가 걸린 것은 양식템플릿 하나뿐이었고,
    ///     나머지 8개는 로그인만 하면 누구나 들어왔다. 자료 초기화 화면까지 그랬다.
    ///     대메뉴로 묶어 막으면 **나중에 화면이 늘어도 저절로 보호된다.**
    ///
    ///   ⚠️ 옛 토큰 호환: 대문자 "TenantAdmin" 으로 발급된 토큰이 남아 있을 수 있다.
    ///     그것을 못 알아보면 대표계정이 자기 설정 화면에서 쫓겨난다.
    public static bool IsTenantAdmin(ClaimsPrincipal user)
    {
        var type = GetAccountType(user);
        return type == TenantAdmin || type == "TenantAdmin";
    }
}
