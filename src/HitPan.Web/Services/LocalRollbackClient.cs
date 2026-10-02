using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace HitPan.Web.Services;

/// <summary>
/// 🔴 20260930작1 갈래 F — 「이전 버전으로 되돌리기」(수동 되돌리기) 화면이 부르는 API.
/// </summary>
/// <remarks>
/// 문(門)은 서버가 막는다 — <c>[Authorize(Policy="TenantAdminOnly")]</c> + <c>[MainPcOnly]</c>(설계 §13-4).
/// 화면은 서버 판정을 믿고 결과를 고객 문구로 옮길 뿐이다(<see cref="LocalSwapUiText"/>).
/// ⬛ 주소·필드 이름은 잠정이었다(F) → 🟢 I-WEB(9/30) 이 <c>LocalRollbackController</c> 라우트·DTO 와 맞췄다(C-5·C-6·C-7).
/// </remarks>
public sealed class LocalRollbackClient(HttpClient http, ILogger<LocalRollbackClient> logger)
{
    /// <summary>되돌릴 수 있나 · 무엇으로(바로 이전 한 판) · 안 되면 사유 코드.</summary>
    public Task<LocalSwapCallResult<LocalRollbackStatus>> GetStatusAsync(CancellationToken ct = default) =>
        LocalSwapCall.GetAsync<LocalRollbackStatus>(http, logger, LocalSwapUiText.ApiRollbackStatus, ct);

    /// <summary>[예] 뒤 — 서버가 전제를 다시 판정한다(화면 값 불신 · 설계 §2).</summary>
    /// <remarks>
    /// 🔴 [3-V] 적발 04 반영(PM 지시 9/30) — 화면은 버전·경로를 요청 본문에 <b>싣지 않는다</b>. 무엇으로 되돌릴지는 서버가 계산한다.
    /// 🔴 C-5(I-WEB 9/30) — 서버 <c>LocalRollbackStartBody</c> 는 <b>1회용 확인 번호 하나</b>를 받는다(없으면 <c>ticket_invalid</c>).
    /// 본문 = <c>{ "ticket": "…" }</c>(<see cref="LocalSwapUiText.RollbackStartBody"/>) — 조회(<see cref="GetStatusAsync"/>) 때 받은 값 그대로.
    /// 번호는 한 번 쓰면 사라진다 ⇒ 실패 뒤에는 다시 조회해 새 번호를 받는다(화면 몫).
    /// </remarks>
    public Task<LocalSwapCallResult<LocalSwapStartResult>> StartAsync(string? ticket, CancellationToken ct = default) =>
        LocalSwapCall.PostAsync<LocalSwapStartResult>(
            http, logger, LocalSwapUiText.ApiRollbackStart, LocalSwapUiText.RollbackStartBody(ticket), ct);
}

/// <summary>
/// 되돌리기 상태 — 서버 <c>HitPan.API.Services.LocalRollback.LocalRollbackStatus</c> 와 같은 칸(C-6 · 게이트 F-C3 가 왕복으로 잰다).
/// </summary>
public sealed class LocalRollbackStatus
{
    public bool CanRollback { get; set; }
    public string? Reason { get; set; }
    public string? CurrentVersion { get; set; }
    /// <summary>되돌아갈 판 = 현재의 바로 앞 판(설계 §3-3). 재료가 없으면 null.</summary>
    public string? TargetVersion { get; set; }
    /// <summary>재료 종류(<c>prev</c>·<c>staging_zip</c> · 확대 1.3.50 세 번째 길 <c>manual_zip</c>) — 화면은 보이지 않는다(개발용어).</summary>
    public string? MaterialKind { get; set; }
    /// <summary>1회용 확인 번호(32자) — [예] 본문에 이것만 싣는다.</summary>
    public string? Ticket { get; set; }
    /// <summary>번호 유효 시간(분).</summary>
    public int TicketMinutes { get; set; }
    /// <summary>지난 교체 한 번의 결과(두 모드 공통 · 계약 §7) — 없으면 null.</summary>
    public LocalSwapLast? Last { get; set; }
    /// <summary>
    /// 🔴 20260930작1 확대 1.3.50 갈래 N3(설계 19-3 계약) — 세 번째 길(본사 보관본 받기) [예] 뒤 받기 진행. 기본 null.
    /// 서버가 메모리에만 들고 있다(API 가 다시 뜨면 사라짐 → 넘긴 뒤 결과는 <see cref="Last"/>).
    /// </summary>
    public LocalRollbackFetch? Fetch { get; set; }
}

/// <summary>
/// 받기 진행 한 칸 — 설계 19-3 <c>{ stage, reason, to, atUtc }</c>. <c>stage</c> 는 기존 수동 업데이트 단계 글자
/// (downloading · verifying · handing_off · handed_off · refused) · <c>reason</c> 은 기존 사유 코드(새 사유 0).
/// </summary>
public sealed class LocalRollbackFetch
{
    public string? Stage { get; set; }
    public string? Reason { get; set; }
    public string? To { get; set; }
    public DateTime AtUtc { get; set; }
}

/// <summary>지난 교체 결과 — 서버 <c>LocalSwapLastResult</c> 와 같은 칸.</summary>
public sealed class LocalSwapLast
{
    public string? Mode { get; set; }
    public string? State { get; set; }
    public string? Reason { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public DateTime AtUtc { get; set; }
}

/// <summary>되돌리기 [예] 뒤 서버 응답 — 202 <c>{ started: true, reason }</c> · 409 <c>{ started: false, reason }</c>.</summary>
public sealed class LocalSwapStartResult
{
    public bool Started { get; set; }
    public string? Reason { get; set; }
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
        // 204 = 내용 없음(수동 업데이트 진행 상태가 없을 때) — 본문을 읽지 않는다(빈 본문을 JSON 으로 읽으면 예외).
        if (res.StatusCode == System.Net.HttpStatusCode.NoContent)
            return new LocalSwapCallResult<T> { Ok = false, Status = status };
        if (res.IsSuccessStatusCode)
        {
            var data = await res.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
            return new LocalSwapCallResult<T> { Ok = data is not null, Status = status, Data = data };
        }

        var text = await res.Content.ReadAsStringAsync(ct);
        return new LocalSwapCallResult<T> { Ok = false, Status = status, Reason = LocalSwapUiText.ExtractCode(text) };
    }
}
