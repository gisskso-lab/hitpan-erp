using System.Data;
using Dapper;

namespace HitPan.Application.Services;

/// <summary>
/// 매출·매입 확정 시 회계 전표(journal_entries + journal_lines)를 자동 기표하는 헬퍼.
///
/// 설계 메모:
/// - 현재 DDL에는 chart_of_accounts 마스터가 없으므로 표준 계정 식별자(상수)를 account_id에 직접 삽입한다.
/// - 추후 chart_of_accounts 도입 시 이 상수들을 UUID로 매핑하는 마이그레이션만 수행하면 된다.
/// - 모든 기표는 status='confirmed'로 즉시 반영. 원장(journal_lines)은 INSERT ONLY 원칙 유지.
/// - 차변 합계 = 대변 합계 균형 강제.
/// - VAT가 0인 거래는 부가세 라인 스킵.
/// </summary>
internal static class AutoJournalHelper
{
    // 표준 계정 식별자 (chart_of_accounts 도입 전까지 account_id 자리에 직접 사용)
    // accounts 테이블의 실 account_code는 표준 5자리 한국 계정과목 코드.
    // 과거 "acc-sales-revenue" 같은 긴 심볼릭 문자열은 VARCHAR(10) 컬럼 초과로 INSERT 실패했다.
    public const string SalesRevenue = "40100";         // 상품매출 (대변)
    public const string VatPayable = "25500";           // 부가세예수금 (대변)
    public const string AccountsReceivable = "10800";   // 외상매출금 (차변)

    public const string PurchaseCost = "50100";         // 상품매입 (차변)
    public const string VatReceivable = "17600";        // 부가세대급금 (차변)
    public const string AccountsPayable = "23200";      // 외상매입금 (대변)

    // 🔴 20260827작4 (사장님 오더) — 수금·지급·경비·급여 기표용 계정.
    //   DB-111_chart_of_accounts_expand.sql 로 심는다. 없으면 FK 1452(fk_jl_account).
    //
    //   ⚠️ 현금은 사장님 지시로 **수기 입력**이다("현금은 수기로").
    //     시스템이 시재를 자동으로 굴리지 않는다. 아래 Cash 는 복식부기 차·대 짝을
    //     맞추기 위한 **상대계정 그릇**일 뿐이다.
    public const string Cash = "10100";                 // 현금
    public const string BankDeposit = "10300";          // 보통예금
    public const string AccountsPayableOther = "25300"; // 미지급금
    public const string WithholdingPayable = "25400";   // 예수금 (급여 원천징수)
    public const string SalaryExpense = "80100";        // 급여 (비용)
    public const string MiscExpense = "84100";          // 잡비 — 경비 계정 미지정 시 기본값

    // 경비 분류별 계정 (FinanceService.ResolveExpenseAccount 가 화면 분류를 여기에 매핑한다)
    public const string WelfareExpense = "81100";       // 복리후생비 (식대 등)
    public const string TravelExpense = "81200";        // 여비교통비
    public const string EntertainmentExpense = "81300"; // 접대비
    public const string CommunicationExpense = "81400"; // 통신비
    public const string SuppliesExpense = "82500";      // 소모품비

    /// <summary>
    /// 매출(거래명세서) 확정 기표.
    /// 차변: 외상매출금 (supply + vat)
    /// 대변: 매출 (supply) + 부가세예수금 (vat)
    /// </summary>
    public static async Task RecordSalesConfirmAsync(
        IDbConnection conn,
        IDbTransaction tx,
        string tenantId,
        string sourceId,
        string documentNo,
        DateTime entryDate,
        string partnerId,
        decimal supplyAmount,
        decimal vatAmount,
        string? employeeId,
        CancellationToken ct)
    {
        // WS-D-2 (2026-05-18) 마이그 분개 중복 방지 가드.
        // 마이그된 거래(source_type='migration')는 DOCF7 분개로 이미 기표됨.
        // 사용자가 재확정 누르면 운영 자동 분개가 중복 INSERT — 차단.
        // 헌법 #3 INSERT ONLY + 사장님 격언 "끝 숫자" 정합.
        if (await IsAlreadyJournaledFromMigrationAsync(conn, tx, tenantId, sourceId, ct).ConfigureAwait(false))
        {
            return;
        }

        var entryId = Guid.NewGuid().ToString();
        var entryNo = $"JE-{entryDate:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var total = supplyAmount + vatAmount;

        await InsertEntryAsync(conn, tx, entryId, tenantId, entryNo, entryDate,
            "sales", sourceId, employeeId, $"매출 자동기표: {documentNo}", ct);

        // 차변 외상매출금 — total=0이면 스킵 (CHECK chk_jl_debit_or_credit: 0/0 라인 금지).
        if (total != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, AccountsReceivable, "debit",
                total, partnerId, $"매출채권 {documentNo}", ct);
        }

        // 대변 매출
        if (supplyAmount != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, SalesRevenue, "credit",
                supplyAmount, partnerId, $"매출 {documentNo}", ct);
        }

        // 대변 부가세예수금 (VAT 0이면 스킵)
        if (vatAmount != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, VatPayable, "credit",
                vatAmount, partnerId, $"부가세예수금 {documentNo}", ct);
        }
    }

    /// <summary>
    /// 매출(세금계산서) 취소 역분개.
    /// 원분개 반전: 차변 매출+부가세예수금 / 대변 외상매출금
    /// </summary>
    public static async Task RecordSalesCancelAsync(
        IDbConnection conn,
        IDbTransaction tx,
        string tenantId,
        string sourceId,
        string documentNo,
        DateTime entryDate,
        string? partnerId,
        decimal supplyAmount,
        decimal vatAmount,
        string? employeeId,
        CancellationToken ct)
    {
        var entryId = Guid.NewGuid().ToString();
        var entryNo = $"JE-{entryDate:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var total = supplyAmount + vatAmount;

        await InsertEntryAsync(conn, tx, entryId, tenantId, entryNo, entryDate,
            "sales_cancel", sourceId, employeeId, $"매출취소 역분개: {documentNo}", ct);

        // 차변 매출 역산
        if (supplyAmount != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, SalesRevenue, "debit",
                supplyAmount, partnerId, $"매출취소 {documentNo}", ct);
        }

        // 차변 부가세예수금 역산
        if (vatAmount != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, VatPayable, "debit",
                vatAmount, partnerId, $"부가세예수금취소 {documentNo}", ct);
        }

        // 대변 외상매출금 역산
        if (total != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, AccountsReceivable, "credit",
                total, partnerId, $"매출채권취소 {documentNo}", ct);
        }
    }

    /// <summary>
    /// 매입반품 확정 역분개.
    /// 원분개(매입확정) 반전: 차변 외상매입금(total) / 대변 매입(supply) + 부가세대급금(vat)
    /// </summary>
    public static async Task RecordPurchaseReturnAsync(
        IDbConnection conn,
        IDbTransaction tx,
        string tenantId,
        string sourceId,
        string documentNo,
        DateTime entryDate,
        string? partnerId,
        decimal supplyAmount,
        decimal vatAmount,
        string? employeeId,
        CancellationToken ct)
    {
        var entryId = Guid.NewGuid().ToString();
        var entryNo = $"JE-{entryDate:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var total = supplyAmount + vatAmount;

        await InsertEntryAsync(conn, tx, entryId, tenantId, entryNo, entryDate,
            "purchase_return", sourceId, employeeId, $"매입반품 역분개: {documentNo}", ct);

        // 차변 외상매입금 역산
        if (total != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, AccountsPayable, "debit",
                total, partnerId, $"매입채무취소 {documentNo}", ct);
        }

        // 대변 매입 역산
        if (supplyAmount != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, PurchaseCost, "credit",
                supplyAmount, partnerId, $"매입반품 {documentNo}", ct);
        }

        // 대변 부가세대급금 역산
        if (vatAmount != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, VatReceivable, "credit",
                vatAmount, partnerId, $"부가세대급금취소 {documentNo}", ct);
        }
    }

    /// <summary>
    /// 매출취소(거래명세서) 역분개.
    /// 원분개(매출확정) 반전: 차변 매출(supply) + 부가세예수금(vat) / 대변 외상매출금(total)
    /// </summary>
    public static async Task RecordSalesDeliveryCancelAsync(
        IDbConnection conn,
        IDbTransaction tx,
        string tenantId,
        string sourceId,
        string documentNo,
        DateTime entryDate,
        string? partnerId,
        decimal supplyAmount,
        decimal vatAmount,
        string? employeeId,
        CancellationToken ct)
    {
        var entryId = Guid.NewGuid().ToString();
        var entryNo = $"JE-{entryDate:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var total = supplyAmount + vatAmount;

        await InsertEntryAsync(conn, tx, entryId, tenantId, entryNo, entryDate,
            "sales_delivery_cancel", sourceId, employeeId, $"매출취소 역분개: {documentNo}", ct);

        // 차변 매출 역산
        if (supplyAmount != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, SalesRevenue, "debit",
                supplyAmount, partnerId, $"매출취소 {documentNo}", ct);
        }

        // 차변 부가세예수금 역산
        if (vatAmount != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, VatPayable, "debit",
                vatAmount, partnerId, $"부가세예수금취소 {documentNo}", ct);
        }

        // 대변 외상매출금 역산
        if (total != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, AccountsReceivable, "credit",
                total, partnerId, $"매출채권취소 {documentNo}", ct);
        }
    }

    /// <summary>
    /// 매입(매입명세서) 확정 기표.
    /// 차변: 매입 (supply) + 부가세대급금 (vat)
    /// 대변: 외상매입금 (supply + vat)
    /// </summary>
    public static async Task RecordPurchaseConfirmAsync(
        IDbConnection conn,
        IDbTransaction tx,
        string tenantId,
        string sourceId,
        string documentNo,
        DateTime entryDate,
        string partnerId,
        decimal supplyAmount,
        decimal vatAmount,
        string? employeeId,
        CancellationToken ct)
    {
        // WS-D-2 (2026-05-18) 마이그 분개 중복 방지 가드.
        if (await IsAlreadyJournaledFromMigrationAsync(conn, tx, tenantId, sourceId, ct).ConfigureAwait(false))
        {
            return;
        }

        var entryId = Guid.NewGuid().ToString();
        var entryNo = $"JE-{entryDate:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var total = supplyAmount + vatAmount;

        await InsertEntryAsync(conn, tx, entryId, tenantId, entryNo, entryDate,
            "purchase", sourceId, employeeId, $"매입 자동기표: {documentNo}", ct);

        // 차변 매입
        if (supplyAmount != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, PurchaseCost, "debit",
                supplyAmount, partnerId, $"매입 {documentNo}", ct);
        }

        // 차변 부가세대급금
        if (vatAmount != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, VatReceivable, "debit",
                vatAmount, partnerId, $"부가세대급금 {documentNo}", ct);
        }

        // 대변 외상매입금 — total=0이면 스킵 (CHECK chk_jl_debit_or_credit: 0/0 라인 금지).
        if (total != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, AccountsPayable, "credit",
                total, partnerId, $"매입채무 {documentNo}", ct);
        }
    }

    /// <summary>
    /// 매출반품 확정 기표 — 자기 이름표(source_type='sales_return') 로 기록한다. (2026-08-28 작12)
    /// 🔴 종전엔 RecordSalesDeliveryCancelAsync('sales_delivery_cancel') 를 빌려 썼다. 분개 금액은
    /// 맞았으나 장부에서 «반품»과 «명세서 취소»가 같은 키로 섞여 식별이 불가능했고, 그 결과
    /// FinanceService 기표누락 검사가 매출반품을 세지 못했다 (매입은 purchase_return 키로 세고 있었다).
    /// 분개 방향은 종전과 동일: 차변 매출(supply) + 부가세예수금(vat) / 대변 외상매출금(total).
    /// ⚠️ 매입반품(대변 매입채무)과 대칭이지만 계정이 다르다 — 복사하지 말 것.
    /// </summary>
    public static async Task RecordSalesReturnAsync(
        IDbConnection conn,
        IDbTransaction tx,
        string tenantId,
        string sourceId,
        string documentNo,
        DateTime entryDate,
        string? partnerId,
        decimal supplyAmount,
        decimal vatAmount,
        string? employeeId,
        CancellationToken ct)
    {
        var entryId = Guid.NewGuid().ToString();
        var entryNo = $"JE-{entryDate:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var total = supplyAmount + vatAmount;

        await InsertEntryAsync(conn, tx, entryId, tenantId, entryNo, entryDate,
            "sales_return", sourceId, employeeId, $"매출반품 역분개: {documentNo}", ct);

        // 차변 매출 역산
        if (supplyAmount != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, SalesRevenue, "debit",
                supplyAmount, partnerId, $"매출반품 {documentNo}", ct);
        }

        // 차변 부가세예수금 역산
        if (vatAmount != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, VatPayable, "debit",
                vatAmount, partnerId, $"부가세예수금반품 {documentNo}", ct);
        }

        // 대변 외상매출금 역산
        if (total != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, AccountsReceivable, "credit",
                total, partnerId, $"매출채권반품 {documentNo}", ct);
        }
    }

    /// <summary>
    /// 매출반품 취소(확정 되돌리기) 기표 — 봉합 (2026-06-23, 15차 적대검증 15-P1).
    /// 매출반품 확정은 RecordSalesDeliveryCancelAsync(역분개: 차변 매출+부가세예수금 / 대변 외상매출금)로
    /// 기표된다. 그 반품을 취소하면 역분개를 되돌려 원래 매출 상태로 복원해야 하므로, 정상 매출확정 분개
    /// (차변 외상매출금 / 대변 매출+부가세예수금)를 다시 기록한다.
    /// source_type='sales_return_cancel'(19자≤30) — 확정 분개('sales_delivery_cancel')와 다른 키라
    /// journal UNIQUE (tenant, source_type, source_id) 충돌 없음(12차 회귀 차단). 멱등은 호출측 status 가드.
    /// </summary>
    public static async Task RecordSalesReturnCancelAsync(
        IDbConnection conn,
        IDbTransaction tx,
        string tenantId,
        string sourceId,
        string documentNo,
        DateTime entryDate,
        string? partnerId,
        decimal supplyAmount,
        decimal vatAmount,
        string? employeeId,
        CancellationToken ct)
    {
        var entryId = Guid.NewGuid().ToString();
        var entryNo = $"JE-{entryDate:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var total = supplyAmount + vatAmount;

        await InsertEntryAsync(conn, tx, entryId, tenantId, entryNo, entryDate,
            "sales_return_cancel", sourceId, employeeId, $"매출반품취소 기표: {documentNo}", ct);

        // 차변 외상매출금 (매출채권 복원)
        if (total != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, AccountsReceivable, "debit",
                total, partnerId, $"매출채권복원 {documentNo}", ct);
        }

        // 대변 매출 (매출 복원)
        if (supplyAmount != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, SalesRevenue, "credit",
                supplyAmount, partnerId, $"매출복원 {documentNo}", ct);
        }

        // 대변 부가세예수금 복원
        if (vatAmount != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, VatPayable, "credit",
                vatAmount, partnerId, $"부가세예수금복원 {documentNo}", ct);
        }
    }

    /// <summary>
    /// 매입반품 취소(확정 되돌리기) 기표 — 봉합 (2026-06-23, 15차 적대검증 15-P1).
    /// 매입반품 확정은 RecordPurchaseReturnAsync(역분개: 차변 외상매입금 / 대변 매입+부가세대급금)로 기표된다.
    /// 그 반품을 취소하면 정상 매입확정 분개(차변 매입+부가세대급금 / 대변 외상매입금)를 다시 기록해 복원한다.
    /// source_type='purchase_return_cancel'(22자≤30) — 확정 분개('purchase_return')와 다른 키.
    /// </summary>
    public static async Task RecordPurchaseReturnCancelAsync(
        IDbConnection conn,
        IDbTransaction tx,
        string tenantId,
        string sourceId,
        string documentNo,
        DateTime entryDate,
        string? partnerId,
        decimal supplyAmount,
        decimal vatAmount,
        string? employeeId,
        CancellationToken ct)
    {
        var entryId = Guid.NewGuid().ToString();
        var entryNo = $"JE-{entryDate:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var total = supplyAmount + vatAmount;

        await InsertEntryAsync(conn, tx, entryId, tenantId, entryNo, entryDate,
            "purchase_return_cancel", sourceId, employeeId, $"매입반품취소 기표: {documentNo}", ct);

        // 차변 매입 (매입 복원)
        if (supplyAmount != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, PurchaseCost, "debit",
                supplyAmount, partnerId, $"매입복원 {documentNo}", ct);
        }

        // 차변 부가세대급금 복원
        if (vatAmount != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, VatReceivable, "debit",
                vatAmount, partnerId, $"부가세대급금복원 {documentNo}", ct);
        }

        // 대변 외상매입금 (매입채무 복원)
        if (total != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, AccountsPayable, "credit",
                total, partnerId, $"매입채무복원 {documentNo}", ct);
        }
    }

    public const string RawMaterials = "14600";          // 원재료 (차변/대변)
    public const string WorkInProcess = "16900";          // 재공품 (차변/대변)

    /// <summary>
    /// BOM 생산 기표.
    /// 차변: 재공품(완성품 원가 전입)
    /// 대변: 원재료(자재 원가 출고)
    /// </summary>
    public static async Task RecordBomProductionAsync(
        IDbConnection conn,
        IDbTransaction tx,
        string tenantId,
        string sourceId,
        string documentNo,
        DateTime entryDate,
        decimal totalCost,
        string? employeeId,
        CancellationToken ct)
    {
        var entryId = Guid.NewGuid().ToString();
        var entryNo = $"JE-{entryDate:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";

        await InsertEntryAsync(conn, tx, entryId, tenantId, entryNo, entryDate,
            "bom_production", sourceId, employeeId, $"BOM생산 원가기표: {documentNo}", ct);

        if (totalCost != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, WorkInProcess, "debit",
                totalCost, null, $"재공품 전입 {documentNo}", ct);
            await InsertLineAsync(conn, tx, entryId, tenantId, RawMaterials, "credit",
                totalCost, null, $"원재료 출고 {documentNo}", ct);
        }
    }

    /// <summary>
    /// BOM 해체 역분개 — RecordBomProductionAsync 의 정확한 Reverse.
    /// 차변: 원재료(자재 원가 복귀)
    /// 대변: 재공품(완성품 원가 역산)
    /// </summary>
    public static async Task RecordBomDisassembleAsync(
        IDbConnection conn,
        IDbTransaction tx,
        string tenantId,
        string sourceId,
        string documentNo,
        DateTime entryDate,
        decimal totalCost,
        string? employeeId,
        CancellationToken ct)
    {
        var entryId = Guid.NewGuid().ToString();
        var entryNo = $"JE-{entryDate:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";

        await InsertEntryAsync(conn, tx, entryId, tenantId, entryNo, entryDate,
            "bom_disassemble", sourceId, employeeId, $"BOM해체 역분개: {documentNo}", ct);

        if (totalCost != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, RawMaterials, "debit",
                totalCost, null, $"원재료 복귀 {documentNo}", ct);
            await InsertLineAsync(conn, tx, entryId, tenantId, WorkInProcess, "credit",
                totalCost, null, $"재공품 역산 {documentNo}", ct);
        }
    }

    /// <summary>
    /// WS-D-2 (2026-05-18) 마이그 분개 중복 방지 가드.
    /// 사장님 결재 Q2: 마이그 분개(source_type='migration')는 DOCF7 이관 시 이미 기표됨.
    /// 사용자가 마이그된 거래에 대해 재확정 누를 때 운영 자동 분개 중복 INSERT 차단.
    ///
    /// 검사 기준: 동일 tenant_id에서 sourceId(=delivery_id/po_id 등)가
    /// source_type='migration' journal_entries에 이미 존재하면 true.
    /// </summary>
    private static async Task<bool> IsAlreadyJournaledFromMigrationAsync(
        IDbConnection conn, IDbTransaction tx,
        string tenantId, string sourceId, CancellationToken ct)
    {
        // sourceId가 마이그 entry의 sourceId 패턴(mig-docf7-*)일 가능성과,
        // 마이그된 거래명세서 ID(GUID)일 가능성 둘 다 검사.
        // 1) sales_deliveries / purchase_receipts에서 source_type='migration' 확인.
        var checkSql = """
            SELECT 1 FROM (
              SELECT delivery_id AS id, source_type FROM sales_deliveries
                WHERE tenant_id = @TenantId AND delivery_id = @SourceId
              UNION ALL
              SELECT receipt_id AS id, source_type FROM purchase_receipts
                WHERE tenant_id = @TenantId AND receipt_id = @SourceId
            ) t
            WHERE t.source_type = 'migration'
            LIMIT 1
            """;
        var exists = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            checkSql, new { TenantId = tenantId, SourceId = sourceId },
            transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        return exists.HasValue;
    }

    // ══════════════════════════════════════════════════════════════════════
    // 🔴 20260827작4 — 수금·지급·경비·급여 기표 (사장님 오더)
    //   "수금, 지급, 경비, 급여등 모든 돈의 흐름을 회계장부 하나로 모두 모여서 정합하도록"
    //
    //   종전엔 이 넷이 **분개를 한 줄도 안 만들었다.** 화면·저장은 되는데 회계로
    //   넘기는 문이 없었다(전수조사 §1-2). 그래서 시산표에 "수금 N건 미기표" 같은
    //   한시 안내문(FinanceService.AppendUnpostedNoticeAsync)이 붙어 있었다.
    //   이 넷이 붙으면 그 안내문은 건수 0 이 되어 저절로 사라진다.
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 결제수단 → 상대계정 매핑. 현금성은 현금, 그 외는 보통예금으로 본다.
    /// </summary>
    /// <remarks>
    /// 🔴 사장님 지시 "현금은 수기로" — 시스템이 시재를 자동으로 굴리지 않는다.
    ///   여기서 정하는 건 **분개의 상대계정 한 칸**일 뿐이고, 실제 현금 잔액 관리는
    ///   사람이 한다. 카드·어음·수표를 보통예금으로 묶는 것도 같은 이유다 —
    ///   미수금/받을어음까지 쪼개면 수기 관리가 오히려 복잡해진다.
    ///   더 세분이 필요하면 그때 사장님 결재를 받아 나눈다.
    /// </remarks>
    private static string ResolveCashAccount(string? method)
        => string.Equals(method, "cash", StringComparison.OrdinalIgnoreCase)
            ? Cash
            : BankDeposit;

    /// <summary>
    /// 수금 기표. 차변: 현금·보통예금 / 대변: 외상매출금.
    /// 받을 돈(외상매출금)이 줄고 현금이 늘어난다.
    /// </summary>
    public static async Task RecordCollectionAsync(
        IDbConnection conn,
        IDbTransaction tx,
        string tenantId,
        string sourceId,
        DateTime entryDate,
        string? partnerId,
        decimal amount,
        string? method,
        string? employeeId,
        CancellationToken ct)
    {
        if (amount == 0m) return;   // CHECK chk_jl_debit_or_credit: 0/0 라인 금지

        var entryId = Guid.NewGuid().ToString();
        var entryNo = $"JE-{entryDate:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";

        await InsertEntryAsync(conn, tx, entryId, tenantId, entryNo, entryDate,
            "collection", sourceId, employeeId, "수금 자동기표", ct);

        await InsertLineAsync(conn, tx, entryId, tenantId, ResolveCashAccount(method), "debit",
            amount, partnerId, "수금", ct);

        await InsertLineAsync(conn, tx, entryId, tenantId, AccountsReceivable, "credit",
            amount, partnerId, "외상매출금 회수", ct);
    }

    /// <summary>
    /// 지급 기표. 차변: 외상매입금 / 대변: 현금·보통예금.
    /// 갚을 돈(외상매입금)이 줄고 현금이 나간다. 수금의 정확한 반대다.
    /// </summary>
    public static async Task RecordPaymentAsync(
        IDbConnection conn,
        IDbTransaction tx,
        string tenantId,
        string sourceId,
        DateTime entryDate,
        string? partnerId,
        decimal amount,
        string? method,
        string? employeeId,
        CancellationToken ct)
    {
        if (amount == 0m) return;

        var entryId = Guid.NewGuid().ToString();
        var entryNo = $"JE-{entryDate:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";

        await InsertEntryAsync(conn, tx, entryId, tenantId, entryNo, entryDate,
            "payment", sourceId, employeeId, "지급 자동기표", ct);

        await InsertLineAsync(conn, tx, entryId, tenantId, AccountsPayable, "debit",
            amount, partnerId, "외상매입금 상환", ct);

        await InsertLineAsync(conn, tx, entryId, tenantId, ResolveCashAccount(method), "credit",
            amount, partnerId, "지급", ct);
    }

    /// <summary>
    /// 경비 기표. 차변: 경비계정 / 대변: 현금·보통예금·미지급금.
    /// </summary>
    /// <remarks>
    /// ⚠️ 계정과목을 지정하지 않은 경비는 **잡비(84100)** 로 떨어진다.
    ///   틀린 계정에 넣는 것보다 잡비로 모아두고 사람이 재분류하는 편이 낫다 —
    ///   추측으로 판단해 접대비를 복리후생비에 넣으면 세무상 문제가 된다.
    /// ⚠️ 카드 결제는 **미지급금** 대변이다. 카드는 그 자리에서 현금이 안 나가고
    ///   나중에 결제일에 빠진다 — 그 시차를 미지급금이 잡는다.
    /// </remarks>
    public static async Task RecordExpenseAsync(
        IDbConnection conn,
        IDbTransaction tx,
        string tenantId,
        string sourceId,
        DateTime entryDate,
        decimal amount,
        string? accountCode,
        string? method,
        string? employeeId,
        string? memo,
        CancellationToken ct)
    {
        if (amount == 0m) return;

        var entryId = Guid.NewGuid().ToString();
        var entryNo = $"JE-{entryDate:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var debitAccount = string.IsNullOrWhiteSpace(accountCode) ? MiscExpense : accountCode;

        // 카드는 즉시 현금이 안 나간다 → 미지급금. 현금/이체는 그 자리에서 빠진다.
        var creditAccount = string.Equals(method, "card", StringComparison.OrdinalIgnoreCase)
            ? AccountsPayableOther
            : ResolveCashAccount(method);

        var desc = string.IsNullOrWhiteSpace(memo) ? "경비 자동기표" : $"경비 자동기표: {memo}";

        await InsertEntryAsync(conn, tx, entryId, tenantId, entryNo, entryDate,
            "expense", sourceId, employeeId, desc, ct);

        await InsertLineAsync(conn, tx, entryId, tenantId, debitAccount, "debit",
            amount, null, "경비", ct);

        await InsertLineAsync(conn, tx, entryId, tenantId, creditAccount, "credit",
            amount, null, "경비 지급", ct);
    }

    /// <summary>
    /// 급여 기표. 차변: 급여(총지급액) / 대변: 예수금(공제액) + 현금·보통예금(실지급액).
    /// </summary>
    /// <remarks>
    /// 🔴 급여는 **3줄 분개**다 — 회사가 부담한 총액과 직원이 받는 실수령액이 다르다.
    ///   차액(소득세·4대보험 등 원천징수분)은 회사가 **대신 보관했다가 나라에 내는 돈**이라
    ///   예수금(부채)으로 잡는다. 이걸 안 나누고 실수령액만 비용으로 잡으면
    ///   인건비가 과소계상되고 원천세 신고와 장부가 안 맞는다.
    ///
    /// ⚠️ 사장님 헌법 "급여는 수동입력 원칙" — 금액을 시스템이 계산하지 않는다.
    ///   여기서는 **이미 사람이 확정한 급여명세서 숫자를 그대로 회계로 옮길 뿐**이다.
    /// </remarks>
    public static async Task RecordPayrollAsync(
        IDbConnection conn,
        IDbTransaction tx,
        string tenantId,
        string sourceId,
        DateTime entryDate,
        decimal grossPay,
        decimal deduction,
        string? method,
        string? employeeId,
        string? memo,
        CancellationToken ct)
    {
        if (grossPay == 0m) return;

        var entryId = Guid.NewGuid().ToString();
        var entryNo = $"JE-{entryDate:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var netPay = grossPay - deduction;
        var desc = string.IsNullOrWhiteSpace(memo) ? "급여 자동기표" : $"급여 자동기표: {memo}";

        await InsertEntryAsync(conn, tx, entryId, tenantId, entryNo, entryDate,
            "payroll", sourceId, employeeId, desc, ct);

        // 차변 급여 — 회사가 부담한 총액
        await InsertLineAsync(conn, tx, entryId, tenantId, SalaryExpense, "debit",
            grossPay, null, "급여", ct);

        // 대변 예수금 — 원천징수해 보관 중인 돈 (공제가 0이면 라인 생략)
        if (deduction != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, WithholdingPayable, "credit",
                deduction, null, "원천징수 예수금", ct);
        }

        // 대변 현금·예금 — 직원 통장에 실제로 나간 돈
        if (netPay != 0m)
        {
            await InsertLineAsync(conn, tx, entryId, tenantId, ResolveCashAccount(method), "credit",
                netPay, null, "급여 지급", ct);
        }
    }

    private static Task InsertEntryAsync(
        IDbConnection conn, IDbTransaction tx,
        string entryId, string tenantId, string entryNo, DateTime entryDate,
        string sourceType, string sourceId, string? employeeId, string memo,
        CancellationToken ct)
    {
        // journal_entries 실 스키마: entry_id / tenant_id / entry_no / entry_date / ym /
        //   description / source_type / source_id / is_confirmed / confirmed_at / confirmed_by /
        //   created_at / created_by.  (code가 들고있던 status/employee_id/memo 컬럼은 실제로는 없음 → drift)
        var ym = entryDate.ToString("yyyy-MM");
        return conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO journal_entries
              (entry_id, tenant_id, entry_no, entry_date, ym, description, source_type, source_id,
               is_confirmed, confirmed_at, confirmed_by, created_at, created_by)
            VALUES
              (@EntryId, @TenantId, @EntryNo, @EntryDate, @Ym, @Memo, @SourceType, @SourceId,
               1, NOW(6), @EmployeeId, NOW(6), @EmployeeId)
            """,
            new { EntryId = entryId, TenantId = tenantId, EntryNo = entryNo, EntryDate = entryDate,
                  Ym = ym, SourceType = sourceType, SourceId = sourceId, EmployeeId = employeeId, Memo = memo },
            transaction: tx,
            cancellationToken: ct));
    }

    private static Task InsertLineAsync(
        IDbConnection conn, IDbTransaction tx,
        string entryId, string tenantId, string accountId, string dcType,
        decimal amount, string? partnerId, string memo,
        CancellationToken ct)
    {
        // journal_lines 실 스키마: account_code / debit_amount / credit_amount / partner_id / memo
        // (code가 들고있던 account_id/dc_type/amount는 drift).
        var debit = dcType == "debit" ? amount : 0m;
        var credit = dcType == "credit" ? amount : 0m;
        return conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO journal_lines
              (entry_id, tenant_id, account_code, debit_amount, credit_amount, partner_id, memo, created_at)
            VALUES
              (@EntryId, @TenantId, @AccountCode, @Debit, @Credit, @PartnerId, @Memo, NOW(6))
            """,
            new { EntryId = entryId, TenantId = tenantId, AccountCode = accountId,
                  Debit = debit, Credit = credit, PartnerId = partnerId, Memo = memo },
            transaction: tx,
            cancellationToken: ct));
    }
}
