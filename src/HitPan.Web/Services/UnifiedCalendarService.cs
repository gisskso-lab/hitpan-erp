using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

/// <summary>
/// 통합 캘린더 API 클라이언트 서비스.
/// 사장님 결재 2026-05-26 (통합 캘린더 + 카드 모달 가도).
/// </summary>
public sealed class UnifiedCalendarService(HttpClient http)
{
    /// <summary>월간 4축 통합 데이터 조회.</summary>
    public async Task<UnifiedCalendarModel?> GetMonthlyAsync(int year, int month, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<UnifiedCalendarModel>(
                $"api/dashboard/unified-calendar?year={year}&month={month}", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[UnifiedCalendarService.GetMonthlyAsync] {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>특정 날짜 4축 상세 조회 (카드 모달).</summary>
    public async Task<UnifiedDayDetailModel?> GetDayDetailAsync(DateTime date, CancellationToken ct = default)
    {
        try
        {
            var iso = date.ToString("yyyy-MM-dd");
            return await http.GetFromJsonAsync<UnifiedDayDetailModel>(
                $"api/dashboard/day-detail?date={iso}", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[UnifiedCalendarService.GetDayDetailAsync] {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    // ─── 사장님 결재 2026-05-26 가도: schedules CRUD 4종 ───
    // §#15 빈 catch 금지 / §#22 본사 0 (모두 고객 PC 로컬 호출)

    /// <summary>일정 생성.</summary>
    public async Task<string?> CreateScheduleAsync(CreateScheduleModel model, CancellationToken ct = default)
    {
        try
        {
            var res = await http.PostAsJsonAsync("api/dashboard/schedules", model, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[UnifiedCalendarService.CreateScheduleAsync] HTTP {(int)res.StatusCode}");
                return null;
            }
            var payload = await res.Content.ReadFromJsonAsync<ScheduleIdResponse>(cancellationToken: ct).ConfigureAwait(false);
            return payload?.ScheduleId;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[UnifiedCalendarService.CreateScheduleAsync] {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>일정 수정.</summary>
    public async Task<bool> UpdateScheduleAsync(string id, UpdateScheduleModel model, CancellationToken ct = default)
    {
        try
        {
            var res = await http.PutAsJsonAsync($"api/dashboard/schedules/{id}", model, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[UnifiedCalendarService.UpdateScheduleAsync] HTTP {(int)res.StatusCode}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[UnifiedCalendarService.UpdateScheduleAsync] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>일정 삭제.</summary>
    public async Task<bool> DeleteScheduleAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var res = await http.DeleteAsync($"api/dashboard/schedules/{id}", ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[UnifiedCalendarService.DeleteScheduleAsync] HTTP {(int)res.StatusCode}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[UnifiedCalendarService.DeleteScheduleAsync] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>완료 토글. isCompleted=null이면 서버 토글, 값 지정 시 강제 설정.</summary>
    public async Task<bool> ToggleCompleteAsync(string id, bool? isCompleted = null, CancellationToken ct = default)
    {
        try
        {
            var body = isCompleted.HasValue ? new { isCompleted = isCompleted.Value } : null;
            var req = new HttpRequestMessage(HttpMethod.Patch, $"api/dashboard/schedules/{id}/complete")
            {
                Content = System.Net.Http.Json.JsonContent.Create(body)
            };
            var res = await http.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[UnifiedCalendarService.ToggleCompleteAsync] HTTP {(int)res.StatusCode}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[UnifiedCalendarService.ToggleCompleteAsync] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private sealed class ScheduleIdResponse
    {
        public string? ScheduleId { get; set; }
    }
}
