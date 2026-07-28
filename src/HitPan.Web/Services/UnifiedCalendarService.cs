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
        return await GetJsonSafeAsync<UnifiedCalendarModel>(
            $"api/dashboard/unified-calendar?year={year}&month={month}",
            nameof(GetMonthlyAsync), ct).ConfigureAwait(false);
    }

    /// <summary>특정 날짜 4축 상세 조회 (카드 모달).</summary>
    public async Task<UnifiedDayDetailModel?> GetDayDetailAsync(DateTime date, CancellationToken ct = default)
    {
        var iso = date.ToString("yyyy-MM-dd");
        return await GetJsonSafeAsync<UnifiedDayDetailModel>(
            $"api/dashboard/day-detail?date={iso}",
            nameof(GetDayDetailAsync), ct).ConfigureAwait(false);
    }

    // 근본 봉합 (사장님 결재 2026-06-26): 대시보드 첫 진입 시 캘린더가 부팅 초기에 호출되면,
    //   인증 핸들러 토큰 주입·BaseAddress 정합 직전 경합으로 응답이 JSON 아닌 HTML(SPA index)일 수 있다.
    //   종전엔 GetFromJsonAsync 가 그 '<' 를 파싱하려다 JsonException → 콘솔 빨간 로그(신규 고객도 봄, 헌법 #19).
    //   해법: 먼저 응답을 받아 Content-Type 이 JSON 일 때만 역직렬화. HTML/빈응답이면 조용히 null(기능 무손상).
    private async Task<T?> GetJsonSafeAsync<T>(string url, string caller, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return default;
            }

            var mediaType = resp.Content.Headers.ContentType?.MediaType;
            if (mediaType is null || !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                // JSON 이 아니면(=HTML SPA 폴백 등) 파싱 시도조차 안 한다 → JsonException 원천 차단.
                return default;
            }

            return await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // §#15 빈 catch 금지 — 단, '<' 파싱예외는 위에서 차단되므로 여기엔 실제 네트워크 오류만 도달.
            Console.Error.WriteLine($"[UnifiedCalendarService.{caller}] {ex.GetType().Name}: {ex.Message}");
            return default;
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
