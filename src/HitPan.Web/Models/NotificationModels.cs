namespace HitPan.Web.Models;

/// <summary>
/// 화면에 뜨는 알림 한 건. 작(2026-08-13) 그룹웨어 단계2.
/// </summary>
/// <remarks>
/// 서버의 <c>AppNotification</c> 과 짝이다. 🔴 필드 이름이 어긋나면 알림이 빈 채로 뜬다 —
/// 8/12 계약서 급여가 정확히 그 병이었다(API 는 보내는데 화면이 다른 이름으로 받아 항상 0원).
/// </remarks>
public sealed class AppNotificationModel
{
    /// <summary>알림 종류. 아이콘·색을 고르는 데 쓴다. 예: <c>approval_pending</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>한 줄 제목. 예: "결재가 올라왔습니다".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>보조 설명. 예: "거래명세서 — 홍길동".</summary>
    public string? Body { get; set; }

    /// <summary>누르면 갈 곳. 예: <c>/approval/pending</c>.</summary>
    public string? Link { get; set; }

    /// <summary>읽음 여부. 서버가 아니라 화면에서만 쓴다(새로고침하면 초기화).</summary>
    public bool IsRead { get; set; }
}
