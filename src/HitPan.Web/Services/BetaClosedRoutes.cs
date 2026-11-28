namespace HitPan.Web.Services;

/// <summary>
/// 🔴 W-S-01 갈래 A (2026-11-27 · 작업지시서 20261127작 §2 · 설계 §2-7) — 지금 판에서 닫아 둔 화면 주소 5개의 「한 장치」.
/// </summary>
/// <remarks>
/// <para>
/// 닫힌 주소 = 01a-6 사용환경 설정 · 01a-10 도우미 연동 · 01b-06 판매수익성분석 · 01b-11 세금계산서 통계 · 01b-27 세무사 자료 보내기.
/// 메뉴(사이드바 5줄 · 전체 메뉴 3줄 · 도우미 바 링크 1)는 이 규칙으로 감추고, 직접 주소는 <c>Pages/BetaClosedNoticePage.razor</c> 가 받아 안내한다.
/// </para>
/// <para>
/// ⚠️ 화면 숨김은 <b>보조</b>다. 막는 본체는 서버(<c>[NonAction]</c> · 도구 · 분석 서비스 깃발)다. 01b-11 은 매입매출장과 서버를 같이 써서 화면만 닫는다.
/// </para>
/// <para>
/// Blazor 의존 0 순수 C# — 시험 프로젝트가 소스를 링크해 직접 부른다(<see cref="DataResetMenuRule"/> 선례).
/// 다시 열 때(정식 판) = 아래 목록에서 그 주소 한 줄을 빼고, 그 화면의 ⬛ 주석으로 남긴 <c>@page</c> 줄을 되살린다.
/// </para>
/// <para>
/// 🔴 W-S-03 (2026-11-28 · 작업지시서 20261128작 §2 · 설계 §2-2) — 17·S18 첫 방문 환영(<c>/welcome</c>)을 더했다(닫힌 주소 6).
/// 메뉴 · 바로가기 0 · 로그인 분기 1(<see cref="AfterLoginPath"/>) · 직접 주소 1(안내 화면). 서버 손 0(<c>RedirectToWelcome</c> 칸은 계속 내려옴 · #37).
/// </para>
/// </remarks>
public static class BetaClosedRoutes
{
    /// <summary>01a-6 사용환경 설정.</summary>
    public const string Settings = "/settings";

    /// <summary>01a-10 도우미 연동.</summary>
    public const string AiAssistant = "/settings/ai-assistant";

    /// <summary>01b-06 판매수익성분석.</summary>
    public const string SalesProfitability = "/sales/profitability";

    /// <summary>01b-11 세금계산서 통계(화면만 닫기).</summary>
    public const string TaxInvoiceStats = "/tax-invoice-stats";

    /// <summary>01b-27 세무사 자료 보내기.</summary>
    public const string TaxExport = "/accounting/export";

    /// <summary>17·S18 첫 방문 환영(W-S-03 · 로그인 분기는 <see cref="AfterLoginPath"/> 가 이 목록을 본다).</summary>
    public const string Welcome = "/welcome";

    /// <summary>닫힌 주소 전부(순서 = 행 순서).</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        Settings,
        AiAssistant,
        SalesProfitability,
        TaxInvoiceStats,
        TaxExport,
        // 🔴 W-S-03 (2026-11-28 · 설계 §2-2 ①) — 다시 열 때 = 이 한 줄 뗌 + 안내 화면 @page "/welcome" 뗌 + Welcome.razor ⬛ 줄 되살림.
        Welcome,
    };

    /// <summary>
    /// 🔴 W-S-03 (2026-11-28 · 작업지시서 20261128작 §2 · 설계 §2-2 ②) — 로그인 뒤 갈 곳.
    /// 환영 화면이 열려 있고(<see cref="Welcome"/> 가 목록에 없음) 서버가 첫 로그인이라 알릴 때만 <see cref="Welcome"/>,
    /// 아니면 <paramref name="normalPath"/>(첫 화면 또는 [최신버젼업데이트] 뒤 수동 업데이트 화면)를 그대로 돌려준다.
    /// </summary>
    /// <remarks>
    /// 바깥 값(주소창 <c>returnUrl</c> 등)을 받지 않는다 — <paramref name="normalPath"/> 는 호출자가 고정 경로 함수
    /// (<c>LocalSwapUiText.PostLoginPath</c>)로 만든 값이다. 약관 강제(<c>/terms</c>) · 새 기기 알림은 <c>Login.razor</c> 에서 이보다 먼저 간다(#20).
    /// </remarks>
    public static string AfterLoginPath(bool redirectToWelcome, string normalPath)
        => redirectToWelcome && IsOpen(Welcome) ? Welcome : normalPath;

    /// <summary>안내 화면 제목(고객 노출 · 개발 낱말 · 「베타」 0 · PM 결재 P2 초안).</summary>
    public const string NoticeTitle = "이 화면은 정식 판에서 열립니다";

    /// <summary>안내 화면 본문.</summary>
    public const string NoticeBody = "지금 쓰시는 히트판에서는 이 화면을 쉬어 갑니다. 회사 자료는 그대로 있습니다.";

    /// <summary>안내 화면 단추 글.</summary>
    public const string NoticeButton = "처음 화면으로";

    /// <summary>
    /// 참 = 닫힌 주소. 쿼리(<c>?</c>) · 조각(<c>#</c>) · 끝 빗금 · 대소문자는 가리지 않는다(Blazor 라우팅과 같음).
    /// 닫힌 주소 아래의 다른 주소(<c>/settings/devices</c> 등)는 닫힌 것이 아니다 — 정확히 같은 주소만 닫힌다.
    /// </summary>
    public static bool IsClosed(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return false;
        }

        var path = href.Trim();
        var cut = path.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0)
        {
            path = path[..cut];
        }

        if (path.Length > 1)
        {
            path = path.TrimEnd('/');
        }

        foreach (var closed in All)
        {
            if (string.Equals(path, closed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>참 = 열린 주소(메뉴 줄을 감쌀 때 쓴다).</summary>
    public static bool IsOpen(string? href) => !IsClosed(href);
}
