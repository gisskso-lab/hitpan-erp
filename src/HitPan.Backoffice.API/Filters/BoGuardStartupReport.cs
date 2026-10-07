using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HitPan.Backoffice.API.Filters;

/// <summary>
/// 🔴 <b>기동 시 「가드가 붙어 있나」를 한 줄로 남긴다</b> — 20261007작10 게시 전 조건 <b>P-2</b>
/// (사장님 결재 2026-10-07 · [5] CTO 결재문 §2 P-2 · [4] 리뷰서 §5 ⚠️).
///
/// <para><b>왜 필요한가.</b> <see cref="BoAccessGuard"/> 는 <b>통과할 때 로그를 0줄</b> 남긴다(차단할 때만 찍는다).
/// 그래서 운영에서 「이 필터가 실제로 등록돼 돌고 있나」를 확인할 신호가 **없었다** —
/// [4] 검증에서 <b>가드가 한 인스턴스에서 미실행으로 보인 1회 관측</b>이 있었고 <b>재현에 실패</b>했다.
/// 신호가 없으면 다음에 같은 일이 생겨도 또 못 가린다.</para>
///
/// <para>🔴 <b>주장을 찍지 않는다. 측정을 찍는다.</b> 「등록했다」는 글자를 로그에 박으면
/// 등록 줄을 지운 뒤에도 그 글자는 그대로 나온다(주석은 코드가 아니다 — 작11 P0 가 그 모양이었다).
/// 그래서 이 보고는 <b>실제 <see cref="MvcOptions.Filters"/> 목록을 읽어</b> 만든다.</para>
/// </summary>
public static class BoGuardStartupReport
{
    /// <summary>기동 보고 한 줄의 재료.</summary>
    /// <param name="GuardRegistered">ⓞ 라이브 판독 + ① 거부 기본값 필터가 등록됐나.</param>
    /// <param name="ScopeFilterRegistered">② 범위값 덮어쓰기 필터가 등록됐나.</param>
    /// <param name="TotalGlobalFilters">전역 필터 총 개수(사람이 눈으로 대조할 숫자).</param>
    /// <param name="Message">로그에 실제로 나가는 한 줄.</param>
    public sealed record Report(
        bool GuardRegistered,
        bool ScopeFilterRegistered,
        int TotalGlobalFilters,
        string Message)
    {
        /// <summary>둘 다 붙어 있나. 하나라도 빠지면 36엔드포인트가 다시 열린다.</summary>
        public bool AllRegistered => GuardRegistered && ScopeFilterRegistered;
    }

    /// <summary>전역 필터 목록을 **읽어** 보고를 만든다(순수 함수 — 게이트가 이 함수를 직접 문다).</summary>
    public static Report Inspect(IEnumerable<IFilterMetadata> filters)
    {
        var types = new List<Type>();
        var count = 0;
        foreach (var f in filters)
        {
            count++;
            var t = ResolveType(f);
            if (t is not null) types.Add(t);
        }

        var guard = types.Contains(typeof(BoAccessGuard));
        var scope = types.Contains(typeof(ResellerScopeArgumentFilter));

        var message = guard && scope
            ? $"[BoAccessGuard] 서버 강제 ON — 전역 필터 {count}개에 "
            + $"{nameof(BoAccessGuard)}·{nameof(ResellerScopeArgumentFilter)} 등록 확인(20261007작10 ①사이클). "
            + "🔴 가드는 통과 시 로그가 0줄이다 — 이 한 줄이 「붙어 있다」의 유일한 신호다."
            : $"[BoAccessGuard] 🔴🔴 서버 강제 OFF — 전역 필터 {count}개 중 "
            + $"{nameof(BoAccessGuard)}={(guard ? "등록" : "**없음**")} · "
            + $"{nameof(ResellerScopeArgumentFilter)}={(scope ? "등록" : "**없음**")}. "
            + "대리점 토큰이 본사 전용 라우트에 닿는다(전 고객사·전 대리점·전 정산 노출). "
            + "Program.cs 의 AddControllers 등록 두 줄을 확인하라.";

        return new Report(guard, scope, count, message);
    }

    /// <summary>
    /// 필터 항목에서 **구현 타입**을 꺼낸다.
    /// <para><c>MvcOptions.Filters.Add&lt;T&gt;()</c> 는 <see cref="TypeFilterAttribute"/> 로 들어가므로
    /// 항목의 겉 타입(<c>TypeFilterAttribute</c>)만 보면 영원히 못 찾는다 — 실제로 그렇게 한 번 틀릴 자리다.</para>
    /// </summary>
    private static Type? ResolveType(IFilterMetadata f) => f switch
    {
        TypeFilterAttribute t => t.ImplementationType,
        ServiceFilterAttribute s => s.ServiceType,
        null => null,
        _ => f.GetType(),
    };
}
