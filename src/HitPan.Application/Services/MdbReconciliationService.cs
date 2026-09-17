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
///    거래처별 잔액 = ΣS_BAL − ΣS_SUK(전 코드) → 양수 합 = 미수 · 음수 합의 절대값 = 미지급 — 작21 의 종전 식. 작22 부터 Detail 참고값.
///  · 작22 (2026-09-09) C2 — 정답 칸 = <b>히트판 ERP 화면 식을 레거시 데이터에 적용</b>(사장님 9/8 ② · 9/9 확인 "맞음"):
///    ⑤ DOCFB 품목별 Σ(IO=1 IJ_QTY) − Σ(IO=2 IJ_QTY) · ⑧ DOCFE IO=2 Σ(AMT1+AMT2) − DOCF5 S_GU 1~5 ΣS_SUK ·
///    ⑨ DOCFE IO=1 Σ(AMT1+AMT2) − DOCF5 S_GU B~F ΣS_BAL (거래처별 IJA_BUY ↔ S_BUY 도 같은 식). 종전 값(DOCFC 최신월 · 순잔액 양/음 합)은 Detail 에 남긴다.
///  · 작22 C3 — 이관 건수 4항목(현금출납·경비·은행거래·발주/수주) + ⑪ 일일보고서(POTHER.DOCME ↔ hr_reports). POTHER 는 있으면 열고 없으면 ⑪ 만 NA.
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

    /// <summary>
    /// 로그에 넣는 사용자 입력에서 줄바꿈·탭을 제거한다 ([3-V] 2026-09-10 · CodeQL cs/log-forging).
    /// MDB 경로는 사장님이 화면에서 치는 값이라 개행을 섞으면 가짜 로그 줄을 만들 수 있다.
    /// </summary>
    private static string ForLog(string? value)
        => System.Text.RegularExpressions.Regex.Replace(value ?? string.Empty, @"[\r\n\t]+", " ").Trim();

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
    /// <param name="folderPath">PYOJUN.MDB / PANDATA.mdb 가 있는 폴더 (절대경로). POTHER.mdb 는 있으면 ⑪ 일일보고서 대사에 쓴다.</param>
    /// <param name="mdbPassword">MDB 비밀번호 (없으면 null).</param>
    /// <param name="tenantId">JWT 클레임에서 온 tenant_id (헌법 #2).</param>
    /// <param name="ct">취소 토큰.</param>
    public async Task<MdbReconciliationReport> BuildAsync(
        string folderPath, string? mdbPassword, string tenantId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new InvalidOperationException("tenant 정보가 없습니다.");

        var (pyojunPath, pandataPath, potherPath) = ResolveMdbPaths(folderPath);

        // MDB 를 못 열면(비번 틀림·엔진 없음) 대사 자체가 불가 — 항목별 NA 가 아니라 호출자에게 그대로 올린다.
        using var pandata = OpenMdb(pandataPath, mdbPassword);
        using var pyojun = OpenMdb(pyojunPath, mdbPassword);
        // 작22 (2026-09-09) C3 ⑪: POTHER 는 선택 — 파일이 없거나 못 열면 일일보고서 항목만 NA 로 남기고 나머지 대사는 계속한다.
        using var pother = TryOpenPother(potherPath, mdbPassword, out var potherErr);

        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var report = new MdbReconciliationReport { GeneratedAt = DateTime.Now };
        var items = report.Items;

        // ① 판매 · ② 매입 (DOCFE 헤더 ↔ sales_deliveries / purchase_receipts)
        await AddDeliveryItemsAsync(pandata, tenantId, items, ct).ConfigureAwait(false);

        // ③ 세금계산서 (DOCF4 ↔ tax_invoices)
        await AddTaxInvoiceItemsAsync(pandata, tenantId, items, ct).ConfigureAwait(false);

        // 🔴 20260915작1 갈래 C — 분류·재고·잔액에 쓰는 레거시 표를 한 번만 읽는다(이관 LoadLegacyPostingContext 와 같은 칼럼).
        var (tables, tablesErr) = await GuardAsync("DOCFB·DOCFE·DOCF5·DOCFC", () => LoadPostingTablesAsync(pandata, ct)).ConfigureAwait(false);

        // ②③⑧ 장부 미반영 보관 · 명세서 줄 = 반영 줄 + 보관 줄 · 봉합 전 옛 명세서 (갈래 C)
        // ④⑤ 재고 기말 = DOCFC 최종 · 원장 이력 입·출(LedgerMove) · 맞춤 줄 (갈래 C — 종전 ④ DOCFB 순수량 · ⑤ DOCFB 품목별 = 자기 자신과 비교 → 제거 ⑨)
        await AddPostingAndStockItemsAsync(pandata, pyojun, tables, tablesErr, tenantId, report, ct).ConfigureAwait(false);

        // ⑥ 회계 분개 (DOCF7 ↔ journal_lines)
        await AddJournalItemsAsync(pandata, tenantId, items, ct).ConfigureAwait(false);

        // ⑦ 수금 · ⑧ 미수금 · ⑨ 미지급금 (DOCF5·DOCFE ↔ collections / 화면 식 — 작22 새 식) + 거래처별 차이 상위 20
        await AddPartnerLedgerItemsAsync(pandata, tenantId, items, ct).ConfigureAwait(false);

        // ⑥ 미수·미지급 = F3(거래처별 마지막 이월 + 그 뒤) ↔ 히트판 KPI 식(갈래 E) + 거래처별 차이 (갈래 C — 종전 DOCFE−DOCF5 새 식 대체)
        await AddBalanceItemsAsync(pyojun, tables, tablesErr, tenantId, report, ct).ConfigureAwait(false);

        // ⑩ 마스터 4종 건수
        await AddMasterItemsAsync(pyojun, tenantId, items, ct).ConfigureAwait(false);

        // ⑪ 일일보고서 (POTHER.DOCME (사원,날짜) 묶음 ↔ hr_reports source_type='migration') — 작22 C3
        await AddDailyReportItemAsync(pother, potherErr, tenantId, items, ct).ConfigureAwait(false);

        // ⑫ 이관 건수 4항목 (DOCF6·DOCF7·BANKF·DOCFA/DOCFO ↔ cashbook·expenses·bank_transactions·purchase_orders+sales_orders) — 작22 C3
        await AddRowCountItemsAsync(pandata, pyojun, tenantId, items, ct).ConfigureAwait(false);

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
    // ②③④⑤⑧ 장부 반영 분류 · 재고 · 원장 — 20260915작1 갈래 C (계산은 MdbReconPosting)
    // ────────────────────────────────────────────────────────────────

    /// <summary>레거시 표 묶음 — 없는 표는 null + Has* false.</summary>
    private sealed class PostingTables
    {
        public DataTable? Docfb;
        public DataTable? Docfe;
        public DataTable? Docf5;
        public DataTable? Docfc;
    }

    /// <summary>
    /// 표 목록을 직접 읽고(목록 예외를 삼키지 않는다) 있는 표만 읽는다. 칼럼은 이관 <c>MdbMigrationService.LoadLegacyPostingContext</c> 와 같다
    /// (DOCFE 머리 키 · DOCF5 6칸 · DOCFC 전체) + DOCFB 분류·원장 7칸. 61만행 DOCF5 를 올리는 것도 이관과 같다(⚠️ 부하 [4]).
    /// </summary>
    private static Task<PostingTables> LoadPostingTablesAsync(OleDbConnection pandata, CancellationToken ct)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow t in pandata.GetSchema("Tables").Rows)
        {
            var name = t["TABLE_NAME"]?.ToString();
            if (!string.IsNullOrEmpty(name)) names.Add(name);
        }
        ct.ThrowIfCancellationRequested();
        var tables = new PostingTables
        {
            Docfb = names.Contains("DOCFB") ? ReadMdbTable(pandata, "SELECT IJ_DT, IJ_IO, IJ_SEQ, IJ_BUY, IJ_QTY, IJ_AMT, IJ_VAT FROM DOCFB") : null,
            Docfe = names.Contains("DOCFE") ? ReadMdbTable(pandata, "SELECT IJA_DT, IJA_IO, IJA_SEQ, IJA_BUY FROM DOCFE") : null,
            Docf5 = names.Contains("DOCF5") ? ReadMdbTable(pandata, "SELECT S_BUY, S_YMD, S_GU, S_BAL, S_SUK, S_SSUN FROM DOCF5") : null,
            Docfc = names.Contains("DOCFC") ? ReadMdbTable(pandata, "SELECT * FROM DOCFC") : null,
        };
        return Task.FromResult(tables);
    }

    private static DataTable ReadMdbTable(OleDbConnection conn, string sql)
    {
        using var cmd = new OleDbCommand(sql, conn);
        using var reader = cmd.ExecuteReader();
        var dt = new DataTable();
        dt.Load(reader);
        return dt;
    }

    /// <summary>
    /// 🆕 3판 R2 — 대사표용 재고 이어 계산. 이관 <c>MdbMigrationService.ReadLegacyPostingContext</c> 와 같은 입력:
    /// DOCFB 중 DOCFC 마지막 달 뒤 줄(품명·규격·창고·날짜·입출·수량·금액) · PYOJUN DOCFS S_IDAN 사전(키 = <see cref="LegacyMdbMapping.StockItemKey"/>) ·
    /// 기준일 = <see cref="MdbLegacyFinalStock.CarryBaseDate"/>(DOCFB 마지막 날짜, 이관일). 이관일은 대사표에 없으므로 이월잔액 <c>base_date</c>(F4 — 가드 발동 시 같은 날)
    /// → 없으면 오늘 로컬 날짜.
    /// </summary>
    private async Task<MdbLegacyFinalStock.RollForwardResult> RollForwardForReconAsync(
        OleDbConnection pandata, OleDbConnection? pyojun, DataTable? docfc, string docfcMaxYm, string docfbLastDate, string tenantId, CancellationToken ct)
    {
        if (!LegacyMdbMapping.TryParseLegacyDate(docfbLastDate, out var lastDate))
            throw new InvalidOperationException($"명세서 마지막 날짜를 읽지 못했습니다({docfbLastDate})");
        if (docfcMaxYm.Length != 6 || !docfcMaxYm.All(char.IsAsciiDigit))
            throw new InvalidOperationException($"최종재고 표 마지막 달을 읽지 못했습니다({docfcMaxYm})");

        // docfcMaxYm 은 6자리 숫자 확인 뒤 결합(주입 불가).
        var after = ReadMdbTable(pandata, $"SELECT * FROM DOCFB WHERE IJ_DT > '{docfcMaxYm}99'");
        var moves = new List<MdbLegacyFinalStock.LegacyStockMove>(after.Rows.Count);
        foreach (DataRow r in after.Rows)
        {
            if (!LegacyMdbMapping.TryParseLegacyDate(MdbReconPosting.CellStr(r, "IJ_DT"), out var d)) continue;
            moves.Add(new MdbLegacyFinalStock.LegacyStockMove(
                MdbReconPosting.CellStr(r, "IJ_PUM"), MdbReconPosting.CellStr(r, "IJ_KU"), MdbReconPosting.CellStr(r, "IJ_CHANG"), d, MdbReconPosting.CellStr(r, "IJ_IO"), MdbReconPosting.CellDec(r, "IJ_QTY"), MdbReconPosting.CellDec(r, "IJ_AMT")));
        }

        var idan = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (pyojun is not null)
        {
            var docfs = ReadMdbTable(pyojun, "SELECT * FROM DOCFS");
            foreach (DataRow r in docfs.Rows)
                idan.TryAdd(LegacyMdbMapping.StockItemKey(MdbReconPosting.CellStr(r, "S_PUM"), MdbReconPosting.CellStr(r, "S_KU")), MdbReconPosting.CellDec(r, "S_IDAN"));
        }

        var migrationDate = await _db.QueryFirstOrDefaultAsync<DateTime?>(new CommandDefinition(
            "SELECT MAX(base_date) FROM partner_legacy_balances WHERE tenant_id = @T",
            new { T = tenantId }, commandTimeout: ErpCommandTimeoutSec, cancellationToken: ct)).ConfigureAwait(false);
        var baseDate = MdbLegacyFinalStock.CarryBaseDate(lastDate, migrationDate ?? DateTime.Now.Date);

        return MdbLegacyFinalStock.RollForward(docfc, moves, idan, baseDate)
            ?? throw new InvalidOperationException("최종재고 표에 시작점이 없어 이어 계산을 못 했습니다");
    }

    private async Task AddPostingAndStockItemsAsync(
        OleDbConnection? pandata, OleDbConnection? pyojun,
        PostingTables? tables, string? tablesErr, string tenantId, MdbReconciliationReport report, CancellationToken ct)
    {
        var items = report.Items;

        // ②③⑧ — 레거시 분류(DOCFB 없으면 NA)
        MdbReconPosting.PostingLegacy? posting = null;
        string? postingErr = tablesErr;
        if (tables?.Docfb is not null)
        {
            (posting, postingErr) = await GuardAsync("장부 반영 분류", () => Task.FromResult(
                MdbReconPosting.ComputePosting(tables.Docfb, tables.Docfe, tables.Docf5))).ConfigureAwait(false);
        }
        else if (tables is not null)
        {
            postingErr = "DOCFB 표 없음";
        }

        var (archive, archiveErr) = await GuardAsync("보관 표(ERP)", () => MdbReconPosting.ReadArchiveAsync(_db, tenantId, ct)).ConfigureAwait(false);
        var (stmtLines, stmtErr) = await GuardAsync("명세서 줄(ERP)", async () =>
            new ScalarBox { Value = await MdbReconPosting.CountStatementLinesAsync(_db, tenantId, ct).ConfigureAwait(false) }).ConfigureAwait(false);
        ScalarBox? leftover = null;
        string? leftoverErr = null;
        if (posting is not null)
        {
            (leftover, leftoverErr) = await GuardAsync("봉합 전 옛 명세서(ERP)", async () =>
                new ScalarBox { Value = await MdbReconPosting.CountLeftoverAsync(_db, tenantId, posting.ArchivedSourceIds, ct).ConfigureAwait(false) }).ConfigureAwait(false);
        }

        MdbReconPosting.AddPostingItems(items, report.Warnings, posting, archive,
            stmtLines is null ? null : (long)stmtLines.Value, leftover is null ? null : (long)leftover.Value,
            ErrOnly(postingErr, archiveErr, stmtErr, leftoverErr));

        // ④⑤ — DOCFC 최종 · 원장 입·출
        MdbLegacyFinalStock.FinalStockResult? final = null;
        string? finalErr = tablesErr;
        if (tables?.Docfc is not null)
            (final, finalErr) = await GuardAsync("DOCFC 최종재고", () => Task.FromResult(MdbLegacyFinalStock.ComputeFinalStock(tables.Docfc)!)).ConfigureAwait(false);

        MdbReconPosting.LedgerLegacy? ledgerLegacy = null;
        if (tables?.Docfb is not null)
            (ledgerLegacy, _) = await GuardAsync("DOCFB 원장", () => Task.FromResult(MdbReconPosting.ComputeLedgerLegacy(tables.Docfb))).ConfigureAwait(false);

        var (ledgerErp, ledgerErpErr) = await GuardAsync("stock_ledger", () => MdbReconPosting.ReadLedgerErpAsync(_db, tenantId, ct)).ConfigureAwait(false);
        var (stockErp, stockErpErr) = await GuardAsync("item_stock", () => MdbReconPosting.ReadStockErpAsync(_db, tenantId, ct)).ConfigureAwait(false);

        // 🆕 3판 R2 (설계 §24 ④⑤) — DOCFC 가 DOCFB 보다 오래됐으면(가드 발동) 레거시 열 = 이관과 같은 MdbLegacyFinalStock.RollForward 결과.
        MdbLegacyFinalStock.RollForwardInfo? rollInfo = null;
        string? rollErr = null;
        if (final is not null && pandata is not null && posting?.LastValidDate is not null
            && MdbReconPosting.FinalStockStaleReason(final.MaxYm, posting.LastValidDate) is not null)
        {
            var (rolled, err) = await GuardAsync("재고 이어 계산", () => RollForwardForReconAsync(pandata, pyojun, tables!.Docfc, final.MaxYm, posting.LastValidDate, tenantId, ct)).ConfigureAwait(false);
            rollErr = err;
            if (rolled is not null)
            {
                final = rolled.Final;
                rollInfo = rolled.Info;
            }
        }

        MdbReconPosting.AddStockItems(items, report.ItemDiffs, report.Warnings,
            final, tables is not null && tables.Docfc is null, stockErp, ledgerLegacy, ledgerErp, posting?.LastValidDate,
            ErrOnly(finalErr, ledgerErpErr, stockErpErr, rollErr), rollInfo, TopDiffRows);

        _logger.LogInformation(
            "[MDB대사] 장부분류 tenant={Tenant} 보관판매={ArchS} 보관매입={ArchP} 옛명세서={Leftover} 분류못함={Unclassified} 최종재고달={FinalYm}",
            tenantId, posting?.ArchiveSales.Groups, posting?.ArchivePurchase.Groups, leftover?.Value, posting?.Unclassified, final?.MaxYm);
    }

    private async Task AddBalanceItemsAsync(
        OleDbConnection pyojun, PostingTables? tables, string? tablesErr, string tenantId, MdbReconciliationReport report, CancellationToken ct)
    {
        MdbReconPosting.BalanceLegacy? legacy = null;
        string? legacyErr = tablesErr;
        var docf5Missing = tables is not null && (tables.Docf5 is null || tables.Docf5.Rows.Count == 0);
        if (tables?.Docf5 is { Rows.Count: > 0 })
            (legacy, legacyErr) = await GuardAsync("F3 잔액", () => Task.FromResult(MdbReconPosting.ComputeBalanceLegacy(tables.Docf5)!)).ConfigureAwait(false);

        var (erp, erpErr) = await GuardAsync("미수·미지급(ERP)", () => MdbReconPosting.ReadBalanceErpAsync(_db, tenantId, ct)).ConfigureAwait(false);
        MdbReconPosting.AddBalanceItems(report.Items, report.Warnings, legacy, docf5Missing, erp, ErrOnly(legacyErr, erpErr));

        if (legacy is null) return;

        // 거래처별 차이 — F3(코드별)를 히트판 거래처로 모아(옛 코드 여러 개 → 한 거래처) 히트판 거래처 잔액과 비교.
        var (legacyNames, namesErr) = await GuardAsync("DOCF8", async () =>
        {
            var names = new Dictionary<int, string>();
            await ReadMdbAsync(pyojun, "SELECT buy_code, buy_name FROM DOCF8",
                r => names[ReadInt(r, 0)] = ReadStr(r, 1), ct).ConfigureAwait(false);
            return names;
        }).ConfigureAwait(false);
        if (namesErr is not null)
            _logger.LogWarning("[MDB대사] DOCF8 거래처명 읽기 실패 — 거래처 차이 목록은 코드로 표시: {Err}", namesErr);

        var (erpRows, rowsErr) = await GuardAsync("거래처별 잔액(ERP)", async () =>
            (await _db.QueryAsync<PartnerErpRow>(new CommandDefinition(
                MdbReconPosting.PartnerBalanceErpSql,
                new { TenantId = tenantId }, commandTimeout: ErpCommandTimeoutSec, cancellationToken: ct)).ConfigureAwait(false)).ToList()).ConfigureAwait(false);
        if (rowsErr is not null)
        {
            _logger.LogWarning("[MDB대사] 거래처별 ERP 잔액 읽기 실패 — 거래처 차이 목록 생략: {Err}", rowsErr);
            return;
        }
        BuildPartnerDiffs(new Dictionary<int, decimal>(legacy.ByCode), legacyNames, erpRows!, report.PartnerDiffs);
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
        // 20260915작1 갈래 C: 종전 거래처별 사전(작21 순잔액 · 작22 DOCFE−DOCF5)은 F3(AddBalanceItemsAsync)로 대체돼 뺐다.
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
        OleDbConnection pandata, string tenantId, List<ReconItem> items, CancellationToken ct)
    {
        // 🔴 20260915작1 갈래 C: ⑧⑨ 미수·미지급과 거래처별 차이는 AddBalanceItemsAsync(F3 ↔ 갈래 E 식)로 옮겼다.
        //   종전 작22 새 식(DOCFE 매출 − DOCF5 수금)은 레거시 이월잔액을 모르는 식이라 F3 와 다른 숫자를 냈다(설계 §17).
        //   여기엔 ⑦ 수금(계열 합)만 남는다.
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
            return agg;
        }).ConfigureAwait(false);

        // DESCRIBE 확인: collections(amount,source_type,is_active).
        var (erpColl, collErr) = await GuardAsync("수금(ERP)", () =>
            _db.QuerySingleAsync<CollectionErpAgg>(new CommandDefinition(
                """
                SELECT COALESCE(SUM(amount), 0) AS AmountSum, COUNT(*) AS Cnt
                  FROM collections
                 WHERE tenant_id=@T AND source_type='migration' AND is_active=1
                """, new { T = tenantId }, commandTimeout: ErpCommandTimeoutSec, cancellationToken: ct))).ConfigureAwait(false);

        items.Add(Make("collections", "수금", "수금", legacy?.CollectionSum, erpColl?.AmountSum,
            JoinDetail(
                legacy is null ? null : $"레거시 수금 {legacy.CollectionRows:N0}건",
                erpColl is null ? null : $"히트판 수금 {erpColl.Cnt:N0}건",
                ErrOnly(legacyErr, collErr))));
    }

    /// <summary>
    /// 거래처별 잔액 차이 상위 20 — 🔴 20260915작1 갈래 C: 레거시 코드별 F3 를 <b>히트판 거래처로 먼저 모은다</b>
    /// (옛 코드 여러 개가 한 거래처로 합쳐지고 · 못 찾은 코드는 이관과 같이 폴백 거래처 <see cref="MdbLegacyPartnerBalance.FallbackPartnerCode"/> 로 간다).
    /// 종전엔 코드마다 거래처 전체 잔액과 비교해 합쳐진 거래처가 늘 차이로 보였다.
    /// </summary>
    /// <param name="legacyNetByPartner">거래처코드 → 레거시 F3 잔액(+ 미수 · − 미지급).</param>
    private void BuildPartnerDiffs(
        Dictionary<int, decimal> legacyNetByPartner, Dictionary<int, string>? legacyNames,
        List<PartnerErpRow> erpRows, List<ReconDiffRow> partnerDiffs)
    {
        var byHash = new Dictionary<string, PartnerErpRow>(StringComparer.OrdinalIgnoreCase);
        var byCode = new Dictionary<string, PartnerErpRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in erpRows)
        {
            if (!string.IsNullOrEmpty(row.MigratedSourceHash)) byHash.TryAdd(row.MigratedSourceHash, row);
            if (!string.IsNullOrEmpty(row.PartnerCode)) byCode.TryAdd(row.PartnerCode, row);
        }
        byCode.TryGetValue(MdbLegacyPartnerBalance.FallbackPartnerCode, out var fallback);

        var legacyByPartner = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var unmatched = new List<ReconDiffRow>();
        foreach (var (buyCode, legacyNet) in legacyNetByPartner)
        {
            if (legacyNet == 0m) continue;
            var hash = ComputeSourceHash($"partners:buy_code:{buyCode}");
            if (!byHash.TryGetValue(hash, out var erp) && !byCode.TryGetValue($"MIG-{buyCode:D5}", out erp))
                erp = fallback;
            if (erp?.PartnerId is null)
            {
                var name = legacyNames is not null && legacyNames.TryGetValue(buyCode, out var ln) && !string.IsNullOrWhiteSpace(ln) ? ln.Trim() : $"거래처코드 {buyCode}";
                unmatched.Add(new ReconDiffRow { Name = name, Legacy = legacyNet, Erp = 0m, Diff = -legacyNet });
                continue;
            }
            legacyByPartner[erp.PartnerId] = (legacyByPartner.TryGetValue(erp.PartnerId, out var cur) ? cur : 0m) + legacyNet;
        }

        var diffs = new List<ReconDiffRow>(unmatched);
        foreach (var row in erpRows)
        {
            if (row.PartnerId is null) continue;
            var erpNet = row.Receivable - row.Payable;
            var legacyNet = legacyByPartner.TryGetValue(row.PartnerId, out var l) ? l : 0m;
            var diff = erpNet - legacyNet;
            if (diff == 0m) continue;
            diffs.Add(new ReconDiffRow
            {
                Name = string.IsNullOrWhiteSpace(row.PartnerName) ? (row.PartnerCode ?? row.PartnerId) : row.PartnerName.Trim(),
                Legacy = legacyNet,
                Erp = erpNet,
                Diff = diff
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
        public long FallbackEmployees { get; set; }   // 작22 ⑩ · 20260910작1 — 이관분이 아닌 사원(대표 1행 · LEGACY_FALLBACK 자리표시). 건수에서 뺀다
        public long BomHeaders { get; set; }
        public long BomLines { get; set; }
        public long FallbackPartners { get; set; }   // 🆕 3판 R2 F2 — 받이 거래처(LEGACY_UNKNOWN_PTNR)
        public long FallbackItems { get; set; }      // 🆕 3판 R2 F2 — 받이 품목(LEGACY_UNKNOWN_ITEM)
    }

    /// <summary>받이 품목 코드 — <c>MdbMigrationService.EnsureLegacyFallbackItemAsync</c> 의 <c>itemCode</c> 와 같은 값(그 파일은 R2 범위 밖이라 값만 맞춘다).</summary>
    private const string FallbackItemCode = "LEGACY_UNKNOWN_ITEM";

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

        // DESCRIBE 확인: partners.is_deleted · items.is_deleted/item_code · employees(tenant_id · emp_no varchar(20) NOT NULL — 2026-09-09 재확인) · bom_headers/bom_items(tenant_id).
        // 작22 (2026-09-09) C2 ⑩: 사원 건수에서 자리표시 사원(emp_no='LEGACY_FALLBACK' · MigrateExpensesAsync 가 매핑 안 되는 사원용으로 만든 1행)을 뺀다 —
        //   레거시 DOCSW 에 없는 행이라 ERP 11 = MIG 10 + 1 로 늘 DIFF 였다(선행검증 §2-6).
        //
        // 🔴 20260910작1 A1: 이제 <b>이관해 온 사원만</b> 센다(emp_no LIKE 'MIG-%').
        //   덮어쓰기가 회사 뼈대를 다시 깔면 **대표 사원 1행(emp_no '0001')** 이 늘 있다. 실제 고객 설치도 마찬가지다
        //   (신규 설치가 부모계정과 함께 만든다). 그 1행 때문에 이 항목은 언제나 레거시보다 1 많아진다.
        //   ⚠️ e2e 테스트 DB 는 부트스트랩을 안 태워 대표가 없었기 때문에 지금까지 이 차이가 안 보였다 —
        //     "테스트에서 안 보인다 ≠ 고객에게 안 보인다"(DB-100 사고 계통).
        //   이 항목이 묻는 것은 "레거시 사원이 다 들어왔나" 이므로, 히트판에서 따로 만든 사원은 세지 않는 것이 맞다.
        var (erp, erpErr) = await GuardAsync("마스터(ERP)", () =>
            _db.QuerySingleAsync<MasterCounts>(new CommandDefinition(
                """
                SELECT
                  (SELECT COUNT(*) FROM partners  WHERE tenant_id=@T AND is_deleted=0) AS Partners,
                  (SELECT COUNT(*) FROM items     WHERE tenant_id=@T AND is_deleted=0) AS Items,
                  (SELECT COUNT(*) FROM items     WHERE tenant_id=@T AND is_deleted=0 AND item_code LIKE 'MIG-AUTO-%') AS AutoItems,
                  (SELECT COUNT(*) FROM employees WHERE tenant_id=@T AND emp_no LIKE 'MIG-%') AS Employees,
                  (SELECT COUNT(*) FROM employees WHERE tenant_id=@T AND emp_no NOT LIKE 'MIG-%') AS FallbackEmployees,
                  (SELECT COUNT(*) FROM bom_headers WHERE tenant_id=@T) AS BomHeaders,
                  (SELECT COUNT(*) FROM bom_items   WHERE tenant_id=@T) AS BomLines,
                  (SELECT COUNT(*) FROM partners  WHERE tenant_id=@T AND is_deleted=0 AND partner_code=@FallbackPartner) AS FallbackPartners,
                  (SELECT COUNT(*) FROM items     WHERE tenant_id=@T AND is_deleted=0 AND item_code=@FallbackItem) AS FallbackItems
                """, new { T = tenantId, FallbackPartner = MdbLegacyPartnerBalance.FallbackPartnerCode, FallbackItem = FallbackItemCode },
                commandTimeout: ErpCommandTimeoutSec, cancellationToken: ct))).ConfigureAwait(false);

        var err = ErrOnly(legacyErr, erpErr);
        // 🆕 3판 R2 (설계 §24 F2) — 설명된 차이(받이 거래처 · 자동등록 품목 + 받이 품목)는 「사유 확인됨」, 나머지만 차이.
        items.Add(MdbReconPosting.ApplyExplained(
            Make("master_partners", "마스터", "업체", legacy?.Partners, erp?.Partners, err),
            erp?.FallbackPartners ?? 0, "받이 거래처"));
        items.Add(MdbReconPosting.ApplyExplained(
            Make("master_items", "마스터", "상품", legacy?.Items, erp?.Items,
                JoinDetail(erp is null ? null : $"히트판 자동등록 품목 {erp.AutoItems:N0}건 포함", err)),
            (erp?.AutoItems ?? 0) + (erp?.FallbackItems ?? 0),
            erp is null ? "자동등록·받이 품목" : $"자동등록 {erp.AutoItems:N0} · 받이 품목 {erp.FallbackItems:N0} ="));
        items.Add(Make("master_employees", "마스터", "사원", legacy?.Employees, erp?.Employees,
            JoinDetail(erp is null || erp.FallbackEmployees == 0 ? null : $"히트판에서 따로 만든 사원 {erp.FallbackEmployees:N0}명 제외(대표·자리표시)", err)));
        items.Add(Make("master_bom", "마스터", "BOM", legacy?.BomHeaders, erp?.BomHeaders,
            JoinDetail(
                legacy is null ? null : $"레거시 자재 라인 {legacy.BomLines:N0}",
                erp is null ? null : $"히트판 자재 라인 {erp.BomLines:N0}",
                err)));
    }

    // ────────────────────────────────────────────────────────────────
    // ⑪ 일일보고서 · ⑫ 이관 건수 4항목 — 작22 (2026-09-09) C3
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⑪ 일일보고서 — 레거시 = POTHER.DOCME 의 (ME_SAWON, ME_DATE) 묶음 수(8자리 아님·연도 0000 제외 = 갈래 D 가 한 장으로 만드는 단위 · 이 MDB 9,167) ↔
    /// ERP = <c>hr_reports</c> 의 이관 행 수. 갈래 D 와의 인터페이스는 <c>source_type='migration'</c> 한 줄뿐이다.
    /// DESCRIBE hr_reports (hitpan_e2e 2026-09-09): <c>source_type</c> 컬럼 <b>없음</b> — 갈래 D 의 DB-119 가 추가한다.
    /// 그 전엔 ERP 쪽 질의가 실패해 <see cref="GuardAsync"/> 가 NA 로 남긴다(정상 · 컬럼이 생기면 저절로 값이 찬다).
    /// </summary>
    private async Task AddDailyReportItemAsync(
        OleDbConnection? pother, string? potherErr, string tenantId, List<ReconItem> items, CancellationToken ct)
    {
        ScalarBox? legacy = null;
        var legacyErr = potherErr;
        if (pother is not null)
        {
            (legacy, legacyErr) = await GuardAsync("DOCME", async () =>
            {
                var box = new ScalarBox();
                await ReadMdbAsync(pother,
                    "SELECT COUNT(*) FROM (SELECT DISTINCT ME_SAWON, ME_DATE FROM DOCME WHERE LEN(ME_DATE)=8 AND LEFT(ME_DATE,4)<>'0000') AS g",
                    r => box.Value = ReadLong(r, 0), ct).ConfigureAwait(false);
                return box;
            }).ConfigureAwait(false);
        }

        var (erp, erpErr) = await GuardAsync("hr_reports", () =>
            _db.QuerySingleAsync<ScalarBox>(new CommandDefinition(
                "SELECT COUNT(*) AS Value FROM hr_reports WHERE tenant_id=@T AND source_type='migration'",
                new { T = tenantId }, commandTimeout: ErpCommandTimeoutSec, cancellationToken: ct))).ConfigureAwait(false);

        items.Add(Make("daily_reports", "그룹웨어", "일일보고서(레거시 상담·메모) 건수", legacy?.Value, erp?.Value,
            JoinDetail("레거시 = DOCME (작성자, 날짜) 묶음 · 연도 0000 제외", ErrOnly(legacyErr, erpErr))));
    }

    private sealed class RowCountLegacy
    {
        public long Cashbook;                // DOCF6 · AC_JEN ≠ '0' (월계 제외 — MigrateCashbookAsync 가 skip 하는 것과 같은 조건)
        public long Expenses;                // DOCF7 · SC_CR>0 OR SC_DR≠0 (MigrateExpensesAsync: amount = cr>0 ? cr : dr · 0 이면 skip)
        public long BankTx;                  // BANKF · BK_NO 비지 않고 BK_AMT>0 (MigrateBankTransactionsAsync)
        public List<string> PoNos = new();   // DOCFA IU_NO — 빈 번호·미등록 거래처 제외 (MigratePurchaseOrdersFromIUAsync)
        public List<string> SoNos = new();   // DOCFO IO_NO — 같은 규칙 (MigrateSalesOrdersFromIOAsync)
        public long PoSkipped;
        public long SoSkipped;
    }

    private sealed class RowCountErp
    {
        public long Cashbook { get; set; }
        public long Expenses { get; set; }
        public long BankTx { get; set; }
    }

    /// <summary>
    /// ⑫ 이관 건수 4항목 — 레거시 열의 필터는 <b>해당 Migrate*Async 가 실제로 넣는 행</b>과 같게 맞췄다(코드를 읽고 옮김 · 복붙 아님).
    /// ERP 열은 <c>source_type='migration'</c> 건수. 발주·수주 표엔 그 컬럼이 없어(DESCRIBE 2026-09-09) 멱등키 <c>po_no</c>/<c>order_no</c>(= IU_NO/IO_NO)로 대조한다.
    /// 금액 의미 대조는 W4(설계 별지 §3 C3).
    /// DESCRIBE 확인(hitpan_e2e 2026-09-09): cashbook·expenses·bank_transactions 에 source_type varchar(30) NULL 실재 ·
    ///   purchase_orders(po_no varchar(20) · is_deleted · source_type 없음) · sales_orders(order_no varchar(20) · is_deleted · source_type 없음).
    /// </summary>
    private async Task AddRowCountItemsAsync(
        OleDbConnection pandata, OleDbConnection pyojun, string tenantId, List<ReconItem> items, CancellationToken ct)
    {
        var (legacy, legacyErr) = await GuardAsync("이관 건수(레거시)", async () =>
        {
            var c = new RowCountLegacy();
            // DOCF6: CashbookDirection("0") = skip(월계). NULL·빈값은 expense 로 들어가므로 센다.
            await ReadMdbAsync(pandata, "SELECT COUNT(*) FROM DOCF6 WHERE AC_JEN IS NULL OR TRIM(AC_JEN) <> '0'",
                r => c.Cashbook = ReadLong(r, 0), ct).ConfigureAwait(false);
            // DOCF7: amount = SC_CR>0 ? SC_CR : SC_DR · amount==0 이면 skip ⇒ 남는 행 = SC_CR>0 OR SC_DR<>0 (NULL 은 0).
            await ReadMdbAsync(pandata, "SELECT COUNT(*) FROM DOCF7 WHERE IIF(SC_CR IS NULL,0,SC_CR) > 0 OR IIF(SC_DR IS NULL,0,SC_DR) <> 0",
                r => c.Expenses = ReadLong(r, 0), ct).ConfigureAwait(false);
            // BANKF: BK_NO 공백이면 skip · BK_AMT<=0 이면 skip.
            await ReadMdbAsync(pandata, "SELECT COUNT(*) FROM BANKF WHERE IIF(BK_NO IS NULL,'',TRIM(BK_NO)) <> '' AND IIF(BK_AMT IS NULL,0,BK_AMT) > 0",
                r => c.BankTx = ReadLong(r, 0), ct).ConfigureAwait(false);

            // 발주·수주: IU_NO/IO_NO 로 묶은 헤더 · 빈 번호 skip · 거래처(첫 라인 IU_BUY)가 partnerMap 에 없으면 skip.
            //   partnerMap 은 DOCF8 전 행이므로 "DOCF8 에 있는 buy_code" 로 같은 조건을 만든다(다른 MDB 파일이라 조인 불가 → 메모리 집합).
            //   첫 라인 대신 MIN(IU_BUY) 를 쓴다 — 한 번호 안에서 거래처는 같다(다르면 이관 쪽도 첫 행 하나로 정하므로 헤더 수는 같다).
            var buyCodes = new HashSet<int>();
            await ReadMdbAsync(pyojun, "SELECT buy_code FROM DOCF8", r => buyCodes.Add(ReadInt(r, 0)), ct).ConfigureAwait(false);
            await ReadMdbAsync(pandata, "SELECT IU_NO, MIN(IU_BUY) AS buy FROM DOCFA GROUP BY IU_NO",
                r =>
                {
                    var no = ReadStr(r, 0);
                    if (no.Length == 0 || !buyCodes.Contains(ReadInt(r, 1))) { c.PoSkipped++; return; }
                    c.PoNos.Add(no);
                }, ct).ConfigureAwait(false);
            await ReadMdbAsync(pandata, "SELECT IO_NO, MIN(IO_BUY) AS buy FROM DOCFO GROUP BY IO_NO",
                r =>
                {
                    var no = ReadStr(r, 0);
                    if (no.Length == 0 || !buyCodes.Contains(ReadInt(r, 1))) { c.SoSkipped++; return; }
                    c.SoNos.Add(no);
                }, ct).ConfigureAwait(false);
            return c;
        }).ConfigureAwait(false);

        var (erp, erpErr) = await GuardAsync("이관 건수(ERP)", () =>
            _db.QuerySingleAsync<RowCountErp>(new CommandDefinition(
                """
                SELECT
                  (SELECT COUNT(*) FROM cashbook          WHERE tenant_id=@T AND source_type='migration') AS Cashbook,
                  (SELECT COUNT(*) FROM expenses          WHERE tenant_id=@T AND source_type='migration') AS Expenses,
                  (SELECT COUNT(*) FROM bank_transactions WHERE tenant_id=@T AND source_type='migration') AS BankTx
                """, new { T = tenantId }, commandTimeout: ErpCommandTimeoutSec, cancellationToken: ct))).ConfigureAwait(false);

        // 발주·수주 ERP 건수 — 레거시 번호 목록으로 po_no/order_no IN (…) 을 1,000개씩 끊어 센다(표식 컬럼이 없어 번호가 곧 멱등키).
        long? erpPo = null, erpSo = null;
        string? ordersErr = null;
        if (legacy is not null)
        {
            var (po, poErr) = await GuardAsync("purchase_orders", async () =>
                new ScalarBox { Value = await CountOrdersByNoAsync("purchase_orders", tenantId, legacy.PoNos, ct).ConfigureAwait(false) }).ConfigureAwait(false);
            var (so, soErr) = await GuardAsync("sales_orders", async () =>
                new ScalarBox { Value = await CountOrdersByNoAsync("sales_orders", tenantId, legacy.SoNos, ct).ConfigureAwait(false) }).ConfigureAwait(false);
            erpPo = po is null ? null : (long)po.Value;
            erpSo = so is null ? null : (long)so.Value;
            ordersErr = ErrOnly(poErr, soErr);
        }

        var err = ErrOnly(legacyErr, erpErr);
        items.Add(Make("count_cashbook", "이관건수", "현금출납 건수", legacy?.Cashbook, erp?.Cashbook,
            JoinDetail("레거시 DOCF6 · 월계(AC_JEN=0) 제외", err)));
        items.Add(Make("count_expenses", "이관건수", "경비(전표) 건수", legacy?.Expenses, erp?.Expenses,
            JoinDetail("레거시 DOCF7 · 금액 0 행 제외", err)));
        items.Add(Make("count_bank_transactions", "이관건수", "은행거래 건수", legacy?.BankTx, erp?.BankTx,
            JoinDetail("레거시 BANKF · 계좌번호 없음·금액 0 이하 제외", err)));
        items.Add(Make("count_orders", "이관건수", "발주·수주 건수",
            legacy is null ? null : (decimal?)(legacy.PoNos.Count + legacy.SoNos.Count),
            erpPo is null || erpSo is null ? null : erpPo + erpSo,
            JoinDetail(
                legacy is null ? null : $"레거시 발주 {legacy.PoNos.Count:N0} + 수주 {legacy.SoNos.Count:N0} (빈 번호·미등록 거래처 제외 발주 {legacy.PoSkipped:N0} · 수주 {legacy.SoSkipped:N0})",
                erpPo is null || erpSo is null ? null : $"히트판 발주 {erpPo:N0} + 수주 {erpSo:N0} (번호로 대조)",
                legacyErr, ordersErr)));
    }

    /// <summary>
    /// 발주·수주 표를 레거시 번호 목록으로 센다 — <c>IN</c> 을 1,000개씩 끊는다(번호가 수만 개여도 한 문장이 커지지 않게).
    /// SQL 은 표마다 고정 문장(문자열 조립 없음). tenant_id 필터 + is_deleted=0.
    /// </summary>
    private async Task<long> CountOrdersByNoAsync(string table, string tenantId, List<string> numbers, CancellationToken ct)
    {
        var sql = table switch
        {
            "purchase_orders" => "SELECT COUNT(*) FROM purchase_orders WHERE tenant_id=@T AND is_deleted=0 AND po_no IN @Nos",
            "sales_orders" => "SELECT COUNT(*) FROM sales_orders WHERE tenant_id=@T AND is_deleted=0 AND order_no IN @Nos",
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "발주·수주 표만 센다."),
        };
        long total = 0;
        for (var i = 0; i < numbers.Count; i += 1000)
        {
            var chunk = numbers.Skip(i).Take(1000).ToList();
            total += await _db.ExecuteScalarAsync<long>(new CommandDefinition(
                sql, new { T = tenantId, Nos = chunk }, commandTimeout: ErpCommandTimeoutSec, cancellationToken: ct)).ConfigureAwait(false);
        }
        return total;
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

    /// <summary>
    /// 품명|규격 키 — 규칙은 <see cref="LegacyMdbMapping.ItemKey"/> 한 군데(작22 (2026-09-09) C2 ⑤: trim · 대소문자 무시 · 끝의 '|' 제거).
    /// 종전 이 자리의 식은 규격이 비면 <c>"품명|"</c> 을 만들어, item_name 에 <c>"품명|규격"</c> 이 통째로 든 옛 MIG-AUTO 441건과 어긋났다.
    /// </summary>
    private static string ItemKey(string? name, string? spec) => LegacyMdbMapping.ItemKey(name, spec);

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
    /// 대사에 필요한 PYOJUN·PANDATA 가 없으면 FileNotFoundException. POTHER 는 경로만 돌려주고 여는 건 <see cref="TryOpenPother"/> 가 한다(작22 ⑪ · 없으면 NA).
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
            _logger.LogInformation("[MDB대사] ACE OLEDB 12.0 없음 — 16.0 으로 재시도 file={File}", ForLog(Path.GetFileName(mdbPath)));
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

    /// <summary>
    /// POTHER.mdb 를 읽기 전용으로 연다 — 선택 파일 (작22 (2026-09-09) C3 ⑪). 없으면 null + 사유, 열다 실패해도 null + 사유.
    /// PANDATA·PYOJUN 과 달리 던지지 않는 이유: 일일보고서 한 항목 때문에 대사표 전체가 안 나오면 안 된다. 사유는 로그와 Detail 에 남긴다(헌법 #15).
    /// </summary>
    private OleDbConnection? TryOpenPother(string potherPath, string? password, out string? error)
    {
        if (!File.Exists(potherPath))
        {
            error = "POTHER.mdb 없음 — 일일보고서 대사 생략";
            _logger.LogInformation("[MDB대사] POTHER.mdb 없음 — ⑪ 일일보고서 항목은 NA path={Path}", ForLog(potherPath));
            return null;
        }
        try
        {
            error = null;
            return OpenMdb(potherPath, password);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MDB대사] POTHER.mdb 열기 실패 — ⑪ 일일보고서 항목만 NA");
            error = $"POTHER.mdb 열기 실패: {ex.Message}";
            return null;
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
    /// <summary>
    /// 🆕 20260915작1 갈래 C — 표로 못 담는 알림(사람 말). 예: 「덮어쓰기로 다시 가져오기가 필요합니다」 · 머리표 없음 — 분류 못 함 ·
    /// 거래처원장 표 없음 · 최종재고 표가 최신이 아님.
    /// </summary>
    public List<string> Warnings { get; set; } = new();
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

/// <summary>
/// 🔴 20260915작1 갈래 C — 대사표 「장부 반영 분류」 계산 (설계 §7 · §14 · §16 · §17 · 작업지시서 §11-2 C).
/// <para>MDB(OLEDB)를 모르는 부분만 모았다 — 입력은 DataTable(레거시 표) 과 IDbConnection(히트판). OS 무관이라 G6 게이트가 합성 표로 부른다.</para>
/// <para>🔴 규칙을 새로 짓지 않는다: 판정 = <see cref="MdbLegacyUnpostedArchive.Partition"/>(이관과 같은 함수) · F3 = <see cref="LegacyMdbMapping.PartnerLegacyBalances"/> ·
/// 재고 최종 = <see cref="MdbLegacyFinalStock.ComputeFinalStock"/> · 원장 입·출 = <see cref="LegacyMdbMapping.LedgerMove"/> ·
/// 히트판 미수·미지급 = <c>FinanceService</c> KPI 식(갈래 E 규칙 · <see cref="MdbLegacyPartnerBalance.HasLegacyBalanceSql"/>).</para>
/// DESCRIBE (#13 · 2026-09-15 격리 33306 f_new): legacy_unposted_documents(io_type · supply_amount · vat_amount · line_count · tenant_id) ·
/// legacy_unposted_document_lines(doc_id · tenant_id) · sales_deliveries(source_id varchar(80) · source_type · is_deleted) ·
/// purchase_receipts(source_id varchar(80) · source_type) · stock_ledger(source_type varchar(30) · source_id varchar(36) · qty_in · qty_out decimal(15,3) · supply_amount) ·
/// item_stock(current_qty decimal(10,2) · avg_cost decimal(19,6)) · partner_legacy_balances(partner_id · balance_amount decimal(15,2)) · partners(partner_code · migrated_source_hash).
/// </summary>
public static class MdbReconPosting
{
    public const string StatusOk = "OK";
    public const string StatusDiff = "DIFF";
    public const string StatusNa = "NA";
    /// <summary>🆕 3판 R2 (작지 §15-2 계약) — 차이 = 설명 수(받이 거래처·자동등록 품목)와 정확히 같음. 화면 문구 「사유 확인됨」(R4).</summary>
    public const string StatusExplained = "EXPLAINED";

    /// <summary>화면 알림 — 덮어쓰기 필요.</summary>
    public const string WarnLeftover = "봉합 전에 가져온 명세서 중 지금은 「장부 미반영 보관」 대상인 것이 남아 있습니다. 덮어쓰기로 다시 가져오기가 필요합니다.";
    public const string WarnUnclassified = "이전 프로그램에 명세서 머리표가 없어 장부 반영 여부를 분류하지 못했습니다 — 모든 명세서를 장부에 반영했습니다.";
    public const string WarnNoLedgerTable = "이전 프로그램에 거래처원장 표가 없어 명세서 머리표로만 분류했습니다 — 미수·미지급 대사는 할 수 없습니다.";
    public const string WarnNoFinalStock = "이전 프로그램에 최종재고 표가 없어 재고 기말을 대사할 수 없습니다.";

    private const int CommandTimeoutSec = 180;

    /// <summary>항목 하나 — 한쪽 null 이면 NA · 차 0 이면 OK · 그 외 DIFF · 차 = 히트판 − 레거시 (대사 서비스 Make 와 같은 규칙).</summary>
    public static ReconItem Item(string key, string category, string label, decimal? legacy, decimal? erp, string? detail)
    {
        var item = new ReconItem { Key = key, Category = category, Label = label, Legacy = legacy, Erp = erp, Detail = detail };
        if (legacy is null || erp is null) { item.Status = StatusNa; return item; }
        item.Diff = erp.Value - legacy.Value;
        item.Status = item.Diff == 0m ? StatusOk : StatusDiff;
        return item;
    }

    private static string? Join(params string?[] parts)
    {
        var list = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return list.Count == 0 ? null : string.Join(" · ", list);
    }

    // ════════════════════════════════════════════════════════════════
    // ② ③ ⑧ 장부 반영 분류 (레거시)
    // ════════════════════════════════════════════════════════════════

    /// <summary>한 종류(판매·매입) 합계.</summary>
    public sealed class SideAgg
    {
        public long Groups { get; set; }
        public long Lines { get; set; }
        public decimal Supply { get; set; }
        public decimal Vat { get; set; }
    }

    /// <summary>DOCFB 분류 결과 (레거시 열).</summary>
    public sealed class PostingLegacy
    {
        /// <summary>DOCFE 없음/0행 → 분류 못 함(R5).</summary>
        public bool Unclassified { get; set; }
        /// <summary>DOCF5 없음/0행 → 머리표로만 분류.</summary>
        public bool NoLedgerTable { get; set; }
        public int InputLines { get; set; }
        public int StatementLines { get; set; }
        public int ArchivedLines { get; set; }
        public int UnknownIoLines { get; set; }
        public SideAgg ArchiveSales { get; } = new();
        public SideAgg ArchivePurchase { get; } = new();
        /// <summary>보관 묶음의 명세서 source_id(<c>mig-docfb-{dt}-{io}-{seq}-{buy}</c> · 이관 명세서·보관 표와 같은 글자) — excluded_leftover 용.</summary>
        public List<string> ArchivedSourceIds { get; } = new();
        /// <summary>DOCFB 마지막 유효 날짜(yyyyMMdd) — 최종재고 표 신선도 비교용.</summary>
        public string? LastValidDate { get; set; }
    }

    /// <summary>
    /// DOCFB(IJ_DT·IJ_IO·IJ_SEQ·IJ_BUY·IJ_AMT·IJ_VAT) + DOCFE(IJA_DT·IJA_IO·IJA_SEQ·IJA_BUY) + DOCF5(S_BUY·S_YMD·S_GU·S_BAL·S_SUK·S_SSUN) → 분류.
    /// 판정은 이관과 같은 <see cref="MdbLegacyUnpostedArchive.Partition"/> — 보관 = 미반영 또는 날짜 없음.
    /// </summary>
    public static PostingLegacy ComputePosting(DataTable docfb, DataTable? docfe, DataTable? docf5)
    {
        ArgumentNullException.ThrowIfNull(docfb);
        var headerKeys = LegacyMdbMapping.BuildHeaderKeySet(docfe);
        var (salesLinks, purchaseLinks) = LegacyMdbMapping.BuildLedgerLinkKeySets(docf5);
        var p = MdbLegacyUnpostedArchive.Partition(docfb, headerKeys, salesLinks, purchaseLinks);

        var result = new PostingLegacy
        {
            Unclassified = p.Unclassified,
            NoLedgerTable = salesLinks is null,
            InputLines = p.InputLines,
            StatementLines = p.StatementLines,
            ArchivedLines = p.ArchivedLines,
            UnknownIoLines = p.UnknownIoLines,
        };
        foreach (var g in p.Archived)
        {
            var side = g.Key.Io == 2 ? result.ArchiveSales : result.ArchivePurchase;
            side.Groups++;
            side.Lines += g.Rows.Count;
            foreach (DataRow r in g.Rows)
            {
                side.Supply += RowDec(r, "IJ_AMT");
                side.Vat += RowDec(r, "IJ_VAT");
            }
            result.ArchivedSourceIds.Add($"mig-docfb-{g.Key.Dt}-{g.Key.Io}-{g.Key.Seq}-{g.Key.Buy}");
        }
        foreach (DataRow r in docfb.Rows)
        {
            var dt = RowStr(r, "IJ_DT").Trim();
            if (LegacyMdbMapping.TryParseLegacyDate(dt, out _) && (result.LastValidDate is null || string.CompareOrdinal(dt, result.LastValidDate) > 0))
                result.LastValidDate = dt;
        }
        return result;
    }

    /// <summary>히트판 보관 표 합계.</summary>
    public sealed class ArchiveErp
    {
        public SideAgg Sales { get; } = new();
        public SideAgg Purchase { get; } = new();
    }

    private sealed class ArchiveRow
    {
        public string? IoType { get; set; }
        public long Docs { get; set; }
        public long LineCnt { get; set; }
        public decimal Supply { get; set; }
        public decimal Vat { get; set; }
    }

    /// <summary>legacy_unposted_documents/_lines 종류별 건·줄(실제 줄 표)·공급가·부가세.</summary>
    public static async Task<ArchiveErp> ReadArchiveAsync(IDbConnection db, string tenantId, CancellationToken ct)
    {
        var rows = await db.QueryAsync<ArchiveRow>(new CommandDefinition(
            """
            SELECT d.io_type AS IoType, COUNT(*) AS Docs,
                   COALESCE(SUM((SELECT COUNT(*) FROM legacy_unposted_document_lines l WHERE l.tenant_id = d.tenant_id AND l.doc_id = d.doc_id)), 0) AS LineCnt,
                   COALESCE(SUM(d.supply_amount), 0) AS Supply, COALESCE(SUM(d.vat_amount), 0) AS Vat
              FROM legacy_unposted_documents d
             WHERE d.tenant_id = @T
             GROUP BY d.io_type
            """, new { T = tenantId }, commandTimeout: CommandTimeoutSec, cancellationToken: ct)).ConfigureAwait(false);
        var e = new ArchiveErp();
        foreach (var r in rows)
        {
            var side = r.IoType == "sales" ? e.Sales : r.IoType == "purchase" ? e.Purchase : null;
            if (side is null) continue;
            side.Groups += r.Docs; side.Lines += r.LineCnt; side.Supply += r.Supply; side.Vat += r.Vat;
        }
        return e;
    }

    /// <summary>이관 명세서 줄(판매 줄 + 매입 줄 · source_type='migration').</summary>
    public static Task<long> CountStatementLinesAsync(IDbConnection db, string tenantId, CancellationToken ct)
        => db.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT
              (SELECT COUNT(*) FROM sales_delivery_items i JOIN sales_deliveries d ON d.delivery_id = i.delivery_id AND d.tenant_id = i.tenant_id
                WHERE d.tenant_id = @T AND d.source_type = 'migration' AND d.is_deleted = 0)
            + (SELECT COUNT(*) FROM purchase_receipt_items i JOIN purchase_receipts r ON r.receipt_id = i.receipt_id AND r.tenant_id = i.tenant_id
                WHERE r.tenant_id = @T AND r.source_type = 'migration')
            """, new { T = tenantId }, commandTimeout: CommandTimeoutSec, cancellationToken: ct));

    /// <summary>
    /// ⑧ excluded_leftover — 지금 판정으로는 보관 대상인데 히트판에 명세서(이관)로 남은 행 수. 봉합 전 이관 뒤 「보태기」로 다시 가져온 DB 에서 생긴다.
    /// source_id 는 이관 명세서·보관 표가 같은 글자라 그대로 맞춘다. 1,000개씩 끊는다.
    /// </summary>
    public static async Task<long> CountLeftoverAsync(IDbConnection db, string tenantId, IReadOnlyList<string> archivedSourceIds, CancellationToken ct)
    {
        long total = 0;
        for (var i = 0; i < archivedSourceIds.Count; i += 1000)
        {
            var chunk = archivedSourceIds.Skip(i).Take(1000).ToList();
            total += await db.ExecuteScalarAsync<long>(new CommandDefinition(
                """
                SELECT
                  (SELECT COUNT(*) FROM sales_deliveries WHERE tenant_id = @T AND source_type = 'migration' AND is_deleted = 0 AND source_id IN @Ids)
                + (SELECT COUNT(*) FROM purchase_receipts WHERE tenant_id = @T AND source_type = 'migration' AND source_id IN @Ids)
                """, new { T = tenantId, Ids = chunk }, commandTimeout: CommandTimeoutSec, cancellationToken: ct)).ConfigureAwait(false);
        }
        return total;
    }

    /// <summary>② ③ ⑦ ⑧ 항목 + 알림.</summary>
    public static void AddPostingItems(List<ReconItem> items, List<string> warnings,
        PostingLegacy? legacy, ArchiveErp? erp, long? statementLines, long? leftover, string? err)
    {
        if (legacy is not null)
        {
            if (legacy.Unclassified) warnings.Add(WarnUnclassified);
            else if (legacy.NoLedgerTable) warnings.Add(WarnNoLedgerTable);
        }
        var cls = legacy is null ? null : legacy.Unclassified ? "머리표 없음 — 분류 못 함" : legacy.NoLedgerTable ? "거래처원장 표 없음 — 머리표로만 분류" : null;
        void Side(string io, string text, SideAgg? l, SideAgg? e)
        {
            items.Add(Item($"archive_{io}_count", "보관", $"장부 미반영 보관({text}) 건수", l?.Groups, e?.Groups,
                Join("레거시 = 머리표도 거래처원장 연결도 없는 묶음(날짜 없는 묶음 포함)", cls, err)));
            items.Add(Item($"archive_{io}_lines", "보관", $"장부 미반영 보관({text}) 줄", l?.Lines, e?.Lines, err));
            items.Add(Item($"archive_{io}_supply", "보관", $"장부 미반영 보관({text}) 공급가액", l?.Supply, e?.Supply, err));
            items.Add(Item($"archive_{io}_vat", "보관", $"장부 미반영 보관({text}) 부가세", l?.Vat, e?.Vat, err));
        }
        Side("sales", "판매", legacy?.ArchiveSales, erp?.Sales);
        Side("purchase", "매입", legacy?.ArchivePurchase, erp?.Purchase);

        decimal? erpLines = erp is null || statementLines is null ? null : statementLines.Value + erp.Sales.Lines + erp.Purchase.Lines;
        items.Add(Item("docfb_lines", "보관", "명세서 줄 = 장부 반영 줄 + 보관 줄", legacy?.InputLines - legacy?.UnknownIoLines, erpLines,
            Join(legacy is null ? null : $"레거시 전체 줄 {legacy.InputLines:N0} = 반영 {legacy.StatementLines:N0} + 보관 {legacy.ArchivedLines:N0}"
                    + (legacy.UnknownIoLines > 0 ? $" + 판매/매입 구분 없는 줄 {legacy.UnknownIoLines:N0}(가져오지 않음)" : string.Empty),
                 erp is null || statementLines is null ? null : $"히트판 명세서 줄 {statementLines:N0} + 보관 줄 {erp.Sales.Lines + erp.Purchase.Lines:N0}",
                 err)));

        items.Add(Item("excluded_leftover", "보관", "보관 대상인데 명세서로 남은 옛 자료", legacy is null ? null : 0m, leftover,
            Join("0 이어야 합니다 — 남아 있으면 매출·미수가 부풉니다", err)));
        if (leftover is > 0) warnings.Add(WarnLeftover + $" (남은 명세서 {leftover:N0}건)");
    }

    // ════════════════════════════════════════════════════════════════
    // ⑤ 재고원장 입·출 (LedgerMove) · 맞춤 줄
    // ════════════════════════════════════════════════════════════════

    /// <summary>레거시 재고원장 열.</summary>
    public sealed class LedgerLegacy
    {
        public long Rows { get; set; }
        public decimal QtyIn { get; set; }
        public decimal QtyOut { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>DOCFB(IJ_IO·IJ_QTY·IJ_AMT) 줄마다 <see cref="LegacyMdbMapping.LedgerMove"/> — 판매/매입 아닌 줄은 이관도 안 넣으므로 뺀다.</summary>
    public static LedgerLegacy ComputeLedgerLegacy(DataTable docfb)
    {
        ArgumentNullException.ThrowIfNull(docfb);
        var l = new LedgerLegacy();
        foreach (DataRow r in docfb.Rows)
        {
            var io = RowStr(r, "IJ_IO").Trim();
            if (io is not ("1" or "2")) continue;
            var (_, qin, qout) = LegacyMdbMapping.LedgerMove(io, RowDec(r, "IJ_QTY"));
            l.Rows++; l.QtyIn += qin; l.QtyOut += qout; l.Amount += RowDec(r, "IJ_AMT");
        }
        return l;
    }

    /// <summary>히트판 이관 원장 — 이력 줄(맞춤 줄 제외)과 맞춤 줄(<c>mb-adj-</c>)을 나눠 센다.</summary>
    public sealed class LedgerErp
    {
        public long HistRows { get; set; }
        public decimal HistIn { get; set; }
        public decimal HistOut { get; set; }
        public decimal HistAmount { get; set; }
        public long AdjRows { get; set; }
        public decimal AdjIn { get; set; }
        public decimal AdjOut { get; set; }
    }

    public static Task<LedgerErp> ReadLedgerErpAsync(IDbConnection db, string tenantId, CancellationToken ct)
        => db.QuerySingleAsync<LedgerErp>(new CommandDefinition(
            """
            SELECT
              COALESCE(SUM(CASE WHEN source_id NOT LIKE @Adj THEN 1 ELSE 0 END), 0) AS HistRows,
              COALESCE(SUM(CASE WHEN source_id NOT LIKE @Adj THEN qty_in ELSE 0 END), 0) AS HistIn,
              COALESCE(SUM(CASE WHEN source_id NOT LIKE @Adj THEN qty_out ELSE 0 END), 0) AS HistOut,
              COALESCE(SUM(CASE WHEN source_id NOT LIKE @Adj THEN COALESCE(supply_amount, 0) ELSE 0 END), 0) AS HistAmount,
              COALESCE(SUM(CASE WHEN source_id LIKE @Adj THEN 1 ELSE 0 END), 0) AS AdjRows,
              COALESCE(SUM(CASE WHEN source_id LIKE @Adj THEN qty_in ELSE 0 END), 0) AS AdjIn,
              COALESCE(SUM(CASE WHEN source_id LIKE @Adj THEN qty_out ELSE 0 END), 0) AS AdjOut
              FROM stock_ledger
             WHERE tenant_id = @T AND source_type = 'migration'
            """, new { T = tenantId, Adj = MdbLegacyFinalStock.AdjustmentSourceIdPrefix + "%" }, commandTimeout: CommandTimeoutSec, cancellationToken: ct));

    // ════════════════════════════════════════════════════════════════
    // ④ 재고 기말 = DOCFC 최종
    // ════════════════════════════════════════════════════════════════

    /// <summary>히트판 품목(재고 키) 합.</summary>
    public sealed class StockErpRow
    {
        public string? ItemName { get; set; }
        public string? Spec { get; set; }
        public decimal Qty { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>item_stock 품목별 Σ current_qty · Σ current_qty×avg_cost (<c>StockService.cs:486</c> 식) · 삭제 품목 제외.</summary>
    public static async Task<List<StockErpRow>> ReadStockErpAsync(IDbConnection db, string tenantId, CancellationToken ct)
        => (await db.QueryAsync<StockErpRow>(new CommandDefinition(
            """
            SELECT i.item_name AS ItemName, i.spec AS Spec, COALESCE(SUM(s.current_qty), 0) AS Qty, COALESCE(SUM(s.current_qty * s.avg_cost), 0) AS Amount
              FROM item_stock s
              JOIN items i ON i.item_id = s.item_id AND i.tenant_id = s.tenant_id
             WHERE s.tenant_id = @T AND i.is_deleted = 0
             GROUP BY i.item_id, i.item_name, i.spec
            """, new { T = tenantId }, commandTimeout: CommandTimeoutSec, cancellationToken: ct)).ConfigureAwait(false)).ToList();

    /// <summary>
    /// 최종재고 표 신선도 — DOCFC 마지막 달이 DOCFB 마지막 날짜의 달보다 앞이면 사유 글자. 같거나 뒤면 null.
    /// (갈래 I 가 남기는 사유 키가 아직 없어 두 표를 직접 비교한다 — 작업지시서 §11-2 C ④.)
    /// </summary>
    public static string? FinalStockStaleReason(string? docfcMaxYm, string? docfbLastDate)
    {
        if (string.IsNullOrEmpty(docfcMaxYm) || string.IsNullOrEmpty(docfbLastDate) || docfbLastDate.Length < 6) return null;
        var lastYm = docfbLastDate[..6];
        return string.CompareOrdinal(docfcMaxYm, lastYm) < 0
            ? $"최종재고 표가 최신이 아닙니다 — 최종재고 표 마지막 달 {Ym(docfcMaxYm)} · 명세서 마지막 달 {Ym(lastYm)} (이전 프로그램에서 월마감을 안 돌린 달의 거래가 재고에 빠질 수 있습니다)"
            : null;
        static string Ym(string s) => s.Length >= 6 ? $"{s[..4]}-{s.Substring(4, 2)}" : s;
    }

    /// <summary>
    /// 🆕 3판 R2 (설계 §24 ④⑤) — <paramref name="final"/> 이 <see cref="MdbLegacyFinalStock.RollForward"/> 결과일 때(<paramref name="rollInfo"/> ≠ null)
    /// 안내 1줄(<see cref="MdbLegacyFinalStock.RollForwardNotice"/> · S1) + 수량 없이 금액만 있던 줄 건수·금액을 알림과 재고 항목 설명에 붙인다.
    /// <paramref name="rollInfo"/> null 이면 종전과 같다.
    /// </summary>
    public static void AddStockItems(List<ReconItem> items, List<ReconDiffRow> itemDiffs, List<string> warnings,
        MdbLegacyFinalStock.FinalStockResult? final, bool docfcMissing, List<StockErpRow>? erpRows,
        LedgerLegacy? ledgerLegacy, LedgerErp? ledgerErp, string? docfbLastDate, string? err,
        MdbLegacyFinalStock.RollForwardInfo? rollInfo, int topN = 20)
    {
        var start = items.Count;
        AddStockItems(items, itemDiffs, warnings, final, docfcMissing, erpRows, ledgerLegacy, ledgerErp, docfbLastDate, err, topN);
        if (rollInfo is null) return;

        var ko = CultureInfo.GetCultureInfo("ko-KR");
        var notice = string.Format(ko, MdbLegacyFinalStock.RollForwardNotice, rollInfo.CarryMonths);
        var zero = RollForwardZeroLine(rollInfo);
        warnings.Add(notice);
        if (zero is not null) warnings.Add(zero);
        for (var i = start; i < items.Count; i++)
        {
            if (items[i].Key is "stock_qty" or "stock_amount")
                items[i].Detail = Join($"레거시 = 최종재고 표 {rollInfo.FromYm} 뒤 {rollInfo.ToYm} 까지 이어 계산", notice, zero, items[i].Detail);
        }
    }

    /// <summary>수량 0 금액 줄 문구 — 0건이면 null.</summary>
    public static string? RollForwardZeroLine(MdbLegacyFinalStock.RollForwardInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return info.ZeroQtyAmountLines == 0
            ? null
            : string.Format(CultureInfo.GetCultureInfo("ko-KR"), "수량 없이 금액만 있던 줄 {0:N0}건 · 금액 {1:N0}원은 재고 계산에서 뺐습니다", info.ZeroQtyAmountLines, info.ZeroQtyAmount);
    }

    /// <summary>④ 재고 항목 + 품목 차이 상위 N + 알림. 수량·금액은 ROUND(…, 2).</summary>
    public static void AddStockItems(List<ReconItem> items, List<ReconDiffRow> itemDiffs, List<string> warnings,
        MdbLegacyFinalStock.FinalStockResult? final, bool docfcMissing, List<StockErpRow>? erpRows,
        LedgerLegacy? ledgerLegacy, LedgerErp? ledgerErp, string? docfbLastDate, string? err, int topN = 20)
    {
        if (docfcMissing || (final is null && err is null)) warnings.Add(WarnNoFinalStock);
        var stale = final is null ? null : FinalStockStaleReason(final.MaxYm, docfbLastDate);
        if (stale is not null) warnings.Add(stale);

        decimal? legacyQty = final is null ? null : Math.Round(final.Lines.Sum(l => l.Qty), 2, MidpointRounding.AwayFromZero);
        decimal? legacyAmt = final is null ? null : Math.Round(final.Lines.Sum(l => l.Amount), 2, MidpointRounding.AwayFromZero);
        decimal? erpQty = erpRows is null ? null : Math.Round(erpRows.Sum(r => r.Qty), 2, MidpointRounding.AwayFromZero);
        decimal? erpAmt = erpRows is null ? null : Math.Round(erpRows.Sum(r => r.Amount), 2, MidpointRounding.AwayFromZero);

        long? mismatch = null;
        if (final is not null && erpRows is not null)
        {
            var erpByKey = new Dictionary<string, (string Name, decimal Qty)>(StringComparer.Ordinal);
            foreach (var r in erpRows)
            {
                var key = MdbLegacyFinalStock.ItemNameKey(r.ItemName, r.Spec);
                var name = string.IsNullOrWhiteSpace(r.Spec) ? (r.ItemName ?? string.Empty).Trim() : $"{(r.ItemName ?? string.Empty).Trim()} {r.Spec.Trim()}";
                erpByKey[key] = erpByKey.TryGetValue(key, out var cur) ? (cur.Name, cur.Qty + r.Qty) : (name, r.Qty);
            }
            var legacyByKey = final.Lines.ToDictionary(l => l.Key, l => l, StringComparer.Ordinal);
            var diffs = new List<ReconDiffRow>();
            foreach (var key in legacyByKey.Keys.Union(erpByKey.Keys, StringComparer.Ordinal))
            {
                var lq = legacyByKey.TryGetValue(key, out var lv) ? Math.Round(lv.Qty, 2, MidpointRounding.AwayFromZero) : 0m;
                var eq = erpByKey.TryGetValue(key, out var ev) ? Math.Round(ev.Qty, 2, MidpointRounding.AwayFromZero) : 0m;
                if (lq == eq) continue;
                var name = lv is not null && lv.Variants.Count > 0
                    ? (string.IsNullOrEmpty(lv.Variants[0].Ku) ? lv.Variants[0].Pum : $"{lv.Variants[0].Pum} {lv.Variants[0].Ku}")
                    : ev.Name;
                diffs.Add(new ReconDiffRow { Name = name, Legacy = lq, Erp = eq, Diff = eq - lq });
            }
            mismatch = diffs.Count;
            itemDiffs.AddRange(diffs.OrderByDescending(d => Math.Abs(d.Diff)).ThenBy(d => d.Name, StringComparer.Ordinal).Take(topN));
        }

        var finalDetail = final is null ? null
            : $"레거시 = 최종재고 표(품목별 마지막 달 · 창고 합산) 마지막 달 {final.MaxYm} · 품목 {final.Lines.Count:N0}"
              + (final.NonDefaultWarehouseLines > 0 ? $" · 기본창고 외 {final.NonDefaultWarehouseLines:N0}줄 합침" : string.Empty);
        items.Add(Item("stock_qty", "재고", "재고(기말수량)", legacyQty, erpQty, Join(finalDetail, stale, err)));
        items.Add(Item("stock_amount", "재고", "재고(기말금액)", legacyAmt, erpAmt, Join("히트판 = 수량 × 평균단가", err)));
        items.Add(Item("stock_item_mismatch", "재고", "재고 품목별 수량 불일치 품목 수", final is null ? null : 0m, mismatch,
            Join("합만 맞고 품목이 틀린 것을 잡습니다", err)));

        // ⑤ 원장 이력 입·출 · 맞춤 줄
        items.Add(Item("ledger_rows", "원장", "재고원장 이력 행수", ledgerLegacy?.Rows, ledgerErp?.HistRows,
            Join("맞춤 줄 제외", err)));
        items.Add(Item("ledger_qty_in", "원장", "재고원장 이력 입고", ledgerLegacy?.QtyIn, ledgerErp?.HistIn,
            Join("음수 수량은 반대 칸 절대값(이관과 같은 규칙)", err)));
        items.Add(Item("ledger_qty_out", "원장", "재고원장 이력 출고", ledgerLegacy?.QtyOut, ledgerErp?.HistOut, err));
        items.Add(Item("ledger_amount", "원장", "재고원장 이력 공급가액", ledgerLegacy?.Amount, ledgerErp?.HistAmount, err));

        decimal? legacyAdj = final is null || ledgerLegacy is null || stale is not null
            ? null
            : Math.Round(final.Lines.Sum(l => l.Qty), 2, MidpointRounding.AwayFromZero) - (ledgerLegacy.QtyIn - ledgerLegacy.QtyOut);
        items.Add(Item("stock_adjust_net", "원장", "최종재고 맞춤 줄 순수량", legacyAdj, ledgerErp is null ? null : ledgerErp.AdjIn - ledgerErp.AdjOut,
            Join("레거시 = 최종재고 수량 − 이력 순수량",
                 ledgerErp is null ? null : $"히트판 맞춤 줄 {ledgerErp.AdjRows:N0}행 · 입고 {ledgerErp.AdjIn:N2} · 출고 {ledgerErp.AdjOut:N2}",
                 stale is null ? null : "최종재고 표가 최신이 아니라 맞춤을 하지 않습니다",
                 err)));
    }

    // ════════════════════════════════════════════════════════════════
    // ⑥ 미수·미지급 = F3 ↔ 갈래 E 식
    // ════════════════════════════════════════════════════════════════

    /// <summary>레거시 F3 합계 + 거래처 코드별 잔액. DOCF5 null/0행 → null(거래처원장 표 없음).</summary>
    public sealed record BalanceLegacy(decimal Receivable, int ReceivableCount, decimal Payable, int PayableCount, IReadOnlyDictionary<int, decimal> ByCode);

    public static BalanceLegacy? ComputeBalanceLegacy(DataTable? docf5)
    {
        if (docf5 is null || docf5.Rows.Count == 0) return null;
        var byCode = LegacyMdbMapping.PartnerLegacyBalances(LegacyMdbMapping.ReadPartnerLedgerRows(docf5));
        var (rec, rc, pay, pc) = LegacyMdbMapping.SummarizeBalances(byCode);
        return new BalanceLegacy(rec, rc, pay, pc, byCode);
    }

    public sealed class BalanceErp
    {
        public decimal Receivable { get; set; }
        public decimal Payable { get; set; }
        public long LegacyReceivableCount { get; set; }
        public long LegacyPayableCount { get; set; }

        // 🆕 3판 R2 (설계 §24 ⑥ 3행) — null = 안 읽음(종전 호출자 호환: 3행을 만들지 않는다).
        /// <summary>Σ L0 미수(이관 이월잔액 + 쪽).</summary>
        public decimal? LegacyReceivable { get; set; }
        /// <summary>Σ M 미수(이관 뒤 이월 쪽에 맞춘 수금).</summary>
        public decimal? MatchedReceivable { get; set; }
        /// <summary>Σ R 미수(남은 금액 · 클램프 없음).</summary>
        public decimal? RemainingReceivable { get; set; }
        /// <summary>R&lt;0 미수 거래처 수.</summary>
        public long NegativeReceivableCount { get; set; }
        public decimal? LegacyPayable { get; set; }
        public decimal? MatchedPayable { get; set; }
        public decimal? RemainingPayable { get; set; }
        public long NegativePayableCount { get; set; }
    }

    /// <summary>
    /// 히트판 미수·미지급 — 🔴 3판 R2 (설계 §20 #10): <c>FinanceService.GetDashboardAsync</c> 와 <b>같은 문자열</b>
    /// <see cref="FinanceService.ReceivableBalanceSql"/>·<see cref="FinanceService.PayableBalanceSql"/>(복사 식 제거) + ⑥ 3행용 ΣL0·ΣM·ΣR·R&lt;0 곳 수.
    /// </summary>
    public static Task<BalanceErp> ReadBalanceErpAsync(IDbConnection db, string tenantId, CancellationToken ct)
        => db.QuerySingleAsync<BalanceErp>(new CommandDefinition(
            $$"""
            SELECT
              {{FinanceService.ReceivableBalanceSql}} AS Receivable,
              {{FinanceService.PayableBalanceSql}} AS Payable,
              (SELECT COUNT(*) FROM partner_legacy_balances WHERE tenant_id=@TenantId AND balance_amount > 0) AS LegacyReceivableCount,
              (SELECT COUNT(*) FROM partner_legacy_balances WHERE tenant_id=@TenantId AND balance_amount < 0) AS LegacyPayableCount,
              (SELECT COALESCE(SUM(r.legacy_amount), 0) FROM ({{LegacyBalanceMatching.ReceivableRemainingSql}}) r) AS LegacyReceivable,
              (SELECT COALESCE(SUM(r.matched_amount), 0) FROM ({{LegacyBalanceMatching.ReceivableRemainingSql}}) r) AS MatchedReceivable,
              (SELECT COALESCE(SUM(r.remaining_amount), 0) FROM ({{LegacyBalanceMatching.ReceivableRemainingSql}}) r) AS RemainingReceivable,
              (SELECT COUNT(*) FROM ({{LegacyBalanceMatching.ReceivableRemainingSql}}) r WHERE r.remaining_amount < 0) AS NegativeReceivableCount,
              (SELECT COALESCE(SUM(r.legacy_amount), 0) FROM ({{LegacyBalanceMatching.PayableRemainingSql}}) r) AS LegacyPayable,
              (SELECT COALESCE(SUM(r.matched_amount), 0) FROM ({{LegacyBalanceMatching.PayableRemainingSql}}) r) AS MatchedPayable,
              (SELECT COALESCE(SUM(r.remaining_amount), 0) FROM ({{LegacyBalanceMatching.PayableRemainingSql}}) r) AS RemainingPayable,
              (SELECT COUNT(*) FROM ({{LegacyBalanceMatching.PayableRemainingSql}}) r WHERE r.remaining_amount < 0) AS NegativePayableCount
            """, new { TenantId = tenantId }, commandTimeout: CommandTimeoutSec, cancellationToken: ct));

    /// <summary>
    /// 🔴 3판 R2 (설계 §20 #9) — 대사표 거래처별 히트판 잔액. #1·#2 를 거래처별로(같은 공용 조건) · 히트판 열 = 식 값 <b>+ M(P)</b>
    /// (이관 뒤 이월 매칭만으로 레거시 F3 와 차이가 생기지 않게). 열: PartnerId·PartnerName·PartnerCode·MigratedSourceHash·Receivable·Payable·ReceivableMatched·PayableMatched.
    /// 교차: Σ(Receivable − ReceivableMatched) = <see cref="FinanceService.ReceivableBalanceSql"/> (거래처 행이 있는 몫).
    /// </summary>
    public static readonly string PartnerBalanceErpSql = $$"""
        SELECT p.partner_id AS PartnerId, p.partner_name AS PartnerName, p.partner_code AS PartnerCode,
               p.migrated_source_hash AS MigratedSourceHash,
               COALESCE(sd.amt, 0) - COALESCE(c.amt, 0) + COALESCE(rr.rem, 0) + COALESCE(rr.m, 0) AS Receivable,
               COALESCE(pr.amt, 0) - COALESCE(rt.amt, 0) - COALESCE(pay.amt, 0) + COALESCE(rp.rem, 0) + COALESCE(rp.m, 0) AS Payable,
               COALESCE(rr.m, 0) AS ReceivableMatched,
               COALESCE(rp.m, 0) AS PayableMatched
          FROM partners p
          LEFT JOIN (SELECT partner_id, SUM(total_amount + vat_amount) AS amt FROM sales_deliveries
                      WHERE tenant_id=@TenantId AND status IN ('confirmed','invoiced') AND is_deleted=0
                        AND NOT (COALESCE(source_type,'') = 'migration' AND {{MdbLegacyPartnerBalance.HasLegacyBalanceSql}}) GROUP BY partner_id) sd ON sd.partner_id = p.partner_id
          LEFT JOIN (SELECT c9.partner_id, SUM(c9.amount) AS amt FROM collections c9
                      WHERE c9.tenant_id=@TenantId AND {{LegacyBalanceMatching.CollectionDocSideWhere("c9")}} GROUP BY c9.partner_id) c ON c.partner_id = p.partner_id
          LEFT JOIN (SELECT partner_id, SUM(total_amount + vat_amount) AS amt FROM purchase_receipts
                      WHERE tenant_id=@TenantId AND status='confirmed'
                        AND NOT (COALESCE(source_type,'') = 'migration' AND {{MdbLegacyPartnerBalance.HasLegacyBalanceSql}}) GROUP BY partner_id) pr ON pr.partner_id = p.partner_id
          LEFT JOIN (SELECT rt9.partner_id, COALESCE(SUM(rti9.supply_amount + rti9.vat_amount),0) AS amt FROM purchase_returns rt9
                      LEFT JOIN purchase_return_items rti9 ON rti9.return_id=rt9.return_id AND rti9.tenant_id=rt9.tenant_id
                      WHERE rt9.tenant_id=@TenantId AND {{LegacyBalanceMatching.PurchaseReturnDocSideWhere("rt9")}} GROUP BY rt9.partner_id) rt ON rt.partner_id = p.partner_id
          LEFT JOIN (SELECT p9.partner_id, SUM(p9.amount) AS amt FROM payments p9
                      WHERE p9.tenant_id=@TenantId AND {{LegacyBalanceMatching.PaymentDocSideWhere("p9")}} GROUP BY p9.partner_id) pay ON pay.partner_id = p.partner_id
          LEFT JOIN (SELECT x.partner_id, SUM(x.remaining_amount) AS rem, SUM(x.matched_amount) AS m
                       FROM ({{LegacyBalanceMatching.ReceivableRemainingSql}}) x GROUP BY x.partner_id) rr ON rr.partner_id = p.partner_id
          LEFT JOIN (SELECT x.partner_id, SUM(x.remaining_amount) AS rem, SUM(x.matched_amount) AS m
                       FROM ({{LegacyBalanceMatching.PayableRemainingSql}}) x GROUP BY x.partner_id) rp ON rp.partner_id = p.partner_id
         WHERE p.tenant_id = @TenantId
        """;

    /// <summary>⑥ 미수·미지급 항목.</summary>
    public static void AddBalanceItems(List<ReconItem> items, List<string> warnings, BalanceLegacy? legacy, bool docf5Missing, BalanceErp? erp, string? err)
    {
        if (docf5Missing && !warnings.Contains(WarnNoLedgerTable)) warnings.Add(WarnNoLedgerTable);
        var legacyRule = "레거시 = 거래처별 마지막 이월 잔액 + 그 뒤 거래 (+ 미수 · − 미지급)";
        items.Add(Item("receivable", "미수", "미수금", legacy?.Receivable, erp?.Receivable,
            Join(legacyRule,
                 legacy is null ? null : $"레거시 {legacy.ReceivableCount:N0}곳",
                 erp is null ? null : $"히트판 이월잔액 {erp.LegacyReceivableCount:N0}곳 (옛 거래처 여러 코드가 한 거래처로 합쳐지면 곳 수가 줄 수 있습니다)",
                 docf5Missing ? "거래처원장 표 없음" : null, err)));
        items.Add(Item("payable", "미지급", "미지급금", legacy?.Payable, erp?.Payable,
            Join(legacyRule,
                 legacy is null ? null : $"레거시 {legacy.PayableCount:N0}곳",
                 erp is null ? null : $"히트판 이월잔액 {erp.LegacyPayableCount:N0}곳",
                 docf5Missing ? "거래처원장 표 없음" : null, err)));

        AddLegacyBalanceRows(items, warnings, legacy, erp, err);
    }

    /// <summary>R&lt;0 거래처 경고 문구(미수/미지급 · 곳 수).</summary>
    public const string WarnNegativeRemaining = "이전 프로그램 이월잔액보다 더 많이 맞춘 거래처가 {0} {1:N0}곳 있습니다(남은 금액이 0보다 작음). 수금·지급 내역을 확인해 주세요.";

    /// <summary>
    /// 🆕 3판 R2 (설계 §24) — ⑥ 미수·미지급 각 3행: 「레거시 이월」(Σ L0 ↔ F3 · OK/DIFF) · 「그 뒤 수금·지급」(Σ M · 정보 = 레거시 칸 비움 → NA)
    /// · 「남은 금액」(Σ R · 정보 · R&lt;0 거래처 수 경고). ERP 쪽 값이 안 읽혔으면(null) 3행을 만들지 않는다.
    /// </summary>
    public static void AddLegacyBalanceRows(List<ReconItem> items, List<string> warnings, BalanceLegacy? legacy, BalanceErp? erp, string? err)
    {
        if (erp?.LegacyReceivable is null && erp?.LegacyPayable is null) return;
        const string info = "정보 — 비교할 레거시 값이 없습니다";

        items.Add(Item("receivable_legacy", "미수", "미수금 · 레거시 이월", legacy?.Receivable, erp.LegacyReceivable,
            Join("히트판 = 이관한 이전 프로그램 이월잔액(받을 돈) 합", err)));
        items.Add(Item("receivable_matched", "미수", "미수금 · 그 뒤 수금", null, erp.MatchedReceivable,
            Join(info, "이관 뒤 이월잔액에 맞춘 수금 합", err)));
        items.Add(Item("receivable_remaining", "미수", "미수금 · 남은 금액", null, erp.RemainingReceivable,
            Join(info, erp.NegativeReceivableCount > 0 ? $"남은 금액이 0보다 작은 거래처 {erp.NegativeReceivableCount:N0}곳" : null, err)));
        if (erp.NegativeReceivableCount > 0)
            warnings.Add(string.Format(CultureInfo.GetCultureInfo("ko-KR"), WarnNegativeRemaining, "미수금", erp.NegativeReceivableCount));

        items.Add(Item("payable_legacy", "미지급", "미지급금 · 레거시 이월", legacy?.Payable, erp.LegacyPayable,
            Join("히트판 = 이관한 이전 프로그램 이월잔액(줄 돈) 합", err)));
        items.Add(Item("payable_matched", "미지급", "미지급금 · 그 뒤 지급", null, erp.MatchedPayable,
            Join(info, "이관 뒤 이월잔액에 맞춘 지급 · 이관 매입 반품 합", err)));
        items.Add(Item("payable_remaining", "미지급", "미지급금 · 남은 금액", null, erp.RemainingPayable,
            Join(info, erp.NegativePayableCount > 0 ? $"남은 금액이 0보다 작은 거래처 {erp.NegativePayableCount:N0}곳" : null, err)));
        if (erp.NegativePayableCount > 0)
            warnings.Add(string.Format(CultureInfo.GetCultureInfo("ko-KR"), WarnNegativeRemaining, "미지급금", erp.NegativePayableCount));
    }

    /// <summary>
    /// 🆕 3판 R2 (설계 §24 F2) — 마스터 건수 차이가 <b>설명 수와 정확히 같으면</b> Status <c>EXPLAINED</c>(「사유 확인됨」 · Detail 에 건수·사유).
    /// 다르면 DIFF 그대로 + Detail 「설명된 n · 설명 안 되는 m」. 숨기지 않는다. 설명 수 0 이면 손대지 않는다(OK/DIFF/NA 종전).
    /// </summary>
    public static ReconItem ApplyExplained(ReconItem item, long explained, string reason)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (explained == 0 || item.Legacy is null || item.Erp is null) return item;
        var diff = item.Erp.Value - item.Legacy.Value;
        if (diff == explained)
        {
            item.Status = StatusExplained;
            item.Detail = Join($"사유 확인됨 · {reason} {explained:N0}", item.Detail);
        }
        else
        {
            // 차이 0 인데 설명 수가 있어도 DIFF — 무언가가 상쇄된 것이므로 숨기지 않는다.
            item.Status = StatusDiff;
            item.Detail = Join($"설명된 {explained:N0}({reason}) · 설명 안 되는 {diff - explained:N0}", item.Detail);
        }
        return item;
    }

    // 🆕 3판 R2 — 대사 서비스(이어 계산 입력 읽기)가 같은 안전 읽기를 쓰도록 노출.
    internal static string CellStr(DataRow r, string c) => RowStr(r, c);
    internal static decimal CellDec(DataRow r, string c) => RowDec(r, c);

    // ── DataRow 안전 읽기 ──
    private static string RowStr(DataRow r, string c)
    {
        if (!r.Table.Columns.Contains(c)) return string.Empty;
        var v = r[c];
        return v == DBNull.Value ? string.Empty : Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static decimal RowDec(DataRow r, string c)
    {
        if (!r.Table.Columns.Contains(c)) return 0m;
        var v = r[c];
        if (v == DBNull.Value) return 0m;
        return v is string s
            ? (decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m)
            : Convert.ToDecimal(v, CultureInfo.InvariantCulture);
    }
}
