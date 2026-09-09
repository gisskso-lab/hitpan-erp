using HitPan.Application.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>MdbMigrationV2ReconGate</b> — 작22 (2026-09-09) 갈래 C: 계산서 invoice_no 방향 토큰(G9) · 대사 품목 키(G9-2) + 배선(W14~W16).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 이 게이트를 짰나</b><br/>
/// 1.3.38 이관은 DOCF4 66,631행을 66,610행으로 넣었다 — <b>21쌍</b>이 사라졌다. 원인은 invoice_no 형식에 방향이 없어
/// (TX_NO,TX_SEQ,TX_PDT,TX_REM) 이 같은 매출·매입 짝이 <c>uk_tax_invoices_invoice_no</c> 에서 하나로 합쳐진 것이고,
/// 어느 쪽이 살지는 실행마다 달랐다(e2e direction≠source_id 토큰 14행). 선행검증 20260909검1 §2-4 가 "같은 4키 + TX_IO 중복 그룹 0" 을 실측해
/// <b>방향 토큰 하나면 21쌍이 전부 갈린다</b>는 걸 확인했다. G9 는 그 함수(<see cref="LegacyMdbMapping.TaxInvoiceNo"/>)를 <b>실제로 불러</b> 21쌍 표본으로 본다.
/// </para>
/// <para>
/// 🔴 <b>21쌍 표본은 MDB 실측이다</b> (2026-09-09 PANDATA.DOCF4 읽기 전용 직독 · HAVING COUNT(*)&gt;1 = 21그룹 · 전부 TX_IO 1·2 한 쌍씩 ·
/// TX_seq 전부 0 · TX_PDT 20건 "00000000" + 1건 "20060103" · TX_REM 은 한 글자라 해시8 이 전부 같다). 실측 STATS: TX_NO 길이 8 고정 · MAX(TX_seq) 9 · TX_PDT 8 · 66,631행.
/// </para>
/// <para>
/// 🔴 <b>G9-2 품목 키</b> — 종전 대사 키는 규격이 비면 <c>"품명|"</c> 을 만들었고, 옛 MIG-AUTO 441건은 item_name 에 <c>"품명|규격"</c> 이 통째로 들어 있어
/// 양쪽 키가 어긋났다(선행검증 §2-6). <see cref="LegacyMdbMapping.ItemKey"/> 가 끝의 '|' 를 걷어 두 모양을 같은 키로 만든다. 규칙은 그 한 군데에만 산다.
/// </para>
/// <para>
/// ⚠️ <b>이 시험이 못 하는 것</b> — 이관 후 tax_invoices 가 66,631 인지, 대사표 ⑤⑧⑨ 가 예측값(0 · +1,483,054 · +50,001)과 맞는지는
/// <c>hitpan_e2e</c> 실측(작지서 §5 · G-MF)의 몫이다. W 층은 소스 글자를 읽는다 — <c>if (false &amp;&amp; …)</c> 로 죽여도 통과할 수 있다는 한계를 안다.
/// 그래서 함수 층(G9)이 값을 보고, W 층은 "그 자리에서 부르는가·종전 모양이 남았는가" 만 본다(MappingGate 의 W 층과 같은 이유).
/// </para>
/// </remarks>
public sealed class MdbMigrationV2ReconGateTests
{
    // ────────────────────────────────────────────────────────────────────────────
    //  G9 — TaxInvoiceNo : 방향 토큰이 21쌍을 가른다 · 30자 · 32 초과 throw · S/B 만
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>ComputeSourceHash("tx_rem:" + TX_REM) 앞 8자 — 21쌍 전부 같은 값(TX_REM 이 한 글자로 같다). e2e invoice_no 꼬리에 실재하는 값.</summary>
    private const string Hash8 = "04D2EFE9";

    /// <summary>21쌍 실측 (TX_NO, TX_seq, TX_PDT). 해시8 은 전부 <see cref="Hash8"/>.</summary>
    public static IEnumerable<object[]> TwentyOnePairs()
    {
        const string pdt0 = "00000000";
        foreach (var no in new[]
                 {
                     "20000000", "20010000", "20020000", "20030000", "20040000", "20050000", "20060000",
                     "20070000", "20090000", "20100000", "20110000", "20120000", "20130000", "20140000",
                     "20150000", "20160000", "20170000", "20180000", "20190000", "20200000",
                 })
        {
            yield return new object[] { no, "0", pdt0 };
        }
        yield return new object[] { "20060001", "0", "20060103" };
    }

    /// <summary>
    /// 🔴 G9 — 같은 (TX_NO, SEQ, PDT, 해시) 라도 direction S 와 B 는 <b>다른 invoice_no</b> 가 된다. 새 형식은 30자.
    /// <para>무력화: <c>TaxInvoiceNo</c> 가 <c>direction</c> 을 문자열에 넣지 않게(종전 형식) 바꾸면 21쌍 전부 <c>NotEqual</c> 에서 빨간불.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(TwentyOnePairs))]
    public void G9_같은_4키라도_매출_매입_방향이_다르면_invoice_no_가_갈린다(string txNo, string seq, string pdt)
    {
        var s = LegacyMdbMapping.TaxInvoiceNo("S", txNo, seq, pdt, Hash8);
        var b = LegacyMdbMapping.TaxInvoiceNo("B", txNo, seq, pdt, Hash8);

        Assert.NotEqual(s, b);
        Assert.Equal($"S-{txNo}-{seq}-{pdt}-{Hash8}", s);
        Assert.Equal($"B-{txNo}-{seq}-{pdt}-{Hash8}", b);
        Assert.Equal(30, s.Length);
        Assert.Equal(30, b.Length);
        Assert.True(s.Length <= LegacyMdbMapping.TaxInvoiceNoMaxLength);

        // 종전 형식 {TX_NO}-{SEQ}-{PDT}-{HASH8} 은 새 형식의 꼬리 그대로다 — 재이관 때 옛 행은 source_id 로 잡혀 제자리 정정된다는 관계를 적어 둔다.
        var old = $"{txNo}-{seq}-{pdt}-{Hash8}";
        Assert.EndsWith(old, s);
        Assert.Equal(old.Length + 2, s.Length);
    }

    /// <summary>
    /// 🔴 G9-b — 길이는 SEQ 자릿수만큼만 늘고(실측 MAX 9 → 30자) 컬럼 한계 32 까지는 통과한다.
    /// </summary>
    [Theory]
    [InlineData("0", 30)]
    [InlineData("9", 30)]
    [InlineData("123", 32)]
    public void G9_길이는_seq_자릿수만_늘고_32_까지_허용(string seq, int expectedLen)
        => Assert.Equal(expectedLen, LegacyMdbMapping.TaxInvoiceNo("S", "20000000", seq, "20060103", Hash8).Length);

    /// <summary>
    /// 🔴 G9-c — 33자가 되면 잘라 넣지 않고 throw 한다(잘라 넣으면 UNIQUE 의 뜻이 깨진다 · LedgerSourceId 와 같은 원칙).
    /// <para>무력화: <c>TaxInvoiceNo</c> 의 길이 검사를 지우면 빨간불.</para>
    /// </summary>
    [Fact]
    public void G9_33자가_되면_throw_잘라_넣지_않는다()
        => Assert.Throws<ArgumentException>(() => LegacyMdbMapping.TaxInvoiceNo("S", "20000000", "1234", "20060103", Hash8));

    /// <summary>
    /// 🔴 G9-d — direction 은 <see cref="LegacyMdbMapping.TaxDirection"/> 이 돌려준 "S"/"B" 만 받는다. 원값 1/2 나 빈값을 넣으면 옛 사고 모양이라 throw.
    /// <para>무력화: <c>TaxInvoiceNo</c> 의 direction 검사를 지우면 빨간불.</para>
    /// </summary>
    [Theory]
    [InlineData("X")]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("s")]
    public void G9_direction_은_S_B_만_받는다(string direction)
        => Assert.Throws<ArgumentOutOfRangeException>(() => LegacyMdbMapping.TaxInvoiceNo(direction, "20000000", "0", "20060103", Hash8));

    // ────────────────────────────────────────────────────────────────────────────
    //  G9-2 — ItemKey : 끝의 '|' 를 걷어 옛 MIG-AUTO 등록분과 레거시 키가 같아진다
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 G9-2 — <c>("품명","규격")</c> 과 옛 등록분 모양 <c>("품명|규격", null)</c> 이 같은 키. 규격이 비면 <c>"품명|"</c> 이 아니라 <c>"품명"</c>.
    /// <para>무력화: <c>LegacyMdbMapping.ItemKey</c> 의 <c>.TrimEnd('|')</c> 를 지우면 2·3·5·6번째 케이스가 빨간불.</para>
    /// </summary>
    [Theory]
    [InlineData("품명", "규격", "품명|규격")]
    [InlineData("품명|규격", null, "품명|규격")]
    [InlineData("품명|규격", "", "품명|규격")]
    [InlineData(" 품명 ", " 규격 ", "품명|규격")]
    [InlineData("품명", "", "품명")]
    [InlineData("품명", null, "품명")]
    [InlineData("htp21c", "a4", "HTP21C|A4")]
    public void G9_2_ItemKey_끝의_파이프를_걷어_옛_등록분과_레거시_키가_같다(string name, string? spec, string expected)
        => Assert.Equal(expected, LegacyMdbMapping.ItemKey(name, spec));

    /// <summary>🔴 G9-2b — 걷어내는 건 끝의 '|' 뿐이다. 다른 규격·다른 자리의 '|' 는 합치지 않는다.</summary>
    [Fact]
    public void G9_2_ItemKey_는_다른_규격을_합치지_않는다()
    {
        Assert.NotEqual(LegacyMdbMapping.ItemKey("품명", "규격"), LegacyMdbMapping.ItemKey("품명", "규격2"));
        Assert.NotEqual(LegacyMdbMapping.ItemKey("HTP21C", null), LegacyMdbMapping.ItemKey("HTP21", "C"));
        Assert.Equal(LegacyMdbMapping.ItemKey("품명", "규격"), LegacyMdbMapping.ItemKey("품명|규격", null));
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  W — 배선 : 서비스가 그 자리에서 그 함수를 부르는가 / 종전 코드 모양이 남았는가
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 W14 — 계산서: <c>MigrateTaxInvoicesAsync</c> 가 <c>TaxInvoiceNo</c> 로 invoice_no 를 만들고 종전 <c>$"{txNo}-…</c> 조립이 없다.
    /// <c>BulkCopyTaxInvoicesAsync</c> 의 헤더 UPSERT 가 <c>ORDER BY source_id</c> 와 <c>invoice_no = VALUES(invoice_no)</c> 를 갖는다.
    /// <para>무력화: 호출부를 종전 <c>var invoiceNoUnique = $"{txNo}-{sourceIdSeq}-…</c> 로 되돌리거나 UPSERT 갱신 목록에서 invoice_no 를 빼면 빨간불.</para>
    /// </summary>
    [Fact]
    public void W14_계산서_invoice_no_는_TaxInvoiceNo_로_만들고_UPSERT_가_invoice_no_를_갱신한다()
    {
        var src = ServiceSource("MdbMigrationService.cs");
        var body = MethodBody(src, "MigrateTaxInvoicesAsync");
        Assert.Contains("LegacyMdbMapping.TaxInvoiceNo(", body);
        Assert.Contains("InvoiceNo = invoiceNoUnique,", body);
        Assert.DoesNotContain("var invoiceNoUnique = $\"{txNo}-", body);

        var bulk = MethodBody(src, "BulkCopyTaxInvoicesAsync");
        Assert.Contains("ORDER BY source_id", bulk);
        Assert.Contains("invoice_no = VALUES(invoice_no)", bulk);
    }

    /// <summary>
    /// 🔴 W15 — MIG-AUTO 품목: <c>EnsureMigAutoItemAsync</c> 가 키를 첫 '|' 에서 품명·규격으로 나눠 <c>spec</c> 컬럼에 넣고, item_code 해시는 키 전체다(BOM #78 재사용 유지).
    /// <para>무력화: 종전처럼 <c>Name = itemName</c> 에 키 전체를 넣고 spec 을 빼면 빨간불. 해시 입력을 <c>namePart</c> 로 바꾸면 빨간불.</para>
    /// </summary>
    [Fact]
    public void W15_MIG_AUTO_품목은_품명과_규격을_나눠_등록하고_해시는_키_전체다()
    {
        var body = MethodBody(ServiceSource("MdbMigrationService.cs"), "EnsureMigAutoItemAsync");
        Assert.Contains("legacyKey.IndexOf('|')", body);
        Assert.Contains("Spec = itemSpec", body);
        Assert.Contains("item_code, item_name, spec, item_type", body);
        Assert.Contains("ComputeSourceHash($\"mig-auto:{legacyKey}\")", body);
    }

    /// <summary>
    /// 🔴 W16 — 대사표: 품목 키는 <c>LegacyMdbMapping.ItemKey</c> 한 군데로 위임하고(자체 규칙 없음), ⑤⑧⑨⑩⑪·건수 4항목의 질의 모양이 서비스에 있다.
    /// <para>무력화: 대사 서비스의 <c>ItemKey</c> 를 종전 자체 식으로 되돌리거나, ⑤ 를 DOCFC 최신월로 되돌리면(DOCFB 품목별 GROUP BY 가 사라짐) 빨간불.</para>
    /// </summary>
    [Fact]
    public void W16_대사표는_새_식과_한_군데_품목키를_쓴다()
    {
        var src = ServiceSource("MdbReconciliationService.cs");
        Assert.Contains("=> LegacyMdbMapping.ItemKey(name, spec)", src);
        Assert.DoesNotContain(".Trim()}\".ToUpperInvariant()", src);
        Assert.Contains("GROUP BY IJ_PUM, IJ_KU, IJ_IO", src);
        Assert.Contains("GROUP BY IJA_IO, IJA_BUY", src);
        Assert.Contains("GROUP BY S_BUY, S_GU", src);
        Assert.Contains("emp_no <> 'LEGACY_FALLBACK'", src);
        Assert.Contains("FROM DOCME", src);
        Assert.Contains("FROM hr_reports WHERE tenant_id=@T AND source_type='migration'", src);
        Assert.Contains("FROM DOCF6 WHERE AC_JEN IS NULL OR TRIM(AC_JEN) <> '0'", src);
        Assert.Contains("BuildPartnerDiffs(newNetByPartner,", src);
    }

    // ── 소스 읽기 헬퍼 (MdbMigrationV2MappingGateTests 와 같은 규칙 — 그 파일은 W10 한 줄만 손댈 수 있어 여기 따로 둔다) ──

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

    private static string ServiceSource(string file)
        => StripComments(ReadSource("src", "HitPan.Application", "Services", file));

    /// <summary>메서드 본문 한 덩어리만 잘라낸다 — 정의(<c>&gt; 메서드명(</c>)부터 다음 <c>private</c> 멤버 직전까지(게이트 사고 ⑥ — 낱말 하나로 검사 금지).</summary>
    private static string MethodBody(string source, string methodName)
    {
        var start = source.IndexOf($"> {methodName}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{methodName} 정의가 있어야 한다");
        var end = source.IndexOf("\n    private ", start + methodName.Length, StringComparison.Ordinal);
        if (end < 0) end = source.Length;
        return source.Substring(start, end - start);
    }
}
