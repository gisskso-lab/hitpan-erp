using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(2026-08-21) 퇴사자 로그인 차단 게이트 — 사장님 지시 ①.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 사장님: <i>"퇴사자는 <b>상태변경으로 처리</b>. 이유: 퇴사자가 <b>근로확인서</b> 같은거 떼러올수도 있으니"</i> /
/// <i>"퇴사자 상태변경시 <b>모든 히트판 기능 비활성처리</b>"</i> /
/// <i>"퇴사자 <b>재입사도 고려</b>해서 상태변경도 가능하도록 <b>(부모계정 관리자만)</b>"</i>
/// </para>
/// <para>
/// 🔴 <b>봉합 전 실측</b>: 퇴사자 계정이 살아 있으면 로그인이 되고,
/// <c>FindActiveEmployeeByUserAsync</c> 가 <c>IsActive</c> 로 걸러 <c>null</c> 을 주는 바람에
/// <b>고아 계정 자가치유 백필</b>로 들어가 <b>새 employees 행이 만들어졌다</b>(새 사번 채번).
/// 퇴사 기록은 남은 채 <b>같은 사람의 재직 행이 하나 더</b> 생긴다 —
/// 사장님 지시 ③(재입사는 부모계정 관리자만)의 <b>권한 우회</b>이기도 하다.
/// </para>
/// <para>
/// ⚠️ <b>백필 자체를 없애면 안 된다.</b> 고아 계정 자가치유는 2026-08-14 사장님 P0 봉합이다
/// (<i>"자식계정은 생성되었으나 다른 그 어떤메뉴에도 그 계정직원은 안나옴"</i>).
/// <b>퇴사자만 갈라내고 고아 계정 구제는 살려둔다.</b>
/// </para>
/// <para>⚠️ 주석 문구를 코드로 오인하지 않도록 판정 전에 주석 줄을 걸러낸다.</para>
/// </remarks>
public sealed class ResignedLoginGateGuardTests
{
    private static string RepoRoot()
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

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>주석·빈 줄을 걸러낸 실제 코드만 남긴다(거짓 경보 방지).</summary>
    private static string CodeLines(string source)
    {
        var noBlock = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return string.Join("\n", noBlock
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l =>
            {
                var t = l.TrimStart();
                return !t.StartsWith("//") && !t.StartsWith("///") && t.Length > 0;
            }));
    }

    private static string AuthServiceCode() =>
        CodeLines(ReadSource("src", "HitPan.Application", "Services", "AuthService.cs"));

    /// <summary>ProcessAsync 처럼 특정 메서드 본문만 잘라낸다.</summary>
    private static string MethodBody(string code, string signature)
    {
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} 를 찾아야 한다");
        return code[start..];
    }

    /// <summary>
    /// 🔴 로그인이 <b>퇴사 여부를 본다</b>.
    /// </summary>
    /// <remarks>
    /// 안 보면 퇴사자가 로그인하고, 그 순간 백필이 <b>새 사번을 발급</b>한다.
    /// </remarks>
    [Fact]
    public void 로그인이_퇴사자를_차단한다()
    {
        var body = MethodBody(AuthServiceCode(), "LoginAsync");

        Assert.True(
            Regex.IsMatch(body, @"Resign|IsActive|is_active", RegexOptions.IgnoreCase),
            """
            LoginAsync 가 퇴사·비활성 여부를 전혀 보지 않는다.
            → 퇴사자 계정이 살아 있으면 그대로 로그인된다(사장님 지시 ② 위반).
              게다가 employees 조회가 null 이 되어 백필이 새 사번을 발급한다.
            """);
    }

    /// <summary>
    /// 🔴 백필이 <b>퇴사자에게 새 사원 행을 만들지 않는다</b>.
    /// </summary>
    /// <remarks>
    /// 로그인 차단을 뚫려도 여기서 한 번 더 막는다(2중 방어).
    /// 문이 둘이다 — <c>LoginAsync</c> 와 <c>RefreshAsync</c> 양쪽이 이 백필을 부른다.
    /// </remarks>
    [Fact]
    public void 백필이_퇴사자에게_새사번을_주지_않는다()
    {
        var body = MethodBody(AuthServiceCode(), "BackfillParentEmployeeAsync");

        // 🔴 "꺼진 행을 찾는" 조회가 실제로 있어야 한다.
        // ⚠️ 처음에는 Resign|IsActive 를 느슨하게 봤는데, 대조실험에서 봉합을 빼도 ★통과했다★ —
        //    바로 위 줄의 기존 코드 `e.UserId == user.Id && e.IsActive` 가 조건을 대신 만족시켰다.
        //    봉합 전부터 있던 문자열로 초록불이 뜨는 ★가짜 게이트★ 였다.
        //    그래서 '꺼진 행(!IsActive)' 을 찾는 조회를 콕 집어 본다.
        Assert.True(
            Regex.IsMatch(body, @"FirstOrDefault\s*\([^)]*!\s*\w+\.IsActive")
                || Regex.IsMatch(body, @"Any\s*\([^)]*!\s*\w+\.IsActive")
                || Regex.IsMatch(body, @"Where\s*\([^)]*!\s*\w+\.IsActive"),
            """
            백필이 '이미 있는데 꺼진 행(=퇴사자)' 을 찾아보지 않는다.
            → 퇴사자로 로그인하면 employees 에 새 행이 INSERT 되고 새 사번이 채번된다.
              퇴사 기록은 남은 채 같은 사람의 재직 행이 하나 더 생긴다.
              (사장님 지시 ③ "재입사는 부모계정 관리자만" 의 권한 우회이기도 하다)
            """);

        // 찾기만 하고 그냥 지나가면 막은 적이 없는 것과 같다 — 새 행 만들기 전에 빠져나가야 한다.
        Assert.True(
            Regex.IsMatch(body, @"!\s*\w+\.IsActive[\s\S]{0,900}?return\s+null"),
            """
            퇴사자 행을 찾아 놓고 return 하지 않는다.
            → 조회만 하고 그대로 INSERT 로 흘러가면 막은 적이 없는 것과 같다.
            """);
    }

    /// <summary>
    /// 🔴 <b>고아 계정 자가치유는 살아 있어야 한다</b> — 퇴사자를 막으려다 이걸 죽이면 안 된다.
    /// </summary>
    /// <remarks>
    /// 2026-08-14 사장님 P0: <i>"자식계정은 생성되었으나 … 다른 그 어떤메뉴에도 그 계정직원은 안나옴."</i>
    /// 백필을 통째로 없애면 그 사고가 재발한다. <b>퇴사자만 갈라낸다.</b>
    /// </remarks>
    [Fact]
    public void 고아계정_자가치유는_유지된다()
    {
        var code = AuthServiceCode();

        Assert.Contains("BackfillParentEmployeeAsync", code);

        var body = MethodBody(code, "BackfillParentEmployeeAsync");

        Assert.True(
            Regex.IsMatch(body, @"(INSERT|Add|AddAsync)", RegexOptions.IgnoreCase),
            """
            백필에서 사원 행을 만드는 코드가 사라졌다.
            → 고아 계정(employees 행이 없는 정상 사용자)이 영영 복구되지 않는다.
              2026-08-14 사장님 P0 가 재발한다. 퇴사자만 갈라내야지 백필을 없애면 안 된다.
            """);
    }
}
