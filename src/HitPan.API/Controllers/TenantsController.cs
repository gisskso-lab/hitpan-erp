using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize]
public class TenantsController : ControllerBase
{
    private readonly ITenantService _tenantService;

    public TenantsController(ITenantService tenantService)
    {
        _tenantService = tenantService;
    }

    // 제거 (2026-06-25, 9축 실측 — 사장님 결재): POST /api/tenants/setup(AllowAnonymous) 백도어 제거.
    //   ① 익명으로 누구나 회사+관리자 계정을 찍어낼 수 있었다(헌법 #38 보안 격벽 위반).
    //   ② 그 admin 을 account_type 미설정 → DB 기본값 'tenant_user'(자식)로 만들어, Role=TenantAdmin
    //      이어도 PermissionService 의 tenant_admin 바이패스가 안 걸려 권한 메뉴 전부 403 락아웃(둔갑 결함).
    //   부모계정 정식 생성 경로는 CompanyBootstrapController.create-parent(부트스트랩 토큰 검증) 단일.
    //   전수확인: tenants/setup·CreateTenantRequest·TenantService.CreateAsync 호출처 0건(프론트·설치·마이그·테스트).
    //   GetCurrentAsync(me)는 보존. TenantService.CreateAsync·CreateTenantRequest/Response DTO·
    //   ITenantService.CreateAsync·미들웨어 예외 2곳도 함께 제거.

    [HttpGet("me")]
    [Authorize(Policy = "TenantProfile")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var me = await _tenantService.GetCurrentAsync(ct);
        if (me is null)
        {
            return NotFound();
        }

        return Ok(me);
    }
}
