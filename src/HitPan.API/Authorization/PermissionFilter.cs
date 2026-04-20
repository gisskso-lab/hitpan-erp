using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HitPan.API.Authorization;

/// <summary>
/// user_permissions 기반 동적 권한 체크 필터.
/// RequirePermissionAttribute 가 TypeFilter 로 주입한다.
/// tenant_admin / platform_admin 은 PermissionService 내부에서 바이패스(#36-A).
/// </summary>
public sealed class PermissionFilter : IAsyncAuthorizationFilter
{
    private readonly IPermissionService _permission;
    private readonly ICurrentTenant _currentTenant;
    private readonly string _menuCode;
    private readonly string _action;

    public PermissionFilter(
        IPermissionService permission,
        ICurrentTenant currentTenant,
        string menuCode,
        string action)
    {
        _permission = permission;
        _currentTenant = currentTenant;
        _menuCode = menuCode;
        _action = action;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (string.IsNullOrEmpty(_currentTenant.UserId) || string.IsNullOrEmpty(_currentTenant.TenantId))
        {
            context.Result = new ForbidResult();
            return;
        }

        var allowed = await _permission.HasPermissionAsync(
            _currentTenant.UserId,
            _currentTenant.TenantId,
            _menuCode,
            _action);

        if (!allowed)
        {
            context.Result = new ForbidResult();
        }
    }
}
