using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Finance;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 어음·카드결제·은행거래 통합 서비스 (사장님 결재 2026-04-29).
/// §#3 INSERT ONLY: bank_transactions / §#4 decimal / §#2 tenant_id 우선 / §#19 errors 0 + warnings 0
/// </summary>
public sealed class BillsCardsBankService : IBillsCardsBankService
{
    private readonly IDbConnection _db;
    private readonly ILogger<BillsCardsBankService> _logger;

    public BillsCardsBankService(IDbConnection db, ILogger<BillsCardsBankService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ═══ Bills ═══════════════════════════════════════════════
    public async Task<List<BillDto>> ListBillsAsync(string tenantId, string? type, string? status, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT b.bill_id AS BillId, b.bill_type AS BillType, b.bill_no AS BillNo,
                   b.bank_name AS BankName, b.issue_place AS IssuePlace,
                   b.partner_id AS PartnerId, COALESCE(p.partner_name, b.partner_name_legacy) AS PartnerName,
                   b.issue_date AS IssueDate, b.maturity_date AS MaturityDate,
                   b.discount_date AS DiscountDate, b.settled_date AS SettledDate,
                   b.amount AS Amount, b.status AS Status, b.remark AS Remark
            FROM bills b
            LEFT JOIN partners p ON p.partner_id = b.partner_id AND p.tenant_id = b.tenant_id
            WHERE b.tenant_id = @TenantId
              AND (@Type IS NULL OR b.bill_type = @Type)
              AND (@Status IS NULL OR b.status = @Status)
              AND (@From IS NULL OR b.issue_date >= @From)
              AND (@To IS NULL OR b.issue_date <= @To)
            ORDER BY b.issue_date DESC, b.bill_no
            """;
        var rows = await _db.QueryAsync<BillDto>(new CommandDefinition(sql,
            new { TenantId = tenantId, Type = type, Status = status, From = from, To = to },
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
    }

    public async Task<string> CreateBillAsync(string tenantId, CreateBillRequest req, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var billId = Guid.NewGuid().ToString();
        const string sql = """
            INSERT INTO bills (bill_id, tenant_id, bill_type, bill_no, bank_name, issue_place,
                               partner_id, issue_date, maturity_date, amount, status, remark)
            VALUES (@BillId, @TenantId, @BillType, @BillNo, @BankName, @IssuePlace,
                    @PartnerId, @IssueDate, @MaturityDate, @Amount, 'issued', @Remark)
            """;
        await _db.ExecuteAsync(new CommandDefinition(sql, new
        {
            BillId = billId, TenantId = tenantId,
            req.BillType, req.BillNo, req.BankName, req.IssuePlace,
            req.PartnerId, req.IssueDate, req.MaturityDate, req.Amount, req.Remark
        }, cancellationToken: ct)).ConfigureAwait(false);
        return billId;
    }

    public async Task UpdateBillStatusAsync(string tenantId, string billId, UpdateBillStatusRequest req, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        const string sql = """
            UPDATE bills SET status = @Status,
                discount_date = COALESCE(@DiscountDate, discount_date),
                settled_date = COALESCE(@SettledDate, settled_date)
            WHERE bill_id = @BillId AND tenant_id = @TenantId
            """;
        await _db.ExecuteAsync(new CommandDefinition(sql, new
        {
            BillId = billId, TenantId = tenantId,
            req.Status, req.DiscountDate, req.SettledDate
        }, cancellationToken: ct)).ConfigureAwait(false);
    }

    // ═══ Card Payments ═══════════════════════════════════════
    public async Task<List<CardPaymentDto>> ListCardPaymentsAsync(string tenantId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT card_payment_id AS CardPaymentId, card_no AS CardNo, card_company AS CardCompany,
                   holder_name AS HolderName, payment_date AS PaymentDate, bank_settle_date AS BankSettleDate,
                   total_amount AS TotalAmount, installment_amount AS InstallmentAmount,
                   installment_months AS InstallmentMonths, settled_amount AS SettledAmount,
                   status AS Status, remark AS Remark
            FROM card_payments
            WHERE tenant_id = @TenantId
              AND (@From IS NULL OR payment_date >= @From)
              AND (@To IS NULL OR payment_date <= @To)
            ORDER BY payment_date DESC
            """;
        var rows = await _db.QueryAsync<CardPaymentDto>(new CommandDefinition(sql,
            new { TenantId = tenantId, From = from, To = to }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
    }

    public async Task<CardPaymentDto?> GetCardPaymentAsync(string tenantId, string cardPaymentId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string headSql = """
            SELECT card_payment_id AS CardPaymentId, card_no AS CardNo, card_company AS CardCompany,
                   holder_name AS HolderName, payment_date AS PaymentDate, bank_settle_date AS BankSettleDate,
                   total_amount AS TotalAmount, installment_amount AS InstallmentAmount,
                   installment_months AS InstallmentMonths, settled_amount AS SettledAmount,
                   status AS Status, remark AS Remark
            FROM card_payments WHERE card_payment_id = @Id AND tenant_id = @TenantId
            """;
        var head = await _db.QuerySingleOrDefaultAsync<CardPaymentDto>(new CommandDefinition(headSql,
            new { Id = cardPaymentId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        if (head is null) return null;

        const string lineSql = """
            SELECT l.line_id AS LineId, l.seq AS Seq, l.partner_id AS PartnerId,
                   COALESCE(p.partner_name, l.partner_name_legacy) AS PartnerName,
                   l.tx_date AS TxDate, l.amount AS Amount, l.remark AS Remark
            FROM card_payment_lines l
            LEFT JOIN partners p ON p.partner_id = l.partner_id AND p.tenant_id = l.tenant_id
            WHERE l.tenant_id = @TenantId AND l.card_payment_id = @Id
            ORDER BY l.seq
            """;
        var lines = await _db.QueryAsync<CardPaymentLineDto>(new CommandDefinition(lineSql,
            new { Id = cardPaymentId, TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        head.Lines = lines.ToList();
        return head;
    }

    public async Task<string> CreateCardPaymentAsync(string tenantId, CreateCardPaymentRequest req, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var headerId = Guid.NewGuid().ToString();
        IDbTransaction? tx = null;
        try
        {
            tx = _db.BeginTransaction();

            const string headSql = """
                INSERT INTO card_payments (card_payment_id, tenant_id, card_no, card_company, holder_name,
                    payment_date, total_amount, installment_amount, installment_months, status, remark)
                VALUES (@Id, @TenantId, @CardNo, @CardCompany, @HolderName,
                    @PaymentDate, @TotalAmount, @InstallmentAmount, @InstallmentMonths, 'pending', @Remark)
                """;
            await _db.ExecuteAsync(new CommandDefinition(headSql, new
            {
                Id = headerId, TenantId = tenantId,
                req.CardNo, req.CardCompany, req.HolderName,
                req.PaymentDate, req.TotalAmount, req.InstallmentAmount, req.InstallmentMonths, req.Remark
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            const string lineSql = """
                INSERT INTO card_payment_lines (line_id, card_payment_id, tenant_id, seq, partner_id, tx_date, amount, remark)
                VALUES (@LineId, @HeaderId, @TenantId, @Seq, @PartnerId, @TxDate, @Amount, @Remark)
                """;
            int seq = 1;
            foreach (var l in req.Lines)
            {
                await _db.ExecuteAsync(new CommandDefinition(lineSql, new
                {
                    LineId = Guid.NewGuid().ToString(), HeaderId = headerId, TenantId = tenantId,
                    Seq = seq++, l.PartnerId, l.TxDate, l.Amount, l.Remark
                }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            tx.Commit();
            return headerId;
        }
        catch
        {
            tx?.Rollback();
            throw;
        }
        finally { tx?.Dispose(); }
    }

    // ═══ Bank Transactions (INSERT ONLY) ═══════════════════
    public async Task<List<BankTxDto>> ListBankTxAsync(string tenantId, string? accountNo, DateTime? from, DateTime? to, CancellationToken ct = default)
        => await ListBankTxAsync(tenantId, accountNo, from, to, 500, ct).ConfigureAwait(false);

    // 헌법 #19·#25 정합 — 5/27 P1-4 봉합 (demo 1차 audit: 3,770행 폭탄 / 32초 로드)
    public async Task<List<BankTxDto>> ListBankTxAsync(string tenantId, string? accountNo, DateTime? from, DateTime? to, int limit, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (limit <= 0 || limit > 5000) limit = 500;

        const string sql = """
            SELECT b.bank_tx_id AS BankTxId, b.account_no AS AccountNo, b.bank_name AS BankName,
                   b.tx_date AS TxDate, b.tx_type AS TxType, b.amount AS Amount, b.balance_after AS BalanceAfter,
                   b.partner_id AS PartnerId, COALESCE(p.partner_name, b.partner_name_legacy) AS PartnerName,
                   b.description AS Description, b.remark AS Remark, b.imported_from AS ImportedFrom
            FROM bank_transactions b
            LEFT JOIN partners p ON p.partner_id = b.partner_id AND p.tenant_id = b.tenant_id
            WHERE b.tenant_id = @TenantId
              AND (@AccountNo IS NULL OR b.account_no = @AccountNo)
              AND (@From IS NULL OR b.tx_date >= @From)
              AND (@To IS NULL OR b.tx_date <= @To)
            ORDER BY b.tx_date DESC, b.created_at DESC
            LIMIT @Limit
            """;
        var rows = await _db.QueryAsync<BankTxDto>(new CommandDefinition(sql,
            new { TenantId = tenantId, AccountNo = accountNo, From = from, To = to, Limit = limit },
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
    }

    public async Task<string> CreateBankTxAsync(string tenantId, CreateBankTxRequest req, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var id = Guid.NewGuid().ToString();
        const string sql = """
            INSERT INTO bank_transactions (bank_tx_id, tenant_id, account_no, bank_name, tx_date, tx_type,
                amount, balance_after, partner_id, description, remark, imported_from)
            VALUES (@Id, @TenantId, @AccountNo, @BankName, @TxDate, @TxType,
                @Amount, @BalanceAfter, @PartnerId, @Description, @Remark, 'manual')
            """;
        await _db.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id, TenantId = tenantId,
            req.AccountNo, req.BankName, req.TxDate, req.TxType,
            req.Amount, req.BalanceAfter, req.PartnerId, req.Description, req.Remark
        }, cancellationToken: ct)).ConfigureAwait(false);
        return id;
    }

    // ═══ DB ════════════════════════════════════════════════
    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State != ConnectionState.Open && _db is DbConnection dc)
        {
            await dc.OpenAsync(ct).ConfigureAwait(false);
        }
    }
}
