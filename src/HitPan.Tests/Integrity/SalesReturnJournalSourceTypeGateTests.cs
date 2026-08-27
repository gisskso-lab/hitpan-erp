using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(20260828작12) 매출반품 분개 «자기 이름표» 게이트.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>[1-V] 선행검증에서 나온 건이다</b> (2026-08-28 신설 루틴 첫 적용).
/// 선행검증서: <c>docs/검증/선행/20260828_선행검증서_매출반품_회계읽는쪽.md</c>
/// </para>
/// <para>
/// 🔴 <b>무엇이 문제였나</b> — 분개는 맞았는데 이름표가 남의 것이었다.
/// 매출반품 확정은 회계 분개를 <b>정상으로 만들고 있었다</b>(금액·차대 모두 정확).
/// 그런데 자기 <c>source_type</c> 이 없어 <c>sales_delivery_cancel</c>(명세서 취소) 키를 빌려 썼다.
/// </para>
/// <para>
/// 매입: <c>purchase_return</c> + <c>purchase_return_cancel</c> — 둘 다 있다.<br/>
/// 매출: (없음) + <c>sales_return_cancel</c> — <b>「반품」 키가 없었다.</b>
/// </para>
/// <para>
/// ⇒ 장부에서 «반품»과 «명세서 취소»가 섞여 식별 불가.
/// 그 결과 <c>FinanceService</c> 「확정전표 기표 누락」 검사가 매출반품을 <b>셀 수 없었다</b>
/// (매입은 세고 있었다). 기표가 실패해도 아무도 못 잡는 사각이었다.
/// </para>
/// <para>
/// 🔴 <b>내가 두 번 틀린 자리다.</b> 인계5 «회계에 아예 없다»(❌ 분개는 만든다),
/// 인계6 «구멍은 읽는 쪽»(🟡 절반 — 쓰는 쪽이 원인). 두 번 다 <b>코드를 안 읽고 낱말만 셌다</b>
/// (헌법 #32). 이 시험은 그 되돌림을 막는다.
/// </para>
/// <para>
/// ⚠️ <b>매입반품과 대칭이지만 계정이 다르다</b> — 매입반품은 매입채무(대변),
/// 매출반품은 외상매출금(대변). 사장님 지시대로 <b>복붙하지 않는다</b>.
/// </para>
/// </remarks>
public class SalesReturnJournalSourceTypeGateTests
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

    private static string Read(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"{path} 가 있어야 한다");
        return File.ReadAllText(path);
    }

    private static string Helper() =>
        Read("src", "HitPan.Application", "Services", "AutoJournalHelper.cs");

    private static string SalesService() =>
        Read("src", "HitPan.Application", "Services", "SalesService.cs");

    private static string FinanceService() =>
        Read("src", "HitPan.Application", "Services", "FinanceService.cs");

    /// <summary>
    /// 메서드 «본문»만 잘라낸다.
    /// 🔴 구간을 자르는 이유 — 파일 전체에서 낱말을 세면 주석·다른 메서드가 거짓 초록불을 만든다.
    /// 같은 낱말이 정의·배선·주석에 전부 산다(가짜 게이트 누적 22번의 주 원인).
    /// </summary>
    private static string MethodBody(string source, string signature)
    {
        var at = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, $"{signature} 가 있어야 한다");

        var open = source.IndexOf('{', at);
        Assert.True(open > 0, $"{signature} 본문 시작을 찾아야 한다");

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) return source[open..i];
            }
        }

        Assert.Fail($"{signature} 본문 끝을 찾아야 한다");
        return string.Empty;
    }

    [Fact(DisplayName = "매출반품 전용 기표 메서드가 자기 source_type 으로 기록한다")]
    public void RecordSalesReturn_Uses_Own_SourceType()
    {
        var body = MethodBody(Helper(), "public static async Task RecordSalesReturnAsync(");

        Assert.Contains("\"sales_return\"", body);

        // 🔴 남의 이름표를 다시 빌려 쓰면 안 된다
        Assert.DoesNotContain("\"sales_delivery_cancel\"", body);
    }

    [Fact(DisplayName = "매출반품 분개는 매출 계정을 쓴다 — 매입반품 복붙 금지")]
    public void RecordSalesReturn_Uses_Sales_Accounts()
    {
        var body = MethodBody(Helper(), "public static async Task RecordSalesReturnAsync(");

        // 차변 매출 + 부가세예수금 / 대변 외상매출금
        Assert.Contains("SalesRevenue", body);
        Assert.Contains("VatPayable", body);
        Assert.Contains("AccountsReceivable", body);

        // ⚠️ 매입 계정이 섞이면 복붙 사고다
        Assert.DoesNotContain("AccountsPayable", body);
        Assert.DoesNotContain("VatReceivable", body);
    }

    [Fact(DisplayName = "매출반품 확정이 전용 메서드를 부른다 — 명세서취소 차용 해소")]
    public void ReturnConfirm_Calls_Dedicated_Method()
    {
        var body = MethodBody(
            SalesService(),
            "public async Task ConfirmSalesReturnAsync(");

        Assert.Contains("RecordSalesReturnAsync", body);

        // 🔴 되돌아가면 안 된다
        Assert.DoesNotContain("RecordSalesDeliveryCancelAsync", body);
    }

    [Fact(DisplayName = "정합성 검사가 매출반품 기표누락을 센다 — 매입만 세던 사각 해소")]
    public void IntegrityCheck_Counts_SalesReturn()
    {
        var src = FinanceService();

        // 매입은 원래 세고 있었다. 매출반품도 같이 세야 대칭이 맞는다.
        Assert.Contains("j.source_type='purchase_return'", src);
        Assert.Contains("j.source_type='sales_return'", src);
        Assert.Contains("FROM sales_returns sr", src);
    }
}
