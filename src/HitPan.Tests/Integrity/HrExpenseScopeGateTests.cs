using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 20260826작3 — 그룹웨어 경비 조회 범위 게이트.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 사장님 결재(2026-08-26): <i>"그룹웨어에서의 경비는 직원들이 경비처리 결재올리는 칸이야."</i> ·
/// <i>"그룹웨어 화면은 경리든 직원이든 본인 것만"</i>.
/// </para>
/// <para>
/// 종전 <c>GetHrExpenses</c> 는 화면이 보내온 <c>employeeId</c> 를 그대로 서비스에 넘겼다.
/// 안 보내면 <c>WHERE</c> 에 조건이 안 붙어 <b>전 직원 경비가 다 나왔다.</b>
/// 실제로 화면(<c>Web/Services/HrService.cs</c>)은 <b>한 번도 보낸 적이 없다</b> —
/// 즉 모든 실사용 호출이 전 직원을 조회하고 있었다.
/// </para>
/// <para>
/// ⚠️ <c>[RequirePermission("HR","view")]</c> 는 이 병을 막지 못한다. 그건 "HR 화면을 볼 수 있나" 이지
/// "누구 것을 볼 수 있나" 가 아니다 — 경비를 올리는 직원은 당연히 그 권한을 갖는다.
/// </para>
/// <para>
/// 🔴 급여(<c>PayrollController.ResolveScopeAsync</c>)는 8/13 에 이미 막고 있었다.
/// <b>같은 회사에서 급여는 막고 경비는 뚫린 비대칭</b>이 이 게이트가 지키는 자리다.
/// </para>
/// </remarks>
public class HrExpenseScopeGateTests
{
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.True(dir is not null && Directory.Exists(Path.Combine(dir, "src")),
            "레포 루트를 찾아야 한다");
        return dir!;
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray()));

    /// <summary>
    /// <c>GetHrExpenses</c> 본문만 잘라낸다. 주석·다른 메서드가 섞이면 판정이 흐려진다.
    /// </summary>
    private static string GetHrExpensesBody()
    {
        var src = Read("src", "HitPan.API", "Controllers", "HrController.cs");

        var sigIdx = src.IndexOf("IActionResult> GetHrExpenses(", StringComparison.Ordinal);
        Assert.True(sigIdx >= 0, "GetHrExpenses 를 찾아야 한다");

        var open = src.IndexOf('{', sigIdx);
        Assert.True(open > 0, "본문 시작 중괄호를 찾아야 한다");

        // 중괄호 깊이로 본문 끝을 찾는다.
        var depth = 0;
        var end = -1;
        for (var i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}')
            {
                depth--;
                if (depth == 0) { end = i; break; }
            }
        }

        Assert.True(end > open, "본문 끝을 찾아야 한다");
        return src[open..(end + 1)];
    }

    /// <summary>
    /// 시그니처가 <c>employeeId</c> 를 <b>받지 않아야</b> 한다.
    /// 받아서 무시하면 다음 사람이 되살린다 — 받을 자리 자체를 없앤다.
    /// </summary>
    [Fact]
    public void 그룹웨어_경비조회는_화면이_보낸_사원id를_받지_않아야_한다()
    {
        var src = Read("src", "HitPan.API", "Controllers", "HrController.cs");

        var sigIdx = src.IndexOf("IActionResult> GetHrExpenses(", StringComparison.Ordinal);
        Assert.True(sigIdx >= 0, "GetHrExpenses 를 찾아야 한다");

        var close = src.IndexOf(')', sigIdx);
        var signature = src[sigIdx..close];

        Assert.False(
            signature.Contains("employeeId", StringComparison.OrdinalIgnoreCase),
            "GetHrExpenses 가 employeeId 를 파라미터로 받고 있다 — 주소창으로 남의 경비를 조회할 수 있다.\n"
            + "시그니처: " + signature);
    }

    /// <summary>
    /// 서비스에 넘기는 사원 id 가 <b>JWT 에서 온 값</b>이어야 한다(<c>Items["EmployeeId"]</c>).
    /// </summary>
    [Fact]
    public void 그룹웨어_경비조회는_본인_사원id로_조회해야_한다()
    {
        var body = GetHrExpensesBody();

        // ① JWT 에서 본인 employee_id 를 꺼내야 한다.
        Assert.Contains("Items[\"EmployeeId\"]", body);

        // ② 그 값이 없으면 통과시키지 않는다(빈 값이면 필터가 안 걸려 전건이 샌다).
        Assert.Matches(new Regex(@"IsNullOrEmpty\(\s*eid\s*\)"), body);

        // ③ 서비스 호출의 두 번째 인자가 그 변수여야 한다.
        var call = Regex.Match(body, @"GetHrExpensesAsync\(\s*([^,]+),\s*([^,]+),");
        Assert.True(call.Success, "GetHrExpensesAsync 호출을 찾아야 한다");

        var secondArg = call.Groups[2].Value.Trim();
        Assert.True(secondArg == "eid",
            $"서비스에 넘기는 사원 id 가 본인 값이 아니다 — 넘기는 값: '{secondArg}'");
    }

    /// <summary>
    /// 🔴 급여 쪽 봉합이 살아 있는지도 함께 본다 — 한쪽만 지키면 다시 비대칭이 된다.
    /// </summary>
    [Fact]
    public void 급여조회도_본인범위_판정을_유지해야_한다()
    {
        var src = Read("src", "HitPan.API", "Controllers", "PayrollController.cs");

        Assert.Contains("ResolveScopeAsync", src);

        // 범위 판정이 요청값을 본인으로 덮는 경로가 남아 있어야 한다.
        var resolve = Regex.Match(src,
            @"ResolveScopeAsync\(string\? requested\)(.*?)\n    \}",
            RegexOptions.Singleline);
        Assert.True(resolve.Success, "ResolveScopeAsync 본문을 찾아야 한다");

        Assert.Contains("CurrentEmployeeId()", resolve.Groups[1].Value);
    }
}
