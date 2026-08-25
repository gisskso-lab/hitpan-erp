using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(20260825작7) 반품확인서 ↔ 판매목록 연결.
/// </summary>
/// <remarks>
/// <para>
/// 사장님 오더(2026-08-25): <i>"판매목록 불러오는 버튼이 있어야 됨.
/// 거래명세서 판매목록조회에도 반품으로 상태변경하는 버튼이 있어야됨. → 당연히 반품확인서에 자동반영."</i>
/// </para>
/// <para>
/// 🔴 <b>착수 전 판정이 틀렸던 자리다.</b> 인수인계서에 <i>"새 API 불필요 — 화면 배선만"</i> 이라 적었는데,
/// 실측하니 <b>전달 경로가 세 군데 끊겨</b> 있었다. 컬럼·FK·DTO 는 다 있는데 값이 안 흘렀다.
/// </para>
/// <list type="number">
/// <item>거래명세서 상세가 <c>delivery_item_id</c>·<c>warehouse_id</c> 를 <b>안 줬다</b></item>
/// <item>화면 저장 payload 에 <c>deliveryId</c>·<c>deliveryItemId</c> 가 <b>아예 없었다</b></item>
/// <item>수정 UPDATE 가 <c>delivery_id</c> 를 <b>안 건드려</b> 두 번째 저장에서 링크가 끊겼다</item>
/// </list>
/// <para>
/// ⚠️ <b>매입은 건드리지 않는다.</b> 지금은 판매 영역 차례다.
/// </para>
/// </remarks>
public class ReturnDeliveryLinkGateTests
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

    /// <summary>주석 줄을 걸러낸 실제 코드만 남긴다(주석에 적힌 낱말로 통과하는 것 방지).</summary>
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
                       && !t.StartsWith("///", StringComparison.Ordinal)
                       && !t.StartsWith("@*", StringComparison.Ordinal)
                       && !t.StartsWith("--", StringComparison.Ordinal);
            }));

    private static string SalesService() =>
        CodeLines(Read("src", "HitPan.Application", "Services", "SalesService.cs"));

    private static string ReturnPageCs() =>
        CodeLines(Read("src", "HitPan.Web", "Pages", "Sales", "SalesReturnPage.razor.cs"));

    private static string ReturnPageRazor() =>
        CodeLines(Read("src", "HitPan.Web", "Pages", "Sales", "SalesReturnPage.razor"));

    private static string ListDialog() =>
        CodeLines(Read("src", "HitPan.Web", "Components", "Sales", "SalesListDialog.razor"));

    /// <summary>여는 괄호부터 짝이 맞는 닫는 괄호까지 잘라낸다 — 메서드 하나만 보려고.</summary>
    private static string Slice(string source, string anchor)
    {
        var start = source.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{anchor}' 를 찾아야 한다");

        var open = source.IndexOf('{', start);
        Assert.True(open >= 0, $"'{anchor}' 뒤에 블록이 있어야 한다");

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) return source[start..(i + 1)];
            }
        }

        Assert.Fail($"'{anchor}' 블록의 끝을 찾지 못했다");
        return string.Empty;
    }

    // ───────────────────────────────────────────────────────────────
    // G1 — 거래명세서가 줄 식별자를 준다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>거래명세서 상세가 판매 줄 식별자를 돌려주는가.</b>
    /// 이게 없으면 품목을 불러와도 <b>어느 판매 줄에서 왔는지 못 적는다</b> —
    /// <c>sales_return_items.delivery_item_id</c> 가 컬럼·FK 까지 있는데 영원히 NULL 로 남는다.
    /// </summary>
    [Fact]
    public void 거래명세서_상세가_판매줄_식별자를_줘야_한다()
    {
        var svc = SalesService();
        var itemSql = Slice(svc, "const string itemSql");

        Assert.Contains("di.delivery_item_id AS DeliveryItemId", itemSql, StringComparison.Ordinal);
        Assert.Contains("di.warehouse_id AS WarehouseId", itemSql, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>양쪽 DTO 가 그 값을 담는가.</b>
    /// 서버(HitPan.Application)와 화면(HitPan.Web)에 <b>같은 이름의 클래스가 따로</b> 있다.
    /// 한쪽만 고치면 SQL 이 값을 실어 보내도 반대편에서 조용히 사라진다.
    /// </summary>
    [Fact]
    public void 서버와_화면_양쪽_DTO_가_판매줄_식별자를_담아야_한다()
    {
        var serverDto = CodeLines(Read("src", "HitPan.Application", "DTOs", "Sales", "DeliveryDtos.cs"));
        var webDto = CodeLines(Read("src", "HitPan.Web", "Models", "DeliveryModels.cs"));

        foreach (var (name, dto) in new[] { ("서버", serverDto), ("화면", webDto) })
        {
            var slice = Slice(dto, "class DeliveryItemDto");
            Assert.True(slice.Contains("DeliveryItemId", StringComparison.Ordinal),
                $"{name} DeliveryItemDto 에 DeliveryItemId 가 있어야 한다");
            Assert.True(slice.Contains("WarehouseId", StringComparison.Ordinal),
                $"{name} DeliveryItemDto 에 WarehouseId 가 있어야 한다");
        }
    }

    // ───────────────────────────────────────────────────────────────
    // G2·G3 — 화면이 링크를 실어 보낸다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>저장 payload 에 원 거래 연결이 실리는가.</b>
    /// 백엔드 INSERT 는 <c>@DeliveryId</c> 를 <b>정상 처리하고 있었다</b> —
    /// 받을 준비는 다 돼 있는데 화면이 안 보내서 지금까지 만든 반품확인서는 전부 링크가 NULL 이다.
    /// </summary>
    [Fact]
    public void 저장_payload_에_원거래_연결이_실려야_한다()
    {
        var save = Slice(ReturnPageCs(), "private async Task SaveAsync()");

        Assert.Contains("deliveryId =", save, StringComparison.Ordinal);
        Assert.Contains("_linkedDeliveryId", save, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>줄마다 원 판매 줄 연결이 실리는가.</b> 머리(header)만 이어 놓으면
    /// 어느 <b>품목 줄</b>이 어느 판매 줄에서 왔는지는 여전히 모른다 — 원단가 추적이 끊긴다.
    /// </summary>
    [Fact]
    public void 저장_payload_의_줄마다_판매줄_연결이_실려야_한다()
    {
        var save = Slice(ReturnPageCs(), "private async Task SaveAsync()");

        Assert.Contains("deliveryItemId =", save, StringComparison.Ordinal);
        Assert.Contains("l.DeliveryItemId", save, StringComparison.Ordinal);
    }

    // ───────────────────────────────────────────────────────────────
    // 🔴 G4 — 이번 차수의 핵심. 두 번째 저장에서 끊기던 자리
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>수정 저장해도 원 거래 연결이 남는가.</b>
    /// </summary>
    /// <remarks>
    /// <b>이 게이트가 이번 차수의 핵심이다.</b> 나머지가 다 통과해도 이게 없으면
    /// <i>"불러왔는데 한 번 더 고쳐 저장하면 링크가 사라지는"</i> 거짓봉합이 된다.
    /// 생성은 <c>delivery_id</c> 를 넣는데 수정이 안 넣던 <b>비대칭</b>이 원인이었다.
    /// </remarks>
    [Fact]
    public void 수정_저장해도_원거래_연결이_남아야_한다()
    {
        var update = Slice(SalesService(), "public async Task UpdateSalesReturnAsync");

        Assert.Contains("delivery_id=COALESCE(@DeliveryId, delivery_id)", update, StringComparison.Ordinal);
        Assert.Contains("DeliveryId =", update, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>수정 DTO 가 원 거래 연결을 받는가.</b>
    /// UPDATE 문만 고치고 DTO 를 안 고치면 화면이 보낸 값이 <b>바인딩 단계에서 사라진다</b>.
    /// </summary>
    [Fact]
    public void 수정_DTO_가_원거래_연결을_받아야_한다()
    {
        var dto = CodeLines(Read("src", "HitPan.Application", "DTOs", "Sales", "CreateSalesReturnRequest.cs"));
        var slice = Slice(dto, "class UpdateSalesReturnRequest");

        Assert.Contains("DeliveryId", slice, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>다시 열 때 링크가 되살아나는가.</b>
    /// 서버가 값을 줘도 화면이 안 받으면, 사용자가 문서를 다시 열어 고치는 순간
    /// 빈 값으로 저장돼 링크가 끊긴다 — G4 를 우회하는 두 번째 구멍이다.
    /// </summary>
    [Fact]
    public void 다시_열_때_원거래_연결이_되살아나야_한다()
    {
        // 서버가 줄 연결을 돌려준다
        var detail = Slice(SalesService(), "public async Task<SalesReturnDetailDto?> GetSalesReturnDetailAsync");
        Assert.Contains("sri.delivery_item_id AS DeliveryItemId", detail, StringComparison.Ordinal);

        // 화면이 그것을 받는다
        var load = Slice(ReturnPageCs(), "private async Task LoadReturnAsync");
        Assert.Contains("_linkedDeliveryId = detail.DeliveryId", load, StringComparison.Ordinal);
        Assert.Contains("DeliveryItemId = it.DeliveryItemId", load, StringComparison.Ordinal);
    }

    // ───────────────────────────────────────────────────────────────
    // G5·G6 — 버튼 두 개
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>반품확인서에 「판매불러오기」 버튼이 있는가.</b> 사장님 반려 ② 의 절반이다.
    /// </summary>
    [Fact]
    public void 반품확인서에_판매불러오기_버튼이_있어야_한다()
    {
        var razor = ReturnPageRazor();

        Assert.Contains("판매불러오기", razor, StringComparison.Ordinal);
        Assert.Contains("LoadFromDeliveryAsync", razor, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>판매목록조회에 「반품」 버튼이 있는가.</b> 사장님 반려 ② 의 나머지 절반이다.
    /// </summary>
    [Fact]
    public void 판매목록조회에_반품_버튼이_있어야_한다()
    {
        var dlg = ListDialog();

        Assert.Contains("CreateReturnAsync", dlg, StringComparison.Ordinal);
        Assert.Contains("WorkDocumentKind.SalesReturn", dlg, StringComparison.Ordinal);
        Assert.Contains("deliveryId=", dlg, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>판 적 없는 건은 반품 못 하는가.</b>
    /// <c>draft</c> 는 아직 판 것이 아니다 — 팔지 않은 물건은 돌아올 수 없다(헌법 #6).
    /// 잘못 쓴 임시전표는 반품이 아니라 삭제로 지운다.
    /// </summary>
    [Fact]
    public void 판매확정_전_거래는_반품할_수_없어야_한다()
    {
        var guard = Slice(ListDialog(), "private bool CanCreateReturn");

        Assert.Contains("confirmed", guard, StringComparison.Ordinal);
        Assert.Contains("invoiced", guard, StringComparison.Ordinal);
        Assert.False(guard.Contains("draft", StringComparison.Ordinal),
            "draft 를 반품 대상에 넣으면 판 적 없는 거래가 반품된다.");

        // 화면 쪽 2차 방어 — 다이얼로그를 우회해 들어와도 막는다.
        var page = Slice(ReturnPageCs(), "private async Task LoadFromDeliveryAsync");
        Assert.Contains("\"draft\"", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>여러 거래를 한 번에 반품 걸지 못하는가.</b>
    /// <c>sales_returns.delivery_id</c> 는 <b>단일 FK</b> 다. 두 건을 담으면
    /// 어느 거래에서 온 반품인지 못 적는다 — 링크가 있으나 마나가 된다.
    /// </summary>
    [Fact]
    public void 여러_거래를_한_반품확인서에_담을_수_없어야_한다()
    {
        var guard = Slice(ListDialog(), "private bool CanCreateReturn");

        Assert.Contains("== 1", guard, StringComparison.Ordinal);
    }

    // ───────────────────────────────────────────────────────────────
    // G7 — 로스 판정은 고객사가 한다
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>불러온 품목의 로스가 전부 해제 상태인가.</b>
    /// 사장님: <i>"로스판정 기준은 고객사가 정하는거지, 너가 왜 정해."</i>
    /// 반품사유나 품목으로 파손을 <b>추측하지 않는다</b>.
    /// </summary>
    [Fact]
    public void 불러온_품목의_로스는_전부_해제여야_한다()
    {
        var fill = Slice(ReturnPageCs(), "private async Task FillFromDeliveryAsync");

        Assert.Contains("IsLoss = false", fill, StringComparison.Ordinal);
        Assert.False(fill.Contains("IsLoss = true", StringComparison.Ordinal),
            "우리가 파손을 미리 판정하면 안 된다. 고객사가 정한다.");
    }

    /// <summary>
    /// 🔴 <b>불러온 단가가 판 값 그대로인가.</b>
    /// 반품은 <b>판 값으로 돌려준다</b>. 여기서 현재 단가표를 다시 조회하면
    /// 그새 단가가 바뀐 품목의 환불액이 어긋난다.
    /// </summary>
    [Fact]
    public void 불러온_단가는_판_값_그대로여야_한다()
    {
        var fill = Slice(ReturnPageCs(), "private async Task FillFromDeliveryAsync");

        Assert.Contains("UnitPrice = it.UnitPrice", fill, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>이미 입력한 품목을 말없이 덮어쓰지 않는가.</b> (헌법 #1)
    /// 손으로 적어 둔 줄이 확인 없이 사라지면 사용자는 다시 못 만든다.
    /// </summary>
    [Fact]
    public void 이미_입력한_품목은_묻고_바꿔야_한다()
    {
        var load = Slice(ReturnPageCs(), "private async Task LoadFromDeliveryAsync");

        Assert.Contains("ShowMessageBoxAsync", load, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>불러오기 연타를 막는가.</b> (20260825작4 계승)
    /// 두 번 누르면 같은 품목이 두 벌 들어가 환불액이 배가 된다.
    /// </summary>
    [Fact]
    public void 판매불러오기_연타를_막아야_한다()
    {
        var fill = Slice(ReturnPageCs(), "private async Task FillFromDeliveryAsync");

        Assert.Contains("if (_isLoadingDelivery) return;", fill, StringComparison.Ordinal);
        Assert.Contains("_isLoadingDelivery = true;", fill, StringComparison.Ordinal);
        Assert.Contains("_isLoadingDelivery = false;", fill, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>두 버튼이 같은 경로를 쓰는가.</b>
    /// 「판매불러오기」와 판매목록 「반품」이 각자 품목을 채우면
    /// <b>한쪽만 고쳐지는 날</b>이 온다. 채우는 코드는 한 벌이어야 한다.
    /// </summary>
    [Fact]
    public void 두_진입점이_같은_채움_경로를_써야_한다()
    {
        var cs = ReturnPageCs();

        // 다이얼로그 경로도, 질의문자열 경로도 같은 메서드를 부른다.
        var calls = cs.Split("FillFromDeliveryAsync(").Length - 1;
        Assert.True(calls >= 3,
            $"정의 1 + 호출 2 = 3곳 이상이어야 한다 (현재 {calls}곳). " +
            "두 진입점이 각자 품목을 채우면 한쪽만 고쳐지는 날이 온다.");

        Assert.Contains("SupplyParameterFromQuery", cs, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>질의를 넘길 때 빈 탭을 재사용하지 않는가.</b>
    /// 재사용하면 이미 열린 빈 탭으로 <b>전환만</b> 되고 주소가 안 바뀐다 —
    /// 넘긴 거래가 화면에 안 실려 <b>버튼이 먹통으로 보인다</b>.
    /// </summary>
    [Fact]
    public void 질의를_넘길_때는_빈탭을_재사용하지_않아야_한다()
    {
        var svc = CodeLines(Read("src", "HitPan.Web", "Services", "WorkTabService.cs"));
        var add = Slice(svc, "public bool TryAddTab(WorkDocumentKind kind, string? query)");

        Assert.Contains("if (!hasQuery)", add, StringComparison.Ordinal);
        Assert.Contains("state.Url += query;", add, StringComparison.Ordinal);
    }

    // ───────────────────────────────────────────────────────────────
    // ⚠️ 범위 — 손대면 안 되는 것들
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>매입 반품화면이 그대로인가.</b> (20260825작6 계승)
    /// 사장님: <i>"애초에 지금 매출파트 수정보완 작업중인데, 매입은 건들지 말았어야지."</i>
    /// </summary>
    [Fact]
    public void 매입_반품화면은_건드리지_않아야_한다()
    {
        var purchase = CodeLines(Read("src", "HitPan.Web", "Pages", "Purchase", "ReturnPage.razor"));

        Assert.Contains("_returnType", purchase, StringComparison.Ordinal);
        Assert.False(purchase.Contains("/sales-returns", StringComparison.Ordinal),
            "매출 경로는 매출 전용 화면에 있어야 한다.");
        Assert.False(purchase.Contains("판매불러오기", StringComparison.Ordinal),
            "판매 불러오기는 매출 화면 기능이다. 매입에 얹으면 두 업무가 다시 섞인다.");
    }

    /// <summary>
    /// 🔴 <b>「반품」 버튼이 원 거래명세서 상태를 바꾸지 않는가.</b>
    /// </summary>
    /// <remarks>
    /// 반품확정이 <b>이미</b> 매출·미수를 차감한다(20260825작6 실측).
    /// 원 거래까지 <c>returned</c> 로 바꾸면 <b>두 번 빠진다</b>.
    /// 부분반품(3개 중 1개)이면 원 거래를 통째로 바꿀 수도 없다.
    /// 원 거래 표기는 4-C 차수에서 부분·전체를 함께 보고 정한다.
    /// </remarks>
    [Fact]
    public void 반품버튼이_원거래_상태를_바꾸지_않아야_한다()
    {
        var create = Slice(ListDialog(), "private async Task CreateReturnAsync");

        Assert.False(create.Contains("BulkConfirmAsync", StringComparison.Ordinal),
            "반품 버튼이 판매확정을 부르면 안 된다.");
        Assert.False(create.Contains("UpdateAsync", StringComparison.Ordinal),
            "반품 버튼이 원 거래를 고치면 매출이 두 번 빠진다.");
        Assert.False(create.Contains("DeleteAsync", StringComparison.Ordinal),
            "반품은 원 거래 삭제가 아니다. 판 기록은 남아야 한다.");
    }

    /// <summary>
    /// 🔴 <b>「반품」 버튼이 반품을 확정하지 않는가.</b> (헌법 #6)
    /// 실제 반품 수량은 판 수량보다 적은 게 보통이고, 파손 판정도 사람이 봐야 한다.
    /// 확정은 반품확인서 화면의 「반품확정」이 한다.
    /// </summary>
    [Fact]
    public void 반품버튼은_확정하지_않고_초안만_열어야_한다()
    {
        var create = Slice(ListDialog(), "private async Task CreateReturnAsync");

        Assert.False(create.Contains("ConfirmSalesReturnAsync", StringComparison.Ordinal),
            "여기서 확정하면 사용자가 수량·파손을 고칠 기회가 없다.");
        Assert.Contains("TryAddTab", create, StringComparison.Ordinal);
    }
}
