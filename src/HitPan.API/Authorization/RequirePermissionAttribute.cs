using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Authorization;

/// <summary>
/// 엔드포인트에 메뉴·액션 기반 동적 권한 체크를 건다.
/// 예: [RequirePermission("COLLECTION", "view")]
///     [RequirePermission("COLLECTION", "create")]
///     [RequirePermission("COLLECTION", "delete")]
/// 액션 값: view / create / update / delete / export
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : TypeFilterAttribute
{
    public RequirePermissionAttribute(string menuCode, string action)
        : base(typeof(PermissionFilter))
    {
        Arguments = new object[] { menuCode, action };
    }
}
