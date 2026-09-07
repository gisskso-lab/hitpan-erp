using System.Data;
using System.Data.Common;
using System.Data.OleDb;
using System.Globalization;
using System.Runtime.Versioning;
using Dapper;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 레거시 히트판(MDB) ↔ 히트판 ERP 대사표 서비스 (20260904작21 갈래 B1).
///
/// 레거시가 스스로 계산해 둔 값(DOCFE 헤더 합·DOCFC 월별 기말 등)을 정답 칸에 두고,
/// 이관된 ERP 쪽 숫자를 나란히 놓아 항목별 OK / DIFF / NA 를 판정한다.
///
/// 원칙
///  · MDB 는 <c>Mode=Read</c> 로만 연다. 쓰지 않는다.
///  · 큰 표(DOCF5 61만행 등)는 GROUP BY 를 MDB 쪽에서 돌려 결과만 받는다 — 전체 행을 메모리에 올리지 않는다.
///  · 항목 하나가 실패해도 그 항목만 NA 로 남기고 나머지는 계속한다 (헌법 #15 — 빈 catch 없음, 사유는 Detail·로그에).
///  · 방향: <c>IO=1 = 매입 · IO=2 = 매출</c> (전결1 Q1). ERP <c>tax_invoices.direction</c> 은 'S'(매출)/'B'(매입).
///  · 계열(전결1 Q2): 수금 = S_GU 1~5 의 ΣS_SUK · 지급 = S_GU B~F 의 ΣS_BAL ·
///    거래처별 잔액 = ΣS_BAL − ΣS_SUK(전 코드) → 양수 합 = 미수 · 음수 합의 절대값 = 미지급.
///  · 차이(Diff) = 히트판 − 레거시.
///  · 금액은 전부 decimal (헌법 #4). 주민번호 등 PII 컬럼은 읽지 않는다 (헌법 #5).
///  · <see cref="MdbMigrationService"/> 는 손대지 않는다 — 경로 해석·해시는 같은 규칙으로 자체 구현.
/// Windows 전용: Microsoft.ACE.OLEDB.12.0 (없으면 16.0) Provider 필요.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MdbReconciliationService
{
    private readonly IDbConnection _db;
    private readonly ILogger<MdbReconciliationService> _logger;

    /// <summary>OLEDB Provider 이름 (읽기 전용으로 연다). 12.0 이 없으면 16.0 으로 한 번 더 시도한다.</summary>
    private const string OleDbProvider12 = "Microsoft.ACE.OLEDB.12.0";
    private const string OleDbProvider16 = "Microsoft.ACE.OLEDB.16.0";

    /// <summary>
    /// 연결문자열은 <see cref="OleDbConnectionStringBuilder"/> 로만 조립한다 ([3-V] 2026-09-08 · CodeQL cs/resource-injection · 병렬이슈 14).
    /// 경로·비번은 사용자 입력이라 문자열 포맷으로 이으면 <c>;</c> 로 키(Mode 등)를 덧붙일 수 있다 — 빌더가 값을 따옴표로 감싼다.
    /// 경로는 <see cref="ResolveMdbPaths"/> 가 절대경로·<c>..</c> 차단·존재 확인·고정 파일명으로 이미 좁혔다.
    /// </summary>
    private static string BuildConnectionString(string provider, string mdbPath, string password)
    {
        var b = new OleDbConnectionStringBuilder { Provider = provider, DataSource = mdbPath };
        b["Mode"] = "Read";
        if (!string.IsNullOrEmpty(password)) b["Jet OLEDB:Database Password"] = password;
        return b.ConnectionString;
    }

    /// <summary>거래처·품목 차이 목록 상위 N.</summary>
    private const int TopDiffRows = 20;

    /// <summary>
    /// ERP 쪽 집계 SQL 의 명령 타임아웃(초). 기본 30초는 이관 직후(수금 61만행 등) 합계에 모자라
    /// 첫 실측에서 「Command Timeout expired」 로 NA 가 났다. 대사는 관리자 1회성 작업이라 넉넉히 잡는다.
    /// </summary>
    private const int ErpCommandTimeoutSec = 180;

    private const string StatusOk = "OK";
    private const string StatusDiff = "DIFF";
    private const string StatusNa = "NA";

    public MdbReconciliationService(IDbConnection db, ILogger<MdbReconciliationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ────────────────────────────────────────────────────────────────
    // 공개 메서드
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 대사표를 만든다. 레거시 쪽은 MDB 를 직접 읽고, ERP 쪽은 현재 테넌트의 이관 결과를 읽는다.
    /// </summary>
    /// <param name="folderPath">PYOJUN.MDB / PANDATA.mdb 가 있는 폴더 (절대경로).</param>
    /// <param name="mdbPassword">MDB 비밀번호 (없으면 null).</param>
    /// <param name="tenantId">JWT 클레임에서 온 tenant_id (헌법 #2).</param>
    /// <param name="ct">취소 토큰.</param>
    public async Task<MdbReconciliationReport> BuildAsync(
        string folderPath, string? mdbPassword, string tenantId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new InvalidOperationException("tenant 정보가 없습니다.");

        var (pyojunPath, pandataPath, _) = ResolveMdbPaths(folderPath);

        // MDB 를 못 열면(비번 틀림·엔진 없음) 대사 자체가 불가 — 항목별 NA 가 아니라 호출자에게 그대로 올린다.
        using var pandata = OpenMdb(pandataPath, mdbPassword);
        using var pyojun = OpenMdb(pyojunPath, mdbPassword);

        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var report = new MdbReconciliationReport { GeneratedAt = DateTime.Now };
        var items = report.Items;

        // ① 판매 · ② 매입 (DOCFE 헤더 ↔ sales_deliveries / purchase_receipts)
        await AddDeliveryItemsAsync(pandata, tenantId, items, ct).ConfigureAwait(false);

        // ③ 세금계산서 (DOCF4 ↔ tax_invoices)
        await AddTaxInvoiceItemsAsync(pandata, tenantId, items, ct).ConfigureAwait(false);

        // ④ 재고원장 (DOCFB ↔ stock_ledger)
        await AddLedgerItemsAsync(pandata, tenantId, items, ct).ConfigureAwait(false);

        // ⑤ 재고 기말수량 (DOCFC 최신월 ↔ item_stock) + 품목별 차이 상위 20
        await AddStockItemsAsync(pandata, tenantId, items, report.ItemDiffs, ct).ConfigureAwait(false);

        // ⑥ 회계 분개 (DOCF7 ↔ journal_lines)
        await AddJournalItemsAsync(pandata, tenantId, items, ct).ConfigureAwait(false);

        // ⑦ 수금 · ⑧ 미수금 · ⑨ 미지급금 (DOCF5 ↔ collections / 업체별원장 식) + 거래처별 차이 상위 20
        await AddPartnerLedgerItemsAsync(pandata, pyojun, tenantId, items, report.PartnerDiffs, ct).ConfigureAwait(false);

        // ⑩ 마스터 4종 건수
        await AddMasterItemsAsync(pyojun, tenantId, items, ct).ConfigureAwait(false);

        report.OkCount = items.Count(i => i.Status == StatusOk);
        report.DiffCount = items.Count(i => i.Status == StatusDiff);

        _logger.LogInformation("[MDB대사] tenant={Tenant} 항목={Total} OK={Ok} DIFF={Diff} NA={Na}",
            tenantId, items.Count, report.OkCount, report.DiffCount,
            items.Count - report.OkCount - report.DiffCount);
        return report;
    }

    // ────────────────────────────────────────────────────────────────
    // ① ② 판매·매입 거래명세서
    // ────────────────────────────────────────────────────────────────

    private sealed class DeliveryLegacyAgg
    {
        public long SalesCount;
        public decimal SalesSupply;
        public decimal SalesVat;
        public long PurchaseCount;
        public decimal PurchaseSupply;
        public decimal PurchaseVat;
        public long SalesHeaderGroups;     // DOCFB (DT,IO,SEQ,BUY) DISTINCT · IO=2
        public long PurchaseHeaderGroups;  // IO=1
    }

    private sealed class DeliveryErpAgg
    {
        public long Cnt { get; set; }
        public decimal Supply { get; set; }
        public decimal Vat { get; set; }
        public long LineCount { get; set; }   // 별칭 Lines/Rows 는 MariaDB 예약어 — 쓰지 않는다
    }

    private async Task AddDeliveryItemsAsync(
        OleDbConnection pandata, string tenantId, List<ReconItem> items, CancellationToken ct)
    {
        var (legacy, legacyErr) = await GuardAsync("DOCFE", async () =>
        {
            var agg = new DeliveryLegacyAgg();
            await ReadMdbAsync(pandata,
                "SELECT IJA_IO, COUNT(*) AS n, SUM(IJA_AMT1) AS amt1, SUM(IJA_AMT2) AS amt2 FROM DOCFE GROUP BY IJA_IO",
                r =>
                {
                    var io = ReadStr(r, 0);
                    if (io == "2") { agg.SalesCount = ReadLong(r, 1); agg.SalesSupply = ReadDec(r, 2); agg.SalesVat = ReadDec(r, 3); }
                    else if (io == "1") { agg.PurchaseCount = ReadLong(r, 1); agg.PurchaseSupply = ReadDec(r, 2); agg.PurchaseVat = ReadDec(r, 3); }
                }, ct).ConfigureAwait(false);
            await ReadMdbAsync(pandata,
                "SELECT h.IJ_IO, COUNT(*) AS n FROM (SELECT DISTINCT IJ_DT, IJ_IO, IJ_SEQ, IJ_BUY FROM DOCFB) AS h GROUP BY h.IJ_IO",
                r =>
                {
                    var io = ReadStr(r, 0);
                    if (io == "2") agg.SalesHeaderGroups = ReadLong(r, 1);
                    else if (io == "1") agg.PurchaseHeaderGroups = ReadLong(r, 1);
                }, ct).ConfigureAwait(false);
            return agg;
        }).ConfigureAwait(false);

        // DESCRIBE sales_deliveries (2026-09-04, hitpan_e2e): total_amount·vat_amount·source_type·is_deleted 실재.
        var (sales, salesErr) = await GuardAsync("sales_deliveries", () =>
            _db.QuerySingleAsync<DeliveryErpAgg>(new CommandDefinition(
                """
                SELECT COUNT(*) AS Cnt,
                       COALESCE(SUM(total_amount), 0) AS Supply,
                       COALESCE(SUM(vat_amount), 0)   AS Vat,
                       (SELECT COUNT(*) FROM sales_delivery_items i
                          JOIN sales_deliveries d2 ON d2.delivery_id = i.delivery_id AND d2.tenant_id = i.tenant_id
                         WHERE d2.tenant_id = @T AND d2.source_type = 'migration' AND d2.is_deleted = 0) AS LineCount
                  FROM sales_deliveries
                 WHERE tenant_id = @T AND source_type = 'migration' AND is_deleted = 0
                """, new { T = tenantId }, commandTimeout: ErpCommandTimeoutSec, cancellationToken: ct))).ConfigureAwait(false);

        // DESCRIBE purchase_receipts: is_deleted 컬럼 없음 — status 만 있다. 이관 행은 source_type 으로 격리.
        var (purchase, purchaseErr) = await GuardAsync("purchase_receipts", () =>
            _db.QuerySingleAsync<DeliveryErpAgg>(new CommandDefinition(
                """
                SELECT COUNT(*) AS Cnt,
                       COALESCE(SUM(total_amount), 0) AS Supply,
                       COALESCE(SUM(vat_amount), 0)   AS Vat,
                       (SELECT COUNT(*) FROM purchase_receipt_items i
                          JOIN purchase_receipts r2 ON r2.receipt_id = i.receipt_id AND r2.tenant_id = i.tenant_id
                         WHERE r2.tenant_id = @T AND r2.source_type = 'migration') AS LineCount
                  FROM purchase_receipts
                 WHERE tenant_id = @T AND source_type = 'migration'
                """, new { T = tenantId }, commandTimeout: ErpCommandTimeoutSec, cancellationToken: ct))).ConfigureAwait(false);

        var salesDetail = JoinDetail(
            legacy is null ? null : $"레거시 DOCFB 헤더그룹 {legacy.SalesHeaderGroups:N0}",
            sales is null ? null : $"히트판 라인 {sales.LineCount:N0}",
            legacyErr, salesErr);
        items.Add(Make("sales_count", "판매", "판매(거래명세서) 건수", legacy?.SalesCount, sales?.Cnt, salesDetail));
        items.Add(Make("sales_supply", "판매", "판매(거래명세서) 공급가액", legacy?.SalesSupply, sales?.Supply, ErrOnly(legacyErr, salesErr)));
        items.Add(Make("sales_vat", "판매", "판매(거래명세서) 부가세", legacy?.SalesVat, sales?.Vat, ErrOnly(legacyErr, salesErr)));

        var purchaseDetail = JoinDetail(
            legacy is null ? null : $"레거시 DOCFB 헤더그룹 {legacy.PurchaseHeaderGroups:N0}",
            purchase is null ? null : $"히트판 라인 {purchase.LineCount:N0}",
            legacyErr, purchaseErr);
        items.Add(Make("purchase_count", "매입", "매입(거래명세서) 건수", legacy?.PurchaseCount, purchase?.Cnt, purchaseDetail));
        items.Add(Make("purchase_supply", "매입", "매입(거래명세서) 공급가액", legacy?.PurchaseSupply, purchase?.Supply, ErrOnly(legacyErr, purchaseErr)));
        items.Add(Make("purchase_vat", "매입", "매입(거래명세서) 부가세", legacy?.PurchaseVat, purchase?.Vat, ErrOnly(legacyErr, purchaseErr)));
    }

    // ────────────────────────────────────────────────────────────────
    // ③ 세금계산서
    // ────────────────────────────────────────────────────────────────

    private sealed class TaxAgg
    {
        public long SalesCount;
        public decimal SalesSupply;
        public decimal SalesVat;
        public long PurchaseCount;
        public decimal PurchaseSupply;
        public decimal PurchaseVat;
    }

    private sealed class TaxErpRow
    {
        public string? Direction { get; set; }
        public long Cnt { get; set; }
        public decimal Supply { get; set; }
        public decimal Vat { get; set; }
    }

    private async Task AddTaxInvoiceItemsAsync(
        OleDbConnection pandata, string tenantId, List<ReconItem> items, CancellationToken ct)
    {
        var (legacy, legacyErr) = await GuardAsync("DOCF4", async () =>
        {
            var agg = new TaxAgg();
            // Access SQL: CASE 없음 → IIF. NULL 칸이 있으면 행 전체 합이 NULL 로 빠지므로 0 으로 막는다.
            await ReadMdbAsync(pandata,
                "SELECT TX_IO, COUNT(*) AS n, " +
                "SUM(IIF(TX_KUM1 IS NULL,0,TX_KUM1)+IIF(TX_KUM2 IS NULL,0,TX_KUM2)+IIF(TX_KUM3 IS NULL,0,TX_KUM3)+IIF(TX_KUM4 IS NULL,0,TX_KUM4)) AS kum, " +
                "SUM(IIF(TX_VAT1 IS NULL,0,TX_VAT1)+IIF(TX_VAT2 IS NULL,0,TX_VAT2)+IIF(TX_VAT3 IS NULL,0,TX_VAT3)+IIF(TX_VAT4 IS NULL,0,TX_VAT4)) AS vat " +
                "FROM DOCF4 GROUP BY TX_IO",
                r =>
                {
                    var io = ReadStr(r, 0);
                    if (io == "2") { agg.SalesCount = ReadLong(r, 1); agg.SalesSupply = ReadDec(r, 2); agg.SalesVat = ReadDec(r, 3); }
                    else if (io == "1") { agg.PurchaseCount = ReadLong(r, 1); agg.PurchaseSupply = ReadDec(r, 2); agg.PurchaseVat = ReadDec(r, 3); }
                }, ct).ConfigureAwait(false);
            return agg;
        }).ConfigureAwait(false);

        // DESCRIBE tax_invoices: direction char(1) 'S'=매출 / 'B'=매입 (전결1 D2) · amount_total · vat_total · source_type.
        var (erpRows, erpErr) = await GuardAsync("tax_invoices", async () =>
            (await _db.QueryAsync<TaxErpRow>(new CommandDefinition(
                """
                SELECT direction AS Direction, COUNT(*) AS Cnt,
                       COALESCE(SUM(amount_total), 0) AS Supply,
                       COALESCE(SUM(vat_total), 0)    AS Vat
                  FROM tax_invoices
                 WHERE tenant_id = @T AND source_type = 'migration'
                 GROUP BY direction
                """, new { T = tenantId }, commandTimeout: ErpCommandTimeoutSec, cancellationToken: ct)).ConfigureAwait(false)).ToList()).ConfigureAwait(false);

        TaxErpRow? erpSales = null, erpPurchase = null;
        string? otherDirections = null;
        if (erpRows is not null)
        {
            erpSales = erpRows.FirstOrDefault(x => x.Direction == "S") ?? new TaxErpRow();
            erpPurchase = erpRows.FirstOrDefault(x => x.Direction == "B") ?? new TaxErpRow();
            var others = erpRows.Where(x => x.Direction != "S" && x.Direction != "B").ToList();
            if (others.Count > 0)
                otherDirections = "히트판에 매출/매입 구분이 아닌 값 " + string.Join(", ", others.Select(o => $"'{o.Direction}' {o.Cnt:N0}건"));
        }

        var err = ErrOnly(legacyErr, erpErr);
        items.Add(Make("tax_sales_count", "계산서", "세금계산서(매출) 건수", legacy?.SalesCount, erpSales?.Cnt, JoinDetail(otherDirections, err)));
        items.Add(Make("tax_sales_supply", "계산서", "세금계산서(매출) 공급가액", legacy?.SalesSupply, erpSales?.Supply, err));
        items.Add(Make("tax_sales_vat", "계산서", "세금계산서(매출) 부가세", legacy?.SalesVat, erpSales?.Vat, err));
        items.Add(Make("tax_purchase_count", "계산서", "세금계산서(매입) 건수", legacy?.PurchaseCount, erpPurchase?.Cnt, JoinDetail(otherDirections, err)));
        items.Add(Make("tax_purchase_supply", "계산서", "세금계산서(매입) 공급가액", legacy?.PurchaseSupply, erpPurchase?.Supply, err));
        items.Add(Make("tax_purchase_vat", "계산서", "세금계산서(매입) 부가세", legacy?.PurchaseVat, erpPurchase?.Vat, err));
    }

    // ────────────────────────────────────────────────────────────────
    // ④ 재고원장
    // ────────────────────────────────────────────────────────────────

    private sealed class LedgerAgg
    {
        public long RowCount;
        public decimal QtyIn;   // IO=1 (매입) Σ
        public decimal QtyOut;  // IO=2 (매출) Σ
        public decimal Amount;
    }

    private sealed class LedgerErpAgg
    {
        public long RowCount { get; set; }
        public decimal QtyIn { get; set; }
        public decimal QtyOut { get; set; }
        public decimal Amount { get; set; }
    }

    private async Task AddLedgerItemsAsync(
        OleDbConnection pandata, string tenantId, List<ReconItem> items, CancellationToken ct)
    {
        var (legacy, legacyErr) = await GuardAsync("DOCFB", async () =>
        {
            var agg = new LedgerAgg();
            await ReadMdbAsync(pandata,
                "SELECT IJ_IO, COUNT(*) AS n, SUM(IJ_QTY) AS qty, SUM(IJ_AMT) AS amt FROM DOCFB GROUP BY IJ_IO",
                r =>
                {
                    var io = ReadStr(r, 0);
                    agg.RowCount += ReadLong(r, 1);
                    agg.Amount += ReadDec(r, 3);
                    if (io == "1") agg.QtyIn += ReadDec(r, 2);
                    else if (io == "2") agg.QtyOut += ReadDec(r, 2);
                }, ct).ConfigureAwait(false);
            return agg;
        }).ConfigureAwait(false);

        // DESCRIBE stock_ledger: qty_in·qty_out decimal(15,3) · supply_amount decimal(15,2) NULL 허용 · source_type.
        var (erp, erpErr) = await GuardAsync("stock_ledger", () =>
            _db.QuerySingleAsync<LedgerErpAgg>(new CommandDefinition(
                """
                SELECT COUNT(*) AS RowCount,
                       COALESCE(SUM(qty_in), 0)        AS QtyIn,
                       COALESCE(SUM(qty_out), 0)       AS QtyOut,
                       COALESCE(SUM(supply_amount), 0) AS Amount
                  FROM stock_ledger
                 WHERE tenant_id = @T AND source_type = 'migration'
                """, new { T = tenantId }, commandTimeout: ErpCommandTimeoutSec, cancellationToken: ct))).ConfigureAwait(false);

        var err = ErrOnly(legacyErr, erpErr);
        var qtyDetail = JoinDetail(
            legacy is null ? null : $"레거시 입고 {legacy.QtyIn:N3} · 출고 {legacy.QtyOut:N3}",
            erp is null ? null : $"히트판 입고 {erp.QtyIn:N3} · 출고 {erp.QtyOut:N3}",
            err);
        items.Add(Make("ledger_rows", "원장", "재고원장 행수", legacy?.RowCount, erp?.RowCount, err));
        items.Add(Make("ledger_net_qty", "원장", "재고원장 순수량(입고−출고)",
            legacy is null ? null : legacy.QtyIn - legacy.QtyOut,
            erp is null ? null : erp.QtyIn - erp.QtyOut, qtyDetail));
        items.Add(Make("ledger_amount", "원장", "재고원장 공급가액", legacy?.Amount, erp?.Amount, err));
    }

    // ────────────────────────────────────────────────────────────────
    // ⑤ 재고 기말수량 + 품목별 차이
    // ────────────────────────────────────────────────────────────────

    private sealed class StockLegacyAgg
    {
        public string Month = string.Empty;
        public decimal Qty;
        public decimal Amount;
        public Dictionary<string, (string Name, decimal Qty)> ByItem = new(StringComparer.Ordinal);
    }

    private sealed class StockErpRow
    {
        public string? ItemName { get; set; }
        public string? Spec { get; set; }
        public decimal Qty { get; set; }
    }

    private async Task AddStockItemsAsync(
        OleDbConnection pandata, string tenantId, List<ReconItem> items, List<ReconDiffRow> itemDiffs, CancellationToken ct)
    {
        var (legacy, legacyErr) = await GuardAsync("DOCFC", async () =>
        {
            var agg = new StockLegacyAgg();
            await ReadMdbAsync(pandata, "SELECT MAX(IM_YM) AS ym FROM DOCFC",
                r => agg.Month = ReadStr(r, 0), ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(agg.Month))
                throw new InvalidOperationException("DOCFC 에 월별 재고 기록이 없습니다.");

            await ReadMdbAsync(pandata,
                "SELECT IM_PUM, IM_KU, SUM(IM_CQTY) AS cq, SUM(IM_CAMT) AS ca FROM DOCFC WHERE IM_YM = ? GROUP BY IM_PUM, IM_KU",
                r =>
                {
                    var pum = ReadStr(r, 0);
                    var ku = ReadStr(r, 1);
                    var qty = ReadDec(r, 2);
                    agg.Qty += qty;
                    agg.Amount += ReadDec(r, 3);
                    var key = ItemKey(pum, ku);
                    var display = string.IsNullOrEmpty(ku) ? pum : $"{pum} {ku}";
                    if (agg.ByItem.TryGetValue(key, out var cur))
                        agg.ByItem[key] = (cur.Name, cur.Qty + qty);
                    else
                        agg.ByItem[key] = (display, qty);
                }, ct, ("ym", agg.Month)).ConfigureAwait(false);
            return agg;
        }).ConfigureAwait(false);

        // DESCRIBE item_stock: current_qty decimal(10,2) · items: item_name varchar(100) · spec varchar(100) · is_deleted.
        var (erpRows, erpErr) = await GuardAsync("item_stock", async () =>
            (await _db.QueryAsync<StockErpRow>(new CommandDefinition(
                """
                SELECT i.item_name AS ItemName, i.spec AS Spec, SUM(s.current_qty) AS Qty
                  FROM item_stock s
                  JOIN items i ON i.item_id = s.item_id AND i.tenant_id = s.tenant_id
                 WHERE s.tenant_id = @T AND i.is_deleted = 0
                 GROUP BY i.item_name, i.spec
                """, new { T = tenantId }, commandTimeout: ErpCommandTimeoutSec, cancellationToken: ct)).ConfigureAwait(false)).ToList()).ConfigureAwait(false);

        decimal? erpTotal = null;
        if (erpRows is not null)
        {
            erpTotal = erpRows.Sum(x => x.Qty);

            if (legacy is not null)
            {
                // 품목별 차이: 키 = 품명|규격 (공백 trim · 대소문자 무시). 양쪽 합집합, 없는 쪽은 0.
                var erpByKey = new Dictionary<string, (string Name, decimal Qty)>(StringComparer.Ordinal);
                foreach (var row in erpRows)
                {
                    var name = row.ItemName ?? string.Empty;
                    var spec = row.Spec ?? string.Empty;
                    var key = ItemKey(name, spec);
                    var display = string.IsNullOrWhiteSpace(spec) ? name.Trim() : $"{name.Trim()} {spec.Trim()}";
                    if (erpByKey.TryGetValue(key, out var cur))
                        erpByKey[key] = (cur.Name, cur.Qty + row.Qty);
                    else
                        erpByKey[key] = (display, row.Qty);
                }

                var diffs = new List<ReconDiffRow>();
                foreach (var key in legacy.ByItem.Keys.Union(erpByKey.Keys, StringComparer.Ordinal))
                {
                    var l = legacy.ByItem.TryGetValue(key, out var lv) ? lv : (Name: string.Empty, Qty: 0m);
                    var e = erpByKey.TryGetValue(key, out var ev) ? ev : (Name: string.Empty, Qty: 0m);
                    var diff = e.Qty - l.Qty;
                    if (diff == 0m) continue;
                    diffs.Add(new ReconDiffRow
                    {
                        Name = string.IsNullOrEmpty(l.Name) ? e.Name : l.Name,
                        Legacy = l.Qty,
                        Erp = e.Qty,
                        Diff = diff
                    });
                }
                itemDiffs.AddRange(diffs.OrderByDescending(d => Math.Abs(d.Diff)).ThenBy(d => d.Name, StringComparer.Ordinal).Take(TopDiffRows));
            }
        }

        var detail = JoinDetail(
            legacy is null ? null : $"레거시 기준월 {legacy.Month} · 기말금액 {legacy.Amount:N0} · 품목 {legacy.ByItem.Count:N0}",
            erpRows is null ? null : $"히트판 품목 {erpRows.Count:N0}",
            ErrOnly(legacyErr, erpErr));
        items.Add(Make("stock_qty", "재고", "재고(기말수량)", legacy?.Qty, erpTotal, detail));
    }

    // ────────────────────────────────────────────────────────────────
    // ⑥ 회계 분개
    // ────────────────────────────────────────────────────────────────

    private sealed class JournalAgg
    {
        public long RowCount { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }

    private async Task AddJournalItemsAsync(
        OleDbConnection pandata, string tenantId, List<ReconItem> items, CancellationToken ct)
    {
        var (legacy, legacyErr) = await GuardAsync("DOCF7", async () =>
        {
            var agg = new JournalAgg();
            // 전결1 Q4: SC_CR=차변 · SC_DR=대변 · 음수는 반대편 양수. 양쪽 0 은 제외.
            await ReadMdbAsync(pandata,
                "SELECT COUNT(*) AS n, " +
                "SUM(IIF(SC_CR>0, SC_CR, 0)) + SUM(IIF(SC_DR<0, -SC_DR, 0)) AS debit, " +
                "SUM(IIF(SC_DR>0, SC_DR, 0)) + SUM(IIF(SC_CR<0, -SC_CR, 0)) AS credit " +
                "FROM DOCF7 WHERE SC_CR<>0 OR SC_DR<>0",
                r => { agg.RowCount = ReadLong(r, 0); agg.Debit = ReadDec(r, 1); agg.Credit = ReadDec(r, 2); }, ct).ConfigureAwait(false);
            return agg;
        }).ConfigureAwait(false);

        // DESCRIBE journal_lines: source_type 없음 → journal_entries.source_type 으로 격리 (entry_id + tenant_id JOIN).
        var (erp, erpErr) = await GuardAsync("journal_lines", () =>
            _db.QuerySingleAsync<JournalAgg>(new CommandDefinition(
                """
                SELECT COUNT(*) AS RowCount,
                       COALESCE(SUM(l.debit_amount), 0)  AS Debit,
                       COALESCE(SUM(l.credit_amount), 0) AS Credit
                  FROM journal_lines l
                  JOIN journal_entries e ON e.entry_id = l.entry_id AND e.tenant_id = l.tenant_id
                 WHERE l.tenant_id = @T AND e.source_type = 'migration'
                """, new { T = tenantId }, commandTimeout: ErpCommandTimeoutSec, cancellationToken: ct))).ConfigureAwait(false);

        var err = ErrOnly(legacyErr, erpErr);
        items.Add(Make("journal_rows", "회계", "회계(분개) 행수", legacy?.RowCount, erp?.RowCount, err));
        items.Add(Make("journal_debit", "회계", "회계(분개) 차변 합계", legacy?.Debit, erp?.Debit, err));
        items.Add(Make("journal_credit", "회계", "회계(분개) 대변 합계", legacy?.Credit, erp?.Credit, err));
    }

    // ────────────────────────────────────────────────────────────────
    // ⑦ ⑧ ⑨ 수금 · 미수금 · 미지급금 + 거래처별 차이
    // ────────────────────────────────────────────────────────────────

    private sealed class PartnerLedgerLegacyAgg
    {
        public decimal CollectionSum;   // S_GU 1~5 ΣS_SUK
        public long CollectionRows;
        public decimal PaymentSum;      // S_GU B~F ΣS_BAL
        public long PaymentRows;
        public decimal Receivable;      // 거래처별 (ΣS_BAL−ΣS_SUK) 양수 합
        public decimal Payable;         // 음수 합의 절대값
        public Dictionary<int, decimal> NetByPartner = new();
    }

    private sealed class PartnerErpRow
    {
        public string? PartnerId { get; set; }
        public string? PartnerName { get; set; }
        public string? PartnerCode { get; set; }
        public string? MigratedSourceHash { get; set; }
        public decimal Receivable { get; set; }
        public decimal Payable { get; set; }
    }

    private sealed class CollectionErpAgg
    {
        public decimal AmountSum { get; set; }
        public long Cnt { get; set; }
    }

    /// <summary>스칼라 한 값을 Dapper 로 받는 그릇 (GuardAsync 가 참조형만 받는다).</summary>
    private sealed class ScalarBox
    {
        public decimal Value { get; set; }
    }

    private static readonly string[] CollectionCodes = { "1", "2", "3", "4", "5" };
    private static readonly string[] PaymentCodes = { "B", "C", "D", "E", "F" };

    private async Task AddPartnerLedgerItemsAsync(
        OleDbConnection pandata, OleDbConnection pyojun, string tenantId,
        List<ReconItem> items, List<ReconDiffRow> partnerDiffs, CancellationToken ct)
    {
        var (legacy, legacyErr) = await GuardAsync("DOCF5", async () =>
        {
            var agg = new PartnerLedgerLegacyAgg();
            // 계열별 합 — 61만행을 올리지 않고 MDB 가 GROUP BY 한 12행만 받는다.
            await ReadMdbAsync(pandata,
                "SELECT S_GU, COUNT(*) AS n, SUM(S_SUK) AS suk, SUM(S_BAL) AS bal FROM DOCF5 GROUP BY S_GU",
                r =>
                {
                    var gu = ReadStr(r, 0).ToUpperInvariant();
                    if (CollectionCodes.Contains(gu)) { agg.CollectionRows += ReadLong(r, 1); agg.CollectionSum += ReadDec(r, 2); }
                    else if (PaymentCodes.Contains(gu)) { agg.PaymentRows += ReadLong(r, 1); agg.PaymentSum += ReadDec(r, 3); }
                }, ct).ConfigureAwait(false);

            // 거래처별 잔액 = ΣS_BAL − ΣS_SUK (전 코드) — 11,813 거래처.
            await ReadMdbAsync(pandata,
                "SELECT S_BUY, SUM(S_BAL) AS bal, SUM(S_SUK) AS suk FROM DOCF5 GROUP BY S_BUY",
                r =>
                {
                    var buy = ReadInt(r, 0);
                    var net = ReadDec(r, 1) - ReadDec(r, 2);
                    agg.NetByPartner[buy] = agg.NetByPartner.TryGetValue(buy, out var cur) ? cur + net : net;
                }, ct).ConfigureAwait(false);

            foreach (var net in agg.NetByPartner.Values)
            {
                if (net > 0) agg.Receivable += net;
                else if (net < 0) agg.Payable += -net;
            }
            return agg;
        }).ConfigureAwait(false);

        // 레거시 거래처명 (DOCF8.buy_name) — PII 컬럼(buy_topjumin 등)은 읽지 않는다.
        var (legacyNames, namesErr) = await GuardAsync("DOCF8", async () =>
        {
            var names = new Dictionary<int, string>();
            await ReadMdbAsync(pyojun, "SELECT buy_code, buy_name FROM DOCF8",
                r => names[ReadInt(r, 0)] = ReadStr(r, 1), ct).ConfigureAwait(false);
            return names;
        }).ConfigureAwait(false);
        if (namesErr is not null)
            _logger.LogWarning("[MDB대사] DOCF8 거래처명 읽기 실패 — 거래처 차이 목록은 코드로 표시: {Err}", namesErr);

        // ERP 합계 — 항목별로 따로 묻는다 (한 문장에 묶으면 61만행 수금 합계가 타임아웃을 끌고 가 셋이 같이 NA 가 됐다).
        // DESCRIBE 확인: sales_deliveries(status,is_deleted,total_amount,vat_amount) · collections(amount,ref_doc_type,source_type,is_active)
        //   · purchase_receipts(status,total_amount,vat_amount) · purchase_returns(is_deleted,status) · purchase_return_items(supply_amount,vat_amount,return_id,tenant_id)
        //   · payments(amount,is_active,payment_type).
        var (erpColl, collErr) = await GuardAsync("수금(ERP)", () =>
            _db.QuerySingleAsync<CollectionErpAgg>(new CommandDefinition(
                """
                SELECT COALESCE(SUM(amount), 0) AS AmountSum, COUNT(*) AS Cnt
                  FROM collections
                 WHERE tenant_id=@T AND source_type='migration' AND is_active=1
                """, new { T = tenantId }, commandTimeout: ErpCommandTimeoutSec, cancellationToken: ct))).ConfigureAwait(false);

        var (erpPaySum, paySumErr) = await GuardAsync("지급(ERP)", () =>
            _db.QuerySingleAsync<ScalarBox>(new CommandDefinition(
                """
                SELECT COALESCE(SUM(amount), 0) AS Value
                  FROM payments
                 WHERE tenant_id=@T AND is_active=1 AND payment_type='purchase'
                """, new { T = tenantId }, commandTimeout: ErpCommandTimeoutSec, cancellationToken: ct))).ConfigureAwait(false);

        // 미수 식 = FinanceService.GetDashboardAsync kpiSql 'receivable' 그대로.
        var (erpRecv, recvErr) = await GuardAsync("미수금(ERP)", () =>
            _db.QuerySingleAsync<ScalarBox>(new CommandDefinition(
                """
                SELECT
                  COALESCE((SELECT SUM(total_amount + vat_amount) FROM sales_deliveries WHERE tenant_id=@T AND status IN ('confirmed','invoiced') AND is_deleted=0), 0)
                  - COALESCE((SELECT SUM(amount) FROM collections WHERE tenant_id=@T AND ref_doc_type = 'sales_delivery'), 0) AS Value
                """, new { T = tenantId }, commandTimeout: ErpCommandTimeoutSec, cancellationToken: ct))).ConfigureAwait(false);

        // 미지급 식 = 같은 kpiSql 'payable' 그대로 (매입 − 매입반품 − 지급).
        var (erpPay, payErr) = await GuardAsync("미지급금(ERP)", () =>
            _db.QuerySingleAsync<ScalarBox>(new CommandDefinition(
                """
                SELECT
                  COALESCE((SELECT SUM(total_amount + vat_amount) FROM purchase_receipts WHERE tenant_id=@T AND status='confirmed'), 0)
                  - COALESCE((SELECT COALESCE(SUM(rti.supply_amount + rti.vat_amount),0) FROM purchase_returns rt LEFT JOIN purchase_return_items rti ON rti.return_id=rt.return_id AND rti.tenant_id=rt.tenant_id WHERE rt.tenant_id=@T AND rt.is_deleted=0 AND rt.status='confirmed'), 0)
                  - COALESCE((SELECT SUM(amount) FROM payments WHERE tenant_id=@T AND is_active=1 AND payment_type='purchase'), 0) AS Value
                """, new { T = tenantId }, commandTimeout: ErpCommandTimeoutSec, cancellationToken: ct))).ConfigureAwait(false);

        // 거래처별 (같은 식을 partner_id 로 나눈 것). DESCRIBE partners: 레거시 코드 컬럼 없음 → migrated_source_hash 로 짝짓는다.
        var (erpRows, rowsErr) = await GuardAsync("업체별원장(거래처별)", async () =>
            (await _db.QueryAsync<PartnerErpRow>(new CommandDefinition(
                """
                SELECT p.partner_id AS PartnerId, p.partner_name AS PartnerName, p.partner_code AS PartnerCode,
                       p.migrated_source_hash AS MigratedSourceHash,
                       COALESCE(sd.amt, 0) - COALESCE(c.amt, 0) AS Receivable,
                       COALESCE(pr.amt, 0) - COALESCE(rt.amt, 0) - COALESCE(pay.amt, 0) AS Payable
                  FROM partners p
                  LEFT JOIN (SELECT partner_id, SUM(total_amount + vat_amount) AS amt FROM sales_deliveries
                              WHERE tenant_id=@T AND status IN ('confirmed','invoiced') AND is_deleted=0 GROUP BY partner_id) sd ON sd.partner_id = p.partner_id
                  LEFT JOIN (SELECT partner_id, SUM(amount) AS amt FROM collections
                              WHERE tenant_id=@T AND ref_doc_type = 'sales_delivery' GROUP BY partner_id) c ON c.partner_id = p.partner_id
                  LEFT JOIN (SELECT partner_id, SUM(total_amount + vat_amount) AS amt FROM purchase_receipts
                              WHERE tenant_id=@T AND status='confirmed' GROUP BY partner_id) pr ON pr.partner_id = p.partner_id
                  LEFT JOIN (SELECT rt.partner_id, COALESCE(SUM(rti.supply_amount + rti.vat_amount),0) AS amt FROM purchase_returns rt
                              LEFT JOIN purchase_return_items rti ON rti.return_id=rt.return_id AND rti.tenant_id=rt.tenant_id
                              WHERE rt.tenant_id=@T AND rt.is_deleted=0 AND rt.status='confirmed' GROUP BY rt.partner_id) rt ON rt.partner_id = p.partner_id
                  LEFT JOIN (SELECT partner_id, SUM(amount) AS amt FROM payments
                              WHERE tenant_id=@T AND is_active=1 AND payment_type='purchase' GROUP BY partner_id) pay ON pay.partner_id = p.partner_id
                 WHERE p.tenant_id = @T
                """, new { T = tenantId }, commandTimeout: ErpCommandTimeoutSec, cancellationToken: ct)).ConfigureAwait(false)).ToList()).ConfigureAwait(false);
        if (rowsErr is not null)
            _logger.LogWarning("[MDB대사] 거래처별 ERP 잔액 읽기 실패 — 거래처 차이 목록 생략: {Err}", rowsErr);

        if (legacy is not null && erpRows is not null)
            BuildPartnerDiffs(legacy, legacyNames, erpRows, partnerDiffs);

        items.Add(Make("collections", "수금", "수금", legacy?.CollectionSum, erpColl?.AmountSum,
            JoinDetail(
                legacy is null ? null : $"레거시 수금 {legacy.CollectionRows:N0}건",
                erpColl is null ? null : $"히트판 수금 {erpColl.Cnt:N0}건",
                ErrOnly(legacyErr, collErr))));
        items.Add(Make("receivable", "미수", "미수금", legacy?.Receivable, erpRecv?.Value,
            JoinDetail(
                legacy is null ? null : $"레거시 거래처 {legacy.NetByPartner.Count:N0}곳 중 미수 {legacy.NetByPartner.Values.Count(v => v > 0):N0}곳",
                ErrOnly(legacyErr, recvErr))));
        items.Add(Make("payable", "미지급", "미지급금", legacy?.Payable, erpPay?.Value,
            JoinDetail(
                legacy is null ? null : $"레거시 지급 {legacy.PaymentRows:N0}건 {legacy.PaymentSum:N0} · 미지급 {legacy.NetByPartner.Values.Count(v => v < 0):N0}곳",
                erpPaySum is null ? null : $"히트판 지급 {erpPaySum.Value:N0}",
                ErrOnly(legacyErr, payErr, paySumErr))));
    }

    private void BuildPartnerDiffs(
        PartnerLedgerLegacyAgg legacy, Dictionary<int, string>? legacyNames,
        List<PartnerErpRow> erpRows, List<ReconDiffRow> partnerDiffs)
    {
        // ERP 거래처를 해시(1순위)·partner_code "MIG-{code:D5}"(2순위) 로 찾을 수 있게 색인.
        var byHash = new Dictionary<string, PartnerErpRow>(StringComparer.OrdinalIgnoreCase);
        var byCode = new Dictionary<string, PartnerErpRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in erpRows)
        {
            if (!string.IsNullOrEmpty(row.MigratedSourceHash)) byHash.TryAdd(row.MigratedSourceHash, row);
            if (!string.IsNullOrEmpty(row.PartnerCode)) byCode.TryAdd(row.PartnerCode, row);
        }

        var matched = new HashSet<string>(StringComparer.Ordinal);
        var diffs = new List<ReconDiffRow>();
        foreach (var (buyCode, legacyNet) in legacy.NetByPartner)
        {
            var hash = ComputeSourceHash($"partners:buy_code:{buyCode}");
            if (!byHash.TryGetValue(hash, out var erp))
                byCode.TryGetValue($"MIG-{buyCode:D5}", out erp);

            var erpNet = erp is null ? 0m : erp.Receivable - erp.Payable;
            if (erp?.PartnerId is not null) matched.Add(erp.PartnerId);

            var diff = erpNet - legacyNet;
            if (diff == 0m) continue;

            string? name = erp?.PartnerName;
            if (string.IsNullOrWhiteSpace(name) && legacyNames is not null && legacyNames.TryGetValue(buyCode, out var ln))
                name = ln;
            if (string.IsNullOrWhiteSpace(name)) name = $"거래처코드 {buyCode}";
            diffs.Add(new ReconDiffRow { Name = name.Trim(), Legacy = legacyNet, Erp = erpNet, Diff = diff });
        }

        // 레거시에 짝이 없는 ERP 거래처 — 잔액이 있으면 그것도 차이다.
        foreach (var row in erpRows)
        {
            if (row.PartnerId is null || matched.Contains(row.PartnerId)) continue;
            var erpNet = row.Receivable - row.Payable;
            if (erpNet == 0m) continue;
            diffs.Add(new ReconDiffRow
            {
                Name = string.IsNullOrWhiteSpace(row.PartnerName) ? (row.PartnerCode ?? row.PartnerId) : row.PartnerName.Trim(),
                Legacy = 0m,
                Erp = erpNet,
                Diff = erpNet
            });
        }

        partnerDiffs.AddRange(diffs.OrderByDescending(d => Math.Abs(d.Diff)).ThenBy(d => d.Name, StringComparer.Ordinal).Take(TopDiffRows));
        _logger.LogDebug("[MDB대사] 거래처 차이 {Count}건 중 상위 {Top} 보고", diffs.Count, TopDiffRows);
    }

    // ────────────────────────────────────────────────────────────────
    // ⑩ 마스터 4종
    // ────────────────────────────────────────────────────────────────

    private sealed class MasterCounts
    {
        public long Partners { get; set; }
        public long Items { get; set; }
        public long AutoItems { get; set; }
        public long Employees { get; set; }
        public long BomHeaders { get; set; }
        public long BomLines { get; set; }
    }

    private async Task AddMasterItemsAsync(
        OleDbConnection pyojun, string tenantId, List<ReconItem> items, CancellationToken ct)
    {
        var (legacy, legacyErr) = await GuardAsync("PYOJUN 마스터", async () =>
        {
            var c = new MasterCounts();
            await ReadMdbAsync(pyojun, "SELECT COUNT(*) FROM DOCF8", r => c.Partners = ReadLong(r, 0), ct).ConfigureAwait(false);
            await ReadMdbAsync(pyojun, "SELECT COUNT(*) FROM DOCFS", r => c.Items = ReadLong(r, 0), ct).ConfigureAwait(false);
            await ReadMdbAsync(pyojun, "SELECT COUNT(*) FROM DOCSW", r => c.Employees = ReadLong(r, 0), ct).ConfigureAwait(false);
            // BOM: 완제품(RT_PUM, RT_KU) 단위 = bom_headers · 자재 라인 = bom_items.
            await ReadMdbAsync(pyojun, "SELECT COUNT(*) FROM (SELECT DISTINCT RT_PUM, RT_KU FROM DOCRT) AS h", r => c.BomHeaders = ReadLong(r, 0), ct).ConfigureAwait(false);
            await ReadMdbAsync(pyojun, "SELECT COUNT(*) FROM DOCRT", r => c.BomLines = ReadLong(r, 0), ct).ConfigureAwait(false);
            return c;
        }).ConfigureAwait(false);

        // DESCRIBE 확인: partners.is_deleted · items.is_deleted/item_code · employees(tenant_id) · bom_headers/bom_items(tenant_id).
        var (erp, erpErr) = await GuardAsync("마스터(ERP)", () =>
            _db.QuerySingleAsync<MasterCounts>(new CommandDefinition(
                """
                SELECT
                  (SELECT COUNT(*) FROM partners  WHERE tenant_id=@T AND is_deleted=0) AS Partners,
                  (SELECT COUNT(*) FROM items     WHERE tenant_id=@T AND is_deleted=0) AS Items,
                  (SELECT COUNT(*) FROM items     WHERE tenant_id=@T AND is_deleted=0 AND item_code LIKE 'MIG-AUTO-%') AS AutoItems,
                  (SELECT COUNT(*) FROM employees WHERE tenant_id=@T) AS Employees,
                  (SELECT COUNT(*) FROM bom_headers WHERE tenant_id=@T) AS BomHeaders,
                  (SELECT COUNT(*) FROM bom_items   WHERE tenant_id=@T) AS BomLines
                """, new { T = tenantId }, commandTimeout: ErpCommandTimeoutSec, cancellationToken: ct))).ConfigureAwait(false);

        var err = ErrOnly(legacyErr, erpErr);
        items.Add(Make("master_partners", "마스터", "업체", legacy?.Partners, erp?.Partners, err));
        items.Add(Make("master_items", "마스터", "상품", legacy?.Items, erp?.Items,
            JoinDetail(erp is null ? null : $"히트판 자동등록 품목 {erp.AutoItems:N0}건 포함", err)));
        items.Add(Make("master_employees", "마스터", "사원", legacy?.Employees, erp?.Employees, err));
        items.Add(Make("master_bom", "마스터", "BOM", legacy?.BomHeaders, erp?.BomHeaders,
            JoinDetail(
                legacy is null ? null : $"레거시 자재 라인 {legacy.BomLines:N0}",
                erp is null ? null : $"히트판 자재 라인 {erp.BomLines:N0}",
                err)));
    }

    // ────────────────────────────────────────────────────────────────
    // 판정·조립 헬퍼
    // ────────────────────────────────────────────────────────────────

    /// <summary>항목 하나를 만든다. 한쪽이라도 null 이면 NA, 차이 0 이면 OK, 그 외 DIFF. 차이 = 히트판 − 레거시.</summary>
    private static ReconItem Make(string key, string category, string label, decimal? legacy, decimal? erp, string? detail)
    {
        var item = new ReconItem { Key = key, Category = category, Label = label, Legacy = legacy, Erp = erp, Detail = detail };
        if (legacy is null || erp is null)
        {
            item.Status = StatusNa;
            return item;
        }
        item.Diff = erp.Value - legacy.Value;
        item.Status = item.Diff == 0m ? StatusOk : StatusDiff;
        return item;
    }

    /// <summary>
    /// 항목 하나를 감싼다 — 실패해도 던지지 않고 (null, 사유) 를 돌려준다.
    /// 취소는 그대로 올린다 (사용자가 끊은 것을 NA 로 위장하지 않는다).
    /// </summary>
    private async Task<(T? Value, string? Error)> GuardAsync<T>(string what, Func<Task<T>> work) where T : class
    {
        try
        {
            return (await work().ConfigureAwait(false), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MDB대사] 항목 계산 실패 — NA 처리 what={What}", what);
            return (null, $"{what} 읽기 실패: {ex.Message}");
        }
    }

    private static string? ErrOnly(params string?[] parts) => JoinDetail(parts);

    private static string? JoinDetail(params string?[] parts)
    {
        var list = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return list.Count == 0 ? null : string.Join(" · ", list);
    }

    /// <summary>품명|규격 키 — 공백 trim · 대소문자 무시.</summary>
    private static string ItemKey(string? name, string? spec)
        => $"{(name ?? string.Empty).Trim()}|{(spec ?? string.Empty).Trim()}".ToUpperInvariant();

    /// <summary>
    /// <see cref="MdbMigrationService"/>.ComputeSourceHash 와 같은 방식 — SHA256 · UTF8 · 대문자 hex 64자.
    /// partners.migrated_source_hash 는 <c>partners:buy_code:{BUY_CODE}</c> 로 만들어져 있다.
    /// </summary>
    private static string ComputeSourceHash(string naturalKey)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(naturalKey ?? string.Empty);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    // ────────────────────────────────────────────────────────────────
    // MDB 접근 (읽기 전용)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 폴더에서 PYOJUN.MDB / PANDATA.mdb / POTHER.mdb 를 대소문자 무시로 찾는다.
    /// <see cref="MdbMigrationService"/>.ResolveMdbPaths 와 같은 규칙(절대경로 · ".." 차단)을 자체 구현 (서비스 파일 무접촉).
    /// 대사에 필요한 PYOJUN·PANDATA 가 없으면 FileNotFoundException. POTHER 는 경로만 돌려주고 열지 않는다.
    /// </summary>
    private static (string Pyojun, string Pandata, string Pother) ResolveMdbPaths(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new InvalidOperationException("MDB 폴더 경로를 입력해주세요.");
        if (folderPath.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("경로에 '..'을 포함할 수 없습니다.");
        if (!Path.IsPathRooted(folderPath))
            throw new InvalidOperationException("절대 경로만 입력 가능합니다.");
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"폴더를 찾을 수 없습니다: {folderPath}");

        var files = Directory.GetFiles(folderPath, "*.mdb", SearchOption.TopDirectoryOnly);
        string? Find(string name) =>
            files.FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));

        var pyojun = Find("PYOJUN.MDB") ?? throw new FileNotFoundException("PYOJUN.MDB 가 없습니다.", Path.Combine(folderPath, "PYOJUN.MDB"));
        var pandata = Find("PANDATA.mdb") ?? throw new FileNotFoundException("PANDATA.mdb 가 없습니다.", Path.Combine(folderPath, "PANDATA.mdb"));
        var pother = Find("POTHER.mdb") ?? Path.Combine(folderPath, "POTHER.mdb");
        return (pyojun, pandata, pother);
    }

    /// <summary>MDB 를 읽기 전용으로 연다. ACE 12.0 이 없으면 16.0 을 시도한다. 비번은 로그에 남기지 않는다.</summary>
    private OleDbConnection OpenMdb(string mdbPath, string? password)
    {
        var pwd = password ?? string.Empty;
        try
        {
            return OpenWith(BuildConnectionString(OleDbProvider12, mdbPath, pwd));
        }
        catch (InvalidOperationException ex12) when (IsProviderMissing(ex12))
        {
            _logger.LogInformation("[MDB대사] ACE OLEDB 12.0 없음 — 16.0 으로 재시도 file={File}", Path.GetFileName(mdbPath));
            try
            {
                return OpenWith(BuildConnectionString(OleDbProvider16, mdbPath, pwd));
            }
            catch (InvalidOperationException ex16) when (IsProviderMissing(ex16))
            {
                throw new InvalidOperationException(
                    "MDB 처리 엔진(Microsoft ACE OLEDB 12.0/16.0)이 설치되어 있지 않습니다. 서버 설정을 확인해주세요.", ex16);
            }
        }

        static OleDbConnection OpenWith(string connStr)
        {
            var conn = new OleDbConnection(connStr);
            try
            {
                conn.Open();
                return conn;
            }
            catch
            {
                conn.Dispose();
                throw;
            }
        }
    }

    private static bool IsProviderMissing(InvalidOperationException ex)
        => ex.Message.Contains("provider", StringComparison.OrdinalIgnoreCase)
           && (ex.Message.Contains("not registered", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("등록", StringComparison.Ordinal));

    /// <summary>MDB 에 SQL 을 보내고 행마다 <paramref name="onRow"/> 를 부른다. 파라미터는 Access 의 <c>?</c> 순서대로.</summary>
    private static async Task ReadMdbAsync(
        OleDbConnection conn, string sql, Action<DbDataReader> onRow, CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        using var cmd = new OleDbCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            onRow(reader);
    }

    private static string ReadStr(DbDataReader r, int i)
        => r.IsDBNull(i) ? string.Empty : (Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty).Trim();

    private static decimal ReadDec(DbDataReader r, int i)
        => r.IsDBNull(i) ? 0m : Convert.ToDecimal(r.GetValue(i), CultureInfo.InvariantCulture);

    private static long ReadLong(DbDataReader r, int i)
        => r.IsDBNull(i) ? 0L : Convert.ToInt64(r.GetValue(i), CultureInfo.InvariantCulture);

    private static int ReadInt(DbDataReader r, int i)
        => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);

    /// <summary>MariaDB 커넥션이 닫혀있으면 비동기로 연다.</summary>
    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection dbConnection)
        {
            await dbConnection.OpenAsync(ct).ConfigureAwait(false);
            return;
        }
        _db.Open();
    }
}

// ════════════════════════════════════════════════════════════════
// 결과 DTO (20260904작21 B1 — 필드명 고정)
// ════════════════════════════════════════════════════════════════

/// <summary>레거시 ↔ 히트판 대사표.</summary>
public sealed class MdbReconciliationReport
{
    public DateTime GeneratedAt { get; set; }
    public List<ReconItem> Items { get; set; } = new();
    /// <summary>거래처별 잔액(미수−미지급) 차이 상위 20.</summary>
    public List<ReconDiffRow> PartnerDiffs { get; set; } = new();
    /// <summary>품목별 기말수량 차이 상위 20.</summary>
    public List<ReconDiffRow> ItemDiffs { get; set; } = new();
    public int OkCount { get; set; }
    public int DiffCount { get; set; }
}

/// <summary>대사 항목 하나. Status ∈ OK | DIFF | NA · Diff = 히트판 − 레거시.</summary>
public sealed class ReconItem
{
    public string Key { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal? Legacy { get; set; }
    public decimal? Erp { get; set; }
    public decimal? Diff { get; set; }
    public string Status { get; set; } = "NA";
    public string? Detail { get; set; }
}

/// <summary>거래처·품목별 차이 한 줄. Diff = 히트판 − 레거시.</summary>
public sealed class ReconDiffRow
{
    public string Name { get; set; } = string.Empty;
    public decimal Legacy { get; set; }
    public decimal Erp { get; set; }
    public decimal Diff { get; set; }
}
