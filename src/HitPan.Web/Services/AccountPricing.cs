namespace HitPan.Web.Services;

/// <summary>
/// 계정 추가 단가 — 화면에 적는 값은 <b>이 한 곳</b>뿐이다 (9/25 결재 · 10/5 작3 P-6 · D-16).
/// </summary>
/// <remarks>부가세 문구 없음(결재 없음). 요금제 기본 가격은 적지 않는다. 단가를 본사가 보내는 길은 백오피스 트랙(설계 §10).</remarks>
public static class AccountPricing
{
    /// <summary>계정 1개 추가 월 요금(원).</summary>
    public const decimal ExtraAccountMonthlyWon = 10000m;

    /// <summary>안내 창 한 줄 — 「계정 1개 추가: 월 10,000원」</summary>
    public static string ExtraAccountLine => $"계정 1개 추가: 월 {ExtraAccountMonthlyWon:N0}원";
}
