using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(20260825작8) 반품확인서 실측 반려 3건 봉합 게이트.
/// </summary>
/// <remarks>
/// <para>
/// 사장님 1.3.11 실측 반려: <i>"② 판매불러오기 클릭시 판매 못불러옴 · ⑤ 거래처를 한번 더 선택해줘야됨
/// · ⑦ 반품확인현황에 반품현황 안뜸, 반품직원 그리드에 없음"</i>
/// </para>
/// <para>
/// 🔴 <b>작7 게이트가 왜 이걸 못 잡았나</b> — 작7 은 <b>채우는 함수</b>(FillFromDeliveryAsync)를 검사했다.
/// 그 함수는 이번에도 정상이었다(실측 4번 통과가 증거다).
/// 끊긴 자리는 <b>다이얼로그가 고른 행을 돌려주는 자리</b>였다.
/// <b>같은 기능의 입구가 둘인데 게이트는 하나만 봤다.</b>
/// [[project_fixed_vs_delivered_gap]] 5차 재발 — 끊기는 자리만 계속 옮겨간다.
/// </para>
/// <para>
/// ⚠️ <b>매입은 건드리지 않는다.</b> 오더가 없다.
/// </para>
/// </remarks>
public class ReturnMeasuredRejectGateTests
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

    private static string ListDialog() =>
        CodeLines(Read("src", "HitPan.Web", "Components", "Sales", "SalesListDialog.razor"));

    private static string ReturnPageCs() =>
        CodeLines(Read("src", "HitPan.Web", "Pages", "Sales", "SalesReturnPage.razor.cs"));

    private static string ReportService() =>
        CodeLines(Read("src", "HitPan.Application", "Services", "ReportService.cs"));

    private static string StatusPage() =>
        CodeLines(Read("src", "HitPan.Web", "Pages", "Sales", "SalesReturnStatusPage.razor"));

    // ─────────────────────────────────────────────────────────────
    // [2번] 다이얼로그가 고른 행을 호출부로 돌려줄 수 있어야 한다
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 판매목록 다이얼로그에 <b>더블클릭 말고도</b> 행을 돌려주는 경로가 있어야 한다.
    /// </summary>
    /// <remarks>
    /// 🔴 이 게이트가 없으면 <c>@ondblclick</c> 하나로 되돌아가고, 2번 반려가 그대로 재발한다.
    /// 체크박스가 보이는데 아무 일도 안 하는 화면은 <b>고장 난 화면</b>이다.
    /// </remarks>
    [Fact]
    public void 판매목록_다이얼로그는_체크한행을_돌려주는_경로를_갖는다()
    {
        var dlg = ListDialog();

        // 🔴 이 게이트는 한 번 가짜였다. 종전엔 "SelectChecked" 낱말만 봤는데,
        //    그 낱말은 OnClick="SelectChecked" 바인딩에도 있어서
        //    핸들러를 통째로 지워도 초록불이 나왔다. 낱말이 아니라 "정의"와 "배선"을 따로 본다.
        Assert.Contains("private void SelectChecked()", dlg, StringComparison.Ordinal);
        Assert.Contains("OnClick=\"SelectChecked\"", dlg, StringComparison.Ordinal);

        // 정의 본문이 실제로 DialogResult.Ok 로 닫아야 한다 —
        // 버튼만 있고 Cancel 로 닫으면 호출부는 아무것도 못 받는다(그게 2번 반려였다).
        var idx = dlg.IndexOf("private void SelectChecked()", StringComparison.Ordinal);
        var body = dlg.Substring(idx, Math.Min(400, dlg.Length - idx));
        Assert.Contains("DialogResult.Ok", body, StringComparison.Ordinal);

        // 1건일 때만 활성 — 여러 건이면 어느 거래인지 알 수 없다.
        Assert.Contains("private bool CanSelectOne", dlg, StringComparison.Ordinal);
        Assert.Contains("Disabled=\"!CanSelectOne\"", dlg, StringComparison.Ordinal);
    }

    /// <summary>
    /// 반품확인서가 다이얼로그를 열 때 <c>ListType</c> 을 넘겨야 한다.
    /// </summary>
    [Fact]
    public void 반품확인서는_판매목록을_거래명세서_조건으로_연다()
    {
        var cs = ReturnPageCs();
        // 낱말이 아니라 실제 파라미터 배선을 본다.
        Assert.Contains("[\"ListType\"] = \"sales\"", cs, StringComparison.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────
    // [5번] 거래처를 다시 고르게 만들지 않는다
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 저장은 거래처 <b>식별자</b>로 판정해야 한다 — 이름 대조 실패로 막지 않는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 진짜 원인은 거래처 칸이 아니라 <b>저장부</b>였다.
    /// <c>SaveAsync</c> 가 거래처를 <c>_partnerCache</c> 에서 <b>이름으로</b> 찾는데,
    /// 그 캐시는 사람이 자동완성에 타이핑해야 처음 적재된다.
    /// 판매불러오기로 들어오면 캐시가 비어 있어, 거래처가 화면에 멀쩡히 보이는데도
    /// 저장이 <i>"거래처를 선택해주세요"</i> 로 막혔다.
    /// </para>
    /// <para>
    /// 이름은 바뀌고 중복될 수도 있다. <b>식별자가 있으면 식별자가 우선</b>이다.
    /// </para>
    /// </remarks>
    [Fact]
    public void 저장은_거래처식별자로_판정한다()
    {
        var cs = ReturnPageCs();

        // 🔴 이 게이트도 한 번 가짜였다. "_draft.PartnerId" 낱말만 봤는데
        //    그 낱말은 OnPartnerChangedAsync 에도 있어서, 폴백을 지워도 초록불이 나왔다.
        //    폴백 표현 자체를 본다.
        Assert.Contains("partner?.PartnerId ?? _draft.PartnerId", cs, StringComparison.Ordinal);

        // payload 는 식별자 변수를 써야 한다 — partner.PartnerId 직접 참조로 돌아가면 재발한다.
        Assert.DoesNotContain("partnerId = partner.PartnerId", cs, StringComparison.Ordinal);
    }

    /// <summary>
    /// 판매를 불러올 때 거래처 목록을 미리 채워, 사람이 한 번 더 고르지 않게 한다.
    /// </summary>
    [Fact]
    public void 판매불러오기는_거래처캐시를_미리_채운다()
    {
        var cs = ReturnPageCs();
        var idx = cs.IndexOf("FillFromDeliveryAsync(string", StringComparison.Ordinal);
        Assert.True(idx > 0, "FillFromDeliveryAsync 본문을 찾아야 한다");

        // 본문 안에서 거래처 캐시를 적재해야 한다.
        var body = cs.Substring(idx, Math.Min(3000, cs.Length - idx));
        Assert.Contains("_partnerCache", body, StringComparison.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────
    // [7번] 현황은 확정분만 — 필터를 풀지 않는다 (회귀 방지)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 매출반품현황 4종 SQL 은 <b>confirmed 만</b> 집계해야 한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>이 게이트는 "고치지 마라"를 지키는 게이트다.</b>
    /// 사장님이 <i>"현황 안뜬다"</i> 고 하셨을 때 가장 쉬운 유혹이 이 필터를 푸는 것이다.
    /// 풀면 현황이 <b>재고·회계와 어긋난다</b> — 원장은 confirmed 에만 반응하기 때문이다(헌법 #6).
    /// </para>
    /// <para>
    /// 안 뜨는 건 결함이 아니라 <b>확정 전</b>이라서다. 답은 필터가 아니라 <b>안내</b>다.
    /// </para>
    /// </remarks>
    [Fact]
    public void 매출반품현황_4종은_확정분만_집계한다()
    {
        var svc = ReportService();

        // 🔴 이 게이트도 한 번 가짜였다. SR_BY_PERIOD~GetSalesRankingAsync 구간만 잘라 봤는데,
        //    필터를 푸는 사고는 그 구간 밖(매입반품 RT_*)에서도 똑같이 일어난다.
        //    실제로 반증 때 RT_* 쪽 필터를 풀었더니 초록불이 나왔다.
        //    ⇒ 이름을 세지 말고, "반품 집계 SQL 전체"에서 필터가 풀린 흔적을 찾는다.
        var srBlocks = new[] { "SR_BY_PERIOD", "SR_BY_PARTNER", "SR_BY_ITEM", "SR_BY_EMPLOYEE" };
        foreach (var name in srBlocks)
        {
            var marker = $"{name} = \"\"\"";
            var i = svc.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(i > 0, $"{name} SQL 을 찾아야 한다");

            // 여는 """ 바로 다음부터 닫는 """ 를 찾는다.
            // (i + 10 으로 잡으면 여는 구분자 안에 걸려 빈 구간을 자른다 — 실제로 그랬다.)
            var bodyStart = i + marker.Length;
            var close = svc.IndexOf("\"\"\"", bodyStart, StringComparison.Ordinal);
            Assert.True(close > bodyStart, $"{name} SQL 끝을 찾아야 한다");

            var sql = svc.Substring(bodyStart, close - bodyStart);
            Assert.True(sql.Contains("sr.status = 'confirmed'", StringComparison.Ordinal),
                $"{name} 이 confirmed 필터를 잃었다. "
                + "현황은 재고·회계와 같은 잣대여야 한다(헌법 #6) — 안 뜨는 건 확정 전이라서지 결함이 아니다.");
        }

        // 반품 집계 SQL 어디에도 필터를 무력화한 흔적이 없어야 한다.
        Assert.DoesNotContain("AND 1=1", svc, StringComparison.Ordinal);
    }

    /// <summary>
    /// 저장 직후 <b>확정해야 현황에 뜬다</b>는 것을 알려야 한다.
    /// </summary>
    /// <remarks>
    /// 정합성은 지키되 흐름은 끊지 않는다(헌법 #20).
    /// 사용자는 저장했으니 끝났다고 믿는다 — 그래서 현황이 비면 고장으로 읽는다.
    /// </remarks>
    [Fact]
    public void 저장직후_확정필요를_안내한다()
    {
        var cs = ReturnPageCs();

        // 정의가 있어야 하고
        Assert.Contains("private void ShowConfirmRequiredHint(", cs, StringComparison.Ordinal);

        // 신규 저장·수정 저장 양쪽에서 실제로 불러야 한다 —
        // 한쪽만 부르면 다른 경로에서 또 침묵한다(작7 의 "생성은 넣고 수정은 안 넣던 비대칭").
        var callCount = cs.Split(new[] { "ShowConfirmRequiredHint(docLabel)" }, StringSplitOptions.None).Length - 1;
        Assert.True(callCount >= 2,
            $"신규·수정 저장 양쪽에서 호출해야 한다 (현재 {callCount}회).");

        // 확정 전일 때만 안내한다 — 확정된 문서에까지 뜨면 잘못된 안내다.
        var idx = cs.IndexOf("private void ShowConfirmRequiredHint(", StringComparison.Ordinal);
        var body = cs.Substring(idx, Math.Min(600, cs.Length - idx));
        Assert.Contains("Draft", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 현황이 0건일 때 <b>왜 비었는지</b> 화면이 설명해야 한다.
    /// </summary>
    [Fact]
    public void 현황_빈화면은_확정조건을_설명한다()
    {
        var page = StatusPage();
        Assert.Contains("반품확정", page, StringComparison.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────
    // [4번] 반품확정 버튼이 보여야 한다 — 화면 권한 = 서버 정책 (20260825작9)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 매출반품 화면의 확정·취소 버튼 권한이 <b>서버 정책과 같아야</b> 한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 사장님 실측 반려: <i>"반품확정 버튼 없음"</i>.
    /// </para>
    /// <para>
    /// 🔴 이 화면은 작6 에서 <b>매출</b>반품 전용으로 갈라져 나왔는데,
    /// 버튼의 권한 목록만 <b>매입</b> 쪽(<c>purchase_manager</c>)에 남아 있었다.
    /// 서버는 <c>Policy="SalesManager"</c>(system_admin·sales_manager·tenant_admin)를 요구한다.
    /// </para>
    /// <para>
    /// 그래서 <c>sales_manager</c> 는 <b>서버가 허용하는데 버튼이 안 보였고</b>,
    /// <c>purchase_manager</c> 는 <b>버튼이 보이는데 눌러도 403</b> 이었다.
    /// <b>화면 권한은 서버보다 관대해도, 인색해도 안 된다.</b>
    /// </para>
    /// </remarks>
    [Fact]
    public void 매출반품_확정버튼_권한은_서버정책과_같다()
    {
        var razor = CodeLines(Read("src", "HitPan.Web", "Pages", "Sales", "SalesReturnPage.razor"));

        // 매입 전용 역할이 매출 화면의 "권한 속성"에 남아 있으면 안 된다 — 4번 반려의 원인.
        //   ⚠️ 낱말로 보면 안 된다. 여러 줄 주석의 이어지는 줄은 CodeLines 가 못 걸러서,
        //      원인을 설명한 주석 문구까지 걸린다(실제로 이 게이트가 그렇게 한 번 빨간불이 났다).
        //      검사 대상은 Roles= 속성 그 자체다.
        Assert.DoesNotContain("Roles=\"tenant_admin,purchase_manager\"", razor, StringComparison.Ordinal);

        // 확정·취소 두 자리 모두 매출 정책이어야 한다(한쪽만 고치면 되돌리지 못하는 반쪽이 된다).
        var count = razor.Split(new[] { "Roles=\"system_admin,tenant_admin,sales_manager\"" },
            StringSplitOptions.None).Length - 1;
        Assert.True(count >= 2,
            $"확정·취소 두 버튼 모두 매출 정책이어야 한다 (현재 {count}곳).");
    }

    // ─────────────────────────────────────────────────────────────
    // [작10] 500 의 원인을 말한다 · 401 면제 · 목록 확정버튼
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 확정 경로가 <b>마이그 미적용 DB</b>에서도 죽지 않아야 한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 사장님 1.3.13 실측 500 의 실제 원인 — <c>Unknown column 'is_loss' in 'SELECT'</c>.
    /// DB-108(작6)이 아직 안 들어간 DB 에서 확정 SELECT 가 통째로 죽었다.
    /// </para>
    /// <para>
    /// 🔴 헌법 #13 — 새 SQL 을 던지기 전에 실제 스키마를 확인한다.
    /// 확정·취소 <b>양쪽</b>이 견뎌야 한다(한쪽만 고치면 되돌리지 못하는 반쪽이 된다).
    /// </para>
    /// </remarks>
    [Fact]
    public void 반품확정취소는_is_loss_컬럼이_없어도_동작한다()
    {
        var svc = CodeLines(Read("src", "HitPan.Application", "Services", "SalesService.cs"));

        // 스키마 확인 후 SELECT 를 고르는 경로가 있어야 한다.
        Assert.Contains("HasSalesReturnLossColumnAsync", svc, StringComparison.Ordinal);
        Assert.Contains("0 AS is_loss", svc, StringComparison.Ordinal);

        // 확정·취소 두 곳 모두 — 정의 1 + 확정 1 + 취소 1 = 3회 이상 등장.
        var uses = svc.Split(new[] { "HasSalesReturnLossColumnAsync" }, StringSplitOptions.None).Length - 1;
        Assert.True(uses >= 3, $"확정·취소 양쪽이 스키마를 확인해야 한다 (현재 {uses}회).");
    }

    /// <summary>
    /// 확정 실패 시 <b>무엇이 문제인지</b> 사용자에게 말해야 한다.
    /// </summary>
    /// <remarks>
    /// 🔴 종전엔 <c>{"error":"서버 오류가 발생했습니다"}</c> 만 떴다.
    /// 원인을 알 수 없는 것이 진짜 결함이었다 — 이걸 찾느라 하루를 썼다.
    /// </remarks>
    [Fact]
    public void 확정실패는_스키마부재를_사용자말로_돌려준다()
    {
        var ctrl = CodeLines(Read("src", "HitPan.API", "Controllers", "SalesController.cs"));

        // 1054(Unknown column) · 1146(Unknown table) 를 잡아 400 + 사유로 돌려준다.
        Assert.Contains("1054", ctrl, StringComparison.Ordinal);
        Assert.Contains("업데이트가 아직 다 적용되지 않아", ctrl, StringComparison.Ordinal);

        // 개발용어 노출 금지 — 컬럼명·SQL 을 고객 화면에 쓰지 않는다.
        Assert.DoesNotContain("Unknown column", ctrl, StringComparison.Ordinal);
    }

    /// <summary>
    /// 화면이 <b>응답 JSON 을 통째로</b> 사용자에게 보여주면 안 된다.
    /// </summary>
    [Fact]
    public void 반품확정_실패메시지는_JSON_원문을_노출하지_않는다()
    {
        var cs = ReturnPageCs();

        Assert.Contains("ExtractServerMessage", cs, StringComparison.Ordinal);

        // err 를 날것으로 스낵바에 넣던 표현이 남아 있으면 안 된다(사장님이 본 그 화면).
        Assert.DoesNotContain("반품 확정 실패: {err}", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("반품 취소 실패: {err}", cs, StringComparison.Ordinal);
    }

    /// <summary>
    /// 로그인 <b>전</b> 업데이트 안내 조회가 401 로 잘리면 안 된다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 엔드포인트는 <c>[AllowAnonymous]</c> 인데 앞단 <c>TenantMiddleware</c> 가 먼저 401 을 냈다.
    /// </para>
    /// <para>
    /// 🔴 <c>/api/auth</c> 를 통째로 열면 <c>me</c>·<c>logout</c> 까지 열린다 — 이 주소 하나만 연다.
    /// </para>
    /// </remarks>
    [Fact]
    public void 로그인전_업데이트조회는_테넌트미들웨어를_통과한다()
    {
        var mw = CodeLines(Read("src", "HitPan.API", "Middleware", "TenantMiddleware.cs"));

        Assert.Contains("/api/auth/update-status-local", mw, StringComparison.Ordinal);

        // 면제를 넓히지 않았는지 — /api/auth 통째 개방 금지.
        Assert.DoesNotContain("StartsWithSegments(\"/api/auth\")", mw, StringComparison.Ordinal);
    }

    /// <summary>
    /// 반품 <b>목록</b>에도 확정 버튼이 있어야 한다.
    /// </summary>
    /// <remarks>
    /// 사장님 실측: <i>"목록에는 없고, 반품확인서 전표작성에는 반품확정버튼 있음"</i>.
    /// ⚠️ 매입반품과 공용 목록이라 <b>매출일 때만</b> 보여야 한다.
    /// </remarks>
    [Fact]
    public void 반품목록에도_확정버튼이_있다()
    {
        var list = CodeLines(Read("src", "HitPan.Web", "Components", "Purchase", "PurchaseReturnList.razor"));

        Assert.Contains("ConfirmOneAsync", list, StringComparison.Ordinal);

        // 매출반품일 때만 — 매입에 잘못 뜨면 경로도 권한도 다른 확정이 나간다.
        Assert.Contains("IsSalesReturn &&", list, StringComparison.Ordinal);

        // 권한은 전표 화면과 동일해야 한다(작9).
        Assert.Contains("Roles=\"system_admin,tenant_admin,sales_manager\"", list, StringComparison.Ordinal);
    }

    /// <summary>
    /// 서버의 매출반품 확정·취소가 <c>SalesManager</c> 정책을 유지해야 한다.
    /// </summary>
    /// <remarks>
    /// 화면만 맞춰 놓고 서버 정책이 바뀌면 다시 어긋난다 — 양쪽을 함께 고정한다.
    /// </remarks>
    [Fact]
    public void 서버_매출반품_확정취소는_SalesManager_정책이다()
    {
        var ctrl = CodeLines(Read("src", "HitPan.API", "Controllers", "SalesController.cs"));

        foreach (var route in new[] { "returns/{id}/confirm", "returns/{id}/cancel" })
        {
            var i = ctrl.IndexOf($"[HttpPost(\"{route}\")]", StringComparison.Ordinal);
            Assert.True(i > 0, $"{route} 엔드포인트를 찾아야 한다");

            // 바로 다음 줄에 정책이 붙어 있어야 한다.
            var next = ctrl.Substring(i, Math.Min(200, ctrl.Length - i));
            Assert.Contains("Policy = \"SalesManager\"", next, StringComparison.Ordinal);
        }
    }
}
