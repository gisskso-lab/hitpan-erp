namespace HitPan.Web.Services;

/// <summary>
/// 🔴 20260930작1 갈래 F — 「최신 버전 확인/업데이트」(수동 업데이트) 화면이 부르는 API.
/// </summary>
/// <remarks>
/// <para>
/// 사장님 용어(9/30): <b>자동</b> = 로그인 팝업 · <b>수동</b> = 메뉴로 직접. 이 클라이언트는 수동 쪽이다.
/// 자동 경로(<c>update-consent</c> · <c>update-consent-local</c>)는 <b>부르지 않는다</b>(G-43 · G-L1).
/// </para>
/// <para>
/// 문(門)은 서버가 막는다 — <c>TenantAdminOnly</c> + <c>[MainPcOnly]</c>(설계 §13-4). 주소·필드는 잠정 — 갈래 A·U 계약 대조 필요.
/// </para>
/// </remarks>
public sealed class ManualUpdateClient(HttpClient http, ILogger<ManualUpdateClient> logger)
{
    /// <summary>지금 판 · 받을 수 있는 최신 판 · 시작 가능 여부 · 안 되면 사유 코드 · 지난번 수동 업데이트 결과.</summary>
    public Task<LocalSwapCallResult<ManualUpdateStatus>> CheckAsync(CancellationToken ct = default) =>
        LocalSwapCall.GetAsync<ManualUpdateStatus>(http, logger, LocalSwapUiText.ApiUpdateStatus, ct);

    /// <summary>[예] 뒤 — 서버가 서명·해시·백업을 거친 뒤에만 교체 작업을 건다(설계 §13-2).</summary>
    public Task<LocalSwapCallResult<LocalSwapStartResult>> StartAsync(string toVersion, CancellationToken ct = default) =>
        LocalSwapCall.PostAsync<LocalSwapStartResult>(
            http, logger, LocalSwapUiText.ApiUpdateStart, new { toVersion }, ct);
}

/// <summary>수동 업데이트 상태(잠정 DTO — 계약 대조 필요).</summary>
public sealed class ManualUpdateStatus
{
    public string? CurrentVersion { get; set; }
    public string? LatestVersion { get; set; }
    public bool UpdateAvailable { get; set; }
    public bool CanStart { get; set; }
    public string? Reason { get; set; }

    /// <summary>지난번 수동 업데이트가 실패해 원래 판으로 돌려 두었으면 true(설계 §13-8 「업데이트 실패」 문구).</summary>
    public bool LastFailedRestored { get; set; }
    public string? LastFromVersion { get; set; }
}
