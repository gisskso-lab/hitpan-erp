using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 대리 근태 게이트. 작(2026-08-21) 작10 A.
/// </summary>
/// <remarks>
/// <para>
/// <b>사장님 오더</b>: <i>"사원등록만 되있고 계정이 없는 직원은 인사담당자가 수동으로
/// 근퇴처리 할 수 있는 장치를 만들어야 됨"</i> / <i>"남의 근퇴 넣는건 권한설정에 넣자."</i>
/// </para>
/// <para>
/// 🔴 <b>이 시험들은 [3-V] 병렬이슈5 의 교훈을 반영해 짰다</b> —
/// 글자만 보면 <c>if (false)</c> 로 코드를 죽여도 통과한다.
/// <b>상수 조건과 순서까지 본다.</b>
/// </para>
/// </remarks>
public sealed class AttendanceProxyGuardTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
            dir = Directory.GetParent(dir)?.FullName;
        Assert.True(dir is not null, "레포 루트를 찾아야 한다");
        return dir!;
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {path}");
        return File.ReadAllText(path);
    }

    private static string StripComments(string source)
        => string.Join('\n', source.Split('\n').Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("//", StringComparison.Ordinal)
                && !t.StartsWith("///", StringComparison.Ordinal)
                && !t.StartsWith("*", StringComparison.Ordinal);
        }));

    /// <summary>
    /// 🚨 <b>테넌트 격리</b>(헌법 #2). 대리입력은 <c>employee_id</c> 를 파라미터로 받는 첫 경로다.
    /// </summary>
    /// <remarks>
    /// <c>employee_id</c> 는 GUID 라 추측이 어렵지만 <b>비밀이 아니다</b> — 화면·API 응답에 실려 나간다.
    /// 번호를 알아낸 사람이 남의 회사 근태를 만드는 것을 막는 것은 <b>소속 검증 하나뿐</b>이다.
    /// ⚠️ 이 검사를 지우면 테넌트 격리가 뚫린다.
    /// </remarks>
    [Fact]
    public void 대리근태는_대상직원의_테넌트_소속을_검증한다()
    {
        var code = StripComments(ReadSource("src", "HitPan.Application", "Services", "HrService.cs"));

        Assert.True(Regex.IsMatch(code, @"EnsureSameTenantEmployeeAsync"),
            "소속 검증 헬퍼가 있어야 한다(헌법 #2).");
        // 🔴 [3-V] 적발(2026-08-21): 종전엔 파일 전체에서 이 패턴을 찾아
        //    ★199행의 무관한 SQL★ 이 시험을 통과시키고 있었다.
        //    tenant_id 를 실제로 빼도 초록불이었다 — 초록불이 내 코드가 아니라 남의 줄에서 왔다.
        //    ⇒ 검사 범위를 헬퍼 본문으로 좁힌다.
        var guardStart = code.IndexOf("private async Task EnsureSameTenantEmployeeAsync", StringComparison.Ordinal);
        Assert.True(guardStart >= 0, "소속 검증 헬퍼 본문을 찾아야 한다");
        var guardEnd = code.IndexOf("throw new InvalidOperationException", guardStart, StringComparison.Ordinal);
        Assert.True(guardEnd > guardStart, "헬퍼 본문 끝을 찾아야 한다");
        var guardBody = code[guardStart..guardEnd];

        Assert.True(Regex.IsMatch(guardBody,
            @"FROM\s+employees\s+WHERE\s+tenant_id\s*=\s*@TenantId\s+AND\s+employee_id\s*=\s*@EmpId"),
            "소속 검증 헬퍼가 tenant_id 와 함께 조회해야 한다. " +
            "tenant_id 를 빼면 남의 회사 사원 번호로 근태를 만들 수 있다(헌법 #2).");

        // 🔴 검증이 실제 작업보다 ★먼저★ 와야 한다. 뒤에 있으면 이미 근태가 만들어진 뒤다.
        foreach (var m in new[] { "CheckInProxyAsync", "CheckOutProxyAsync" })
        {
            var start = code.IndexOf(m, StringComparison.Ordinal);
            Assert.True(start >= 0, $"{m} 이 있어야 한다");
            var body = code[start..Math.Min(start + 1200, code.Length)];
            var guard = body.IndexOf("EnsureSameTenantEmployeeAsync", StringComparison.Ordinal);
            var work = body.IndexOf(m.Replace("Proxy", ""), guard < 0 ? 0 : guard, StringComparison.Ordinal);
            Assert.True(guard >= 0 && guard < work,
                $"{m} 에서 소속 검증이 실제 처리보다 앞에 와야 한다.");
        }
    }

    /// <summary>
    /// 🔴 <b>권한은 HR 이 아니라 HR_PROXY 로 가른다.</b>
    /// </summary>
    /// <remarks>
    /// HR 5축(view/create/update/delete/export)은 <b>"내 데이터에 무엇을 하나"</b> 축이고,
    /// 대리입력은 <b>"남의 데이터를 건드리나"</b> 축이다.
    /// <c>HR</c> <c>update</c> 에 얹으면 <b>자기 근태 고치라고 준 권한이 남의 근태까지 연다.</b>
    /// </remarks>
    [Fact]
    public void 대리근태는_별도_권한항목으로_통제된다()
    {
        var ctrl = StripComments(ReadSource("src", "HitPan.API", "Controllers", "HrController.cs"));

        Assert.True(Regex.IsMatch(ctrl, @"RequirePermission\(""HR_PROXY"",\s*""create""\)[\s\S]{0,400}?CheckInProxy"),
            "대리 출근은 HR_PROXY create 로 통제돼야 한다.");
        Assert.True(Regex.IsMatch(ctrl, @"RequirePermission\(""HR_PROXY"",\s*""update""\)[\s\S]{0,400}?CheckOutProxy"),
            "대리 퇴근은 HR_PROXY update 로 통제돼야 한다.");
    }

    /// <summary>
    /// 🔴 <b>안 먹는 체크박스 방지.</b> 1.2.74 P0 재발 차단.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 사장님(2026-08-14): <i>"권한설정으로 모든걸 풀었지만 ... 아무것도 못함."</i>
    /// 실측하니 메뉴 20개 중 서버가 실제로 강제하는 것은 <b>8개뿐</b>이었고,
    /// 나머지는 <b>켜도 꺼도 서버 동작이 같았다</b>.
    /// </para>
    /// <para>
    /// 🔴 <c>HR_PROXY</c> 가 <c>EnforcedMenus</c> 에 없으면 화면에서 감춰진다 —
    /// <b>고객이 켤 방법이 없어 기능이 죽는다.</b> 백엔드 마스터에 없으면 CI 정합검사가 깨진다.
    /// </para>
    /// </remarks>
    [Fact]
    public void HR_PROXY_가_세_곳에_모두_등록돼_있다()
    {
        var back = ReadSource("src", "HitPan.Application", "Services", "PermissionService.cs");
        var front = ReadSource("src", "HitPan.Web", "Pages", "Settings", "PermissionPage.razor.cs");

        Assert.Contains(@"(""HR_PROXY""", back, StringComparison.Ordinal);
        Assert.Contains(@"(""HR_PROXY""", front, StringComparison.Ordinal);

        var enforced = Regex.Match(front, @"EnforcedMenus\s*=\s*new[^;]*;", RegexOptions.Singleline);
        Assert.True(enforced.Success, "EnforcedMenus 집합을 찾아야 한다");
        Assert.Contains("HR_PROXY", enforced.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>자동 근퇴 경로를 건드리지 않았다.</b>
    /// </summary>
    /// <remarks>
    /// 로그인·로그아웃 자동 근퇴는 <b>이미 있었고</b>(<c>AuthController</c>),
    /// 6/22 봉합이 <c>employee_id</c> 키를 통일해 자동/수동 이중행을 막아 뒀다.
    /// ⚠️ 대리입력을 붙이면서 이 경로를 건드리면 그 봉합이 깨진다.
    /// </remarks>
    [Fact]
    public void 자동_근퇴_경로가_그대로_살아있다()
    {
        var auth = StripComments(ReadSource("src", "HitPan.API", "Controllers", "AuthController.cs"));

        Assert.True(Regex.IsMatch(auth, @"CheckInAsync\("),
            "로그인 시 자동 출근이 살아 있어야 한다.");
        Assert.True(Regex.IsMatch(auth, @"CheckOutAsync\("),
            "로그아웃 시 자동 퇴근이 살아 있어야 한다.");
        Assert.True(Regex.IsMatch(auth, @"employee_id"),
            "자동 근퇴는 employee_id 클레임을 써야 한다(6/22 봉합 — 이중행 방지).");
    }

    /// <summary>
    /// 🔴 <b>죽은 코드가 아니다</b> — [3-V] 병렬이슈5 교훈.
    /// </summary>
    /// <remarks>
    /// 글자 검사만 하는 시험은 <c>if (false)</c> 로 감싸면 그대로 통과한다.
    /// 실제 회귀는 블록이 사라지는 게 아니라 <b>조건 한 줄이 죽는</b> 식으로 온다.
    /// </remarks>
    [Fact]
    public void 대리근태_경로에_상수조건이_없다()
    {
        var svc = StripComments(ReadSource("src", "HitPan.Application", "Services", "HrService.cs"));

        var start = svc.IndexOf("CheckInProxyAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = svc.IndexOf("═══ 초과근무", start, StringComparison.Ordinal);
        var block = end > start ? svc[start..end] : svc[start..];

        Assert.False(Regex.IsMatch(block, @"if\s*\(\s*(false|true)\s*\)"),
            "대리 근태 경로에 상수 조건(if(false)/if(true))이 있으면 안 된다. " +
            "if(false) 면 소속 검증이 죽어 테넌트 격리가 뚫린다 — 글자는 남아 다른 시험이 전부 통과한다.");
    }
    /// <summary>
    /// 🔴 <b>화면까지 이어졌나</b> — API 만 만들고 부를 화면이 없으면 "되는 척" 이다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>project_fixed_vs_delivered_gap</c> — <b>"고쳤다" ≠ "갔다"</b>.
    /// 코드는 매번 정상인데 <b>끊긴 것은 전달 경로</b>이고, 끊기는 자리만 옮겨간다.
    /// </para>
    /// <para>
    /// 세 층의 주소가 하나라도 어긋나면 <b>버튼을 눌러도 404</b> 이고,
    /// 빌드도 시험도 통과한다 — 화면·웹서비스·컨트롤러가 서로를 컴파일 타임에 모른다.
    /// </para>
    /// </remarks>
    [Fact]
    public void 대리근태가_화면부터_API까지_이어져_있다()
    {
        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "HrAttendancePage.razor");
        var web = ReadSource("src", "HitPan.Web", "Services", "HrService.cs");
        var ctrl = ReadSource("src", "HitPan.API", "Controllers", "HrController.cs");

        // ① 화면에 버튼이 있고 ② 그 버튼이 웹서비스를 부르고
        Assert.Contains("ProxyCheckInAsync", page, StringComparison.Ordinal);
        Assert.Contains("ProxyCheckOutAsync", page, StringComparison.Ordinal);
        Assert.Contains("CheckInProxyAsync", web, StringComparison.Ordinal);

        // ③ 웹서비스 주소와 컨트롤러 주소가 같아야 한다 — 여기가 실제로 끊기는 자리다
        Assert.Contains("api/hr/attendance/proxy/check-in", web, StringComparison.Ordinal);
        Assert.Contains("api/hr/attendance/proxy/check-out", web, StringComparison.Ordinal);
        Assert.Contains(@"HttpPost(""attendance/proxy/check-in"")", ctrl, StringComparison.Ordinal);
        Assert.Contains(@"HttpPost(""attendance/proxy/check-out"")", ctrl, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>권한 없으면 버튼을 아예 안 그린다.</b>
    /// </summary>
    /// <remarks>
    /// 1.2.74 P0 과 같은 계열 — <b>눌러도 안 되는 버튼을 보여주는 것이 제일 나쁘다.</b>
    /// ⚠️ 화면 판정은 <b>표시용</b>이고 진짜 문지기는 서버 <c>[RequirePermission]</c> 다.
    /// 화면 값을 고쳐도 서버가 403 을 준다 — 이 시험은 보안이 아니라 <b>사용성</b>을 지킨다.
    /// </remarks>
    [Fact]
    public void 대리근태_카드는_권한이_있을_때만_그린다()
    {
        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "HrAttendancePage.razor");

        Assert.True(Regex.IsMatch(page, @"@if\s*\(_canProxy\)"),
            "권한이 없으면 대리입력 카드를 그리지 않아야 한다.");
        Assert.True(Regex.IsMatch(page, @"_canProxy\s*=\s*false", RegexOptions.Singleline)
                 || Regex.IsMatch(page, @"catch[\s\S]{0,200}?_canProxy\s*=\s*false"),
            "권한 판정에 실패하면 열어주지 않아야 한다(못 읽었는데 열면 막은 적이 없는 것과 같다).");
    }


}
