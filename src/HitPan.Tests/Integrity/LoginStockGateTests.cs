using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(20260825작19) 로그인 업데이트 · 재고관리 게이트.
/// </summary>
/// <remarks>
/// <para>
/// 사장님 오더(2026-08-25): <i>"로그인 화면에 최신업데이트 버튼 상단버튼 삭제"</i> ·
/// <i>"재고현황 : 조회유형 그리도 동작 안함"</i> · <i>"창고관리 : 위치에 카카오맵 zip코드"</i> ·
/// <i>"창고분리 : 자사창고, 위탁창고, 총품목수, 총재고금액 모두 미반영. 그리고 오류"</i> ·
/// <i>"각종 수불부 : 수불부종류 콤보박스가 동일한 정보를 낼 것"</i>.
/// </para>
/// <para>
/// 🔴 <b>이번 차수의 교훈</b> — 창고분리는 <b>같은 뜻을 서로 다른 말로 저장</b>하고 있었다.
/// 창고관리는 한글(자사창고·3PL)로 넣는데 창고분리는 영문(normal·consign)만 알았다.
/// 게다가 <c>wh_type</c> 이 <b>빈 문자열</b>인 행도 실재한다(실측). 낱말을 그대로 비교하면
/// 실재하는 창고가 자사도 위탁도 아닌 것이 되어 KPI 가 0 이 된다.
/// </para>
/// <para>
/// ⚠️ 한계 — 이 시험은 <b>배선이 끊겼는지</b>를 본다. 화면 클릭·DB 왕복은 재현하지 않는다.
/// <b>최종 판정은 사장님 실측이다</b>.
/// </para>
/// </remarks>
public class LoginStockGateTests
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
    /// 🔴 봉합 주석은 대개 <b>고치기 전 코드를 인용</b>한다. 주석까지 세면
    /// 고쳤는데도 옛 코드가 남은 것처럼 보이거나, 반대로 지운 척에 속는다.
    /// 판정은 언제나 <b>코드에만</b> 한다 (작18 에서 이 함정에 두 번 걸렸다).
    /// </remarks>
    private static string StripComments(string source)
    {
        var lines = source.Split('\n')
            .Select(l =>
            {
                var t = l.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal)) return string.Empty;
                if (t.StartsWith("@*", StringComparison.Ordinal)) return string.Empty;
                if (t.StartsWith("///", StringComparison.Ordinal)) return string.Empty;
                if (t.StartsWith("*", StringComparison.Ordinal)) return string.Empty;
                return l;
            });
        return string.Join("\n", lines);
    }

    // ── L1 · 로그인 업데이트 버튼 ──

    /// <summary>상단 업데이트 배너를 지웠다 — 버튼이 둘로 보이던 것을 없앤다.</summary>
    [Fact]
    public void 로그인_상단_업데이트_배너는_없어야_한다()
    {
        var src = StripComments(Read("src", "HitPan.Web", "Pages", "Login.razor"));

        // 배너 고유 문구 — 하단 버튼에는 없는 말이다.
        Assert.DoesNotContain("지금 업데이트", src);
        Assert.DoesNotContain("새 버전(@_latestVersion)이 준비되었습니다", src);
    }

    /// <summary>
    /// 🔴 배너를 지워도 <b>업데이트 경로는 살아 있어야</b> 한다.
    /// 로그인 전 탈출구는 2026-08-11 사장님 지시로 만든 자리다 — 없애면 안 된다.
    /// </summary>
    [Fact]
    public void 로그인_하단_업데이트_버튼은_살아있어야_한다()
    {
        var src = StripComments(Read("src", "HitPan.Web", "Pages", "Login.razor"));

        Assert.Contains("최신버젼업데이트", src);
        Assert.Contains("OnClick=\"ManualUpdateAsync\"", src);
        // 실제 실행 경로까지 이어져 있어야 한다.
        Assert.Contains("StartUpdateAsync", src);
    }

    // ── S1 · 재고현황 조회유형 ──

    /// <summary>조회유형을 바꾸면 다시 조회해야 한다 — 판매·매입에서 이미 고친 병.</summary>
    [Fact]
    public void 재고현황_조회유형은_바꾸면_다시_조회해야_한다()
    {
        var src = StripComments(Read("src", "HitPan.Web", "Pages", "Stock.razor"));

        Assert.Contains("ValueChanged=\"OnViewTypeChangedAsync\"", src);
        // @bind-Value 는 값만 담고 재조회를 안 건다 — 그게 사장님이 본 "동작 안함" 이다.
        Assert.DoesNotContain("@bind-Value=\"_viewType\"", src);
    }

    // ── S3 · 창고분리 ──

    /// <summary>
    /// 🔴 서버는 배열을 준다. 객체로 받으면 `[` 에서 즉시 터진다(사장님이 본 JsonException).
    /// </summary>
    [Fact]
    public void 창고분리는_배열로_역직렬화해야_한다()
    {
        var src = StripComments(Read("src", "HitPan.Web", "Pages", "Stock", "WarehouseSplitPage.razor"));

        Assert.Contains("GetFromJsonAsync<List<WarehouseSplitRow>>", src);
        // 객체 래퍼로 받던 종전 코드면 걸린다.
        Assert.DoesNotContain("GetFromJsonAsync<WarehouseSplitResult>", src);
    }

    /// <summary>
    /// 필드 이름이 서버 DTO 와 같아야 한다 — 형태만 맞추면 <b>표는 뜨는데 칸이 빈다</b>.
    /// </summary>
    [Fact]
    public void 창고분리_행모델은_서버_DTO와_이름이_같아야_한다()
    {
        var src = StripComments(Read("src", "HitPan.Web", "Pages", "Stock", "WarehouseSplitPage.razor"));

        foreach (var f in new[] { "WhCode", "WhName", "WhType", "TotalQty", "TotalValue" })
        {
            Assert.Contains($"public {(f is "TotalQty" or "TotalValue" ? "decimal" : "string")} {f}", src);
        }

        // 서버에 없는 옛 이름이 남아 있으면 그 칸은 영영 빈다.
        Assert.DoesNotContain("public string WarehouseCode", src);
        Assert.DoesNotContain("public decimal StockAmount", src);
    }

    /// <summary>
    /// 🔴 창고 유형을 <b>관용적으로</b> 받아야 한다.
    /// </summary>
    /// <remarks>
    /// 실측: 같은 테넌트에 <c>[]</c>(빈 문자열)와 <c>[자사창고]</c>(한글)가 섞여 있었다.
    /// 영문만 비교하면 <b>둘 다 자사로 안 세어 KPI 가 0</b> 이 된다 —
    /// 사장님 실측 <i>"자사창고, 위탁창고 … 모두 미반영"</i> 이 이것이다.
    /// </remarks>
    [Fact]
    public void 창고분리는_한글_창고유형도_알아들어야_한다()
    {
        var src = StripComments(Read("src", "HitPan.Web", "Pages", "Stock", "WarehouseSplitPage.razor"));

        // 창고관리 화면이 실제로 저장하는 값들 — 이 셋을 모르면 KPI 가 샌다.
        Assert.Contains("\"자사창고\"", src);
        Assert.Contains("\"3PL\"", src);
        Assert.Contains("NormalizeType", src);
    }

    /// <summary>KPI 4개는 서버가 주지 않는다 — 화면이 계산해야 한다.</summary>
    [Fact]
    public void 창고분리_KPI는_화면에서_계산해야_한다()
    {
        var src = StripComments(Read("src", "HitPan.Web", "Pages", "Stock", "WarehouseSplitPage.razor"));

        Assert.Contains("OwnCount =", src);
        Assert.Contains("ConsignCount =", src);
        Assert.Contains("TotalItems =", src);
        Assert.Contains("TotalAmount =", src);
    }

    // ── S2 · 창고 주소 ──

    /// <summary>창고에도 우편번호 찾기가 있어야 한다 (사장님 오더 "업체마스터 참고").</summary>
    [Fact]
    public void 창고관리에_우편번호찾기가_있어야_한다()
    {
        var src = StripComments(Read("src", "HitPan.Web", "Pages", "Stock", "WarehouseManagePage.razor"));

        Assert.Contains("openDaumPostcode", src);
        Assert.Contains("OnAddressSelected", src);
        Assert.Contains("hitpan.openNavi", src);
    }

    /// <summary>
    /// 🔴 <b>수정에도</b> 주소를 실어야 한다 — 작7 의 비대칭 사고를 되풀이하지 않는다.
    /// </summary>
    /// <remarks>
    /// 생성에만 넣고 수정에 빠뜨리면 <b>두 번째 저장에서 주소가 조용히 사라진다.</b>
    /// 서버 UPDATE 와 화면 편집 로드 <b>양쪽</b>을 본다.
    /// </remarks>
    [Fact]
    public void 창고주소는_수정경로에도_실려야_한다()
    {
        var server = StripComments(Read("src", "HitPan.API", "Controllers", "WarehouseController.cs"));
        var idxUpdate = server.IndexOf("UPDATE warehouses", StringComparison.Ordinal);
        Assert.True(idxUpdate >= 0, "창고 수정 SQL 이 있어야 한다");

        var updateSql = server.Substring(idxUpdate, Math.Min(600, server.Length - idxUpdate));
        Assert.Contains("zip_code", updateSql);
        Assert.Contains("address_detail", updateSql);

        // 화면 편집 로드에도 실려야 한다 — 안 실으면 빈 값으로 덮인다.
        var page = StripComments(Read("src", "HitPan.Web", "Pages", "Stock", "WarehouseManagePage.razor"));
        var idxEdit = page.IndexOf("private void OpenEditDialog", StringComparison.Ordinal);
        Assert.True(idxEdit >= 0, "편집 다이얼로그 로드가 있어야 한다");
        var editBody = page.Substring(idxEdit, Math.Min(700, page.Length - idxEdit));
        Assert.Contains("ZipCode = wh.ZipCode", editBody);
    }

    // ── S6 · 수불부 콤보 ──

    /// <summary>세 수불부가 같은 「수불부 종류」 콤보를 가져야 한다 (사장님 오더).</summary>
    [Fact]
    public void 세_수불부가_같은_콤보를_가져야_한다()
    {
        foreach (var path in new[]
                 {
                     new[] { "src", "HitPan.Web", "Pages", "Stock", "StockLedgerPage.razor" },
                     new[] { "src", "HitPan.Web", "Pages", "Items", "ItemLedgerPage.razor" },
                     new[] { "src", "HitPan.Web", "Pages", "Partners", "PartnerLedgerPage.razor" },
                 })
        {
            var src = StripComments(Read(path));
            Assert.Contains("수불부 종류", src);
            Assert.Contains("상품별 수불부", src);
            Assert.Contains("업체별 수불부", src);
        }
    }

    /// <summary>
    /// 🔴 상품별 수불부는 상품명을 <c>item=</c> 로 보내야 한다.
    /// </summary>
    /// <remarks>
    /// 종전엔 <c>partner=</c> 로 보냈다. 서버 item 브랜치는 <c>@Item</c> 만 매칭하므로
    /// <b>필터가 통째로 무시되어 전체가 나왔다.</b> SQL 대조실험으로 확인했다
    /// (partner= → 1건 = 필터 무시 / item= → 0건 = 제대로 걸림).
    /// StockLedgerPage 는 19차에 이미 고쳤는데 <b>ItemLedgerPage 만 안 고쳐져 있었다.</b>
    /// </remarks>
    [Fact]
    public void 상품별_수불부는_item_파라미터로_보내야_한다()
    {
        var src = StripComments(Read("src", "HitPan.Web", "Pages", "Items", "ItemLedgerPage.razor"));

        Assert.Contains("qs.Add($\"item={Uri.EscapeDataString(_partner)}\")", src);
        Assert.DoesNotContain("qs.Add($\"partner={Uri.EscapeDataString(_partner)}\")", src);
    }
}
