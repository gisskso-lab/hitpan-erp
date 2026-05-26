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
}
