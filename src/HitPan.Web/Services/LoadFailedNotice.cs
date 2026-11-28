using System.Net;
using MudBlazor;

namespace HitPan.Web.Services;

/// <summary>
/// 화면오류기록띠 대표 ③(작업지시서 §8-2 · 2026-11-28) — <b>사람이 연 화면</b>의 조회가 실패했을 때 띄우는 쉬운 알림 한 자리.
/// </summary>
/// <remarks>
/// <para>
/// 종전엔 서비스가 실패를 <c>Console.Error</c> 로 찍어 흰 띠(「오류가 났습니다 · 새로고침」)가 떴고,
/// 그 띠가 화면의 <b>유일한 실패 신호</b>였다(어음 · 카드 · 통장 · 반품현황 전표별 · 대시보드 달력).
/// 기록을 <c>LogWarning</c> 으로 바꾸면 띠가 사라지므로, 같은 자리에 대시보드와 같은 글을 한 번 띄운다.
/// </para>
/// <para>
/// 알림을 안 띄우는 경우: 화면을 떠나 취소된 조회(시간 초과는 띄움) · 401 · 403(공통 처리기가 이미 알림 — 겹치지 않게).
/// 5분 자동 조회(CS 새 답글 배지 · 사이드바 CS 수)는 이 자리를 부르지 않는다(기록만).
/// </para>
/// </remarks>
internal static class LoadFailedNotice
{
    /// <summary>대표 결재 글(대시보드와 같은 꼴).</summary>
    public const string Text = "정보를 불러오지 못했습니다. 잠시 후 다시 시도해 주세요.";

    /// <summary>알림을 띄울 실패인가 — 취소(시간 초과 아님) · 401 · 403 은 거짓.</summary>
    public static bool ShouldNotify(Exception ex) => ex switch
    {
        OperationCanceledException oce when oce.InnerException is not TimeoutException => false,
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } => false,
        _ => true,
    };

    /// <summary>띄울 실패면 쉬운 알림 한 번.</summary>
    public static void Show(ISnackbar snackbar, Exception ex)
    {
        if (ShouldNotify(ex)) snackbar.Add(Text, Severity.Warning);
    }
}
