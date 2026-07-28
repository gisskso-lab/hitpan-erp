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
