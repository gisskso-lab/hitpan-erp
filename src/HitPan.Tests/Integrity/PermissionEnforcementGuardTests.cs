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

        // 20261005작5 §5-2 — 「직원 계정 관리」 3단계는 [RequireUsersLevel(n)] 이 강제한다(n 이상 = 그 단계 코드까지).
        //   1 → USERS · 2 → USERS_ACCOUNT · 3 → USERS_SEAT. 위가 아래를 포함하므로 n 이하 코드가 모두 강제된다.
        var levelCodes = new[] { "USERS", "USERS_ACCOUNT", "USERS_SEAT" };
        foreach (var n in Directory.EnumerateFiles(controllers, "*.cs", SearchOption.AllDirectories)
                     .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"^\s*\[RequireUsersLevel\((\d)\)\]", RegexOptions.Multiline)
                         .Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))))
        {
            enforced.Add(levelCodes[Math.Clamp(n, 1, 3) - 1]);
        }

        var lying = declared.Except(enforced).ToArray();
        Assert.True(lying.Length == 0,
            "화면이 '강제된다' 고 보여주는데 서버에 [RequirePermission] 이 없는 메뉴: "
            + string.Join(", ", lying)
            + "\n안 먹는 체크박스를 보여주면 관리자가 권한을 줬다고 믿는다.");
    }

    // ══ 20261005작5 §8-8 V5-20 — 주소별 단계표 ══
    //   위 게이트는 「코드당 한 곳이라도 있으면」 판정이라, for-employee 의 [RequireUsersLevel(2)] 를 빼도
    //   같은 단계 주소(linkable·suspend·resume)가 가려 초록이었다(작업리뷰서 2차 2-3 실측).
    //   여기서는 주소 하나하나에 「판정 하나 · 그 단계」가 붙어 있는지 본다 — 개발명세서 §3 주소별 단계표가 정본.

    /// <summary>개발명세서 §3 주소별 단계표(UserController · <c>api/users</c>). 키 = "동사 경로꼬리".</summary>
    private static readonly IReadOnlyDictionary<string, int> UsersLevelTable = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["GET "] = 1,
        ["GET {id}"] = 1,
        ["GET seats"] = 1,
        ["POST {id}/suspend"] = 2,
        ["POST {id}/resume"] = 2,
        ["GET linkable-employees"] = 2,
        ["POST for-employee"] = 2,
        ["GET seat-subscription"] = 3,
    };

    /// <summary>
    /// UserController 의 메서드마다 (주소, 붙은 RequireUsersLevel 값들). 주석 줄(⬛ 옛 줄)은 세지 않는다.
    /// 속성 영역 = 메서드 선언 바로 위로 이어진 속성·주석 줄.
    /// </summary>
    private static Dictionary<string, List<int>> UsersControllerLevels()
    {
        var lines = ReadSource("src", "HitPan.API", "Controllers", "UserController.cs").Replace("\r\n", "\n").Split('\n');
        var decl = new Regex(@"^\s*public\s+(?:async\s+)?[\w<>\[\],\s]+?\s+\w+\(", RegexOptions.CultureInvariant);
        var http = new Regex(@"\[Http(Get|Post|Put|Delete|Patch)(?:\(""([^""]*)""\))?\]", RegexOptions.CultureInvariant);
        var lvl = new Regex(@"\[RequireUsersLevel\((\d+)\)\]", RegexOptions.CultureInvariant);
        var result = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Length; i++)
        {
            if (!decl.IsMatch(lines[i]) || lines[i].Contains(" class ", StringComparison.Ordinal)) continue;
            string? address = null;
            var levels = new List<int>();
            for (var j = i - 1; j >= 0; j--)
            {
                var t = lines[j].Trim();
                if (t.StartsWith("//", StringComparison.Ordinal)) continue;     // ⬛ 옛 줄·설명 — 세지 않는다
                if (!t.StartsWith('[')) break;
                var h = http.Match(t);
                if (h.Success) address = h.Groups[1].Value.ToUpperInvariant() + " " + h.Groups[2].Value;
                foreach (Match m in lvl.Matches(t))
                    levels.Add(int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
            if (address is null) continue;
            Assert.False(result.ContainsKey(address), $"UserController 에 같은 주소가 두 번 — {address}");
            result[address] = levels;
        }
        return result;
    }

    /// <summary>
    /// 🔴 V5-20 — 표의 주소마다 <c>[RequireUsersLevel(n)]</c> 이 <b>정확히 하나 · 표의 n</b> 으로 붙어 있다.
    /// 그 주소에서 속성을 빼거나 n 을 바꾸면 그 줄이 FAIL(대조군: for-employee 속성 임시 제거 → FAIL 실측 · 개발명세서_게이트 §8-8).
    /// </summary>
    [Theory]
    [InlineData("GET ")]
    [InlineData("GET {id}")]
    [InlineData("GET seats")]
    [InlineData("POST {id}/suspend")]
    [InlineData("POST {id}/resume")]
    [InlineData("GET linkable-employees")]
    [InlineData("POST for-employee")]
    [InlineData("GET seat-subscription")]
    public void 직원계정관리_주소마다_단계_판정이_붙어있다(string address)
    {
        var expected = UsersLevelTable[address];
        var actual = UsersControllerLevels();
        Assert.True(actual.TryGetValue(address, out var levels),
            $"UserController 에 주소 '{address}' 가 없다 — 표(개발명세서 §3)와 코드가 갈라졌다");
        Assert.True(levels!.Count == 1 && levels[0] == expected,
            $"'{address}' 는 [RequireUsersLevel({expected})] 하나여야 한다 — 실제: "
            + (levels.Count == 0 ? "없음" : string.Join(",", levels.Select(n => $"RequireUsersLevel({n})")))
            + "\n같은 단계의 다른 주소가 있다고 이 주소가 막히는 것이 아니다(V5-20).");
    }

    /// <summary>🔴 V5-20 짝 — 표에 없는 주소에 단계 판정이 붙으면 표가 낡은 것이다(표를 정본으로 유지).</summary>
    [Fact]
    public void 직원계정관리_단계_판정은_표에_있는_주소에만_붙는다()
    {
        var unlisted = UsersControllerLevels()
            .Where(kv => kv.Value.Count > 0 && !UsersLevelTable.ContainsKey(kv.Key))
            .Select(kv => kv.Key)
            .ToArray();
        Assert.True(unlisted.Length == 0,
            "표(개발명세서 §3)에 없는 주소에 [RequireUsersLevel] 이 붙었다: " + string.Join(", ", unlisted));
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
