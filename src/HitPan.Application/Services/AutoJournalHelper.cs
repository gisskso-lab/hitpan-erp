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
    public const string SalesRevenue = "acc-sales-revenue";             // 매출 (대변)
    public const string VatPayable = "acc-vat-payable";                 // 부가세예수금 (대변)
    public const string AccountsReceivable = "acc-accounts-receivable"; // 외상매출금 (차변)

    public const string PurchaseCost = "acc-purchase-cost";             // 매입 (차변)
    public const string VatReceivable = "acc-vat-receivable";           // 부가세대급금 (차변)
    public const string AccountsPayable = "acc-accounts-payable";       // 외상매입금 (대변)

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
        var entryId = Guid.NewGuid().ToString();
        var entryNo = $"JE-{entryDate:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var total = supplyAmount + vatAmount;

        await InsertEntryAsync(conn, tx, entryId, tenantId, entryNo, entryDate,
            "sales", sourceId, employeeId, $"매출 자동기표: {documentNo}", ct);

        // 차변 외상매출금
        await InsertLineAsync(conn, tx, entryId, tenantId, AccountsReceivable, "debit",
            total, partnerId, $"매출채권 {documentNo}", ct);

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

        // 대변 외상매입금
        await InsertLineAsync(conn, tx, entryId, tenantId, AccountsPayable, "credit",
            total, partnerId, $"매입채무 {documentNo}", ct);
    }

    private static Task InsertEntryAsync(
        IDbConnection conn, IDbTransaction tx,
        string entryId, string tenantId, string entryNo, DateTime entryDate,
        string sourceType, string sourceId, string? employeeId, string memo,
        CancellationToken ct)
    {
        return conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO journal_entries
              (entry_id, tenant_id, entry_no, entry_date, source_type, source_id,
               status, employee_id, memo, created_at)
            VALUES
              (@EntryId, @TenantId, @EntryNo, @EntryDate, @SourceType, @SourceId,
               'confirmed', @EmployeeId, @Memo, NOW())
            """,
            new { EntryId = entryId, TenantId = tenantId, EntryNo = entryNo, EntryDate = entryDate,
                  SourceType = sourceType, SourceId = sourceId, EmployeeId = employeeId, Memo = memo },
            transaction: tx,
            cancellationToken: ct));
    }

    private static Task InsertLineAsync(
        IDbConnection conn, IDbTransaction tx,
        string entryId, string tenantId, string accountId, string dcType,
        decimal amount, string? partnerId, string memo,
        CancellationToken ct)
    {
        return conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO journal_lines
              (entry_id, tenant_id, account_id, dc_type, amount, partner_id, memo, created_at)
            VALUES
              (@EntryId, @TenantId, @AccountId, @DcType, @Amount, @PartnerId, @Memo, NOW())
            """,
            new { EntryId = entryId, TenantId = tenantId, AccountId = accountId, DcType = dcType,
                  Amount = amount, PartnerId = partnerId, Memo = memo },
            transaction: tx,
            cancellationToken: ct));
    }
}
