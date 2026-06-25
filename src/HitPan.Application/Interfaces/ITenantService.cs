using HitPan.Application.DTOs.Tenant;

namespace HitPan.Application.Interfaces;

public interface ITenantService
{
    // CreateAsync 제거 (2026-06-25, 9축 실측 — 사장님 결재): /api/tenants/setup 익명 백도어 제거에 동반.
    //   부모계정 생성은 CompanyBootstrapController.create-parent(부트스트랩 토큰 검증) 단일 경로.
    Task<TenantMeResponse?> GetCurrentAsync(CancellationToken ct = default);
}
