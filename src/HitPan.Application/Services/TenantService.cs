using HitPan.Application.Common;
using HitPan.Application.DTOs.Tenant;
using HitPan.Application.Interfaces;
using HitPan.Domain.Entities;
using HitPan.Domain.Enums;

namespace HitPan.Application.Services;

public class TenantService : ITenantService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentTenant _currentTenant;

    public TenantService(IUnitOfWork unitOfWork, ICurrentTenant currentTenant)
    {
        _unitOfWork = unitOfWork;
        _currentTenant = currentTenant;
    }

    // CreateAsync 제거 (2026-06-25, 9축 실측 — 사장님 결재): /api/tenants/setup 익명 백도어의 본체.
    //   이 메서드는 admin User 에 AccountType 을 설정하지 않아 DB 기본값 'tenant_user'(자식)로 부모를
    //   만들었다(Role=TenantAdmin 이어도 account_type 불일치 → tenant_admin 권한 바이패스 안 됨 → 권한
    //   메뉴 전부 403 락아웃). 또 익명으로 회사+관리자를 찍어낼 수 있어 보안 격벽(헌법 #38) 위반.
    //   부모계정 정식 생성은 CompanyBootstrapController.create-parent(부트스트랩 토큰 검증)가 단일 경로다.
    //   GetCurrentAsync(아래)는 /api/tenants/me 에서 쓰이므로 보존.

    public async Task<TenantMeResponse?> GetCurrentAsync(CancellationToken ct = default)
    {
        var tenantId = _currentTenant.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
            return null;

        var tenants = _unitOfWork.Repository<Tenant>();
        var tenant = await tenants.GetByIdAsync(tenantId);
        if (tenant is null)
            return null;

        return new TenantMeResponse
        {
            TenantId = tenant.Id,
            CompanyName = tenant.CompanyName,
            Status = tenant.Status.ToString().ToLowerInvariant()
        };
    }
}
