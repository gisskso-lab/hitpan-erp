using System.Data;
using Dapper;
using HitPan.Application.Common;
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
/// 멱등성:
///   1) HTTP 미들웨어 (작4 IdempotencyMiddleware) — Idempotency-Key 헤더 캐싱
///   2) IssueAsync 의 SELECT 체크 — 같은 delivery_id 에 두 번 발행 차단
///
/// 🔴 정정 (20260827작11): 종전 주석은 "DB UNIQUE (uk_tax_invoices_delivery)" 가
///   2층을 맡는다고 적었으나 **DDL 에 그런 UNIQUE 는 없다.** 16차에 넣으려다
///   MariaDB 가 FK 컬럼 참조 STORED generated 표현식을 금지해 ERROR 1901 로 회수됐다
///   (installer/hitpan_db_clean.sql 의 tax_invoices 주석에 사고 이력 기록).
///   ⇒ **실제 방어는 SELECT 체크 하나뿐이다.** 없는 UNIQUE 를 믿고 약하게 두면 안 된다.
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

        // 2) 중복 발행 차단 (사장님: "사슬동작중 중복생성 절대금지")
        //
        // 🔴 20260827작11 W2 — 이 가드에 결함이 세 개 있었다.
        //
        //   ① tenant_id 조건이 없었다. 남의 회사 계산서가 걸려 우리 발행이 막히거나,
        //      반대로 우리 것이 안 걸릴 수 있었다(헌법 #2 테넌트 격리).
        //   ② 번호를 안 알려줬다 — "이미 발행됨" 만 말하니 담당자가 계산서 목록에서
        //      눈으로 찾아야 했다(작8 교훈: 막는 것 ≠ 알려주는 것).
        //   ③ 위 주석이 DB UNIQUE(uk_tax_invoices_delivery)가 있다고 적어놨는데
        //      **DDL 에 그런 UNIQUE 는 없다.** 16차에 넣으려다 MariaDB 가 FK 컬럼 참조
        //      STORED generated 를 금지해 ERROR 1901 로 회수됐다(clean DDL 주석에 기록).
        //      ⇒ 실제 방어는 이 SELECT 하나뿐이다. 주석을 믿고 약하게 두면 안 된다.
        var existing = await _db.QueryFirstOrDefaultAsync<string?>(
            new CommandDefinition(
                """
                SELECT invoice_no FROM tax_invoices
                 WHERE delivery_id = @DeliveryId AND tenant_id = @TenantId
                   AND status = 'issued'
                 ORDER BY invoice_no
                 LIMIT 1
                """,
                new { DeliveryId = request.DeliveryId, TenantId = tenantId },
                cancellationToken: ct));

        if (existing is not null)
        {
            throw new TaxInvoiceException("already_issued",
                $"이미 계산서({existing})가 발행된 거래명세서입니다.");
        }

        // 3) 계산서 번호 생성 (세-yyyyMMdd-NNN 패턴, 테넌트 일자별 순번) — WO-11
        // 🔴 20260827작9 W2 — COUNT+1 → MAX+1(DocumentNumberHelper).
        //   COUNT+1 은 동시 발행 시 같은 번호를 내고(uk_tax_invoices_invoice_no 충돌),
        //   취소·삭제분이 생기면 이미 쓴 번호를 재발급한다.
        //   세금계산서는 국세청에 나가는 번호라 중복이 특히 위험하다.
        // 🔴 W2-b — 채번 날짜만 업무일(KST). KST 09시 이전 발행이 전날 번호를 받았다.
        //   ⚠️ 저장 시각(issued_at)은 UTC 그대로 둔다 — 시각은 UTC 로 쌓는 것이 맞고,
        //      여기서 같이 바꾸면 DB 에 +9h 틀어진 시각이 들어간다. 날짜와 시각은 다른 축이다.
        var now = DateTime.UtcNow;
        var prefix = $"세-{BusinessDate.Today:yyyyMMdd}-";
        var invoiceNo = await DocumentNumberHelper.NextNumberAsync(
            _db, tenantId, "tax_invoices", "invoice_no", prefix, ct);

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
        catch (Exception)
        {
            try { await tx.RollbackAsync(ct); } catch (Exception rbex) { Console.Error.WriteLine($"[TaxInvoiceService] rollback failed: {rbex.Message}"); }
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
        // 봉합 (2026-06-23, 16차 P0-1): 종전엔 tax_invoices.partner_id 를 SELECT 했으나 그 컬럼이
        //   존재하지 않아(DDL은 partner_code(int)만 보유) 신규설치 DB에서 계산서 취소 시 "Unknown column
        //   'partner_id'" 500 으로 취소 기능 전체가 마비됐다(헌법 #20·#36). partner_id 는 발행(IssueAsync:50)이
        //   sales_deliveries 에서 읽어 기표하던 것과 동일하게, tax_invoices.delivery_id → sales_deliveries JOIN
        //   으로 얻는다(DDL 무변경, FK fk_tax_invoices_delivery 이미 존재). 역분개의 partner 라인 정합 유지.
        var invoiceRow = await _db.QueryFirstOrDefaultAsync<(string? Status, string? InvoiceNo, string? PartnerId, DateTime IssuedAt, decimal AmountTotal, decimal VatTotal)>(
            new CommandDefinition(
                """
                SELECT ti.status AS Status, ti.invoice_no AS InvoiceNo, sd.partner_id AS PartnerId,
                       ti.issued_at AS IssuedAt, ti.amount_total AS AmountTotal, ti.vat_total AS VatTotal
                  FROM tax_invoices ti
                  LEFT JOIN sales_deliveries sd
                    ON sd.delivery_id = ti.delivery_id AND sd.tenant_id = ti.tenant_id
                 WHERE ti.invoice_id = @InvoiceId AND ti.tenant_id = @TenantId
                """,
                new { InvoiceId = invoiceId, TenantId = tenantId },
                cancellationToken: ct));

        if (invoiceRow.Status is null)
        {
            throw new TaxInvoiceException("not_found", "계산서를 찾을 수 없습니다.");
        }
        if (string.Equals(invoiceRow.Status, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            throw new TaxInvoiceException("already_canceled", "이미 취소된 계산서입니다.");
        }

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

            // 역분개 — IssueAsync 기표의 차/대변 반전 (INSERT ONLY 원칙)
            // 원분개: 차변 외상매출금(total) / 대변 매출(supply) + 부가세예수금(vat)
            // 역분개: 차변 매출(supply) + 부가세예수금(vat) / 대변 외상매출금(total)
            if (invoiceRow.AmountTotal != 0m || invoiceRow.VatTotal != 0m)
            {
                await AutoJournalHelper.RecordSalesCancelAsync(
                    conn, dbTx!,
                    tenantId,
                    invoiceId,
                    invoiceRow.InvoiceNo ?? invoiceId,
                    invoiceRow.IssuedAt,
                    invoiceRow.PartnerId,
                    invoiceRow.AmountTotal,
                    invoiceRow.VatTotal,
                    userId,
                    ct);
            }

            await tx.CommitAsync(ct);
        }
        catch (Exception)
        {
            try { await tx.RollbackAsync(ct); } catch (Exception rbex) { Console.Error.WriteLine($"[TaxInvoiceService] rollback failed: {rbex.Message}"); }
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
