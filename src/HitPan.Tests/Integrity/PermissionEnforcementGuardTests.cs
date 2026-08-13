using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(2026-08-14) 🔴 <b>권한이 실제로 먹는지</b> 보는 게이트.
/// </summary>
/// <remarks>
/// <para>
/// <b>무엇을 겪고서</b> — 사장님(1.2.74 실사용): <i>"부모계정으로 자식계정에게 권한설정으로
/// 모든걸 풀었지만, 클라이언트 pc에서 첫 접속시 권한설정으로 막혀, 아무것도 못함."</i>
/// </para>
/// <para>
/// 실측하니 두 가지였다:
/// ① 권한설정은 <c>user_permissions</c> 에 저장하는데, 차단은 <c>[Authorize(Policy)]</c> 가
///    JWT <c>account_type</c> 만 보고 결정했다 — <b>서로 다른 것을 본다.</b>
///    클래스 레벨 <c>TenantAdminOnly</c> 가 걸린 컨트롤러에서는 권한을 다 켜도 소용이 없었다.
/// ② 권한설정 체크박스 20개 중 <b>서버가 강제하는 것은 8개뿐</b>이고
///    나머지는 <b>켜도 꺼도 동작이 같았다.</b>
/// </para>
/// <para>
/// 🔴 기존 CI(<c>check-permission-menu-sync.sh</c>)는 <b>화면 목록 ↔ 서비스 목록</b>만 비교해
/// 이 구멍을 못 잡았다. 여기서는 <b>"목록이 같은가" 가 아니라 "실제로 먹는가"</b> 를 본다.
/// </para>
/// </remarks>
public class PermissionEnforcementGuardTests
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

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray()));

    private static string PermissionPageSrc() =>
        ReadSource("src", "HitPan.Web", "Pages", "Settings", "PermissionPage.razor.cs");

    /// <summary>
    /// 🔴 화면에 뜨는 체크박스는 <b>전부 서버가 강제하는 것</b>이어야 한다.
    /// </summary>
    /// <remarks>
    /// 안 먹는 체크박스를 보여주면 관리자는 <b>권한을 줬다고 믿는다.</b>
    /// 사장님이 "다 풀었다" 고 하신 그 상태가 정확히 이것이다 — 되는 척이다.
    /// </remarks>
    [Fact]
    public void 화면에_뜨는_권한은_전부_서버가_강제한다()
    {
        var page = PermissionPageSrc();

        // 화면이 EnforcedMenus 로 걸러 보여줘야 한다.
        Assert.Contains("EnforcedMenus", page);
        Assert.Contains("VisibleMenus", page);

        // 화면이 강제한다고 선언한 코드들을 뽑는다.
        var start = page.IndexOf("EnforcedMenus = new(", StringComparison.Ordinal);
        Assert.True(start > 0, "EnforcedMenus 선언이 있어야 한다");
        var end = page.IndexOf("};", start, StringComparison.Ordinal);
        var declared = Regex.Matches(page[start..end], @"""([A-Z_]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(declared);

        // 서버 컨트롤러에서 실제 [RequirePermission("CODE", ...)] 를 전부 긁는다.
        var controllers = Path.Combine(FindRepoRoot(), "src", "HitPan.API", "Controllers");
        var enforced = Directory.EnumerateFiles(controllers, "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"RequirePermission\(""([A-Z_]+)""")
                .Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        var lying = declared.Except(enforced).ToArray();
        Assert.True(lying.Length == 0,
            "화면이 '강제된다' 고 보여주는데 서버에 [RequirePermission] 이 없는 메뉴: "
            + string.Join(", ", lying)
            + "\n안 먹는 체크박스를 보여주면 관리자가 권한을 줬다고 믿는다.");
    }

    /// <summary>
    /// 🔴 감춘 권한이 <b>저장 때 지워지면 안 된다.</b>
    /// </summary>
    /// <remarks>
    /// 화면에서 감춘 뒤 저장이 전체 삭제 후 재삽입이면, <b>안 보이는 권한이 조용히 날아간다.</b>
    /// 나중에 강제를 붙였을 때 되살아나야 하므로 upsert 여야 한다.
    /// </remarks>
    [Fact]
    public void 권한_저장은_지우지_않고_덮어쓴다()
    {
        var svc = ReadSource("src", "HitPan.Application", "Services", "PermissionService.cs");

        Assert.Contains("ON DUPLICATE KEY UPDATE", svc);
        Assert.DoesNotContain("DELETE FROM user_permissions", svc);
    }

    /// <summary>
    /// 🔴 직원이 <b>일하려면 읽어야 하는 것</b>은 관리자 전용이 아니어야 한다.
    /// </summary>
    /// <remarks>
    /// 부서·직급·사원 목록은 <b>메신저 부서방·결재선·조직도</b>의 선행조건이다.
    /// 종전엔 클래스 레벨 <c>TenantAdminOnly</c> 가 조회까지 막아 자식계정이
    /// 화면을 열어도 빈 목록만 봤다(웹이 403 을 빈 배열로 삼켜 "0명" 으로 보였다).
    /// ⚠️ 쓰기는 그대로 관리자 전용이어야 한다 — 여기서 확인하는 것은 <b>조회</b>뿐이다.
    /// </remarks>
    [Theory]
    [InlineData("DepartmentController.cs")]
    [InlineData("PositionController.cs")]
    [InlineData("EmployeeController.cs")]
    public void 직원이_읽어야_하는_목록은_조회가_열려있다(string controller)
    {
        var src = ReadSource("src", "HitPan.API", "Controllers", controller);

        // 클래스 레벨은 관리자 전용을 유지한다(쓰기 보호).
        Assert.Contains("TenantAdminOnly", src);

        // 그런데 GET 하나 이상은 TenantOnly 로 열려 있어야 한다.
        Assert.Contains("[Authorize(Policy = \"TenantOnly\")]", src);

        // 열어 준 자리 바로 뒤가 GET 인지 확인 — 쓰기를 연 것이면 안 된다.
        var opened = Regex.Matches(src,
            @"\[Authorize\(Policy = ""TenantOnly""\)\]\s*\r?\n\s*\[Http(\w+)");
        Assert.True(opened.Count > 0, "TenantOnly 바로 뒤에 HTTP 동사가 있어야 한다");

        foreach (Match m in opened)
        {
            Assert.True(m.Groups[1].Value == "Get",
                $"{controller}: 조회(GET)만 열어야 하는데 {m.Groups[1].Value} 가 열렸다 — 쓰기는 관리자 전용이다");
        }
    }
}
