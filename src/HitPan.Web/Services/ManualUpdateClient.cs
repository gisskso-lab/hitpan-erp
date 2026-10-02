using Microsoft.Extensions.Logging;

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
/// 문(門)은 서버가 막는다 — <c>TenantAdminOnly</c> + <c>[MainPcOnly]</c>(설계 §13-4).
/// 🟢 I-WEB(9/30) C-6 — 주소·칸은 <c>ManualUpdateController</c>(<c>api/manual-update</c> · check · apply · status)와 맞췄다.
/// </para>
/// <para>
/// ⬛ F 가 올린 이 파일은 주석이 글자 깨짐(한글 → ?)으로 들어가 있었다 — 코드는 ASCII 라 빌드는 됐다. I-WEB 이 주석을 다시 적었다(뜻은 같음).
/// </para>
/// </remarks>
public sealed class ManualUpdateClient(HttpClient http, ILogger<ManualUpdateClient> logger)
{
    /// <summary>최신 버전 확인 — 지금 판 · 받을 수 있는 최신 판 · 사유 코드 · 바쁨(읽기만 · 받기·백업 0).</summary>
    public Task<LocalSwapCallResult<ManualUpdateStatus>> CheckAsync(CancellationToken ct = default) =>
        LocalSwapCall.GetAsync<ManualUpdateStatus>(http, logger, LocalSwapUiText.ApiUpdateCheck, ct);

    /// <summary>[예] 뒤 — 서버가 서명·해시·백업을 거친 뒤에만 교체 일꾼에 넘긴다(설계 §13-2). 받아들이면 202 + 진행 상태.</summary>
    /// <remarks>🔴 [3-V] 적발 04 반영 — 버전·경로를 본문에 싣지 않는다(받을 판은 서버가 피드에서 계산 · 본문 = 빈 객체 · 입구는 서버 기본 <c>menu</c>).</remarks>
    public Task<LocalSwapCallResult<ManualUpdateJob>> StartAsync(CancellationToken ct = default) =>
        LocalSwapCall.PostAsync<ManualUpdateJob>(
            http, logger, LocalSwapUiText.ApiUpdateStart, LocalSwapUiText.EmptyBody, ct);

    /// <summary>진행 상태 — 일꾼에 넘기기 전까지만 있다. 없으면 204(<see cref="LocalSwapCallResult{T}.Status"/> = 204).</summary>
    public Task<LocalSwapCallResult<ManualUpdateJob>> GetJobAsync(CancellationToken ct = default) =>
        LocalSwapCall.GetAsync<ManualUpdateJob>(http, logger, LocalSwapUiText.ApiUpdateJob, ct);
}

/// <summary>
/// 「최신 버전 확인」 결과 — 서버 <c>ManualUpdateCheckResult</c> 와 같은 칸(게이트 F-C3 가 왕복으로 잰다).
/// </summary>
public sealed class ManualUpdateStatus
{
    public string? CurrentVersion { get; set; }
    public string? LatestVersion { get; set; }
    public bool UpdateAvailable { get; set; }
    public string? Reason { get; set; }
    public long? PackageSizeBytes { get; set; }
    public string? ReleaseNotes { get; set; }
    /// <summary>자동 업데이트·다른 교체가 도는 중.</summary>
    public bool Busy { get; set; }

    /// <summary>
    /// 지난 교체 결과(계약 §7). ⚠️ 지금 서버 <c>ManualUpdateCheckResult</c> 에는 이 칸이 없다 — 늘 null(명세서 I-WEB §4 계약 쪽 문제 1).
    /// 서버가 같은 이름(<c>last</c>)으로 싣는 날 화면이 그대로 받는다.
    /// </summary>
    public LocalSwapLast? Last { get; set; }

    /// <summary>[업데이트 하기] 를 켤 수 있나 — 새 판 있음 · 바쁘지 않음 · 사유 = ok.</summary>
    public bool CanStart =>
        UpdateAvailable && !Busy && !string.IsNullOrWhiteSpace(LatestVersion) &&
        (string.IsNullOrWhiteSpace(Reason) || string.Equals(Reason.Trim(), LocalSwapUiText.ReasonOk, StringComparison.OrdinalIgnoreCase));
}

/// <summary>수동 업데이트 진행 상태 — 서버 <c>ManualUpdateJobStatus</c> 와 같은 칸.</summary>
public sealed class ManualUpdateJob
{
    public string? JobId { get; set; }
    /// <summary>checking · downloading · verifying · backing_up · handing_off · handed_off · refused.</summary>
    public string? Stage { get; set; }
    public string? Reason { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
