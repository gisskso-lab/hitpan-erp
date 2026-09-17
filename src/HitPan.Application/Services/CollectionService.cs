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
                   c.ref_doc_type AS RefDocType, c.ref_doc_id AS RefDocId, c.memo AS Memo,
                   c.source_type AS SourceType
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
                              c.ref_doc_type AS RefDocType, c.ref_doc_id AS RefDocId, c.memo AS Memo,
                              c.source_type AS SourceType
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
            // 🔴 20260915작1 3판 R1 (설계 §22 · P2 · P3) — 맞출 대상 검사. 같은 트랜잭션 · INSERT 앞 · 실패 = 400 고객 문구.
            await EnsureCollectionTargetAllowedAsync(request, tenantId, tx, ct);

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

            // 🔴 20260827작4 (사장님 오더 "모든 돈의 흐름을 회계장부 하나로") — 수금 자동기표.
            //   차변 현금·보통예금 / 대변 외상매출금.
            //   ⚠️ **같은 트랜잭션 안**이다 — 수금은 저장됐는데 분개만 빠지는 일이 없어야 한다.
            //     기표가 실패하면 수금 저장도 함께 롤백된다(정합성 우선, 헌법 #20 아래 #42).
            await AutoJournalHelper.RecordCollectionAsync(
                _db, tx, tenantId, id, request.CollectionDate,
                request.PartnerId, request.Amount, request.CollectionMethod, userId, ct);

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
        // 20260915작1 3판 R1 — 역분개(날짜·결제수단)와 월마감(P4) 판정에 원 수금일·수단이 필요하다.
        var col = await _db.QueryFirstOrDefaultAsync<(string PartnerId, decimal Amount, string Method, DateTime Date, string? SourceType)>(new CommandDefinition(
            "SELECT partner_id AS PartnerId, amount AS Amount, collection_method AS Method, collection_date AS Date, source_type AS SourceType FROM collections WHERE collection_id = @Id AND tenant_id = @TenantId AND is_active = 1",
            new { Id = collectionId, TenantId = tenantId }, cancellationToken: ct));

        if (string.IsNullOrEmpty(col.PartnerId)) return;

        // 🔴 20260915작1 3판 R1 (PM 후속 1) — 이관 수금은 partner_balance 에 기록되지 않았고(MdbMigrationService 이관) 레거시 줄은 빼지 않는다 → 삭제 거절.
        if (string.Equals(col.SourceType, "migration", StringComparison.Ordinal))
            throw new InvalidOperationException(MsgMigratedCollectionDelete);

        // 🔴 P4 — 역분개 날짜 = 원 수금일. 그 달이 마감됐으면 삭제를 막는다(등록과 같은 문구).
        await ApprovalTriggerHelper.EnsureNotClosedAsync(_db, tenantId, col.Date, ct);

        // 트랜잭션으로 비활성화 + 잔액 차감 원자적 처리
        using var tx = _db.BeginTransaction();
        try
        {
            var affected = await _db.ExecuteAsync(new CommandDefinition(
                "UPDATE collections SET is_active = 0, updated_at = NOW(6) WHERE collection_id = @Id AND tenant_id = @TenantId AND is_active = 1",
                new { Id = collectionId, TenantId = tenantId }, transaction: tx, cancellationToken: ct));
            if (affected == 0)
            {
                // 동시에 다른 요청이 먼저 지웠다 — 잔액·역분개를 두 번 하지 않는다.
                tx.Rollback();
                return;
            }

            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE partner_balance SET
                  total_receipt = GREATEST(0, total_receipt - @Amount),
                  last_updated_at = NOW(6)
                WHERE tenant_id = @TenantId AND partner_id = @PartnerId
                """,
                new { TenantId = tenantId, col.PartnerId, col.Amount }, transaction: tx, cancellationToken: ct));

            // 🔴 20260915작1 3판 R1 (R-A5①) — 수금 취소 역분개. 같은 트랜잭션 · 원 수금일.
            await AutoJournalHelper.RecordCollectionCancelAsync(
                _db, tx, tenantId, collectionId, col.Date,
                col.PartnerId, col.Amount, col.Method, null, ct);

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
                   py.payment_type AS PaymentType, py.ref_order_id AS RefOrderId, py.memo AS Memo,
                   py.source_type AS SourceType
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
            // 🔴 20260915작1 3판 R1 (설계 §21·§22 · P2 · P3) — 맞출 대상 검사. 수금과 대칭.
            await EnsurePaymentTargetAllowedAsync(request, tenantId, tx, ct);

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

            // 🔴 20260827작4 — 지급 자동기표. 차변 외상매입금 / 대변 현금·보통예금.
            //   수금(RecordCollectionAsync)의 정확한 반대. 같은 트랜잭션 안이다.
            await AutoJournalHelper.RecordPaymentAsync(
                _db, tx, tenantId, id, request.PaymentDate,
                request.PartnerId, request.Amount, request.PaymentMethod, userId, ct);

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
        // 20260915작1 3판 R1 — 역분개·월마감(P4)용 원 지급일·수단.
        var pay = await _db.QueryFirstOrDefaultAsync<(string PartnerId, decimal Amount, string Method, DateTime Date, string? SourceType)>(new CommandDefinition(
            "SELECT partner_id AS PartnerId, amount AS Amount, payment_method AS Method, payment_date AS Date, source_type AS SourceType FROM payments WHERE payment_id = @Id AND tenant_id = @TenantId AND is_active = 1",
            new { Id = paymentId, TenantId = tenantId }, cancellationToken: ct));

        if (string.IsNullOrEmpty(pay.PartnerId)) return;

        // 🔴 20260915작1 3판 R1 (PM 후속 1) — 이관 지급 삭제 거절(수금과 같은 이유).
        if (string.Equals(pay.SourceType, "migration", StringComparison.Ordinal))
            throw new InvalidOperationException(MsgMigratedPaymentDelete);

        // 🔴 P4 — 원 지급일의 달이 마감됐으면 삭제를 막는다.
        await ApprovalTriggerHelper.EnsureNotClosedAsync(_db, tenantId, pay.Date, ct);

        using var tx = _db.BeginTransaction();
        try
        {
            var affected = await _db.ExecuteAsync(new CommandDefinition(
                "UPDATE payments SET is_active = 0, updated_at = NOW(6) WHERE payment_id = @Id AND tenant_id = @TenantId AND is_active = 1",
                new { Id = paymentId, TenantId = tenantId }, transaction: tx, cancellationToken: ct));
            if (affected == 0)
            {
                tx.Rollback();
                return;
            }

            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE partner_balance SET
                  total_payment = GREATEST(0, total_payment - @Amount),
                  last_updated_at = NOW(6)
                WHERE tenant_id = @TenantId AND partner_id = @PartnerId
                """,
                new { TenantId = tenantId, pay.PartnerId, pay.Amount }, transaction: tx, cancellationToken: ct));

            // 🔴 20260915작1 3판 R1 (R-A5①) — 지급 취소 역분개. 같은 트랜잭션 · 원 지급일.
            await AutoJournalHelper.RecordPaymentCancelAsync(
                _db, tx, tenantId, paymentId, pay.Date,
                pay.PartnerId, pay.Amount, pay.Method, null, ct);

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
              -- 🔴 20260915작1 갈래 E: 이월잔액 행이 있는 회사는 이관 명세서를 명세서별로 보이지 않는다 — 아래 「이전 프로그램 이월」 요약 1줄이 대신한다.
              AND NOT (COALESCE(sd.source_type, '') = 'migration' AND " + MdbLegacyPartnerBalance.HasLegacyBalanceSql + @")
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
        }).ToList();

        // 🔴 20260915작1 갈래 E (설계 §17 · R-A2 (나)) — 「이전 프로그램 이월」 미수(partner_legacy_balances 의 + 잔액)를 거래처 요약에 한 번 더한다.
        //   명세서 목록(Documents)에는 넣지 않는다: 이 화면의 명세서 줄은 수금 등록 때 ref_doc_id 로 쓰인다(CollectionPage.razor:496) — 가짜 명세서 id 금지.
        //   사람이 이관 명세서에 붙여 넣은 수금(ref_doc_id → 이관 명세서)은 이월잔액에서 뺀다(대시보드 미수 식과 같은 기준).
        var legacyRows = await GetLegacyOutstandingAsync(tenantId, receivable: true, ct);
        MergeLegacyIntoSummary(summary, legacyRows, partnerNames,
            () => new ReceivableSummaryDto(),
            s => s.PartnerId,
            (s, amt, bucket) =>
            {
                s.Outstanding += amt;
                switch (bucket)
                {
                    case "0_30": s.Aging0_30 += amt; break;
                    case "31_60": s.Aging31_60 += amt; break;
                    case "61_90": s.Aging61_90 += amt; s.IsOverdue = true; break;
                    default: s.Aging90Plus += amt; s.IsOverdue = true; break;
                }
            },
            (s, id, name) => { s.PartnerId = id; s.PartnerName = name; });
        summary = summary.OrderByDescending(s => s.Outstanding).ToList();

        return new ReceivablesResponseDto { Summary = summary, Documents = docs, LegacyBalances = ToLegacyRows(legacyRows, partnerNames) };
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
              -- 🔴 20260915작1 갈래 E: 이월잔액 행이 있는 회사는 이관 매입을 명세서별로 보이지 않는다 — 「이전 프로그램 이월」 요약 1줄이 대신한다.
              AND NOT (COALESCE(pr.source_type, '') = 'migration' AND " + MdbLegacyPartnerBalance.HasLegacyBalanceSql + @")
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
        }).ToList();

        // 🔴 20260915작1 갈래 E — 「이전 프로그램 이월」 미지급(partner_legacy_balances 의 − 잔액 절대값)을 거래처 요약에 한 번 더한다. 명세서 목록에는 넣지 않는다.
        var legacyRows = await GetLegacyOutstandingAsync(tenantId, receivable: false, ct);
        MergeLegacyIntoSummary(summary, legacyRows, partnerNames,
            () => new PayableSummaryDto(),
            s => s.PartnerId,
            (s, amt, bucket) =>
            {
                s.Outstanding += amt;
                switch (bucket)
                {
                    case "0_30": s.Aging0_30 += amt; break;
                    case "31_60": s.Aging31_60 += amt; break;
                    case "61_90": s.Aging61_90 += amt; s.IsOverdue = true; break;
                    default: s.Aging90Plus += amt; s.IsOverdue = true; break;
                }
            },
            (s, id, name) => { s.PartnerId = id; s.PartnerName = name; });
        summary = summary.OrderByDescending(s => s.Outstanding).ToList();

        return new PayablesResponseDto { Summary = summary, Documents = docs, LegacyBalances = ToLegacyRows(legacyRows, partnerNames) };
    }

    /// <summary>
    /// 20260915작1 갈래 E — 거래처별 「이전 프로그램 이월」 남은 금액.
    /// 미수: + 잔액 − 사람이 이관 명세서에 붙인 수금 · 미지급: − 잔액 절대값 − 사람이 이관 매입에 붙인 지급·반품. 0 이하 거래처는 뺀다.
    /// </summary>
    /// <remarks>
    /// 🔴 20260915작1 3판 R1 (설계 §20 #3·#4) — 식은 <see cref="LegacyBalanceMatching.ListAsync"/> 한 곳(M = 이월 매칭 수금·지급 + 갈래 E 호환분).
    /// 화면 한 줄은 R &gt; 0 만. 갈래 E 의 옛 이월 식(이관 명세서·매입에 붙인 사람 수금·지급만 뺐다)은 이 공용 식의 E 호환분으로 옮겼다.
    /// </remarks>
    private async Task<List<LegacyOutstandingRow>> GetLegacyOutstandingAsync(string tenantId, bool receivable, CancellationToken ct)
    {
        var matched = await LegacyBalanceMatching.ListAsync(_db, null, tenantId, receivable, ct);
        return matched
            .Where(r => r.RemainingAmount > 0m)
            .Select(r => new LegacyOutstandingRow
            {
                PartnerId = r.PartnerId,
                Amount = r.RemainingAmount,
                BaseDate = r.BaseDate,
                LegacyAmount = r.LegacyAmount,
                MatchedAmount = r.MatchedAmount,
            })
            .ToList();
    }

    private static void MergeLegacyIntoSummary<TSummary>(
        List<TSummary> summary,
        List<LegacyOutstandingRow> legacyRows,
        IReadOnlyDictionary<string, string> partnerNames,
        Func<TSummary> create,
        Func<TSummary, string> keyOf,
        Action<TSummary, decimal, string> add,
        Action<TSummary, string, string> init)
    {
        if (legacyRows.Count == 0) return;
        var byPartner = summary.ToDictionary(keyOf, s => s, StringComparer.Ordinal);
        foreach (var row in legacyRows)
        {
            if (!byPartner.TryGetValue(row.PartnerId, out var s))
            {
                s = create();
                init(s, row.PartnerId, partnerNames.GetValueOrDefault(row.PartnerId, ""));
                summary.Add(s);
                byPartner[row.PartnerId] = s;
            }
            var days = (DateTime.Today - row.BaseDate.Date).Days;
            var bucket = days <= 30 ? "0_30" : days <= 60 ? "31_60" : days <= 90 ? "61_90" : "90_plus";
            add(s, row.Amount, bucket);
        }
    }

    private sealed class LegacyOutstandingRow
    {
        public string PartnerId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime BaseDate { get; set; }
        public decimal LegacyAmount { get; set; }
        public decimal MatchedAmount { get; set; }
    }

    private static List<LegacyBalanceRowDto> ToLegacyRows(List<LegacyOutstandingRow> rows, IReadOnlyDictionary<string, string> partnerNames)
        => rows.Select(r => new LegacyBalanceRowDto
        {
            PartnerId = r.PartnerId,
            PartnerName = partnerNames.GetValueOrDefault(r.PartnerId, ""),
            BaseDate = r.BaseDate,
            LegacyAmount = r.LegacyAmount,
            MatchedAmount = r.MatchedAmount,
            RemainingAmount = r.Amount,
        }).ToList();

    // ═══════════════════════════════════════════
    // 🔴 20260915작1 3판 R1 — 맞출 대상 서버 검사 (설계 §22 · §27 P2·P3)
    // ═══════════════════════════════════════════

    internal const string MsgMigratedCollectionDelete = "이전 프로그램에서 옮겨 온 수금은 삭제할 수 없습니다.";
    internal const string MsgMigratedPaymentDelete = "이전 프로그램에서 옮겨 온 지급은 삭제할 수 없습니다.";
    internal const string MsgNoDelivery = "맞출 거래명세서가 없습니다.";
    internal const string MsgNoReceipt = "맞출 매입전표가 없습니다.";
    internal const string MsgDeliveryOver = "이 거래명세서에 남은 받을 돈은 {0:N0}원입니다. 그보다 큰 금액은 맞출 수 없습니다.";
    internal const string MsgReceiptOver = "이 매입전표에 남은 줄 돈은 {0:N0}원입니다. 그보다 큰 금액은 맞출 수 없습니다.";
    internal const string MsgMigratedDelivery = "이전 프로그램에서 옮겨온 거래명세서입니다. 이 거래처의 받을 돈은 「" + LegacyBalanceMatching.Label + "」으로 맞춰 주세요.";
    internal const string MsgMigratedReceipt = "이전 프로그램에서 옮겨온 매입전표입니다. 이 거래처의 줄 돈은 「" + LegacyBalanceMatching.Label + "」으로 맞춰 주세요.";

    private static readonly System.Globalization.CultureInfo Ko = System.Globalization.CultureInfo.GetCultureInfo("ko-KR");

    /// <summary>
    /// 수금 등록 검사. ref = 이월잔액 → <see cref="LegacyBalanceMatching.EnsureMatchAllowedAsync"/> ·
    /// ref = 거래명세서 → 행 잠금 뒤 (P3) 이월잔액 회사의 이관 명세서 거절 · (P2) 남은 금액 초과 거절. 그 밖(ref 없음 등)은 종전대로 검사 없음.
    /// </summary>
    private async Task EnsureCollectionTargetAllowedAsync(CreateCollectionRequest request, string tenantId, IDbTransaction tx, CancellationToken ct)
    {
        if (string.Equals(request.RefDocType, LegacyBalanceMatching.RefType, StringComparison.Ordinal))
        {
            await LegacyBalanceMatching.EnsureMatchAllowedAsync(_db, tx, tenantId, request.PartnerId, request.RefDocId,
                request.Amount, request.CollectionDate, receivable: true, logger: null, ct);
            return;
        }

        if (!string.Equals(request.RefDocType, "sales_delivery", StringComparison.Ordinal) || string.IsNullOrEmpty(request.RefDocId))
            return;

        var doc = await _db.QueryFirstOrDefaultAsync<TargetDoc>(new CommandDefinition(
            $"""
            SELECT sd.partner_id AS PartnerId, sd.total_amount + sd.vat_amount AS TotalWithVat,
                   COALESCE(sd.source_type, '') = 'migration' AND {MdbLegacyPartnerBalance.HasLegacyBalanceSql} AS MigratedUnderLegacy
              FROM sales_deliveries sd
             WHERE sd.tenant_id = @TenantId AND sd.delivery_id = @RefId
               AND sd.is_deleted = 0 AND sd.status IN ('confirmed', 'invoiced')
             FOR UPDATE
            """,
            new { TenantId = tenantId, RefId = request.RefDocId }, transaction: tx, cancellationToken: ct));
        if (doc is null || !string.Equals(doc.PartnerId, request.PartnerId, StringComparison.Ordinal))
            throw new InvalidOperationException(MsgNoDelivery);
        if (doc.MigratedUnderLegacy)
            throw new InvalidOperationException(MsgMigratedDelivery);

        var collected = await _db.ExecuteScalarAsync<decimal>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(amount), 0) FROM collections
             WHERE tenant_id = @TenantId AND is_active = 1 AND ref_doc_type = 'sales_delivery' AND ref_doc_id = @RefId
            """,
            new { TenantId = tenantId, RefId = request.RefDocId }, transaction: tx, cancellationToken: ct));
        var remaining = doc.TotalWithVat - collected;
        if (request.Amount > remaining)
            throw new InvalidOperationException(string.Format(Ko, MsgDeliveryOver, Math.Max(remaining, 0m)));
    }

    /// <summary>지급 등록 검사 — 수금과 대칭. 매입전표 남은 금액 = 공급가+부가세 − 확정 반품 − 활성 매입 지급(<see cref="GetPayablesAsync"/> 와 같은 식).</summary>
    private async Task EnsurePaymentTargetAllowedAsync(CreatePaymentRequest request, string tenantId, IDbTransaction tx, CancellationToken ct)
    {
        if (string.Equals(request.PaymentType, LegacyBalanceMatching.RefType, StringComparison.Ordinal))
        {
            await LegacyBalanceMatching.EnsureMatchAllowedAsync(_db, tx, tenantId, request.PartnerId, request.RefOrderId,
                request.Amount, request.PaymentDate, receivable: false, logger: null, ct);
            return;
        }

        if (!string.Equals(request.PaymentType, "purchase", StringComparison.Ordinal) || string.IsNullOrEmpty(request.RefOrderId))
            return;

        var doc = await _db.QueryFirstOrDefaultAsync<TargetDoc>(new CommandDefinition(
            $"""
            SELECT pr.partner_id AS PartnerId, pr.total_amount + pr.vat_amount AS TotalWithVat,
                   COALESCE(pr.source_type, '') = 'migration' AND {MdbLegacyPartnerBalance.HasLegacyBalanceSql} AS MigratedUnderLegacy
              FROM purchase_receipts pr
             WHERE pr.tenant_id = @TenantId AND pr.receipt_id = @RefId AND pr.status = 'confirmed'
             FOR UPDATE
            """,
            new { TenantId = tenantId, RefId = request.RefOrderId }, transaction: tx, cancellationToken: ct));
        if (doc is null || !string.Equals(doc.PartnerId, request.PartnerId, StringComparison.Ordinal))
            throw new InvalidOperationException(MsgNoReceipt);
        if (doc.MigratedUnderLegacy)
            throw new InvalidOperationException(MsgMigratedReceipt);

        var used = await _db.ExecuteScalarAsync<decimal>(new CommandDefinition(
            """
            SELECT COALESCE((SELECT SUM(amount) FROM payments
                              WHERE tenant_id = @TenantId AND is_active = 1 AND payment_type = 'purchase' AND ref_order_id = @RefId), 0)
                 + COALESCE((SELECT SUM(rti.supply_amount + rti.vat_amount)
                               FROM purchase_returns rt
                               JOIN purchase_return_items rti ON rti.return_id = rt.return_id AND rti.tenant_id = rt.tenant_id
                              WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed' AND rt.receipt_id = @RefId), 0)
            """,
            new { TenantId = tenantId, RefId = request.RefOrderId }, transaction: tx, cancellationToken: ct));
        var remaining = doc.TotalWithVat - used;
        if (request.Amount > remaining)
            throw new InvalidOperationException(string.Format(Ko, MsgReceiptOver, Math.Max(remaining, 0m)));
    }

    private sealed class TargetDoc
    {
        public string PartnerId { get; set; } = string.Empty;
        public decimal TotalWithVat { get; set; }
        public bool MigratedUnderLegacy { get; set; }
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection c) { await c.OpenAsync(ct); return; }
        _db.Open();
    }
}
