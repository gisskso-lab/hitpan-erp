using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

/// <summary>
/// 연차 신청/승인/반려 API 클라이언트 서비스이다.
/// </summary>
public sealed class LeaveRequestService(HttpClient http)
{
    public async Task<List<LeaveRequestModel>> GetListAsync(string? employeeId = null, CancellationToken ct = default)
    {
        try
        {
            var path = string.IsNullOrWhiteSpace(employeeId)
                ? "api/leave-requests"
                : $"api/leave-requests?employeeId={Uri.EscapeDataString(employeeId)}";
            return await http.GetFromJsonAsync<List<LeaveRequestModel>>(path, ct).ConfigureAwait(false)
                   ?? new List<LeaveRequestModel>();
        }
        catch
        {
            return new List<LeaveRequestModel>();
        }
    }

    public async Task<bool> CreateAsync(CreateLeaveRequestModel request, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/leave-requests", request, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ApproveAsync(string id, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync(
                $"api/leave-requests/{Uri.EscapeDataString(id)}/approve",
                new { requestId = id, approved = true, rejectReason = (string?)null },
                ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RejectAsync(string id, string reason, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync(
                $"api/leave-requests/{Uri.EscapeDataString(id)}/reject",
                new { requestId = id, approved = false, rejectReason = reason },
                ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
