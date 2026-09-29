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
}
