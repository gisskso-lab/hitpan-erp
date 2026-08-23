using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(2026-08-12) 그룹웨어 단계0·1 봉합 게이트.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>이 시험이 존재하는 이유</b> — 이 팀은 <b>"고쳤는데 안 갔다"</b> 를 두 번 겪었다.
/// 8/11 AI 연동은 팩토리를 만들고 <b>실제 대화 경로에 배선하지 않아</b> 시험 24개가 전부 통과하는데도
/// 고객이 챗GPT를 골라도 클로드로 갔다. 8/12 마이그레이션은 SQL 을 엉뚱한 폴더에 둬서
/// 빌드 0/0·시험 286·ddl-smoke 가 전부 통과했는데 고객에게 안 갔다.
/// </para>
/// <para>
/// 그래서 여기서는 <b>"코드가 존재하는가" 가 아니라 "실제로 그 경로를 타는가"</b> 를 본다.
/// 순수 함수 시험만 쌓으면 배선 결함을 못 잡는다는 것이 그때 얻은 교훈이다.
/// </para>
/// <para>
/// ⚠️ 과거 이 계열 시험에서 <b>주석에 든 문구를 코드로 오인</b>해 거짓 경보를 낸 적이 있어,
/// 판정 전에 주석 줄을 걸러낸다(<see cref="CodeLines"/>).
/// </para>
/// </remarks>
public class GroupwareStage0GuardTests
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

    /// <summary>주석 줄을 걸러낸 실제 코드만 남긴다(거짓 경보 방지).</summary>
    private static string CodeLines(string source) =>
        string.Join('\n', source.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l =>
            {
                var t = l.TrimStart();
                return t.Length > 0
                       && !t.StartsWith("//", StringComparison.Ordinal)
                       && !t.StartsWith("*", StringComparison.Ordinal)
                       && !t.StartsWith("/*", StringComparison.Ordinal)
                       && !t.StartsWith("@*", StringComparison.Ordinal);
            }));

    // ───────────────────────────────────────────────────────────────
    // P0-D. 급여가 항상 0원으로 보이던 결함 (JSON 이름 불일치)
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// API 는 <c>salary_amount AS SalaryAmount</c> 로 내보내는데 웹 모델이 <c>Salary</c> 로 받아
    /// <b>항상 0원</b>이었다. 🔴 500 이 안 나서 고객이 "급여 0원" 을 사실로 믿는 종류의 결함이다.
    /// <b>추정이 아니라 실제 직렬화를 돌려</b> 증명한다.
    /// </summary>
    [Fact]
    public void 계약서_급여는_API_JSON_이름으로_역직렬화된다()
    {
        // ASP.NET Core 기본 직렬화(camelCase)를 그대로 흉내낸 API 응답.
        const string apiJson = """
            {"contractId":"C1","employeeName":"홍길동","salaryAmount":3500000,"salaryType":"monthly"}
            """;

        var model = JsonSerializer.Deserialize<SalaryProbe>(apiJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(model);
        Assert.Equal(3_500_000m, model!.Salary);
    }

    /// <summary>
    /// 표기를 지우면 급여가 조용히 0 으로 돌아가므로, 실제 웹 모델에 남아 있는지도 지킨다.
    /// API 별칭과 웹 속성명이 다른 자리 전부가 대상이다.
    /// </summary>
    [Fact]
    public void 웹모델에_JSON_이름_표기가_남아있다()
    {
        var source = ReadSource("src", "HitPan.Web", "Models", "ESignModels.cs");

        string[] required =
        [
            "[JsonPropertyName(\"salaryAmount\")]",
            "[JsonPropertyName(\"workPlace\")]",
            "[JsonPropertyName(\"workingHours\")]",
            "[JsonPropertyName(\"annualLeave\")]",
            "[JsonPropertyName(\"extraTerms\")]"
        ];

        foreach (var attr in required)
        {
            Assert.Contains(attr, source);
        }
    }

    // ───────────────────────────────────────────────────────────────
    // P0-D. 무효 서명이 "유효" 로 보이던 결함
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// API 는 <c>is_void AS IsVoid</c> 만 내려주는데 화면이 존재하지 않는 <c>Status</c> 로 판정해
    /// <b>무효화된 서명도 항상 "유효"</b> 로 보였고, 무효 건에 [무효화] 버튼까지 계속 떴다.
    /// 전자서명은 보관·감사추적 대상이라 이 표시가 틀리면 문서 신뢰가 무너진다.
    /// </summary>
    [Fact]
    public void 서명_유효표시는_IsVoid로_판정한다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.Web", "Pages", "HR", "ESignHistoryPage.razor"));

        Assert.Contains("context.IsVoid", code);
        Assert.DoesNotContain("context.Status == \"signed\"", code);
    }

    /// <summary>화면이 <c>IsVoid</c> 를 보려면 모델에 그 칸이 있어야 한다. 원래 아예 없었다.</summary>
    [Fact]
    public void 서명이력_모델에_IsVoid가_있다()
    {
        var source = ReadSource("src", "HitPan.Web", "Models", "ESignModels.cs");
        Assert.Contains("public bool IsVoid", source);
    }

    // ───────────────────────────────────────────────────────────────
    // P0-A. 퇴사자 로그인 차단
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 퇴사 처리는 <c>employees.is_active</c> 만 껐고 로그인은 <c>users.IsActive</c> 를 본다.
    /// <b>서로 다른 표의 다른 칸이라 퇴사자가 계속 로그인됐다.</b>
    /// </summary>
    [Fact]
    public void 퇴사처리는_로그인_계정도_끈다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.Application", "Services", "EmployeeService.cs"));

        Assert.Contains("UPDATE users", code);
        Assert.Contains("u.is_active = 0", code);
    }

    /// <summary>
    /// 🔴 테넌트 격리(헌법 #2). 계정 차단 쿼리가 테넌트를 안 걸면 다른 회사 계정을 끌 수 있다.
    /// 파일 전체가 아니라 <b>해당 SQL 블록만</b> 잘라 본다 — 다른 쿼리의 조건에 속지 않기 위해서다.
    /// </summary>
    [Fact]
    public void 퇴사처리_계정차단_쿼리는_테넌트로_격리된다()
    {
        var source = ReadSource("src", "HitPan.Application", "Services", "EmployeeService.cs");

        var start = source.IndexOf("UPDATE users", StringComparison.Ordinal);
        Assert.True(start > 0, "계정 차단 쿼리가 있어야 한다");

        var end = source.IndexOf("\"\"\"", start, StringComparison.Ordinal);
        Assert.True(end > start, "SQL 블록의 끝을 찾아야 한다");

        var sql = source[start..end];
        Assert.Contains("@TenantId", sql);
    }

    /// <summary>
    /// 🔴 <b>배선 시험 — 이것이 핵심이다.</b>
    /// 서비스에 메서드를 만들어 놓고 화면이 옛 경로를 그대로 부르면 아무것도 달라지지 않는다.
    /// 8/11 AI 연동에서 정확히 그렇게 당했다.
    /// </summary>
    [Fact]
    public void 화면_퇴사처리_버튼은_새_경로를_탄다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.Web", "Pages", "Settings", "EmployeePage.razor"));

        Assert.Contains("OnClick=\"ResignAsync\"", code);
        Assert.DoesNotContain("OnClick=\"DeleteAsync\"", code);
    }

    /// <summary>
    /// 퇴사 처리 경로가 API 까지 이어지는지 본다. 화면 → 웹서비스 → 컨트롤러 주소가
    /// 한 글자라도 어긋나면 404 이고, 사용자는 "퇴사 처리 실패" 만 본다.
    /// </summary>
    [Fact]
    public void 퇴사처리_주소가_화면과_API에서_일치한다()
    {
        var webService = CodeLines(ReadSource("src", "HitPan.Web", "Services", "EmployeeService.cs"));
        var controller = CodeLines(ReadSource("src", "HitPan.API", "Controllers", "EmployeeController.cs"));

        Assert.Contains("/resign", webService);
        Assert.Contains("[HttpPost(\"{id}/resign\")]", controller);

        Assert.Contains("/resign-precheck", webService);
        Assert.Contains("[HttpGet(\"{id}/resign-precheck\")]", controller);
    }

    // ───────────────────────────────────────────────────────────────
    // 단계1. 숨은 화면 메뉴 등재
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 메뉴에 올린 주소가 실제 화면과 다르면 404 다.
    /// 있는 걸 보이게 하려다 <b>없는 걸 가리키면</b> 더 나쁘다.
    /// </summary>
    [Fact]
    public void 사이드바_HR_메뉴는_전부_실재하는_화면을_가리킨다()
    {
        var sidebar = ReadSource("src", "HitPan.Web", "Layout", "Sidebar.razor");
        var pagesDir = Path.Combine(FindRepoRoot(), "src", "HitPan.Web", "Pages");

        var routes = Regex.Matches(sidebar, @"Href=""(/hr/[a-z\-]+)""")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToArray();

        Assert.NotEmpty(routes);

        var declared = Directory.EnumerateFiles(pagesDir, "*.razor", SearchOption.AllDirectories)
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"@page\s+""([^""]+)""")
                .Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var route in routes)
        {
            Assert.True(declared.Contains(route), $"메뉴가 가리키는 {route} 화면이 실재해야 한다(404 금지)");
        }
    }

    /// <summary>
    /// 전수조사에서 찾은 <b>"만들어 놓고 메뉴에 없던"</b> 화면들이 실제로 등재됐는지 본다.
    /// 특히 초과근무는 사장님이 지시한 야근·주말근무의 토대이고,
    /// 부서 관리는 메신저 부서방의 선행조건이다.
    /// </summary>
    /// <remarks>
    /// 작(2026-08-13) 메뉴 간소화로 <b>목록이 줄었다.</b> 종전에 여기 있던
    /// attendance-calendar·leave-calendar·leave-status·expense-status 네 개는
    /// 각각 근태·휴가·경비 화면으로 합쳐져 <b>메뉴에서 내려간 것이 맞다.</b>
    /// 다만 화면이 없어진 것이 아니므로 <b>주소는 살아 있어야 한다</b> —
    /// 그쪽은 아래 <see cref="합쳐서_내린_주소도_여전히_열린다"/> 가 지킨다.
    /// </remarks>
    [Fact]
    public void 숨어있던_화면들이_메뉴에_올라왔다()
    {
        var sidebar = ReadSource("src", "HitPan.Web", "Layout", "Sidebar.razor");

        string[] mustBeVisible =
        [
            "/hr/overtime",
            "/hr/departments"
        ];

        foreach (var route in mustBeVisible)
        {
            Assert.Contains($"Href=\"{route}\"", sidebar);
        }
    }

    /// <summary>
    /// 메뉴에서 내린 주소가 <b>여전히 열려야 한다.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 메뉴를 줄이는 작업에서 제일 위험한 자리다. 메뉴에서 안 보인다고 화면까지
    /// 지우면, 즐겨찾기로 들어오던 사람과 <b>메신저 문서 링크</b>가 404 를 맞는다
    /// (ChatPopup.razor 가 /hr/leave-status·/hr/expense-status 로 보낸다).
    /// 실제로 8/13 에 메신저 링크 2건이 404 였고 시험이 잡았다 — 같은 사고를 반복하지 않는다.
    /// </remarks>
    [Fact]
    public void 합쳐서_내린_주소도_여전히_열린다()
    {
        var pagesDir = Path.Combine(FindRepoRoot(), "src", "HitPan.Web", "Pages");

        var declared = Directory.EnumerateFiles(pagesDir, "*.razor", SearchOption.AllDirectories)
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"@page\s+""([^""]+)""")
                .Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string[] mergedAway =
        [
            "/hr/employees",           // → 사원관리(/employees)
            "/hr/attendance-calendar", // → 근태 관리 기간 버튼
            "/hr/leave-calendar",      // → 휴가·연차 '휴가 일정' 탭
            "/hr/leave-status",        // → 휴가·연차 '전사 현황' 탭 (메신저 링크)
            "/hr/expense-status",      // → 경비 요약카드      (메신저 링크)
            "/hr/esign-history"        // → 전자근로계약서 '서명 이력' 탭
        ];

        foreach (var route in mergedAway)
        {
            Assert.True(declared.Contains(route),
                $"메뉴에서 내렸어도 {route} 주소는 살아 있어야 한다(즐겨찾기·메신저 문서 링크 404 금지)");
        }
    }

    /// <summary>
    /// 한 주소를 화면 <b>둘</b>이 가지면 라우팅이 터진다.
    /// 합치면서 옛 주소를 새 화면에 얹었으므로, 옛 화면 파일이 남아 있으면 충돌한다.
    /// </summary>
    [Fact]
    public void 같은_주소를_두_화면이_가지지_않는다()
    {
        var pagesDir = Path.Combine(FindRepoRoot(), "src", "HitPan.Web", "Pages");

        var dupes = Directory.EnumerateFiles(pagesDir, "*.razor", SearchOption.AllDirectories)
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"@page\s+""([^""]+)""")
                .Select(m => m.Groups[1].Value))
            .GroupBy(r => r, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.True(dupes.Length == 0, $"한 주소를 여러 화면이 가진다: {string.Join(", ", dupes)}");
    }

    /// <summary>
    /// 작20260822작1 G1-[D] — 직급 관리는 <b>메뉴에서 내린다</b>(사장님 결재 2026-08-23).
    /// <para>사장님: <i>"직책관리 메뉴가 굳이 필요 없눈거야"</i></para>
    /// </summary>
    /// <remarks>
    /// 🔴 <b>메뉴만 내린다. 화면·주소·표는 살린다.</b>
    /// 이 시험이 지키는 것은 "지웠나" 가 아니라 <b>"안 지웠나"</b> 다 —
    /// 메뉴를 내리면서 화면까지 지우면 즐겨찾기·직접입력이 404 가 된다(G1-6, 헌법 #1·#37).
    /// <para>
    /// [반증] 사이드바에 <c>/settings/positions</c> 링크를 되살리면 첫 단언에서 FAIL.
    ///        화면 파일을 지우면 뒤 두 단언에서 FAIL.
    /// </para>
    /// </remarks>
    [Fact]
    public void 직급관리는_메뉴에서_내리되_화면과_주소는_살린다()
    {
        var sidebar = ReadSource("src", "HitPan.Web", "Layout", "Sidebar.razor");

        // 메뉴에서 내렸다 — 둘 다 없어야 한다.
        Assert.DoesNotContain("Href=\"/settings/positions\"", sidebar);
        Assert.DoesNotContain("Href=\"/hr/positions\"", sidebar);

        // 🔴 화면과 주소는 남는다. 지우면 404 다(헌법 #1·#37).
        var settingsPage = Path.Combine(FindRepoRoot(),
            "src", "HitPan.Web", "Pages", "Settings", "PositionsPage.razor");
        Assert.True(File.Exists(settingsPage), "주소는 살린다 — 404 금지(G1-6)");

        var hrPage = Path.Combine(FindRepoRoot(),
            "src", "HitPan.Web", "Pages", "HR", "HrPositionsPage.razor");
        Assert.True(File.Exists(hrPage), "화면과 주소는 남긴다(헌법 #1)");
    }

    // ───────────────────────────────────────────────────────────────
    // 헌법 #15 — 빈 catch 금지
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 빈 catch 는 <b>"서버 오류"와 "자료 0건"을 화면에서 구분 불가능</b>하게 만든다.
    /// </summary>
    [Fact]
    public void 전자서명_서비스에_빈_catch가_없다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.Web", "Services", "ESignService.cs"));
        Assert.DoesNotContain("catch (Exception)", code);
    }

    // ───────────────────────────────────────────────────────────────
    // 검증팀 반려분 — 봉합이 만든 새 결함을 다시 만들지 않게 지킨다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 검증팀 P0-2. <b>이름만 맞추고 타입을 안 맞추면 새 결함이 된다.</b>
    /// <para>
    /// <c>salary_amount</c> 는 NULL 허용인데 웹 모델을 non-nullable <c>decimal</c> 로 두면,
    /// 이름이 맞는 순간 NULL 에서 <c>JsonException</c> 이 난다.
    /// 이름이 안 맞을 때는 그 키를 무시해서 <b>조용했던 자리가 예외 경로로 바뀐다.</b>
    /// 게다가 목록 조회의 catch 가 예외를 삼켜 <b>급여 미입력 계약서 한 장에 목록 전체가 빈 화면</b>이 된다.
    /// </para>
    /// </summary>
    [Fact]
    public void 급여가_비어있어도_계약서가_깨지지_않는다()
    {
        // 급여를 안 적고 저장한 계약서(실무에서 흔하다).
        const string apiJson = """
            {"contractId":"C1","employeeName":"홍길동","salaryAmount":null,"salaryType":"monthly"}
            """;

        var model = JsonSerializer.Deserialize<ContractProbe>(apiJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(model);
        Assert.Null(model!.SalaryAmount);
        Assert.Equal(0m, model.Salary);
    }

    /// <summary>
    /// 🔴 검증팀 P0-3. <c>pay_day varchar(20)</c>·<c>annual_leave varchar(100)</c> 는
    /// <b>자유 문자열을 받으라고 만든 칸</b>이다. 인사담당자는 "매월 25일", "연 15일" 처럼 쓴다.
    /// 이걸 숫자 타입으로 받으면 그 순간 상세 화면이 통째로 안 열린다.
    /// </summary>
    [Fact]
    public void 급여일과_연차가_자유문자열이어도_계약서가_깨지지_않는다()
    {
        const string apiJson = """
            {"contractId":"C1","payDay":"매월 25일","annualLeave":"연 15일"}
            """;

        var model = JsonSerializer.Deserialize<ContractProbe>(apiJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(model);
        Assert.Equal("매월 25일", model!.PayDay);
        Assert.Equal("연 15일", model.AnnualLeaveDays);
    }

    /// <summary>
    /// 위 두 시험은 탐침을 쓰므로, <b>실제 웹 모델</b>의 타입도 지킨다.
    /// 숫자 타입으로 되돌리면 고객 화면이 조용히 빈다.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>읽기 모델만</b> 본다. 같은 파일의 <c>CreateLaborContractModel</c>(작성용)은
    /// UI 바인딩용 속성을 <c>[JsonIgnore]</c> 로 두고 별도 직렬화 속성으로 API 와 1:1 매핑하는
    /// 구조라(2026-06-22 봉합) <c>int? PayDay</c> 가 정상이다.
    /// 🔴 파일 전체를 뭉뚱그려 검사했다가 이 정상 코드를 결함으로 오인해 거짓 경보를 냈다 —
    /// 그래서 <b>클래스 블록을 잘라</b> 본다.
    /// </remarks>
    [Fact]
    public void 읽기모델_타입이_실제_컬럼과_맞다()
    {
        var source = ReadSource("src", "HitPan.Web", "Models", "ESignModels.cs");

        foreach (var className in new[] { "LaborContractListModel", "LaborContractDetailModel" })
        {
            var block = ClassBlock(source, className);

            // salary_amount decimal(15,2) NULL → decimal?
            Assert.Contains("public decimal? SalaryAmount", block);
            Assert.DoesNotContain("public decimal Salary {", block);
        }

        // pay_day varchar(20) NULL / annual_leave varchar(100) NULL → string?
        var detail = ClassBlock(source, "LaborContractDetailModel");
        Assert.Contains("public string? PayDay", detail);
        Assert.Contains("public string? AnnualLeaveDays", detail);
        Assert.DoesNotContain("public int? PayDay", detail);
        Assert.DoesNotContain("public decimal? AnnualLeaveDays", detail);
    }

    /// <summary>지정한 클래스 선언부터 다음 클래스 선언 직전까지를 잘라낸다.</summary>
    private static string ClassBlock(string source, string className)
    {
        var start = source.IndexOf($"class {className}", StringComparison.Ordinal);
        Assert.True(start > 0, $"{className} 이 있어야 한다");

        var next = source.IndexOf("\npublic sealed class ", start, StringComparison.Ordinal);
        return next > start ? source[start..next] : source[start..];
    }

    /// <summary>
    /// 🔴 검증팀 P0-1 — <b>가장 무거웠던 건.</b>
    /// 대표계정 제외 조건을 <c>account_type &lt;&gt; 'tenant_owner'</c> 로 썼는데
    /// 이 시스템에 <c>tenant_owner</c> 라는 값은 <b>존재하지 않는다</b>(실측: <c>tenant_admin</c> 뿐).
    /// 비교가 항상 참이라 가드가 아무도 막지 못했고, 대표계정을 퇴사 처리하면
    /// <b>고객사 전체가 로그인 불능</b>이 된다 — 헌법 #38 상 로컬에 복구 경로가 없다.
    /// 주석은 "제외한다"고 선언했는데 구현이 그 선언을 실행하지 않았다.
    /// </summary>
    [Fact]
    public void 대표계정은_퇴사처리로_잠기지_않는다()
    {
        var source = ReadSource("src", "HitPan.Application", "Services", "EmployeeService.cs");

        var start = source.IndexOf("UPDATE users", StringComparison.Ordinal);
        Assert.True(start > 0);
        var end = source.IndexOf("\"\"\"", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var sql = source[start..end];

        // 부모 여부를 실제로 가리는 컬럼은 is_parent 다(UserService·프로비저너가 쓰는 그 칸).
        Assert.Contains("is_parent", sql);

        // 존재하지 않는 값으로 가드하면 아무도 막히지 않는다.
        Assert.DoesNotContain("tenant_owner", sql);
    }

    /// <summary>
    /// 🔴 검증팀 P1-4. 주석은 "호출부에서 트랜잭션으로 감싼다"고 했는데
    /// <b>감싸는 호출부가 없었다.</b> 사원만 퇴사 처리되고 계정이 살아남으면,
    /// 화면상 이미 퇴사자라 되돌릴 방법이 없다.
    /// </summary>
    [Fact]
    public void 퇴사처리는_한_트랜잭션으로_묶인다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.Application", "Services", "EmployeeService.cs"));

        Assert.Contains("BeginTransaction", code);
        Assert.Contains("tx.Commit()", code);
        Assert.Contains("tx.Rollback()", code);
    }

    /// <summary>
    /// 🔴 검증팀 P1-5. 계정이 없는 사원(실측 12명 중 11명)에게도
    /// "로그인 계정도 함께 차단됐습니다" 라고 안내하던 <b>거짓 표시</b>를 없앤다.
    /// 대화상자는 계정 유무를 정확히 판정하면서 결과 안내만 단정하고 있었다.
    /// </summary>
    [Fact]
    public void 계정을_못_껐으면_껐다고_말하지_않는다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.Web", "Pages", "Settings", "EmployeePage.razor.cs"));

        // 실제 결과(accountBlocked)를 보고 문구를 가른다.
        Assert.Contains("accountBlocked", code);

        // 무조건 차단됐다고 말하는 단정 문구가 남아 있으면 안 된다.
        Assert.DoesNotContain(
            "Snackbar.Add(\"퇴사 처리했습니다. 로그인 계정도 함께 차단됐습니다.\", Severity.Success)",
            code);
    }

    /// <summary>
    /// 🔴 검증팀 P2-6. 퇴사 처리가 실패하면 <b>사용자에게 알린다.</b>
    /// 앞서는 아무 말 없이 대화상자가 열린 채 남아, 안 눌린 줄 알고 다시 누르게 했다.
    /// 옛 [삭제] 경로는 실패를 알려줬는데 새 경로가 그보다 후퇴해 있었다.
    /// </summary>
    [Fact]
    public void 퇴사처리_실패를_사용자에게_알린다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.Web", "Pages", "Settings", "EmployeeResignDialog.razor"));

        Assert.Contains("Snackbar.Add", code);
        Assert.Contains("Severity.Error", code);
    }

    /// <summary>
    /// 🔴 검증팀 P2-7. 퇴사 처리는 <b>사람의 계정을 끄는 행위</b>다.
    /// 누가·언제 했는지 기록이 없으면 노무 분쟁에서 근거를 댈 수 없다.
    /// </summary>
    [Fact]
    public void 퇴사처리는_감사기록을_남긴다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.Application", "Services", "EmployeeService.cs"));

        Assert.Contains("_audit.LogAsync", code);
        Assert.Contains("\"resign\"", code);
    }

    /// <summary>
    /// 🔴 반자동 원칙(사장님 2026-08-12). 근로계약서는 서명되면 법적 문서가 된다.
    /// 입력하지 않은 연차를 시스템이 <b>15일이라고 단정해 찍으면 안 된다</b> —
    /// 연차는 사업장 규모·근속·고용형태에 따라 달라진다.
    /// </summary>
    [Fact]
    public void 계약서가_연차_기본값을_단정하지_않는다()
    {
        var code = CodeLines(ReadSource("src", "HitPan.Web", "Pages", "HR", "LaborContractSignPage.razor"));

        Assert.DoesNotContain("?? \"15\"", code);
        Assert.DoesNotContain("?? \"25\"", code);
    }

    /// <summary>
    /// JSON 짝 맞춤을 실제 직렬화로 증명하기 위한 탐침.
    /// 실제 웹 모델과 <b>같은 표기·같은 타입</b>을 쓴다.
    /// </summary>
    private sealed class SalaryProbe
    {
        [JsonPropertyName("salaryAmount")]
        public decimal? SalaryAmount { get; set; }

        [JsonIgnore]
        public decimal Salary => SalaryAmount ?? 0m;
    }

    /// <summary>NULL·자유문자열 내성을 증명하기 위한 탐침.</summary>
    private sealed class ContractProbe
    {
        [JsonPropertyName("salaryAmount")]
        public decimal? SalaryAmount { get; set; }

        [JsonIgnore]
        public decimal Salary => SalaryAmount ?? 0m;

        [JsonPropertyName("payDay")]
        public string? PayDay { get; set; }

        [JsonPropertyName("annualLeave")]
        public string? AnnualLeaveDays { get; set; }
    }
}
