using System.Reflection;
using HitPan.API.Controllers;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>DataResetParentOnlyGate — 비DB 쪽</b>(20261125작 · 「모든데이터 초기화」 진입 = 대표 계정 전용 · 지시서 §5 N1 ~ N4).
/// </summary>
/// <remarks>
/// <para>DB 를 쓰지 않는다 — 이 PC 로컬에서도 돈다. DB 쪽(P1 ~ P8)은 <c>DataResetParentOnlyGateDbTests</c>(CI db-gate).</para>
/// <para>
/// ⚠️ 메뉴 규칙(N1 · N2)은 <b>차단이 아니다.</b> 막는 것은 서버 대표 판정(P2 · P7)이다. N3 은 문(속성)이 그대로인지 — 일반 직원 403 대조군.
/// </para>
/// </remarks>
public sealed class DataResetParentOnlyGateTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            // ⬛ 고침 1 이전(CodeQL 「Path.Combine 앞 인자가 조용히 버려질 수 있음」):
            //   if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            // Path.Join 은 뒤 조각이 절대 경로여도 앞을 버리지 않는다 — 상대 조각 하나라 결과는 같다.
            if (File.Exists(Path.Join(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 시험이 Sidebar.razor 를 읽을 수 없다.");
    }

    // ══ N1 — 메뉴 규칙을 실제로 불러 본다 ══

    [Theory(DisplayName = "N1 🔴 ShowResetLink — allowed · no_parent · multiple_parents 만 보임 · not_parent · null · 빈칸 · 모르는 값은 안 보임")]
    [InlineData("allowed", true)]
    [InlineData("no_parent", true)]
    [InlineData("multiple_parents", true)]
    [InlineData("not_parent", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("ALLOWED", false)]
    [InlineData("unknown", false)]
    public void N1_메뉴_규칙(string? gate, bool expected)
    {
        Assert.True(DataResetMenuRule.ShowResetLink(gate) == expected,
            $"🔴 dataResetGate={(gate is null ? "null" : $"\"{gate}\"")} → 메뉴 {(expected ? "보여야" : "안 보여야")} 한다. "
          + "못 읽었는데 열어 주면 막은 적이 없는 것과 같다.");
    }

    [Fact(DisplayName = "N1b 🔴 화면 상수 4 = 서버 DataResetGateCodes 4 (화면 ↔ 서버 계약 · 글자 하나만 바뀌어도 메뉴가 사라지거나 열린다)")]
    public void N1b_화면_서버_글자_같다()
    {
        Assert.Equal(HitPan.Application.DTOs.DataReset.DataResetGateCodes.Allowed, DataResetMenuRule.Allowed);
        Assert.Equal(HitPan.Application.DTOs.DataReset.DataResetGateCodes.NotParent, DataResetMenuRule.NotParent);
        Assert.Equal(HitPan.Application.DTOs.DataReset.DataResetGateCodes.NoParent, DataResetMenuRule.NoParent);
        Assert.Equal(HitPan.Application.DTOs.DataReset.DataResetGateCodes.MultipleParents, DataResetMenuRule.MultipleParents);
        foreach (HitPan.Application.DTOs.DataReset.DataResetGate g in Enum.GetValues(typeof(HitPan.Application.DTOs.DataReset.DataResetGate)))
        {
            var code = HitPan.Application.DTOs.DataReset.DataResetGateCodes.ToCode(g);
            Assert.Equal(g != HitPan.Application.DTOs.DataReset.DataResetGate.NotParent, DataResetMenuRule.ShowResetLink(code));
        }
    }

    // ══ N2 — 사이드바 원문 ══

    [Fact(DisplayName = "N2 🔴 Sidebar — /data/reset 줄만 ShowResetLink 블록 안 · 백업 · 자료이관 · 로그 3줄은 밖(범위 「초기화만」) · 관리자 블록에서 my-level 부름")]
    public void N2_사이드바_감쌈()
    {
        // ⬛ 고침 1 이전(CodeQL): var path = Path.Combine(RepoRoot(), "src", "HitPan.Web", "Layout", "Sidebar.razor");
        var path = Path.Join(RepoRoot(), "src", "HitPan.Web", "Layout", "Sidebar.razor");
        Assert.True(File.Exists(path), $"Sidebar.razor 가 없다: {path}");
        var lines = File.ReadAllLines(path);

        int Find(string needle)
        {
            var hits = Enumerable.Range(0, lines.Length).Where(i => lines[i].Contains(needle, StringComparison.Ordinal)).ToList();
            Assert.True(hits.Count == 1, $"🔴 Sidebar.razor 에 「{needle}」 줄이 {hits.Count}개 — 1개여야 한다.");
            return hits[0];
        }

        var reset = Find("Href=\"/data/reset\"");
        var ifLine = Find("DataResetMenuRule.ShowResetLink(_dataResetGate)");
        Assert.True(lines[ifLine].TrimStart().StartsWith("@if", StringComparison.Ordinal),
            $"🔴 ShowResetLink 가 @if 조건이 아니다: {lines[ifLine].Trim()}");
        Assert.True(ifLine < reset, "🔴 /data/reset 줄이 ShowResetLink 블록 앞에 있다 — 감싸지 않았다.");

        // @if 줄부터 중괄호를 세어 블록 끝을 찾는다.
        var depth = 0;
        var opened = false;
        var close = -1;
        for (var i = ifLine; i < lines.Length && close < 0; i++)
        {
            foreach (var ch in lines[i])
            {
                if (ch == '{') { depth++; opened = true; }
                else if (ch == '}') { depth--; if (opened && depth == 0) { close = i; break; } }
            }
        }
        Assert.True(close > 0, "🔴 ShowResetLink 블록의 닫는 중괄호를 못 찾았다.");
        Assert.True(reset > ifLine && reset < close,
            $"🔴 /data/reset 줄({reset + 1})이 ShowResetLink 블록({ifLine + 1}~{close + 1}) 밖이다 — 대표 아닌 관리자에게도 메뉴가 보인다.");

        foreach (var other in new[] { "Href=\"/data/backup\"", "Href=\"/settings/mdb-migration\"", "Href=\"/data/logs\"" })
        {
            var at = Find(other);
            Assert.False(at > ifLine && at < close,
                $"🔴 「{other}」 줄({at + 1})이 초기화 블록 안에 들어갔다 — 대표 범위는 「초기화만」(§146)이다. 나머지는 별건.");
        }

        // 관리자 블록에서 my-level 을 읽어 _dataResetGate 를 채운다(K4 (가)).
        var assign = Find("_dataResetGate = me?.DataResetGate");
        var adminIf = Enumerable.Range(0, assign).Reverse().FirstOrDefault(i => lines[i].Contains("if (", StringComparison.Ordinal), -1);
        Assert.True(adminIf >= 0 && lines[adminIf].Contains("if (_isTenantAdmin)", StringComparison.Ordinal),
            $"🔴 _dataResetGate 를 채우는 자리가 관리자 블록(if (_isTenantAdmin)) 안이 아니다: {(adminIf >= 0 ? lines[adminIf].Trim() : "없음")}");
        Assert.True(Enumerable.Range(adminIf, assign - adminIf + 1).Any(i => lines[i].Contains("GetMyLevelAsync()", StringComparison.Ordinal)),
            "🔴 관리자 블록에서 my-level(GetMyLevelAsync)을 부르지 않는다 — 메뉴 재료가 늘 null(대표에게도 안 보임).");
    }

    // ══ N3 — 문은 그대로 ══

    [Fact(DisplayName = "N3 🔴 DataResetController 문 = TenantAdminOnly + MainPcOnly 그대로(일반 직원 403 대조군 — 대표 판정은 서비스 몫)")]
    public void N3_문_그대로()
    {
        var t = typeof(DataResetController);
        var auth = t.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();
        Assert.Contains(auth, a => a.Policy == "TenantAdminOnly");
        Assert.NotNull(t.GetCustomAttribute<HitPan.API.Security.MainPcOnlyAttribute>(inherit: true));
    }

    // ══ N4 — (K3 안 1) 확인 문구 「초기화」 서버 대조 ══

    [Theory(DisplayName = "N4 🔴 초기화 API — 확인 문구가 「초기화」(앞뒤 빈칸 허용)가 아니면 서비스를 부르지도 않고 400 · Success=false")]
    [InlineData("", false)]
    [InlineData("덮어쓰기", false)]
    [InlineData("초기 화", false)]
    [InlineData("초기화 ", true)]
    [InlineData(" 초기화", true)]
    [InlineData("초기화", true)]
    public async Task N4_확인문구_서버대조(string confirmText, bool serviceCalled)
    {
        var called = false;
        var spy = FakeProxy<HitPan.Application.Interfaces.IDataResetService>.Create(m =>
        {
            if (m.Name != "ResetAllAsync") throw new NotSupportedException(m.Name);
            called = true;
            return Task.FromResult(new HitPan.Application.DTOs.DataReset.DataResetResponse { Success = false, Error = "n4 가짜" });
        });
        var c = new DataResetController(spy, provisioner: null!,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DataResetController>.Instance);
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Items["TenantId"] = "n4-tenant";
        http.Items["UserId"] = "n4-user";
        c.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = http };

        var result = await c.ResetAll(new HitPan.Application.DTOs.DataReset.DataResetRequest
        {
            Password = "n4-pw",
            ConfirmText = confirmText,
        }, default);

        Assert.True(called == serviceCalled,
            $"🔴 확인 문구 「{confirmText}」 → 서비스 {(serviceCalled ? "불려야" : "안 불려야")} 하는데 {(called ? "불렸다" : "안 불렸다")}.");
        var bad = Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(result);
        var body = Assert.IsType<HitPan.Application.DTOs.DataReset.DataResetResponse>(bad.Value);
        Assert.False(body.Success);
        if (!serviceCalled) Assert.Equal("확인을 위해 '초기화' 라고 입력해 주세요.", body.Error);
    }

    // ══ N5 — [3-V] S-1 (PM 결재 §150): 판정이 던져도 my-level 은 200 · 기존 칸 그대로 · 판정 칸 not_parent ══

    [Fact(DisplayName = "N5 🔴 my-level — 대표 판정이 예외를 던져도 200 · level · isAdmin 그대로 · dataResetGate = not_parent(메뉴 감춤)")]
    public async Task N5_판정_실패해도_my_level_살아있음()
    {
        var c = new UserController(userService: null!);
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Items["TenantId"] = "n5-tenant";
        http.Items["UserId"] = "n5-user";
        http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim("account_type", "tenant_admin") }, "n5"));
        c.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = http };

        var permission = FakeProxy<HitPan.Application.Interfaces.IPermissionService>.Create(
            m => m.Name == "GetUsersLevelAsync" ? Task.FromResult(3) : throw new NotSupportedException(m.Name));
        var throwingGate = FakeProxy<HitPan.Application.Interfaces.IDataResetService>.Create(
            m => m.Name == "GetResetGateAsync"
                ? Task.FromException<HitPan.Application.DTOs.DataReset.DataResetGate>(new InvalidOperationException("n5 판정 실패"))
                : throw new NotSupportedException(m.Name));

        var result = await c.GetMyLevel(permission, throwingGate,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UserController>.Instance, default);

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        object? Prop(string name) => ok.Value?.GetType().GetProperty(name)?.GetValue(ok.Value);
        Assert.Equal(3, Prop("level"));
        Assert.Equal(true, Prop("isAdmin"));
        Assert.Equal(DataResetMenuRule.NotParent, Prop("dataResetGate"));
    }

    // ══ N6 — (고침 1 · [4] L6) my-level 은 관리자에게 대표 판정을 실제로 부르고 그 글자를 돌려준다 · 관리자 아니면 안 부른다 ══

    [Theory(DisplayName = "N6 🔴 my-level — 관리자면 대표 판정을 1번 불러 그 글자(allowed 등)를 돌려줌 · 관리자 아니면 판정 0번 · not_parent (판정을 안 부르고 늘 not_parent 면 대표에게 메뉴가 사라진다)")]
    [InlineData("tenant_admin", HitPan.Application.DTOs.DataReset.DataResetGate.Allowed, 1, "allowed")]
    [InlineData("tenant_admin", HitPan.Application.DTOs.DataReset.DataResetGate.MultipleParents, 1, "multiple_parents")]
    [InlineData("tenant_user", HitPan.Application.DTOs.DataReset.DataResetGate.Allowed, 0, "not_parent")]
    public async Task N6_my_level_판정_부름(string accountType, HitPan.Application.DTOs.DataReset.DataResetGate fakeGate,
        int expectedCalls, string expectedCode)
    {
        var c = new UserController(userService: null!);
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Items["TenantId"] = "n6-tenant";
        http.Items["UserId"] = "n6-user";
        http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim("account_type", accountType) }, "n6"));
        c.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = http };

        var permission = FakeProxy<HitPan.Application.Interfaces.IPermissionService>.Create(
            m => m.Name == "GetUsersLevelAsync" ? Task.FromResult(3) : throw new NotSupportedException(m.Name));
        var calls = 0;
        var countingGate = FakeProxy<HitPan.Application.Interfaces.IDataResetService>.Create(m =>
        {
            if (m.Name != "GetResetGateAsync") throw new NotSupportedException(m.Name);
            calls++;
            return Task.FromResult(fakeGate);
        });

        var result = await c.GetMyLevel(permission, countingGate,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UserController>.Instance, default);

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        object? Prop(string name) => ok.Value?.GetType().GetProperty(name)?.GetValue(ok.Value);
        Assert.True(calls == expectedCalls,
            $"🔴 account_type={accountType} → 대표 판정을 {expectedCalls}번 불러야 하는데 {calls}번 불렀다. "
          + "관리자에게 판정을 안 부르면 대표에게도 메뉴가 사라지고, 관리자 아닌데 부르면 쓸데없이 DB 를 읽는다.");
        Assert.Equal(expectedCode, Prop("dataResetGate"));
        Assert.Equal(3, Prop("level"));
    }

    /// <summary>
    /// 인터페이스 가짜 — 부른 메서드를 받아 돌려줄 값을 정한다. <c>: IDataResetService</c> 구현체를 늘리지 않으려고 쓴다
    /// (#12 · [4] R6 「구현체 3곳」 grep 이 그대로 3).
    /// </summary>
    public class FakeProxy<T> : DispatchProxy where T : class
    {
        private Func<MethodInfo, object?> _handler = _ => null;

        public static T Create(Func<MethodInfo, object?> handler)
        {
            var p = Create<T, FakeProxy<T>>();
            ((FakeProxy<T>)(object)p)._handler = handler;
            return p;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            _handler(targetMethod ?? throw new ArgumentNullException(nameof(targetMethod)));
    }
}
