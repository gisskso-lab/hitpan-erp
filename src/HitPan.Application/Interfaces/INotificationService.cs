namespace HitPan.Application.Interfaces;

/// <summary>
/// 앱 내 실시간 알림 발송기. 작(2026-08-13) 그룹웨어 단계2.
/// </summary>
/// <remarks>
/// <para>
/// 김삼성 상무: <i>"'결재가 올라왔습니다' 알림부터 붙이세요. 그러면 직원이 메신저를 켤 이유가 생깁니다."</i>
/// 사장님: <i>"히트판ERP내 알림되도록(인스타나 페이스북처럼)"</i>.
/// </para>
/// <para>
/// 🔴 <b>인터페이스로 가르는 이유</b> — Application 계층은 SignalR 을 몰라야 한다.
/// 구현(<c>SignalRNotificationService</c>)은 API 계층에 두고 여기서는 "누구에게 무엇을" 만 말한다.
/// </para>
/// <para>
/// 🔴 <b>알림 실패가 업무를 막으면 안 된다.</b> 결재는 이미 저장됐는데 알림 전송이 실패했다고
/// 결재까지 되돌리면 그게 더 큰 사고다. 구현체는 예외를 밖으로 던지지 않는다
/// (다만 삼키지도 않는다 — 로그는 남긴다, 헌법 #15).
/// </para>
/// </remarks>
public interface INotificationService
{
    /// <summary>
    /// 사원 한 명에게 알림을 보낸다. 상대가 접속해 있지 않으면 조용히 지나간다.
    /// </summary>
    /// <param name="tenantId">테넌트(헌법 #2 — 호출부가 JWT 에서 받은 값을 넘긴다).</param>
    /// <param name="employeeId">받는 사람. 🔴 user_id 가 아니라 <b>사원 ID</b> 다(결재 체계와 동일).</param>
    /// <param name="notification">보낼 내용.</param>
    Task NotifyEmployeeAsync(string tenantId, string employeeId,
        AppNotification notification, CancellationToken ct = default);
}

/// <summary>
/// 화면에 뜨는 알림 한 건.
/// </summary>
/// <remarks>
/// 🔴 <b>금액·급여 같은 민감값을 본문에 넣지 않는다.</b> 알림은 화면에 떠 있는 동안
/// 옆 사람에게도 보인다. 무엇이 왔는지만 알리고, 자세한 내용은 눌러서 보게 한다.
/// </remarks>
public sealed class AppNotification
{
    /// <summary>알림 종류. 화면이 아이콘·색을 고르는 데 쓴다. 예: <c>approval_pending</c>.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>한 줄 제목. 예: "결재가 올라왔습니다".</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>보조 설명. 예: "거래명세서 — 홍길동".</summary>
    public string? Body { get; init; }

    /// <summary>누르면 갈 곳. 예: <c>/approval/pending</c>.</summary>
    public string? Link { get; init; }
}
