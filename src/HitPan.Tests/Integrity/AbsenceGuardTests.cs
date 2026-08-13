using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 그룹웨어 단계6 휴직 게이트. 작(2026-08-13).
/// </summary>
/// <remarks>
/// 🔴 이 시험들은 <b>실측으로 확인한 것</b>만 지킨다. 추측으로 짜지 않는다.
/// 단계4 검증 교훈: <i>"문자열 존재 확인은 배선 확인이 아니다."</i>
/// </remarks>
public sealed class AbsenceGuardTests
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
            + "(AllowUserVariables=true 라 예외도 안 난다). 단계4 P0-1 과 같은 병이다.");
    }

    // ───────────────────────────────────────────────────────────────
    // 왜 표를 나눴나 — 되돌리려는 시도를 막는다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>휴직을 휴가 표에 넣으면 안 된다.</b>
    /// </summary>
    /// <remarks>
    /// 실측 근거 두 가지다. 나중에 "하나로 합치자" 가 반드시 다시 나오기 때문에 시험으로 남긴다.
    /// <list type="number">
    ///   <item><c>leave_requests.leave_days</c> 는 <c>decimal(3,1)</c> = 최대 99.9일.
    ///         육아휴직 548일을 넣으면 MySQL 이 거부한다
    ///         (실측: <c>ERROR 1264 Out of range value for column 'leave_days'</c>).</item>
    ///   <item>휴가가 승인되면 <c>LeaveBalanceHelper</c> 가 <c>annual_leave_used</c> 를 더한다.
    ///         휴직을 휴가로 올리면 <b>연차 잔여가 마이너스</b>가 되어 복직 후 연차를 못 쓴다.</item>
    /// </list>
    /// </remarks>
    [Fact]
    public void 휴직은_연차_잔여를_건드리지_않는다()
    {
        var svc = ReadSource("src", "HitPan.Application", "Services", "AbsenceService.cs");
        var code = StripComments(svc);

        // 🔴 이 두 칸을 건드리는 순간 휴직이 연차를 깎는다.
        Assert.DoesNotContain("annual_leave_used", code);
        Assert.DoesNotContain("annual_leave_total", code);

        // 휴가 표에도 손대지 않는다 — 자릿수에서 안 들어가는 표다.
        Assert.DoesNotContain("INSERT INTO leave_requests", code);
        Assert.DoesNotContain("UPDATE leave_requests", code);
    }

    /// <summary>
    /// 🔴 휴직 표의 기간은 <b>날짜</b>로 잡는다. 일수 칸을 두면 다시 자릿수에 걸린다.
    /// </summary>
    [Fact]
    public void 휴직_기간은_날짜로_잡아_자릿수_제한이_없다()
    {
        var ddl = ReadSource("src", "HitPan.API", "Migrations", "SQL",
            "DB-98_employee_leave_of_absence.sql");

        Assert.Contains("`start_date`     date         NOT NULL", ddl);
        Assert.Contains("`end_date`       date         NOT NULL", ddl);

        // 일수를 숫자 칸으로 두면 leave_days(3,1) 과 같은 사고가 재발한다.
        // ⚠️ 주석에는 그 이유를 적어 뒀으므로 **컬럼 정의 줄만** 본다.
        //    (처음엔 파일 전체를 보게 짰다가 설명 주석에 걸려 실패했다)
        var columnLines = StripComments(ddl);
        Assert.DoesNotContain("`absence_days`", columnLines);
        Assert.DoesNotContain("`leave_days`", columnLines);
    }

    // ───────────────────────────────────────────────────────────────
    // 사장님 지시 — 수동, 그리고 막지 않기
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 사장님(2026-08-13): <i>"휴직도 수동으로!!!!"</i> / <i>"휴직일수"</i>.
    /// 서버가 기간을 <b>계산하지 않는다.</b> 사람이 넣은 날짜를 그대로 저장한다.
    /// </summary>
    [Fact]
    public void 휴직_기간은_사람이_넣은_그대로_저장된다()
    {
        var svc = ReadSource("src", "HitPan.Application", "Services", "AbsenceService.cs");
        var code = StripComments(svc);

        // 요청의 날짜를 그대로 넘긴다(.Date 로 시각만 떼는 것은 허용).
        Assert.Contains("StartDate = request.StartDate.Date", code);
        Assert.Contains("EndDate = request.EndDate.Date", code);

        // 🔴 서버가 종료일을 만들어내면 안 된다.
        //    AddMonths/AddDays 로 기간을 산출하는 순간 '수동' 이 아니게 된다.
        Assert.DoesNotContain("request.StartDate.AddMonths", code);
        Assert.DoesNotContain("request.StartDate.AddDays", code);
        Assert.DoesNotContain("request.StartDate.AddYears", code);
    }

    /// <summary>
    /// 🔴 <b>기준을 두지 않는다.</b> 사장님(2026-08-13):
    /// <i>"복잡하게 생각할것 없이 그냥 휴직은 상태처리, 상태확인 정도로만 써도 될듯."</i>
    /// </summary>
    /// <remarks>
    /// 처음엔 <c>labor_policy_settings</c> 에서 육아휴직 18개월 등을 읽어 초과를 경고하는
    /// 구조로 짰다가 <b>전부 걷어냈다.</b> 사장님 지시대로면 비교할 기준 자체가 필요 없고,
    /// 기준을 두면 법이 바뀔 때마다 그 기준을 관리해야 한다 — 그게 피하려던 복잡함이다.
    ///
    /// ⚠️ 이 시험은 <b>되돌아가는 것을 막는다.</b> "육아휴직 18개월 넘으면 경고해주면 좋잖아" 가
    /// 반드시 다시 나오는데, 그건 사장님이 명시적으로 걷어내라고 한 것이다.
    /// </remarks>
    [Fact]
    public void 휴직은_법정_기준과_비교하지_않는다()
    {
        var svc = ReadSource("src", "HitPan.Application", "Services", "AbsenceService.cs");
        var code = StripComments(svc);

        // 🔴 기준값 표를 읽지 않는다.
        Assert.DoesNotContain("labor_policy_settings", code);

        // 법정 숫자가 코드에 있으면 안 된다(육아휴직 18개월·출산휴가 90일 등).
        foreach (var literal in new[] { "18m", "90m", "120m", "30.4375" })
        {
            Assert.DoesNotContain(literal, code);
        }

        // 초과 판정 자체가 없어야 한다.
        Assert.DoesNotContain("Exceeds", code);
        Assert.DoesNotContain("exceeds_standard", code);
    }

    /// <summary>
    /// 🔴 <b>급여는 금액을 직접 받는다.</b> 계산하지 않는다.
    /// </summary>
    /// <remarks>
    /// 사장님(2026-08-13): <i>"급여는 자동계산하지 말고 수동으로 int값 직접 받아서 입력하는게
    /// 가장 깔끔함"</i> / <i>"각 고객사 니즈나 사정도 부합시킬 수 있고."</i>
    ///
    /// 그 말이 맞다. 회사마다 육아휴직에 얹어주는 곳·무급인 곳·몇 달만 주는 곳이 다 다른데,
    /// 자동 계산으로는 못 맞춘다. 금액을 직접 받으면 어떤 회사든 그대로 된다.
    /// </remarks>
    [Fact]
    public void 휴직_급여는_금액을_직접_받는다()
    {
        var svc = ReadSource("src", "HitPan.Application", "Services", "AbsenceService.cs");

        // 넣은 금액이 저장되는 자리가 INSERT·UPDATE 양쪽에 있어야 한다.
        // 🔴 문자열이 있는지가 아니라 **파라미터 객체에 짝이 있는지** 본다(단계4 P0-1 교훈).
        AssertDapperParametersBound(svc, "@PayAmount", 2);
        AssertDapperParametersBound(svc, "@PayNote", 2);

        var code = StripComments(svc);

        // 🔴 금액을 우리가 만들어내면 안 된다. 비율·일할 계산이 있으면 그 순간 '수동' 이 아니다.
        Assert.DoesNotMatch(new Regex(@"PayAmount\s*[*/]"), code);
        Assert.DoesNotContain("일할", code);

        // 금액은 decimal 이다(헌법 #4 — 금액에 float/double 금지).
        var dto = ReadSource("src", "HitPan.Application", "DTOs", "Leave", "AbsenceDtos.cs");
        Assert.Contains("public decimal PayAmount", dto);
        Assert.DoesNotContain("double PayAmount", dto);
        Assert.DoesNotContain("float PayAmount", dto);
    }

    /// <summary>
    /// 🔴 급여·회계가 <b>가져갈 자리</b>가 있어야 한다.
    /// </summary>
    /// <remarks>
    /// 사장님: <i>"그러면 자연스럽게 급여, 회계이슈도 해결될듯"</i>
    /// 해결되려면 급여가 <b>실제로 가져갈 수 있어야</b> 한다. 금액만 저장하고 꺼낼 길이 없으면
    /// 단계8 에서 다시 만들어야 하고, 그때 이 자리가 안 이어진다(헌법 #20 — 흐름은 안 끊긴다).
    /// </remarks>
    [Fact]
    public void 급여가_휴직_금액을_가져갈_길이_있다()
    {
        var svc = ReadSource("src", "HitPan.Application", "Services", "AbsenceService.cs");
        var iface = ReadSource("src", "HitPan.Application", "Interfaces", "IAbsenceService.cs");
        var ctrl = ReadSource("src", "HitPan.API", "Controllers", "AbsenceController.cs");

        Assert.Contains("GetPayForMonthAsync", iface);
        Assert.Contains("GetPayForMonthAsync", svc);
        Assert.Contains("[HttpGet(\"pay\")]", ctrl);

        var code = StripComments(svc);

        // 그 달에 걸치는 휴직을 잡아야 한다 — 달 중간에 시작/끝나는 건이 흔하다.
        Assert.Contains("a.start_date <= @Last", code);
        Assert.Contains("COALESCE(a.actual_return_date, a.end_date) >= @First", code);

        // 급여 자료라 관리자만 본다.
        Assert.Matches(new Regex(
            @"\[Authorize\(Policy = ""TenantAdminOnly""\)\]\s*\r?\n\s*public\s+async\s+Task<IActionResult>\s+Pay\b"),
            StripComments(ctrl));
    }

    /// <summary>
    /// 🔴 직원 상태가 <b>재직·휴직·연차</b> 세 가지로 갈린다.
    /// 사장님(2026-08-13): <i>"상태처리 : 재직 휴직 연차"</i>
    /// </summary>
    /// <remarks>
    /// 실측: 종전엔 <c>employees</c> 에 상태 칸이 없어 <b>재직 아니면 퇴사</b> 둘뿐이었다.
    /// 휴직자를 <c>is_active=0</c> 으로 두면 퇴사자와 구분이 안 되고,
    /// <c>is_active=1</c> 로 두면 급여·연차에 그대로 들어간다. <b>어느 쪽도 사고다.</b>
    /// </remarks>
    [Fact]
    public void 직원_상태가_재직_휴직_연차로_갈린다()
    {
        var ddl = ReadSource("src", "HitPan.API", "Migrations", "SQL",
            "DB-99_employee_work_status.sql");

        Assert.Contains("ADD COLUMN IF NOT EXISTS `work_status`", ddl);

        var dto = ReadSource("src", "HitPan.Application", "DTOs", "Leave", "AbsenceDtos.cs");
        foreach (var (code, label) in new[]
                 {
                     ("active", "재직"), ("absence", "휴직"), ("leave", "연차"),
                 })
        {
            Assert.Contains($"[{(code == "active" ? "Active" : code == "absence" ? "Absence" : "Leave")}] = \"{label}\"", dto);
        }

        var svc = ReadSource("src", "HitPan.Application", "Services", "AbsenceService.cs");
        var body = StripComments(svc);

        // 승인·복직이 사원 상태를 바꿔야 한다. 안 바꾸면 휴직중인데 재직으로 보인다.
        Assert.Contains("UPDATE employees", body);
        Assert.Contains("SET work_status = @WorkStatus", body);

        // 🔴 is_active 를 건드리면 안 된다 — 그 순간 퇴사자와 구분이 사라지고
        //    기존 화면·쿼리에서 사원이 통째로 사라진다.
        Assert.DoesNotMatch(new Regex(@"SET\s+is_active\s*="), body);
        Assert.DoesNotMatch(new Regex(@"is_active\s*=\s*0"), body);
    }

    // ───────────────────────────────────────────────────────────────
    // 앞 단계에서 났던 P0 가 재발하지 않는가
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 단계3 P0-1 재발 방지 — 결재가 안 올라갔는데 "올렸습니다" 를 띄우면 안 된다.
    /// </summary>
    /// <remarks>
    /// 그때는 결재 설정이 꺼져 있으면 <c>TryCreateApprovalAsync</c> 가 조용히 아무것도 안 했고,
    /// 화면은 성공을 띄웠고, 문서는 <c>pending</c> 에 갇혔고, 결재함엔 안 떴다.
    /// </remarks>
    [Fact]
    public void 결재가_올라갔는지_세어보고_사실대로_돌려준다()
    {
        var svc = ReadSource("src", "HitPan.Application", "Services", "AbsenceService.cs");
        var code = StripComments(svc);

        // 올릴 수 있는 상태인지 먼저 묻고
        Assert.Contains("DescribeApprovalBlockerAsync", code);

        // 🔴 올린 뒤 **실제로 만들어졌는지 센다.** 믿지 않는다.
        Assert.Contains("SELECT COUNT(*) FROM approval_documents", code);
        Assert.Contains("return (false, \"결재 문서가 만들어지지 않았습니다.", code);

        // 결과에 사실이 담겨야 한다.
        var dto = ReadSource("src", "HitPan.Application", "DTOs", "Leave", "AbsenceDtos.cs");
        Assert.Contains("public bool ApprovalCreated", dto);
        Assert.Contains("public string? ApprovalSkipReason", dto);

        // 화면이 그 사실을 그대로 보여줘야 한다 — 서버만 정직하면 소용없다.
        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "AbsencePage.razor");
        Assert.Contains("result?.ApprovalCreated != true", page);
        Assert.Contains("ApprovalSkipReason", page);
    }

    /// <summary>
    /// 🔴 단계3 P0-2 재발 방지 — 긴 반려 사유가 <c>ERROR 1406</c> 으로 트랜잭션을 되감으면 안 된다.
    /// </summary>
    [Fact]
    public void 반려_사유가_길어도_터지지_않는다()
    {
        var svc = ReadSource("src", "HitPan.Application", "Services", "AbsenceService.cs");
        var code = StripComments(svc);

        Assert.Contains("TruncateRejectReason", code);
        // 한글·이모지를 string.Length 로 세면 저장 폭과 어긋난다.
        Assert.Contains("StringInfo", code);
        Assert.Contains("LengthInTextElements", code);

        // 잘라내는 폭과 DDL 의 칸 폭이 같아야 한다.
        var ddl = ReadSource("src", "HitPan.API", "Migrations", "SQL",
            "DB-98_employee_leave_of_absence.sql");
        Assert.Contains("`reject_reason`  varchar(500)", ddl);
        Assert.Contains("RejectReasonMaxLength = 500", code);
    }

    /// <summary>
    /// 🔴 실측으로 잡은 것 — 관리자 판정 근거가 <c>TenantAdminOnly</c> 정책과 같아야 한다.
    /// </summary>
    /// <remarks>
    /// 처음에 <c>role</c> 클레임을 봤다가 실측에서 잡았다. 정책은
    /// <c>Program.cs</c> 에서 <c>account_type</c> 을 본다. 그대로 뒀으면 같은 관리자가
    /// <b>[승인] 은 되는데 [전원 목록] 은 본인 것만 보이는</b> 어긋남이 났다.
    /// </remarks>
    [Fact]
    public void 관리자_판정이_정책과_같은_클레임을_본다()
    {
        var ctrl = ReadSource("src", "HitPan.API", "Controllers", "AbsenceController.cs");
        var code = StripComments(ctrl);

        Assert.Contains("User.HasClaim(\"account_type\", \"tenant_admin\")", code);

        // 🔴 다른 클레임을 보면 정책과 갈라진다.
        Assert.DoesNotContain("FindFirstValue(\"role\")", code);
        Assert.DoesNotContain("User.IsInRole", code);

        // 정책 쪽도 같은 클레임인지 확인한다(둘 중 하나가 바뀌면 잡힌다).
        var program = ReadSource("src", "HitPan.API", "Program.cs");
        Assert.Contains("HasClaim(\"account_type\", \"tenant_admin\")", program);
    }

    /// <summary>
    /// 🔴 일반 직원이 남의 휴직을 보거나 남의 이름으로 신청하면 안 된다.
    /// 화면이 보내온 값을 믿지 않고 <b>서버가 덮는다.</b>
    /// </summary>
    [Fact]
    public void 일반_직원은_본인_것만_보고_본인_것만_넣는다()
    {
        var ctrl = ReadSource("src", "HitPan.API", "Controllers", "AbsenceController.cs");
        var code = StripComments(ctrl);

        // 목록·점검에서 관리자가 아니면 본인 id 로 덮는다.
        Assert.Contains("var scoped = IsAdmin() ? employeeId : CurrentEmployeeId();", code);

        // 저장에서도 남의 이름으로 못 넣는다.
        Assert.Contains("request.EmployeeId = me;", code);

        // 단건 조회에서 남의 사유가 보이면 안 된다.
        Assert.Contains("if (!IsAdmin() && dto.EmployeeId != CurrentEmployeeId()) return Forbid();", code);

        // 승인·반려·복직은 관리자 전용이어야 한다.
        foreach (var action in new[] { "Approve", "Reject", "Return", "Sync" })
        {
            var m = Regex.Match(code,
                @"\[Authorize\(Policy = ""TenantAdminOnly""\)\]\s*\r?\n\s*public\s+async\s+Task<IActionResult>\s+"
                + action + @"\b");
            Assert.True(m.Success, $"🔴 {action} 은 관리자 전용이어야 한다(TenantAdminOnly).");
        }
    }

    // ───────────────────────────────────────────────────────────────
    // 신규 고객사에 실제로 가는가 — 8/12 사고의 자리
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>신규 고객사에도 기준값이 깔려야 한다.</b> 실측으로 잡은 P0 다.
    /// </summary>
    /// <remarks>
    /// 마이그의 시드는 <c>SELECT DISTINCT tenant_id FROM employees</c> 로 회사를 고른다.
    /// 그런데 <b>신규 설치는 그 시점에 직원이 0명</b>이라 어느 회사에도 안 깔린다.
    /// 직급(DB-93)이 같은 이유로 안 깔렸던 것과 같은 자리다.
    ///
    /// 그대로 뒀으면 신규 고객사에서 연차가 <b>0일</b>로 나오고(폴백을 안 두므로),
    /// 휴직은 "기준이 정해져 있지 않습니다" 만 떴다.
    /// </remarks>
    [Fact]
    public void 신규_고객사에도_노무_기준값이_깔린다()
    {
        var prov = ReadSource("src", "HitPan.API", "Services", "CompanyBootstrapProvisioner.cs");
        var code = StripComments(prov);

        Assert.Contains("INSERT INTO labor_policy_settings", code);

        // 연차·휴직 양쪽 열쇠가 다 있어야 한다. 하나라도 빠지면 그 화면이 죽는다.
        foreach (var key in new[]
                 {
                     "annual_leave_base_days", "annual_leave_max_days",
                     "monthly_leave_days_under_1y", "small_business_threshold",
                     "short_time_weekly_hours",
                     "childcare_leave_max_months", "maternity_leave_days",
                     "family_care_leave_max_days",
                 })
        {
            Assert.Contains(key, code);
        }

        // 이미 있으면 덮지 않는다(헌법 #1 · #11 — 고객이 고친 값을 되돌리면 안 된다).
        Assert.Contains("WHERE NOT EXISTS", code);
    }

    /// <summary>
    /// 🔴 마이그레이션이 <b>고객에게 가는 자리</b>에 있어야 한다.
    /// 8/12 사고: <c>installer/migrations/</c> 에 뒀더니 배포본에 안 실려 화면이 죽었다.
    /// </summary>
    [Fact]
    public void 휴직_마이그가_고객에게_가는_자리에_있다()
    {
        var path = Path.Combine(RepoRoot(), "src", "HitPan.API", "Migrations", "SQL",
            "DB-98_employee_leave_of_absence.sql");

        Assert.True(File.Exists(path),
            "🔴 마이그는 src/HitPan.API/Migrations/SQL/ 에 있어야 고객에게 간다. "
            + "다른 자리에 두면 빌드·시험·워크플로가 전부 통과하고도 고객 화면이 죽는다(8/12 실제 사고).");
    }

    /// <summary>
    /// 🔴 새 표는 <c>ENGINE=InnoDB</c> 를 명시한다(헌법 #17). 테넌트 칸도 있어야 한다(헌법 #2).
    /// </summary>
    [Fact]
    public void 휴직표가_헌법을_지킨다()
    {
        var ddl = ReadSource("src", "HitPan.API", "Migrations", "SQL",
            "DB-98_employee_leave_of_absence.sql");

        Assert.Contains("ENGINE=InnoDB", ddl);
        Assert.Contains("utf8mb4_unicode_ci", ddl);
        Assert.Contains("`tenant_id`      varchar(36)  NOT NULL", ddl);

        // 멱등이어야 한다 — 두 번 돌아도 같아야 한다.
        Assert.Contains("CREATE TABLE IF NOT EXISTS", ddl);
        Assert.Contains("WHERE NOT EXISTS", ddl);
    }

    /// <summary>
    /// 🔴 테넌트는 JWT 에서만 온다(헌법 #2). 파라미터로 받으면 즉시 반려다.
    /// </summary>
    [Fact]
    public void 테넌트를_파라미터로_받지_않는다()
    {
        var ctrl = ReadSource("src", "HitPan.API", "Controllers", "AbsenceController.cs");
        var code = StripComments(ctrl);

        Assert.DoesNotContain("[FromQuery] string tenantId", code);
        Assert.DoesNotContain("[FromBody] string tenantId", code);
        Assert.DoesNotContain("[FromRoute] string tenantId", code);

        // 전부 HttpContext 에서 꺼낸다.
        Assert.Contains("HttpContext.Items[\"TenantId\"]", code);
    }

    // ───────────────────────────────────────────────────────────────
    // 화면·연결
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 만들어 놓고 메뉴에 없으면 없는 것과 같다(단계1 교훈 — 숨은 화면 6개).
    /// </summary>
    [Fact]
    public void 휴직_화면이_메뉴에_올라와_있다()
    {
        var sidebar = ReadSource("src", "HitPan.Web", "Layout", "Sidebar.razor");
        Assert.Contains("/hr/absence", sidebar);

        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "AbsencePage.razor");
        Assert.Contains("@page \"/hr/absence\"", page);

        // DI 등록이 없으면 화면이 열리다 죽는다.
        var webProgram = ReadSource("src", "HitPan.Web", "Program.cs");
        Assert.Contains("AddScoped<AbsenceService>()", webProgram);

        var apiProgram = ReadSource("src", "HitPan.API", "Program.cs");
        Assert.Contains("AddScoped<IAbsenceService, AbsenceService>()", apiProgram);
    }

    /// <summary>
    /// 🔴 양쪽 이름표가 같아야 한다. Web 은 Application 을 참조하지 않아 <b>두 벌</b>이 있다.
    /// 한쪽만 고치면 화면과 저장값이 어긋난다.
    /// </summary>
    [Fact]
    public void 상태_코드가_양쪽에서_같다()
    {
        var appDto = ReadSource("src", "HitPan.Application", "DTOs", "Leave", "AbsenceDtos.cs");
        var webModel = ReadSource("src", "HitPan.Web", "Models", "AbsenceModels.cs");

        // 진행 단계
        foreach (var status in new[]
                 {
                     "draft", "pending", "approved", "active", "returned", "rejected", "cancelled",
                 })
        {
            Assert.Contains($"\"{status}\"", appDto);
            Assert.Contains($"[\"{status}\"]", webModel);
        }

        // 직원 상태(재직·휴직·연차)
        foreach (var ws in new[] { "active", "absence", "leave" })
        {
            Assert.Contains($"\"{ws}\"", appDto);
            Assert.Contains($"[\"{ws}\"]", webModel);
        }
    }

    /// <summary>
    /// 🔴 휴직 <b>종류를 고르게 하지 않는다.</b>
    /// 사장님(2026-08-13): <i>"비고에 육아, 연수, 교육, 등 자유롭게 쓰면 됨"</i>
    /// </summary>
    /// <remarks>
    /// 회사마다 부르는 이름이 다르고, 목록에 없는 사유가 늘 생긴다.
    /// 목록을 두면 "기타" 로 몰리고, 그러면 목록이 있으나 마나다.
    /// ⚠️ 이 시험은 <b>되돌아가는 것을 막는다</b> — 종류 드롭다운은 늘 다시 제안된다.
    /// </remarks>
    [Fact]
    public void 휴직_사유는_비고에_자유롭게_쓴다()
    {
        var appDto = ReadSource("src", "HitPan.Application", "DTOs", "Leave", "AbsenceDtos.cs");
        var webModel = ReadSource("src", "HitPan.Web", "Models", "AbsenceModels.cs");
        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "AbsencePage.razor");

        // 🔴 종류 목록이 있으면 안 된다.
        Assert.DoesNotContain("AbsenceTypeLabels", appDto);
        Assert.DoesNotContain("AbsenceTypeLabels", webModel);
        Assert.DoesNotContain("AbsenceTypeLabels", page);

        foreach (var t in new[] { "childcare", "maternity", "family_care" })
        {
            Assert.DoesNotContain(t, appDto);
            Assert.DoesNotContain(t, webModel);
        }

        // 비고는 자유 입력이어야 한다.
        Assert.Contains("public string? Reason", appDto);
        Assert.Contains("Label=\"비고\"", page);
    }

    /// <summary>
    /// 🔴 모르는 코드값을 '기타' 로 뭉개면 안 된다. 잘못 들어간 값이 화면에서 정상으로 보인다.
    /// (단계4 <c>ParseEmpType</c> 이 조용히 Regular 로 떨어지던 것과 같은 병)
    /// </summary>
    [Fact]
    public void 모르는_코드값은_그대로_보여준다()
    {
        var appDto = ReadSource("src", "HitPan.Application", "DTOs", "Leave", "AbsenceDtos.cs");
        var webModel = ReadSource("src", "HitPan.Web", "Models", "AbsenceModels.cs");

        // TryGetValue 로 찾고, 못 찾으면 **받은 값 그대로** 돌려준다.
        Assert.Contains("Map.TryGetValue(code, out var v) ? v : code", appDto);
        Assert.Contains("Map.TryGetValue(code, out var v) ? v : code", webModel);
    }

    /// <summary>
    /// 🔴 복직은 <b>자동으로 하지 않는다.</b> 종료일이 지났다고 복직 처리하면
    /// 아직 안 나온 사람이 재직자로 잡혀 급여가 나간다. 연장도 흔하다.
    /// </summary>
    [Fact]
    public void 복직은_사람이_처리한다()
    {
        var svc = ReadSource("src", "HitPan.Application", "Services", "AbsenceService.cs");
        var code = StripComments(svc);

        // 상태 맞추기는 approved→active 만 한다.
        var sync = Regex.Match(code,
            @"SET status = 'active'.*?WHERE tenant_id = @TenantId.*?AND status = 'approved'",
            RegexOptions.Singleline);
        Assert.True(sync.Success, "상태 맞추기는 승인→휴직중만 해야 한다.");

        // 🔴 자동으로 'returned' 로 바꾸는 자리가 있으면 안 된다.
        Assert.DoesNotMatch(new Regex(
            @"SET status = 'returned'[^;]*end_date\s*<\s*CURDATE\(\)", RegexOptions.Singleline), code);

        // 실제 복직일은 요청에서 받는다.
        AssertDapperParametersBound(svc, "@ReturnDate", 1);
        Assert.Contains("request.ActualReturnDate", code);
    }

    /// <summary>
    /// 🔴 기간이 겹치는 휴직은 막는다. 이것은 '기준 초과' 와 성격이 다르다 —
    /// 겹친 휴직은 회사가 더 주는 것이 아니라 <b>데이터가 모순된 것</b>이다.
    /// </summary>
    [Fact]
    public void 기간이_겹치는_휴직은_막는다()
    {
        var svc = ReadSource("src", "HitPan.Application", "Services", "AbsenceService.cs");
        var code = StripComments(svc);

        Assert.Contains("start_date <= @End", code);
        Assert.Contains("end_date   >= @Start", code);
        Assert.Contains("이미 등록된 휴직 기간", code);

        // 자기 자신은 겹침에서 빼야 수정이 된다.
        Assert.Contains("@AbsenceId IS NULL OR absence_id <> @AbsenceId", code);
    }

    /// <summary>
    /// 🔴 빈 catch 금지(헌법 #15). 삼킨 예외는 없는 일이 된다.
    /// </summary>
    [Fact]
    public void 예외를_조용히_삼키지_않는다()
    {
        foreach (var (parts, _) in new[]
                 {
                     (new[] { "src", "HitPan.Application", "Services", "AbsenceService.cs" }, ""),
                     (new[] { "src", "HitPan.API", "Controllers", "AbsenceController.cs" }, ""),
                     (new[] { "src", "HitPan.Web", "Services", "AbsenceService.cs" }, ""),
                 })
        {
            var src = ReadSource(parts);

            // catch { } / catch (…) { } 처럼 본문이 빈 것.
            var empty = Regex.Matches(src, @"catch\s*(\([^)]*\))?\s*\{\s*\}");
            Assert.True(empty.Count == 0,
                $"🔴 {string.Join('/', parts)} 에 빈 catch 가 {empty.Count}개 있다(헌법 #15).");
        }
    }

    /// <summary>
    /// 🔴 화면이 <b>실패와 0건을 구분</b>해야 한다.
    /// 실패를 빈 목록으로 뭉개면 "휴직자가 없다" 로 보인다.
    /// </summary>
    [Fact]
    public void 화면이_실패와_영건을_구분한다()
    {
        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "AbsencePage.razor");

        Assert.Contains("else if (_items is null)", page);
        Assert.Contains("불러오지 못했습니다", page);
        Assert.Contains("else if (_items.Count == 0)", page);
        Assert.Contains("휴직 내역이 없습니다", page);

        // 클라이언트가 실패를 null 로 돌려야 화면이 구분할 수 있다.
        var svc = ReadSource("src", "HitPan.Web", "Services", "AbsenceService.cs");
        Assert.Contains("public async Task<List<AbsenceModel>?> GetListAsync", svc);
    }
}
