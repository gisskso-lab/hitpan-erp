using System.Net.Http.Json;
using System.Text.Json;
using HitPan.Backoffice.Models.Backoffice;

namespace HitPan.Backoffice.Services;

public sealed class BackofficeService(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // ── 인증 ──────────────────────────────────────────────────────────────────

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

    // ── 본사 대시보드 ─────────────────────────────────────────────────────────

    public async Task<AdminDashboardData?> GetAdminDashboardAsync(CancellationToken ct = default)
    {
        var res = await http.GetFromJsonAsync<ApiResult<AdminDashboardData>>("api/admin/dashboard", Json, ct);
        return res?.Data;
    }

    // ── 본사 고객사 ───────────────────────────────────────────────────────────

    public async Task<PagedResult<AdminTenantListItem>?> GetAdminTenantsAsync(
        string? status = null, string? resellerId = null, string? search = null,
        int page = 1, int size = 20, CancellationToken ct = default)
    {
        var qs = BuildQs(("status", status), ("resellerId", resellerId),
                         ("search", search), ("page", page.ToString()), ("size", size.ToString()));
        var res = await http.GetFromJsonAsync<ApiResult<PagedResult<AdminTenantListItem>>>(
            $"api/admin/tenants{qs}", Json, ct);
        return res?.Data;
    }

    public async Task<AdminTenantDetail?> GetAdminTenantAsync(string tenantId, CancellationToken ct = default)
    {
        var res = await http.GetFromJsonAsync<ApiResult<AdminTenantDetail>>(
            $"api/admin/tenants/{tenantId}", Json, ct);
        return res?.Data;
    }

    public Task<AdminTenantDetail?> GetAdminTenantDetailAsync(string tenantId, CancellationToken ct = default)
        => GetAdminTenantAsync(tenantId, ct);

    public async Task UpdateTenantStatusAsync(
        string tenantId, string newStatus, string? reason = null, CancellationToken ct = default)
    {
        using var res = await http.PatchAsJsonAsync(
            $"api/admin/tenants/{tenantId}/status", new { newStatus, reason }, ct);
        res.EnsureSuccessStatusCode();
    }

    // ── 본사 대리점 ───────────────────────────────────────────────────────────

    public async Task<PagedResult<ResellerListItem>?> GetResellersAsync(
        string? status = null, string? search = null,
        int page = 1, int size = 20, CancellationToken ct = default)
    {
        var qs = BuildQs(("status", status), ("search", search),
                         ("page", page.ToString()), ("size", size.ToString()));
        var res = await http.GetFromJsonAsync<ApiResult<PagedResult<ResellerListItem>>>(
            $"api/admin/resellers{qs}", Json, ct);
        return res?.Data;
    }

    public async Task<ResellerDetail?> GetResellerAsync(string resellerId, CancellationToken ct = default)
    {
        var res = await http.GetFromJsonAsync<ApiResult<ResellerDetail>>(
            $"api/admin/resellers/{resellerId}", Json, ct);
        return res?.Data;
    }

    public Task<ResellerDetail?> GetResellerDetailAsync(string resellerId, CancellationToken ct = default)
        => GetResellerAsync(resellerId, ct);

    public async Task<(bool ok, string? error)> CreateResellerAsync(object request, CancellationToken ct = default)
    {
        using var res = await http.PostAsJsonAsync("api/admin/resellers", request, ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await res.Content.ReadFromJsonAsync<ApiResult<object>>(Json, ct);
        return (false, err?.Message ?? "등록 실패");
    }

    public async Task UpdateResellerStatusAsync(
        string resellerId, string newStatus, string? reason = null, CancellationToken ct = default)
    {
        using var res = await http.PatchAsJsonAsync(
            $"api/admin/resellers/{resellerId}/status", new { newStatus, reason }, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task<List<ResellerAccountItem>?> GetResellerAccountsAsync(string resellerId, CancellationToken ct = default)
    {
        var res = await http.GetFromJsonAsync<ApiResult<List<ResellerAccountItem>>>(
            $"api/admin/resellers/{resellerId}/accounts", Json, ct);
        return res?.Data;
    }

    public async Task<List<CommissionPolicyItem>?> GetCommissionPoliciesAsync(string resellerId, CancellationToken ct = default)
    {
        var res = await http.GetFromJsonAsync<ApiResult<List<CommissionPolicyItem>>>(
            $"api/admin/resellers/{resellerId}/commissions", Json, ct);
        return res?.Data;
    }

    public Task<List<CommissionPolicyItem>?> GetResellerCommissionsAsync(string resellerId, CancellationToken ct = default)
        => GetCommissionPoliciesAsync(resellerId, ct);

    public async Task<(bool ok, string? error)> CreateCommissionPolicyAsync(
        string resellerId, object request, CancellationToken ct = default)
    {
        using var res = await http.PostAsJsonAsync($"api/admin/resellers/{resellerId}/commissions", request, ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await res.Content.ReadFromJsonAsync<ApiResult<object>>(Json, ct);
        return (false, err?.Message ?? "등록 실패");
    }

    // ── 본사 정산 ─────────────────────────────────────────────────────────────

    public async Task<PagedSettlementResult?> GetSettlementsAsync(
        string? settlementMonth = null, string? resellerId = null, string? status = null,
        int page = 1, int size = 20, CancellationToken ct = default)
    {
        var qs = BuildQs(("settlementMonth", settlementMonth), ("status", status),
                         ("resellerId", resellerId), ("page", page.ToString()), ("size", size.ToString()));
        var res = await http.GetFromJsonAsync<ApiResult<PagedSettlementResult>>(
            $"api/admin/settlements{qs}", Json, ct);
        return res?.Data;
    }

    public async Task<SettlementListItem?> GetSettlementAsync(string settlementId, CancellationToken ct = default)
    {
        var res = await http.GetFromJsonAsync<ApiResult<SettlementListItem>>(
            $"api/admin/settlements/{settlementId}", Json, ct);
        return res?.Data;
    }

    public async Task ApproveSettlementAsync(
        string settlementId, string? memo = null, CancellationToken ct = default)
    {
        using var res = await http.PostAsJsonAsync($"api/admin/settlements/{settlementId}/approve", new { memo }, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task PaySettlementAsync(
        string settlementId, DateTime paymentDate, string? memo = null, CancellationToken ct = default)
    {
        using var res = await http.PostAsJsonAsync($"api/admin/settlements/{settlementId}/pay",
            new { paymentDate, memo }, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task GenerateSettlementsAsync(
        string settlementMonth, string? resellerId = null, CancellationToken ct = default)
    {
        using var res = await http.PostAsJsonAsync("api/admin/settlements/generate",
            new { settlementMonth, resellerId }, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task CancelSettlementAsync(
        string settlementId, string? reason = null, CancellationToken ct = default)
    {
        using var res = await http.PostAsJsonAsync($"api/admin/settlements/{settlementId}/cancel",
            new { reason }, ct);
        res.EnsureSuccessStatusCode();
    }

    // ── 대리점 포털 ───────────────────────────────────────────────────────────

    public async Task<ResellerDashboardData?> GetResellerDashboardAsync(CancellationToken ct = default)
    {
        var res = await http.GetFromJsonAsync<ApiResult<ResellerDashboardData>>("api/reseller/dashboard", Json, ct);
        return res?.Data;
    }

    public async Task<PagedResult<ResellerTenantListItem>?> GetMyTenantsAsync(
        string? status = null, string? search = null,
        int page = 1, int size = 20, CancellationToken ct = default)
    {
        var qs = BuildQs(("status", status), ("search", search),
                         ("page", page.ToString()), ("size", size.ToString()));
        var res = await http.GetFromJsonAsync<ApiResult<PagedResult<ResellerTenantListItem>>>(
            $"api/reseller/tenants{qs}", Json, ct);
        return res?.Data;
    }

    public Task<PagedResult<ResellerTenantListItem>?> GetResellerTenantsAsync(
        string? status = null, string? search = null,
        int page = 1, int size = 20, CancellationToken ct = default)
        => GetMyTenantsAsync(status, search, page, size, ct);

    public async Task<ResellerTenantDetail?> GetMyTenantAsync(string tenantId, CancellationToken ct = default)
    {
        var res = await http.GetFromJsonAsync<ApiResult<ResellerTenantDetail>>(
            $"api/reseller/tenants/{tenantId}", Json, ct);
        return res?.Data;
    }

    public Task<ResellerTenantDetail?> GetResellerTenantDetailAsync(string tenantId, CancellationToken ct = default)
        => GetMyTenantAsync(tenantId, ct);

    public async Task<List<CommissionPolicyItem>?> GetMyCommissionPoliciesAsync(CancellationToken ct = default)
    {
        var res = await http.GetFromJsonAsync<ApiResult<List<CommissionPolicyItem>>>(
            "api/reseller/commissions/policies", Json, ct);
        return res?.Data;
    }

    public async Task<PagedResult<SettlementListItem>?> GetMySettlementsAsync(
        string? settlementMonth = null, string? status = null,
        int page = 1, int size = 20, CancellationToken ct = default)
    {
        var qs = BuildQs(("settlementMonth", settlementMonth), ("status", status),
                         ("page", page.ToString()), ("size", size.ToString()));
        var res = await http.GetFromJsonAsync<ApiResult<PagedResult<SettlementListItem>>>(
            $"api/reseller/commissions/settlements{qs}", Json, ct);
        return res?.Data;
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────────────

    private static string BuildQs(params (string key, string? value)[] pairs)
    {
        var parts = pairs
            .Where(p => !string.IsNullOrWhiteSpace(p.value))
            .Select(p => $"{p.key}={Uri.EscapeDataString(p.value!)}");
        var qs = string.Join("&", parts);
        return qs.Length > 0 ? "?" + qs : "";
    }
}
