using System.Data;
using System.Globalization;

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

    /// <summary>세금계산서 <c>invoice_no</c> 컬럼 길이 — <c>tax_invoices.invoice_no varchar(32) NOT NULL</c> (DESCRIBE 확인 2026-09-09 hitpan_e2e).</summary>
    public const int TaxInvoiceNoMaxLength = 32;

    /// <summary>
    /// 이관 세금계산서 <c>invoice_no</c> = <c>{direction}-{TX_NO}-{SEQ}-{PDT}-{REM해시8}</c> — 작22 (2026-09-09) C1.
    /// <list type="bullet">
    ///   <item>종전(5월 #71 옵션 A/F)은 <c>{TX_NO}-{SEQ}-{PDT}-{HASH8}</c> 로 <b>방향이 없었다.</b> DOCF4 에는 그 4키가 같은 행이
    ///   <b>21쌍</b>(TX_IO 1 과 2) 있어 <c>uk_tax_invoices_invoice_no</c> 가 먼저 걸려 ON DUPLICATE 로 한쪽이 덮였고, 실행마다 승자가 바뀌었다.
    ///   선행검증 20260909검1 §2-4: 같은 4키 + <c>TX_IO</c> 중복 그룹 <b>0</b> ⇒ 방향만 앞세우면 21쌍이 전부 갈린다.</item>
    ///   <item><paramref name="direction"/> 은 <see cref="TaxDirection"/> 이 돌려준 "S"/"B" 만 받는다 — 그 외는 throw(원값 1/2 를 넣으면 옛 사고 모양이 된다).</item>
    ///   <item>실측 TX_NO 8자 · SEQ ≤ 9 · PDT 8자 · 해시 8자 ⇒ <b>30자</b>. 컬럼은 varchar(32) — 넘으면 throw(잘라 넣으면 UNIQUE 의 뜻이 깨진다).</item>
    ///   <item>빈 PDT 는 호출부가 종전처럼 "0" 을 넘긴다. <c>source_id</c>(<c>mig-{io}-…</c>) 형식은 이 함수와 무관하게 <b>불변</b> — 재이관 때 같은 행을 잡는 열쇠다.</item>
    /// </list>
    /// </summary>
    public static string TaxInvoiceNo(string direction, string txNo, string seq, string pdt, string remHash8)
    {
        if (direction is not ("S" or "B"))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "세금계산서 direction 은 \"S\"(매출) 또는 \"B\"(매입)만 허용한다.");
        }
        var no = $"{direction}-{txNo}-{seq}-{pdt}-{remHash8}";
        if (no.Length > TaxInvoiceNoMaxLength)
        {
            throw new ArgumentException(
                $"tax_invoices.invoice_no 는 {TaxInvoiceNoMaxLength}자 이하여야 한다: '{no}' ({no.Length}자)");
        }
        return no;
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

    /// <summary>
    /// 대사표 품목 키 = <c>품명|규격</c> — 공백 trim · 대소문자 무시 · <b>끝의 <c>|</c> 는 걷어낸다</b> (작22 (2026-09-09) C2 ⑤).
    /// 규격이 비면 <c>"품명|"</c> 이 아니라 <c>"품명"</c> 이다. 그래야 <c>EnsureMigAutoItemAsync</c> 가 종전에
    /// <c>item_name="품명|규격" · spec NULL</c> 로 등록한 441건(선행검증 20260909검1 §2-6)이 레거시 <c>(IJ_PUM, IJ_KU)</c> 키와
    /// 같은 글자가 된다 — 옛 등록분을 고치지 않고도 대사가 맞는다.
    /// <see cref="MdbReconciliationService"/> 는 이 함수만 부른다(규칙은 한 군데). 이관 쪽 <c>BuildItemKey</c>(대소문자 보존 · 매핑용)와는 용도가 다르다.
    /// </summary>
    public static string ItemKey(string? name, string? spec)
        => $"{(name ?? string.Empty).Trim()}|{(spec ?? string.Empty).Trim()}".TrimEnd('|').ToUpperInvariant();

    // ════════════════════════════════════════════════════════════════════════════
    //  20260915작1 갈래 A — 장부 반영 판정 · 기준일 · 재고 품목 키 · F3 잔액
    //  진실원: 설계 20260915_설계_자료이관_머리없는줄_분류봉합.md §14·§16·§17 · 작업지시서 §11·§13(R5-2)
    //  🔴 판정은 「머리표(DOCFE) 있음 OR 거래처원장(DOCF5) 연결 있음」 뿐이다.
    //     거래처 코드를 연결 키에 넣지 않는다(선행 ⑥) · 코드 대역·날짜 모양으로 판정하지 않는다(§1 반증).
    //     모양(조립·단가행·품명없음·시험)은 보관 표 사유 칸의 **표시**에만 쓴다 — <see cref="UnpostedReason"/>.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>DOCFB 묶음의 장부 반영 판정 결과.</summary>
    public enum LegacyPostingStatus
    {
        /// <summary>머리표 있음 또는 거래처원장 연결 있음 → 명세서(장부 반영).</summary>
        Posted,
        /// <summary>둘 다 없음 → 보관 표(<c>legacy_unposted_documents</c>).</summary>
        Unposted,
        /// <summary>DOCFE 표가 없거나 0행 → 분류하지 않고 전부 반영(R5 · R5-2). 대사표에 「머리표 없음 — 분류 못 함」.</summary>
        Unclassified,
    }

    /// <summary>DOCFB 묶음 = DOCFE 머리 키 <c>(DT, IO, SEQ, BUY)</c> (설계 §1 · DOCFE DISTINCT 키 중복 0). 값은 Trim 한 글자.</summary>
    public readonly record struct LegacyDocKey(string Dt, string Io, int Seq, int Buy)
    {
        /// <summary>Trim 해서 만든다(표 사이 공백 차이로 짝이 안 맞는 일을 막는다).</summary>
        public static LegacyDocKey Of(string? dt, string? io, int seq, int buy)
            => new((dt ?? string.Empty).Trim(), (io ?? string.Empty).Trim(), seq, buy);
    }

    /// <summary>
    /// 거래처원장 연결 키 = DOCFB <c>(IJ_DT, IJ_SEQ)</c> ↔ DOCF5 <c>(S_YMD, S_SSUN)</c>. 🔴 거래처 코드는 넣지 않는다.
    /// 종류(판매 S_GU '0' / 매입 S_GU 'A')는 키가 아니라 <b>집합을 따로</b> 둬서 가른다.
    /// </summary>
    public readonly record struct LegacyLedgerLinkKey(string Ymd, int Seq)
    {
        /// <summary>Trim 해서 만든다.</summary>
        public static LegacyLedgerLinkKey Of(string? ymd, int seq) => new((ymd ?? string.Empty).Trim(), seq);
    }

    /// <summary>DOCF5 한 줄 (F3 · 연결 키 계산 입력).</summary>
    public readonly record struct LegacyPartnerLedgerRow(int Buy, string Ymd, string Gu, decimal Bal, decimal Suk, int SSun);

    /// <summary>
    /// 설계 §14 판정 원형: <c>!headerTableOk</c> → 반영(R5) · 아니면 <c>hasHeader || hasLedgerLink</c>.
    /// <paramref name="headerTableOk"/> = DOCFE 표가 있고 1행 이상.
    /// </summary>
    public static bool IsPostedToBooks(bool headerTableOk, bool hasHeader, bool hasLedgerLink)
        => !headerTableOk || hasHeader || hasLedgerLink;

    /// <summary>
    /// DOCFB 묶음 하나 판정.
    /// <list type="bullet">
    ///   <item><paramref name="headerKeys"/> null 또는 0개 = DOCFE 없음 → <see cref="LegacyPostingStatus.Unclassified"/> (전부 반영 · R5-2 는 DOCF5 유무 무관).</item>
    ///   <item>머리 키가 있으면 Posted.</item>
    ///   <item>연결 집합은 <b>종류 맞춤</b>: IO "2"(판매) → <paramref name="salesLinks"/> · IO "1"(매입) → <paramref name="purchaseLinks"/>.
    ///   null/0개 = DOCF5 없음 → 머리표로만 판정(대사표 「거래처원장 표 없음」은 호출자 몫).</item>
    /// </list>
    /// </summary>
    public static LegacyPostingStatus ClassifyPosting(
        LegacyDocKey doc,
        IReadOnlySet<LegacyDocKey>? headerKeys,
        IReadOnlySet<LegacyLedgerLinkKey>? salesLinks,
        IReadOnlySet<LegacyLedgerLinkKey>? purchaseLinks)
    {
        var headerTableOk = headerKeys is { Count: > 0 };
        if (!headerTableOk) return LegacyPostingStatus.Unclassified;

        var hasHeader = headerKeys!.Contains(doc);
        var links = doc.Io switch
        {
            "2" => salesLinks,
            "1" => purchaseLinks,
            _ => null,
        };
        // 병렬이슈36: 순번 0 은 연결 키로 쓰지 않는다(DOCFB SEQ 0 묶음 · DOCF5 「매출세액」 메모행 S_SSUN 0 이 날짜만 겹치면 거짓 연결).
        var hasLink = doc.Seq != 0 && links is { Count: > 0 } && links.Contains(LegacyLedgerLinkKey.Of(doc.Dt, doc.Seq));

        return IsPostedToBooks(headerTableOk, hasHeader, hasLink)
            ? LegacyPostingStatus.Posted
            : LegacyPostingStatus.Unposted;
    }

    /// <summary>
    /// DOCFE → 머리 키 집합. 표가 null 이거나 0행이면 <b>null</b>(= 표 없음 · R5).
    /// 칼럼: <c>IJA_DT · IJA_IO · IJA_SEQ · IJA_BUY</c>.
    /// </summary>
    public static HashSet<LegacyDocKey>? BuildHeaderKeySet(DataTable? docfe)
    {
        if (docfe is null || docfe.Rows.Count == 0) return null;
        var set = new HashSet<LegacyDocKey>();
        foreach (DataRow r in docfe.Rows)
        {
            set.Add(LegacyDocKey.Of(RowStr(r, "IJA_DT"), RowStr(r, "IJA_IO"), RowInt(r, "IJA_SEQ"), RowInt(r, "IJA_BUY")));
        }
        return set;
    }

    /// <summary>
    /// DOCF5 줄 → 판매·매입 연결 키 집합. <b>이월행 제외 · S_SSUN 0 제외(병렬이슈36)</b> · S_GU '0' → 판매 · 'A' → 매입 · 그 외(수금·지급) 무시.
    /// 입력이 null 이거나 0줄이면 둘 다 <b>null</b>(= 거래처원장 표 없음).
    /// </summary>
    public static (HashSet<LegacyLedgerLinkKey>? Sales, HashSet<LegacyLedgerLinkKey>? Purchase) BuildLedgerLinkKeySets(
        IEnumerable<LegacyPartnerLedgerRow>? rows)
    {
        if (rows is null) return (null, null);
        var sales = new HashSet<LegacyLedgerLinkKey>();
        var purchase = new HashSet<LegacyLedgerLinkKey>();
        var any = false;
        foreach (var r in rows)
        {
            any = true;
            if (IsCarryOverRow(r.Gu, r.Ymd)) continue;
            if (r.SSun == 0) continue; // 병렬이슈36: S_SSUN 0(매출세액 메모행 등)은 연결 키가 아니다
            var gu = (r.Gu ?? string.Empty).Trim().ToUpperInvariant();
            if (gu == "0") sales.Add(LegacyLedgerLinkKey.Of(r.Ymd, r.SSun));
            else if (gu == "A") purchase.Add(LegacyLedgerLinkKey.Of(r.Ymd, r.SSun));
        }
        return any ? (sales, purchase) : (null, null);
    }

    /// <summary><see cref="BuildLedgerLinkKeySets(IEnumerable{LegacyPartnerLedgerRow})"/> 의 DataTable 판. 표 null/0행 → (null, null).</summary>
    public static (HashSet<LegacyLedgerLinkKey>? Sales, HashSet<LegacyLedgerLinkKey>? Purchase) BuildLedgerLinkKeySets(DataTable? docf5)
        => docf5 is null || docf5.Rows.Count == 0 ? (null, null) : BuildLedgerLinkKeySets(ReadPartnerLedgerRows(docf5));

    /// <summary>
    /// DOCF5 DataTable → <see cref="LegacyPartnerLedgerRow"/>. 칼럼: <c>S_BUY · S_YMD · S_GU · S_BAL · S_SUK · S_SSUN</c>. null → 빈 목록.
    /// </summary>
    public static List<LegacyPartnerLedgerRow> ReadPartnerLedgerRows(DataTable? docf5)
    {
        var list = new List<LegacyPartnerLedgerRow>(docf5?.Rows.Count ?? 0);
        if (docf5 is null) return list;
        foreach (DataRow r in docf5.Rows)
        {
            list.Add(new LegacyPartnerLedgerRow(
                RowInt(r, "S_BUY"), RowStr(r, "S_YMD").Trim(), RowStr(r, "S_GU").Trim(),
                RowDec(r, "S_BAL"), RowDec(r, "S_SUK"), RowInt(r, "S_SSUN")));
        }
        return list;
    }

    /// <summary>DOCF5 이월행 = S_GU '0' AND S_YMD 끝 두 자리 '00' (월 이월 집계행 · 실측 326,194행 전부 S_GU 0).</summary>
    public static bool IsCarryOverRow(string? sGu, string? sYmd)
    {
        var ymd = (sYmd ?? string.Empty).Trim();
        return (sGu ?? string.Empty).Trim() == "0" && ymd.Length == 8 && ymd.EndsWith("00", StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>F3</b> (설계 §17) — 거래처별 <b>마지막 이월행 S_BAL</b>(없으면 0) + <b>그 뒤(S_YMD 가 더 큰) 이월 아닌 줄 Σ(S_BAL − S_SUK)</b>.
    /// 부호: <b>+ = 미수(받을 돈) · − = 미지급(줄 돈)</b>. 판매 발생 S_GU 0 은 S_BAL(+) · 수금 1~5 는 S_SUK(−) ·
    /// 매입 발생 A 는 S_SUK(−) · 지급 B~F 는 S_BAL(+).
    /// <para>「마지막 달 이월행만」(그 뒤 줄 누락 · PM 140,865,113)이나 전 줄 합(F1)이 아니다 — G1 함정.</para>
    /// 반환: 거래처 코드(S_BUY) → 잔액. 0 인 거래처도 포함한다(개수 셀 때는 0 제외).
    /// </summary>
    public static Dictionary<int, decimal> PartnerLegacyBalances(IEnumerable<LegacyPartnerLedgerRow> rows)
    {
        var lastCarry = new Dictionary<int, (string Ymd, decimal Bal)>();
        var all = rows as IList<LegacyPartnerLedgerRow> ?? rows.ToList();

        foreach (var r in all)
        {
            if (!IsCarryOverRow(r.Gu, r.Ymd)) continue;
            var ymd = r.Ymd.Trim();
            // 같은 날 이월 중복 실측 0 — 있으면 뒤에 읽힌 줄이 아니라 큰 날짜 기준으로만 바꾼다(같은 날은 첫 줄 유지).
            if (!lastCarry.TryGetValue(r.Buy, out var cur) || string.CompareOrdinal(ymd, cur.Ymd) > 0)
            {
                lastCarry[r.Buy] = (ymd, r.Bal);
            }
        }

        var result = new Dictionary<int, decimal>();
        foreach (var (buy, c) in lastCarry) result[buy] = c.Bal;

        foreach (var r in all)
        {
            if (IsCarryOverRow(r.Gu, r.Ymd)) continue;
            var ymd = (r.Ymd ?? string.Empty).Trim();
            if (lastCarry.TryGetValue(r.Buy, out var c) && string.CompareOrdinal(ymd, c.Ymd) <= 0) continue;
            result[r.Buy] = (result.TryGetValue(r.Buy, out var acc) ? acc : 0m) + r.Bal - r.Suk;
        }
        return result;
    }

    /// <summary>
    /// F3 합계 — 미수 = 양수 잔액 합 · 미지급 = 음수 잔액의 <b>절대값</b> 합 · 곳 수는 0 아닌 거래처만.
    /// 공영정보 MDB 기대: 미수 142,062,113 (1,059곳) / 미지급 51,577,479 (530곳).
    /// </summary>
    public static (decimal Receivable, int ReceivableCount, decimal Payable, int PayableCount) SummarizeBalances(
        IReadOnlyDictionary<int, decimal> balances)
    {
        decimal rec = 0m, pay = 0m;
        int rc = 0, pc = 0;
        foreach (var v in balances.Values)
        {
            if (v > 0) { rec += v; rc++; }
            else if (v < 0) { pay += -v; pc++; }
        }
        return (rec, rc, pay, pc);
    }

    /// <summary>레거시 8자리 날짜 <c>yyyyMMdd</c> 파싱. <c>00000000</c>·<c>00000001</c>·빈칸 등 → false.</summary>
    public static bool TryParseLegacyDate(string? yyyymmdd, out DateTime date)
        => DateTime.TryParseExact((yyyymmdd ?? string.Empty).Trim(), "yyyyMMdd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>
    /// 🆕 기준일 (설계 §14) = DOCFC MAX(IM_YM) 말일 → 없으면 DOCF5 마지막 이월 달(YYYYMM00) 말일 → 없으면 DOCFB 최대 유효 IJ_DT.
    /// 셋 다 없으면 null (호출자가 정한다 — 오늘 날짜로 몰래 채우지 않는다).
    /// </summary>
    public static DateTime? LegacyBaseDate(
        IEnumerable<string?>? docfcYm, IEnumerable<string?>? docf5CarryYmd, IEnumerable<string?>? docfbDt)
    {
        var fromC = MaxMonthEnd(docfcYm, 6);
        if (fromC is not null) return fromC;

        var fromF5 = MaxMonthEnd(docf5CarryYmd?.Where(y => IsCarryOverRow("0", y)).Select(y => y!.Trim()[..6]), 6);
        if (fromF5 is not null) return fromF5;

        DateTime? max = null;
        if (docfbDt is not null)
        {
            foreach (var d in docfbDt)
            {
                if (TryParseLegacyDate(d, out var p) && (max is null || p > max)) max = p;
            }
        }
        return max;
    }

    /// <summary>
    /// <see cref="LegacyBaseDate(IEnumerable{string?}?, IEnumerable{string?}?, IEnumerable{string?}?)"/> 의 DataTable 판.
    /// 칼럼: DOCFC <c>IM_YM</c> · DOCF5 <c>S_GU·S_YMD</c>(이월행만) · DOCFB <c>IJ_DT</c>. 표 null 허용.
    /// </summary>
    public static DateTime? LegacyBaseDate(DataTable? docfc, DataTable? docf5, DataTable? docfb)
    {
        static IEnumerable<string?> Col(DataTable? t, string c)
            => t is null ? Enumerable.Empty<string?>() : t.Rows.Cast<DataRow>().Select(r => (string?)RowStr(r, c));

        var carry = docf5 is null
            ? Enumerable.Empty<string?>()
            : docf5.Rows.Cast<DataRow>()
                .Where(r => IsCarryOverRow(RowStr(r, "S_GU"), RowStr(r, "S_YMD")))
                .Select(r => (string?)RowStr(r, "S_YMD"));
        return LegacyBaseDate(Col(docfc, "IM_YM"), carry, Col(docfb, "IJ_DT"));
    }

    /// <summary>
    /// 명세서·보관 표·재고원장 날짜 = IJ_DT 가 유효하면 그 날, 아니면(<c>00000000</c>·<c>00000001</c> 등) <b>기준일</b>.
    /// 원본 IJ_DT 글자는 호출자가 <c>legacy_dt</c> 에 그대로 남긴다.
    /// </summary>
    public static DateTime ResolveLegacyDate(string? ijDt, DateTime baseDate)
        => TryParseLegacyDate(ijDt, out var d) ? d : baseDate;

    /// <summary><see cref="StockItemKey"/> 구분자 = U+001F (제어문자 · 품명·규격·창고 글자에 안 나온다).</summary>
    public const char StockKeySeparator = (char)0x1F;

    /// <summary>
    /// 🆕 재고 품목 키 (설계 §16) = 품명·규격·창고 각각 <b>Trim + 대소문자 무시</b>(Access · utf8mb4_unicode_ci 비교와 같은 방향).
    /// 구분자는 글자에 안 나오는 U+001F — <see cref="ItemKey"/> 의 <c>|</c> 와 달리 규격·창고 빈칸이 서로 섞이지 않는다.
    /// 🔴 이관 매핑용 <c>MdbMigrationService.BuildItemKey</c>(대소문자 보존)는 건드리지 않는다 — 이 키는 재고 합산·대사 전용.
    /// <paramref name="warehouse"/> null = 창고 합산(R7 기본창고).
    /// </summary>
    public static string StockItemKey(string? name, string? spec, string? warehouse = null)
        => string.Join(StockKeySeparator,
            (name ?? string.Empty).Trim().ToUpperInvariant(),
            (spec ?? string.Empty).Trim().ToUpperInvariant(),
            (warehouse ?? string.Empty).Trim().ToUpperInvariant());

    /// <summary>
    /// 보관 표 사유 칸(<c>reason varchar(30)</c>) <b>표시용</b> 이름표. 🔴 판정에 쓰지 않는다 — 판정은 <see cref="ClassifyPosting"/> 만.
    /// 우선순위(설계 §1): 단가행(DT 00000000/00000001) → 조립·해체(BUY 2147483500) → 품명 없음(BUY 0 AND SEQ 0) →
    /// 금액 0 시험(SEQ 31001~31004 · 묶음 금액·부가세 0) → 시험(SEQ 31001~31004) → 기타.
    /// </summary>
    public static string UnpostedReason(string? dt, int buy, int seq, decimal groupAbsAmount, decimal groupAbsVat)
    {
        var d = (dt ?? string.Empty).Trim();
        if (d is "00000000" or "00000001") return "danga";
        if (buy == 2147483500) return "assembly";
        if (buy == 0 && seq == 0) return "no_name";
        if (seq is >= 31001 and <= 31004)
            return groupAbsAmount == 0m && groupAbsVat == 0m ? "test_zero_amount" : "test";
        return "other";
    }

    /// <summary><see cref="UnpostedReason"/> 코드 → 화면 글자.</summary>
    public static string UnpostedReasonText(string? reason) => reason switch
    {
        "danga" => "단가 기록 줄",
        "assembly" => "조립·해체",
        "no_name" => "품명 없음",
        "test_zero_amount" => "시험 입력(금액 0)",
        "test" => "시험 입력",
        _ => "장부 미반영",
    };

    // ── DataRow 안전 읽기 (이 클래스 전용 · 칼럼 없음/DBNull → 기본값) ──

    private static DateTime? MaxMonthEnd(IEnumerable<string?>? yms, int len)
    {
        if (yms is null) return null;
        DateTime? max = null;
        foreach (var raw in yms)
        {
            var ym = (raw ?? string.Empty).Trim();
            if (ym.Length < len) continue;
            if (!DateTime.TryParseExact(ym[..len] + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first)) continue;
            var end = first.AddMonths(1).AddDays(-1);
            if (max is null || end > max) max = end;
        }
        return max;
    }

    private static string RowStr(DataRow r, string col)
    {
        if (!r.Table.Columns.Contains(col)) return string.Empty;
        var v = r[col];
        return v is null or DBNull ? string.Empty : Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static int RowInt(DataRow r, string col)
    {
        if (!r.Table.Columns.Contains(col)) return 0;
        var v = r[col];
        if (v is null or DBNull) return 0;
        return v switch
        {
            int i => i,
            short s => s,
            long l => checked((int)l),
            byte b => b,
            _ => int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 0,
        };
    }

    private static decimal RowDec(DataRow r, string col)
    {
        if (!r.Table.Columns.Contains(col)) return 0m;
        var v = r[col];
        if (v is null or DBNull) return 0m;
        return v switch
        {
            decimal m => m,
            int i => i,
            short s => s,
            long l => l,
            _ => decimal.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var p) ? p : 0m,
        };
    }
}
