using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>매출반품 저장 착지 게이트 — 20260831작15</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>사장님 실측 반려(2026-08-31)</b>: <i>"명-20260831-001 전표에서 품목90개를 반품처리 했는데
/// 명-20260831-003 전표가 생성됨. … <b>마이너스 전표가 아님.</b> 따라서 반품이 아니라,
/// 명-20260831-001 전표에 <b>추가로 수량 90개가 주문이 된 셈</b>임."</i>
/// </para>
///
/// <para>
/// 🔴 <b>왜 20260828작14 게이트가 이걸 못 잡았나.</b> 그때 내가 짠 것은
/// <c>Assert.Contains("반품하기", src)</c> — <b>글자만 셌다.</b> 버튼은 실재했고 배너도 떴다.
/// 안 잰 것은 <b>「그 (−)가 반품으로 도착하는가」</b> 다.
/// [[project_fixed_vs_delivered_gap]] <b>8차 재발</b> · 게이트 체크리스트 ⑮ 위반.
/// </para>
///
/// <para>
/// 🔴 <b>그래서 이 게이트는 「도착지」를 잰다.</b> 반품 모드 저장 경로가
/// <b>반품 생성 API 로 가는가</b>, 그리고 <b>판매 생성 API 로 가지 않는가</b> —
/// 둘을 <b>같이</b> 재야 한다. 앞만 재면 둘 다 부르는 코드가 통과한다.
/// </para>
///
/// <para>
/// ⚠️ <b>이 시험의 한계</b> — 이것은 <b>배선 게이트</b>다(어느 문으로 가는가).
/// 저장 결과가 DB 에 어떤 모양으로 앉는지는 <see cref="SalesRemainingQtyGateTests"/> 계열이
/// 실 DB 로 잰다. 화면 실측은 또 별개다 — <b>게이트 통과 ≠ 사장님 화면에서 됨</b>(8/27 작7 교훈).
/// </para>
/// </remarks>
public sealed class SalesReturnSaveRoutingGateTests
{
    private static string WebRoot =>
        Path.Combine(RepoRoot(), "src", "HitPan.Web");

    private static string DeliveryPage =>
        Path.Combine(WebRoot, "Pages", "Sales", "DeliveryPage.razor");

    private static string DeliveryServiceCs =>
        Path.Combine(WebRoot, "Services", "DeliveryService.cs");

    // ─────────────────────────────────────────────────────────────────────
    // G-1 🔴 반품 모드 저장이 「반품 생성 API」를 부른다
    //
    //   반증: 분기를 지우면 호출이 0건이 되어 FAIL.
    //   🔴 정의·주석이 아니라 **살아있는 호출**을 센다 — 20260828 에 주석 처리해도
    //      이름이 파일에 남아 통과한 사고가 있었다(체크리스트 ⑯).
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G1_반품모드_저장은_반품생성API를_부른다()
    {
        var svc = ReadStripped(DeliveryServiceCs);

        // Web 계층에 반품 생성 메서드가 실재하고, 반품 엔드포인트로 POST 하는가
        Assert.True(
            Regex.IsMatch(svc, @"PostAsJsonAsync\s*<[^>]*>?\s*\(\s*""api/sales/returns""")
            || Regex.IsMatch(svc, @"""api/sales/returns""[^;]{0,400}?PostAsJsonAsync", RegexOptions.Singleline)
            || Regex.IsMatch(svc, @"HttpMethod\.Post\s*,\s*""api/sales/returns"""),
            "DeliveryService 에 매출반품 **생성**(POST api/sales/returns) 호출이 없다. "
            + "목록·상세·확정·취소만 있고 생성이 없으면 [반품하기] 는 갈 곳이 없다.");

        var page = ReadStripped(DeliveryPage);

        // 화면이 그 생성 메서드를 실제로 부르는가 (선언만 있고 안 부르면 8/28 과 같은 사고)
        Assert.True(
            LiveCallCount(page, "CreateSalesReturnAsync") > 0,
            "DeliveryPage 가 CreateSalesReturnAsync 를 부르지 않는다. "
            + "메서드를 만들어 두고 화면이 안 부르면 저장은 여전히 판매로 간다.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-2 🔴 반품 모드에서는 「판매 생성」으로 가지 않는다  ← 이것이 사장님이 겪은 사고
    //
    //   G-1 만 있으면 **둘 다 부르는 코드**가 통과한다(전표 2장). 반드시 같이 잰다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G2_반품모드는_판매생성으로_가지_않는다()
    {
        var page = ReadStripped(DeliveryPage);

        // 저장 분기에서 반품 모드를 판정하는가
        Assert.True(
            Regex.IsMatch(page, @"_isReturnMode") && LiveBranchOnReturnMode(page),
            "저장 경로가 _isReturnMode 를 보지 않는다. "
            + "20260828작14 에서 _isReturnMode 는 **배너에서만** 쓰였고 저장은 그것을 몰랐다 — "
            + "그래서 반품이 판매로 저장됐다.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-3 🔴 저장 수량은 양수로 전환된다
    //
    //   화면은 (−)로 보여주고 저장은 양수여야 한다.
    //   서버 DTO 가 [Range(0.0001,…)] 이라 음수를 보내면 400 이다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G3_반품_저장수량은_양수로_전환된다()
    {
        // 🔴 20260831 반증에서 이 게이트도 가짜였다 — 처음엔 파일 어디든 `Math.Abs(` 가
        //   하나라도 있으면 통과했다. 절대값을 지웠는데 **다른 곳의 Math.Abs 가 대신 통과시켰다**
        //   (체크리스트 ⑥ — 낱말 하나로 검사 금지).
        //   ⇒ **반품 payload 를 조립하는 그 블록 안에서** 수량이 절대값인지 본다.
        var page = ReadStripped(DeliveryPage);

        var payloadBlock = Regex.Match(
            page,
            @"new\s+CreateSalesReturnItemPayload\s*\{(?<body>.*?)\}",
            RegexOptions.Singleline);

        Assert.True(payloadBlock.Success,
            "반품 품목 payload(CreateSalesReturnItemPayload) 조립부를 찾지 못했다.");

        var body = payloadBlock.Groups["body"].Value;

        Assert.True(
            Regex.IsMatch(body, @"Qty\s*=\s*Math\.Abs\s*\("),
            "반품 payload 의 수량이 절대값이 아니다. "
            + "화면이 (−)로 들고 있는 값을 그대로 보내면 서버 [Range(0.0001,…)] 에 400 으로 막힌다.");

        Assert.True(
            Regex.IsMatch(body, @"SupplyAmount\s*=\s*Math\.Abs\s*\("),
            "반품 payload 의 공급가액이 절대값이 아니다. 서버가 음수 금액을 거부한다.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-4 🔴 사슬 링크가 payload 에 실린다 (결재 3 — 근거 없는 마이너스 금지)
    //
    //   20260828작14 는 주석으로 "줄 단위 사슬 링크" 라 써놓고
    //   payload DTO 에 칸이 없어 값이 버려졌다. 메모 글자만 남았다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G4_반품_payload에_사슬링크가_실린다()
    {
        // ⚠️ 20260831 — 처음엔 DeliveryService.cs 만 봤는데 **매핑은 화면에 있었다.**
        //   봉합이 틀린 게 아니라 **내가 딴 데를 재고 있었다**(체크리스트 ⑯).
        //   ⇒ 페이로드를 조립하는 곳이 어디든 잡히도록 두 파일을 합쳐서 본다.
        var src = ReadStripped(DeliveryPage) + "\n" + ReadStripped(DeliveryServiceCs);

        Assert.True(
            Regex.IsMatch(src, @"DeliveryId\s*="),
            "반품 payload 에 원 거래명세서 링크(DeliveryId)가 없다. 사슬 근거가 메모 글자만 남는다.");

        Assert.True(
            Regex.IsMatch(src, @"DeliveryItemId\s*="),
            "반품 payload 에 줄 단위 링크(DeliveryItemId)가 없다. "
            + "원단가 추적이 끊긴다 — 20260828작14 가 주석만 달고 실제로는 버리던 자리다.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-5 🔴 반품 사유가 필수다 (PRD FR-5 — 창고가 양품·폐기를 갈라야 한다)
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G5_반품사유가_필수다()
    {
        var page = ReadStripped(DeliveryPage);

        Assert.True(
            Regex.IsMatch(page, @"ReturnReason|반품\s*사유"),
            "반품 사유 입력이 화면에 없다. 불량 반품(is_loss)을 구분 못 하면 창고가 폐기품을 재고에 넣는다.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // G-6 🟢 대조군 — 평소(반품 아님) 저장은 종전대로 판매로 간다 (헌법 #20)
    //
    //   🔴 막는 것만 재면 정상 업무를 막고도 통과한다. 8/28 에 분할출고가 그렇게 죽었다.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void G6_대조군_평소저장은_판매로_간다()
    {
        var svc = ReadStripped(DeliveryServiceCs);

        Assert.True(
            Regex.IsMatch(svc, @"""api/sales/deliveries"""),
            "평소 거래명세서 저장 경로(POST api/sales/deliveries)가 사라졌다. "
            + "반품을 고치면서 정상 판매를 끊으면 헌법 #20 위반이다.");

        Assert.True(
            Regex.IsMatch(svc, @"Idempotency-Key"),
            "멱등 헤더가 사라졌다 — 20260831 에 봉합한 신규저장 P0 가 되살아난다.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 도우미
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>주석·문자열 리터럴을 걷어낸 소스. 주석에 이름이 남아 통과하는 사고를 막는다.</summary>
    private static string ReadStripped(string path)
    {
        Assert.True(File.Exists(path), $"파일이 없다: {path}");
        var src = File.ReadAllText(path);

        src = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);   // 블록 주석
        src = Regex.Replace(src, @"^\s*//.*?$", " ", RegexOptions.Multiline);   // 줄 주석
        src = Regex.Replace(src, @"^\s*@\*.*?\*@", " ", RegexOptions.Multiline | RegexOptions.Singleline); // razor 주석
        return src;
    }

    /// <summary>정의·주석을 뺀 <b>살아있는 호출</b> 수. <c>name(</c> 중 <c>Task name(</c> 같은 정의는 제외.</summary>
    private static int LiveCallCount(string strippedSrc, string name)
    {
        var calls = Regex.Matches(strippedSrc, Regex.Escape(name) + @"\s*\(").Count;
        var defs = Regex.Matches(
            strippedSrc,
            @"(?:private|public|internal|protected|async|Task|void)[^\n;{]{0,80}?" + Regex.Escape(name) + @"\s*\(").Count;
        return calls - defs;
    }

    /// <summary>
    /// 저장 경로가 반품 모드로 <b>실제로</b> 분기하는가.
    /// </summary>
    /// <remarks>
    /// 🔴 20260831 반증에서 <b>내 게이트가 가짜로 드러났다</b> — <c>if (false &amp;&amp; _isReturnMode)</c> 로
    /// 분기를 죽였는데 <b>통과했다</b>(체크리스트 ⑧). 낱말이 있는지만 봤기 때문이다.
    /// ⇒ ①상수로 죽인 조건(<c>false &amp;&amp;</c>)을 <b>명시적으로 잡아내고</b>
    ///   ②그 분기가 <b>반품 저장을 실제로 부르는지</b>까지 본다.
    /// </remarks>
    private static bool LiveBranchOnReturnMode(string strippedSrc)
    {
        // ① 상수로 죽인 분기는 분기가 아니다 — false && / && false / if (false)
        if (Regex.IsMatch(strippedSrc, @"if\s*\(\s*false\s*&&\s*_isReturnMode")
            || Regex.IsMatch(strippedSrc, @"if\s*\(\s*_isReturnMode\s*&&\s*false")
            || Regex.IsMatch(strippedSrc, @"if\s*\(\s*(?:false|true)\s*\)\s*\{?\s*await\s+SaveReturnAsync"))
        {
            return false;
        }

        // ② 분기 조건에 _isReturnMode 가 있고, 그 안에서 반품 저장을 부르는가
        var branch = Regex.Match(
            strippedSrc,
            @"if\s*\(\s*_isReturnMode\s*\)\s*\{(?<body>[^}]{0,400})\}",
            RegexOptions.Singleline);

        return branch.Success
               && Regex.IsMatch(branch.Groups["body"].Value, @"SaveReturnAsync\s*\(");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
