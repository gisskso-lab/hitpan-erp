using Microsoft.AspNetCore.Authorization;

namespace HitPan.Backoffice.Auth;

// 백오피스 화면 인가 정책 — 한 자리에 모아 둔다 (2026-10-18 작B2-A)
//
// 🔴 왜 Program.cs 에서 꺼내 왔나
//   게이트가 「정책이 실제로 누구를 통과시키나」를 **불러서** 재려면 정책 구성이 호출 가능해야 한다.
//   Program.cs 안의 람다는 시험에서 못 부른다 ⇒ 베낀 모양으로 재게 되고, 그 초록은 화면이 바뀌어도
//   초록이다(2026-10-12·10-15 사고). 그래서 순수 `Microsoft.AspNetCore.Authorization` 의존
//   파일 1장으로 분리했고, 시험은 이 파일을 **소스 링크**해서 같은 코드를 돌린다(ApiErrorText 선례).
//
// 🔴 이 파일은 Blazor·MudBlazor·HttpContext 에 의존하지 않는다. 늘리지 마라.
public static class BackofficePolicies
{
    /// <summary>본사 운영(플랫폼) 계정 — 고객사·대리점·CS 등 일반 화면.</summary>
    public const string PlatformOnlyV2 = "PlatformOnlyV2";

    /// <summary>본사 운영 관리자 이상.</summary>
    public const string PlatformManagerOrAbove = "PlatformManagerOrAbove";

    /// <summary>사장님 영역 — 자격증명·BO계정·권한·시스템설정·흐름시험(`/owner/…` 5장).</summary>
    public const string OwnerOnly = "OwnerOnly";

    // 🔴 OwnerOnly 가 통과시켜야 하는 것 — 정본은 레포가 이미 적어 뒀다.
    //   installer/backoffice/90_seed_permissions.sql:78-85 의 owner.* 권한
    //     allowed_roles = 'owner,platform_owner,super_admin'
    //   그런데 종전 화면 정책은 role=owner **하나만** 요구했고,
    //   installer/backoffice/00_backoffice_core.sql:55 의
    //     platform_admins.role enum = super_admin|billing_admin|cs_admin|readonly
    //   에는 **owner 가 없다** ⇒ role=owner claim 은 발급될 수 없다
    //     (BackofficeAuthController.cs:60 이 DB role 칸을 그대로 claim 으로 싣는다)
    //   ⇒ `/owner/…` 5장이 **누구도 못 들어가는 상태**로 게시돼 있었다(2026-10-18 실측 P0).
    //   그래서 권한표와 같은 세 축을 허용한다. enum 을 늘리지 않는다(스키마 무접촉).
    private static readonly string[] OwnerRoles = { "owner", "super_admin" };
    private static readonly string[] OwnerAccountTypes = { "platform_owner" };

    /// <summary>
    /// 백오피스 정책 3종을 등록한다. Program.cs 와 게이트가 **같은** 이 메서드를 쓴다.
    /// </summary>
    public static void AddBackofficePolicies(AuthorizationOptions o)
    {
        o.AddPolicy(PlatformOnlyV2, p => p.RequireAuthenticatedUser()
            .RequireClaim("account_type", "platform_admin", "platform_owner"));

        o.AddPolicy(PlatformManagerOrAbove, p => p.RequireAuthenticatedUser()
            .RequireClaim("account_type", "platform_admin", "platform_owner"));

        // 종전(2026-06-04~2026-10-18): .RequireClaim("role", "owner") — 위 주석의 이유로 통과 불가였다.
        //   #1 에 따라 종전 의도(role=owner)를 지우지 않고 **그대로 포함**한 채 두 축을 더한다.
        o.AddPolicy(OwnerOnly, p => p.RequireAuthenticatedUser()
            .RequireAssertion(ctx =>
                ctx.User.Claims.Any(c => c.Type == "role" && OwnerRoles.Contains(c.Value))
                || ctx.User.Claims.Any(c => c.Type == "account_type" && OwnerAccountTypes.Contains(c.Value))));
    }
}
