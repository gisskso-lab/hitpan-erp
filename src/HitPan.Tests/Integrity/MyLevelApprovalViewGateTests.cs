using System.Reflection;
using HitPan.API.Authorization;
using HitPan.API.Controllers;
using Xunit;
using FakeProxyOf = HitPan.Tests.Integrity.DataResetParentOnlyGateTests;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>MyLevelApprovalViewGate</b> — 20261128작 로그인경합 갈래 B2 (D16 · 설계 §7-2-2 P1 · §7-2-6 차례 1).
/// </summary>
/// <remarks>
/// <para><c>GET api/users/my-level</c> 의 새 칸 <c>approvalView</c> 가 <b>서버 문이 쓰는 그 판정</b>을 그대로 돌려주는지 동작으로 잰다
/// (사이드바는 이 칸이 <c>false</c> 일 때만 결재 대기 수를 안 부른다 · <c>null</c> = 옛 동작).</para>
/// <para>DB 를 쓰지 않는다 — 가짜 권한 서비스(<see cref="DataResetParentOnlyGateTests.FakeProxy{T}"/>)로 부른 인자 · 돌려준 값을 본다.
/// 권한 판정 함수 · 정책 · 필터는 손 0(이 시험은 읽기만 잰다).</para>
/// </remarks>
public sealed class MyLevelApprovalViewGateTests
{
    internal sealed record Call(string UserId, string TenantId, string Menu, string Action);

    private static UserController NewController(string accountType, string tenantId, string userId)
    {
        var c = new UserController(userService: null!);
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Items["TenantId"] = tenantId;
        http.Items["UserId"] = userId;
        http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim("account_type", accountType) }, "b2"));
        c.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = http };
        return c;
    }

    private static HitPan.Application.Interfaces.IDataResetService GateAllowed() =>
        FakeProxyOf.FakeProxy<HitPan.Application.Interfaces.IDataResetService>.Create(m =>
            m.Name == "GetResetGateAsync"
                ? Task.FromResult(HitPan.Application.DTOs.DataReset.DataResetGate.Allowed)
                : throw new NotSupportedException(m.Name));

    private static object? Prop(Microsoft.AspNetCore.Mvc.IActionResult result, string name)
    {
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        return ok.Value?.GetType().GetProperty(name)?.GetValue(ok.Value);
    }

    // ══ B2-S1 — 부른 인자 = (내 userId, 내 tenantId, "APPROVAL", "view") · 값이 그대로 칸으로 ══

    [Theory(DisplayName = "B2-S1 🔴 my-level approvalView — 결재 보기 판정을 (내 userId · 내 tenantId · APPROVAL · view) 로 1번 불러 그 값을 그대로 돌려줌(거짓 → false · 참 → true)")]
    [InlineData("tenant_user", false)]
    [InlineData("tenant_user", true)]
    [InlineData("tenant_admin", true)]
    public async Task B2_S1_판정_인자와_값(string accountType, bool fakeAllowed)
    {
        var calls = new List<Call>();
        var permission = ArgsProxy.Create(calls, () => Task.FromResult(fakeAllowed));
        var c = NewController(accountType, "b2-tenant", "b2-user");

        var result = await c.GetMyLevel(permission, GateAllowed(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UserController>.Instance, default);

        Assert.True(calls.Count == 1,
            $"🔴 결재 보기 판정을 1번 불러야 하는데 {calls.Count}번 불렀다 — 안 부르면 칸이 서버 판정과 어긋난다.");
        Assert.Equal(new Call("b2-user", "b2-tenant", "APPROVAL", "view"), calls[0]);
        Assert.Equal(fakeAllowed, Prop(result, "approvalView"));
        Assert.Equal(1, Prop(result, "level"));
        Assert.Equal(accountType == "tenant_admin", Prop(result, "isAdmin"));
    }

    // ══ B2-S2 — 판정이 던져도 my-level 은 200 · 기존 칸 그대로 · approvalView = null(화면 옛 동작) ══

    [Fact(DisplayName = "B2-S2 🔴 my-level — 결재 보기 판정이 예외를 던져도 200 · level · isAdmin · dataResetGate 그대로 · approvalView = null(화면은 옛 동작 = 부르고 서버가 판정)")]
    public async Task B2_S2_판정_실패해도_200_null()
    {
        var calls = new List<Call>();
        var permission = ArgsProxy.Create(calls,
            () => Task.FromException<bool>(new InvalidOperationException("b2 판정 실패")));
        var c = NewController("tenant_user", "b2-tenant", "b2-user");

        var result = await c.GetMyLevel(permission, GateAllowed(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UserController>.Instance, default);

        Assert.Single(calls);
        Assert.Null(Prop(result, "approvalView"));
        Assert.Equal(1, Prop(result, "level"));
        Assert.Equal(false, Prop(result, "isAdmin"));
        Assert.Equal(HitPan.Application.DTOs.DataReset.DataResetGateCodes.NotParent, Prop(result, "dataResetGate"));
    }

    // ══ B2-S3 — 화면 칸이 따르는 판정 = 결재 대기 주소의 서버 문 그대로(메뉴 · 동작 글자 대조) ══

    [Fact(DisplayName = "B2-S3 🔴 결재 대기 주소(ApprovalController.GetPending) 문 = RequirePermission(\"APPROVAL\",\"view\") — my-level approvalView 가 부르는 메뉴 · 동작과 같음(어긋나면 사이드바가 403 을 다시 부르거나 결재권자 배지가 사라짐)")]
    public async Task B2_S3_서버문과_같은_메뉴_동작()
    {
        var m = typeof(ApprovalController).GetMethod(nameof(ApprovalController.GetPending))
            ?? throw new Xunit.Sdk.XunitException("ApprovalController.GetPending 이 없다.");
        var door = m.GetCustomAttributes<RequirePermissionAttribute>(inherit: true).ToList();
        Assert.Single(door);
        var args = door[0].Arguments ?? Array.Empty<object>();
        Assert.Equal(new object[] { "APPROVAL", "view" }, args);

        var calls = new List<Call>();
        var permission = ArgsProxy.Create(calls, () => Task.FromResult(false));
        await NewController("tenant_user", "b2-tenant", "b2-user").GetMyLevel(permission, GateAllowed(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UserController>.Instance, default);
        Assert.Equal((string)args[0], calls.Single().Menu);
        Assert.Equal((string)args[1], calls.Single().Action);
    }

    /// <summary>인자까지 받는 가짜 — <c>HasPermissionAsync</c> 의 인자를 적고 <c>GetUsersLevelAsync</c> 는 1 을 준다.</summary>
    public class ArgsProxy : DispatchProxy
    {
        private List<Call> _calls = new();
        private Func<Task<bool>> _approval = () => Task.FromResult(false);

        internal static HitPan.Application.Interfaces.IPermissionService Create(List<Call> calls, Func<Task<bool>> approval)
        {
            var p = Create<HitPan.Application.Interfaces.IPermissionService, ArgsProxy>();
            // ⬛ [낡은 줄 · B2 고침 1 이전] var me = (ArgsProxy)(object)p;
            // B2 고침 1 ⑧(CodeQL 군더더기 형변환) — 봉인 안 된 클래스라 인터페이스에서 바로 형변환된다(동작 같음)
            var me = (ArgsProxy)p;
            me._calls = calls;
            me._approval = approval;
            return p;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            switch (targetMethod.Name)
            {
                case "GetUsersLevelAsync":
                    return Task.FromResult(1);
                case "HasPermissionAsync":
                    _calls.Add(new Call((string)args![0]!, (string)args[1]!, (string)args[2]!, (string)args[3]!));
                    return _approval();
                default:
                    throw new NotSupportedException(targetMethod.Name);
            }
        }
    }
}
