using System.Data;
using System.Globalization;
using Dapper;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 🆕 20260915작1 갈래 D — 「이전 프로그램 최종재고 맞춤」 + 끝전(avg_cost) 맞춤.
/// </summary>
/// <remarks>
/// <para>
/// 진실원: 작업지시서 <c>docs/운영기록/20260915작1_자료이관_머리없는줄_분류봉합_작업지시서.md</c> §4 D · §8-2 · §10-1 · §11-1 · §13 R6-2 · §14 병렬이슈37 ·
/// 설계 <c>docs/설계/erp/20260915_설계_자료이관_머리없는줄_분류봉합.md</c> §10 · §11 · §12 · §16 ·
/// 개발명세서 <c>docs/개발/erp/20260915작1_갈래D_재고맞춤_개발명세서.md</c>.
/// </para>
/// <para>
/// 🔴 호출 순서(갈래 B 가 <c>MdbMigrationService</c> 에 넣는다):
/// ① <see cref="AdjustStockAsync(IDbConnection, string, DataTable?, DateTime, Func{string, string, IDbTransaction?, CancellationToken, Task{string}}, CancellationToken)"/> — <c>item_stock_rebuild</c> <b>앞</b>
/// ② <c>item_stock_rebuild</c>
/// ③ <see cref="ApplyFinalCostAsync(IDbConnection, string, DataTable?, CancellationToken)"/> — 리빌드 <b>뒤</b>
/// (리빌드가 <c>avg_cost</c> 를 입고 가중평균으로 덮으므로 끝전은 반드시 뒤에서 · 병렬이슈37).
/// ①·③ 에는 <b>같은 DOCFC DataTable 인스턴스</b>를 넘긴다 — ① 이 고른 품목을 표에 적어 두고 ③ 이 그대로 읽는다.
/// </para>
/// <para>
/// DESCRIBE 근거(#13 · 격리 MariaDB 11.4.10 포트 33306 · 출하 DDL 적재 · 2026-09-15):
/// <c>stock_ledger</c> tenant_id varchar(36) NN · item_id varchar(36) NN · warehouse_id varchar(36) NN · partner_id varchar(36) NULL ·
/// ledger_date date NN · ym varchar(7) NN · move_type varchar(10) NN · source_type varchar(30) NN · source_id varchar(36) NN ·
/// doc_no varchar(20) NULL · qty_in/qty_out decimal(15,3) NN · unit_cost decimal(15,4) NULL · supply_amount decimal(15,2) NULL ·
/// memo varchar(200) NULL · migrated_source_hash char(64) NULL · created_at datetime(6) NN DEFAULT ·
/// UNIQUE(tenant_id,source_type,source_id,item_id,move_type,warehouse_id) · UNIQUE(tenant_id,migrated_source_hash).
/// <c>item_stock</c> stock_id varchar(36) PK · tenant_id · item_id · warehouse_id varchar(36) NN · current_qty decimal(10,2) ·
/// avg_cost decimal(19,6) NN · last_updated_at datetime(6) NN · UNIQUE(tenant_id,item_id,warehouse_id).
/// <c>items</c> created_at datetime(6) NN.
/// </para>
/// </remarks>
public static class MdbLegacyFinalStock
{
    /// <summary>맞춤 줄 메모 (고객 화면 노출 문구 · 개발용어 금지 · R8).</summary>
    public const string AdjustmentMemo = "이전 프로그램 최종재고 맞춤";

    /// <summary>맞춤 줄 source_id 접두 — <c>mb-adj-{DOCFC MAX(IM_YM)}</c>.</summary>
    public const string AdjustmentSourceIdPrefix = "mb-adj-";

    /// <summary>① 이 고른 (재고 키 → item_id) 를 DOCFC 표에 적어 두는 자리 이름(테넌트별).</summary>
    internal const string ItemMapPropertyPrefix = "HitPan.MdbLegacyFinalStock.ItemMap|";

    // ══════════════════════════════════════════════════════════════
    // 순수 계산 (G3 가 합성 DataTable 로 부른다)
    // ══════════════════════════════════════════════════════════════

    /// <summary>DOCFC 최종 기말 — 재고 키(품명·규격 Trim+대소문자 무시 · 창고 합산) 한 줄.</summary>
    /// <param name="Key"><see cref="LegacyMdbMapping.StockItemKey"/>(품명, 규격) — 창고 없음.</param>
    /// <param name="Qty">창고별 마지막 달 IM_CQTY 합.</param>
    /// <param name="Amount">창고별 마지막 달 IM_CAMT 합.</param>
    /// <param name="Variants">원문 (IM_PUM, IM_KU) Trim 값 — 대소문자가 다른 표기를 모두 담는다(순서 = 표 등장 순).</param>
    /// <param name="WarehouseLines">합친 (품명·규격·창고) 줄 수.</param>
    public sealed record FinalStockLine(
        string Key, decimal Qty, decimal Amount,
        IReadOnlyList<(string Pum, string Ku)> Variants, int WarehouseLines);

    /// <summary>DOCFC 최종 기말 계산 결과.</summary>
    /// <param name="MaxYm">DOCFC 전체 MAX(IM_YM) — 맞춤 줄 source_id·해시에 쓴다.</param>
    /// <param name="Lines">재고 키별 최종 기말 (키 서수 정렬).</param>
    /// <param name="SkippedRows">IM_YM 이 6자리 숫자가 아니라 「마지막 달」을 가를 수 없어 뺀 행.</param>
    /// <param name="NonDefaultWarehouseLines">창고 00(빈칸) 이외 창고 줄 수 — 기본창고에 합친 줄(R7 · 대사표 표시용).</param>
    public sealed record FinalStockResult(
        string MaxYm, IReadOnlyList<FinalStockLine> Lines, int SkippedRows, int NonDefaultWarehouseLines);

    /// <summary>
    /// DOCFC → 품목별 최종 기말 (설계 §10 · §11 · §16).
    /// ① (IM_PUM, IM_KU, IM_CHANG) 을 <b>Trim + 대소문자 무시</b>로 묶어 <b>그 묶음의</b> MAX(IM_YM) 행을 고른다(전체 MAX 달만 보면 그 전에 끝난 품목이 빠진다).
    ///    같은 묶음에 같은 MAX 달 행이 둘 이상(대소문자만 다른 표기)이면 모두 더한다 — Access 의 대소문자 무시 조인이 돌려주는 행과 같다.
    /// ② 창고를 지우고 품명·규격 키로 합산(R7 기본창고).
    /// DOCFC null / 0행 / 유효 IM_YM 0행 → null.
    /// </summary>
    public static FinalStockResult? ComputeFinalStock(DataTable? docfc)
    {
        if (docfc is null || docfc.Rows.Count == 0) return null;

        // (창고 포함 키) → (마지막 달, 수량, 금액, 표기들)
        var perWarehouse = new Dictionary<string, (string Ym, decimal Qty, decimal Amt, string Key, List<(string, string)> Variants, bool NonDefault)>(StringComparer.Ordinal);
        string? maxYm = null;
        int skipped = 0;

        foreach (DataRow row in docfc.Rows)
        {
            var ym = Str(row, "IM_YM").Trim();
            if (!IsYm(ym))
            {
                skipped++;
                continue;
            }

            var pum = Str(row, "IM_PUM").Trim();
            var ku = Str(row, "IM_KU").Trim();
            var chang = Str(row, "IM_CHANG").Trim();
            var whKey = LegacyMdbMapping.StockItemKey(pum, ku, chang);
            var qty = Dec(row, "IM_CQTY");
            var amt = Dec(row, "IM_CAMT");

            if (maxYm is null || string.CompareOrdinal(ym, maxYm) > 0) maxYm = ym;

            if (!perWarehouse.TryGetValue(whKey, out var cur) || string.CompareOrdinal(ym, cur.Ym) > 0)
            {
                perWarehouse[whKey] = (ym, qty, amt, LegacyMdbMapping.StockItemKey(pum, ku),
                    new List<(string, string)> { (pum, ku) }, !IsDefaultWarehouse(chang));
            }
            else if (string.CompareOrdinal(ym, cur.Ym) == 0)
            {
                if (!cur.Variants.Contains((pum, ku))) cur.Variants.Add((pum, ku));
                perWarehouse[whKey] = (cur.Ym, cur.Qty + qty, cur.Amt + amt, cur.Key, cur.Variants, cur.NonDefault);
            }
        }

        if (maxYm is null) return null;

        var byItem = new Dictionary<string, (decimal Qty, decimal Amt, List<(string Pum, string Ku)> Variants, int Lines)>(StringComparer.Ordinal);
        int nonDefault = 0;
        foreach (var w in perWarehouse.Values)
        {
            if (w.NonDefault) nonDefault++;
            if (!byItem.TryGetValue(w.Key, out var it))
                it = (0m, 0m, new List<(string, string)>(), 0);
            foreach (var v in w.Variants)
                if (!it.Variants.Contains(v)) it.Variants.Add(v);
            byItem[w.Key] = (it.Qty + w.Qty, it.Amt + w.Amt, it.Variants, it.Lines + 1);
        }

        var lines = byItem
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new FinalStockLine(kv.Key, kv.Value.Qty, kv.Value.Amt, kv.Value.Variants, kv.Value.Lines))
            .ToList();
        return new FinalStockResult(maxYm, lines, skipped, nonDefault);
    }

    /// <summary>
    /// 조정량 = 목표 − 이관 원장 순수량 (설계 §10 D3).
    /// <paramref name="ledgerNet"/> 에만 있는 품목(DOCFB 에만 있던 품목 · 대소문자만 다른 나머지 품목 · 품명 없는 폴백 품목) = 목표 0.
    /// 0 인 조정은 돌려주지 않는다. item_id 서수 정렬.
    /// </summary>
    public static IReadOnlyList<(string ItemId, decimal Diff)> ComputeAdjustments(
        IReadOnlyDictionary<string, decimal> target, IReadOnlyDictionary<string, decimal> ledgerNet)
    {
        var ids = new SortedSet<string>(target.Keys, StringComparer.Ordinal);
        ids.UnionWith(ledgerNet.Keys);
        var list = new List<(string, decimal)>();
        foreach (var id in ids)
        {
            var t = target.TryGetValue(id, out var tv) ? tv : 0m;
            var n = ledgerNet.TryGetValue(id, out var nv) ? nv : 0m;
            var diff = t - n;
            if (diff != 0m) list.Add((id, diff));
        }
        return list;
    }

    /// <summary>
    /// 대소문자·공백만 다른 품목이 히트판에 여럿일 때 맞춤 줄을 넣을 품목 = <b>가장 먼저 등록된 품목</b>(items.created_at 오름차순 → item_id 서수).
    /// 후보가 비면 null.
    /// </summary>
    public static string? ChooseItem(IEnumerable<(string ItemId, DateTime CreatedAt)> candidates)
        => candidates
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.ItemId, StringComparer.Ordinal)
            .Select(c => c.ItemId)
            .FirstOrDefault();

    /// <summary>(in/out, 수량) — diff&gt;0 입고 · diff&lt;0 출고 절대값.</summary>
    public static (string MoveType, decimal QtyIn, decimal QtyOut) AdjustmentMove(decimal diff)
        => diff >= 0m ? ("in", diff, 0m) : ("out", 0m, -diff);

    /// <summary>맞춤 줄 source_id = <c>mb-adj-{ym}</c>.</summary>
    public static string AdjustmentSourceId(string maxYm) => AdjustmentSourceIdPrefix + maxYm;

    /// <summary>맞춤 줄 migrated_source_hash = SHA256(<c>adj|{ym}|{item_id}|{move_type}</c>) 대문자 16진 64자(이관 원장 해시와 같은 모양).</summary>
    public static string AdjustmentHash(string maxYm, string itemId, string moveType)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"adj|{maxYm}|{itemId}|{moveType}")));

    /// <summary>
    /// 끝전 단가 = 금액 ÷ 수량 을 소수 6자리(avg_cost decimal(19,6) · R6-2)로 반올림(0 에서 먼 쪽 · MariaDB DECIMAL 반올림과 같다).
    /// 수량 0 이면 null — 금액을 단가로 표현할 수 없다(개발명세서 §규칙).
    /// </summary>
    public static decimal? FinalUnitCost(decimal qty, decimal amount)
        => qty == 0m ? null : Math.Round(amount / qty, 6, MidpointRounding.AwayFromZero);

    // ══════════════════════════════════════════════════════════════
    // 🆕 20260915작1 갈래 I — DOCFC 신선도 가드 (작업지시서 §14-8 PM 반증)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 신선도 가드 사유 문구(대사표·결과 표가 그대로 읽는다 · 고객 화면 노출 가능 · 개발용어 금지).
    /// 이 문구가 결과에 있으면 최종재고 맞춤·끝전을 <b>하지 않았다</b> — 재고는 이력(DOCFB) 누계 그대로다.
    /// </summary>
    public const string StaleFinalStockReason = "최종재고 표가 최신이 아님";

    /// <summary>
    /// 표의 날짜 칸에서 <c>yyyyMMdd</c> 로 읽히는 가장 늦은 날짜(<paramref name="notAfter"/> 이하만). 표·칸 없음 / 유효 날짜 0 → null.
    /// <paramref name="notAfter"/> 뒤 날짜(잘못 친 미래 날짜)는 뺀다 — 하나만 있어도 가드가 늘 skip 되는 것을 막는다.
    /// </summary>
    public static DateTime? LastValidLegacyDate(DataTable? table, string column, DateTime notAfter)
    {
        if (table is null || !table.Columns.Contains(column)) return null;
        DateTime? max = null;
        foreach (DataRow row in table.Rows)
        {
            if (LegacyMdbMapping.TryParseLegacyDate(Str(row, column), out var d)
                && d.Date <= notAfter.Date
                && (max is null || d > max))
            {
                max = d;
            }
        }
        return max;
    }

    /// <summary>
    /// DOCFC 마지막 달(MAX IM_YM) 이 DOCFB 마지막 유효 날짜의 달보다 앞이면(월마감을 안 돌린 MDB) 사유를, 아니면 null.
    /// DOCFC 계산 없음(null) · DOCFB 날짜 없음(null) → null (가드할 근거가 없다 — DOCFC 없음 skip 은 맞춤 단계가 따로 한다).
    /// </summary>
    public static string? StaleReason(string? docfcMaxYm, DateTime? docfbLastDate)
    {
        if (string.IsNullOrEmpty(docfcMaxYm) || docfbLastDate is null) return null;
        var docfbYm = docfbLastDate.Value.ToString("yyyyMM", CultureInfo.InvariantCulture);
        return string.CompareOrdinal(docfcMaxYm, docfbYm) < 0 ? StaleFinalStockReason : null;
    }

    /// <summary><see cref="StaleReason(string?, DateTime?)"/> 의 DOCFC 표 판 — <see cref="ComputeFinalStock"/> 의 MaxYm 으로 가른다.</summary>
    public static string? StaleReason(DataTable? docfc, DateTime? docfbLastDate)
        => StaleReason(ComputeFinalStock(docfc)?.MaxYm, docfbLastDate);

    // ══════════════════════════════════════════════════════════════
    // 🆕 20260915작1 개정 3판 갈래 R3 — 재고 이어 계산 (설계 §23 · 작업지시서 §15-2·§15-5 · 선행검증 20260917 B-2)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// S1(PM 전결 §15-9) 대사표 안내 1줄 — <c>{0}</c> = 이어 계산한 개월 수. 고객 화면 노출 문구(개발용어 금지).
    /// 이어 계산 구간은 최종재고 표 행이 없어 특수입출고(창고이동 등)를 알 길이 없다(설계 §23 전제 정정).
    /// </summary>
    public const string RollForwardNotice = "최종재고 표 뒤 {0}개월은 거래명세서 입출고로 이어 계산했습니다. 창고이동 등 특수입출고는 반영되지 않았을 수 있습니다";

    /// <summary>DOCFB 한 줄(이어 계산 입력). <paramref name="Io"/> "1" 입고 · "2" 출고 — 나머지는 안 센다.</summary>
    public sealed record LegacyStockMove(string Pum, string Ku, string Chang, DateTime Date, string Io, decimal Qty, decimal Amount);

    /// <summary>이어 계산 정보(대사표·결과 표시용).</summary>
    /// <param name="FromYm">DOCFC 마지막 달(시작점).</param>
    /// <param name="ToYm">기준일 달(끝).</param>
    /// <param name="CarryMonths">이어 계산한 달 수(FromYm 다음 달 ~ ToYm).</param>
    /// <param name="MoveLines">계산에 쓴 DOCFB 줄 수(수량 0 줄 · 구간 밖 · 입출 구분 밖 제외).</param>
    /// <param name="ZeroQtyAmountLines">수량 0 인데 금액만 있어 뺀 줄 수(P/L 등).</param>
    /// <param name="ZeroQtyAmount">그 줄들의 금액 합.</param>
    public sealed record RollForwardInfo(string FromYm, string ToYm, int CarryMonths, int MoveLines, int ZeroQtyAmountLines, decimal ZeroQtyAmount);

    /// <summary>이어 계산 결과 — <see cref="FinalStockResult"/> 는 맞춤·끝전 오버로드에 그대로 넘긴다.</summary>
    public sealed record RollForwardResult(FinalStockResult Final, RollForwardInfo Info);

    /// <summary>
    /// 이어 계산 기준일 = min(DOCFB 마지막 유효 날짜의 달 말일, 이관일) — 이관일은 <b>로컬 날짜</b>(P6 · 호출자가 넘긴다).
    /// </summary>
    public static DateTime CarryBaseDate(DateTime docfbLastDate, DateTime migrationLocalDate)
    {
        var d = docfbLastDate.Date;
        var monthEnd = new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
        var m = migrationLocalDate.Date;
        return monthEnd <= m ? monthEnd : m;
    }

    /// <summary>
    /// DOCFC 가 DOCFB 보다 오래됐을 때(월마감 안 돌린 MDB) 레거시 월 총평균을 흉내 내 기준일 달까지 이어 계산한다 — 선행검증 B-2 규칙 ③.
    /// <para>시작 = (품명·규격·창고) 키별 DOCFC 마지막 달 기말수량·기말금액·IM_DAN. DOCFB 에만 있는 키는 0 에서 시작.</para>
    /// <para>DOCFC 마지막 달 다음 달 ~ 기준일 달, 달마다 <b>모든 키</b>:
    /// 입고수량 = Σ IO1 수량(음수 포함) · 입고금액 = Σ IO1 금액 · 출고수량 = Σ IO2 수량(음수 = 반품이 출고를 줄임 · <b>금액에 안 씀</b>) ·
    /// 입고수량≠0 이고 (기초+입고)수량≠0 → 단가 = (기초금액+입고금액)/(기초수량+입고수량) · 아니면 상품마스터 S_IDAN(0/없으면 직전 단가) ·
    /// 기말금액 = ROUND(기말수량×단가, 0 · 0 에서 먼 쪽).</para>
    /// <para>수량 0 줄은 계산에서 빼고 금액이 있으면 건수·금액을 <see cref="RollForwardInfo"/> 에 남긴다. 날짜 &gt; 기준일 · DOCFC 마지막 달 이하 줄 제외.</para>
    /// DOCFC null/0행/유효 달 0 → null(시작점이 없다).
    /// </summary>
    /// <param name="masterIdanByItemKey">키 = <see cref="LegacyMdbMapping.StockItemKey"/>(품명, 규격) · 값 = DOCFS S_IDAN.</param>
    public static RollForwardResult? RollForward(DataTable? docfc, IEnumerable<LegacyStockMove> movesAfter,
        IReadOnlyDictionary<string, decimal> masterIdanByItemKey, DateTime baseDate)
    {
        ArgumentNullException.ThrowIfNull(movesAfter);
        ArgumentNullException.ThrowIfNull(masterIdanByItemKey);
        if (docfc is null || docfc.Rows.Count == 0) return null;

        var state = new Dictionary<string, CarryState>(StringComparer.Ordinal);
        string? fromYm = null;
        int skipped = 0;
        foreach (DataRow row in docfc.Rows)
        {
            var ym = Str(row, "IM_YM").Trim();
            if (!IsYm(ym))
            {
                skipped++;
                continue;
            }
            var pum = Str(row, "IM_PUM").Trim();
            var ku = Str(row, "IM_KU").Trim();
            var chang = Str(row, "IM_CHANG").Trim();
            var whKey = LegacyMdbMapping.StockItemKey(pum, ku, chang);
            if (fromYm is null || string.CompareOrdinal(ym, fromYm) > 0) fromYm = ym;

            var qty = Dec(row, "IM_CQTY");
            var amt = Dec(row, "IM_CAMT");
            var dan = Dec(row, "IM_DAN");
            if (!state.TryGetValue(whKey, out var cur) || string.CompareOrdinal(ym, cur.Ym) > 0)
            {
                state[whKey] = new CarryState(ym, LegacyMdbMapping.StockItemKey(pum, ku), !IsDefaultWarehouse(chang))
                {
                    Qty = qty, Amt = amt, Dan = dan,
                };
                state[whKey].Variants.Add((pum, ku));
            }
            else if (string.CompareOrdinal(ym, cur.Ym) == 0)
            {
                cur.Qty += qty;
                cur.Amt += amt;
                if (cur.Dan == 0m) cur.Dan = dan;
                if (!cur.Variants.Contains((pum, ku))) cur.Variants.Add((pum, ku));
            }
        }
        if (fromYm is null) return null;

        var toYm = baseDate.ToString("yyyyMM", CultureInfo.InvariantCulture);
        var months = new List<string>();
        for (var ym = NextYm(fromYm); string.CompareOrdinal(ym, toYm) <= 0; ym = NextYm(ym)) months.Add(ym);

        // 달 → 키 → (입고수량, 입고금액, 출고수량)
        var buckets = new Dictionary<string, Dictionary<string, (decimal InQ, decimal InA, decimal OutQ)>>(StringComparer.Ordinal);
        int moveLines = 0, zeroLines = 0;
        decimal zeroAmount = 0m;
        foreach (var mv in movesAfter)
        {
            var mYm = mv.Date.ToString("yyyyMM", CultureInfo.InvariantCulture);
            if (string.CompareOrdinal(mYm, fromYm) <= 0 || mv.Date.Date > baseDate.Date) continue;
            var io = (mv.Io ?? string.Empty).Trim();
            if (io is not ("1" or "2")) continue;
            if (mv.Qty == 0m)
            {
                if (mv.Amount != 0m)
                {
                    zeroLines++;
                    zeroAmount += mv.Amount;
                }
                continue;
            }
            moveLines++;

            var pum = (mv.Pum ?? string.Empty).Trim();
            var ku = (mv.Ku ?? string.Empty).Trim();
            var chang = (mv.Chang ?? string.Empty).Trim();
            var whKey = LegacyMdbMapping.StockItemKey(pum, ku, chang);
            if (!state.TryGetValue(whKey, out var st))
            {
                state[whKey] = st = new CarryState(fromYm, LegacyMdbMapping.StockItemKey(pum, ku), !IsDefaultWarehouse(chang));
            }
            if (!st.Variants.Contains((pum, ku))) st.Variants.Add((pum, ku));

            if (!buckets.TryGetValue(mYm, out var perKey))
                buckets[mYm] = perKey = new Dictionary<string, (decimal, decimal, decimal)>(StringComparer.Ordinal);
            var b = perKey.TryGetValue(whKey, out var bv) ? bv : (0m, 0m, 0m);
            perKey[whKey] = io == "1"
                ? (b.Item1 + mv.Qty, b.Item2 + mv.Amount, b.Item3)
                : (b.Item1, b.Item2, b.Item3 + mv.Qty);
        }

        foreach (var ym in months)
        {
            buckets.TryGetValue(ym, out var perKey);
            foreach (var (whKey, st) in state)
            {
                var (inQ, inA, outQ) = perKey is not null && perKey.TryGetValue(whKey, out var bv) ? bv : (0m, 0m, 0m);
                decimal dan;
                if (inQ != 0m && st.Qty + inQ != 0m)
                    dan = (st.Amt + inA) / (st.Qty + inQ);
                else if (masterIdanByItemKey.TryGetValue(st.ItemKey, out var idan) && idan != 0m)
                    dan = idan;
                else
                    dan = st.Dan;

                var closing = st.Qty + inQ - outQ;
                st.Qty = closing;
                st.Dan = dan;
                st.Amt = Math.Round(closing * dan, 0, MidpointRounding.AwayFromZero);
            }
        }

        var byItem = new Dictionary<string, (decimal Qty, decimal Amt, List<(string Pum, string Ku)> Variants, int Lines)>(StringComparer.Ordinal);
        int nonDefault = 0;
        foreach (var st in state.Values)
        {
            if (st.NonDefault) nonDefault++;
            if (!byItem.TryGetValue(st.ItemKey, out var it))
                it = (0m, 0m, new List<(string, string)>(), 0);
            foreach (var v in st.Variants)
                if (!it.Variants.Contains(v)) it.Variants.Add(v);
            byItem[st.ItemKey] = (it.Qty + st.Qty, it.Amt + st.Amt, it.Variants, it.Lines + 1);
        }

        var lines = byItem
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new FinalStockLine(kv.Key, kv.Value.Qty, kv.Value.Amt, kv.Value.Variants, kv.Value.Lines))
            .ToList();
        var maxYm = months.Count > 0 ? toYm : fromYm;
        return new RollForwardResult(
            new FinalStockResult(maxYm, lines, skipped, nonDefault),
            new RollForwardInfo(fromYm, toYm, months.Count, moveLines, zeroLines, zeroAmount));
    }

    /// <summary>이어 계산 중 (품명·규격·창고) 키 하나의 기말 상태.</summary>
    private sealed class CarryState(string ym, string itemKey, bool nonDefault)
    {
        public string Ym { get; } = ym;
        public string ItemKey { get; } = itemKey;
        public bool NonDefault { get; } = nonDefault;
        public decimal Qty { get; set; }
        public decimal Amt { get; set; }
        public decimal Dan { get; set; }
        public List<(string Pum, string Ku)> Variants { get; } = new();
    }

    private static string NextYm(string ym)
    {
        var y = int.Parse(ym.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture);
        var m = int.Parse(ym.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture);
        if (++m > 12) { m = 1; y++; }
        return y.ToString("D4", CultureInfo.InvariantCulture) + m.ToString("D2", CultureInfo.InvariantCulture);
    }

    // ══════════════════════════════════════════════════════════════
    // ① 맞춤 줄 — item_stock_rebuild 앞
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 계약 시그니처 — 자기 트랜잭션을 연다(<paramref name="db"/> 는 트랜잭션이 걸려 있지 않은 연결이어야 한다).
    /// 반환 = 새로 들어간 맞춤 줄 수(재실행 0).
    /// </summary>
    public static Task<int> AdjustStockAsync(
        IDbConnection db, string tenantId, DataTable? docfc, DateTime baseDate,
        Func<string, string, IDbTransaction?, CancellationToken, Task<string>> ensureItem,
        CancellationToken ct)
        => AdjustStockAsync(db, null, tenantId, docfc, baseDate, ensureItem, null, ct);

    /// <summary>
    /// <paramref name="tx"/> 가 있으면 그 트랜잭션 안에서 돌고(커밋은 호출자), null 이면 자기 트랜잭션을 연다.
    /// <paramref name="logger"/> null 이면 경고를 남길 곳이 없다 — 이관 서비스는 넘겨라.
    /// </summary>
    public static async Task<int> AdjustStockAsync(
        IDbConnection db, IDbTransaction? tx, string tenantId, DataTable? docfc, DateTime baseDate,
        Func<string, string, IDbTransaction?, CancellationToken, Task<string>> ensureItem,
        ILogger? logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(ensureItem);

        var final = ComputeFinalStock(docfc);
        if (final is null)
        {
            logger?.LogWarning(
                "[MDB마이그레이션] 최종재고 표 없음(DOCFC 없음·0행·유효 달 0) — 이전 프로그램 최종재고 맞춤 skip · 재고는 이력 누계로 이관 (테넌트 {TenantId})",
                tenantId);
            return 0;
        }
        if (final.SkippedRows > 0)
            logger?.LogWarning("[MDB마이그레이션] DOCFC IM_YM 이 6자리 달이 아닌 {Skipped}행 — 최종재고 계산에서 제외", final.SkippedRows);

        return await AdjustStockAsync(db, tx, tenantId, docfc, final, baseDate, ensureItem, logger, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 🆕 20260915작1 3판 R3 — 목표 재고를 <paramref name="final"/>(DOCFC 최종 또는 <see cref="RollForward"/> 결과)로 받는 오버로드.
    /// 맞춤 줄 source_id·해시 달 = <paramref name="final"/>.MaxYm. <paramref name="docfc"/> 는 ③ 끝전에 품목 기록을 넘기는 자리(없으면 null).
    /// </summary>
    public static async Task<int> AdjustStockAsync(
        IDbConnection db, IDbTransaction? tx, string tenantId, DataTable? docfc, FinalStockResult final,
        DateTime baseDate, Func<string, string, IDbTransaction?, CancellationToken, Task<string>> ensureItem,
        ILogger? logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(final);
        ArgumentNullException.ThrowIfNull(ensureItem);

        return await InTransactionAsync(db, tx, async t =>
        {
            var warehouseId = await WarehouseLookup.ResolveTenantDefaultWarehouseAsync(db, tenantId, t, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(warehouseId))
            {
                logger?.LogWarning("[MDB마이그레이션] 기본창고 없음 — 최종재고 맞춤 skip (테넌트 {TenantId})", tenantId);
                return 0;
            }

            // R8 — DOCFC 품목 전부 등록(수량 0 포함) · 대소문자만 다른 표기는 각각 ensureItem → 가장 먼저 등록된 품목 하나로.
            // 대소문자·공백만 다른 품목이 히트판에 이미 여럿일 수 있다(갈래 A 발견 · 이관 매핑 키는 대소문자 보존) →
            // 후보 = ensureItem 이 돌려준 품목 ∪ 품명·규격 재고 키가 같은 기존 품목 · 그중 가장 먼저 등록된 하나.
            var nameIndex = await LoadItemNameIndexAsync(db, t, tenantId, ct).ConfigureAwait(false);
            var itemMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var target = new Dictionary<string, decimal>(StringComparer.Ordinal);
            int multiCandidate = 0;
            foreach (var line in final.Lines)
            {
                ct.ThrowIfCancellationRequested();
                var ids = new List<string>();
                foreach (var (pum, ku) in line.Variants)
                {
                    var id = await ensureItem(pum, ku, t, ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(id) && !ids.Contains(id)) ids.Add(id);
                }
                if (ids.Count == 0)
                {
                    logger?.LogWarning("[MDB마이그레이션] 최종재고 품목 등록 실패 — 키 {Key} 수량 {Qty} 맞춤 제외", line.Key, line.Qty);
                    continue;
                }
                if (nameIndex.TryGetValue(line.Key, out var same))
                    foreach (var s in same)
                        if (!ids.Contains(s.ItemId)) ids.Add(s.ItemId);

                var chosen = ids[0];
                if (ids.Count > 1)
                {
                    multiCandidate++;
                    var cands = await db.QueryAsync<(string ItemId, DateTime CreatedAt)>(new CommandDefinition(
                        "SELECT item_id AS ItemId, created_at AS CreatedAt FROM items WHERE tenant_id = @TenantId AND item_id IN @Ids",
                        new { TenantId = tenantId, Ids = ids }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);
                    chosen = ChooseItem(cands) ?? ids.OrderBy(x => x, StringComparer.Ordinal).First();
                }

                itemMap[line.Key] = chosen;
                target[chosen] = (target.TryGetValue(chosen, out var tv) ? tv : 0m) + line.Qty;
            }

            if (docfc is not null)
                docfc.ExtendedProperties[ItemMapPropertyPrefix + tenantId] = itemMap;

            // 사람 입력 원장 제외 — source_type='migration'(이력 + 맞춤 줄)만 합한다.
            var netRows = await db.QueryAsync<(string ItemId, decimal Net)>(new CommandDefinition(
                """
                SELECT item_id AS ItemId, SUM(qty_in) - SUM(qty_out) AS Net
                FROM stock_ledger
                WHERE tenant_id = @TenantId AND source_type = 'migration'
                GROUP BY item_id
                """,
                new { TenantId = tenantId }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);
            var ledgerNet = netRows.ToDictionary(r => r.ItemId, r => r.Net, StringComparer.Ordinal);

            var adjustments = ComputeAdjustments(target, ledgerNet);
            var sourceId = AdjustmentSourceId(final.MaxYm);
            const string insertSql = """
                INSERT IGNORE INTO stock_ledger
                  (tenant_id, item_id, warehouse_id, partner_id, ledger_date, ym,
                   move_type, source_type, source_id, doc_no, qty_in, qty_out,
                   unit_cost, supply_amount, memo, migrated_source_hash)
                VALUES
                  (@TenantId, @ItemId, @WarehouseId, NULL, @LedgerDate, @Ym,
                   @MoveType, 'migration', @SourceId, NULL, @QtyIn, @QtyOut,
                   NULL, NULL, @Memo, @Hash)
                """;

            int inserted = 0, blocked = 0;
            decimal sumDiff = 0m, blockedQty = 0m;
            foreach (var (itemId, diff) in adjustments)
            {
                ct.ThrowIfCancellationRequested();
                var (moveType, qtyIn, qtyOut) = AdjustmentMove(diff);
                var n = await db.ExecuteAsync(new CommandDefinition(insertSql, new
                {
                    TenantId = tenantId,
                    ItemId = itemId,
                    WarehouseId = warehouseId,
                    LedgerDate = baseDate.Date,
                    Ym = baseDate.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    MoveType = moveType,
                    SourceId = sourceId,
                    QtyIn = qtyIn,
                    QtyOut = qtyOut,
                    Memo = AdjustmentMemo,
                    Hash = AdjustmentHash(final.MaxYm, itemId, moveType),
                }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);
                if (n > 0)
                {
                    inserted += n;
                    sumDiff += diff;
                }
                else
                {
                    blocked++;
                    blockedQty += diff;
                }
            }

            if (blocked > 0)
            {
                // 같은 달 맞춤 줄이 같은 방향으로 이미 있는데 차이가 또 났다(다른 백업 병합 등) — UNIQUE 에 막혀 조용히 틀어지지 않게 드러낸다.
                logger?.LogWarning(
                    "[MDB마이그레이션] 최종재고 맞춤 줄 {Blocked}품목이 같은 달({Ym}) 기존 맞춤 줄에 막혀 못 들어갔다 — 남은 차이 합 {Qty} · 덮어쓰기로 다시 가져오기가 필요합니다",
                    blocked, final.MaxYm, blockedQty);
            }

            logger?.LogInformation(
                "[MDB마이그레이션] 이전 프로그램 최종재고 맞춤 — DOCFC {Ym} 품목 {Items} · 창고 00 외 {NonDefault}줄 기본창고 합산 · 표기 여럿 {Multi}품목 · 맞춤 줄 {Inserted}행 수량합 {Sum}",
                final.MaxYm, final.Lines.Count, final.NonDefaultWarehouseLines, multiCandidate, inserted, sumDiff);
            return inserted;
        }, ct).ConfigureAwait(false);
    }

    // ══════════════════════════════════════════════════════════════
    // ③ 끝전 — item_stock_rebuild 뒤
    // ══════════════════════════════════════════════════════════════

    /// <summary>계약 시그니처 — 자기 트랜잭션을 연다. 반환 = avg_cost 를 고친 item_stock 행 수.</summary>
    public static Task<int> ApplyFinalCostAsync(IDbConnection db, string tenantId, DataTable? docfc, CancellationToken ct)
        => ApplyFinalCostAsync(db, null, tenantId, docfc, null, ct);

    /// <summary>
    /// DOCFC 최종 수량≠0 품목: 기본창고 item_stock 행 <c>avg_cost = ROUND(CAMT/CQTY, 6)</c>.
    /// 수량 0 · 금액≠0 품목은 단가로 표현할 수 없어 <b>건드리지 않고</b> 건수·금액을 경고로 남긴다.
    /// </summary>
    public static async Task<int> ApplyFinalCostAsync(
        IDbConnection db, IDbTransaction? tx, string tenantId, DataTable? docfc, ILogger? logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var final = ComputeFinalStock(docfc);
        if (final is null)
        {
            logger?.LogWarning("[MDB마이그레이션] 최종재고 표 없음 — 재고 금액 끝전 맞춤 skip (테넌트 {TenantId})", tenantId);
            return 0;
        }

        return await ApplyFinalCostAsync(db, tx, tenantId, docfc, final, logger, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 🆕 20260915작1 3판 R3 — 끝전 목표를 <paramref name="final"/>(DOCFC 최종 또는 <see cref="RollForward"/> 결과)로 받는 오버로드.
    /// 품목 기록은 ① 과 같은 <paramref name="docfc"/> 인스턴스에서 읽는다(없으면 품명·규격 폴백).
    /// </summary>
    public static async Task<int> ApplyFinalCostAsync(
        IDbConnection db, IDbTransaction? tx, string tenantId, DataTable? docfc, FinalStockResult final,
        ILogger? logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(final);

        return await InTransactionAsync(db, tx, async t =>
        {
            var warehouseId = await WarehouseLookup.ResolveTenantDefaultWarehouseAsync(db, tenantId, t, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(warehouseId))
            {
                logger?.LogWarning("[MDB마이그레이션] 기본창고 없음 — 재고 금액 끝전 맞춤 skip (테넌트 {TenantId})", tenantId);
                return 0;
            }

            var itemMap = docfc?.ExtendedProperties[ItemMapPropertyPrefix + tenantId] as IReadOnlyDictionary<string, string>;
            if (itemMap is null)
            {
                logger?.LogWarning("[MDB마이그레이션] 최종재고 맞춤 품목 기록이 없다(같은 DOCFC 표로 맞춤 단계를 먼저 부르지 않음) — 품명·규격으로 찾는다");
                itemMap = await ResolveItemsByNameAsync(db, t, tenantId, ct).ConfigureAwait(false);
            }

            var perItem = new Dictionary<string, (decimal Qty, decimal Amt)>(StringComparer.Ordinal);
            int unresolved = 0, zeroQtyWithAmount = 0;
            decimal zeroQtyAmount = 0m;
            foreach (var line in final.Lines)
            {
                if (!itemMap.TryGetValue(line.Key, out var itemId))
                {
                    if (line.Qty != 0m || line.Amount != 0m) unresolved++;
                    continue;
                }
                var cur = perItem.TryGetValue(itemId, out var c) ? c : (0m, 0m);
                perItem[itemId] = (cur.Item1 + line.Qty, cur.Item2 + line.Amount);
            }

            const string updateSql = """
                UPDATE item_stock
                   SET avg_cost = @AvgCost, last_updated_at = NOW(6)
                 WHERE tenant_id = @TenantId AND item_id = @ItemId AND warehouse_id = @WarehouseId
                """;
            int updated = 0, missingRow = 0;
            foreach (var (itemId, (qty, amt)) in perItem.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value)))
            {
                ct.ThrowIfCancellationRequested();
                var cost = FinalUnitCost(qty, amt);
                if (cost is null)
                {
                    if (amt != 0m)
                    {
                        zeroQtyWithAmount++;
                        zeroQtyAmount += amt;
                    }
                    continue;
                }
                var n = await db.ExecuteAsync(new CommandDefinition(updateSql,
                    new { AvgCost = cost.Value, TenantId = tenantId, ItemId = itemId, WarehouseId = warehouseId },
                    transaction: t, cancellationToken: ct)).ConfigureAwait(false);
                if (n > 0) updated += n; else missingRow++;
            }

            if (zeroQtyWithAmount > 0)
                logger?.LogWarning(
                    "[MDB마이그레이션] 최종 수량 0 인데 금액이 있는 품목 {Count}건(금액 합 {Amount}) — 재고 금액(수량×단가)으로 표현할 수 없어 단가를 안 바꿨다",
                    zeroQtyWithAmount, zeroQtyAmount);
            if (unresolved > 0 || missingRow > 0)
                logger?.LogWarning(
                    "[MDB마이그레이션] 재고 금액 끝전 맞춤 — 품목 못 찾음 {Unresolved}건 · 기본창고 재고 행 없음 {Missing}건",
                    unresolved, missingRow);
            logger?.LogInformation("[MDB마이그레이션] 재고 금액 끝전 맞춤 — item_stock {Updated}행", updated);
            return updated;
        }, ct).ConfigureAwait(false);
    }

    // ══════════════════════════════════════════════════════════════
    // 내부
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 폴백 — ① 기록이 없을 때 items 의 품명·규격으로 재고 키를 만든다(가장 먼저 등록된 품목).
    /// item_name 에 <c>품명|규격</c> 이 통째로 들어간 옛 등록분(spec NULL)은 첫 <c>|</c> 로 나눈다. 50자 넘게 잘린 품명은 못 찾는다.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, string>> ResolveItemsByNameAsync(
        IDbConnection db, IDbTransaction t, string tenantId, CancellationToken ct)
    {
        var index = await LoadItemNameIndexAsync(db, t, tenantId, ct).ConfigureAwait(false);
        return index.ToDictionary(kv => kv.Key, kv => ChooseItem(kv.Value)!, StringComparer.Ordinal);
    }

    /// <summary>items → (재고 키 → [(item_id, created_at)]). 삭제 표시 품목 제외.</summary>
    private static async Task<Dictionary<string, List<(string ItemId, DateTime CreatedAt)>>> LoadItemNameIndexAsync(
        IDbConnection db, IDbTransaction t, string tenantId, CancellationToken ct)
    {
        var rows = await db.QueryAsync<(string ItemId, string ItemName, string? Spec, DateTime CreatedAt)>(new CommandDefinition(
            "SELECT item_id AS ItemId, item_name AS ItemName, spec AS Spec, created_at AS CreatedAt FROM items WHERE tenant_id = @TenantId AND is_deleted = 0",
            new { TenantId = tenantId }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

        var index = new Dictionary<string, List<(string ItemId, DateTime CreatedAt)>>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            var key = ItemNameKey(r.ItemName, r.Spec);
            if (!index.TryGetValue(key, out var list)) index[key] = list = new List<(string, DateTime)>();
            list.Add((r.ItemId, r.CreatedAt));
        }
        return index;
    }

    /// <summary>
    /// 품목 마스터 → 재고 키. item_name 에 <c>품명|규격</c> 이 통째로 들어간 옛 등록분(spec 빈칸)은 첫 <c>|</c> 로 나눈다(작22 C2 ⑤).
    /// </summary>
    public static string ItemNameKey(string? itemName, string? spec)
    {
        var name = itemName ?? string.Empty;
        if (string.IsNullOrEmpty(spec))
        {
            var sep = name.IndexOf('|');
            if (sep >= 0)
            {
                spec = name[(sep + 1)..];
                name = name[..sep];
            }
        }
        return LegacyMdbMapping.StockItemKey(name, spec);
    }

    private static async Task<int> InTransactionAsync(
        IDbConnection db, IDbTransaction? tx, Func<IDbTransaction, Task<int>> work, CancellationToken ct)
    {
        if (tx is not null) return await work(tx).ConfigureAwait(false);

        if (db.State != ConnectionState.Open)
        {
            if (db is System.Data.Common.DbConnection dbc) await dbc.OpenAsync(ct).ConfigureAwait(false);
            else db.Open();
        }
        using var own = db.BeginTransaction();
        var result = await work(own).ConfigureAwait(false);
        own.Commit();
        return result;
    }

    private static bool IsYm(string ym)
        => ym.Length == 6 && ym.All(char.IsAsciiDigit);

    /// <summary>레거시 기본 창고 = <c>00</c> 또는 빈칸.</summary>
    private static bool IsDefaultWarehouse(string chang)
        => chang.Length == 0 || chang.All(ch => ch == '0');

    private static string Str(DataRow row, string col)
    {
        if (!row.Table.Columns.Contains(col)) return string.Empty;
        var v = row[col];
        return v == DBNull.Value ? string.Empty : Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static decimal Dec(DataRow row, string col)
    {
        if (!row.Table.Columns.Contains(col)) return 0m;
        var v = row[col];
        if (v == DBNull.Value) return 0m;
        return v is string s
            ? (decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m)
            : Convert.ToDecimal(v, CultureInfo.InvariantCulture);
    }
}
