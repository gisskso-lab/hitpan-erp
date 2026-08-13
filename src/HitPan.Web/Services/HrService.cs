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
        // 🔴 봉합 (2026-08-13, 단계7): 종전엔 `catch { return new(); }` 였다.
        //    왜 실패했는지가 통째로 사라져 화면은 "0건" 으로 보이고 원인을 못 찾는다(헌법 #15).
        //    같은 파일 ApproveOvertimeAsync 는 이미 제대로 남기고 있었다 — 기준이 갈려 있었다.
        try { return await http.GetFromJsonAsync<List<AttendanceModel>>(q.TrimEnd('&', '?'), ct) ?? new(); }
        catch (Exception ex) { LogFailure(nameof(GetAttendanceAsync), ex); return new(); }
    }

    public async Task<bool> CheckInAsync(string? memo = null, CancellationToken ct = default)
    {
        try { using var r = await http.PostAsJsonAsync("api/hr/check-in", new { memo }, ct); return r.IsSuccessStatusCode; }
        catch (Exception ex) { LogFailure(nameof(CheckInAsync), ex); return false; }
    }

    public async Task<bool> CheckOutAsync(CancellationToken ct = default)
    {
        try { using var r = await http.PostAsJsonAsync("api/hr/check-out", new { }, ct); return r.IsSuccessStatusCode; }
        catch (Exception ex) { LogFailure(nameof(CheckOutAsync), ex); return false; }
    }

    // 초과근무
    public async Task<List<OvertimeModel>> GetOvertimeAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var q = "api/hr/overtime?";
        if (from.HasValue) q += $"from={from:yyyy-MM-dd}&";
        if (to.HasValue) q += $"to={to:yyyy-MM-dd}&";
        try { return await http.GetFromJsonAsync<List<OvertimeModel>>(q.TrimEnd('&', '?'), ct) ?? new(); }
        catch (Exception ex) { LogFailure(nameof(GetOvertimeAsync), ex); return new(); }
    }

    public async Task<bool> CreateOvertimeAsync(CreateOvertimeModel m, CancellationToken ct = default)
    {
        try { using var r = await http.PostAsJsonAsync("api/hr/overtime", m, ct); return r.IsSuccessStatusCode; }
        catch (Exception ex) { LogFailure(nameof(CreateOvertimeAsync), ex); return false; }
    }

    // 봉합 (2026-06-23, 19차): 초과근무 승인/반려 — action: approved | rejected.
    public async Task<bool> ApproveOvertimeAsync(string overtimeId, string action, CancellationToken ct = default)
    {
        try
        {
            using var r = await http.PostAsJsonAsync($"api/hr/overtime/{overtimeId}/approve", new { action }, ct);
            return r.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HrService.ApproveOvertimeAsync] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    // HR 경비
    public async Task<List<HrExpenseModel>> GetHrExpensesAsync(CancellationToken ct = default)
    {
        try { return await http.GetFromJsonAsync<List<HrExpenseModel>>("api/hr/expense-requests", ct) ?? new(); }
        catch (Exception ex) { LogFailure(nameof(GetHrExpensesAsync), ex); return new(); }
    }

    /// <summary>
    /// 실패한 이유를 남긴다(헌법 #15 — 빈 catch 금지).
    /// </summary>
    /// <remarks>
    /// 삼킨 예외는 없는 일이 된다. 화면은 "0건" 이나 "실패" 만 보여주고
    /// <b>왜 그런지는 아무 데도 안 남아</b> 나중에 원인을 못 찾는다.
    /// </remarks>
    private static void LogFailure(string where, Exception ex)
        => Console.Error.WriteLine($"[HrService.{where}] {ex.GetType().Name}: {ex.Message}");

    public async Task<bool> CreateHrExpenseAsync(CreateHrExpenseModel m, CancellationToken ct = default)
    {
        var (ok, _) = await CreateHrExpenseDetailedAsync(m, ct).ConfigureAwait(false);
        return ok;
    }

    /// <summary>
    /// 경비 신청. 작(2026-08-13) 단계7 — <b>결재가 실제로 올라갔는지</b>까지 돌려준다.
    /// </summary>
    /// <remarks>
    /// 🔴 단계3 P0-1 교훈: 종전엔 <c>IsSuccessStatusCode</c> 만 보고 "신청 완료" 를 띄웠다.
    /// 결재 설정이 꺼져 있으면 결재가 조용히 안 올라가는데 직원은 올라간 줄 안다 —
    /// 문서는 갇히고 결재함엔 안 뜬다. 그래서 서버가 알려주는 사실을 그대로 받는다.
    ///
    /// ⚠️ 옛 <see cref="CreateHrExpenseAsync"/> 는 <b>지우지 않는다</b>(헌법 #1).
    /// 다른 화면이 부르고 있을 수 있어, 그 자리는 그대로 두고 이쪽을 얹는다.
    /// </remarks>
    public async Task<(bool Ok, CreateHrExpenseResultModel? Result)> CreateHrExpenseDetailedAsync(
        CreateHrExpenseModel m, CancellationToken ct = default)
    {
        try
        {
            using var r = await http.PostAsJsonAsync("api/hr/expense-requests", m, ct)
                .ConfigureAwait(false);

            if (!r.IsSuccessStatusCode)
            {
                // "마감된 기간입니다" 같은 이유가 직원에게 닿아야 다음 행동을 안다.
                var msg = await ReadMessageAsync(r, ct).ConfigureAwait(false);
                return (false, msg is null ? null : new CreateHrExpenseResultModel { Message = msg });
            }

            var result = await r.Content
                .ReadFromJsonAsync<CreateHrExpenseResultModel>(cancellationToken: ct)
                .ConfigureAwait(false);

            return (true, result);
        }
        catch (Exception ex)
        {
            // 헌법 #15 — 빈 catch 금지. 종전엔 `catch { return false; }` 라 원인이 사라졌다.
            Console.Error.WriteLine($"[HrService] 경비 신청 실패: {ex.GetType().Name}: {ex.Message}");
            return (false, null);
        }
    }

    private async Task<string?> ReadMessageAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            var body = await res.Content
                .ReadFromJsonAsync<CreateHrExpenseResultModel>(cancellationToken: ct)
                .ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(body?.Message) ? null : body!.Message;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[HrService] 경비 오류 응답 해석 실패({(int)res.StatusCode}): {ex.GetType().Name}");
            return null;
        }
    }
}
