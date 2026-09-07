namespace HitPan.Application.Services;

/// <summary>
/// 🔴 <b>레거시 MDB 코드값 → 히트판 어휘 판정 — 20260904작21 (A0)</b>
///
/// <para>
/// <see cref="MdbMigrationService"/> 6,099줄 안에 흩어져 있던 <b>방향·수량·차대변·수금/지급·입출금 판정</b>을
/// 전부 여기 <b>순수 static 함수</b>로 뺐다. 서비스는 이 함수만 부른다.
/// </para>
///
/// <para>
/// 🔴 <b>왜 뺐나</b><br/>
/// 9/4 선행검증(20260904검2)에서 전제 5건이 반증됐다 — 매출·매입이 <b>반대</b>로 들어가고(P0-A),
/// 재고원장 수량이 <b>전부 0</b>(P0-B: MDB 값은 <c>"1"/"2"</c> 인데 코드는 <c>"I"/"O"</c> 를 비교했다),
/// 회계 음수 3,667행이 CHECK 에 걸려 버려졌고(P0-D), 수금·지급 계열을 안 갈랐다(P0-E).
/// 판정이 서비스 안에 묻혀 있으면 <b>게이트가 실제로 불러 볼 수 없고</b>, 글자검사만 남는다
/// (가짜 게이트 누적 24번). 순수함수로 빼야 시험이 DB 없이 <b>실제 값을 넣고 결과를 본다.</b>
/// </para>
///
/// <para>
/// ⚠️ 이 클래스는 <b>판정만</b> 한다. DB 를 읽지도 쓰지도 않는다. 시그니처는 작업지시서 A0 에 고정돼 있고
/// 게이트(<c>MdbMigrationV2MappingGateTests</c>)와 대사표 서비스(갈래 B)가 같은 시그니처를 부른다 — 바꾸려면 둘 다 본다.
/// </para>
///
/// <para>
/// 근거(전결1): Q1 <c>IO=1 매입 · IO=2 매출 · TX_IO 동일</c> / Q2 <c>S_GU</c> 코드표 / Q3 <c>AC_JEN</c> /
/// Q4 음수는 반대편 양수 / Q5 원장은 반대 칸 절대값 / D3 원장 source_id 5키.
/// </para>
/// </summary>
public static class LegacyMdbMapping
{
    /// <summary>
    /// DOCFB <c>IJ_IO</c> → 거래명세서 종류. <b>1 = 매입(purchase) · 2 = 매출(sales)</b>.
    /// 종전 코드(:4287)는 <c>io == 1 → sales</c> 로 반대였다 — 공영정보는 HTP21C 를 11만 번 <b>판</b> 회사다.
    /// 1·2 외의 값은 실측상 존재하지 않으므로 throw 한다(조용히 한쪽으로 몰지 않는다).
    /// </summary>
    public static string DeliveryKind(int ijIo) => ijIo switch
    {
        1 => "purchase",
        2 => "sales",
        _ => throw new ArgumentOutOfRangeException(nameof(ijIo), ijIo, "DOCFB.IJ_IO 는 1(매입) 또는 2(매출)만 허용한다."),
    };

    /// <summary>
    /// DOCFB <c>IJ_IO</c> + 수량 → 재고원장 <c>move_type / qty_in / qty_out</c>.
    /// <list type="bullet">
    ///   <item>"1"(매입) 양수 → 입고 · "2"(매출) 양수 → 출고</item>
    ///   <item><b>음수는 반대 칸에 절대값</b>(Q5) — 매출 음수(반품) → <c>qty_in</c>, 매입 음수 → <c>qty_out</c>.
    ///   원장은 두 칸 구조라 음수를 넣으면 화면·검사식이 깨진다(8/31 판정과 같은 근거). 순수량은 불변.</item>
    /// </list>
    /// 종전 코드(:1820-1836)는 <c>"I"/"O"</c> 를 비교해 MDB 값 <c>"1"/"2"</c> 에 한 번도 맞지 않았다 ⇒ 전 행 0.
    /// </summary>
    public static (string MoveType, decimal QtyIn, decimal QtyOut) LedgerMove(string ijIo, decimal qty)
    {
        var io = (ijIo ?? string.Empty).Trim();
        var abs = Math.Abs(qty);
        var inbound = io switch
        {
            "1" => qty >= 0,   // 매입: 양수 입고, 음수(반품) 출고
            "2" => qty < 0,    // 매출: 양수 출고, 음수(반품) 입고
            _ => throw new ArgumentOutOfRangeException(nameof(ijIo), ijIo, "DOCFB.IJ_IO 는 \"1\"(매입) 또는 \"2\"(매출)만 허용한다."),
        };
        return inbound ? ("in", abs, 0m) : ("out", 0m, abs);
    }

    /// <summary>재고원장 <c>source_id</c> 컬럼 길이 — <c>stock_ledger.source_id varchar(36)</c>(출하 DDL).</summary>
    public const int LedgerSourceIdMaxLength = 36;

    /// <summary>
    /// 재고원장 <c>source_id</c> = <c>mb-{DT}-{IO}-{SEQ}-{BUY}-{SUN}</c> (D3 · 5키).
    /// 종전 <c>mig-{DT}-{SEQ}</c> 2키는 DISTINCT 115,720 ≠ 5키 124,152 ⇒ 8,432행이 UNIQUE 에 IGNORE 됐다.
    /// 5키 DISTINCT = 전체 행수(실측). 실측 최대 34자. <b>36자를 넘으면 throw</b> — 잘라 넣으면 멱등키가 깨진다.
    /// </summary>
    public static string LedgerSourceId(string dt, string io, int seq, int buy, int sun)
    {
        var id = $"mb-{dt}-{io}-{seq}-{buy}-{sun}";
        if (id.Length > LedgerSourceIdMaxLength)
        {
            throw new ArgumentException(
                $"stock_ledger.source_id 는 {LedgerSourceIdMaxLength}자 이하여야 한다: '{id}' ({id.Length}자)");
        }
        return id;
    }

    /// <summary>
    /// 세금계산서 <c>direction</c> — <b>"1" → "B"(매입) · "2" → "S"(매출)</b>, "S"/"B" 는 통과.
    /// <c>TX_IO</c> 가 비면 <c>TX_GU</c> 에 같은 규칙, 그래도 없으면 "S".
    /// DDL 주석 <c>'S=매출, B=매입 (TX_IO)'</c> 과 맞춘다(D2). 종전 코드는 원값 1/2 를 그대로 넣었고
    /// 빈값 폴백(:3155)은 <c>gu == "2" ? "B" : "S"</c> 로 방향이 반대였다.
    /// </summary>
    public static string TaxDirection(string txIo, string txGu)
        => MapDirection(txIo) ?? MapDirection(txGu) ?? "S";

    private static string? MapDirection(string? raw)
    {
        var v = (raw ?? string.Empty).Trim().ToUpperInvariant();
        return v switch
        {
            "1" or "B" => "B",
            "2" or "S" => "S",
            _ => null,
        };
    }

    /// <summary>
    /// DOCF7 <c>SC_CR / SC_DR</c> → 분개 <c>(차변, 대변)</c>.
    /// 레거시 명명은 뒤집혀 있다 — <b><c>SC_CR</c> = 차변 · <c>SC_DR</c> = 대변</b>(진범 #76 swap 유지).
    /// <b>음수는 반대편 양수</b>(Q4): <c>SC_DR &lt; 0</c> → 차변 +|v| · <c>SC_CR &lt; 0</c> → 대변 +|v|.
    /// 양쪽 0 은 <c>(0, 0)</c> 으로 돌려주고 부르는 쪽이 카운트 후 제외한다.
    /// 종전 코드는 음수를 그대로 던져 <c>chk_jl_debit_or_credit</c> CHECK 에서 3,667행이 죽었다(P0-D).
    /// </summary>
    public static (decimal Debit, decimal Credit) JournalSides(decimal scCr, decimal scDr)
    {
        decimal debit = 0m, credit = 0m;
        if (scCr >= 0) debit += scCr; else credit += -scCr;
        if (scDr >= 0) credit += scDr; else debit += -scDr;
        return (debit, credit);
    }

    /// <summary>
    /// DOCF5 <c>S_GU</c> → 거래처원장 계열 + 수단 (Q2).
    /// <list type="bullet">
    ///   <item><b>수금</b> 1·2·3·4·5 (금액 = <c>S_SUK</c>) → <c>collections</c> · 수단 1 cash · 2 bank_transfer · 3 bank_transfer · 4 check · 5 card</item>
    ///   <item><b>지급</b> B·C·D·E·F (금액 = <c>S_BAL</c>) → <c>payments</c> · 수단 B cash · C bank_transfer · D check · E card · F cash</item>
    ///   <item><b>skip</b> 0(채권 발생 — ERP 는 명세서에서 계산) · A(매입 발생 — purchase_receipts 사본) · 그 외</item>
    /// </list>
    /// 종전 코드(:2153)는 <c>S_SUK == 0 이면 S_BAL</c> 로 전 코드를 수금에 넣어 지급이 0건이었다(P0-E).
    /// </summary>
    public static (string Kind, string Method) PartnerLedgerKind(string sGu)
    {
        var gu = (sGu ?? string.Empty).Trim().ToUpperInvariant();
        return gu switch
        {
            "1" => ("collection", "cash"),
            "2" => ("collection", "bank_transfer"),
            "3" => ("collection", "bank_transfer"),
            "4" => ("collection", "check"),
            "5" => ("collection", "card"),
            "B" => ("payment", "cash"),
            "C" => ("payment", "bank_transfer"),
            "D" => ("payment", "check"),
            "E" => ("payment", "card"),
            "F" => ("payment", "cash"),
            _ => ("skip", string.Empty),
        };
    }

    /// <summary>
    /// DOCF6 <c>AC_JEN</c> → 현금출납 방향 (Q3).
    /// <b>"0" = 월계(이월 집계행) → skip</b> · "1"·"3" = 입금(income) · "2"·"4" = 출금(expense) · 그 외 expense.
    /// 종전 코드(:2377)는 <c>isExpense = true</c> 고정이라 입금 2,765행+은행입금이 전부 지출로 들어갔다.
    /// </summary>
    public static string CashbookDirection(string acJen)
    {
        var jen = (acJen ?? string.Empty).Trim();
        return jen switch
        {
            "0" => "skip",
            "1" or "3" => "income",
            "2" or "4" => "expense",
            _ => "expense",
        };
    }

    // ────────────────────────────────────────────────────────────────
    // 보조 — Q12 (셋트 · BOM 원가/금액) memo 조립. A0 고정 시그니처 7개 밖의 덧붙임.
    // ────────────────────────────────────────────────────────────────

    /// <summary><c>items.memo varchar(500)</c>.</summary>
    public const int ItemMemoMaxLength = 500;

    /// <summary><c>bom_items.memo varchar(200)</c>.</summary>
    public const int BomItemMemoMaxLength = 200;

    /// <summary>
    /// DOCFS <c>S_SET</c> ∈ {1,2,3} 이면 memo 앞에 <c>[셋트:{S_SET}] </c> 를 붙인다 (Q12).
    /// 1·2 = 셋트 완제품 · 3 = 셋트 자재. <c>item_type</c> 은 건드리지 않는다 — ERP 어휘가 화면 필터에 흩어져 있어
    /// 값을 바꾸면 화면이 깨질 수 있다. 손실 0 으로 보존만 한다.
    /// </summary>
    public static string ItemMemoWithSet(string sSet, string? desc)
    {
        var set = (sSet ?? string.Empty).Trim();
        var body = (desc ?? string.Empty).Trim();
        var memo = set is "1" or "2" or "3"
            ? $"[셋트:{set}] {body}".TrimEnd()
            : body;
        return memo.Length > ItemMemoMaxLength ? memo[..ItemMemoMaxLength] : memo;
    }

    /// <summary>
    /// DOCRT → <c>bom_items.memo</c> = <c>RT_GU</c> + (<c>RT_SON</c>/<c>RT_KUM</c> 중 0 아닌 게 있으면 <c> | 원가 {SON} 금액 {KUM}</c>).
    /// ERP <c>bom_items</c> 에 원가·금액 자리가 없어 memo 에 보존한다 (Q12). 200자 초과 시 자른다.
    /// </summary>
    public static string BomItemMemo(string rtGu, decimal rtSon, decimal rtKum)
    {
        var memo = (rtGu ?? string.Empty).Trim();
        if (rtSon != 0 || rtKum != 0)
        {
            memo = $"{memo} | 원가 {rtSon:0.####} 금액 {rtKum:0.####}".TrimStart(' ', '|').Trim();
        }
        return memo.Length > BomItemMemoMaxLength ? memo[..BomItemMemoMaxLength] : memo;
    }
}
