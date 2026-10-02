using HitPan.API.Services.LocalSwap;

namespace HitPan.API.Services.LocalRollback;

/// <summary>
/// 수동 되돌리기(자료관리 「이전 버전으로 되돌리기」) — 20260930작1 갈래 A · 설계 §13.
/// 바로 이전 한 판만 · 메인PC + 관리자만(컨트롤러 문) · 고객 자료 불변(자료 연결 0).
/// </summary>
public interface ILocalRollbackService
{
    /// <summary>
    /// 지금 되돌릴 수 있나 + 경고 화면이 [예] 때 돌려줄 1회용 확인 번호(10분).
    /// 화면은 이 값을 보여 주기만 하고, [예] 때 번호만 보낸다.
    /// </summary>
    LocalRollbackStatus GetStatus(string userId);

    /// <summary>
    /// [예] — 번호가 같은 사용자·10분 안·같은 재료일 때만 교체를 건다. 판·재료는 서버가 <b>다시</b> 계산한다(화면 값 불신).
    /// </summary>
    SwapLaunchResult Start(string userId, string? ticket);
}

/// <summary>상태 응답(갈래 F 가 읽는 모양).</summary>
/// <param name="CanRollback">지금 [예] 를 받을 수 있나.</param>
/// <param name="Reason">사유 코드(계약 §6) — 열 수 있으면 <c>ok</c>.</param>
/// <param name="CurrentVersion">지금 판.</param>
/// <param name="TargetVersion">되돌릴 판(열 수 있을 때).</param>
/// <param name="MaterialKind"><c>prev</c> · <c>staging_zip</c>.</param>
/// <param name="Ticket">1회용 확인 번호(열 수 있을 때).</param>
/// <param name="TicketMinutes">번호 유효 분.</param>
/// <param name="Last">마지막 교체 결과(다음 로그인 뒤 알림 · 두 모드 공통).</param>
/// <param name="Fetch">
/// 20260930작1 1.3.50 확대 N2(설계 §19-3) — 세 번째 길(본사 보관 이전 판 받기)의 진행. 받기를 건 적이 없으면 null.
/// 메모리만(API 가 다시 뜨면 사라진다 — 넘긴 뒤 결과는 <see cref="Last"/>).
/// </param>
public sealed record LocalRollbackStatus(
    bool CanRollback,
    string Reason,
    string CurrentVersion,
    string? TargetVersion,
    string? MaterialKind,
    string? Ticket,
    int TicketMinutes,
    LocalSwapLastResult? Last,
    LocalRollbackFetchStatus? Fetch = null);

/// <summary>
/// 20260930작1 1.3.50 확대 N2(설계 §19-3) — 세 번째 길 받기 진행 한 칸.
/// </summary>
/// <param name="Stage">기존 <c>ManualUpdateStages</c> 글자 — <c>downloading</c>·<c>verifying</c>·<c>handing_off</c>·<c>handed_off</c>·<c>refused</c>.</param>
/// <param name="Reason">멈춘 사유(19-3 · 새 사유 0) — 끝나기 전·넘긴 뒤는 null.</param>
/// <param name="To">되돌릴 판.</param>
/// <param name="AtUtc">이 단계로 옮긴 때.</param>
public sealed record LocalRollbackFetchStatus(string Stage, string? Reason, string To, DateTime AtUtc);

/// <summary>마지막 교체 결과 — 요청서 끝 상태.</summary>
public sealed record LocalSwapLastResult(string Mode, string State, string? Reason, string From, string To, DateTime AtUtc);
