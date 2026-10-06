using Microsoft.AspNetCore.Mvc.Filters;

namespace HitPan.Backoffice.API.Filters;

/// <summary>
/// 🔴 ② <b>클라이언트가 보낸 범위값을 토큰값으로 덮어쓴다</b>
/// (20261007작10 ①사이클 갈래 ㄱ · 설계 §1-2 ② · §1-3 · PM 결재 P-4).
///
/// <para><c>resellerId</c> / <c>reseller_id</c> 파라미터는 <b>본사에게는 정당한 필터</b>라 제거하지 않는다
/// (설계 §1-3 · PM P-4 — 제거는 본사 기능을 깎는 일이다). 대신 <b>대리점 토큰일 때만</b>
/// 비어 있으면 채우고, 남의 값이면 바꾼다. 클라이언트 입력은 신뢰하지 않는다(헌법 #2).</para>
///
/// <para>🔴 <b>덮어쓴 사실은 반드시 로그에 남긴다</b> — PM 결재 조건(2026-10-07).
/// 조용히 바뀌면 「왜 내 조회 결과가 다르냐」를 나중에 추적할 수 없다.
/// 비어 있던 자리를 채운 것(=보통)과 <b>남의 값을 바꾼 것</b>(=의심 신호)을 로그 등급으로 가른다.</para>
///
/// <para>⚠️ 이 필터는 <b>막지 않는다</b>. 막는 것은 <see cref="BoAccessGuard"/> 한 곳이다
/// (강제 지점을 늘리지 않는 것이 설계값). 대리점 토큰이 본사 라우트에 닿는 일 자체가 이미 403 이므로,
/// 이 필터가 실제로 덮어쓰는 자리는 <c>[ResellerScoped]</c> 라우트뿐이다.</para>
/// </summary>
public sealed class ResellerScopeArgumentFilter : IAsyncActionFilter
{
    // 설계 §1-3 이 지목한 두 이름만 본다(ResellerSerialController:39,46 · ResellerSettlementController:44,52).
    private static readonly string[] ScopeParamNames = { "resellerId", "reseller_id" };

    private readonly ILogger<ResellerScopeArgumentFilter> _logger;

    public ResellerScopeArgumentFilter(ILogger<ResellerScopeArgumentFilter> logger)
    {
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // BoAccessGuard 가 담아 둔 값만 쓴다 — 여기서 토큰을 다시 읽지 않는다(출처 1개).
        var scope = context.HttpContext.Items[BoAccessGuard.ScopeItemKey] as string;

        if (!string.IsNullOrWhiteSpace(scope))
        {
            var path = context.HttpContext.Request.Path.Value ?? "";
            var sub = context.HttpContext.User.FindFirst("sub")?.Value ?? "(없음)";

            foreach (var name in ScopeParamNames)
            {
                if (!context.ActionArguments.ContainsKey(name)) continue;

                var before = context.ActionArguments[name] as string;
                if (string.Equals(before, scope, StringComparison.OrdinalIgnoreCase)) continue;

                context.ActionArguments[name] = scope;

                if (string.IsNullOrWhiteSpace(before))
                {
                    // 보통 경로 — 비워서 보낸 자리를 토큰값으로 채웠다(안 채우면 전건이 된다).
                    _logger.LogInformation(
                        "[ResellerScope] 범위값 주입 param={Param} 값=(비어 있었음)→{Scope} sub={Sub} path={Path} "
                      + "(출처=토큰 · 헌법 #2)", name, scope, sub, path);
                }
                else
                {
                    // 🔴 의심 신호 — 대리점이 남의 reseller_id 를 보냈다. 조회 결과가 요청과 다를 것이므로 반드시 남긴다.
                    _logger.LogWarning(
                        "[ResellerScope] 범위값 덮어씀 param={Param} 보낸값={Sent}→적용값={Scope} sub={Sub} path={Path} "
                      + "(클라이언트 입력 무시 · 헌법 #2 — 조회 결과가 요청과 다른 이유가 이것이다)",
                        name, before, scope, sub, path);
                }
            }
        }

        await next();
    }
}
