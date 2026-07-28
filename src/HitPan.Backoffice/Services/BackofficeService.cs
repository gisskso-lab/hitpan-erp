using System.Net.Http.Json;
using System.Text.Json;
using HitPan.Backoffice.Models.Backoffice;

namespace HitPan.Backoffice.Services;

// 헌법 #35 객체 완전 분리 (사장님 결재 2026-06-04 W1+W2+W5):
//   - 인증 2개 (AdminLogin·ResellerLogin) — 백오피스 API에 저장됨, 작동
//   - 나머지 14개 — 옛 ERP api/admin/* · api/reseller/* 좀비 흐름
//     → ERP에서 컨트롤러 16개 삭제 후 폐기됨 (W9 백오피스 신설 대기)
//     → 사장님 클릭 시 폭발 방지로 안전 stub (NotSupported + Obsolete)
//     → 사용 화면 7개는 "준비 중" 안내로 봉합
public sealed class BackofficeService(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string ObsoleteW9 = "W9 백오피스 신설 예정 (사장님 결재 후). 현재 호출 시 NotSupportedException.";
    private const string W9Message = "이 기능은 백오피스 API 신설 대기 중입니다 (W9 차수). 잠시만 기다려주세요.";

    // ── 인증 (작동) ───────────────────────────────────────────────────────────

    public async Task<BackofficeLoginResult> AdminLoginAsync(string email, string password, CancellationToken ct = default)
    {
        using var res = await http.PostAsJsonAsync("api/backoffice/auth/admin/login",
            new { email, password }, ct);
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadFromJsonAsync<ApiResult<object>>(Json, ct);
            return new BackofficeLoginResult { Success = false, Message = err?.Message ?? "로그인 실패" };
        }
        var ok = await res.Content.ReadFromJsonAsync<ApiResult<AdminLoginResponse>>(Json, ct);
        return new BackofficeLoginResult { Success = true, Data = ok?.Data };
    }

    public async Task<BackofficeLoginResult> ResellerLoginAsync(string email, string password, CancellationToken ct = default)
    {
        using var res = await http.PostAsJsonAsync("api/backoffice/auth/reseller/login",
            new { email, password }, ct);
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadFromJsonAsync<ApiResult<object>>(Json, ct);
            return new BackofficeLoginResult { Success = false, Message = err?.Message ?? "로그인 실패" };
        }
        var ok = await res.Content.ReadFromJsonAsync<ApiResult<ResellerLoginResponse>>(Json, ct);
        return new BackofficeLoginResult { Success = true, ResellerData = ok?.Data };
    }

    // ── W9 대기 stub (사장님 클릭 폭발 방지) ─────────────────────────────────

    [Obsolete(ObsoleteW9)]
    public Task<AdminDashboardData?> GetAdminDashboardAsync(CancellationToken ct = default)
        => Task.FromException<AdminDashboardData?>(new NotSupportedException(W9Message));

    [Obsolete(ObsoleteW9)]
    public Task<PagedResult<AdminTenantListItem>?> GetAdminTenantsAsync(
        string? status = null, string? resellerId = null, string? search = null,
        int page = 1, int size = 20, CancellationToken ct = default)
        => Task.FromException<PagedResult<AdminTenantListItem>?>(new NotSupportedException(W9Message));

    [Obsolete(ObsoleteW9)]
    public Task<AdminTenantDetail?> GetAdminTenantAsync(string tenantId, CancellationToken ct = default)
        => Task.FromException<AdminTenantDetail?>(new NotSupportedException(W9Message));

    [Obsolete(ObsoleteW9)]
    public Task<AdminTenantDetail?> GetAdminTenantDetailAsync(string tenantId, CancellationToken ct = default)
        => GetAdminTenantAsync(tenantId, ct);

    [Obsolete(ObsoleteW9)]
    public Task UpdateTenantStatusAsync(
        string tenantId, string newStatus, string? reason = null, CancellationToken ct = default)
        => Task.FromException(new NotSupportedException(W9Message));

    [Obsolete(ObsoleteW9)]
    public Task<PagedResult<ResellerListItem>?> GetResellersAsync(
        string? status = null, string? search = null,
        int page = 1, int size = 20, CancellationToken ct = default)
        => Task.FromException<PagedResult<ResellerListItem>?>(new NotSupportedException(W9Message));

    [Obsolete(ObsoleteW9)]
    public Task<ResellerDetail?> GetResellerAsync(string resellerId, CancellationToken ct = default)
        => Task.FromException<ResellerDetail?>(new NotSupportedException(W9Message));

    [Obsolete(ObsoleteW9)]
    public Task<ResellerDetail?> GetResellerDetailAsync(string resellerId, CancellationToken ct = default)
        => GetResellerAsync(resellerId, ct);

    [Obsolete(ObsoleteW9)]
    public Task<(bool ok, string? error)> CreateResellerAsync(object request, CancellationToken ct = default)
        => Task.FromResult<(bool ok, string? error)>((false, W9Message));

    [Obsolete(ObsoleteW9)]
    public Task UpdateResellerStatusAsync(
        string resellerId, string newStatus, string? reason = null, CancellationToken ct = default)
        => Task.FromException(new NotSupportedException(W9Message));

    [Obsolete(ObsoleteW9)]
    public Task<List<ResellerAccountItem>?> GetResellerAccountsAsync(string resellerId, CancellationToken ct = default)
        => Task.FromException<List<ResellerAccountItem>?>(new NotSupportedException(W9Message));

    [Obsolete(ObsoleteW9)]
    public Task<List<CommissionPolicyItem>?> GetCommissionPoliciesAsync(string resellerId, CancellationToken ct = default)
        => Task.FromException<List<CommissionPolicyItem>?>(new NotSupportedException(W9Message));

    [Obsolete(ObsoleteW9)]
    public Task<List<CommissionPolicyItem>?> GetResellerCommissionsAsync(string resellerId, CancellationToken ct = default)
        => GetCommissionPoliciesAsync(resellerId, ct);

    [Obsolete(ObsoleteW9)]
    public Task<(bool ok, string? error)> CreateCommissionPolicyAsync(
        string resellerId, object request, CancellationToken ct = default)
        => Task.FromResult<(bool ok, string? error)>((false, W9Message));

    [Obsolete(ObsoleteW9)]
    public Task<PagedSettlementResult?> GetSettlementsAsync(
        string? settlementMonth = null, string? resellerId = null, string? status = null,
        int page = 1, int size = 20, CancellationToken ct = default)
        => Task.FromException<PagedSettlementResult?>(new NotSupportedException(W9Message));

    [Obsolete(ObsoleteW9)]
    public Task<SettlementListItem?> GetSettlementAsync(string settlementId, CancellationToken ct = default)
        => Task.FromException<SettlementListItem?>(new NotSupportedException(W9Message));

    [Obsolete(ObsoleteW9)]
    public Task ApproveSettlementAsync(string settlementId, string? memo = null, CancellationToken ct = default)
        => Task.FromException(new NotSupportedException(W9Message));

    [Obsolete(ObsoleteW9)]
    public Task PaySettlementAsync(
        string settlementId, DateTime paymentDate, string? memo = null, CancellationToken ct = default)
        => Task.FromException(new NotSupportedException(W9Message));

    [Obsolete(ObsoleteW9)]
    public Task GenerateSettlementsAsync(
        string settlementMonth, string? resellerId = null, CancellationToken ct = default)
        => Task.FromException(new NotSupportedException(W9Message));

    [Obsolete(ObsoleteW9)]
    public Task CancelSettlementAsync(string settlementId, string? reason = null, CancellationToken ct = default)
        => Task.FromException(new NotSupportedException(W9Message));

    [Obsolete(ObsoleteW9)]
    public Task<ResellerDashboardData?> GetResellerDashboardAsync(CancellationToken ct = default)
        => Task.FromException<ResellerDashboardData?>(new NotSupportedException(W9Message));

    [Obsolete(ObsoleteW9)]
    public Task<PagedResult<ResellerTenantListItem>?> GetMyTenantsAsync(
        string? status = null, string? search = null,
        int page = 1, int size = 20, CancellationToken ct = default)
        => Task.FromException<PagedResult<ResellerTenantListItem>?>(new NotSupportedException(W9Message));

    [Obsolete(ObsoleteW9)]
    public Task<PagedResult<ResellerTenantListItem>?> GetResellerTenantsAsync(
        string? status = null, string? search = null,
        int page = 1, int size = 20, CancellationToken ct = default)
        => GetMyTenantsAsync(status, search, page, size, ct);

    [Obsolete(ObsoleteW9)]
    public Task<ResellerTenantDetail?> GetMyTenantAsync(string tenantId, CancellationToken ct = default)
        => Task.FromException<ResellerTenantDetail?>(new NotSupportedException(W9Message));

    [Obsolete(ObsoleteW9)]
    public Task<ResellerTenantDetail?> GetResellerTenantDetailAsync(string tenantId, CancellationToken ct = default)
        => GetMyTenantAsync(tenantId, ct);

    [Obsolete(ObsoleteW9)]
    public Task<List<CommissionPolicyItem>?> GetMyCommissionPoliciesAsync(CancellationToken ct = default)
        => Task.FromException<List<CommissionPolicyItem>?>(new NotSupportedException(W9Message));

    [Obsolete(ObsoleteW9)]
    public Task<PagedResult<SettlementListItem>?> GetMySettlementsAsync(
        string? settlementMonth = null, string? status = null,
        int page = 1, int size = 20, CancellationToken ct = default)
        => Task.FromException<PagedResult<SettlementListItem>?>(new NotSupportedException(W9Message));
}
