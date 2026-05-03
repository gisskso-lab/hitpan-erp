using System.Data;
using Dapper;
using HitPan.Application.Interfaces;
using HitPan.Contracts.Sales;

namespace HitPan.Application.Services;

/// <summary>
/// 세금계산서 1계층 (내부 마킹) 구현 — DESIGN_PRINCIPLES §7 / 작업지시서 20260425작2
///
/// 본 라운드 범위:
///   - 발행 (Issue): 거래명세서(confirmed) → tax_invoices INSERT
///   - 단건/목록 조회
///   - 취소 (Cancel): status=canceled UPDATE만 (역분개·summary 차감은 별도 작업)
///
/// 비범위 (작2 §3):
///   - 2/3계층 (전자세금계산서 즉시/예약 발행)
///   - 취소 시 자동 분개 / monthly_summary 차감
///   - PDF 양식 변경 (기존 PdfExportService 활용)
///
/// 멱등성 2층:
///   1) HTTP 미들웨어 (작4 IdempotencyMiddleware) — Idempotency-Key 헤더 캐싱
///   2) DB UNIQUE (uk_tax_invoices_delivery) — 같은 delivery_id에 두 번 발행 차단
/// </summary>
public sealed class TaxInvoiceService : ITaxInvoiceService
{
    private readonly IDbConnection _db;
    private readonly IUnitOfWork _unitOfWork;

    public TaxInvoiceService(IDbConnection db, IUnitOfWork unitOfWork)
    {
        _db = db;
        _unitOfWork = unitOfWork;
    }

    public async Task<TaxInvoiceResponse> IssueAsync(
        IssueTaxInvoiceRequest request,
        string tenantId,
        string userId,
        string? idempotencyKey,
        CancellationToken ct = default)
    {
        // 1) 거래명세서 검증 (존재 + 동일 테넌트 + confirmed 상태)
        var delivery = await _db.QueryFirstOrDefaultAsync<DeliveryRow>(
            new CommandDefinition(
                """
                SELECT delivery_id    AS DeliveryId,
                       tenant_id      AS TenantId,
                       delivery_no    AS DeliveryNo,
                       partner_id     AS PartnerId,
                       delivery_date  AS DeliveryDate,
                       status         AS Status,
                       total_amount   AS TotalAmount,
                       vat_amount     AS VatAmount
                  FROM sales_deliveries
                 WHERE delivery_id = @DeliveryId AND tenant_id = @TenantId
                """,
                new { DeliveryId = request.DeliveryId, TenantId = tenantId },
                cancellationToken: ct));

        if (delivery is null)
        {
            throw new TaxInvoiceException("delivery_not_found", "거래명세서를 찾을 수 없습니다.");
        }

        if (!string.Equals(delivery.Status, "confirmed", StringComparison.OrdinalIgnoreCase))
        {
            throw new TaxInvoiceException("delivery_not_confirmed", "확정된 거래명세서만 계산서 발행이 가능합니다.");
        }

        // 2) 중복 발행 차단 — DB UNIQUE 보강 (uk_tax_invoices_delivery)
        var existing = await _db.QueryFirstOrDefaultAsync<string?>(
            new CommandDefinition(
                "SELECT invoice_id FROM tax_invoices WHERE delivery_id = @DeliveryId AND status = 'issued' LIMIT 1",
                new { DeliveryId = request.DeliveryId },
                cancellationToken: ct));

        if (existing is not null)
        {
            throw new TaxInvoiceException("already_issued", "이미 계산서가 발행된 거래명세서입니다.");
        }

        // 3) 계산서 번호 생성 (세-yyyyMMdd-NNN 패턴, 테넌트 일자별 순번) — WO-11
        var now = DateTime.UtcNow;
        var prefix = $"세-{now:yyyyMMdd}-";
        var todayCount = await _db.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM tax_invoices WHERE tenant_id = @TenantId AND invoice_no LIKE @Prefix",
                new { TenantId = tenantId, Prefix = prefix + "%" },
                cancellationToken: ct));
        var invoiceNo = $"{prefix}{todayCount + 1:D3}";

        // 4) UoW 트랜잭션 (작5 — INSERT tax_invoices + UPDATE sales_deliveries.tax_invoice_id 동일 tx)
        //    검증팀 BK #1: 역참조가 없으면 거래명세서 화면에서 발행 여부 표시 불가.
        var invoiceId = Guid.NewGuid().ToString();
        using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var conn = _unitOfWork.GetDbConnection();
            var dbTx = tx.DbTransaction;

            // 4-a) tax_invoices INSERT
            await conn.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO tax_invoices
                      (invoice_id, tenant_id, delivery_id, invoice_no,
                       issued_at, issued_by, amount_total, vat_total,
                       status, etax_status, idempotency_key)
                    VALUES
                      (@InvoiceId, @TenantId, @DeliveryId, @InvoiceNo,
                       NOW(6), @IssuedBy, @Amount, @Vat,
                       'issued', 'pending', @IdempotencyKey)
                    """,
                    new
                    {
                        InvoiceId = invoiceId,
                        TenantId = tenantId,
                        DeliveryId = delivery.DeliveryId,
                        InvoiceNo = invoiceNo,
                        IssuedBy = userId,
                        Amount = delivery.TotalAmount,
                        Vat = delivery.VatAmount,
                        IdempotencyKey = idempotencyKey
                    },
                    transaction: dbTx,
                    cancellationToken: ct));

            // 4-b) sales_deliveries.tax_invoice_id 역참조 갱신 (DB-20)
            //   거래명세서 화면에서 "이미 발행됨" 칩 표시용. 둘 중 하나 실패 시 전체 롤백.
            await conn.ExecuteAsync(
                new CommandDefinition(
                    "UPDATE sales_deliveries SET tax_invoice_id = @InvoiceId, updated_at = NOW(6) WHERE delivery_id = @DeliveryId AND tenant_id = @TenantId",
                    new { InvoiceId = invoiceId, DeliveryId = delivery.DeliveryId, TenantId = tenantId },
                    transaction: dbTx,
                    cancellationToken: ct));

            await tx.CommitAsync(ct);
        }
        catch
        {
            try { await tx.RollbackAsync(ct); } catch { /* 이미 닫힌 tx */ }
            throw;
        }

        return new TaxInvoiceResponse(
            InvoiceId: invoiceId,
            TenantId: tenantId,
            DeliveryId: delivery.DeliveryId,
            InvoiceNo: invoiceNo,
            IssuedAt: now,
            IssuedBy: userId,
            AmountTotal: delivery.TotalAmount,
            VatTotal: delivery.VatAmount,
            GrandTotal: delivery.TotalAmount + delivery.VatAmount,
            Status: "issued",
            EtaxStatus: "pending",
            EtaxIssuedAt: null);
    }

    public async Task<TaxInvoiceResponse?> GetAsync(string invoiceId, string tenantId, CancellationToken ct = default)
    {
        var row = await _db.QueryFirstOrDefaultAsync<TaxInvoiceRow>(
            new CommandDefinition(
                """
                SELECT invoice_id    AS InvoiceId,
                       tenant_id     AS TenantId,
                       delivery_id   AS DeliveryId,
                       invoice_no    AS InvoiceNo,
                       issued_at     AS IssuedAt,
                       issued_by     AS IssuedBy,
                       amount_total  AS AmountTotal,
                       vat_total     AS VatTotal,
                       status        AS Status,
                       etax_status   AS EtaxStatus,
                       etax_issued_at AS EtaxIssuedAt
                  FROM tax_invoices
                 WHERE invoice_id = @InvoiceId AND tenant_id = @TenantId
                """,
                new { InvoiceId = invoiceId, TenantId = tenantId },
                cancellationToken: ct));

        if (row is null) return null;
        return ToResponse(row);
    }

    public async Task<List<TaxInvoiceListItem>> ListAsync(
        string tenantId,
        DateTime? from,
        DateTime? to,
        string? partnerId,
        CancellationToken ct = default)
    {
        var sql = """
            SELECT ti.invoice_id  AS InvoiceId,
                   ti.delivery_id AS DeliveryId,
                   sd.delivery_no AS DeliveryNo,
                   ti.invoice_no  AS InvoiceNo,
                   ti.issued_at   AS IssuedAt,
                   sd.partner_id  AS PartnerId,
                   COALESCE(p.partner_name, '') AS PartnerName,
                   ti.amount_total AS AmountTotal,
                   ti.vat_total    AS VatTotal,
                   ti.status       AS Status,
                   ti.etax_status  AS EtaxStatus
              FROM tax_invoices ti
              JOIN sales_deliveries sd ON sd.delivery_id = ti.delivery_id
              LEFT JOIN partners p ON p.partner_id = sd.partner_id
             WHERE ti.tenant_id = @TenantId
               AND (@From IS NULL OR ti.issued_at >= @From)
               AND (@To   IS NULL OR ti.issued_at <  @ToEnd)
               AND (@PartnerId IS NULL OR sd.partner_id = @PartnerId)
             ORDER BY ti.issued_at DESC
             LIMIT 1000
            """;

        var result = await _db.QueryAsync<TaxInvoiceListItem>(
            new CommandDefinition(
                sql,
                new
                {
                    TenantId = tenantId,
                    From = from,
                    To = to,
                    ToEnd = to?.AddDays(1),  // 일자 포함 검색
                    PartnerId = partnerId
                },
                cancellationToken: ct));

        return result.AsList();
    }

    public async Task<CancelTaxInvoiceResponse> CancelAsync(
        string invoiceId,
        CancelTaxInvoiceRequest request,
        string tenantId,
        string userId,
        CancellationToken ct = default)
    {
        var existing = await _db.QueryFirstOrDefaultAsync<string?>(
            new CommandDefinition(
                "SELECT status FROM tax_invoices WHERE invoice_id = @InvoiceId AND tenant_id = @TenantId",
                new { InvoiceId = invoiceId, TenantId = tenantId },
                cancellationToken: ct));

        if (existing is null)
        {
            throw new TaxInvoiceException("not_found", "계산서를 찾을 수 없습니다.");
        }
        if (string.Equals(existing, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            throw new TaxInvoiceException("already_canceled", "이미 취소된 계산서입니다.");
        }

        // UoW 트랜잭션 (작5 — UPDATE tax_invoices.status='canceled' + UPDATE sales_deliveries.tax_invoice_id=NULL 동일 tx)
        //   역참조 환원 → 같은 거래명세서를 다시 발행 가능 (uk_tax_invoices_delivery는 issued만 차단하지 않음 → 별도 라운드 보강)
        //   역분개·summary 차감은 별도 라운드 (4프로토콜 #4 쪼개기, 작2 §3 비범위)
        var now = DateTime.UtcNow;
        using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var conn = _unitOfWork.GetDbConnection();
            var dbTx = tx.DbTransaction;

            await conn.ExecuteAsync(
                new CommandDefinition(
                    "UPDATE tax_invoices SET status = 'canceled', updated_at = NOW(6) WHERE invoice_id = @InvoiceId AND tenant_id = @TenantId",
                    new { InvoiceId = invoiceId, TenantId = tenantId },
                    transaction: dbTx,
                    cancellationToken: ct));

            // sales_deliveries 역참조 환원 (DB-20)
            await conn.ExecuteAsync(
                new CommandDefinition(
                    "UPDATE sales_deliveries SET tax_invoice_id = NULL, updated_at = NOW(6) WHERE tax_invoice_id = @InvoiceId AND tenant_id = @TenantId",
                    new { InvoiceId = invoiceId, TenantId = tenantId },
                    transaction: dbTx,
                    cancellationToken: ct));

            await tx.CommitAsync(ct);
        }
        catch
        {
            try { await tx.RollbackAsync(ct); } catch { /* 이미 닫힌 tx */ }
            throw;
        }

        return new CancelTaxInvoiceResponse(invoiceId, now, "canceled");
    }

    private static TaxInvoiceResponse ToResponse(TaxInvoiceRow r) =>
        new(
            InvoiceId: r.InvoiceId,
            TenantId: r.TenantId,
            DeliveryId: r.DeliveryId,
            InvoiceNo: r.InvoiceNo,
            IssuedAt: r.IssuedAt,
            IssuedBy: r.IssuedBy,
            AmountTotal: r.AmountTotal,
            VatTotal: r.VatTotal,
            GrandTotal: r.AmountTotal + r.VatTotal,
            Status: r.Status,
            EtaxStatus: r.EtaxStatus,
            EtaxIssuedAt: r.EtaxIssuedAt);

    // === 내부 row 모델 ==========================================
    private sealed record DeliveryRow(
        string DeliveryId,
        string TenantId,
        string DeliveryNo,
        string PartnerId,
        DateTime DeliveryDate,
        string Status,
        decimal TotalAmount,
        decimal VatAmount);

    private sealed record TaxInvoiceRow(
        string InvoiceId,
        string TenantId,
        string DeliveryId,
        string InvoiceNo,
        DateTime IssuedAt,
        string IssuedBy,
        decimal AmountTotal,
        decimal VatTotal,
        string Status,
        string EtaxStatus,
        DateTime? EtaxIssuedAt);
}

/// <summary>
/// 계산서 발행/취소 시 비즈니스 규칙 위반을 알리는 예외.
/// 컨트롤러가 ErrorCode → HTTP 상태 매핑 (400/404/409).
/// </summary>
public sealed class TaxInvoiceException : Exception
{
    public string ErrorCode { get; }

    public TaxInvoiceException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}
