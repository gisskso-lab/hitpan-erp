using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.Common;
using HitPan.Application.DTOs.Approval;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>수금·지급 서비스 — 거래처 채권·채무 관리</summary>
public class CollectionService : ICollectionService
{
    private readonly IDbConnection _db;
    private readonly IAuditService _audit;

    public CollectionService(IDbConnection db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
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
        sql += " ORDER BY c.collection_date DESC, c.created_at DESC LIMIT 500";

        var rows = (await _db.QueryAsync<CollectionListDto>(new CommandDefinition(
            sql, new { TenantId = tenantId, From = from, To = to, PartnerId = partnerId },
            cancellationToken: ct))).ToList();

        foreach (var r in rows)
            r.CollectionMethodLabel = ApprovalService.MethodLabels.GetValueOrDefault(r.CollectionMethod, r.CollectionMethod);

        return rows;
    }

    /// <summary>
    /// 서버 페이지네이션 버전 (2026-05-13 야간, 헌법 #25 정공법).
    /// 기존 GetCollectionsAsync 유지 — ServerData 전환 시 이 메서드 사용.
    /// </summary>
    public async Task<PagedResult<CollectionListDto>> GetCollectionsPagedAsync(
        string tenantId, PagedRequest req,
        DateTime? from = null, DateTime? to = null, string? partnerId = null,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        // 성능: COUNT는 JOIN 없이 collections만 스캔, SELECT만 partners JOIN.
        var countWhere = """
                         FROM collections c
                         WHERE c.tenant_id = @TenantId AND c.is_active = 1
                         """;
        var listWhere = """
                        FROM collections c
                        LEFT JOIN partners p ON p.partner_id = c.partner_id
                        WHERE c.tenant_id = @TenantId AND c.is_active = 1
                        """;
        if (from.HasValue) { countWhere += " AND c.collection_date >= @From"; listWhere += " AND c.collection_date >= @From"; }
        if (to.HasValue) { countWhere += " AND c.collection_date <= @To"; listWhere += " AND c.collection_date <= @To"; }
        if (!string.IsNullOrEmpty(partnerId)) { countWhere += " AND c.partner_id = @PartnerId"; listWhere += " AND c.partner_id = @PartnerId"; }

        var countSql = $"SELECT COUNT(*) {countWhere}";

        var listSql = $"""
                       SELECT c.collection_id AS CollectionId, c.partner_id AS PartnerId,
                              p.partner_name AS PartnerName, c.collection_date AS CollectionDate,
                              c.amount AS Amount, c.collection_method AS CollectionMethod,
                              c.ref_doc_type AS RefDocType, c.ref_doc_id AS RefDocId, c.memo AS Memo
                       {listWhere}
                       ORDER BY c.collection_date DESC, c.created_at DESC
                       LIMIT @Take OFFSET @Skip
                       """;

        var parameters = new
        {
            TenantId = tenantId,
            From = from,
            To = to,
            PartnerId = partnerId,
            req.Skip,
            req.Take
        };

        var totalCount = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            countSql, parameters, cancellationToken: ct));

        var items = totalCount == 0
            ? new List<CollectionListDto>()
            : (await _db.QueryAsync<CollectionListDto>(new CommandDefinition(
                listSql, parameters, cancellationToken: ct))).ToList();

        foreach (var r in items)
            r.CollectionMethodLabel = ApprovalService.MethodLabels.GetValueOrDefault(r.CollectionMethod, r.CollectionMethod);

        return new PagedResult<CollectionListDto>
        {
            Page = req.Page,
            PageSize = req.Take,
            TotalCount = totalCount,
            Items = items
        };
    }

    public async Task<string> CreateCollectionAsync(CreateCollectionRequest request, string tenantId, string userId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        // 월마감 체크
        await ApprovalTriggerHelper.EnsureNotClosedAsync(_db, tenantId, request.CollectionDate, ct);
        using var tx = _db.BeginTransaction();
        var id = Guid.NewGuid().ToString();
        try
        {
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

            // 감사로그 — 수금 생성
            var afterJson = $"{{\"partner_id\":\"{request.PartnerId}\",\"date\":\"{request.CollectionDate:yyyy-MM-dd}\",\"amount\":{request.Amount},\"method\":\"{request.CollectionMethod}\"}}";
            await _audit.LogAsync("create", "collection", id, afterJson: afterJson, ct: ct);

            return id;
        }
        catch (Exception)
        {
            try { tx.Rollback(); } catch (Exception rbex) { Console.Error.WriteLine($"[CollectionService] rollback failed: {rbex.Message}"); }
            throw;
        }
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
        try
        {
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

            // 감사로그 — 수금 소프트 삭제
            var beforeJson = $"{{\"partner_id\":\"{col.PartnerId}\",\"amount\":{col.Amount}}}";
            await _audit.LogAsync("delete", "collection", collectionId, beforeJson: beforeJson, ct: ct);
        }
        catch (Exception)
        {
            try { tx.Rollback(); } catch (Exception rbex) { Console.Error.WriteLine($"[CollectionService] rollback failed: {rbex.Message}"); }
            throw;
        }
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
        try
        {
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

            // 감사로그 — 지급 생성
            var afterJson = $"{{\"partner_id\":\"{request.PartnerId}\",\"date\":\"{request.PaymentDate:yyyy-MM-dd}\",\"amount\":{request.Amount},\"method\":\"{request.PaymentMethod}\"}}";
            await _audit.LogAsync("create", "payment", id, afterJson: afterJson, ct: ct);

            return id;
        }
        catch (Exception)
        {
            try { tx.Rollback(); } catch (Exception rbex) { Console.Error.WriteLine($"[CollectionService] rollback failed: {rbex.Message}"); }
            throw;
        }
    }

    public async Task DeletePaymentAsync(string paymentId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var pay = await _db.QueryFirstOrDefaultAsync<(string PartnerId, decimal Amount)>(new CommandDefinition(
            "SELECT partner_id AS PartnerId, amount AS Amount FROM payments WHERE payment_id = @Id AND tenant_id = @TenantId AND is_active = 1",
            new { Id = paymentId, TenantId = tenantId }, cancellationToken: ct));

        if (string.IsNullOrEmpty(pay.PartnerId)) return;

        using var tx = _db.BeginTransaction();
        try
        {
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

            // 감사로그 — 지급 소프트 삭제
            var beforeJson = $"{{\"partner_id\":\"{pay.PartnerId}\",\"amount\":{pay.Amount}}}";
            await _audit.LogAsync("delete", "payment", paymentId, beforeJson: beforeJson, ct: ct);
        }
        catch (Exception)
        {
            try { tx.Rollback(); } catch (Exception rbex) { Console.Error.WriteLine($"[CollectionService] rollback failed: {rbex.Message}"); }
            throw;
        }
    }

    // ═══════════════════════════════════════════
    // 미수/미지급 정공법 (WS-20260427-04, 사장님 헌법 §20)
    // 사장님 가드레일: 매입·판매 확정 전표 → 미수/미지급 자동 계산 → 전표 매칭 처리
    // - sales_deliveries 와 collections 의 ref_doc_id 1:1 매칭
    // - purchase_receipts 와 payments 의 ref_order_id 1:1 매칭
    // - 잔액 = (total + vat) - SUM(이미 처리된 금액), > 0 인 것만 노출
    // ═══════════════════════════════════════════

    public async Task<ReceivablesResponseDto> GetReceivablesAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        // 전표별 미수 잔액 (status=confirmed AND outstanding > 0)
        var docSql = @"
            SELECT
              sd.partner_id          AS PartnerId,
              sd.delivery_id         AS DeliveryId,
              sd.delivery_no         AS DeliveryNo,
              sd.delivery_date       AS DeliveryDate,
              (sd.total_amount + sd.vat_amount) AS TotalWithVat,
              IFNULL(c.collected, 0) AS Collected,
              (sd.total_amount + sd.vat_amount) - IFNULL(c.collected, 0) AS Outstanding,
              CASE
                WHEN DATEDIFF(CURDATE(), sd.delivery_date) <= 30 THEN '0_30'
                WHEN DATEDIFF(CURDATE(), sd.delivery_date) <= 60 THEN '31_60'
                WHEN DATEDIFF(CURDATE(), sd.delivery_date) <= 90 THEN '61_90'
                ELSE '90_plus'
              END AS AgingBucket
            FROM sales_deliveries sd
            LEFT JOIN (
              SELECT ref_doc_id, SUM(amount) AS collected
              FROM collections
              WHERE is_active = 1 AND ref_doc_type = 'sales_delivery' AND tenant_id = @TenantId
              GROUP BY ref_doc_id
            ) c ON c.ref_doc_id = sd.delivery_id
            WHERE sd.tenant_id = @TenantId
              AND sd.status = 'confirmed'
              AND sd.is_deleted = 0
              AND (sd.total_amount + sd.vat_amount) - IFNULL(c.collected, 0) > 0
            ORDER BY sd.delivery_date ASC";

        var docs = (await _db.QueryAsync<ReceivableDocumentDto>(new CommandDefinition(
            docSql, new { TenantId = tenantId }, cancellationToken: ct))).ToList();

        // 거래처별 그룹핑 (요약 표)
        var partnerNamesSql = @"
            SELECT partner_id, partner_name
            FROM partners
            WHERE tenant_id = @TenantId AND is_deleted = 0";
        var partnerNames = (await _db.QueryAsync<(string partner_id, string partner_name)>(
            new CommandDefinition(partnerNamesSql, new { TenantId = tenantId }, cancellationToken: ct)))
            .ToDictionary(x => x.partner_id, x => x.partner_name);

        var summary = docs.GroupBy(d => d.PartnerId).Select(g => new ReceivableSummaryDto
        {
            PartnerId = g.Key,
            PartnerName = partnerNames.GetValueOrDefault(g.Key, ""),
            Outstanding = g.Sum(d => d.Outstanding),
            Aging0_30 = g.Where(d => d.AgingBucket == "0_30").Sum(d => d.Outstanding),
            Aging31_60 = g.Where(d => d.AgingBucket == "31_60").Sum(d => d.Outstanding),
            Aging61_90 = g.Where(d => d.AgingBucket == "61_90").Sum(d => d.Outstanding),
            Aging90Plus = g.Where(d => d.AgingBucket == "90_plus").Sum(d => d.Outstanding),
            IsOverdue = g.Any(d => d.AgingBucket == "61_90" || d.AgingBucket == "90_plus"),
            DocumentCount = g.Count()
        }).OrderByDescending(s => s.Outstanding).ToList();

        return new ReceivablesResponseDto { Summary = summary, Documents = docs };
    }

    public async Task<PayablesResponseDto> GetPayablesAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);

        var docSql = @"
            SELECT
              pr.partner_id          AS PartnerId,
              pr.receipt_id          AS ReceiptId,
              pr.receipt_no          AS ReceiptNo,
              pr.receipt_date        AS ReceiptDate,
              (pr.total_amount + pr.vat_amount) AS TotalWithVat,
              IFNULL(pay.paid, 0)    AS Paid,
              (pr.total_amount + pr.vat_amount) - IFNULL(ret.returned, 0) - IFNULL(pay.paid, 0) AS Outstanding,
              CASE
                WHEN DATEDIFF(CURDATE(), pr.receipt_date) <= 30 THEN '0_30'
                WHEN DATEDIFF(CURDATE(), pr.receipt_date) <= 60 THEN '31_60'
                WHEN DATEDIFF(CURDATE(), pr.receipt_date) <= 90 THEN '61_90'
                ELSE '90_plus'
              END AS AgingBucket
            FROM purchase_receipts pr
            LEFT JOIN (
              SELECT ref_order_id, SUM(amount) AS paid
              FROM payments
              WHERE is_active = 1 AND payment_type = 'purchase' AND tenant_id = @TenantId
              GROUP BY ref_order_id
            ) pay ON pay.ref_order_id = pr.receipt_id
            -- 🔴 20260825작17 — 반품한 만큼은 줄 돈이 아니다.
            --   종전엔 지급(payments)만 뺐다. 반품해도 미지급이 그대로 남아
            --   돌려준 물건 값을 아직 줘야 하는 상태로 보였다(6/23 에 지급 축에서 겪은 것과 같은 병).
            LEFT JOIN (
              SELECT rt.receipt_id, SUM(rti.supply_amount + rti.vat_amount) AS returned
              FROM purchase_returns rt
              LEFT JOIN purchase_return_items rti ON rti.return_id = rt.return_id AND rti.tenant_id = rt.tenant_id
              WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
              GROUP BY rt.receipt_id
            ) ret ON ret.receipt_id = pr.receipt_id
            WHERE pr.tenant_id = @TenantId
              AND pr.status = 'confirmed'
              AND (pr.total_amount + pr.vat_amount) - IFNULL(ret.returned, 0) - IFNULL(pay.paid, 0) > 0
            ORDER BY pr.receipt_date ASC";

        var docs = (await _db.QueryAsync<PayableDocumentDto>(new CommandDefinition(
            docSql, new { TenantId = tenantId }, cancellationToken: ct))).ToList();

        var partnerNamesSql = @"
            SELECT partner_id, partner_name
            FROM partners
            WHERE tenant_id = @TenantId AND is_deleted = 0";
        var partnerNames = (await _db.QueryAsync<(string partner_id, string partner_name)>(
            new CommandDefinition(partnerNamesSql, new { TenantId = tenantId }, cancellationToken: ct)))
            .ToDictionary(x => x.partner_id, x => x.partner_name);

        var summary = docs.GroupBy(d => d.PartnerId).Select(g => new PayableSummaryDto
        {
            PartnerId = g.Key,
            PartnerName = partnerNames.GetValueOrDefault(g.Key, ""),
            Outstanding = g.Sum(d => d.Outstanding),
            Aging0_30 = g.Where(d => d.AgingBucket == "0_30").Sum(d => d.Outstanding),
            Aging31_60 = g.Where(d => d.AgingBucket == "31_60").Sum(d => d.Outstanding),
            Aging61_90 = g.Where(d => d.AgingBucket == "61_90").Sum(d => d.Outstanding),
            Aging90Plus = g.Where(d => d.AgingBucket == "90_plus").Sum(d => d.Outstanding),
            IsOverdue = g.Any(d => d.AgingBucket == "61_90" || d.AgingBucket == "90_plus"),
            DocumentCount = g.Count()
        }).OrderByDescending(s => s.Outstanding).ToList();

        return new PayablesResponseDto { Summary = summary, Documents = docs };
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection c) { await c.OpenAsync(ct); return; }
        _db.Open();
    }
}
