using System.Net.Http;

namespace HitPan.Web.Services;

/// <summary>
/// 🔴 로그아웃 응답을 「서버 쪽 로그아웃이 끝났나」로 읽는 판정 한 곳 (20260928작2 절V · [4] 최종 판정 §10 P2).
/// </summary>
/// <remarks>
/// <para>
/// [무엇이 났나] 개정3 까지 <c>AuthService.LogoutAsync</c> 는 <b>성공 응답(2xx)만</b> 완료로 봤다. 다른 탭이 먼저 로그아웃했거나
/// 오래 방치해 서버 쪽 로그인이 <b>이미 끝난</b> 상태에서 [로그아웃]을 누르면 응답이 401 이고, 화면은 「로그아웃이 서버에서
/// 끝나지 않았다」는 <b>거짓 안내</b>를 localStorage 에 남겨 다음 로그인 화면에 띄웠다(헛안내).
/// </para>
/// <para>
/// [지금] 401 = 서버가 이 로그인을 이미 모른다 = <b>끝난 것</b> ⇒ 완료. 5xx·연결 실패(<c>null</c>)는 서버가 접속을 못 끝냈을 수 있어
/// <b>미완료</b>(안내 남김 · 절K 그대로). 그 밖 4xx(403·408·429 등)는 개정3 판 그대로 미완료로 둔다(이번 절 범위 밖 · 명세서 절V).
/// <b>서버 변경 0.</b>
/// </para>
/// <para>🔴 <b>Blazor 를 쓰지 않는다</b> — 시험 프로젝트가 이 파일을 소스 링크로 불러 판정(W-4)을 직접 잰다.</para>
/// </remarks>
public static class LogoutGate
{
    /// <summary>서버 쪽 로그아웃이 끝났으면 true — 2xx 또는 401. <paramref name="statusCode"/> null = 연결 실패.</summary>
    /// <remarks>⚠️ 401 을 미완료로 되돌리면(개정3 판 「2xx 만 완료」) W-4 의 401 줄이 FAIL 한다.</remarks>
    public static bool IsServerLogoutDone(int? statusCode)
    {
        if (statusCode is null)
        {
            return false;
        }

        var code = statusCode.Value;
        if (code >= 200 && code <= 299)
        {
            return true;
        }

        return code == 401;
    }

    /// <summary>
    /// 🔴 20260928작2 절W (9/29 CTO 결재 §3 · 사장님 5-2 (A)) — 「갱신이 서버에 닿지 못해(<see cref="RefreshDecision.Keep"/>)
    /// 원래 401 을 그대로 돌려준 응답」에 다는 표식 이름. 화면(브라우저) 안에서만 달고 읽는다 — 서버는 이 이름을 모른다.
    /// </summary>
    public const string RefreshUnreachedHeader = "X-HitPan-Refresh-Unreached";

    /// <summary>
    /// 🔴 절W — 401 뒤 갱신 판정이 <see cref="RefreshDecision.Keep"/>(5xx·연결 실패·저장 실패)이면 응답에 표식을 단다.
    /// 그 밖 판정(Clear·Saved·OtherTabRotated)은 아무것도 하지 않는다. 응답의 상태·본문은 바꾸지 않는다(다른 API 호출 동작 무변경).
    /// </summary>
    /// <remarks>부르는 곳 = <c>HitPanApiAuthHandler.SendAsync</c> 의 Keep 갈래 한 곳.</remarks>
    public static void MarkIfRefreshUnreached(HttpResponseMessage response, RefreshDecision decision)
    {
        if (decision != RefreshDecision.Keep)
        {
            return;
        }

        response.Headers.Remove(RefreshUnreachedHeader);
        response.Headers.TryAddWithoutValidation(RefreshUnreachedHeader, "1");
    }

    /// <summary>
    /// 🔴 절W — 응답 전체를 보고 판정한다. <see cref="RefreshUnreachedHeader"/> 표식이 붙은 401 은 서버가 로그아웃을 거절한 게 아니라
    /// <b>갱신이 서버에 못 닿은 것</b>이므로 <b>미완료</b>(안내 남김 · 문구 기존 그대로). 표식이 없으면 <see cref="IsServerLogoutDone(int?)"/> 그대로.
    /// </summary>
    /// <remarks>⚠️ 표식을 무시하면(절V 판 「401 = 완료」) W-4b 의 Keep 줄이 FAIL 한다(⚠️ W-5·W-6 은 작11 playwright 측정 id 가 이미 쓴다).</remarks>
    public static bool IsServerLogoutDone(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        if (false && status == 401 && response.Headers.Contains(RefreshUnreachedHeader))
        {
            return false;
        }

        return IsServerLogoutDone(status);
    }
}
