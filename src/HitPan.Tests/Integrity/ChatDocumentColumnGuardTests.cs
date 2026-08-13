using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 메신저 문서 연결 쿼리가 <b>실제로 있는 컬럼</b> 을 쓰는지 본다. 작(2026-08-13 봉합).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 이 시험이 생겼나</b> — 2026-08-13 코드리뷰가 잡은 결함:
/// </para>
/// <list type="bullet">
///   <item><c>expenses</c> 에 <c>status</c> 컬럼이 없다(실제 <c>approval_status</c>) → 경비 첨부 <b>500</b></item>
///   <item><c>hr_reports</c> 에 <c>report_date</c> 가 없다(실제 <c>period_start</c>) → 보고서 첨부 <b>500</b></item>
/// </list>
/// <para>
/// 🔴 <b>빌드 0/0 · 시험 441 · ddl-smoke 가 셋 다 통과했는데도 못 잡았다.</b>
/// 전부 코드를 <b>문자열로</b> 검사하는 시험이라, DB 에 실제로 쿼리를 날려본 적이 없었기 때문이다.
/// 고객이 화면을 열어야만 드러나는 자리다([[project_fixed_vs_delivered_gap]] 계열).
/// </para>
/// <para>
/// ⇒ 이 시험은 <b>쿼리에 적힌 컬럼 이름을 뽑아 출하 DDL 과 대조한다.</b>
/// 헌법 #13(새 SQL 작성 전 DESCRIBE 의무)을 사람 기억이 아니라 <b>게이트로</b> 만든다.
/// </para>
/// </remarks>
public sealed class ChatDocumentColumnGuardTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.True(dir is not null && Directory.Exists(Path.Combine(dir, "src")), "레포 루트를 찾아야 한다");
        return dir!;
    }

    private static string ReadSource(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    /// <summary>출하 DDL 에서 그 표의 컬럼 이름을 전부 뽑는다.</summary>
    private static HashSet<string> ColumnsOf(string table)
    {
        var ddl = ReadSource("installer", "hitpan_db_clean.sql");

        var start = ddl.IndexOf($"CREATE TABLE `{table}` (", StringComparison.Ordinal);
        Assert.True(start > 0, $"출하 DDL 에 {table} 이 있어야 한다");

        var end = ddl.IndexOf(") ENGINE=", start, StringComparison.Ordinal);
        Assert.True(end > start, $"{table} 정의가 끝나야 한다");

        var body = ddl[start..end];

        // 컬럼 줄은 백틱으로 시작한다. KEY·PRIMARY KEY 줄은 백틱이 뒤에 오므로 걸러진다.
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.TrimStart();
            var m = Regex.Match(trimmed, @"^`([a-z0-9_]+)`\s+[a-z]", RegexOptions.IgnoreCase);
            if (m.Success) columns.Add(m.Groups[1].Value);
        }

        Assert.True(columns.Count > 0, $"{table} 컬럼을 뽑아야 한다");
        return columns;
    }

    /// <summary>
    /// 🔴 문서 연결 쿼리가 쓰는 컬럼이 <b>전부 실제로 있어야</b> 한다.
    /// </summary>
    /// <remarks>
    /// 한 갈래라도 없는 컬럼을 쓰면 그 첨부 기능은 <b>100% 500</b> 이다 —
    /// "가끔 틀린다" 가 아니라 <b>한 번도 안 된다.</b>
    /// </remarks>
    [Theory]
    [InlineData("approval", "approval_documents", new[] { "approval_id", "title", "status", "requested_at", "requester_id", "tenant_id" })]
    [InlineData("leave", "leave_requests", new[] { "request_id", "start_date", "end_date", "status", "employee_id", "tenant_id" })]
    [InlineData("expense", "expenses", new[] { "expense_id", "description", "approval_status", "expense_date", "employee_id", "tenant_id" })]
    [InlineData("payroll", "payroll_slips", new[] { "slip_id", "pay_year", "pay_month", "status", "pay_date", "employee_id", "tenant_id" })]
    [InlineData("contract", "labor_contracts", new[] { "contract_id", "status", "start_date", "employee_id", "tenant_id" })]
    [InlineData("report", "hr_reports", new[] { "report_id", "title", "status", "submitted_at", "period_start", "employee_id", "tenant_id" })]
    public void 문서연결_쿼리가_쓰는_컬럼이_실제로_있다(string refType, string table, string[] usedColumns)
    {
        var actual = ColumnsOf(table);

        foreach (var column in usedColumns)
        {
            Assert.True(actual.Contains(column),
                $"'{refType}' 갈래가 {table}.{column} 을 쓰는데 그 컬럼이 없다. " +
                $"이 기능은 열자마자 500 이 난다(MariaDB 1054). " +
                $"실제 컬럼: {string.Join(", ", actual.OrderBy(x => x))}");
        }
    }

    /// <summary>
    /// 🔴 <b>없어진 이름이 되살아나지 않게</b> 막는다.
    /// </summary>
    [Fact]
    public void 없는_컬럼_이름을_다시_쓰지_않는다()
    {
        // 🔴 주석은 걷어낸다 — 봉합 기록에 옛 이름이 남아 있고, 그건 되살아난 게 아니다.
        var code = string.Join('\n',
            ReadSource("src", "HitPan.Application", "Services", "ChatService.cs")
                .Split('\n')
                .Where(l =>
                {
                    var t = l.TrimStart();
                    return !t.StartsWith("//", StringComparison.Ordinal)
                        && !t.StartsWith("///", StringComparison.Ordinal)
                        && !t.StartsWith("--", StringComparison.Ordinal);
                }));

        // 2026-08-13 에 실제로 500 을 냈던 두 이름.
        // 🔴 `approval_status` 는 정상이므로 앞에 다른 글자가 붙지 않은 `status` 만 잡는다.
        Assert.False(Regex.IsMatch(code, @"(?<![a-z_])status AS Status,\s*expense_date"),
            "expenses 에는 status 컬럼이 없다 — approval_status 를 써야 한다(1054, 경비 첨부 500).");

        Assert.False(Regex.IsMatch(code, @"(?<![a-z_])report_date(?![a-z_])"),
            "hr_reports 에는 report_date 컬럼이 없다 — period_start·submitted_at 을 써야 한다(1054, 보고서 첨부 500).");
    }

    /// <summary>
    /// 🔴 문서 목록은 <b>내 것만</b> 나와야 한다 — 이 목록이 권한 판정도 겸한다.
    /// </summary>
    /// <remarks>
    /// 경비 갈래에 <c>employee_id</c> 필터가 빠져 있어 <b>회사 전체 경비</b>가 나왔다.
    /// <c>ResolveDocTitleAsync</c> 가 이 목록으로 첨부 가능 여부를 판정하므로,
    /// 목록에 뜨면 <b>남의 경비를 대화방에 붙일 수 있었다.</b>
    /// </remarks>
    [Fact]
    public void 문서목록은_본인_것만_나온다()
    {
        var code = ReadSource("src", "HitPan.Application", "Services", "ChatService.cs");

        // 갈래마다 SELECT 첫 줄에 종류 이름이 리터럴로 박혀 있다 — 그걸 기준으로 자른다.
        // (정규식으로 switch 전체를 잡으면 주석·중첩 때문에 갈래를 놓친다.)
        string[] refTypes = { "approval", "leave", "expense", "payroll", "contract", "report" };

        foreach (var refType in refTypes)
        {
            var marker = $"SELECT '{refType}' AS RefType";
            var start = code.IndexOf(marker, StringComparison.Ordinal);

            Assert.True(start > 0, $"'{refType}' 갈래 쿼리를 찾아야 한다");

            // 그 갈래의 SQL 끝(문자열 리터럴 종료)까지.
            var end = code.IndexOf("\"\"\"", start, StringComparison.Ordinal);
            Assert.True(end > start, $"'{refType}' 갈래 SQL 이 끝나야 한다");

            var sql = code[start..end];

            // 결재는 requester_id(기안자 = 본인), 나머지는 employee_id 로 거른다.
            var scoped = sql.Contains("employee_id = @employeeId", StringComparison.Ordinal)
                      || sql.Contains("requester_id = @employeeId", StringComparison.Ordinal);

            Assert.True(scoped,
                $"'{refType}' 갈래에 본인 필터가 없다 — 남의 문서가 목록에 뜨고, " +
                "그러면 대화방에 붙일 수도 있다(이 목록이 권한 판정을 겸한다).");

            // 헌법 #2 — 테넌트 격리.
            Assert.True(sql.Contains("tenant_id = @tenantId", StringComparison.Ordinal),
                $"'{refType}' 갈래에 테넌트 조건이 없다(헌법 #2).");
        }
    }
}
