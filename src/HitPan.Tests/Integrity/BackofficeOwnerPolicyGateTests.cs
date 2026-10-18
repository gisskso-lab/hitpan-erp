using System.Security.Claims;
using HitPan.Backoffice.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HitPan.Tests.Integrity;

// 🔴 작B2-A 게이트 (G-B2A-1~6) — `/owner/…` 화면 5장이 **누구도 못 들어가는** 상태였다.
//
//   2026-10-18 실측(P0):
//     - 화면 정책은 role=owner **하나만** 요구했다
//     - 그런데 platform_admins.role enum = super_admin|billing_admin|cs_admin|readonly  ⇒ owner 없음
//     - 로그인 토큰의 role claim 은 DB role 칸 그대로(BackofficeAuthController.cs:60)
//     ⇒ role=owner claim 은 발급 불가 ⇒ 자격증명·BO계정·권한·시스템설정·흐름시험 5장 영구 차단
//     ⇒ 사장님이 테스트 메일 1통을 누를 자리조차 없었다(B1①2단 → O트랙 정지)
//
// 🔴 이 게이트는 정책을 **베끼지 않는다.** 제품(Program.cs)이 쓰는 그 메서드
//    `BackofficePolicies.AddBackofficePolicies` 를 불러서 **실제 인가 평가**를 돌린다.
//    베낀 모양으로 재면 제품이 바뀌어도 초록이다(2026-10-12·10-15 사고).
// 🔴 음성 대조군 필수 — 통과해야 하는 축만 재면 「전부 통과」로 열어 놓은 것과 구분되지 않는다.
public class BackofficeOwnerPolicyGateTests
{
    private static IAuthorizationService BuildAuthz()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // 제품과 같은 등록 경로
        services.AddAuthorization(BackofficePolicies.AddBackofficePolicies);
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal User(params (string Type, string Value)[] claims)
    {
        var id = new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            authenticationType: "TestCookie");
        return new ClaimsPrincipal(id);
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    // ── 통과해야 하는 축 3개 (권한표 정본: owner,platform_owner,super_admin) ──

    [Fact] // G-B2A-1
    public async Task G_B2A_1_role_super_admin_은_OwnerOnly_를_통과한다()
    {
        var authz = BuildAuthz();
        var r = await authz.AuthorizeAsync(
            User(("role", "super_admin"), ("account_type", "platform_admin")),
            null, BackofficePolicies.OwnerOnly);

        Assert.True(r.Succeeded,
            "🔴 시드 계정(master)의 role 은 super_admin 이다(91_seed_admin.sql:29). " +
            "이것이 막히면 `/owner/…` 5장은 누구도 못 들어간다.");
    }

    [Fact] // G-B2A-2
    public async Task G_B2A_2_role_owner_도_그대로_통과한다_종전의도_보존()
    {
        var authz = BuildAuthz();
        var r = await authz.AuthorizeAsync(
            User(("role", "owner")), null, BackofficePolicies.OwnerOnly);

        Assert.True(r.Succeeded, "🔴 #1 — 종전 의도(role=owner)를 지우지 않았음을 동작으로 확인한다.");
    }

    [Fact] // G-B2A-3
    public async Task G_B2A_3_account_type_platform_owner_도_통과한다()
    {
        var authz = BuildAuthz();
        var r = await authz.AuthorizeAsync(
            User(("account_type", "platform_owner")), null, BackofficePolicies.OwnerOnly);

        Assert.True(r.Succeeded, "권한표 allowed_roles 의 platform_owner 축.");
    }

    // ── 🔴 음성 대조군 3개 — 이게 없으면 「전부 열어 둔 것」과 구분이 안 된다 ──

    [Fact] // G-B2A-4
    public async Task G_B2A_4_cs_admin_은_막힌다()
    {
        var authz = BuildAuthz();
        var r = await authz.AuthorizeAsync(
            User(("role", "cs_admin"), ("account_type", "platform_admin")),
            null, BackofficePolicies.OwnerOnly);

        Assert.False(r.Succeeded,
            "🔴 cs_admin·billing_admin·readonly 는 자격증명 화면을 못 본다. " +
            "여기가 통과하면 봉합이 문을 통째로 연 것이다.");
    }

    [Fact] // G-B2A-5
    public async Task G_B2A_5_미인증은_막힌다()
    {
        var authz = BuildAuthz();
        var r = await authz.AuthorizeAsync(Anonymous(), null, BackofficePolicies.OwnerOnly);

        Assert.False(r.Succeeded, "RequireAuthenticatedUser 가 살아 있어야 한다.");
    }

    [Fact] // G-B2A-6
    public async Task G_B2A_6_일반화면_정책은_종전대로다()
    {
        var authz = BuildAuthz();

        var platformAdmin = await authz.AuthorizeAsync(
            User(("account_type", "platform_admin")), null, BackofficePolicies.PlatformOnlyV2);
        var resellerAdmin = await authz.AuthorizeAsync(
            User(("account_type", "reseller_admin")), null, BackofficePolicies.PlatformOnlyV2);

        Assert.True(platformAdmin.Succeeded, "본사 운영 계정은 종전대로 통과한다.");
        Assert.False(resellerAdmin.Succeeded,
            "🔴 대리점 계정이 본사 화면에 들어가면 안 된다 — 이 봉합이 다른 정책을 건드리지 않았음을 확인한다.");
    }
}
