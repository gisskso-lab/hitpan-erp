using HitPan.Backoffice.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using HitPan.Backoffice.API.Security;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HitPan.Backoffice.API.Filters;

/// <summary>
/// 🔴🔴 <b>백오피스 서버 강제 한 곳</b> — ⓞ 라이브 판독 + ① 거부 기본값
/// (20261007작10 ①사이클 갈래 ㄱ · 설계 §1-2·§3-3 · PM 결재 P-1·P-5).
///
/// <para><b>왜 전역 필터 하나인가.</b> 10컨트롤러 36엔드포인트에 손으로 <c>WHERE</c> 를 넣는 방식은
/// 9/22 <c>TenantMiddleware</c> 화이트리스트가 <b>세 번 연속</b> 빠뜨린 모양 그대로다(설계 §1-1).
/// 강제 지점을 <b>늘리지 않고</b> 한 곳에 모은다 — 그래서 기존 컨트롤러는 한 줄도 고치지 않았다(헌법 #1).</para>
///
/// <para><b>순서</b>(이 필터 안에서 차례로):
/// <list type="number">
/// <item><c>[AllowAnonymous]</c> 라우트 → <b>통과</b>(토큰이 실려 와도 · 20261007작10 §2-7 교정②). 이어서 인증 안 된 요청 → <b>통과</b>. 익명 입구는 이 필터 소관이 아니다(설계 §1-2).
///       <c>[Authorize]</c> 는 이 필터보다 앞인 <c>UseAuthorization()</c> 미들웨어가 이미 판정했다.</item>
/// <item>ⓞ <b>라이브 계정 판독</b>(캐시 없음) — 없음·비활성·소속변경 ⇒ <b>즉시 403</b>.
///       토큰 수명(8시간) 안에 권한을 낮추거나 계정을 꺼도 <b>다음 요청부터</b> 끊긴다.</item>
/// <item>① <b>거부 기본값</b> — 대리점 갈래인데 <c>[ResellerScoped]</c> 가 없는 액션이면 403.
///       본사 갈래는 이 단계를 지나간다(전건 조회가 정당하다).</item>
/// </list></para>
///
/// <para>🔴 <b>유효역할 = 좁은 쪽</b>(토큰 역할, DB 역할 중 낮은 등급) — ⚠️ <b>이 값을 실제 권한 판정에 쓰는 갈래는 대리점 사다리뿐이다</b>(아래 본문 주석 · 20261007작10 §2-7 교정①). 올리는 쪽은 토큰이 좁으므로
/// 재로그인까지 안 바뀐다 — 이 <b>비대칭은 의도</b>다(메뉴는 토큰 클레임에서 그려지므로 상향을 즉시 먹이면
/// 「API 는 되는데 메뉴에 없다」가 된다 · 설계 §3-3 표 · 게이트 G-8음 이 이 비대칭을 지킨다).</para>
///
/// <para>🔴 <b><c>account_type</c> 클레임을 읽지 않는다.</b> 그 축은 역호환 파생값이고
/// (<see cref="BoRoles.DeriveAccountType"/> 머리말) 판정은 역할 4값에서만 나온다 — 읽는 곳이 0 인 것이 설계값이다.</para>
/// </summary>
public sealed class BoAccessGuard : IAsyncAuthorizationFilter
{
    /// <summary><c>HttpContext.Items</c> 에 담는 범위값 키(설계 §1-2 ①).</summary>
    public const string ScopeItemKey = "bo.reseller_scope";

    /// <summary>ⓞ 가 접은 유효역할을 담는 키 — 뒤 단계가 토큰 역할 대신 이것을 본다.</summary>
    public const string EffectiveRoleItemKey = "bo.effective_role";

    private readonly IBoLiveAccountReader _reader;
    private readonly ILogger<BoAccessGuard> _logger;

    public BoAccessGuard(IBoLiveAccountReader reader, ILogger<BoAccessGuard> logger)
    {
        _reader = reader;
        _logger = logger;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // ── [AllowAnonymous] 면제 (20261007작10 §2-7 교정② · [4] 리뷰서 §3-②) ────────
        // 🔴 토큰이 실려 오면 인증은 **성공**하므로 아래 「미인증 면제」에 걸리지 않는다.
        //    그래서 익명 라우트가 **대리점 토큰에만** 403 이 되는 비대칭이 있었다
        //    (실측: /api/payments/toss/config — 무토큰 200 · 본사 200 · 대리점 403 · G-9 가 그 FAIL 을 재현한다).
        //    로그아웃 방문자는 되는데 로그인한 대리점은 안 되는 입구는 보호가 아니다.
        // 🔴 「이 라우트가 익명이어도 되는가」의 판정은 이 가드 소관이 아니다 —
        //    작11 게이트(BackofficeAnonymousAdminApiGate)가 그 축을 문다. 토큰 없는 요청은 어차피
        //    여기를 통과하므로, 가드가 그 일을 대신하면 **막는 척**이 된다.
        if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()) return;

        var user = context.HttpContext.User;

        // ── 익명은 이 필터 소관이 아니다(설계 §1-2) ─────────────────────
        if (user?.Identity?.IsAuthenticated != true) return;

        var sub = user.FindFirst("sub")?.Value;
        var tokenRole = BoRoles.Normalize(user.FindFirst("role")?.Value);
        var tokenRid = user.FindFirst("reseller_id")?.Value;
        if (string.IsNullOrWhiteSpace(tokenRid)) tokenRid = null;
        var path = context.HttpContext.Request.Path.Value ?? "";

        if (string.IsNullOrWhiteSpace(sub))
        {
            // 백오피스 토큰은 모두 sub 를 담는다(BackofficeAuthController.cs:56·112).
            // 없으면 우리가 발급한 토큰이 아니다 ⇒ fail-closed.
            _logger.LogWarning("[BoAccessGuard] sub 클레임 없는 토큰 — 차단 path={Path}", path);
            context.Result = Deny("계정을 확인할 수 없습니다. 다시 로그인해 주세요.");
            return;
        }

        // ── ⓞ 라이브 계정 판독 (요청당 1회 · 캐시 없음 · 설계 §3-3) ──────
        BoLiveAccount live;
        try
        {
            live = await _reader.ReadAsync(sub, context.HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            // #15 — 삼키지 않는다. 그리고 판독 실패를 통과로 바꾸지 않는다(fail-closed).
            _logger.LogError(ex, "[BoAccessGuard] 라이브 계정 판독 실패 sub={Sub} path={Path} — fail-closed 403", sub, path);
            context.Result = Deny("권한을 확인하지 못했습니다. 잠시 후 다시 시도해 주세요.");
            return;
        }

        if (!live.Found)
        {
            _logger.LogWarning("[BoAccessGuard] 계정 줄 0건 — 차단 sub={Sub} path={Path}", sub, path);
            context.Result = Deny("계정을 찾을 수 없습니다. 다시 로그인해 주세요.");
            return;
        }

        if (!live.IsActive)
        {
            _logger.LogWarning("[BoAccessGuard] 비활성 계정 — 즉시 차단 sub={Sub} path={Path}", sub, path);
            context.Result = Deny("계정이 비활성화되었습니다.");
            return;
        }

        // 갈래 변경(본사 ↔ 대리점) · 소속 대리점 변경 ⇒ 즉시 403 + 재로그인 안내
        var liveIsReseller = BoRoles.IsReseller(live.EffectiveRole) || live.ResellerId is not null;
        var tokenIsReseller = tokenRid is not null || BoRoles.IsReseller(tokenRole);

        if (liveIsReseller != tokenIsReseller
            || (liveIsReseller && !string.Equals(live.ResellerId, tokenRid, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning(
                "[BoAccessGuard] 소속 변경 감지 — 즉시 차단 sub={Sub} 토큰소속={TokenRid} DB소속={LiveRid} path={Path}",
                sub, tokenRid ?? "(본사)", live.ResellerId ?? "(본사)", path);
            context.Result = Deny("소속이 변경되었습니다. 다시 로그인해 주세요.");
            return;
        }

        // 유효역할 = 좁은 쪽(토큰 역할, DB 역할 중 낮은 등급). 상향은 토큰이 좁아 재로그인까지 안 바뀐다.
        var effective = Narrower(tokenRole, live.EffectiveRole);
        if (effective is null)
        {
            _logger.LogWarning(
                "[BoAccessGuard] 역할을 4값으로 접을 수 없다 — 차단 sub={Sub} 토큰역할={T} DB역할={D} path={Path}",
                sub, user.FindFirst("role")?.Value ?? "(없음)", live.EffectiveRole ?? "(없음)", path);
            context.Result = Deny("역할을 확인할 수 없습니다. 다시 로그인해 주세요.");
            return;
        }
        context.HttpContext.Items[EffectiveRoleItemKey] = effective;

        // ── ① 거부 기본값 ────────────────────────────────────────────
        if (!BoRoles.IsReseller(effective))
        {
            // 본사 갈래 — 이 필터 소관 아님(행 범위 제한이 없는 것이 정당). [BoPermission]·Policy 가 뒤에서 판정한다.
            // 🔴 정정(20261007작10 §2-7 교정① · [4] 리뷰서 §3-①) — **본사 갈래는 역할 하향이 강제되지 않는다.**
            //    위에서 접은 유효역할(EffectiveRoleItemKey)을 **읽는 곳이 레포 전체에 0곳**이고,
            //    실제 판정자 BoPermissionAttribute.cs:37 은 **토큰의 raw role 클레임**을 읽는다.
            //    실측: super_admin → readonly 로 낮춰도 tenants·owner/bo-users·resellers 전부 200
            //         (비활성·소속변경·삭제는 본사에도 즉시 403 — 그쪽은 사실이다).
            //    ⇒ 「하향 즉시」가 실제로 끊는 것은 **대리점 사다리**뿐이다. 본사 갈래 강제는 **2차수 과녁 F-5**
            //      (조건부 교정 ⑦ IsAllowedAsync 의 CSV 어휘 전환과 한 몸 — 한쪽만 고치면 권한 화면이 잠긴다).
            return;
        }

        var scoped = FindResellerScoped(context);
        if (scoped is null)
        {
            // 🔴 36엔드포인트가 여기서 한 번에 닫힌다. 표식을 안 붙인 신설 라우트도 자동으로 닫힌다.
            _logger.LogWarning(
                "[BoAccessGuard] 대리점 토큰이 본사 전용 라우트 접근 — 거부 기본값 403 sub={Sub} reseller={Rid} path={Path}",
                sub, live.ResellerId, path);
            context.Result = Deny("대리점 계정은 이 기능을 사용할 수 없습니다.");
            return;
        }

        if (BoRoles.Rank(effective) < scoped.MinRank)
        {
            _logger.LogWarning(
                "[BoAccessGuard] 대리점 사다리 등급 부족 — 차단 sub={Sub} 유효역할={Eff} 필요={Min} path={Path}",
                sub, effective, scoped.MinRole, path);
            context.Result = Deny("이 기능은 대리점 관리자만 사용할 수 있습니다.");
            return;
        }

        // 통과 — 범위값을 담는다. ② ③ 이 이 값만 쓴다.
        context.HttpContext.Items[ScopeItemKey] = live.ResellerId;
    }

    /// <summary>갈래 안에서 등급이 낮은 쪽. 한쪽이 모르는 값이면 아는 쪽(좁은 쪽 판정은 Rank 가 맡는다).</summary>
    internal static string? Narrower(string? a, string? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return BoRoles.Rank(a) <= BoRoles.Rank(b) ? a : b;
    }

    /// <summary>액션 → 컨트롤러 순으로 <see cref="ResellerScopedAttribute"/> 를 찾는다(액션이 더 구체적).</summary>
    internal static ResellerScopedAttribute? FindResellerScoped(FilterContext context)
    {
        if (context.ActionDescriptor is ControllerActionDescriptor cad)
        {
            return cad.MethodInfo.GetCustomAttributes(typeof(ResellerScopedAttribute), inherit: true)
                       .OfType<ResellerScopedAttribute>().FirstOrDefault()
                ?? cad.ControllerTypeInfo.GetCustomAttributes(typeof(ResellerScopedAttribute), inherit: true)
                       .OfType<ResellerScopedAttribute>().FirstOrDefault();
        }
        return context.ActionDescriptor.EndpointMetadata.OfType<ResellerScopedAttribute>().FirstOrDefault();
    }

    private static ObjectResult Deny(string message) =>
        new(new { success = false, message }) { StatusCode = 403 };
}
