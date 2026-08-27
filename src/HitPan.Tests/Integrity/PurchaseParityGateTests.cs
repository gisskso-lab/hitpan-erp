using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(20260825작16) 매입 ↔ 판매 정합 게이트.
/// </summary>
/// <remarks>
/// <para>
/// 사장님 실측·지시(2026-08-25): <i>"매입확정된 품목도 매출과 마찬가지로 반품으로 전환 가능하도록"</i> ·
/// <i>"발주서, 매입처리, 반품처리 목록 그리드도 판매관리 대메뉴 전표들 메뉴와 마찬가지로 담당자 행 추가"</i> ·
/// <i>"반품처리 반품사유도 판매쪽 반품확인서와 마찬가지로 자유입력"</i>.
/// </para>
/// <para>
/// 🔴 <b>이번 차수의 근본 교훈은 "판매만 고치고 매입에 안 옮겼다" 이다.</b>
/// 조회유형 재조회(작2)도, 반품사유 자율입력(작6)도 판매에서 이미 고쳤는데
/// 매입에는 그대로 병이 남아 있었다. <b>게이트가 판매 폴더만 보고 있었기 때문에</b>
/// 매입은 병이 있는 채로 초록불이었다. 그래서 이 게이트는 <b>매입을 직접</b> 본다.
/// </para>
/// <para>
/// ⚠️ 한계 — 이 시험은 <b>배선이 끊겼는지</b>를 본다. 화면 클릭·DB 왕복은 재현하지 않는다.
/// <b>최종 판정은 사장님 실측이다</b>(개발PC 통과는 검증이 아니다).
/// </para>
/// </remarks>
public class PurchaseParityGateTests
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

    /// <summary>주석을 걷어낸 실제 코드만 남긴다 — 봉합 설명의 낱말이 거짓 초록불을 만들지 않게.</summary>
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
                       && !t.StartsWith("@*", StringComparison.Ordinal)
                       && !t.StartsWith("///", StringComparison.Ordinal);
            }));

    private static string PurchaseService() =>
        CodeLines(Read("src", "HitPan.Application", "Services", "PurchaseService.cs"));

    // ───────────────────────────────────────────────────────────
    // 오더 2 — 확정건도 반품으로 전환할 수 있어야 한다
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>확정 행 선택을 다시 막으면 안 된다.</b>
    /// 사장님 전결: <i>"반품은 조회전용이 아니라 워크플로우의 한 축이지."</i>
    /// 종전 코드는 <c>ToggleOneAsync</c> 에서 확정 행 선택을 통째로 무시해
    /// <b>확정된 매입을 반품으로 전환할 길이 없었다</b> — 정작 반품이 필요한 건 확정된 건이다.
    /// </summary>
    [Fact]
    public void 확정된_매입도_선택할_수_있어야_한다()
    {
        var code = CodeLines(Read("src", "HitPan.Web", "Components", "Purchase",
            "PurchaseReceiptList.razor.cs"));

        Assert.DoesNotContain("if (IsConfirmed(row)) { row.IsChecked = false;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("value && !IsConfirmed(row)", code, StringComparison.Ordinal);

        var razor = CodeLines(Read("src", "HitPan.Web", "Components", "Purchase",
            "PurchaseReceiptList.razor"));
        Assert.DoesNotContain("Disabled=\"@IsConfirmed(context)\"", razor, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>선택을 열었으니 위험한 동작은 여전히 막혀야 한다.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 <b>20260827작8 — 일괄삭제를 이 시험에서 뺐다.</b> 지우는 게 아니라 <b>막는 자리를 옮겼다.</b>
    ///
    /// <para>
    /// 종전엔 <c>BulkDeleteAsync</c> 도 화면에서 <c>draft</c> 로 걸러 확정 행을 막았다.
    /// 그런데 그러면 확정 건은 <b>DELETE 요청 자체가 안 나가</b> 서버 삭제가드(작7)가
    /// 실행되지 못했고, 화면이 자기 혼자 만든 <i>"삭제 가능한 draft 상태 매입명세가 없습니다"</i>
    /// 를 뿌렸다 — <b>1.3.28 사장님 실측 반려의 원인이 바로 이 필터다.</b>
    /// 사장님이 요구한 건 <i>"해당 사슬로 인해 삭제가 불가하다고 <b>메시지를 띄우면</b> 되"</i> 이고,
    /// 막기만 하고 <b>이유를 못 알려주면 요구를 못 지킨 것</b>이다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>보호가 사라진 게 아니다.</b> 확정 매입 삭제는 이제 서버
    /// <c>DeletePurchaseReceiptAsync</c> 가 막는다(원장·자식반품 검사).
    /// 화면 필터보다 <b>강한 자리</b>다 — API 를 직접 부르는 경로도 함께 막히기 때문이다.
    /// 그 보호는 <c>PurchaseChainGuardGateTests</c> 가, 메시지 도달은
    /// <c>ErrorMessageDeliveryGateTests.G1</c> 이 각각 지킨다.
    /// </para>
    ///
    /// <para>
    /// <b>일괄확정(<c>BulkConfirmAsync</c>)은 그대로 둔다.</b> 이건 되돌릴 수 없는 원장 기표를
    /// 일으키는 동작이라 확정 행이 섞이면 <b>이중 기표</b>가 된다. 삭제와 성격이 다르다.
    /// </para>
    /// </remarks>
    [Fact]
    public void 일괄확정은_draft_만_대상이어야_한다()
    {
        var code = CodeLines(Read("src", "HitPan.Web", "Components", "Purchase",
            "PurchaseReceiptList.razor.cs"));

        var at = code.IndexOf("private async Task BulkConfirmAsync()", StringComparison.Ordinal);
        Assert.True(at >= 0, "BulkConfirmAsync 가 있어야 한다");

        var body = code[at..Math.Min(code.Length, at + 900)];
        Assert.Contains("\"draft\"", body);
    }

    /// <summary>
    /// 🔴 <b>단건 매입확정 버튼이 있어야 한다.</b> 사장님 지시:
    /// <i>"선택일괄확정버튼 옆에 매입확정 버튼 만들기"</i>.
    /// 정의(<c>ConfirmOneAsync</c>)와 배선(<c>OnClick</c>)을 <b>따로</b> 본다 —
    /// 이름만 있고 안 걸린 경우를 잡기 위해서다.
    /// </summary>
    [Fact]
    public void 매입처리_목록에_단건_확정버튼이_있어야_한다()
    {
        var razor = CodeLines(Read("src", "HitPan.Web", "Components", "Purchase",
            "PurchaseReceiptList.razor"));
        var cs = CodeLines(Read("src", "HitPan.Web", "Components", "Purchase",
            "PurchaseReceiptList.razor.cs"));

        Assert.Contains("ConfirmOneAsync(context)", razor, StringComparison.Ordinal);
        Assert.Contains("private async Task ConfirmOneAsync", cs, StringComparison.Ordinal);

        var at = cs.IndexOf("private async Task ConfirmOneAsync", StringComparison.Ordinal);
        var body = cs[at..Math.Min(cs.Length, at + 2200)];

        // 실제로 확정 API 를 부르는가 — 껍데기 메서드 차단.
        Assert.Contains("/confirm", body, StringComparison.Ordinal);
        // 두 번 나가는 것을 막는가(화면 잠금 + 서버 멱등키 둘 다).
        Assert.Contains("_confirmingId", body, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>다이얼로그 안에서 <c>AuthorizeView</c> 를 쓰면 목록이 통째로 죽는다.</b>
    /// <c>MudDialogProvider</c> 가 <c>CascadingAuthenticationState</c> 바깥이라 그렇다(작11).
    /// 권한은 서버(403)에 맡긴다.
    /// </summary>
    [Fact]
    public void 매입_목록에_AuthorizeView_를_쓰면_안_된다()
    {
        foreach (var file in new[] { "PurchaseReceiptList.razor", "PurchaseOrderList.razor", "PurchaseReturnList.razor" })
        {
            var razor = CodeLines(Read("src", "HitPan.Web", "Components", "Purchase", file));
            Assert.DoesNotContain("<AuthorizeView", razor, StringComparison.Ordinal);
        }
    }

    // ───────────────────────────────────────────────────────────
    // 오더 3 — 작성자(담당자) 행
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>매입 3목록에 작성자 칸이 있어야 한다.</b> 사장님 지시대로 판매 전표와 같은 모양이다.
    /// </summary>
    [Theory]
    [InlineData("PurchaseOrderList.razor")]
    [InlineData("PurchaseReceiptList.razor")]
    [InlineData("PurchaseReturnList.razor")]
    public void 매입_3목록에_작성자_칸이_있어야_한다(string file)
    {
        var razor = CodeLines(Read("src", "HitPan.Web", "Components", "Purchase", file));

        Assert.Contains("<MudTh>작성자</MudTh>", razor, StringComparison.Ordinal);
        Assert.Contains("context.CreatedByName", razor, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>칸만 만들고 값이 안 오면 영원히 빈칸이다.</b>
    /// 조회 SQL 3곳이 <c>employees</c> 를 <c>user_id = created_by</c> 로 조인해야 한다.
    /// ⚠️ <c>employee_id</c> 로 조인하면 축이 갈려 <b>같은 「작성자」인데 화면마다 다른 사람</b>이 나온다.
    /// </summary>
    [Fact]
    public void 매입_목록조회가_작성자_이름을_함께_가져와야_한다()
    {
        var svc = PurchaseService();

        var selects = svc.Split("ec.emp_name AS CreatedByName").Length - 1;
        Assert.True(selects >= 3,
            $"발주·매입·반품 3목록이 작성자를 SELECT 해야 한다 — 현재 {selects}곳");

        var joins = svc.Split("ec.user_id =").Length - 1;
        Assert.True(joins >= 3,
            $"작성자 조인은 employees.user_id 축이어야 한다 — 현재 {joins}곳");
    }

    /// <summary>
    /// 🔴 <b>읽기만 고치면 값이 없다 — 쓰기도 채워야 한다.</b>
    /// 발주·매입 엔티티 2곳 + 반품 SQL 2곳 = <b>4곳</b>.
    /// ⚠️ 반품이 2곳인 게 핵심이다 — 직접작성과 매입→반품 <b>전환</b> 경로가 따로 있다.
    /// 한쪽만 채우면 <b>전환으로 만든 반품만 작성자가 빈다</b>(작7 에서 겪은 비대칭).
    /// </summary>
    [Fact]
    public void 매입_전표_작성시_작성자를_채워야_한다()
    {
        var svc = PurchaseService();

        var writes = svc.Split("CreatedBy = _currentTenant.UserId").Length - 1;
        Assert.True(writes >= 4,
            $"발주·매입 엔티티 2곳 + 반품 INSERT 2곳 = 4곳이어야 한다 — 현재 {writes}곳");

        // 반품 INSERT 두 곳 모두 컬럼을 넣었는가.
        var cols = svc.Split("created_by, created_at, updated_at)").Length - 1;
        Assert.True(cols >= 2,
            $"반품 INSERT 2곳(직접작성·매입전환)이 created_by 를 넣어야 한다 — 현재 {cols}곳");
    }

    // ───────────────────────────────────────────────────────────
    // 오더 4 — 반품사유 자율 입력
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>반품사유를 고정 콤보로 되돌리면 안 된다.</b> 사장님:
    /// <i>"로스판정 기준은 고객사가 정하는거지, 너가 왜 정해"</i> — 헌법 #11 과 같은 축이다.
    /// </summary>
    [Fact]
    public void 매입_반품사유는_자율입력이어야_한다()
    {
        var grid = CodeLines(Read("src", "HitPan.Web", "Components", "Purchase",
            "PurchaseReturnGrid.razor"));

        // 우리가 정한 사유 코드를 화면에 박아두지 않는다.
        Assert.DoesNotContain("<MudSelectItem Value=\"@(\"defect\")\">", grid, StringComparison.Ordinal);
        Assert.DoesNotContain("@bind-Value=\"ReturnReason\"", grid, StringComparison.Ordinal);

        // 직접 입력이 되어야 한다 — 목록이 비어도 쓸 수 있어야 하니 CoerceValue 가 핵심이다.
        Assert.Contains("MudAutocomplete", grid, StringComparison.Ordinal);
        Assert.Contains("CoerceValue=\"true\"", grid, StringComparison.Ordinal);
        Assert.Contains("SearchFunc=\"SearchReasonAsync\"", grid, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>쓴 말이 다음 선택지가 되려면 서버가 목록을 줘야 한다.</b>
    /// 화면·컨트롤러·서비스 3층을 <b>따로</b> 본다.
    /// </summary>
    [Fact]
    public void 매입_반품사유_목록_API_가_3층_모두_배선돼야_한다()
    {
        var grid = CodeLines(Read("src", "HitPan.Web", "Components", "Purchase",
            "PurchaseReturnGrid.razor"));
        Assert.Contains("api/purchase/returns/reasons", grid, StringComparison.Ordinal);

        var ctl = CodeLines(Read("src", "HitPan.API", "Controllers", "PurchaseController.cs"));
        Assert.Contains("[HttpGet(\"returns/reasons\")]", ctl, StringComparison.Ordinal);

        var svc = PurchaseService();
        Assert.Contains("GetPurchaseReturnReasonsAsync", svc, StringComparison.Ordinal);
        Assert.Contains("FROM purchase_returns", svc, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>라우트 순서 — <c>reasons</c> 가 <c>{id}</c> 보다 앞이어야 한다.</b>
    /// 뒤에 두면 "reasons" 가 id 로 잡혀 상세조회로 새어 <b>404</b> 가 난다.
    /// 배선이 다 맞는데 화면만 안 되는, 찾기 어려운 종류의 고장이다.
    /// </summary>
    [Fact]
    public void 반품사유_라우트가_상세조회보다_앞에_있어야_한다()
    {
        var ctl = CodeLines(Read("src", "HitPan.API", "Controllers", "PurchaseController.cs"));

        var reasons = ctl.IndexOf("[HttpGet(\"returns/reasons\")]", StringComparison.Ordinal);
        var byId = ctl.IndexOf("[HttpGet(\"returns/{id}\")]", StringComparison.Ordinal);

        Assert.True(reasons >= 0, "returns/reasons 라우트가 있어야 한다");
        Assert.True(byId >= 0, "returns/{id} 라우트가 있어야 한다");
        Assert.True(reasons < byId,
            "returns/reasons 가 returns/{id} 보다 먼저 선언돼야 한다 — 뒤에 두면 reasons 가 id 로 잡혀 404 가 난다.");
    }
}
