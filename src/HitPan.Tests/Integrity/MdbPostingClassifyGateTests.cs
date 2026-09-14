using System.Data;
using HitPan.Application.Services;
using Xunit;
using M = HitPan.Application.Services.LegacyMdbMapping;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>MdbPostingClassifyGate (G1)</b> — 20260915작1 갈래 A · 자료이관 머리 없는 명세서 줄 분류.
/// </summary>
/// <remarks>
/// <para>
/// 진실원: 설계 <c>docs/설계/erp/20260915_설계_자료이관_머리없는줄_분류봉합.md</c> §14·§16·§17 · 작업지시서 §11-3 G1 · §13 R5-2.
/// 이 게이트는 <see cref="LegacyMdbMapping"/> 순수 함수를 <b>값을 넣어 실제로 부른다</b>(DB 없음 · SKIP 없음).
/// </para>
/// <para>
/// 🔴 함정(작업지시서 §11-3): ① 수금 줄만 같은 키 → 미반영(종류 안 가리면 FAIL) · ② 다른 거래처 같은 키 → 반영(거래처 키면 FAIL) ·
/// ③ 머리 O·연결 X → 반영 · ④ DOCFE 없음 → 전부 반영 · ⑤ F3 를 「마지막 달 이월만」/F1 로 바꾸면 FAIL · ⑥ 00000000 → 기준일 ·
/// ⑦ 대소문자·뒤 공백만 다른 품목 = 같은 키.
/// </para>
/// <para>
/// 실물 대조(MDB 사본 · 같은 함수 실행 · 개발명세서 §3): 판매 미반영 353묶음 414줄 · 매입 210묶음 235줄 ·
/// F3 미수 142,062,113(1,059곳) / 미지급 51,577,479(530곳) · 기준일 2026-02-28 — 설계 기대값과 차 0.
/// ⚠️ 이 게이트는 판정만 잰다. 서비스가 이 함수를 그 자리에서 부르는지는 갈래 B(G2)·C(G6)·E(G4) 몫이다.
/// </para>
/// </remarks>
public sealed class MdbPostingClassifyGateTests
{
    private static readonly HashSet<M.LegacyDocKey> SomeHeaders = new()
    {
        M.LegacyDocKey.Of("20250101", "2", 1, 100),
    };

    private static M.LegacyPartnerLedgerRow Row(int buy, string ymd, string gu, decimal bal, decimal suk, int ssun = 0)
        => new(buy, ymd, gu, bal, suk, ssun);

    // ─────────────── ① 종류 맞춤 ───────────────

    /// <summary>
    /// ① 같은 (날짜·순번)에 <b>수금 줄(S_GU 2)만</b> 있으면 판매 명세서와 연결이 아니다 → 미반영.
    /// <para>무력화: <c>BuildLedgerLinkKeySets</c> 가 S_GU 를 안 가리고 모든 이월 아닌 줄을 넣으면 빨간불(실측 판매 123 · 매입 109묶음 오판).</para>
    /// </summary>
    [Fact]
    public void G1a_수금줄만_같은키면_미반영()
    {
        var (sales, purchase) = M.BuildLedgerLinkKeySets(new[] { Row(100, "20250310", "2", 0, 11_000, ssun: 27) });
        var doc = M.LegacyDocKey.Of("20250310", "2", 27, 100);
        Assert.Equal(M.LegacyPostingStatus.Unposted, M.ClassifyPosting(doc, SomeHeaders, sales, purchase));
    }

    /// <summary>① 매입 발생 줄(S_GU A)은 판매 명세서를, 판매 발생 줄(S_GU 0)은 매입 명세서를 살리지 못한다.</summary>
    [Fact]
    public void G1b_종류가_다른_발생줄은_연결이_아니다()
    {
        var (sales, purchase) = M.BuildLedgerLinkKeySets(new[]
        {
            Row(100, "20250311", "A", 0, 5_000, ssun: 3),
            Row(100, "20250312", "0", 5_000, 0, ssun: 4),
        });
        Assert.Equal(M.LegacyPostingStatus.Unposted, M.ClassifyPosting(M.LegacyDocKey.Of("20250311", "2", 3, 100), SomeHeaders, sales, purchase));
        Assert.Equal(M.LegacyPostingStatus.Unposted, M.ClassifyPosting(M.LegacyDocKey.Of("20250312", "1", 4, 100), SomeHeaders, sales, purchase));
        // 대조군 — 종류가 맞으면 반영
        Assert.Equal(M.LegacyPostingStatus.Posted, M.ClassifyPosting(M.LegacyDocKey.Of("20250311", "1", 3, 100), SomeHeaders, sales, purchase));
        Assert.Equal(M.LegacyPostingStatus.Posted, M.ClassifyPosting(M.LegacyDocKey.Of("20250312", "2", 4, 100), SomeHeaders, sales, purchase));
    }

    /// <summary>이월행(YYYYMM00)은 순번이 같아도 연결 키가 아니다.</summary>
    [Fact]
    public void G1c_이월행은_연결키가_아니다()
    {
        // 순번을 0 이 아닌 5 로 둔다 — 0 이면 병렬이슈36 가드가 먼저 막아 이 함정이 가려진다(무력화 시험 M12 실측).
        var (sales, purchase) = M.BuildLedgerLinkKeySets(new[] { Row(100, "20250300", "0", 9_000, 0, ssun: 5) });
        Assert.DoesNotContain(M.LegacyLedgerLinkKey.Of("20250300", 5), sales!);
        Assert.Equal(M.LegacyPostingStatus.Unposted, M.ClassifyPosting(M.LegacyDocKey.Of("20250300", "2", 5, 100), SomeHeaders, sales, purchase));
    }

    /// <summary>
    /// 🔴 병렬이슈36 — <b>순번 0 은 연결 키가 아니다.</b> 순번 0 판매 명세서 + 같은 날 순번 0 「매출세액」 원장행(S_GU 0 · S_SSUN 0) → 미반영.
    /// (실측 DOCFB IJ_SEQ=0 묶음 381 · DOCF5 판매 S_SSUN=0 메모행 36,725줄 — 이 MDB 는 날짜가 안 겹쳐 오판 0 이지만 다른 고객 MDB 는 붙는다.)
    /// <para>무력화: 집합 쪽 가드(<c>r.SSun == 0</c>) 제거 → 1번 단언 빨간불 · 판정 쪽 가드(<c>doc.Seq != 0</c>) 제거 → 2번 단언 빨간불.</para>
    /// </summary>
    [Fact]
    public void G1n_순번0은_연결키가_아니다()
    {
        var (sales, purchase) = M.BuildLedgerLinkKeySets(new[] { Row(100, "20240505", "0", 1_000, 0, ssun: 0) });
        var doc = M.LegacyDocKey.Of("20240505", "2", 0, 100);
        Assert.DoesNotContain(M.LegacyLedgerLinkKey.Of("20240505", 0), sales!);
        Assert.Equal(M.LegacyPostingStatus.Unposted, M.ClassifyPosting(doc, SomeHeaders, sales, purchase));

        var handMade = new HashSet<M.LegacyLedgerLinkKey> { M.LegacyLedgerLinkKey.Of("20240505", 0), M.LegacyLedgerLinkKey.Of("20240506", 1) };
        Assert.Equal(M.LegacyPostingStatus.Unposted, M.ClassifyPosting(doc, SomeHeaders, handMade, null));
        // 대조군 — 순번 1 은 그대로 연결
        Assert.Equal(M.LegacyPostingStatus.Posted, M.ClassifyPosting(M.LegacyDocKey.Of("20240506", "2", 1, 100), SomeHeaders, handMade, null));
        // 머리표 판정은 순번 0 이어도 그대로
        var headers = new HashSet<M.LegacyDocKey> { doc };
        Assert.Equal(M.LegacyPostingStatus.Posted, M.ClassifyPosting(doc, headers, sales, purchase));
    }

    // ─────────────── ② 거래처 코드 키 금지 ───────────────

    /// <summary>
    /// ② DOCF5 줄의 거래처 코드가 명세서와 달라도 (날짜·순번·종류)가 같으면 반영(선행 ⑥ 2015-05-12 330,000 모양).
    /// <para>무력화: 연결 키에 거래처 코드(S_BUY↔IJ_BUY)를 넣으면 빨간불.</para>
    /// </summary>
    [Fact]
    public void G1d_다른거래처_같은키면_반영()
    {
        var (sales, purchase) = M.BuildLedgerLinkKeySets(new[] { Row(2147438512, "20150512", "0", 330_000, 0, ssun: 9) });
        var doc = M.LegacyDocKey.Of("20150512", "2", 9, 555);
        Assert.Equal(M.LegacyPostingStatus.Posted, M.ClassifyPosting(doc, SomeHeaders, sales, purchase));
    }

    // ─────────────── ③ 머리 O · 연결 X ───────────────

    /// <summary>③ 머리표가 있으면 연결이 없어도 반영(실측 20251111 SEQ5 BUY 2147444473 · 20250310 SEQ27 0원).</summary>
    [Fact]
    public void G1e_머리있고_연결없으면_반영()
    {
        var headers = new HashSet<M.LegacyDocKey> { M.LegacyDocKey.Of("20251111", "2", 5, 2147444473) };
        var (sales, purchase) = M.BuildLedgerLinkKeySets(new[] { Row(1, "20251112", "0", 1, 0, ssun: 1) });
        Assert.Equal(M.LegacyPostingStatus.Posted,
            M.ClassifyPosting(M.LegacyDocKey.Of("20251111 ", "2", 5, 2147444473), headers, sales, purchase));
    }

    /// <summary>
    /// 🔴 모양 규칙 판정 금지 — 조립 코드(2147483500)·단가행 날짜여도 머리표가 있으면 반영, 평범한 모양이어도 둘 다 없으면 미반영.
    /// 이름표(<see cref="LegacyMdbMapping.UnpostedReason"/>)는 표시만 한다.
    /// </summary>
    [Fact]
    public void G1f_모양은_판정을_가르지_않는다()
    {
        var assembly = M.LegacyDocKey.Of("20230301", "1", 1, 2147483500);
        var normal = M.LegacyDocKey.Of("20230302", "2", 1, 12345);
        var headers = new HashSet<M.LegacyDocKey> { assembly };
        Assert.Equal(M.LegacyPostingStatus.Posted, M.ClassifyPosting(assembly, headers, null, null));
        Assert.Equal(M.LegacyPostingStatus.Unposted, M.ClassifyPosting(normal, headers, null, null));

        Assert.Equal("danga", M.UnpostedReason("00000001", 5, 1, 0, 0));
        Assert.Equal("assembly", M.UnpostedReason("20230301", 2147483500, 1, 10, 0));
        Assert.Equal("no_name", M.UnpostedReason("20050101", 0, 0, 0, 0));
        Assert.Equal("test_zero_amount", M.UnpostedReason("20010820", 7, 31001, 0, 0));
        Assert.Equal("test", M.UnpostedReason("20051215", 7, 31002, 200, 0));
        Assert.Equal("other", M.UnpostedReason("20230302", 12345, 1, 0, 0));
    }

    // ─────────────── ④ R5 · R5-2 ───────────────

    /// <summary>
    /// ④ DOCFE 표가 없거나 0행 → 분류하지 않고 <b>전부 반영</b>(Unclassified) — DOCF5 가 있든 없든(R5-2).
    /// <para>무력화: 헤더 집합 null 을 「머리 없음」으로 읽어 Unposted 로 보내면 빨간불(옛 MDB 명세서 전부 버림 · #20).</para>
    /// </summary>
    [Fact]
    public void G1g_DOCFE없으면_전부반영()
    {
        var (sales, purchase) = M.BuildLedgerLinkKeySets(new[] { Row(1, "20250101", "2", 0, 1, ssun: 1) });
        var doc = M.LegacyDocKey.Of("20250101", "2", 1, 1);

        Assert.Equal(M.LegacyPostingStatus.Unclassified, M.ClassifyPosting(doc, null, sales, purchase));
        Assert.Equal(M.LegacyPostingStatus.Unclassified, M.ClassifyPosting(doc, new HashSet<M.LegacyDocKey>(), null, null));

        var emptyDocfe = new DataTable();
        emptyDocfe.Columns.Add("IJA_DT", typeof(string));
        Assert.Null(M.BuildHeaderKeySet(emptyDocfe));
        Assert.Null(M.BuildHeaderKeySet(null));
        Assert.Equal(M.LegacyPostingStatus.Unclassified, M.ClassifyPosting(doc, M.BuildHeaderKeySet(emptyDocfe), sales, purchase));

        Assert.True(M.IsPostedToBooks(headerTableOk: false, hasHeader: false, hasLedgerLink: false));
        Assert.False(M.IsPostedToBooks(headerTableOk: true, hasHeader: false, hasLedgerLink: false));
    }

    /// <summary>DOCFE 있음·DOCF5 없음 → 머리표로만 판정.</summary>
    [Fact]
    public void G1h_DOCF5없으면_머리표로만()
    {
        var (s1, p1) = M.BuildLedgerLinkKeySets((DataTable?)null);
        Assert.Null(s1); Assert.Null(p1);
        var (s2, p2) = M.BuildLedgerLinkKeySets(Array.Empty<M.LegacyPartnerLedgerRow>());
        Assert.Null(s2); Assert.Null(p2);
        Assert.Equal(M.LegacyPostingStatus.Posted, M.ClassifyPosting(M.LegacyDocKey.Of("20250101", "2", 1, 100), SomeHeaders, null, null));
        Assert.Equal(M.LegacyPostingStatus.Unposted, M.ClassifyPosting(M.LegacyDocKey.Of("20250102", "2", 1, 100), SomeHeaders, null, null));
    }

    /// <summary>DataTable 입력(OleDb 형 short·int·decimal · 뒤 공백) 경로도 같은 판정.</summary>
    [Fact]
    public void G1i_DataTable_입력도_같은판정()
    {
        var docfe = new DataTable();
        docfe.Columns.Add("IJA_DT", typeof(string)); docfe.Columns.Add("IJA_IO", typeof(string));
        docfe.Columns.Add("IJA_SEQ", typeof(short)); docfe.Columns.Add("IJA_BUY", typeof(int));
        docfe.Rows.Add("20250101 ", "2", (short)1, 100);

        var docf5 = new DataTable();
        docf5.Columns.Add("S_BUY", typeof(int)); docf5.Columns.Add("S_YMD", typeof(string)); docf5.Columns.Add("S_GU", typeof(string));
        docf5.Columns.Add("S_BAL", typeof(decimal)); docf5.Columns.Add("S_SUK", typeof(decimal)); docf5.Columns.Add("S_SSUN", typeof(short));
        docf5.Rows.Add(7, "20250105", "A", 0m, 3_000m, (short)2);
        docf5.Rows.Add(7, "20250106", "3", 0m, 3_000m, (short)3);

        var headers = M.BuildHeaderKeySet(docfe);
        var (sales, purchase) = M.BuildLedgerLinkKeySets(docf5);
        Assert.Equal(M.LegacyPostingStatus.Posted, M.ClassifyPosting(M.LegacyDocKey.Of("20250101", "2", 1, 100), headers, sales, purchase));
        Assert.Equal(M.LegacyPostingStatus.Posted, M.ClassifyPosting(M.LegacyDocKey.Of("20250105", "1", 2, 999), headers, sales, purchase));
        Assert.Equal(M.LegacyPostingStatus.Unposted, M.ClassifyPosting(M.LegacyDocKey.Of("20250106", "2", 3, 7), headers, sales, purchase));
        // 매입 A 3,000(−) + 수금 3 3,000(−) = −6,000 (DataTable 값 읽기 확인용 · 부호는 식 그대로)
        Assert.Equal(-6_000m, M.PartnerLegacyBalances(M.ReadPartnerLedgerRows(docf5))[7]);
    }

    // ─────────────── ⑤ F3 ───────────────

    /// <summary>
    /// ⑤ F3 = 거래처별 <b>마지막 이월행 S_BAL + 그 뒤 이월 아닌 줄 Σ(S_BAL−S_SUK)</b>.
    /// <list type="bullet">
    ///   <item>거래처 100: 이월 202601 1,000 · 202602 1,500 · 그 앞 1월 줄 500 · 그 뒤 2월 판매 300 · 수금 200 → <b>1,600</b>
    ///   (「2월 이월만」 = 1,500 · F1 전 줄 합 = 600 → 둘 다 빨간불).</item>
    ///   <item>거래처 200: 마지막 이월이 202512(2월 이월 없음) 700 · 그 뒤 1월 판매 100 → <b>800</b> (「마지막 달(2026-02) 이월」 식이면 0/100).</item>
    ///   <item>거래처 300: 이월 없음 · 매입 A 5,000 · 지급 C 2,000 → <b>−3,000</b> (미지급).</item>
    /// </list>
    /// </summary>
    [Fact]
    public void G1j_F3_마지막이월_더하기_그뒤줄()
    {
        var rows = new[]
        {
            Row(100, "20260100", "0", 1_000, 0),
            Row(100, "20260115", "0", 500, 0, 1),
            Row(100, "20260200", "0", 1_500, 0),
            Row(100, "20260205", "0", 300, 0, 2),
            Row(100, "20260210", "2", 0, 200, 3),
            Row(200, "20251200", "0", 700, 0),
            Row(200, "20260107", "0", 100, 0, 1),
            Row(300, "20260111", "A", 0, 5_000, 1),
            Row(300, "20260112", "C", 2_000, 0, 2),
        };
        var bal = M.PartnerLegacyBalances(rows);

        Assert.Equal(1_600m, bal[100]);
        Assert.Equal(800m, bal[200]);
        Assert.Equal(-3_000m, bal[300]);

        var sum = M.SummarizeBalances(bal);
        Assert.Equal((2_400m, 2, 3_000m, 1), sum);
    }

    /// <summary>F3 는 입력 순서와 무관하다(뒤섞어도 같은 값).</summary>
    [Fact]
    public void G1k_F3_입력순서_무관()
    {
        var rows = new[]
        {
            Row(100, "20260205", "0", 300, 0, 2),
            Row(100, "20260200", "0", 1_500, 0),
            Row(100, "20260115", "0", 500, 0, 1),
            Row(100, "20260100", "0", 1_000, 0),
        };
        Assert.Equal(1_800m, M.PartnerLegacyBalances(rows)[100]);
    }

    // ─────────────── ⑥ 기준일 ───────────────

    /// <summary>⑥ 기준일 = DOCFC 마지막 달 말일 → DOCF5 마지막 이월 달 말일 → DOCFB 최대 유효 날짜 · 00000000/00000001 → 기준일.</summary>
    [Fact]
    public void G1l_기준일_폴백과_잘못된날짜()
    {
        Assert.Equal(new DateTime(2026, 2, 28), M.LegacyBaseDate(new[] { "202511", "202602 " }, new[] { "20261200" }, new[] { "20271231" }));
        Assert.Equal(new DateTime(2024, 2, 29), M.LegacyBaseDate(null, new[] { "20231100", "20240200", "20240215" }, new[] { "20271231" }));
        Assert.Equal(new DateTime(2025, 3, 10), M.LegacyBaseDate(Array.Empty<string?>(), null, new[] { "20250310", "00000000", "00000001", "" }));
        Assert.Null(M.LegacyBaseDate(null, null, new[] { "00000000" }));

        var baseDate = new DateTime(2026, 2, 28);
        Assert.Equal(baseDate, M.ResolveLegacyDate("00000000", baseDate));
        Assert.Equal(baseDate, M.ResolveLegacyDate("00000001", baseDate));
        Assert.Equal(baseDate, M.ResolveLegacyDate(null, baseDate));
        Assert.Equal(new DateTime(2025, 3, 10), M.ResolveLegacyDate("20250310", baseDate));

        var docfc = new DataTable(); docfc.Columns.Add("IM_YM", typeof(string)); docfc.Rows.Add("202602");
        Assert.Equal(baseDate, M.LegacyBaseDate(docfc, null, null));
    }

    // ─────────────── ⑦ 재고 품목 키 ───────────────

    /// <summary>
    /// ⑦ 대소문자·앞뒤 공백만 다른 품목 = 같은 키(실측 구분 키면 1,199품목 · 55,253,974.4 로 틀어짐 → 1,192 · 54,753,974.4).
    /// 규격 칸과 창고 칸은 섞이지 않는다.
    /// </summary>
    [Fact]
    public void G1m_품목키_대소문자_뒤공백_무시()
    {
        Assert.Equal(M.StockItemKey("HTP21C ", "표준형-l", ""), M.StockItemKey(" htp21c", "표준형-L  ", null));
        Assert.Equal(M.StockItemKey("Mouse", "ea", "a창고 "), M.StockItemKey("MOUSE", "EA", "A창고"));
        Assert.NotEqual(M.StockItemKey("A", "B", ""), M.StockItemKey("A", "", "B"));
        Assert.NotEqual(M.StockItemKey("A", "B", "W1"), M.StockItemKey("A", "B", "W2"));
        // 기존 대사 키는 그대로(#1)
        Assert.Equal("품명", M.ItemKey("품명", " "));
    }
}
