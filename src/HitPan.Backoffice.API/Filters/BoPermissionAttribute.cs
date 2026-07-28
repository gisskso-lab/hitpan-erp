using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HitPan.Backoffice.API.Filters;

// [BoPermission("reseller-applications.approve")] — DB 저장 권한 검사 (사장님 결재 2026-06-04)
//
// 동작:
//   1) User.IsAuthenticated 확인 (JWT 통과 필수)
//   2) role 클레임 추출
//   3) IBoPermissionService.IsAllowedAsync(key, role) 호출
//   4) 거부 시 403
//
// 헌법 정합:
//   #11 — 사장님이 /owner/permissions 에서 저장한 정책에 따라 검사
//   #15 — 빈 catch 금지 (서비스 측 try/catch)
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class BoPermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _key;

    public BoPermissionAttribute(string permissionKey)
    {
        _key = permissionKey;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var role = user.FindFirst("role")?.Value ?? "";
        var svc = context.HttpContext.RequestServices.GetService(typeof(IBoPermissionService)) as IBoPermissionService;
        if (svc is null)
        {
            context.Result = new StatusCodeResult(500);
            return;
        }

        var allowed = await svc.IsAllowedAsync(_key, role, context.HttpContext.RequestAborted);
        if (!allowed)
        {
            context.Result = new ObjectResult(new
            {
                success = false,
                message = $"권한이 없습니다 (필요 권한: {_key}). 사장님께 권한 부여를 요청해주세요."
            })
            { StatusCode = 403 };
        }
    }
}
