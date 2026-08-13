using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(2026-08-13) 부서·직급 자동 생성 게이트 — 사장님 지시:
/// <i>"사원관리에서 직급을 설정하면 자동으로 직급이 생기고, 부서를 설정하면
/// 자동으로 그 부서로 묶으면 되는거니"</i> / <i>"단, 메신저를 위해 데이터 구조는 남겨둬야지"</i>.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>이 시험이 지키는 것은 두 가지다.</b>
/// ① 자동 생성이 <b>실제 저장 경로</b>를 타는가 (화면에만 있고 서버가 안 하면 조용히 안 된다)
/// ② <b>표를 없애지 않았는가</b> — <c>departments</c>·<c>positions</c> 위에 메신저 부서방과
/// 결재선이 선다. 메뉴를 줄이는 작업과 함께 진행돼서, 표까지 걷어내는 사고가 나기 쉬운 자리다.
/// </para>
/// <para>
/// ⚠️ 주석에 든 문구를 코드로 오인하지 않도록 판정 전에 주석 줄을 걸러낸다.
/// </para>
/// </remarks>
public class DeptPositionAutoCreateGuardTests
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

    private static string ReadSource(params string[] relativeParts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(relativeParts).ToArray());
        Assert.True(File.Exists(path), $"{path} 가 있어야 한다");
        return File.ReadAllText(path);
    }

    /// <summary>주석·빈 줄을 걸러낸 실제 코드만 남긴다(거짓 경보 방지).</summary>
    private static string CodeLines(string source) =>
        string.Join('\n', source.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l =>
            {
                var t = l.TrimStart();
                return t.Length > 0
                       && !t.StartsWith("//", StringComparison.Ordinal)
                       && !t.StartsWith("///", StringComparison.Ordinal)
                       && !t.StartsWith("--", StringComparison.Ordinal)
                       && !t.StartsWith("@*", StringComparison.Ordinal)
                       && !t.StartsWith('*');
            }));

    private static string EmployeeServiceCode() =>
        CodeLines(ReadSource("src", "HitPan.Application", "Services", "EmployeeService.cs"));

    // ───────────────────────────────────────────────────────────────
    // ① 자동 생성이 실제 저장 경로를 타는가
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 부서 자동 생성이 <b>서버</b>에 있어야 한다.
    /// </summary>
    /// <remarks>
    /// 🔴 화면에서만 만들면 다른 경로(가져오기·API 직접 호출)로 들어온 사원은 부서가 안 생긴다.
    /// 저장을 책임지는 자리에 둬야 어느 길로 들어와도 같은 결과가 난다.
    /// </remarks>
    [Fact]
    public void 부서_자동생성이_사원_저장_경로에_있다()
    {
        var code = EmployeeServiceCode();

        Assert.Contains("ResolveDeptIdAsync", code);
        Assert.Contains("INSERT INTO departments", code);

        // 등록·수정 두 곳 모두에서 불러야 한다.
        var calls = Regex.Matches(code, @"await ResolveDeptIdAsync\(").Count;
        Assert.True(calls >= 2,
            $"등록·수정 두 곳에서 불러야 한다(실측 {calls}회). 한쪽만 되면 '등록할 땐 됐는데 고칠 땐 안 되네' 가 된다");
    }

    /// <summary>
    /// 직급 자동 생성이 <b>서버</b>에 있어야 한다.
    /// </summary>
    /// <remarks>
    /// <c>employees.position</c> 은 이름 문자열이라 사원 저장만 보면 마스터가 없어도 된다.
    /// 그래도 마스터에 넣는 이유는 <b>결재선이 직급으로 짜이기</b> 때문이다.
    /// </remarks>
    [Fact]
    public void 직급_자동생성이_사원_저장_경로에_있다()
    {
        var code = EmployeeServiceCode();

        Assert.Contains("EnsurePositionExistsAsync", code);
        Assert.Contains("INSERT INTO positions", code);

        var calls = Regex.Matches(code, @"await EnsurePositionExistsAsync\(").Count;
        Assert.True(calls >= 2,
            $"등록·수정 두 곳에서 불러야 한다(실측 {calls}회)");
    }

    /// <summary>
    /// 이름이 같은 부서를 <b>두 번 만들지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 "영업부" 와 "영업부 "(뒤 공백)가 각각 생기면, 사원 화면 부서 목록에
    /// <b>같아 보이는 부서가 둘</b> 뜬다. 고객은 무엇을 고른 건지 알 수 없고
    /// 메신저 부서방도 둘로 갈린다. 찾을 때 대소문자·공백을 무시해야 한다.
    /// </remarks>
    [Fact]
    public void 같은_이름의_부서를_두번_만들지_않는다()
    {
        var code = EmployeeServiceCode();

        var idx = code.IndexOf("ResolveDeptIdAsync", StringComparison.Ordinal);
        Assert.True(idx >= 0, "ResolveDeptIdAsync 가 있어야 한다");

        var insertIdx = code.IndexOf("INSERT INTO departments", idx, StringComparison.Ordinal);
        Assert.True(insertIdx > idx, "부서를 만들기 전에 먼저 찾아봐야 한다");

        // 만들기 전에 SELECT 로 찾아보는 구간이 있어야 한다.
        var lookup = code[idx..insertIdx];
        Assert.Contains("SELECT dept_id", lookup);
        Assert.Contains("FROM departments", lookup);

        // 대소문자·앞뒤 공백을 무시하고 맞춰야 한다.
        Assert.True(lookup.Contains("LOWER(", StringComparison.Ordinal)
                    && lookup.Contains("TRIM(", StringComparison.Ordinal),
            "부서 이름 비교는 대소문자·앞뒤 공백을 무시해야 한다 — 안 그러면 같아 보이는 부서가 둘 생긴다");
    }

    /// <summary>
    /// 이름이 같은 직급을 두 번 만들지 않는다. <c>positions</c> 는
    /// <c>UNIQUE(tenant_id, code)</c> 라 코드가 겹치면 <b>저장이 통째로 터진다.</b>
    /// </summary>
    [Fact]
    public void 같은_이름의_직급을_두번_만들지_않는다()
    {
        var code = EmployeeServiceCode();

        var idx = code.IndexOf("EnsurePositionExistsAsync", StringComparison.Ordinal);
        Assert.True(idx >= 0, "EnsurePositionExistsAsync 가 있어야 한다");

        var tail = code[idx..];
        Assert.Contains("SELECT position_id", tail);

        // INSERT 자체도 WHERE NOT EXISTS 로 한 번 더 막아야 한다(동시 저장 대비).
        var insertIdx = tail.IndexOf("INSERT INTO positions", StringComparison.Ordinal);
        Assert.True(insertIdx >= 0, "직급 INSERT 가 있어야 한다");
        Assert.Contains("WHERE NOT EXISTS", tail[insertIdx..]);
    }

    // ───────────────────────────────────────────────────────────────
    // ② 표를 없애지 않았는가 (사장님 지시 5 — 메신저)
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>departments·positions 표가 살아 있어야 한다.</b>
    /// </summary>
    /// <remarks>
    /// 사장님 못박음: <i>"단, 메신저를 위해 데이터 구조는 남겨둬야지"</i>.
    /// 메뉴를 줄이는 작업과 같은 날 진행돼서, 메뉴에서 안 보인다고 표까지 걷어내는
    /// 사고가 나기 쉽다. 출하 DDL(헌법 #36 단일 진실원)에 두 표가 있는지 본다.
    /// </remarks>
    [Fact]
    public void 부서_직급_표가_출하DDL에_살아있다()
    {
        var ddl = ReadSource("installer", "hitpan_db_clean.sql");

        Assert.Contains("CREATE TABLE `departments`", ddl);
        Assert.Contains("CREATE TABLE `positions`", ddl);

        // 메신저 부서방이 dept_id 로 묶인다 — 그 컬럼이 있어야 한다.
        Assert.Contains("`dept_id`", ddl);
    }

    /// <summary>
    /// 부서 관리·직급 관리 <b>화면이 남아 있어야 한다.</b>
    /// </summary>
    /// <remarks>
    /// 자동 생성은 <b>거드는 것</b>이지 대체가 아니다. 고객이 부서를 고치고·지우고·
    /// 순서를 정하는 자리는 그대로 있어야 한다(헌법 #11 — 어드민이 직접 설정).
    /// 자동 생성은 상위부서·정렬·코드를 <b>추측하지 않으므로</b> 그건 사람이 정한다.
    /// </remarks>
    [Fact]
    public void 부서_직급_관리화면이_그대로_있다()
    {
        var root = FindRepoRoot();

        Assert.True(File.Exists(Path.Combine(root,
            "src", "HitPan.Web", "Pages", "HR", "HrDepartmentsPage.razor")), "부서 관리 화면");
        Assert.True(File.Exists(Path.Combine(root,
            "src", "HitPan.Web", "Pages", "Settings", "PositionsPage.razor")), "직급 관리 화면");

        // 부서 관리는 메신저 부서방의 선행조건이라 메뉴에도 남아 있어야 한다.
        var sidebar = ReadSource("src", "HitPan.Web", "Layout", "Sidebar.razor");
        Assert.Contains("Href=\"/hr/departments\"", sidebar);
    }

    // ───────────────────────────────────────────────────────────────
    // ③ 화면이 사실대로 알리는가
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 함께 만든 부서·직급을 <b>알려야 한다.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 조용히 만들면 부서·직급이 어느새 늘어나 있고 고객은 누가 만들었는지 모른다.
    /// 오타 한 번에 "영어부" 가 생겨도 아무도 모르는 상태가 된다.
    /// 미리(입력 중) 알리고, 만든 뒤에도 알린다.
    /// </remarks>
    [Fact]
    public void 새로_만든_부서와_직급을_사용자에게_알린다()
    {
        var page = ReadSource("src", "HitPan.Web", "Pages", "Settings", "EmployeePage.razor");
        var behind = ReadSource("src", "HitPan.Web", "Pages", "Settings", "EmployeePage.razor.cs");

        // 입력 중 미리 알림
        Assert.Contains("IsNewDeptName", page);
        Assert.Contains("IsNewPositionName", page);

        // 저장 뒤 알림
        var code = CodeLines(behind);
        Assert.Contains("createdDept", code);
        Assert.Contains("createdPosition", code);
    }

    /// <summary>
    /// 부서·직급 칸이 <b>없는 이름도 받아야</b> 한다. 고르기만 되면 자동 생성이 시작될 수 없다.
    /// </summary>
    [Fact]
    public void 부서_직급_칸이_새_이름을_받는다()
    {
        var page = ReadSource("src", "HitPan.Web", "Pages", "Settings", "EmployeePage.razor");

        // MudSelect(고르기 전용) 가 아니라 MudAutocomplete(치기 가능) 여야 한다.
        Assert.Contains("MudAutocomplete", page);
        Assert.Contains("Label=\"부서\"", page);
        Assert.Contains("Label=\"직급\"", page);

        // 목록에 없는 값을 그대로 받아들여야 한다.
        Assert.Contains("CoerceValue=\"true\"", page);
    }

    /// <summary>
    /// 사원을 열었을 때 부서 칸이 <b>채워져야</b> 한다.
    /// </summary>
    /// <remarks>
    /// 🔴 이걸 빠뜨리면 사원을 열었을 때 부서가 빈칸으로 보이고, 다른 항목만 고쳐 저장해도
    /// <b>부서가 조용히 지워진다.</b> 8/13 단계4 에서 WeeklyHours 가 똑같이 당했다 —
    /// 폼에 되돌려 넣지 않아 저장 한 번에 값이 null 로 덮였다. 같은 사고를 반복하지 않는다.
    /// </remarks>
    [Fact]
    public void 사원을_열면_부서칸이_채워진다()
    {
        var code = CodeLines(ReadSource(
            "src", "HitPan.Web", "Pages", "Settings", "EmployeePage.razor.cs"));

        Assert.Contains("_deptText = detail.DeptName", code);
    }
}
