using HitPan.Application.DTOs.Backoffice;

namespace HitPan.Application.Interfaces;

public interface IResellerService
{
    // 본사 — 대리점 관리
    Task<ResellerListResponse> GetResellersAsync(string? status, string? search, int page, int size, CancellationToken ct = default);
    Task<ResellerDetail> GetResellerAsync(string resellerId, CancellationToken ct = default);
    Task<string> GetResellerBankAccountAsync(string resellerId, CancellationToken ct = default);
    Task<CreateResellerResponse> CreateResellerAsync(CreateResellerRequest request, string createdBy, CancellationToken ct = default);
    Task UpdateResellerAsync(string resellerId, UpdateResellerRequest request, CancellationToken ct = default);
    Task UpdateResellerStatusAsync(string resellerId, UpdateResellerStatusRequest request, CancellationToken ct = default);

    // 본사 — 대리점 계정 관리
    Task<List<ResellerAccountItem>> GetResellerAccountsAsync(string resellerId, CancellationToken ct = default);
    Task<CreateResellerAccountResponse> CreateResellerAccountAsync(string resellerId, CreateResellerAccountRequest request, CancellationToken ct = default);
    Task ToggleResellerAccountAsync(string resellerId, string accountId, CancellationToken ct = default);

    // 본사 — 수수료 정책
    Task<List<CommissionPolicyItem>> GetCommissionPoliciesAsync(string resellerId, CancellationToken ct = default);
    Task<string> CreateCommissionPolicyAsync(string resellerId, CreateCommissionPolicyRequest request, string createdBy, CancellationToken ct = default);

    // 본사 — 수수료 정산
    Task<SettlementListResponse> GetSettlementsAsync(string? settlementMonth, string? status, string? resellerId, int page, int size, CancellationToken ct = default);
    Task<SettlementListItem> GetSettlementAsync(string settlementId, CancellationToken ct = default);
    Task<GenerateSettlementResponse> GenerateSettlementsAsync(GenerateSettlementRequest request, CancellationToken ct = default);
    Task ApproveSettlementAsync(string settlementId, string approvedBy, ApproveSettlementRequest request, CancellationToken ct = default);
    Task PaySettlementAsync(string settlementId, PaySettlementRequest request, CancellationToken ct = default);
    Task CancelSettlementAsync(string settlementId, CancelSettlementRequest request, CancellationToken ct = default);

    // 대리점 — 내 고객사
    Task<ResellerTenantListResponse> GetMyTenantsAsync(string resellerId, string? status, string? search, int page, int size, CancellationToken ct = default);
    Task<ResellerTenantDetail> GetMyTenantAsync(string resellerId, string tenantId, CancellationToken ct = default);

    // 대리점 — 내 수수료
    Task<List<CommissionPolicyItem>> GetMyCommissionPoliciesAsync(string resellerId, CancellationToken ct = default);
    Task<SettlementListResponse> GetMySettlementsAsync(string resellerId, int page, int size, CancellationToken ct = default);
    Task<ResellerDashboardResponse> GetResellerDashboardAsync(string resellerId, CancellationToken ct = default);

    // 본사 — 대시보드
    Task<AdminDashboardResponse> GetAdminDashboardAsync(CancellationToken ct = default);

    // 본사 — 고객사 관리
    Task<AdminTenantListResponse> GetAdminTenantsAsync(string? status, string? resellerId, string? planType, string? search, int page, int size, CancellationToken ct = default);
    Task<AdminTenantDetail> GetAdminTenantAsync(string tenantId, CancellationToken ct = default);
    Task UpdateTenantStatusAsync(string tenantId, UpdateTenantStatusRequest request, CancellationToken ct = default);
    Task<AdminCreateTenantResponse> AdminCreateTenantAsync(AdminCreateTenantRequest request, string adminId, CancellationToken ct = default);
}
