using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Approval;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>수금·지급 서비스 — 거래처 채권·채무 관리</summary>
public class CollectionService : ICollectionService
{
    private readonly IDbConnection _db;

    public CollectionService(IDbConnection db)
    {
        _db = db;
    }

    // ═══════════════════════════════════════════
    // 수금 (거래처에서 받은 돈)
    // ═══════════════════════════════════════════

    public async Task<List<CollectionListDto>> GetCollectionsAsync(
        string tenantId, DateTime? from = null, DateTime? to = null, string? partnerId = null, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var sql = """
            SELECT c.collection_id AS CollectionId, c.partner_id AS PartnerId,
                   p.partner_name AS PartnerName, c.collection_date AS CollectionDate,
                   c.amount AS Amount, c.collection_method AS CollectionMethod,
                   c.ref_doc_type AS RefDocType, c.ref_doc_id AS RefDocId, c.memo AS Memo
            FROM collections c
            LEFT JOIN partners p ON p.partner_id = c.partner_id
            WHERE c.tenant_id = @TenantId AND c.is_active = 1
            """;
        if (from.HasValue) sql += " AND c.collection_date >= @From";
        if (to.HasValue) sql += " AND c.collection_date <= @To";
        if (!string.IsNullOrEmpty(partnerId)) sql += " AND c.partner_id = @PartnerId";
        sql += " ORDER BY c.collection_date DESC, c.created_at DESC";

        var rows = (await _db.QueryAsync<CollectionListDto>(new CommandDefinition(
            sql, new { TenantId = tenantId, From = from, To = to, PartnerId = partnerId },
            cancellationToken: ct))).ToList();

        foreach (var r in rows)
            r.CollectionMethodLabel = ApprovalService.MethodLabels.GetValueOrDefault(r.CollectionMethod, r.CollectionMethod);

        return rows;
    }

    public async Task<string> CreateCollectionAsync(CreateCollectionRequest request, string tenantId, string userId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        // 월마감 체크
        await ApprovalTriggerHelper.EnsureNotClosedAsync(_db, tenantId, request.CollectionDate, ct);
        using var tx = _db.BeginTransaction();
        var id = Guid.NewGuid().ToString();
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO collections
              (collection_id, tenant_id, partner_id, collection_date, amount, collection_method,
               ref_doc_type, ref_doc_id, memo, created_by)
            VALUES
              (@Id, @TenantId, @PartnerId, @CollectionDate, @Amount, @Method,
               @RefDocType, @RefDocId, @Memo, @UserId)
            """,
            new
            {
                Id = id,
                TenantId = tenantId,
                request.PartnerId,
                request.CollectionDate,
                request.Amount,
                Method = request.CollectionMethod,
                request.RefDocType,
                request.RefDocId,
                request.Memo,
                UserId = userId
            }, transaction: tx, cancellationToken: ct));

        // partner_balance 수금 반영
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO partner_balance
              (balance_id, tenant_id, partner_id, total_sales, total_receipt, total_purchase, total_payment, last_updated_at)
            VALUES
              (UUID(), @TenantId, @PartnerId, 0, @Amount, 0, 0, NOW(6))
            ON DUPLICATE KEY UPDATE
              total_receipt = total_receipt + @Amount,
              last_updated_at = NOW(6)
            """,
            new { TenantId = tenantId, request.PartnerId, request.Amount }, transaction: tx, cancellationToken: ct));

        tx.Commit();
        return id;
    }

    public async Task DeleteCollectionAsync(string collectionId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        // 금액 조회 후 partner_balance 차감
        var col = await _db.QueryFirstOrDefaultAsync<(string PartnerId, decimal Amount)>(new CommandDefinition(
            "SELECT partner_id AS PartnerId, amount AS Amount FROM collections WHERE collection_id = @Id AND tenant_id = @TenantId AND is_active = 1",
            new { Id = collectionId, TenantId = tenantId }, cancellationToken: ct));

        if (string.IsNullOrEmpty(col.PartnerId)) return;

        // 트랜잭션으로 비활성화 + 잔액 차감 원자적 처리
        using var tx = _db.BeginTransaction();

        await _db.ExecuteAsync(new CommandDefinition(
            "UPDATE collections SET is_active = 0, updated_at = NOW(6) WHERE collection_id = @Id AND tenant_id = @TenantId",
            new { Id = collectionId, TenantId = tenantId }, transaction: tx, cancellationToken: ct));

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE partner_balance SET
              total_receipt = GREATEST(0, total_receipt - @Amount),
              last_updated_at = NOW(6)
            WHERE tenant_id = @TenantId AND partner_id = @PartnerId
            """,
            new { TenantId = tenantId, col.PartnerId, col.Amount }, transaction: tx, cancellationToken: ct));

        tx.Commit();
    }

    // ═══════════════════════════════════════════
    // 지급 (거래처에 준 돈 — 기존 payments 테이블 사용)
    // ═══════════════════════════════════════════

    public async Task<List<PaymentListDto>> GetPaymentsAsync(
        string tenantId, DateTime? from = null, DateTime? to = null, string? partnerId = null, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var sql = """
            SELECT py.payment_id AS PaymentId, py.partner_id AS PartnerId,
                   p.partner_name AS PartnerName, py.payment_date AS PaymentDate,
                   py.amount AS Amount, py.payment_method AS PaymentMethod,
                   py.payment_type AS PaymentType, py.ref_order_id AS RefOrderId, py.memo AS Memo
            FROM payments py
            LEFT JOIN partners p ON p.partner_id = py.partner_id
            WHERE py.tenant_id = @TenantId AND py.is_active = 1
            """;
        if (from.HasValue) sql += " AND py.payment_date >= @From";
        if (to.HasValue) sql += " AND py.payment_date <= @To";
        if (!string.IsNullOrEmpty(partnerId)) sql += " AND py.partner_id = @PartnerId";
        sql += " ORDER BY py.payment_date DESC, py.created_at DESC";

        var rows = (await _db.QueryAsync<PaymentListDto>(new CommandDefinition(
            sql, new { TenantId = tenantId, From = from, To = to, PartnerId = partnerId },
            cancellationToken: ct))).ToList();

        foreach (var r in rows)
            r.PaymentMethodLabel = ApprovalService.MethodLabels.GetValueOrDefault(r.PaymentMethod, r.PaymentMethod);

        return rows;
    }

    public async Task<string> CreatePaymentAsync(CreatePaymentRequest request, string tenantId, string userId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await ApprovalTriggerHelper.EnsureNotClosedAsync(_db, tenantId, request.PaymentDate, ct);
        using var tx = _db.BeginTransaction();
        var id = Guid.NewGuid().ToString();
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO payments
              (payment_id, tenant_id, partner_id, payment_date, amount, payment_method,
               payment_type, ref_order_id, memo, created_by)
            VALUES
              (@Id, @TenantId, @PartnerId, @PaymentDate, @Amount, @Method,
               @PaymentType, @RefOrderId, @Memo, @UserId)
            """,
            new
            {
                Id = id,
                TenantId = tenantId,
                request.PartnerId,
                request.PaymentDate,
                request.Amount,
                Method = request.PaymentMethod,
                request.PaymentType,
                request.RefOrderId,
                request.Memo,
                UserId = userId
            }, transaction: tx, cancellationToken: ct));

        // partner_balance 지급 반영
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO partner_balance
              (balance_id, tenant_id, partner_id, total_sales, total_receipt, total_purchase, total_payment, last_updated_at)
            VALUES
              (UUID(), @TenantId, @PartnerId, 0, 0, 0, @Amount, NOW(6))
            ON DUPLICATE KEY UPDATE
              total_payment = total_payment + @Amount,
              last_updated_at = NOW(6)
            """,
            new { TenantId = tenantId, request.PartnerId, request.Amount }, transaction: tx, cancellationToken: ct));

        tx.Commit();
        return id;
    }

    public async Task DeletePaymentAsync(string paymentId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var pay = await _db.QueryFirstOrDefaultAsync<(string PartnerId, decimal Amount)>(new CommandDefinition(
            "SELECT partner_id AS PartnerId, amount AS Amount FROM payments WHERE payment_id = @Id AND tenant_id = @TenantId AND is_active = 1",
            new { Id = paymentId, TenantId = tenantId }, cancellationToken: ct));

        if (string.IsNullOrEmpty(pay.PartnerId)) return;

        using var tx = _db.BeginTransaction();

        await _db.ExecuteAsync(new CommandDefinition(
            "UPDATE payments SET is_active = 0, updated_at = NOW(6) WHERE payment_id = @Id AND tenant_id = @TenantId",
            new { Id = paymentId, TenantId = tenantId }, transaction: tx, cancellationToken: ct));

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE partner_balance SET
              total_payment = GREATEST(0, total_payment - @Amount),
              last_updated_at = NOW(6)
            WHERE tenant_id = @TenantId AND partner_id = @PartnerId
            """,
            new { TenantId = tenantId, pay.PartnerId, pay.Amount }, transaction: tx, cancellationToken: ct));

        tx.Commit();
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection c) { await c.OpenAsync(ct); return; }
        _db.Open();
    }
}
