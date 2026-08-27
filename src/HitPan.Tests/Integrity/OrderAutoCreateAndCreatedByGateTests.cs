using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(20260825작5) 수주 자동생성 안내 + 전표 작성자 기록.
/// </summary>
/// <remarks>
/// <para>
/// 사장님 오더(2026-08-25):
/// </para>
/// <list type="number">
/// <item><i>"고객사가 수주를 안잡고 바로 거래를 잡을시, 워크플로우 흐름 정합성이 깨지지 않도록
/// 수주서 자동생성. '이 판매거래의 수주서가 없습니다. 수주서는 자동으로 생성됩니다' 라는
/// 메시지 띄우도록 수정."</i></item>
/// <item><i>"목록, 현황, 순위표, 분석에 각 견적서, 수주서, 판매관리 전표 작성 계정사원
/// 기록으로 남고 그리드에 보여주기."</i></item>
/// </list>
/// <para>
/// 🔴 <b>조사로 드러난 것 — 자동생성은 이미 돌고 있었다.</b> 없던 것은 <b>안내</b>였다.
/// 그리고 별개의 사고가 하나 더 있었다: <b>화면이 수주번호를 지어내고 있었다.</b>
/// <c>$"수-{날짜}-001"</c> 을 문자열로 만들어 넣어, 실제 자동생성분이 <c>-004</c> 여도
/// 브레드크럼은 항상 <c>-001</c> 로 보였다. 서버가 수주번호를 내려주지 않아서였다.
/// </para>
/// <para>
/// 🔴 <b>작성자 식별자 모순.</b> 현황·순위표·분석의 사원별 집계는 <c>e.user_id = created_by</c>
/// 로 조인하는데, 견적 코드는 <c>created_by</c> 에 <b>employee_id</b> 를 넣고 있었다.
/// 두 값은 별개 GUID 라 이 조인은 절대 매칭되지 않는다 — 견적이 0행이라 표면화만 안 됐다.
/// 사장님 결재: <b>user_id 로 통일</b>. 이 상태로 나머지 전표에 작성자를 채웠다면
/// 조인이 전부 죽거나 엉뚱한 사람이 떴을 것이다.
/// </para>
/// <para>
/// ⚠️ 과거 전표는 <b>공란</b>이 정답이다 (사장님 결재) — 컬럼이 전부 NULL 허용이라
/// 마이그레이션 없이 신규분부터 쌓인다. 없는 사실을 지어내지 않는다.
/// </para>
/// </remarks>
public class OrderAutoCreateAndCreatedByGateTests
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
                       && !t.StartsWith("///", StringComparison.Ordinal)
                       && !t.StartsWith("@*", StringComparison.Ordinal);
            }));

    private static string SalesService() =>
        CodeLines(Read("src", "HitPan.Application", "Services", "SalesService.cs"));

    private static string DeliveryPage() =>
        CodeLines(Read("src", "HitPan.Web", "Pages", "Sales", "DeliveryPage.razor"));

    // ───────────────────────────────────────────────────────────────
    // 🔴 오더 ③ — 수주 자동생성 안내
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>사장님 문구 그대로 안내하는가.</b> 저장 전에 알려야 사용자가 멈출 기회를 갖는다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>문구가 갱신됐다 (20260827작10 W3).</b> 같은 화면에 사장님 문구가 두 번 내려왔다:
    /// <list type="bullet">
    /// <item>8/25(작5): <i>"이 판매거래의 수주서가 없습니다. 수주서는 자동으로 생성됩니다"</i></item>
    /// <item>8/27(작10): <i>"수주서가 없는 거래입니다. 수주서를 자동 생성합니다"</i> ← <b>현행</b></item>
    /// </list>
    /// <b>나중 지시를 따른다.</b> 이 시험을 지우지 않고 문구만 갱신하는 이유는,
    /// 이 게이트가 지키던 것이 문구가 아니라 <b>"저장 전에 멈출 기회를 준다"</b> 이기 때문이다.
    /// 그 축은 그대로 살린다(아래 취소 검사).
    /// </remarks>
    [Fact]
    public void 수주_없이_저장하면_저장_전에_안내해야_한다()
    {
        var code = DeliveryPage();

        Assert.Contains("수주서가 없는 거래입니다. 수주서를 자동 생성합니다.",
            code, StringComparison.Ordinal);

        // 🔴 이 게이트의 본체 — 알림이 아니라 **확인**이라야 한다.
        //   취소하면 저장이 멈춰야 사용자가 수주를 먼저 만들 수 있다(반자동 원칙).
        Assert.Contains("proceed != true", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>화면이 수주번호를 지어내지 않는가.</b>
    /// <c>$"수-{...}-001"</c> 패턴이 살아 있으면 실제 번호와 무관하게 항상 -001 로 보인다.
    /// </summary>
    [Fact]
    public void 화면은_수주번호를_지어내면_안_된다()
    {
        var code = DeliveryPage();

        Assert.False(code.Contains("-001\";", StringComparison.Ordinal),
            "화면이 수주번호를 문자열로 지어내고 있다. 서버가 준 실제 번호를 써야 한다.");
    }

    /// <summary>
    /// 🔴 <b>서버가 자동 생성한 수주번호를 돌려주는가.</b>
    /// 종전에는 (id, 전표번호) 만 반환해 화면이 진짜 번호를 알 길이 없었다.
    /// </summary>
    [Fact]
    public void 서버는_자동생성_수주번호를_반환해야_한다()
    {
        var iface = CodeLines(Read("src", "HitPan.Application", "Interfaces", "ISalesService.cs"));
        Assert.Contains("AutoCreatedOrderNo", iface, StringComparison.Ordinal);

        var svc = SalesService();
        Assert.Contains("autoCreatedOrderNo = autoOrderNo;", svc, StringComparison.Ordinal);
        Assert.Contains("return (deliveryId, deliveryNo, autoCreatedOrderNo);", svc, StringComparison.Ordinal);

        var controller = CodeLines(Read("src", "HitPan.API", "Controllers", "SalesController.cs"));
        Assert.Contains("autoCreatedOrderNo", controller, StringComparison.Ordinal);
    }

    // ───────────────────────────────────────────────────────────────
    // 🔴 오더 ④ — 전표 작성자 기록
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>네 전표 모두 작성자를 남기는가.</b>
    /// 종전에는 견적만 채웠고(그나마 잘못된 체계), 수주·판매·반품은 아예 비어 있었다.
    /// </summary>
    [Fact]
    public void 판매_전표는_작성자를_기록해야_한다()
    {
        var svc = SalesService();

        // 거래명세서 · 수주(수동/자동) — EF 엔티티 대입
        var assigns = svc.Split("CreatedBy = _currentTenant.UserId").Length - 1;
        Assert.True(assigns >= 3,
            $"거래명세서·수주(수동)·수주(자동) 세 곳에 작성자를 채워야 한다 (현재 {assigns}곳)");

        // 매출반품 — Dapper 원시 SQL
        Assert.Contains("created_at, created_by, updated_at", svc, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>견적이 잘못된 식별자를 넣지 않는가.</b>
    /// <c>created_by</c> 에 employee_id 를 넣으면 사원별 집계 조인이 통째로 죽는다.
    /// </summary>
    [Fact]
    public void 견적_작성자는_user_id_체계여야_한다()
    {
        var code = CodeLines(Read("src", "HitPan.Application", "Services", "QuotationService.cs"));

        Assert.False(code.Contains("CreatedBy = request.EmployeeId", StringComparison.Ordinal),
            "created_by 에 employee_id 를 넣으면 e.user_id = created_by 조인이 절대 매칭되지 않는다.");

        Assert.Contains("CreatedBy = _currentTenant.UserId", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>목록 조회가 작성자 이름을 함께 가져오는가.</b>
    /// 값을 저장해도 조회에서 조인하지 않으면 그리드는 영원히 공란이다.
    /// 조인 기준은 <c>user_id</c> — 사원별 집계가 이미 쓰는 전제와 같아야 한다.
    /// </summary>
    [Fact]
    public void 목록_조회는_작성자를_조인해야_한다()
    {
        var svc = SalesService();
        var joins = svc.Split("ec.user_id = ").Length - 1;
        Assert.True(joins >= 3,
            $"판매·수주·매출반품 목록에 작성자 조인이 있어야 한다 (현재 {joins}곳)");

        var quote = CodeLines(Read("src", "HitPan.Application", "Services", "QuotationService.cs"));
        Assert.Contains("ec.user_id = q.created_by", quote, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>그리드에 작성자 열이 있는가.</b> 사장님 오더의 "그리드에 보여주기".
    /// </summary>
    [Theory]
    [InlineData("QuotationList.razor")]
    [InlineData("SalesOrderList.razor")]
    [InlineData("TransactionList.razor")]
    [InlineData("SalesListDialog.razor")]
    public void 전표_목록_그리드에_작성자_열이_있어야_한다(string page)
    {
        var code = CodeLines(Read("src", "HitPan.Web", "Components", "Sales", page));

        Assert.Contains("작성자", code, StringComparison.Ordinal);
        Assert.Contains("CreatedByName", code, StringComparison.Ordinal);
    }
}
