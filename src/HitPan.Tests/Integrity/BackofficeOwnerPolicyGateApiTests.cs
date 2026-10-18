using System.Security.Claims;
using HitPan.Backoffice.API.Auth;
using Xunit;

namespace HitPan.Tests.Integrity;

// 🔴 작B2-A 게이트 2부 (G-B2A-7~12) — **API 쪽 Owner 자물쇠**.
//
//   화면(Blazor 정책)만 고쳐서는 버튼이 안 먹는다. 컨트롤러가 액션 본문에서 한 번 더 막는다:
//     CredentialsStatusController.cs:39·104·231  → IsOwner() 불통과 시 403
//     BoPermissionsController.cs                 → 같은 모양
//   종전 판정은 role=="owner" 하나였고, 그 값은 platform_admins.role enum 에 없다 ⇒ 발급 불가.
//   🟢 2026-10-18 운영 실측: master@hitpan.kr(role=super_admin)로 토큰을 받아
//      POST /api/backoffice/credentials/test-mail 을 호출하니
//      {"success":false,"message":"사장님(Owner)만 접근할 수 있습니다."} 가 돌아왔다.
//      ⇒ 이 게이트가 빨강이면 그 403 이 되살아난 것이다.
public class BackofficeOwnerPolicyGateApiTests
{
    private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            authenticationType: "TestCookie"));

    [Fact] // G-B2A-7
    public void G_B2A_7_super_admin_은_API_Owner_자물쇠를_통과한다()
        => Assert.True(
            OwnerClaims.IsOwner(User(("role", "super_admin"), ("account_type", "platform_admin"))),
            "🔴 운영 계정(master)의 role 이 super_admin 이다. 막히면 자격증명·테스트메일 API 가 403 이다.");

    [Fact] // G-B2A-8
    public void G_B2A_8_role_owner_도_그대로_통과한다()
        => Assert.True(OwnerClaims.IsOwner(User(("role", "owner"))),
            "#1 — 종전 의도(role=owner)를 지우지 않았음을 동작으로 확인한다.");

    [Fact] // G-B2A-9
    public void G_B2A_9_account_type_platform_owner_도_통과한다()
        => Assert.True(OwnerClaims.IsOwner(User(("account_type", "platform_owner"))));

    // ── 🔴 음성 대조군 3 ──

    [Fact] // G-B2A-10
    public void G_B2A_10_cs_admin_은_막힌다()
        => Assert.False(
            OwnerClaims.IsOwner(User(("role", "cs_admin"), ("account_type", "platform_admin"))),
            "🔴 여기가 통과하면 자격증명 API 를 본사 직원 전원에게 연 것이다.");

    [Fact] // G-B2A-11
    public void G_B2A_11_reseller_admin_은_막힌다()
        => Assert.False(
            OwnerClaims.IsOwner(User(("role", "reseller_user"), ("account_type", "reseller_admin"))),
            "🔴 대리점은 본사 자격증명 근처에 못 온다.");

    [Fact] // G-B2A-12
    public void G_B2A_12_미인증과_null_은_막힌다()
    {
        Assert.False(OwnerClaims.IsOwner(new ClaimsPrincipal(new ClaimsIdentity())), "미인증");
        Assert.False(OwnerClaims.IsOwner(null), "null");
    }
}
