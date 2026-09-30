using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace HitPan.Web.Services;

/// <summary>
/// 🔴 20260930작1 갈래 F — 「이전 버전으로 되돌리기」(수동 되돌리기) 화면이 부르는 API.
/// </summary>
/// <remarks>
/// 문(門)은 서버가 막는다 — <c>[Authorize(Policy="TenantAdminOnly")]</c> + <c>[MainPcOnly]</c>(설계 §13-4).
/// 화면은 서버 판정을 믿고 결과를 고객 문구로 옮길 뿐이다(<see cref="LocalSwapUiText"/>).
/// 주소·필드 이름은 잠정(설계 §0 · §13) — 갈래 A 계약 문서와 대조 필요.
/// </remarks>
public sealed class LocalRollbackClient(HttpClient http, ILogger<LocalRollbackClient> logger)
{
    /// <summary>되돌릴 수 있나 · 무엇으로(바로 이전 한 판) · 안 되면 사유 코드.</summary>
    public Task<LocalSwapCallResult<LocalRollbackStatus>> GetStatusAsync(CancellationToken ct = default) =>
        LocalSwapCall.GetAsync<LocalRollbackStatus>(http, logger, LocalSwapUiText.ApiRollbackStatus, ct);

    /// <summary>[예] 뒤 — 서버가 전제를 다시 판정한다(화면 값 불신 · 설계 §2).</summary>
    /// <remarks>
    /// 🔴 [3-V] 적발 04 반영(PM 지시 9/30) — 화면은 버전·경로를 요청 본문에 <b>싣지 않는다</b>. 무엇으로 되돌릴지는 서버가 계산한다.
    /// 본문은 빈 객체 하나(<see cref="LocalSwapUiText.EmptyBody"/>).
    /// </remarks>
    public Task<LocalSwapCallResult<LocalSwapStartResult>> StartAsync(CancellationToken ct = default) =>
        LocalSwapCall.PostAsync<LocalSwapStartResult>(
            http, logger, LocalSwapUiText.ApiRollbackStart, LocalSwapUiText.EmptyBody, ct);
}

/// <summary>되돌리기 상태(잠정 DTO — 계약 대조 필요).</summary>
public sealed class LocalRollbackStatus
{
    public string? CurrentVersion { get; set; }
    /// <summary>되돌아갈 판 = 현재의 바로 앞 판(설계 §3-3). 재료가 없으면 null.</summary>
    public string? TargetVersion { get; set; }
    public bool CanRollback { get; set; }
    public string? Reason { get; set; }
}

/// <summary>[예] 뒤 서버 응답(잠정 DTO — 계약 대조 필요).</summary>
public sealed class LocalSwapStartResult
{
    public bool Accepted { get; set; }
    public string? Reason { get; set; }
    public string? Ticket { get; set; }
}

/// <summary>호출 한 번의 결과 — 성공이면 <see cref="Data"/>, 아니면 상태·사유 코드.</summary>
public sealed class LocalSwapCallResult<T> where T : class
{
    public bool Ok { get; init; }
    public int Status { get; init; }
    public string? Reason { get; init; }
    public T? Data { get; init; }

    /// <summary>실패를 고객 문구로(개발용어 0).</summary>
    public string FailureText => LocalSwapUiText.HttpFailureText(Status, Reason);
}

/// <summary>두 클라이언트가 같이 쓰는 요청 한 벌(복붙 0).</summary>
internal static class LocalSwapCall
{
    public static async Task<LocalSwapCallResult<T>> GetAsync<T>(
        HttpClient http, ILogger logger, string url, CancellationToken ct) where T : class
    {
        try
        {
            using var res = await http.GetAsync(url, ct);
            return await ReadAsync<T>(res, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "[LocalSwap] 조회 실패 {Url}", url);
            return new LocalSwapCallResult<T> { Ok = false, Status = 0 };
        }
    }

    public static async Task<LocalSwapCallResult<T>> PostAsync<T>(
        HttpClient http, ILogger logger, string url, object body, CancellationToken ct) where T : class
    {
        try
        {
            using var res = await http.PostAsJsonAsync(url, body, ct);
            return await ReadAsync<T>(res, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "[LocalSwap] 요청 실패 {Url}", url);
            return new LocalSwapCallResult<T> { Ok = false, Status = 0 };
        }
    }

    private static async Task<LocalSwapCallResult<T>> ReadAsync<T>(HttpResponseMessage res, CancellationToken ct) where T : class
    {
        var status = (int)res.StatusCode;
        if (res.IsSuccessStatusCode)
        {
            var data = await res.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
            return new LocalSwapCallResult<T> { Ok = data is not null, Status = status, Data = data };
        }

        var text = await res.Content.ReadAsStringAsync(ct);
        return new LocalSwapCallResult<T> { Ok = false, Status = status, Reason = LocalSwapUiText.ExtractCode(text) };
    }
}
