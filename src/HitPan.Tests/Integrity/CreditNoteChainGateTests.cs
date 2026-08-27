using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(20260828작13) 마이너스 계산서 · 사슬연결도 · 계산서취소 P0 게이트.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 사장님 오더: <i>"매출반품은 마이너스 전표 끊으면 해결되잖아. 전자세금계산서 국세청 전송건도
/// 마이너스 계산서. 단, 마이너스 계산서 끊을시 <b>반드시 해당 반품의 연결사슬을 표기할것!!(사슬연결도)</b>"</i>
/// </para>
/// <para>
/// 🔴 <b>[1-V] 선행검증이 P0 를 찾아냈다</b> — <c>TaxInvoiceService.CancelAsync</c> 가
/// <b>발행이 만들지도 않은 분개를 되돌리고</b> 있었다. 실측(2026-08-28): 명세서 확정 1건 +
/// 계산서 취소 1건 ⇒ 외상매출금 0 / 매출 0 / 부가세예수금 0.
/// <b>거래명세서는 confirmed 이고 재고도 나갔는데 장부에서 매출만 사라진다</b>(헌법 #20 위반,
/// 부가세 매출세액 «과소»신고).
/// </para>
/// <para>
/// 🔴 원인은 <b>주석을 믿은 것</b>이다. 종전 주석이 <i>"역분개 — IssueAsync 기표의 차/대변 반전"</i>
/// 이라 적었으나 <c>IssueAsync</c> 에는 <c>AutoJournalHelper</c> 호출이 <b>한 건도 없다</b>.
/// 매출 기표는 「거래명세서 확정」에서 일어난다(<c>SalesService.cs:581</c>, 헌법 #6).
/// </para>
/// <para>
/// ⚠️ <b>마이너스 계산서는 회계를 기표하지 않는다.</b> 매출반품 분개는 반품 «확정» 시점에
/// 이미 <c>sales_return</c> 키로 들어간다(20260828작12). 또 기표하면 이중계상이다.
/// </para>
/// </remarks>
public class CreditNoteChainGateTests
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

    private static string Service() =>
        Read("src", "HitPan.Application", "Services", "TaxInvoiceService.cs");

    private static string Controller() =>
        Read("src", "HitPan.API", "Controllers", "TaxInvoiceController.cs");

    /// <summary>
    /// 메서드 «본문»만 잘라낸다.
    /// 🔴 파일 전체에서 낱말을 세면 주석·다른 메서드가 거짓 초록불을 만든다
    /// (가짜 게이트 누적 22번의 주 원인).
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

    [Fact(DisplayName = "P0 — 계산서 취소는 자기 분개가 실재할 때만 역분개한다")]
    public void Cancel_Reverses_Only_When_Own_Entry_Exists()
    {
        var body = MethodBody(Service(), "public async Task<CancelTaxInvoiceResponse> CancelAsync");

        // 가드가 있어야 한다
        Assert.Contains("hasOwnEntry", body);
        Assert.Contains("FROM journal_entries", body);

        // 🔴 무조건 역분개로 되돌아가면 안 된다
        Assert.DoesNotContain(
            "if (invoiceRow.AmountTotal != 0m || invoiceRow.VatTotal != 0m)", body);
    }

    [Fact(DisplayName = "마이너스 계산서가 음수 금액으로 발행된다")]
    public void CreditNote_Issues_Negative_Amounts()
    {
        var body = MethodBody(Service(), "public async Task<CreditNoteResponse> IssueCreditNoteAsync");

        // 부호를 확실히 뒤집는다 — Math.Abs 로 감싸 원본이 음수여도 두 번 뒤집히지 않게
        Assert.Contains("-Math.Abs(ret.TotalAmount)", body);
        Assert.Contains("-Math.Abs(ret.VatAmount)", body);
    }

    [Fact(DisplayName = "사슬연결도 — 마이너스 계산서가 반품을 source 축에 건다")]
    public void CreditNote_Links_Chain_To_Return()
    {
        var body = MethodBody(Service(), "public async Task<CreditNoteResponse> IssueCreditNoteAsync");

        // INSERT 에 사슬 축이 들어가야 한다
        Assert.Contains("source_type", body);
        Assert.Contains("source_id", body);
        Assert.Contains("CreditNoteSourceType", body);

        // 원 반품번호를 응답에 실어 화면이 「반품전표 : 반-...」 를 그릴 수 있어야 한다
        Assert.Contains("ret.ReturnNo", body);
    }

    [Fact(DisplayName = "확정된 반품만 마이너스 계산서를 끊을 수 있다")]
    public void CreditNote_Requires_Confirmed_Return()
    {
        var body = MethodBody(Service(), "public async Task<CreditNoteResponse> IssueCreditNoteAsync");

        Assert.Contains("return_not_confirmed", body);
        Assert.Contains("confirmed", body);
    }

    [Fact(DisplayName = "같은 반품에 마이너스 계산서를 두 번 못 끊는다")]
    public void CreditNote_Blocks_Duplicate()
    {
        var body = MethodBody(Service(), "public async Task<CreditNoteResponse> IssueCreditNoteAsync");

        // 🔴 국세청에 두 번 나가면 되돌릴 수 없다
        Assert.Contains("already_issued", body);
        Assert.Contains("FROM tax_invoices", body);
    }

    [Fact(DisplayName = "마이너스 계산서는 회계를 기표하지 않는다 — 이중계상 금지")]
    public void CreditNote_Does_Not_Post_Journal()
    {
        var body = MethodBody(Service(), "public async Task<CreditNoteResponse> IssueCreditNoteAsync");

        // 반품 확정이 이미 sales_return 키로 기표했다(20260828작12). 또 하면 이중계상.
        Assert.DoesNotContain("AutoJournalHelper", body);
    }

    [Fact(DisplayName = "API 진입점이 있다 — 화면이 부를 수 있어야 한다")]
    public void CreditNote_Endpoint_Exists()
    {
        var src = Controller();

        // 🔴 "고쳤다 ≠ 갔다" — 서비스만 만들고 진입점이 없으면 화면은 못 쓴다
        Assert.Contains("credit-notes", src);
        Assert.Contains("IssueCreditNoteAsync", src);

        // 헌법 #2 — tenant_id 는 파라미터로 받지 않는다
        var body = MethodBody(src, "public async Task<IActionResult> IssueCreditNote");
        Assert.Contains("HttpContext.Items[\"TenantId\"]", body);
    }
}
