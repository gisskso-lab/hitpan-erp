using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

/// <summary>인사·근태 API 클라이언트</summary>
public sealed class HrClientService(HttpClient http)
{
    // 출퇴근
    public async Task<List<AttendanceModel>> GetAttendanceAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var q = "api/hr/attendance?";
        if (from.HasValue) q += $"from={from:yyyy-MM-dd}&";
        if (to.HasValue) q += $"to={to:yyyy-MM-dd}&";
        try { return await http.GetFromJsonAsync<List<AttendanceModel>>(q.TrimEnd('&', '?'), ct) ?? new(); }
        catch { return new(); }
    }

    public async Task<bool> CheckInAsync(string? memo = null, CancellationToken ct = default)
    {
        try { using var r = await http.PostAsJsonAsync("api/hr/check-in", new { memo }, ct); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    public async Task<bool> CheckOutAsync(CancellationToken ct = default)
    {
        try { using var r = await http.PostAsJsonAsync("api/hr/check-out", new { }, ct); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    // 초과근무
    public async Task<List<OvertimeModel>> GetOvertimeAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var q = "api/hr/overtime?";
        if (from.HasValue) q += $"from={from:yyyy-MM-dd}&";
        if (to.HasValue) q += $"to={to:yyyy-MM-dd}&";
        try { return await http.GetFromJsonAsync<List<OvertimeModel>>(q.TrimEnd('&', '?'), ct) ?? new(); }
        catch { return new(); }
    }

    public async Task<bool> CreateOvertimeAsync(CreateOvertimeModel m, CancellationToken ct = default)
    {
        try { using var r = await http.PostAsJsonAsync("api/hr/overtime", m, ct); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    // HR 경비
    public async Task<List<HrExpenseModel>> GetHrExpensesAsync(CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<List<HrExpenseModel>>("api/hr/expense-requests", ct) ?? new(); }
        catch { return new(); }
    }

    public async Task<bool> CreateHrExpenseAsync(CreateHrExpenseModel m, CancellationToken ct = default)
    {
        try { using var r = await http.PostAsJsonAsync("api/hr/expense-requests", m, ct); return r.IsSuccessStatusCode; }
        catch { return false; }
    }
}
