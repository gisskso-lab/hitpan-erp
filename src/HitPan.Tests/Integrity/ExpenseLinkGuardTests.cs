using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 그룹웨어 단계7 경비 연결 게이트. 작(2026-08-13).
/// </summary>
/// <remarks>
/// 🔴 <b>사장님이 정한 범위</b>(2026-08-13):
/// <i>"연결만 해두고 비워놔. 그리고 수불부(원장)는 따로 점검해야되 그때, 경비처리하고 원장을 연결하면 될듯."</i>
///
/// ⇒ <b>연결은 한다. 기표(분개)는 안 한다.</b> 이 두 가지를 시험이 각각 지킨다.
/// </remarks>
public sealed class ExpenseLinkGuardTests
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
    /// 🔴 <b>기표하지 않는다.</b> 사장님: <i>"연결만 해두고 비워놔."</i>
    /// </summary>
    /// <remarks>
    /// 왜 지금 안 하나 — 실측: <c>AutoJournalHelper</c> 는 판매·매입·BOM 을 <b>이미 기표</b>하고
    /// <b>경비만 빠져 있다</b>. 그래서 "경비도 기표하자" 가 자연스러워 보이는데,
    /// 경비 항목별 <b>차변 계정과목</b>을 회계 전체를 보고 정해야 한다.
    /// 경비만 먼저 원장에 올리면 <b>장부가 어긋난다.</b>
    ///
    /// ⚠️ 이 시험은 <b>앞서 나가는 것을 막는다.</b> 원장 전수점검 때 사장님 결재를 받고 붙인다.
    /// </remarks>
    [Fact]
    public void 경비는_아직_기표하지_않는다()
    {
        var hr = StripComments(ReadSource("src", "HitPan.Application", "Services", "HrService.cs"));

        // 🔴 원장·분개에 손대면 안 된다.
        Assert.DoesNotContain("journal_entries", hr);
        Assert.DoesNotContain("journal_lines", hr);
        Assert.DoesNotContain("AutoJournalHelper", hr);
        Assert.DoesNotContain("stock_ledger", hr);
    }

    /// <summary>
    /// 🔴 <b>결재에 올라간다.</b> 종전엔 이 자리가 통째로 없었다.
    /// </summary>
    /// <remarks>
    /// 실측으로 잡았다 — 회계 쪽 <c>FinanceService.CreateExpenseAsync</c> 와 나란히 놓으니
    /// 그룹웨어 쪽 <c>CreateHrExpenseAsync</c> 에 결재 트리거가 <b>없었다</b>.
    /// 직원이 경비를 올려도 결재함에 안 뜨고 <c>pending</c> 에 갇힌다.
    /// </remarks>
    [Fact]
    public void 경비_신청이_결재에_올라간다()
    {
        var hr = ReadSource("src", "HitPan.Application", "Services", "HrService.cs");
        var code = StripComments(hr);

        // 🔴 문서유형이 회계 경비와 **같아야** 한다. 갈라지면 관리자가 결재선을 두 번 짜야 하고,
        //    하나만 짜면 나머지가 조용히 안 돈다.
        //
        // ⚠️ 처음엔 Assert.Contains("\"expense\"", code) 로만 봤다가 **훼손 실험에서 헛통과**했다.
        //    문서유형을 "hr_expense_SEPARATE" 로 바꿔도 파일 어딘가에 "expense" 가 있어 통과한 것이다.
        //    단계4 P0-1 과 같은 병이다 — <b>문자열 존재 확인은 배선 확인이 아니다.</b>
        //    ⇒ 호출문의 **첫 인자**를 직접 본다.
        var call = Regex.Match(code,
            @"TryCreateApprovalAsync\s*\(\s*_db\s*,\s*""(?<docType>[^""]+)""");

        Assert.True(call.Success,
            "🔴 경비 신청이 결재를 올리지 않는다. TryCreateApprovalAsync 호출이 없다 — "
            + "직원이 올려도 결재함에 안 뜨고 pending 에 갇힌다.");

        Assert.True(call.Groups["docType"].Value == "expense",
            $"🔴 결재 문서유형이 '{call.Groups["docType"].Value}' 다. 회계 경비와 같은 'expense' 여야 한다. "
            + "갈라지면 관리자가 결재선을 두 번 짜야 하고, 하나만 짜면 나머지가 조용히 안 돈다.");

        // 회계 쪽도 같은 문서유형을 쓰는지 본다(둘 중 하나가 바뀌면 잡힌다).
        var finance = StripComments(
            ReadSource("src", "HitPan.Application", "Services", "FinanceService.cs"));
        var financeCall = Regex.Match(finance,
            @"TryCreateApprovalAsync\s*\(\s*_db\s*,\s*\r?\n?\s*""(?<docType>[^""]+)""");

        Assert.True(financeCall.Success && financeCall.Groups["docType"].Value == "expense",
            "🔴 회계 경비의 결재 문서유형이 'expense' 가 아니다. 그룹웨어와 갈라졌다.");
    }

    /// <summary>
    /// 🔴 단계3 P0-1 재발 방지 — 결재가 안 올라갔는데 "신청 완료" 를 띄우면 안 된다.
    /// </summary>
    /// <remarks>
    /// <c>TryCreateApprovalAsync</c> 는 결재 설정이 꺼져 있거나 결재선이 없으면
    /// <b>조용히 아무것도 안 한다</b>. 실측: 지금 <c>approval_settings</c> 는 <b>0행</b>이라
    /// 대부분의 회사가 이 상태다. 그래서 <b>세어 보고 사실대로 돌려준다.</b>
    /// </remarks>
    [Fact]
    public void 결재가_올라갔는지_세어보고_사실대로_알린다()
    {
        var hr = ReadSource("src", "HitPan.Application", "Services", "HrService.cs");
        var code = StripComments(hr);

        // 서버가 센다.
        Assert.Contains("SELECT COUNT(*) FROM approval_documents", code);
        Assert.Contains("DescribeApprovalBlockerAsync", code);

        // 인터페이스에 열려 있어야 컨트롤러가 쓴다.
        var iface = ReadSource("src", "HitPan.Application", "Interfaces", "IHrService.cs");
        Assert.Contains("CheckHrExpenseApprovalAsync", iface);

        // 컨트롤러가 그 사실을 응답에 담아야 한다.
        var ctrl = StripComments(ReadSource("src", "HitPan.API", "Controllers", "HrController.cs"));
        Assert.Contains("CheckHrExpenseApprovalAsync", ctrl);
        Assert.Contains("approvalCreated", ctrl);
        Assert.Contains("approvalSkipReason", ctrl);

        // 🔴 화면이 그 사실을 보여줘야 한다 — 서버만 정직하면 소용없다.
        var page = ReadSource("src", "HitPan.Web", "Pages", "HR", "HrExpenseRequestPage.razor");
        Assert.Contains("ApprovalCreated", page);
        Assert.Contains("ApprovalSkipReason", page);

        // 종전의 "무조건 성공" 문구가 남아 있으면 안 된다.
        Assert.DoesNotContain("Snackbar.Add(\"신청 완료\"", page);
    }

    /// <summary>
    /// 🔴 마감한 달에는 경비가 못 들어간다. 회계 쪽과 <b>같은 규칙</b>을 쓴다.
    /// </summary>
    /// <remarks>
    /// 한쪽만 막으면 경리는 못 넣는데 직원은 넣어지는 어긋남이 난다 —
    /// 그러면 마감한 달의 결산이 뒤집힌다.
    /// </remarks>
    [Fact]
    public void 마감한_달에는_경비를_못_넣는다()
    {
        var hr = StripComments(ReadSource("src", "HitPan.Application", "Services", "HrService.cs"));
        var finance = StripComments(
            ReadSource("src", "HitPan.Application", "Services", "FinanceService.cs"));

        // 양쪽 다 같은 게이트를 불러야 한다.
        Assert.Contains("EnsureNotClosedAsync", hr);
        Assert.Contains("EnsureNotClosedAsync", finance);

        // 컨트롤러가 그 이유를 사용자에게 전해야 한다("마감된 기간입니다").
        var ctrl = StripComments(ReadSource("src", "HitPan.API", "Controllers", "HrController.cs"));
        Assert.Contains("catch (InvalidOperationException ex)", ctrl);
        Assert.Contains("BadRequest(new { message = ex.Message })", ctrl);
    }

    /// <summary>
    /// 🔴 감사로그를 남긴다. 돈이 오가는 신청이다.
    /// </summary>
    /// <remarks>
    /// 회계 쪽은 남기고 있었는데 그룹웨어 쪽은 안 남겼다 — 같은 경비인데 기준이 갈려 있었다.
    /// </remarks>
    [Fact]
    public void 경비_신청에_감사로그가_남는다()
    {
        var hr = StripComments(ReadSource("src", "HitPan.Application", "Services", "HrService.cs"));

        Assert.Contains("_audit", hr);
        Assert.Contains("LogAsync(\"create\", \"hr_expense\"", hr);
    }

    /// <summary>
    /// 🔴 경리가 직원 신청분을 <b>볼 수 있어야</b> 한다(헌법 #20 — 흐름은 안 끊긴다).
    /// </summary>
    /// <remarks>
    /// 실측: 표가 둘로 갈려 있다 —
    /// <c>expenses</c> 27,639행(회계 정본) / <c>hr_expense_requests</c> 0행(그룹웨어).
    /// <c>FinanceService</c> 가 <c>UNION ALL</c> 로 붙여 경리 화면에 함께 나온다.
    /// ⚠️ 이 <c>UNION</c> 을 지우면 <b>직원이 올린 경비가 경리에게 안 보인다.</b>
    /// </remarks>
    [Fact]
    public void 경리_화면이_직원_신청분도_본다()
    {
        var finance = StripComments(
            ReadSource("src", "HitPan.Application", "Services", "FinanceService.cs"));

        Assert.Contains("FROM expenses", finance);
        Assert.Contains("FROM hr_expense_requests", finance);
        Assert.Contains("UNION ALL", finance);
    }

    /// <summary>
    /// 🔴 빈 catch 금지(헌법 #15). 삼킨 예외는 없는 일이 된다.
    /// </summary>
    /// <remarks>
    /// 실측으로 잡았다 — Web 쪽 <c>CreateHrExpenseAsync</c> 가 <c>catch { return false; }</c> 였다.
    /// 왜 실패했는지가 통째로 사라져 "신청 실패" 만 뜨고 원인을 못 찾는다.
    /// </remarks>
    [Fact]
    public void 경비_경로에_빈_catch_가_없다()
    {
        foreach (var parts in new[]
                 {
                     new[] { "src", "HitPan.Application", "Services", "HrService.cs" },
                     new[] { "src", "HitPan.Web", "Services", "HrService.cs" },
                     new[] { "src", "HitPan.API", "Controllers", "HrController.cs" },
                 })
        {
            // ⚠️ 주석은 걷어내고 본다. 봉합하면서 "종전엔 `catch { return false; }` 였다" 라고
            //    적어 둔 설명 자체에 걸려 헛실패했다(실측).
            var src = StripComments(ReadSource(parts));

            var empty = Regex.Matches(src, @"catch\s*(\([^)]*\))?\s*\{\s*\}");
            Assert.True(empty.Count == 0,
                $"🔴 {string.Join('/', parts)} 에 빈 catch 가 {empty.Count}개 있다(헌법 #15).");

            // `catch { return false; }` 처럼 예외 정보를 통째로 버리는 것도 막는다.
            var swallow = Regex.Matches(src, @"catch\s*\{\s*return\s+(false|null|new\(\))\s*;\s*\}");
            Assert.True(swallow.Count == 0,
                $"🔴 {string.Join('/', parts)} 에 예외를 통째로 버리는 catch 가 {swallow.Count}개 있다. "
                + "왜 실패했는지가 사라져 원인을 못 찾는다.");
        }
    }

    /// <summary>
    /// 🔴 테넌트는 JWT 에서만 온다(헌법 #2).
    /// </summary>
    [Fact]
    public void 경비_컨트롤러가_테넌트를_파라미터로_받지_않는다()
    {
        var ctrl = StripComments(ReadSource("src", "HitPan.API", "Controllers", "HrController.cs"));

        Assert.DoesNotContain("[FromQuery] string tenantId", ctrl);
        Assert.DoesNotContain("[FromBody] string tenantId", ctrl);
        Assert.DoesNotContain("[FromRoute] string tenantId", ctrl);
        Assert.Contains("HttpContext.Items[\"TenantId\"]", ctrl);
    }
}
