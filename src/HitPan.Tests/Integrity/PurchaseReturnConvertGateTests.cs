using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(20260825작18) 매입반품 전환·목록·현황 게이트.
/// </summary>
/// <remarks>
/// <para>
/// 사장님 실측 반려(2026-08-25): <i>"1. 반품처리에 매입자료 불러오기 버튼 생성"</i> ·
/// <i>"2. 매입처리에서 반품처리 버튼 클릭시 매입반품목록에 조회가 안됨"</i> ·
/// <i>"3. 반품현황에도 조회가 안됨"</i>.
/// </para>
/// <para>
/// 🔴 <b>이번 차수의 근본 교훈</b> — 증상은 3개인데 <b>원인은 서로 달랐다</b>.
/// PM 이 초안에서 셋을 한 덩어리로 보고 1:1 봉합을 붙였다가 매니저 반증에 전부 깨졌다.
/// 특히 <b>목록의 「확정」 버튼만 IsSalesReturn 분기가 빠져</b> 매입반품 ID 를 매출 API 로
/// 보내고 있었다 — 확정이 원천 차단돼 있었으니 현황(=confirmed 집계)이 0건인 게 당연했다.
/// </para>
/// <para>
/// ⚠️ 한계 — 이 시험은 <b>배선이 끊겼는지</b>를 본다. 화면 클릭·DB 왕복은 재현하지 않는다.
/// <b>최종 판정은 사장님 실측이다</b>(개발PC 통과는 검증이 아니다).
/// </para>
/// </remarks>
public class PurchaseReturnConvertGateTests
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
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// 주석을 걷어낸 코드만 남긴다.
    /// </summary>
    /// <remarks>
    /// 🔴 봉합 주석은 대개 <b>고치기 전 코드를 인용</b>한다(<i>"종전 `catch { return new(); }` 는…"</i>).
    /// 주석까지 보면 <b>고쳤는데도 게이트가 옛 코드를 찾아내</b> 빨간불이 난다.
    /// 반대로 금지패턴 검사에서는 <b>주석만 지우고 코드는 안 지운 척</b>에 속을 수도 있다.
    /// 그래서 판정은 언제나 <b>코드에만</b> 한다.
    /// </remarks>
    private static string StripComments(string source)
    {
        var lines = source.Split('\n')
            .Select(l =>
            {
                var trimmed = l.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)) return string.Empty;
                if (trimmed.StartsWith("--", StringComparison.Ordinal)) return string.Empty;
                if (trimmed.StartsWith("///", StringComparison.Ordinal)) return string.Empty;
                if (trimmed.StartsWith("*", StringComparison.Ordinal)) return string.Empty;
                return l;
            });
        return string.Join("\n", lines);
    }

    /// <summary>메서드 본문만 잘라낸다 — 파일 전체를 보면 다른 메서드의 코드에 속는다.</summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"메서드를 찾아야 한다: {signature}");

        var brace = source.IndexOf('{', start);
        Assert.True(brace >= 0, $"본문 시작 중괄호를 찾아야 한다: {signature}");

        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) return source.Substring(brace, i - brace + 1);
            }
        }

        Assert.Fail($"본문 끝을 찾아야 한다: {signature}");
        return string.Empty;
    }

    // ── 오더 ②③ · P0 — 목록의 「확정」이 매입 경로로 가야 한다 ──

    /// <summary>
    /// 🔴 이번 차수의 P0. 매입반품 목록에서 확정을 누르면 <b>매입</b> 확정 API 로 가야 한다.
    /// 매입반품 ID 는 purchase_returns 에 있어, sales_returns 조회는 반드시 0건이다.
    /// </summary>
    [Fact]
    public void 목록_확정은_매출전용이_아니라_반품유형으로_갈라야_한다()
    {
        var body = MethodBody(
            Read("src", "HitPan.Web", "Components", "Purchase", "PurchaseReturnList.razor.cs"),
            "private async Task ConfirmOneAsync");

        Assert.Contains("ConfirmPurchaseReturnAsync", body);
        Assert.Contains("IsSalesReturn", body);

        // 🔴 동작 판정 — "매출 호출이 있다/없다" 가 아니라 **분기 안에 있는가**를 본다.
        //   무조건 매출로 가던 종전 코드는 이 검사에서 걸린다.
        var salesCallIndex = body.IndexOf("ConfirmSalesReturnAsync", StringComparison.Ordinal);
        var branchIndex = body.IndexOf("IsSalesReturn", StringComparison.Ordinal);
        Assert.True(salesCallIndex < 0 || branchIndex < salesCallIndex,
            "매출 확정 호출은 IsSalesReturn 분기 뒤에 있어야 한다 — 무조건 매출로 가면 매입반품은 확정될 수 없다");
    }

    /// <summary>매입반품 확정 안내문이 매출 문맥(매출·미수)을 그대로 쓰면 안 된다.</summary>
    [Fact]
    public void 매입반품_확정안내는_미지급금_문맥이어야_한다()
    {
        var body = MethodBody(
            Read("src", "HitPan.Web", "Components", "Purchase", "PurchaseReturnList.razor.cs"),
            "private async Task ConfirmOneAsync");

        Assert.Contains("미지급금", body);
    }

    // ── 오더 ② — 전환하면 그 반품서로 데려간다 ──

    /// <summary>
    /// 서버가 주는 returnId 를 버리면 안 된다. 버리면 담당자가 방금 만든 문서를 찾을 수 없다.
    /// </summary>
    [Fact]
    public void 전환_서비스는_반품ID를_돌려줘야_한다()
    {
        var src = Read("src", "HitPan.Web", "Services", "DeliveryService.cs");
        var body = MethodBody(src, "ConvertReceiptToReturnAsync(string receiptId");

        Assert.Contains("ReturnId", body);

        // bool 만 돌려주던 종전 시그니처면 이 검사에서 걸린다.
        Assert.DoesNotContain("public async Task<bool> ConvertReceiptToReturnAsync", src);
    }

    /// <summary>전환 성공 시 그 반품서 화면으로 이동해야 한다 (사장님 결재).</summary>
    [Fact]
    public void 전환후_반품서_화면으로_이동해야_한다()
    {
        var body = MethodBody(
            Read("src", "HitPan.Web", "Pages", "Purchase", "PurchaseReceiptPage.razor.cs"),
            "private async Task ConvertToReturnAsync");

        Assert.Contains("NavigateTo", body);
        Assert.Contains("/returns", body);
    }

    /// <summary>이동 대상 화면이 id 쿼리를 실제로 받아야 한다 — 안 받으면 빈 화면이 열린다.</summary>
    [Fact]
    public void 반품화면은_id_쿼리를_수신해야_한다()
    {
        var src = Read("src", "HitPan.Web", "Pages", "Purchase", "ReturnPage.razor.cs");

        Assert.Contains("SupplyParameterFromQuery", src);
        Assert.Contains("LoadReturnAsync", MethodBody(src, "protected override async Task OnInitializedAsync"));
    }

    /// <summary>
    /// 🔴 <b>두 번째 전환</b>부터 화면이 안 바뀌던 회귀를 막는다 (검증팀 적발).
    /// </summary>
    /// <remarks>
    /// <c>/returns</c> 는 단일 라우트이고 반품 탭이 그 URL 하나를 재사용한다.
    /// <c>OnInitializedAsync</c> 에서만 id 를 읽으면, 화면이 이미 열린 상태에서 다시 전환할 때
    /// Blazor 가 컴포넌트를 재사용해 <b>이전 반품서가 그대로 남는다</b>.
    /// 첫 전환만 보고 "고쳤다" 하면 놓치는 자리다 — <i>끊기는 자리만 옮겨간다</i>.
    /// </remarks>
    [Fact]
    public void 반품화면은_id가_바뀌면_다시_읽어야_한다()
    {
        var src = Read("src", "HitPan.Web", "Pages", "Purchase", "ReturnPage.razor.cs");

        // 🔴 이름 앞부분만 맞춰선 안 된다 — OnParametersSetAsyncDISABLED 같은 이름도 통과한다.
        //   실제로 이 검사를 그렇게 짰다가, 메서드를 비활성화했는데도 초록불이 났다.
        //   Blazor 가 호출하는 **정확한 시그니처**를 요구한다.
        Assert.Contains("protected override async Task OnParametersSetAsync()", src);

        var body = StripComments(MethodBody(src, "protected override async Task OnParametersSetAsync()"));

        Assert.Contains("LoadReturnAsync", body);
        // 같은 문서를 두 번 읽지 않도록 이전 값과 비교해야 한다(무한 재조회 방지).
        Assert.Contains("_loadedFromQueryId", body);
    }

    // ── 오더 ① — 매입불러오기 ──

    /// <summary>「매입불러오기」 버튼이 있어야 한다.</summary>
    [Fact]
    public void 반품처리에_매입불러오기_버튼이_있어야_한다()
    {
        var markup = Read("src", "HitPan.Web", "Pages", "Purchase", "ReturnPage.razor");

        Assert.Contains("매입불러오기", markup);
        // 🔴 낱말 하나로 검사하지 않는다 — 버튼이 실제로 핸들러에 배선돼야 한다.
        Assert.Contains("OnClick=\"LoadFromReceiptAsync\"", markup);
    }

    /// <summary>
    /// 🔴 사장님 결재 — 새 화면을 만들지 않고 <b>기존 매입목록</b>을 연다.
    /// </summary>
    [Fact]
    public void 매입불러오기는_기존_매입목록을_재사용해야_한다()
    {
        var body = MethodBody(
            Read("src", "HitPan.Web", "Pages", "Purchase", "ReturnPage.razor.cs"),
            "private async Task LoadFromReceiptAsync");

        Assert.Contains("PurchaseReceiptList", body);
    }

    /// <summary>확정 안 된 매입은 불러올 수 없다 (헌법 #6) — 받지도 않은 물건은 반품 대상이 아니다.</summary>
    [Fact]
    public void 매입불러오기는_확정건만_받아야_한다()
    {
        var body = MethodBody(
            Read("src", "HitPan.Web", "Pages", "Purchase", "ReturnPage.razor.cs"),
            "private async Task LoadFromReceiptAsync");

        Assert.Contains("confirmed", body);
    }

    /// <summary>작8 사고 이식 — partnerCache 를 안 채우면 저장이 "거래처를 선택해주세요" 로 막힌다.</summary>
    [Fact]
    public void 매입불러오기는_거래처캐시를_채워야_한다()
    {
        // 🔴 주석을 걷어내고 본다 — 봉합 근거 주석에도 `_partnerCache` 라는 낱말이 나온다.
        //   낱말만 세면 **실제 적재 라인을 지워도 초록불**이 난다(검증팀 실증).
        var body = StripComments(MethodBody(
            Read("src", "HitPan.Web", "Pages", "Purchase", "ReturnPage.razor.cs"),
            "private async Task LoadFromReceiptAsync"));

        // 이름이 아니라 **적재 동작**을 요구한다.
        Assert.Contains("_partnerCache ??= await PartnersApi.GetListAsync()", body);
    }

    // ── W5 — 전환 안전장치 ──

    /// <summary>확정된 매입만 반품 전환할 수 있어야 한다 (판매쪽과 대칭).</summary>
    [Fact]
    public void 전환은_확정된_매입만_대상이어야_한다()
    {
        var body = MethodBody(
            Read("src", "HitPan.Application", "Services", "PurchaseService.cs"),
            "public async Task<(string ReturnId, string ReturnNo)> ConvertReceiptToReturnAsync");

        Assert.Contains("status", body);
        Assert.Contains("매입확정 전", body);
    }

    /// <summary>
    /// 같은 매입을 두 번 전환하면 안 된다. 화면에 변화가 없어 담당자가 또 누르던 자리다.
    /// </summary>
    [Fact]
    public void 전환은_중복을_막아야_한다()
    {
        var body = MethodBody(
            Read("src", "HitPan.Application", "Services", "PurchaseService.cs"),
            "public async Task<(string ReturnId, string ReturnNo)> ConvertReceiptToReturnAsync");

        Assert.Contains("이미 반품 전환된", body);
        // 살아있는 반품만 세야 한다 — 취소분은 다시 전환할 수 있어야 한다.
        Assert.Contains("canceled", body);
    }

    /// <summary>헤더·품목이 한 트랜잭션이어야 한다 — 아니면 품목 0건 유령 헤더가 남는다.</summary>
    [Fact]
    public void 전환은_한_트랜잭션이어야_한다()
    {
        var body = MethodBody(
            Read("src", "HitPan.Application", "Services", "PurchaseService.cs"),
            "public async Task<(string ReturnId, string ReturnNo)> ConvertReceiptToReturnAsync");

        Assert.Contains("BeginTransactionAsync", body);
        Assert.Contains("CommitAsync", body);
        Assert.Contains("RollbackAsync", body);
    }

    /// <summary>
    /// 🔴 트랜잭션은 <b>같은 커넥션</b>일 때만 트랜잭션이다.
    /// </summary>
    /// <remarks>
    /// PM 이 이 자리에서 한 번 틀렸다 — 조회는 <c>_db</c>, 기록은 <c>_unitOfWork</c> 커넥션으로 짰다.
    /// <c>IDbConnection</c> 은 <c>InfrastructureExtensions.cs</c> 에서 <c>new MySqlConnection(connStr)</c>
    /// 으로 <b>EF DbContext 와 별개</b>로 등록되므로, 그렇게 짜면 검사가 트랜잭션 밖에 남아
    /// 중복가드가 경합에 뚫리고 롤백해도 스냅샷이 어긋난다.
    /// <b>"트랜잭션을 썼다" 는 초록불에 속지 않도록 커넥션 일치까지 본다.</b>
    /// </remarks>
    [Fact]
    public void 전환은_조회도_같은_커넥션에서_해야_한다()
    {
        var body = StripComments(MethodBody(
            Read("src", "HitPan.Application", "Services", "PurchaseService.cs"),
            "public async Task<(string ReturnId, string ReturnNo)> ConvertReceiptToReturnAsync"));

        // 트랜잭션을 여는 메서드 안에서 _db 를 쓰면 그 질의는 트랜잭션 밖이다.
        Assert.DoesNotContain("_db.", body);

        // 중복 검사는 경합을 막기 위해 행 잠금까지 걸어야 한다.
        Assert.Contains("FOR UPDATE", body);
    }

    // ── W4 — 전표 일자 ──

    /// <summary>
    /// 매입 전표 일자가 UTC 면 한국 오전 9시 이전 건이 전부 어제로 기록된다.
    /// 🔴 <b>한 자리만 고치면 안 된다</b> — 경로마다 날짜가 갈려 채번이 쪼개진다.
    /// </summary>
    [Fact]
    public void 매입_전표일자는_UTC를_쓰면_안된다()
    {
        var src = Read("src", "HitPan.Application", "Services", "PurchaseService.cs");

        Assert.DoesNotContain("DateTime.UtcNow.Date", src);
        Assert.Contains("BusinessDate.Today", src);
    }

    // ── W6 · 헌법 #15 — 침묵 삼킴 ──

    /// <summary>실패를 빈 목록으로 위장하면 고장이 "0건"으로 보여 원인 규명이 불가능해진다.</summary>
    [Fact]
    public void 반품목록_조회는_실패를_삼키면_안된다()
    {
        var body = StripComments(MethodBody(
            Read("src", "HitPan.Web", "Services", "DeliveryService.cs"),
            "GetPurchaseReturnListAsync("));

        Assert.DoesNotContain("catch { return new(); }", body);
        Assert.Contains("Console.Error.WriteLine", body);
    }

    // ── 사원별 현황 ──

    /// <summary>
    /// 사원별 반품현황은 반품을 <b>실제로 작성한 사람</b>으로 집계해야 한다.
    /// 🔴 종전엔 "created_by 컬럼 없음" 이라는 <b>거짓 주석</b> 위에 우회 조인이 서 있었다.
    /// </summary>
    [Fact]
    public void 사원별_반품현황은_반품작성자로_집계해야_한다()
    {
        var src = Read("src", "HitPan.Application", "Services", "ReportService.cs");

        // 🔴 낱말 하나로 찾으면 안 된다 — RT_BY_EMPLOYEE 는 switch 분기에도 나온다.
        //   상수 "선언" 자리를 집어야 SQL 본문을 본다.
        var start = src.IndexOf("string RT_BY_EMPLOYEE", StringComparison.Ordinal);
        Assert.True(start >= 0, "RT_BY_EMPLOYEE 선언이 있어야 한다");

        var end = src.IndexOf("\"\"\";", start, StringComparison.Ordinal);
        Assert.True(end > start, "SQL 끝을 찾아야 한다");
        var sql = src.Substring(start, end - start);

        Assert.Contains("rt.created_by", sql);
        // 우회 조인이 남아 있으면 receipt_id 없는 반품이 '미지정' 으로 뭉친다.
        Assert.DoesNotContain("purchase_receipts pr", sql);
    }

    // ── 헌법 #2 — 테넌트 격리 ──

    /// <summary>매입반품 목록의 partners 조인에 tenant_id 가 있어야 한다 (거래처명 교차 노출 차단).</summary>
    [Fact]
    public void 반품목록_거래처조인은_테넌트를_걸어야_한다()
    {
        var body = MethodBody(
            Read("src", "HitPan.Application", "Services", "PurchaseService.cs"),
            "public async Task<List<PurchaseReturnListDto>> GetReturnsAsync");

        Assert.Contains("LEFT JOIN partners p ON p.partner_id = r.partner_id AND p.tenant_id = r.tenant_id", body);
    }
}
