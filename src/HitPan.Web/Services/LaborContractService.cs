using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

/// <summary>
/// 전자근로계약서 API 클라이언트 서비스이다.
/// - GET  /api/labor-contracts?employeeId=&status=
/// - GET  /api/labor-contracts/{id}
/// - POST /api/labor-contracts              (TenantAdmin)
/// - POST /api/labor-contracts/{id}/send    (발송)
/// - POST /api/labor-contracts/{id}/sign    (서명 첨부, body: { esignId })
/// </summary>
public sealed class LaborContractService(HttpClient http)
{
    /// <summary>
    /// 근로계약서 목록을 조회한다.
    /// </summary>
    public async Task<List<LaborContractListModel>> GetListAsync(
        string? employeeId = null,
        string? status = null,
        CancellationToken ct = default)
    {
        try
        {
            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(employeeId)) query.Add($"employeeId={Uri.EscapeDataString(employeeId)}");
            if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
            var url = "api/labor-contracts" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
            return await http.GetFromJsonAsync<List<LaborContractListModel>>(url, ct).ConfigureAwait(false)
                   ?? new List<LaborContractListModel>();
        }
        catch
        {
            return new List<LaborContractListModel>();
        }
    }

    /// <summary>
    /// 근로계약서 상세를 조회한다.
    /// </summary>
    public async Task<LaborContractDetailModel?> GetAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<LaborContractDetailModel>(
                $"api/labor-contracts/{Uri.EscapeDataString(id)}", ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 근로계약서를 생성한다. (TenantAdmin)
    /// 생성된 계약서 ID를 반환.
    /// </summary>
    public async Task<string?> CreateAsync(CreateLaborContractModel request, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/labor-contracts", request, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            var created = await res.Content.ReadFromJsonAsync<LaborContractDetailModel>(cancellationToken: ct)
                .ConfigureAwait(false);
            return created?.ContractId;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 근로계약서를 직원에게 발송한다.
    /// </summary>
    public async Task<bool> SendAsync(string id, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync(
                $"api/labor-contracts/{Uri.EscapeDataString(id)}/send",
                new { },
                ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 근로계약서에 서명을 첨부한다.
    /// </summary>
    public async Task<bool> AttachSignAsync(string id, string esignId, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync(
                $"api/labor-contracts/{Uri.EscapeDataString(id)}/sign",
                new { esignId },
                ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
