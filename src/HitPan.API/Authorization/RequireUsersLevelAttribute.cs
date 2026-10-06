using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HitPan.API.Authorization;

/// <summary>
/// 20261005작5 §5-2 — 「직원 계정 관리」 단계 n 이상만 통과. 1 조회(USERS) · 2 계정설정(USERS_ACCOUNT) · 3 구독계정추가(USERS_SEAT).
/// 대표·관리자(tenant_admin) = 늘 3. 판정은 <see cref="IPermissionService.GetUsersLevelAsync"/> 하나(위가 아래를 포함).
/// </summary>
/// <remarks>
/// 403 본문에 <c>error = "users_level_required"</c> 를 싣는다 — 기기 인증 403(DeviceAuth)과 본문으로 구별된다([3-V] P2-06).
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class RequireUsersLevelAttribute : TypeFilterAttribute
{
    public int Level { get; }

    public RequireUsersLevelAttribute(int level)
        : base(typeof(UsersLevelFilter))
    {
        Level = level;
        Arguments = new object[] { level };
    }
}

public sealed class UsersLevelFilter : IAsyncAuthorizationFilter
{
    public const string ErrorCode = "users_level_required";

    private readonly IPermissionService _permission;
    private readonly ICurrentTenant _currentTenant;
    private readonly int _level;

    public UsersLevelFilter(IPermissionService permission, ICurrentTenant currentTenant, int level)
    {
        _permission = permission;
        _currentTenant = currentTenant;
        _level = level;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (string.IsNullOrEmpty(_currentTenant.UserId) || string.IsNullOrEmpty(_currentTenant.TenantId))
        {
            context.Result = new ForbidResult();
            return;
        }

        var level = await _permission.GetUsersLevelAsync(
            _currentTenant.UserId, _currentTenant.TenantId, context.HttpContext.RequestAborted).ConfigureAwait(false);

        if (level < _level)
        {
            context.Result = new ObjectResult(new
            {
                error = ErrorCode,
                required = _level,
                level,
                message = "이 기능을 쓸 권한이 없습니다. 대표님께 권한을 요청하세요."
            })
            { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
