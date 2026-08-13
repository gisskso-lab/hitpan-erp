using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 그룹웨어 단계8 급여·퇴직금 게이트. 작(2026-08-13).
/// </summary>
/// <remarks>
/// 🔴 <b>사장님이 정한 방식</b>(2026-08-13):
/// <i>"급여는 자동계산하지 말고 수동으로 int값 직접 받아서 입력하는게 가장 깔끔함"</i> /
/// <i>"각 고객사 니즈나 사정도 부합시킬 수 있고."</i> /
/// <i>"권한 계층분리로 급여를 관리해도 충분히 됨."</i>
///
/// 이 시험들은 <b>되돌아가는 것을 막는다</b> — "4대보험 자동계산 넣어주면 좋잖아" 가
/// 반드시 다시 나오는데, 그건 사장님이 명시적으로 안 한다고 정한 것이다.
/// </remarks>
public sealed class PayrollGuardTests
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

    /// <summary>주석 줄을 걷어낸 코드만 남긴다. 설명문에 걸려 헛통과·헛실패하지 않게.</summary>
    private static string StripComments(string source)
        => string.Join('\n', source.Split('\n').Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("//", StringComparison.Ordinal)
                && !t.StartsWith("///", StringComparison.Ordinal)
                && !t.StartsWith("--", StringComparison.Ordinal)
                && !t.StartsWith("*", StringComparison.Ordinal);
        }));

    /// <summary>
    /// SQL 의 <c>@파라미터</c> 마다 Dapper 파라미터 객체에 짝이 있는지 본다.
    /// </summary>
    /// <remarks>
    /// 🔴 단계4 P0-1 과 같은 병을 막는다 — SQL 에만 있고 객체에 없으면 값이 조용히 NULL 로 들어간다.
    /// <c>AllowUserVariables=true</c> 때문에 <b>예외조차 안 난다.</b>
    /// 급여에서 이게 나면 <b>금액이 0 으로 저장</b>된다.
    /// </remarks>
    private static void AssertDapperParametersBound(string source, string sqlParam, int expectedAtLeast)
    {
        Assert.StartsWith("@", sqlParam);
        var propName = sqlParam[1..];
        var code = StripComments(source);

        var sqlUses = Regex.Matches(code, Regex.Escape(sqlParam) + @"\b").Count;
        Assert.True(sqlUses >= expectedAtLeast,
            $"{sqlParam} 가 SQL 에서 {expectedAtLeast}번 이상 쓰여야 한다(실제 {sqlUses}).");

        var bindings = Regex.Matches(code, @"\b" + Regex.Escape(propName) + @"\s*=").Count;

        Assert.True(bindings >= expectedAtLeast,
            $"🔴 {sqlParam} 가 SQL 에 {sqlUses}번 쓰였는데 파라미터 바인딩은 {bindings}곳뿐이다. "
            + "SQL 에만 있고 파라미터 객체에 없으면 값이 조용히 NULL 로 들어간다"
            + "(AllowUserVariables=true 라 예외도 안 난다). 급여에서 나면 금액이 0 으로 저장된다.");
    }

    // ───────────────────────────────────────────────────────────────
    // 사장님 지시 — 계산하지 않는다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>4대보험·소득세를 계산하지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// 사장님: <i>"급여는 자동계산하지 말고 수동으로 int값 직접 받아서 입력하는게 가장 깔끔함"</i>
    ///
    /// 왜 맞나 — 국민연금 9%→9.5%(2026-01) · 건강보험 7.09%→7.19%(2026-01) ·
    /// 간이세액표 개정(2026-02). <b>매년 바뀐다.</b> 회사마다 상여 주기·수당·비과세도 다르다.
    /// 틀리면 <b>직원 돈이 틀리고</b> 되돌리기 어렵다.
    ///
    /// ⚠️ 이 시험은 <b>되돌아가는 것을 막는다.</b> 요율 자동계산은 늘 다시 제안된다.
    /// </remarks>
    [Fact]
    public void 급여는_요율로_계산하지_않는다()
    {
        var svc = StripComments(
            ReadSource("src", "HitPan.Application", "Services", "PayrollService.cs"));

        // 🔴 요율 상수가 있으면 안 된다(국민연금 4.5%·건강보험 3.545% 등).
        foreach (var rate in new[]
                 {
                     "0.045", "0.0450", "0.03545", "0.009", "0.0009",
                     "0.095", "0.0719", "4.5m", "9.5m", "7.19m",
                 })
        {
            Assert.DoesNotContain(rate, svc);
        }

        // 요율 표를 읽지도 않는다.
        Assert.DoesNotContain("insurance_rate", svc);
        Assert.DoesNotContain("tax_table", svc);
        Assert.DoesNotContain("labor_policy_settings", svc);

        // 곱셈으로 금액을 만들어내면 안 된다(금액 × 요율).
        Assert.DoesNotMatch(new Regex(@"Amount\s*\*\s*"), svc);
        Assert.DoesNotMatch(new Regex(@"AvgWage\s*\*\s*"), svc);
        Assert.DoesNotMatch(new Regex(@"SeveranceAmount\s*[*/]"), svc);
    }

    /// <summary>
    /// 🔴 서버가 하는 계산은 <b>줄의 합계</b>뿐이다 — 그것도 화면 값을 안 믿기 위해서다.
    /// </summary>
    /// <remarks>
    /// 화면이 보내온 합계를 그대로 저장하면 <b>줄과 합계가 어긋난 명세</b>가 남는다.
    /// 그러면 명세서에 찍히는 숫자와 회계로 넘어가는 숫자가 갈라진다.
    /// </remarks>
    [Fact]
    public void 합계는_서버가_줄을_더해서_낸다()
    {
        var svc = StripComments(
            ReadSource("src", "HitPan.Application", "Services", "PayrollService.cs"));

        Assert.Contains("lines.Where(l => l.LineType == PayrollLineTypeLabels.Payment).Sum(l => l.Amount)", svc);
        Assert.Contains("lines.Where(l => l.LineType == PayrollLineTypeLabels.Deduct).Sum(l => l.Amount)", svc);
        Assert.Contains("netPayment = totalPayment - totalDeduct", svc);

        // 🔴 요청에 합계 칸이 있으면 안 된다. 있으면 언젠가 그걸 쓰게 된다.
        var dto = ReadSource("src", "HitPan.Application", "DTOs", "Payroll", "PayrollDtos.cs");
        var reqBlock = Regex.Match(dto,
            @"class SavePayrollSlipRequest.*?\n\}", RegexOptions.Singleline).Value;

        Assert.False(string.IsNullOrEmpty(reqBlock), "SavePayrollSlipRequest 를 찾아야 한다");
        Assert.DoesNotContain("TotalPayment", reqBlock);
        Assert.DoesNotContain("TotalDeduct", reqBlock);
        Assert.DoesNotContain("NetPayment", reqBlock);
    }

    /// <summary>
    /// 🔴 퇴직금도 <b>금액을 받는다.</b> 법정 산식을 우리가 돌리지 않는다.
    /// </summary>
    /// <remarks>
    /// 산식(평균임금 × 30일 × 재직일수/365)이 있지만 평균임금에 상여·연차수당을 어떻게 넣는지가
    /// 회사마다 다르고 <b>다툼이 잦다</b>. 퇴직연금(DB·DC·IRP)이면 산식이 다르다.
    /// 틀리면 <b>법적 분쟁</b>이 된다.
    /// </remarks>
    [Fact]
    public void 퇴직금도_금액을_직접_받는다()
    {
        var svc = StripComments(
            ReadSource("src", "HitPan.Application", "Services", "PayrollService.cs"));

        // 산식 흔적이 있으면 안 된다.
        Assert.DoesNotContain("* 30", svc);
        Assert.DoesNotContain("/ 365", svc);
        Assert.DoesNotContain("/ 365.25", svc);

        // 받은 금액이 그대로 저장돼야 한다(INSERT + UPDATE 양쪽).
        var raw = ReadSource("src", "HitPan.Application", "Services", "PayrollService.cs");
        AssertDapperParametersBound(raw, "@AvgWage", 2);
        AssertDapperParametersBound(raw, "@Severance", 2);
        AssertDapperParametersBound(raw, "@Tax", 2);

        // 실지급액만 서버가 뺀다(빼기 하나까지 사람에게 시키면 오타가 난다).
        Assert.Contains("request.SeveranceAmount - request.TaxAmount", svc);
    }

    // ───────────────────────────────────────────────────────────────
    // 보호는 권한 계층이 한다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 급여는 <b>권한 계층</b>으로 막는다. 사장님:
    /// <i>"권한 계층분리로 급여를 관리해도 충분히 됨."</i> /
    /// <i>"히트판의 계층분리는 굉장히 촘촘하게 설계되어있음"</i>
    /// </summary>
    /// <remarks>
    /// 실측: <c>user_permissions</c> 가 메뉴별 × 5동작으로 갈리고 부모계정은 바이패스한다.
    /// ⇒ 급여 API <b>전부</b>가 <c>menu_code='PAYROLL'</c> 로 막혀야 한다.
    /// 하나라도 빠지면 그 자리로 남의 급여가 새 나간다.
    /// </remarks>
    [Fact]
    public void 급여를_쓰는_동작은_전부_권한으로_막혀_있다()
    {
        var ctrl = ReadSource("src", "HitPan.API", "Controllers", "PayrollController.cs");
        var code = StripComments(ctrl);

        // 메뉴 코드가 고정돼 있어야 한다.
        Assert.Contains("private const string Menu = \"PAYROLL\";", code);

        // 🔴 쓰기(POST)는 **전부** 권한으로 막아야 한다 — 남의 급여를 만들 이유가 없다.
        //    ⚠️ 조회(GET)는 일부러 안 막는다. 막으면 일반 직원이 자기 명세도 못 본다
        //      (사장님: "자기 급여만 볼 수 있게"). 범위는 ResolveScopeAsync 가 좁힌다.
        var posts = Regex.Matches(code,
            @"\[HttpPost\([^\]]*\)\]\s*\r?\n\s*(?<attr>\[RequirePermission\(Menu,\s*""[^""]+""\)\])?");

        var postCount = Regex.Matches(code, @"\[HttpPost\(").Count;
        var postWithPerm = posts.Count(m => m.Groups["attr"].Success);

        Assert.True(postCount > 0, "급여 컨트롤러에 쓰기 동작이 있어야 한다");
        Assert.True(postCount == postWithPerm,
            $"🔴 급여 쓰기 동작 {postCount}개 중 권한이 걸린 것은 {postWithPerm}개뿐이다. "
            + "하나라도 빠지면 아무나 남의 급여를 만들거나 확정할 수 있다.");

        // 동작 문자열이 PermissionService 가 아는 것이어야 한다(view/create/update/delete/export).
        foreach (var m in Regex.Matches(code, @"\[RequirePermission\(Menu,\s*""(?<action>[^""]+)""\)\]")
                     .Select(x => x.Groups["action"].Value))
        {
            Assert.Contains(m, new[] { "view", "create", "update", "delete", "export" });
        }

        // 전 직원 명단이 나가는 context 는 권한으로 막아야 한다.
        var ctxBlock = Regex.Match(code,
            @"\[HttpGet\(""context""\)\]\s*\r?\n\s*(?<attr>\[[^\]]*\])").Groups["attr"].Value;
        Assert.Contains("RequirePermission", ctxBlock);
    }

    /// <summary>
    /// 🔴 <b>남의 급여는 못 본다.</b> 사장님 지시(2026-08-13):
    /// <i>"a직원 급여를 b직원이 볼수 없어야해. 자기 급여만 볼수 있게."</i> /
    /// <i>"자기 외 직원들 급여는 부모계정인 사장, 담당자만 볼수있음."</i> /
    /// <i>"부모계정이 권한을 준 담당계정은 다 볼 수 있게. 왜냐면 월급을 줘야하니까"</i>
    /// </summary>
    /// <remarks>
    /// 세 갈래여야 한다:
    /// <list type="bullet">
    ///   <item>부모계정(사장) — 전 직원</item>
    ///   <item>급여 담당자(PAYROLL 권한) — 전 직원 (월급을 줘야 하므로)</item>
    ///   <item><b>일반 직원 — 본인 것만</b></item>
    /// </list>
    ///
    /// 🔴 <c>[RequirePermission]</c> 만으로는 <b>이걸 못 만든다</b> — 그 속성은 메뉴 단위라
    /// 통과하면 전 직원이 나오고, 막으면 직원이 자기 것도 못 본다.
    /// </remarks>
    [Fact]
    public void 일반_직원은_본인_급여만_본다()
    {
        var ctrl = ReadSource("src", "HitPan.API", "Controllers", "PayrollController.cs");
        var code = StripComments(ctrl);

        // 세 갈래 판정이 있어야 한다.
        Assert.Contains("CanSeeOthersAsync", code);
        Assert.Contains("ResolveScopeAsync", code);

        // 부모계정 — 전 직원
        Assert.Contains("_currentTenant.AccountType, \"tenant_admin\"", code);
        // 담당자 — 권한을 받았으면 전 직원(월급을 줘야 하니까)
        Assert.Contains("HasPermissionAsync(_currentTenant.UserId, _currentTenant.TenantId, Menu, \"view\")", code);

        // 🔴 그 외에는 **본인 id 로 덮는다.** 화면이 보내온 값을 믿지 않는다 —
        //    주소창에 남의 id 를 넣으면 그대로 새 나간다.
        Assert.Contains("var me = CurrentEmployeeId();", code);
        Assert.Contains("return (true, me);", code);

        // 목록·퇴직금이 그 판정을 실제로 써야 한다(안 쓰면 만들어만 둔 것이다).
        foreach (var ep in new[] { "GetSlips", "GetSeverance" })
        {
            var block = Regex.Match(code,
                $@"public\s+async\s+Task<IActionResult>\s+{ep}\b.*?\n    \}}",
                RegexOptions.Singleline).Value;

            Assert.False(string.IsNullOrEmpty(block), $"{ep} 를 찾아야 한다");
            Assert.Contains("ResolveScopeAsync", block);
            Assert.Contains("scoped", block);
        }

        // 🔴 단건 조회도 막아야 한다. 목록만 좁히고 id 로 직접 열리면 막은 것이 아니다.
        var single = Regex.Match(code,
            @"public\s+async\s+Task<IActionResult>\s+GetSlip\(.*?\n    \}",
            RegexOptions.Singleline).Value;

        Assert.False(string.IsNullOrEmpty(single), "GetSlip 을 찾아야 한다");
        Assert.Contains("CanSeeOthersAsync", single);
        Assert.Contains("dto.EmployeeId != CurrentEmployeeId()", single);
        Assert.Contains("return Forbid()", single);
    }

    /// <summary>
    /// 🔴 본인 판정에 <c>user_id</c> 가 아니라 <b><c>employee_id</c></b> 를 써야 한다.
    /// </summary>
    /// <remarks>
    /// <b>둘은 별개 GUID</b>다. AuthService 가 두 클레임을 따로 발급한다
    /// (<c>TenantMiddleware.cs:72</c> — 2026-06-21 에 이걸 혼동해 P0 가 났다).
    ///
    /// 여기서 <c>user_id</c> 를 쓰면 아무 명세와도 안 맞아 직원이 자기 급여를 못 보거나,
    /// 더 나쁘게는 <b>엉뚱한 사람 것이 보인다.</b>
    /// </remarks>
    [Fact]
    public void 본인_판정에_employee_id_를_쓴다()
    {
        var ctrl = ReadSource("src", "HitPan.API", "Controllers", "PayrollController.cs");
        var code = StripComments(ctrl);

        Assert.Contains("User.FindFirstValue(\"employee_id\")", code);

        // 🔴 UserId 로 명세를 찾으면 안 된다.
        Assert.DoesNotMatch(new Regex(@"ScopedEmployeeId\s*=\s*_currentTenant\.UserId"), code);
        Assert.DoesNotMatch(new Regex(@"return \(true, _currentTenant\.UserId\)"), code);
    }

    /// <summary>
    /// 🔴 테넌트는 JWT 에서만 온다(헌법 #2). 급여에서 새면 <b>남의 회사 급여</b>가 보인다.
    /// </summary>
    [Fact]
    public void 급여가_테넌트를_파라미터로_받지_않는다()
    {
        var ctrl = StripComments(
            ReadSource("src", "HitPan.API", "Controllers", "PayrollController.cs"));

        Assert.DoesNotContain("[FromQuery] string tenantId", ctrl);
        Assert.DoesNotContain("[FromBody] string tenantId", ctrl);
        Assert.DoesNotContain("[FromRoute] string tenantId", ctrl);
        Assert.Contains("HttpContext.Items[\"TenantId\"]", ctrl);

        // 모든 쿼리가 tenant_id 로 걸러야 한다.
        var svc = StripComments(
            ReadSource("src", "HitPan.Application", "Services", "PayrollService.cs"));

        var selects = Regex.Matches(svc, @"FROM payroll_slips|FROM payroll_slip_lines|FROM severance_payments");
        Assert.True(selects.Count > 0, "급여 표를 읽는 쿼리가 있어야 한다");

        var tenantFilters = Regex.Matches(svc, @"tenant_id\s*=\s*@TenantId").Count;
        Assert.True(tenantFilters >= selects.Count,
            $"🔴 급여 표를 읽는 곳이 {selects.Count}개인데 tenant 필터는 {tenantFilters}개다. "
            + "하나라도 빠지면 남의 회사 급여가 보인다.");
    }

    // ───────────────────────────────────────────────────────────────
    // 휴직 연동 — 사장님이 말한 "자연스럽게 해결"
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 급여가 <b>휴직에서 정해 둔 금액을 가져올 수 있어야</b> 한다.
    /// </summary>
    /// <remarks>
    /// 사장님(2026-08-13): <i>"휴직시 급여 : 텍스트 박스로 수동입력 →
    /// 그러면 자연스럽게 급여, 회계이슈도 해결될듯"</i>
    ///
    /// 해결되려면 급여가 <b>실제로 가져와야</b> 한다. 안 이어지면 담당자가
    /// 휴직자를 재직자로 착각해 정상 급여를 지급한다(헌법 #20 — 흐름은 안 끊긴다).
    ///
    /// ⚠️ 다만 <b>자동으로 넣지는 않는다</b> — 보여주고, 담당자가 눌러야 들어간다(반자동).
    /// </remarks>
    [Fact]
    public void 급여가_휴직_금액을_가져온다()
    {
        var svc = StripComments(
            ReadSource("src", "HitPan.Application", "Services", "PayrollService.cs"));

        // 그 달에 걸치는 휴직을 잡아야 한다 — 달 중간에 시작/끝나는 건이 흔하다.
        Assert.Contains("employee_leave_of_absence", svc);
        Assert.Contains("a.start_date <= @Last", svc);
        Assert.Contains("COALESCE(a.actual_return_date, a.end_date) >= @First", svc);
        Assert.Contains("a.pay_amount   AS AbsencePayAmount", svc);

        // 🔴 자동으로 급여 항목에 넣으면 안 된다. 담당자가 눌러야 들어간다.
        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "PayrollPage.razor");
        Assert.Contains("ApplyAbsencePay", page);
        Assert.Contains("OnClick=\"ApplyAbsencePay\"", page);

        // 화면이 휴직 사실을 표시해야 한다 — 담당자가 지나치면 안 된다.
        Assert.Contains("이 달에 휴직이 있습니다", page);
    }

    // ───────────────────────────────────────────────────────────────
    // 데이터 무결
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 같은 사람의 같은 달 명세는 <b>하나뿐</b>이다. 두 장이면 이중 지급이 난다.
    /// </summary>
    [Fact]
    public void 같은_달_명세는_하나뿐이다()
    {
        var ddl = ReadSource("src", "HitPan.API", "Migrations", "SQL", "DB-100_payroll.sql");
        Assert.Contains("UNIQUE KEY `uk_payroll_emp_month` (`tenant_id`, `employee_id`, `pay_year`, `pay_month`)", ddl);

        // 서버도 미리 막고 사유를 말해 준다(DB 오류 그대로 보여주면 못 알아본다).
        var svc = StripComments(
            ReadSource("src", "HitPan.Application", "Services", "PayrollService.cs"));
        Assert.Contains("월 명세가 이미 있습니다", svc);
    }

    /// <summary>
    /// 🔴 확정한 급여는 <b>못 고친다.</b> 확정한 급여가 뒤에서 바뀌면 명세서가 거짓이 된다.
    /// </summary>
    [Fact]
    public void 확정한_급여는_못_고친다()
    {
        var svc = StripComments(
            ReadSource("src", "HitPan.Application", "Services", "PayrollService.cs"));

        Assert.Contains("if (status is not \"draft\")", svc);
        Assert.Contains("상태라 고칠 수 없습니다", svc);

        // 확정·지급·취소가 상태 조건을 걸고 움직여야 한다(멱등·역행 방지).
        Assert.Contains("AND status = 'draft'", svc);
        Assert.Contains("AND status = 'confirmed'", svc);
    }

    /// <summary>
    /// 🔴 금액은 <c>decimal</c> 이다(헌법 #4 — float/double 금지).
    /// </summary>
    /// <remarks>
    /// 급여에서 부동소수를 쓰면 원 단위가 어긋나고, 그 차이가 <b>직원 통장 금액</b>이 된다.
    /// </remarks>
    [Fact]
    public void 급여_금액이_decimal_이다()
    {
        var ddl = ReadSource("src", "HitPan.API", "Migrations", "SQL", "DB-100_payroll.sql");

        foreach (var col in new[]
                 {
                     "`total_payment` decimal(15,2)", "`total_deduct`  decimal(15,2)",
                     "`net_payment`   decimal(15,2)", "`amount`      decimal(15,2)",
                     "`avg_wage`      decimal(15,2)", "`severance_amount` decimal(15,2)",
                 })
        {
            Assert.Contains(col, ddl);
        }

        // ⚠️ 주석에 "float/double 금지" 라고 적어 뒀으므로 **컬럼 정의 줄만** 본다
        //    (실측: 그 설명문에 걸려 헛실패했다).
        var columnLines = StripComments(ddl);
        Assert.DoesNotContain("float", columnLines);
        Assert.DoesNotContain("double", columnLines);

        var dto = ReadSource("src", "HitPan.Application", "DTOs", "Payroll", "PayrollDtos.cs");
        Assert.DoesNotMatch(new Regex(@"(double|float)\s+(Amount|TotalPayment|NetPayment|SeveranceAmount)"), dto);
    }

    /// <summary>
    /// 🔴 새 표는 <c>ENGINE=InnoDB</c> 명시(헌법 #17) · 테넌트 칸(헌법 #2) · 멱등.
    /// </summary>
    [Fact]
    public void 급여표가_헌법을_지킨다()
    {
        var ddl = ReadSource("src", "HitPan.API", "Migrations", "SQL", "DB-100_payroll.sql");

        // ⚠️ 개수를 세지 않는다 — 주석에 같은 말이 있으면 헛실패한다(실측).
        //    **표마다 실제로 있는지**를 본다. 그게 원래 확인하려던 것이다.
        foreach (var t in new[] { "payroll_slips", "payroll_slip_lines", "severance_payments" })
        {
            var block = Regex.Match(ddl,
                $@"CREATE TABLE IF NOT EXISTS `{t}`.*?ENGINE=InnoDB[^;]*;", RegexOptions.Singleline).Value;

            Assert.False(string.IsNullOrEmpty(block),
                $"🔴 {t} 가 'CREATE TABLE IF NOT EXISTS' + 'ENGINE=InnoDB' 로 정의돼야 한다"
                + "(헌법 #17 · 멱등).");

            Assert.Contains("`tenant_id`", block);           // 헌법 #2
            Assert.Contains("utf8mb4_unicode_ci", block);     // Collation 통일
        }
    }

    /// <summary>
    /// 🔴 마이그레이션이 <b>고객에게 가는 자리</b>에 있어야 한다(8/12 실제 사고).
    /// </summary>
    [Fact]
    public void 급여_마이그가_고객에게_가는_자리에_있다()
    {
        var path = Path.Combine(RepoRoot(), "src", "HitPan.API", "Migrations", "SQL",
            "DB-100_payroll.sql");

        Assert.True(File.Exists(path),
            "🔴 마이그는 src/HitPan.API/Migrations/SQL/ 에 있어야 고객에게 간다. "
            + "다른 자리에 두면 빌드·시험·워크플로가 전부 통과하고도 고객 화면이 죽는다(8/12 실제 사고).");
    }

    // ───────────────────────────────────────────────────────────────
    // 화면·연결
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 만들어 놓고 메뉴에 없으면 없는 것과 같다(단계1 교훈 — 숨은 화면 6개).
    /// </summary>
    [Fact]
    public void 급여_화면이_메뉴에_올라와_있다()
    {
        var sidebar = ReadSource("src", "HitPan.Web", "Layout", "Sidebar.razor");
        Assert.Contains("/hr/payroll", sidebar);

        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "PayrollPage.razor");
        Assert.Contains("@page \"/hr/payroll\"", page);

        var webProgram = ReadSource("src", "HitPan.Web", "Program.cs");
        Assert.Contains("AddScoped<PayrollService>()", webProgram);

        var apiProgram = ReadSource("src", "HitPan.API", "Program.cs");
        Assert.Contains("AddScoped<IPayrollService, PayrollService>()", apiProgram);
    }

    /// <summary>
    /// 🔴 화면이 <b>실패와 0건을 구분</b>해야 한다.
    /// </summary>
    /// <remarks>
    /// 급여에서 이걸 뭉개면 특히 위험하다 — 급여가 있는데 "없다" 로 보이면
    /// 담당자가 다시 만들어 <b>이중 지급</b>이 난다.
    /// 권한 없음(403)도 실패라 사유가 보여야 한다.
    /// </remarks>
    [Fact]
    public void 화면이_실패와_영건을_구분한다()
    {
        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "PayrollPage.razor");

        Assert.Contains("else if (_slips is null)", page);
        Assert.Contains("불러오지 못했습니다", page);
        Assert.Contains("else if (_slips.Count == 0)", page);

        var svc = ReadSource("src", "HitPan.Web", "Services", "PayrollService.cs");
        Assert.Contains("public async Task<List<PayrollSlipModel>?> GetSlipsAsync", svc);

        // 🔴 403 은 권한 없음이다 — 급여는 권한으로 막으므로 이 안내가 꼭 필요하다.
        Assert.Contains("급여를 볼 권한이 없습니다", svc);
    }

    /// <summary>
    /// 🔴 양쪽 이름표가 같아야 한다. Web 은 Application 을 참조하지 않아 <b>두 벌</b>이 있다.
    /// </summary>
    [Fact]
    public void 급여_코드값이_양쪽에서_같다()
    {
        var appDto = ReadSource("src", "HitPan.Application", "DTOs", "Payroll", "PayrollDtos.cs");
        var webModel = ReadSource("src", "HitPan.Web", "Models", "PayrollModels.cs");

        foreach (var status in new[] { "draft", "confirmed", "paid", "cancelled" })
        {
            Assert.Contains($"\"{status}\"", appDto);
            Assert.Contains($"[\"{status}\"]", webModel);
        }

        foreach (var t in new[] { "payment", "deduct" })
        {
            Assert.Contains($"\"{t}\"", appDto);
            Assert.Contains($"[\"{t}\"]", webModel);
        }

        foreach (var p in new[] { "direct", "db", "dc", "irp" })
        {
            Assert.Contains($"\"{p}\"", appDto);
            Assert.Contains($"[\"{p}\"]", webModel);
        }

        // 모르는 값은 그대로 보여준다 — 뭉개면 잘못된 값이 정상으로 보인다.
        Assert.Contains("Map.TryGetValue(code, out var v) ? v : code", appDto);
        Assert.Contains("Map.TryGetValue(code, out var v) ? v : code", webModel);
    }

    /// <summary>
    /// 🔴 빈 catch 금지(헌법 #15).
    /// </summary>
    [Fact]
    public void 급여_경로에_빈_catch_가_없다()
    {
        foreach (var parts in new[]
                 {
                     new[] { "src", "HitPan.Application", "Services", "PayrollService.cs" },
                     new[] { "src", "HitPan.API", "Controllers", "PayrollController.cs" },
                     new[] { "src", "HitPan.Web", "Services", "PayrollService.cs" },
                 })
        {
            var src = StripComments(ReadSource(parts));

            var empty = Regex.Matches(src, @"catch\s*(\([^)]*\))?\s*\{\s*\}");
            Assert.True(empty.Count == 0,
                $"🔴 {string.Join('/', parts)} 에 빈 catch 가 {empty.Count}개 있다(헌법 #15).");

            var swallow = Regex.Matches(src, @"catch\s*\{\s*return\s+(false|null|new\(\))\s*;\s*\}");
            Assert.True(swallow.Count == 0,
                $"🔴 {string.Join('/', parts)} 에 예외를 통째로 버리는 catch 가 {swallow.Count}개 있다.");
        }
    }

    /// <summary>
    /// 🔴 급여 변경은 <b>감사로그</b>에 남는다. 돈이다.
    /// </summary>
    [Fact]
    public void 급여_변경이_감사로그에_남는다()
    {
        var svc = StripComments(
            ReadSource("src", "HitPan.Application", "Services", "PayrollService.cs"));

        Assert.Contains("_audit", svc);
        Assert.Contains("\"payroll_slip\"", svc);
        Assert.Contains("\"severance\"", svc);

        // 만들기·확정·지급·취소가 모두 남아야 한다 — 하나만 빠져도 추적이 끊긴다.
        foreach (var action in new[] { "\"confirm\"", "\"pay\"", "\"cancel\"" })
        {
            Assert.Contains(action, svc);
        }
    }
}
