using HitPan.Application.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>MdbMigrationV2MappingGate</b> — 레거시 MDB 코드값 판정 7종 + 서비스 배선 (20260904작21 A10).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 이 게이트를 짰나</b><br/>
/// 9/4 선행검증(20260904검2)이 5월 마이그 자산의 전제 5건을 반증했다 —
/// 매출·매입 방향 <b>반대</b>(P0-A) · 재고원장 수량 <b>전부 0</b>(P0-B) · 라인 3갈래 유실(P0-C) ·
/// 회계 음수 3,667행 CHECK 사망(P0-D) · 수금/지급 계열 미분리(P0-E).
/// 그 판정들이 6,099줄 서비스 안에 묻혀 있어 <b>아무도 값을 넣어 보지 못했다.</b>
/// 작21 A0 가 판정을 <see cref="LegacyMdbMapping"/> 순수함수로 뺐고, 이 게이트는 그 함수를 <b>실제로 부른다.</b>
/// </para>
///
/// <para>
/// 🔴 <b>케이스 값은 전부 MDB 실측(선행검증서 §3)에서 뽑았다.</b> 예: <c>LedgerMove("2", -16)</c> 은
/// 매출 음수 라인(IO=2 반품 377행)이고, <c>JournalSides(0, -10)</c> 은 <c>SC_DR&lt;0</c> 3,645행의 모양이다.
/// </para>
///
/// <para>
/// 🔴 <b>두 층으로 나뉜다</b><br/>
/// G1~G7 = <b>판정 함수</b>를 값으로 검사한다. 함수를 되돌리면 빨간불.<br/>
/// W1~W9 = <b>배선</b>을 검사한다 — 서비스가 그 함수를 <i>그 자리에서</i> 부르는지, 종전 코드 모양이 남았는지.
/// 함수가 맞아도 서비스가 안 부르면 고객 화면은 종전 그대로다(8/27 작7: 게이트 9건 통과하고도 반려 —
/// "부르는 것과 따르는 것은 다르다"). 그래서 배선 층이 따로 있다.
/// </para>
///
/// <para>
/// ⚠️ <b>이 시험이 못 하는 것</b> — MDB 실물을 읽어 DB 에 넣는 종단은 못 잰다. 그것은 <c>hitpan_e2e</c> 실측(작업지시서 §5)의 몫이다.
/// 배선 층은 소스 글자를 읽는다 — <c>if (false &amp;&amp; …)</c> 로 죽여도 통과할 수 있다는 한계를 안다. 그래서 종전 코드 모양의 <b>부재</b>도 함께 본다.
/// </para>
/// </remarks>
public sealed class MdbMigrationV2MappingGateTests
{
    // ────────────────────────────────────────────────────────────────────────────
    //  G1 — DeliveryKind : IO=1 매입 · IO=2 매출 (전결1 Q1)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G1 — <b>IO=1 은 매입(purchase), IO=2 는 매출(sales).</b>
    /// 실측: IO=1 거래처 아리엘상사(용지 매입처) · IO=2 거래처 피시아트, 품목 HTP21C 15,714행 — 공영정보는 히트판을 <b>파는</b> 회사다.
    /// <para>무력화: <c>LegacyMdbMapping.DeliveryKind</c> 의 <c>1 =&gt; "purchase"</c> / <c>2 =&gt; "sales"</c> 를 서로 바꾸면 빨간불.
    /// 서비스 쪽을 종전 <c>if (io == 1) // 매출</c> 로 되돌리면 이 테스트가 아니라 <b>W1</b> 이 빨간불.</para>
    /// </summary>
    [Theory]
    [InlineData(1, "purchase")]
    [InlineData(2, "sales")]
    public void G1_DeliveryKind_1은_매입_2는_매출(int ijIo, string expected)
        => Assert.Equal(expected, LegacyMdbMapping.DeliveryKind(ijIo));

    /// <summary>
    /// 🔴 G1-b — 1·2 밖의 값은 조용히 한쪽으로 몰지 않고 throw 한다(실측상 그런 값은 없다).
    /// <para>무력화: <c>DeliveryKind</c> 의 <c>_ =&gt; throw</c> 를 <c>_ =&gt; "sales"</c> 로 바꾸면 빨간불.</para>
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void G1_DeliveryKind_그외는_throw(int ijIo)
        => Assert.Throws<ArgumentOutOfRangeException>(() => LegacyMdbMapping.DeliveryKind(ijIo));

    // ────────────────────────────────────────────────────────────────────────────
    //  G2 — LedgerMove : "1"/"2" 문자 판정 + 음수는 반대 칸 절대값 (P0-B · Q5)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G2 — <b>MDB IJ_IO 는 "1"/"2" 다.</b> "1" 양수 → 입고, "2" 양수 → 출고, <b>음수는 반대 칸에 절대값</b>.
    /// 종전 코드는 <c>"I"/"O"</c> 를 비교해 124,152행 전부 <c>qty_in = qty_out = 0</c> 이었다.
    /// <para>무력화: <c>LegacyMdbMapping.LedgerMove</c> 의 <c>"1" =&gt; qty &gt;= 0</c> 을 종전처럼 <c>io == "I"</c> 로 되돌리면 전 케이스 빨간불.
    /// 음수 처리 <c>Math.Abs</c> 를 빼고 <c>qty</c> 를 그대로 넣으면 <c>("2", -16)</c> 케이스가 빨간불.
    /// 서비스(<c>MigrateStockLedgerAsync</c>)를 종전 <c>QtyIn = io == "I" ? qty : 0m</c> 로 되돌리면 <b>W2</b> 가 빨간불.</para>
    /// </summary>
    [Theory]
    [InlineData("1", 5, "in", 5, 0)]        // 매입 양수 = 입고
    [InlineData("2", 7, "out", 0, 7)]       // 매출 양수 = 출고
    [InlineData("2", -16, "in", 16, 0)]     // 매출 음수(반품) = 입고 절대값 — 실측 IO=2 음수 377행
    [InlineData("1", -3, "out", 0, 3)]      // 매입 음수(반품) = 출고 절대값 — 실측 IO=1 음수 16행
    [InlineData(" 2 ", 4, "out", 0, 4)]     // 앞뒤 공백 방어
    public void G2_LedgerMove_방향과_음수_절대값(string ijIo, decimal qty, string moveType, decimal qtyIn, decimal qtyOut)
    {
        var (mt, qi, qo) = LegacyMdbMapping.LedgerMove(ijIo, qty);
        Assert.Equal(moveType, mt);
        Assert.Equal(qtyIn, qi);
        Assert.Equal(qtyOut, qo);
    }

    /// <summary>
    /// 🔴 G2-b — 종전 코드가 비교하던 <c>"I"/"O"</c> 는 MDB 에 한 행도 없다. 그런 값이 오면 throw.
    /// <para>무력화: <c>LedgerMove</c> 의 <c>_ =&gt; throw</c> 를 <c>_ =&gt; false</c> 로 바꾸면 빨간불.</para>
    /// </summary>
    [Theory]
    [InlineData("I")]
    [InlineData("O")]
    [InlineData("")]
    public void G2_LedgerMove_종전_IO문자는_MDB에_없다(string ijIo)
        => Assert.Throws<ArgumentOutOfRangeException>(() => LegacyMdbMapping.LedgerMove(ijIo, 1m));

    // ────────────────────────────────────────────────────────────────────────────
    //  G3 — LedgerSourceId : 5키 · varchar(36) (D3 · P0-C)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G3 — <c>mb-{DT}-{IO}-{SEQ}-{BUY}-{SUN}</c>. 종전 2키 <c>mig-{DT}-{SEQ}</c> 는 DISTINCT 115,720 ≠ 5키 124,152 ⇒ 8,432행 IGNORE.
    /// <para>무력화: <c>LedgerSourceId</c> 의 보간 문자열에서 <c>-{buy}-{sun}</c> 을 빼면 빨간불.
    /// 서비스를 종전 <c>$"mig-{dtStr}-{GetShort(row, "IJ_SEQ")}"</c> 로 되돌리면 <b>W2</b> 가 빨간불.</para>
    /// </summary>
    [Fact]
    public void G3_LedgerSourceId_5키_형식()
        => Assert.Equal("mb-20260220-1-12-345-6", LegacyMdbMapping.LedgerSourceId("20260220", "1", 12, 345, 6));

    /// <summary>
    /// 🔴 G3-b — 최대치 키도 36자 안에 든다(<c>stock_ledger.source_id varchar(36)</c>). 실측 최대 34자.
    /// <para>무력화: 접두를 <c>mig-docfb-</c> 로 바꾸면(종전 명세서 형식) 36자를 넘어 빨간불.</para>
    /// </summary>
    [Fact]
    public void G3_LedgerSourceId_최대키_36자_이내()
    {
        var id = LegacyMdbMapping.LedgerSourceId("20260220", "2", 99999, int.MaxValue, 999);
        Assert.Equal("mb-20260220-2-99999-2147483647-999", id);
        Assert.True(id.Length <= LegacyMdbMapping.LedgerSourceIdMaxLength, $"{id.Length}자");
    }

    /// <summary>
    /// 🔴 G3-c — 36자를 넘으면 잘라 넣지 않고 throw (잘라 넣으면 멱등키가 깨진다).
    /// <para>무력화: <c>LedgerSourceId</c> 의 길이 검사 <c>if (id.Length &gt; …) throw</c> 를 지우면 빨간불.</para>
    /// </summary>
    [Fact]
    public void G3_LedgerSourceId_36자_초과는_throw()
        => Assert.Throws<ArgumentException>(() => LegacyMdbMapping.LedgerSourceId("20260220", "2", 99999, int.MinValue, 99999));

    // ────────────────────────────────────────────────────────────────────────────
    //  G4 — TaxDirection : "1"→B(매입) · "2"→S(매출) (D2)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G4 — DDL 주석 <c>'S=매출, B=매입 (TX_IO)'</c> 과 맞춘다. TX_IO 가 비면 TX_GU 에 같은 규칙, 그래도 없으면 "S".
    /// 종전 폴백(<c>gu == "2" ? "B" : "S"</c>)은 방향이 반대였고, direction 에는 원값 1/2 가 그대로 들어갔다.
    /// <para>무력화: <c>MapDirection</c> 의 <c>"1" or "B" =&gt; "B"</c> / <c>"2" or "S" =&gt; "S"</c> 를 서로 바꾸면 빨간불.
    /// 서비스(<c>MigrateTaxInvoicesAsync</c>)를 종전 <c>Direction = io</c> 로 되돌리면 <b>W3</b> 이 빨간불.</para>
    /// </summary>
    [Theory]
    [InlineData("2", "", "S")]     // 매출계산서 65,876행
    [InlineData("1", "", "B")]     // 매입계산서 755행
    [InlineData("", "2", "S")]     // TX_IO 빈값 → TX_GU 폴백(같은 규칙)
    [InlineData("", "1", "B")]
    [InlineData("S", "", "S")]     // 이미 S/B 면 통과
    [InlineData("B", "2", "B")]    // TX_IO 가 있으면 TX_GU 는 안 본다
    [InlineData("", "", "S")]      // 아무것도 없으면 S
    [InlineData("b", "", "B")]     // 대소문자 방어
    public void G4_TaxDirection(string txIo, string txGu, string expected)
        => Assert.Equal(expected, LegacyMdbMapping.TaxDirection(txIo, txGu));

    // ────────────────────────────────────────────────────────────────────────────
    //  G5 — JournalSides : SC_CR=차변·SC_DR=대변 + 음수는 반대편 양수 (P0-D · Q4)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G5 — <c>SC_CR</c> = 차변, <c>SC_DR</c> = 대변(진범 #76 swap 유지). <b>음수는 반대편 양수</b>.
    /// 실측: 음수 3,667행(SC_DR 3,645 · SC_CR 22)이 CHECK <c>chk_jl_debit_or_credit</c> 에서 죽었다 = 30차 "86.7%" 의 정체.
    /// <para>무력화: <c>JournalSides</c> 의 <c>else credit += -scCr</c> / <c>else debit += -scDr</c> 를 빼면 <c>(0,-10)</c>·<c>(-5,0)</c> 이 빨간불.
    /// 차·대를 swap 하지 않게(<c>debit = scDr</c>) 바꾸면 <c>(12000,0)</c> 이 빨간불.
    /// 서비스(<c>MigrateJournalAsync</c>)를 종전 <c>var dr = rawCr; var cr = rawDr;</c> 로 되돌리면 <b>W4</b> 가 빨간불.</para>
    /// </summary>
    [Theory]
    [InlineData(0, -10, 10, 0)]        // SC_DR 음수 → 차변 +10 (실측 3,645행 모양)
    [InlineData(-5, 0, 0, 5)]          // SC_CR 음수 → 대변 +5 (실측 22행 모양)
    [InlineData(12000, 0, 12000, 0)]   // SC_CR 양수 = 차변 그대로
    [InlineData(0, 800, 0, 800)]       // SC_DR 양수 = 대변 그대로
    [InlineData(0, 0, 0, 0)]           // 양쪽 0(실측 3행) → (0,0) — 부르는 쪽이 카운트 후 제외
    public void G5_JournalSides(decimal scCr, decimal scDr, decimal debit, decimal credit)
    {
        var (d, c) = LegacyMdbMapping.JournalSides(scCr, scDr);
        Assert.Equal(debit, d);
        Assert.Equal(credit, c);
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  G6 — PartnerLedgerKind : 수금 1~5 · 지급 B~F · skip 0·A·그 외 (P0-E · Q2)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G6 — S_GU 코드표. 0(채권 발생 500,255행)·A(매입 발생) 는 <b>이관하지 않는다</b>. 1~5 수금 · B~F 지급.
    /// 종전 코드는 <c>S_SUK == 0 이면 S_BAL</c> 로 전 코드를 수금에 넣어 지급이 0건이었다.
    /// <para>무력화: <c>PartnerLedgerKind</c> 의 <c>_ =&gt; ("skip", …)</c> 를 <c>("collection", "bank_transfer")</c> 로 바꾸면 "0"·"A" 케이스가 빨간불.
    /// "C" 를 collection 으로 바꾸면 "C" 케이스가 빨간불.
    /// 서비스(<c>MigrateCollectionsAsync</c>)를 종전 <c>if (amount == 0) amount = GetDec(row, "S_BAL")</c> 로 되돌리면 <b>W5</b> 가 빨간불.</para>
    /// </summary>
    [Theory]
    [InlineData("0", "skip", "")]
    [InlineData("A", "skip", "")]
    [InlineData("", "skip", "")]
    [InlineData("1", "collection", "cash")]
    [InlineData("2", "collection", "bank_transfer")]   // 적요 = 계좌번호
    [InlineData("3", "collection", "bank_transfer")]   // 적요 = 전자결제용 충전
    [InlineData("4", "collection", "check")]
    [InlineData("5", "collection", "card")]
    [InlineData("B", "payment", "cash")]
    [InlineData("C", "payment", "bank_transfer")]      // 적요 = 자사 법인계좌
    [InlineData("D", "payment", "check")]
    [InlineData("E", "payment", "card")]
    [InlineData("F", "payment", "cash")]               // 적요 = 잔액정리·조정금액
    [InlineData("c", "payment", "bank_transfer")]      // 대소문자 방어
    public void G6_PartnerLedgerKind(string sGu, string kind, string method)
    {
        var (k, m) = LegacyMdbMapping.PartnerLedgerKind(sGu);
        Assert.Equal(kind, k);
        if (kind != "skip") Assert.Equal(method, m);
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  G7 — CashbookDirection : 0 월계 skip · 1·3 입금 · 2·4 출금 (Q3)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G7 — AC_JEN. 0 = 월계(이월 집계행 311) → skip · 1·3 = 입금 · 2·4 = 출금.
    /// 종전 코드는 <c>isExpense = true</c> 고정이라 입금 2,765행이 전부 지출로 들어갔다.
    /// <para>무력화: <c>CashbookDirection</c> 의 <c>"1" or "3" =&gt; "income"</c> 을 <c>"expense"</c> 로 바꾸면 빨간불.
    /// <c>"0" =&gt; "skip"</c> 을 빼면 "0" 케이스가 빨간불.
    /// 서비스(<c>MigrateCashbookAsync</c>)를 종전 <c>var isExpense = true;</c> 로 되돌리면 <b>W6</b> 이 빨간불.</para>
    /// </summary>
    [Theory]
    [InlineData("0", "skip")]
    [InlineData("1", "income")]
    [InlineData("3", "income")]
    [InlineData("2", "expense")]
    [InlineData("4", "expense")]
    [InlineData("", "expense")]
    [InlineData("9", "expense")]
    public void G7_CashbookDirection(string acJen, string expected)
        => Assert.Equal(expected, LegacyMdbMapping.CashbookDirection(acJen));

    // ────────────────────────────────────────────────────────────────────────────
    //  G8 — 보조 (Q12) : 셋트 memo · BOM 원가/금액 memo
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🟢 G8 — S_SET 1·2·3 만 <c>[셋트:N]</c> 을 붙이고, 그 외(0·공백)는 설명 그대로.
    /// <para>무력화: <c>ItemMemoWithSet</c> 의 <c>set is "1" or "2" or "3"</c> 조건을 빼면 "0" 케이스가 빨간불.</para>
    /// </summary>
    [Theory]
    [InlineData("1", "완제품 설명", "[셋트:1] 완제품 설명")]
    [InlineData("3", "", "[셋트:3]")]
    [InlineData("0", "일반 설명", "일반 설명")]
    [InlineData("", "일반 설명", "일반 설명")]
    public void G8_ItemMemoWithSet(string sSet, string desc, string expected)
        => Assert.Equal(expected, LegacyMdbMapping.ItemMemoWithSet(sSet, desc));

    /// <summary>
    /// 🟢 G8-b — RT_SON/RT_KUM 이 둘 다 0 이면 RT_GU 만, 하나라도 있으면 <c> | 원가 … 금액 …</c> 을 덧붙인다. 200자에서 자른다.
    /// <para>무력화: <c>BomItemMemo</c> 의 <c>rtSon != 0 || rtKum != 0</c> 을 <c>false</c> 로 바꾸면 두 번째 케이스가 빨간불.</para>
    /// </summary>
    [Fact]
    public void G8_BomItemMemo()
    {
        Assert.Equal("구분", LegacyMdbMapping.BomItemMemo("구분", 0, 0));
        Assert.Equal("구분 | 원가 1200 금액 3600", LegacyMdbMapping.BomItemMemo("구분", 1200m, 3600m));
        Assert.Equal("원가 0 금액 50", LegacyMdbMapping.BomItemMemo("", 0, 50m));
        Assert.Equal(LegacyMdbMapping.BomItemMemoMaxLength, LegacyMdbMapping.BomItemMemo(new string('가', 300), 0, 0).Length);
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  W — 배선 : 서비스가 그 자리에서 그 함수를 부르는가 / 종전 코드 모양이 남았는가
    // ────────────────────────────────────────────────────────────────────────────

    private const string ServiceFile = "MdbMigrationService.cs";

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        Assert.True(dir is not null && Directory.Exists(Path.Combine(dir, "src")), "레포 루트를 찾아야 한다");
        return dir!;
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>주석 줄을 걷어낸 코드만 남긴다 — 설명문에 적힌 종전 코드 모양에 걸려 헛통과·헛실패하지 않게.</summary>
    private static string StripComments(string source)
        => string.Join('\n', source.Split('\n').Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("//", StringComparison.Ordinal)
                && !t.StartsWith("--", StringComparison.Ordinal)
                && !t.StartsWith("*", StringComparison.Ordinal);
        }));

    private static string ServiceSource()
        => StripComments(ReadSource("src", "HitPan.Application", "Services", ServiceFile));

    /// <summary>
    /// 메서드 본문 한 덩어리만 잘라낸다 — 시그니처부터 다음 <c>private</c> 멤버 직전까지.
    /// 파일 전체에서 낱말을 세면 정의·배선·주석이 섞여 가짜가 된다(게이트 사고 ⑥). 자리를 좁혀 본다.
    /// </summary>
    private static string MethodBody(string source, string methodName)
    {
        // 🔴 정의만 잡는다 — 호출 자리(`await MigrateXxxAsync(`)를 먼저 잡으면 엉뚱한 구간을 검사한다(첫 실행에서 실제로 그랬다).
        //    이 파일의 정의는 전부 `Task<…> 메서드명(` 꼴이라 `> 메서드명(` 이 정의에만 나온다.
        var start = source.IndexOf($"> {methodName}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{ServiceFile} 에 {methodName} 정의가 있어야 한다");
        var end = source.IndexOf("\n    private ", start + methodName.Length, StringComparison.Ordinal);
        if (end < 0) end = source.Length;
        return source.Substring(start, end - start);
    }

    /// <summary>
    /// 🔴 W1 — 명세서 방향: <c>MigrateDeliveriesAndReceiptsAsync</c> 가 <c>DeliveryKind</c> 를 부르고, 종전 <c>if (io == 1)</c> 분기가 없다.
    /// <para>무력화: 서비스의 <c>var isSales = LegacyMdbMapping.DeliveryKind(io) == "sales";</c> 를 지우고 <c>if (io == 1)</c> 로 되돌리면 빨간불.</para>
    /// </summary>
    [Fact]
    public void W1_명세서_방향은_DeliveryKind_로_정한다()
    {
        var body = MethodBody(ServiceSource(), "MigrateDeliveriesAndReceiptsAsync");
        Assert.Contains("LegacyMdbMapping.DeliveryKind(io)", body);
        Assert.DoesNotContain("if (io == 1)", body);
        Assert.Contains("EnsureLegacyFallbackPartnerAsync(", body);   // A3: 거래처 미등록은 폴백으로 잇는다(종전 continue)
    }

    /// <summary>
    /// 🔴 W2 — 원장 수량·키: <c>MigrateStockLedgerAsync</c> 가 <c>LedgerMove</c>·<c>LedgerSourceId</c> 를 부르고,
    /// 종전 <c>io == "I"</c> 비교와 2키 <c>mig-{dtStr}-{GetShort</c> 가 없다.
    /// <para>무력화: 서비스를 종전 <c>QtyIn = io == "I" ? qty : 0m</c> / <c>$"mig-{dtStr}-{GetShort(row, "IJ_SEQ")}"</c> 로 되돌리면 빨간불.</para>
    /// </summary>
    [Fact]
    public void W2_원장_수량과_키는_LedgerMove_LedgerSourceId_로_정한다()
    {
        var body = MethodBody(ServiceSource(), "MigrateStockLedgerAsync");
        Assert.Contains("LegacyMdbMapping.LedgerMove(io, qty)", body);
        Assert.Contains("LegacyMdbMapping.LedgerSourceId(", body);
        Assert.DoesNotContain("io == \"I\"", body);
        Assert.DoesNotContain("io == \"O\"", body);
        Assert.DoesNotContain("mig-{dtStr}-{GetShort", body);
        Assert.Contains("QtyIn = qtyIn", body);
        Assert.Contains("QtyOut = qtyOut", body);
    }

    /// <summary>
    /// 🔴 W3 — 계산서 방향: <c>MigrateTaxInvoicesAsync</c> 가 <c>TaxDirection</c> 을 부르고 <c>Direction = direction</c> 을 넣는다. 종전 폴백 모양이 없다.
    /// <para>무력화: 서비스를 종전 <c>Direction = io,</c> / <c>io = gu == "2" ? "B" : "S";</c> 로 되돌리면 빨간불.</para>
    /// </summary>
    [Fact]
    public void W3_계산서_방향은_TaxDirection_으로_정한다()
    {
        var body = MethodBody(ServiceSource(), "MigrateTaxInvoicesAsync");
        Assert.Contains("LegacyMdbMapping.TaxDirection(io, GetStr(r, \"TX_GU\"))", body);
        Assert.Contains("Direction = direction,", body);
        Assert.DoesNotContain("Direction = io,", body);
        Assert.DoesNotContain("gu == \"2\" ? \"B\" : \"S\"", body);
    }

    /// <summary>
    /// 🔴 W4 — 회계 차대변: <c>MigrateJournalAsync</c> 가 <c>JournalSides</c> 를 부르고, 종전 <c>var dr = rawCr;</c> 직접 대입이 없다.
    /// <para>무력화: 서비스를 종전 <c>var dr = rawCr; var cr = rawDr;</c> 로 되돌리면 빨간불.</para>
    /// </summary>
    [Fact]
    public void W4_회계_차대변은_JournalSides_로_정한다()
    {
        var body = MethodBody(ServiceSource(), "MigrateJournalAsync");
        Assert.Contains("LegacyMdbMapping.JournalSides(rawCr, rawDr)", body);
        Assert.DoesNotContain("var dr = rawCr;", body);
        Assert.DoesNotContain("var cr = rawDr;", body);
    }

    /// <summary>
    /// 🔴 W5 — 수금/지급 분리: <c>MigrateCollectionsAsync</c> 가 <c>PartnerLedgerKind</c> 를 부르고 종전 <c>S_BAL</c> 대체가 없다.
    /// <c>MigratePaymentsAsync</c> 가 실재하고 <c>payments</c> 에 INSERT IGNORE 하며, <c>MigrateCoreAsync</c> 의 잡 목록에 <c>"payments"</c> 잡이 있다.
    /// <para>무력화: 서비스를 종전 <c>if (amount == 0) amount = GetDec(row, "S_BAL");</c> 로 되돌리거나 <c>"payments"</c> 잡을 목록에서 빼면 빨간불.</para>
    /// </summary>
    [Fact]
    public void W5_수금은_PartnerLedgerKind_로_가르고_지급은_별도_잡이다()
    {
        var src = ServiceSource();
        var coll = MethodBody(src, "MigrateCollectionsAsync");
        Assert.Contains("LegacyMdbMapping.PartnerLedgerKind(gu)", coll);
        Assert.DoesNotContain("amount = GetDec(row, \"S_BAL\")", coll);
        Assert.Contains("GetDec(row, \"S_SUK\")", coll);

        var pay = MethodBody(src, "MigratePaymentsAsync");
        Assert.Contains("LegacyMdbMapping.PartnerLedgerKind(gu)", pay);
        Assert.Contains("INSERT IGNORE INTO payments", pay);
        Assert.Contains("GetDec(row, \"S_BAL\")", pay);
        Assert.Contains("'purchase'", pay);   // ERP 지급 어휘 = 'purchase' (PaymentPage.razor:454) — [3-V] 정정
        Assert.Contains("'migration'", pay);

        var core = MethodBody(src, "MigrateCoreAsync");
        Assert.Contains("RunTableStepAsync(\"payments\"", core);
        Assert.Contains("MigratePaymentsAsync(", core);
    }

    /// <summary>
    /// 🔴 W6 — 현금출납 방향: <c>MigrateCashbookAsync</c> 가 <c>CashbookDirection</c> 을 부르고 종전 <c>isExpense = true</c> 고정이 없다.
    /// <para>무력화: 서비스를 종전 <c>var isExpense = true;</c> 로 되돌리면 빨간불.</para>
    /// </summary>
    [Fact]
    public void W6_현금출납_방향은_CashbookDirection_으로_정한다()
    {
        var body = MethodBody(ServiceSource(), "MigrateCashbookAsync");
        Assert.Contains("LegacyMdbMapping.CashbookDirection(acJen)", body);
        Assert.DoesNotContain("isExpense = true", body);
        Assert.Contains("direction == \"skip\"", body);
    }

    /// <summary>
    /// 🔴 W7 — 창고: <c>"wh-migration"</c>·<c>'WH-MIG'</c> 상수가 파일 어디에도 없고,
    /// <c>EnsureMigrationWarehouseAsync</c> 가 <c>WarehouseLookup.ResolveTenantDefaultWarehouseAsync</c> 로 찾은 뒤 없을 때만 MAIN 을 만든다.
    /// <para>무력화: 서비스를 종전 <c>var defaultWarehouseId = "wh-migration";</c> 로 되돌리면 빨간불.</para>
    /// </summary>
    [Fact]
    public void W7_이관_창고는_테넌트_기본창고다()
    {
        var src = ServiceSource();
        Assert.DoesNotContain("\"wh-migration\"", src);
        Assert.DoesNotContain("'WH-MIG'", src);
        var body = MethodBody(src, "EnsureMigrationWarehouseAsync");
        Assert.Contains("WarehouseLookup.ResolveTenantDefaultWarehouseAsync(", body);
        Assert.Contains("'MAIN'", body);
        var core = MethodBody(src, "MigrateCoreAsync");
        Assert.Contains("defaultWarehouseId = await EnsureMigrationWarehouseAsync(", core);
    }

    /// <summary>
    /// 🔴 W8 — 품목 전수 등록은 <b>병렬 전(1단계)</b> 에 끝난다(헌법 #16 — 공유 딕셔너리를 병렬 중 변경 금지).
    /// <c>MigrateCoreAsync</c> 안에서 <c>RegisterUnlistedDocfbItemsAsync(</c> 호출 위치가 <c>Task.WhenAll</c> 보다 앞이어야 한다.
    /// <para>무력화: 호출을 <c>stock_ledger</c> 잡 안으로 옮기면(= WhenAll 뒤) 빨간불. 호출을 지우면 빨간불.</para>
    /// </summary>
    [Fact]
    public void W8_품목_전수등록은_병렬_전에_끝난다()
    {
        var src = ServiceSource();
        var core = MethodBody(src, "MigrateCoreAsync");
        var call = core.IndexOf("await RegisterUnlistedDocfbItemsAsync(", StringComparison.Ordinal);
        var whenAll = core.IndexOf("Task.WhenAll(", StringComparison.Ordinal);
        Assert.True(call >= 0, "RegisterUnlistedDocfbItemsAsync 를 MigrateCoreAsync 가 불러야 한다");
        Assert.True(whenAll >= 0, "Task.WhenAll 병렬 단계가 있어야 한다");
        Assert.True(call < whenAll, "품목 전수 등록은 Task.WhenAll 보다 앞(1단계)이어야 한다");

        var reg = MethodBody(src, "RegisterUnlistedDocfbItemsAsync");
        Assert.Contains("SELECT DISTINCT IJ_PUM, IJ_KU FROM DOCFB", reg);
        Assert.Contains("EnsureMigAutoItemAsync(", reg);
        Assert.Contains("itemMap[key] = itemId", reg);
    }

    /// <summary>
    /// 🔴 W9 — 결과·DDL: <c>MdbMigrationResult.Payments</c> 가 <c>Total</c> 에 합산되고, DB-118 과 출하 DDL(#36)에
    /// <c>payments.source_type/source_id/migrated_source_hash</c> + <c>uq_payments_source</c> 가 있다.
    /// <para>무력화: <c>Total</c> 식에서 <c>+ Payments</c> 를 빼거나, DB-118 을 지우거나, clean DDL 의 컬럼을 빼면 빨간불.</para>
    /// </summary>
    [Fact]
    public void W9_결과_Payments_합산_및_DB118_출하DDL_편입()
    {
        var src = ServiceSource();
        var total = src.Substring(src.IndexOf("public int Total =>", StringComparison.Ordinal));
        total = total.Substring(0, total.IndexOf(';'));
        Assert.Contains("+ Payments", total);
        Assert.Contains("public int Payments { get; set; }", src);

        var ddl = ReadSource("src", "HitPan.API", "Migrations", "SQL", "DB-118_payments_source.sql");
        foreach (var col in new[] { "source_type", "source_id", "migrated_source_hash", "uq_payments_source" })
            Assert.Contains(col, ddl);
        Assert.Contains("information_schema", ddl);   // 멱등 관용구

        var clean = ReadSource("installer", "hitpan_db_clean.sql");
        var start = clean.IndexOf("CREATE TABLE `payments`", StringComparison.Ordinal);
        Assert.True(start >= 0, "출하 DDL 에 payments 가 있어야 한다");
        var table = clean.Substring(start, clean.IndexOf("ENGINE=", start, StringComparison.Ordinal) - start);
        Assert.Contains("`source_type` varchar(30)", table);
        Assert.Contains("`source_id` varchar(80)", table);
        Assert.Contains("`migrated_source_hash` char(64)", table);
        Assert.Contains("UNIQUE KEY `uq_payments_source` (`tenant_id`,`source_type`,`source_id`)", table);
    }

    /// <summary>
    /// 🔴 W10 — A0 시그니처 고정. 갈래 B(대사표)와 이 게이트가 같은 시그니처를 부른다 — 바뀌면 둘 다 깨진다.
    /// <para>무력화: <c>LegacyMdbMapping</c> 의 아무 함수 하나를 rename 하거나 매개변수 타입을 바꾸면 빨간불.</para>
    /// </summary>
    /// <summary>
    /// 🔴 W11 — 명세서 헤더 <c>total_amount</c> 는 <b>공급가 합계</b>다 (ERP 어휘: <c>SalesService:68</c> <c>TotalAmount = Items.Sum(SupplyAmount)</c>,
    /// <c>FinanceService</c> 는 <c>total_amount + vat_amount</c> 로 합계를 만든다). 종전 이관은 <c>supplyTotal + vatTotal</c> 을 넣어 부가세가 두 번 잡혔다(병렬이슈 13).
    /// <para>무력화: <c>TotalAmount = supplyTotal,</c> 을 <c>supplyTotal + vatTotal</c> 로 되돌리면 빨간불.</para>
    /// </summary>
    [Fact]
    public void W11_명세서_total_amount는_공급가_합계다()
    {
        var body = MethodBody(ServiceSource(), "MigrateDeliveriesAndReceiptsAsync");
        Assert.Equal(2, CountOf(body, "TotalAmount = supplyTotal,"));
        Assert.DoesNotContain("TotalAmount = supplyTotal + vatTotal", body);
    }

    /// <summary>
    /// 🔴 W12 — 잡 conn 발급은 <b>재시도 + try 안</b>(병렬이슈 12). 종전엔 try 밖이라 발급 타임아웃 1건이 <c>Task.WhenAll</c> 전체를 죽였다.
    /// <para>무력화: <c>RunTableStepAsync</c> 가 <c>_migrationFactory.CreateOpenAsync(</c> 를 try 밖에서 직접 부르게 되돌리면 빨간불.</para>
    /// </summary>
    [Fact]
    public void W12_잡_conn_발급은_재시도하고_실패는_잡_단위로_격리된다()
    {
        var src = ServiceSource();
        var start = src.IndexOf("Task RunTableStepAsync(", StringComparison.Ordinal);
        Assert.True(start >= 0, "RunTableStepAsync 정의가 있어야 한다");
        var end = src.IndexOf("\n    private ", start + 10, StringComparison.Ordinal);
        var step = src.Substring(start, end - start);
        Assert.Contains("OpenJobConnectionWithRetryAsync(tableName, ct)", step);
        Assert.DoesNotContain("_migrationFactory.CreateOpenAsync(", step);
        var tryMatch = System.Text.RegularExpressions.Regex.Match(step, @"\btry\s*\{");
        var openIdx = step.IndexOf("OpenJobConnectionWithRetryAsync(", StringComparison.Ordinal);
        Assert.True(tryMatch.Success && tryMatch.Index < openIdx, "conn 발급은 try 블록 안에서 해야 한다");
        var retry = MethodBody(src, "OpenJobConnectionWithRetryAsync");
        Assert.Contains("Task.Delay(", retry);
        Assert.Contains("CreateOpenAsync(ct)", retry);
    }

    private static int CountOf(string haystack, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0) { count++; idx += needle.Length; }
        return count;
    }

    [Fact]
    public void W10_LegacyMdbMapping_시그니처_고정()
    {
        var t = typeof(LegacyMdbMapping);
        Assert.NotNull(t.GetMethod("DeliveryKind", new[] { typeof(int) }));
        Assert.NotNull(t.GetMethod("LedgerMove", new[] { typeof(string), typeof(decimal) }));
        Assert.NotNull(t.GetMethod("LedgerSourceId", new[] { typeof(string), typeof(string), typeof(int), typeof(int), typeof(int) }));
        Assert.NotNull(t.GetMethod("TaxDirection", new[] { typeof(string), typeof(string) }));
        Assert.NotNull(t.GetMethod("JournalSides", new[] { typeof(decimal), typeof(decimal) }));
        Assert.NotNull(t.GetMethod("PartnerLedgerKind", new[] { typeof(string) }));
        Assert.NotNull(t.GetMethod("CashbookDirection", new[] { typeof(string) }));
        Assert.Equal(typeof(string), t.GetMethod("DeliveryKind", new[] { typeof(int) })!.ReturnType);
        Assert.Equal(typeof((string, decimal, decimal)), t.GetMethod("LedgerMove", new[] { typeof(string), typeof(decimal) })!.ReturnType);
        Assert.Equal(typeof((decimal, decimal)), t.GetMethod("JournalSides", new[] { typeof(decimal), typeof(decimal) })!.ReturnType);
        Assert.Equal(typeof((string, string)), t.GetMethod("PartnerLedgerKind", new[] { typeof(string) })!.ReturnType);
    }
}
