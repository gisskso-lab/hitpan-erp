using System.Data;
using Dapper;
using HitPan.Application.Common;
using HitPan.Application.DTOs.Sales;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>
/// 견적서 비즈니스 로직을 처리한다.
/// </summary>
public class QuotationService : IQuotationService
{
    // Dapper 쿼리 실행을 위한 DB 연결이다.
    private readonly IDbConnection _db;

    // 전표 작성자(created_by) 를 채우기 위한 현재 로그인 정보다 (20260825작5).
    private readonly ICurrentTenant _currentTenant;

    /// <summary>
    /// 생성자를 초기화한다.
    /// </summary>
    /// <param name="db">DB 연결 객체다.</param>
    /// <param name="currentTenant">현재 로그인한 계정 정보다.</param>
    public QuotationService(IDbConnection db, ICurrentTenant currentTenant)
    {
        _db = db;
        _currentTenant = currentTenant;
    }

    /// <inheritdoc />
    public async Task<List<QuotationListDto>> GetListAsync(
        string tenantId,
        DateTime? from = null,
        DateTime? to = null,
        string? partnerName = null,
        string? status = null,
        CancellationToken ct = default)
    {
        const string sql = """
                           SELECT
                               q.quote_id AS QuoteId,
                               q.quote_no AS QuoteNo,
                               q.partner_id AS PartnerId,
                               p.partner_name AS PartnerName,
                               q.quote_date AS QuoteDate,
                               q.valid_until AS ValidUntil,
                               q.status AS Status,
                               q.total_amount AS TotalAmount,
                               q.vat_amount AS VatAmount,
                               ec.emp_name AS CreatedByName
                           FROM quotations q
                           LEFT JOIN partners p
                               ON p.partner_id = q.partner_id
                              AND p.tenant_id = q.tenant_id
                           LEFT JOIN employees ec
                               ON ec.user_id = q.created_by
                              AND ec.tenant_id = q.tenant_id
                           WHERE q.tenant_id = @TenantId
                             AND q.is_deleted = 0
                             AND (@From IS NULL OR q.quote_date >= @From)
                             AND (@To IS NULL OR q.quote_date <= @To)
                             AND (@PartnerName IS NULL OR p.partner_name LIKE CONCAT('%', @PartnerName, '%'))
                             AND (@Status IS NULL OR q.status = @Status)
                           ORDER BY q.quote_date DESC, q.quote_no DESC
                           LIMIT 200
                           """;

        var rows = await _db.QueryAsync<QuotationListDto>(
            new CommandDefinition(
                sql,
                new
                {
                    TenantId = tenantId,
                    From = from?.Date,
                    To = to?.Date,
                    PartnerName = partnerName,
                    Status = status
                },
                cancellationToken: ct));

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<QuotationDetailDto?> GetAsync(string tenantId, string quoteId, CancellationToken ct = default)
    {
        const string headerSql = """
                                 SELECT
                                     q.quote_id AS QuoteId,
                                     q.tenant_id AS TenantId,
                                     q.quote_no AS QuoteNo,
                                     q.partner_id AS PartnerId,
                                     p.partner_name AS PartnerName,
                                     q.employee_id AS EmployeeId,
                                     q.quote_date AS QuoteDate,
                                     q.valid_until AS ValidUntil,
                                     q.status AS Status,
                                     q.total_amount AS TotalAmount,
                                     q.vat_amount AS VatAmount,
                                     q.memo AS Memo,
                                     q.converted_order_id AS ConvertedOrderId,
                                     q.created_at AS CreatedAt,
                                     q.created_by AS CreatedBy,
                                     q.updated_at AS UpdatedAt,
                                     q.updated_by AS UpdatedBy
                                 FROM quotations q
                                 LEFT JOIN partners p
                                     ON p.partner_id = q.partner_id
                                    AND p.tenant_id = q.tenant_id
                                 WHERE q.tenant_id = @TenantId
                                   AND q.quote_id = @QuoteId
                                   AND q.is_deleted = 0
                                 """;

        var detail = await _db.QueryFirstOrDefaultAsync<QuotationDetailDto>(
            new CommandDefinition(headerSql, new { TenantId = tenantId, QuoteId = quoteId }, cancellationToken: ct));
        if (detail is null)
        {
            return null;
        }

        const string itemSql = """
                               SELECT
                                   qi.item_id AS ItemId,
                                   i.item_name AS ItemName,
                                   qi.spec AS Spec,
                                   qi.unit AS Unit,
                                   qi.qty AS Qty,
                                   qi.unit_price AS UnitPrice,
                                   qi.discount_rate AS DiscountRate,
                                   qi.amount AS Amount,
                                   qi.vat_amount AS VatAmount,
                                   qi.memo AS Memo,
                                   qi.sort_order AS SortOrder
                               FROM quotation_items qi
                               LEFT JOIN items i
                                   ON i.item_id = qi.item_id
                                  AND i.tenant_id = @TenantId
                               WHERE qi.quote_id = @QuoteId
                               ORDER BY qi.sort_order, qi.id
                               """;

        detail.Items = (await _db.QueryAsync<QuotationItemDto>(
            new CommandDefinition(itemSql, new { TenantId = tenantId, QuoteId = quoteId }, cancellationToken: ct))).ToList();
        return detail;
    }

    /// <inheritdoc />
    public async Task<string> CreateAsync(string tenantId, CreateQuotationRequest request, CancellationToken ct = default)
    {
        ValidateRequest(request.Items);

        var quoteDate = request.QuoteDate == default ? DateTime.UtcNow.Date : request.QuoteDate.Date;
        var quoteId = Guid.NewGuid().ToString();
        var quoteNo = await GenerateQuoteNoAsync(tenantId, quoteDate, ct);
        var totalAmount = request.Items.Sum(x => x.Amount);
        var vatAmount = request.Items.Sum(x => x.VatAmount);

        const string insertHeaderSql = """
                                       INSERT INTO quotations
                                       (
                                           quote_id, tenant_id, quote_no, partner_id, employee_id,
                                           quote_date, valid_until, status, total_amount, vat_amount, memo,
                                           converted_order_id, created_at, created_by, updated_at, updated_by,
                                           is_deleted, deleted_at
                                       )
                                       VALUES
                                       (
                                           @QuoteId, @TenantId, @QuoteNo, @PartnerId, @EmployeeId,
                                           @QuoteDate, @ValidUntil, 'draft', @TotalAmount, @VatAmount, @Memo,
                                           NULL, NOW(6), @CreatedBy, NOW(6), @CreatedBy, 0, NULL
                                       )
                                       """;

        await _db.ExecuteAsync(new CommandDefinition(
            insertHeaderSql,
            new
            {
                QuoteId = quoteId,
                TenantId = tenantId,
                QuoteNo = quoteNo,
                request.PartnerId,
                request.EmployeeId,
                QuoteDate = quoteDate,
                request.ValidUntil,
                TotalAmount = totalAmount,
                VatAmount = vatAmount,
                request.Memo,
                // 20260825작5 봉합: 종전에는 employee_id 를 넣었는데, 현황·순위표·분석의 사원별
                // 집계가 e2.user_id = q.created_by 로 조인해 절대 매칭되지 않았다(견적 0행이라
                // 표면화만 안 됐다). 사장님 결재 — created_by 는 user_id 체계로 통일한다.
                CreatedBy = _currentTenant.UserId
            },
            cancellationToken: ct));

        await InsertItemsAsync(quoteId, request.Items, ct);
        return quoteId;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(string tenantId, string quoteId, UpdateQuotationRequest request, CancellationToken ct = default)
    {
        ValidateRequest(request.Items);
        await EnsureExistsAsync(tenantId, quoteId, ct);

        var quoteDate = request.QuoteDate == default ? DateTime.UtcNow.Date : request.QuoteDate.Date;
        var totalAmount = request.Items.Sum(x => x.Amount);
        var vatAmount = request.Items.Sum(x => x.VatAmount);
        var normalizedStatus = NormalizeStatus(request.Status);

        const string updateHeaderSql = """
                                       UPDATE quotations
                                       SET partner_id = @PartnerId,
                                           employee_id = @EmployeeId,
                                           quote_date = @QuoteDate,
                                           valid_until = @ValidUntil,
                                           status = @Status,
                                           total_amount = @TotalAmount,
                                           vat_amount = @VatAmount,
                                           memo = @Memo,
                                           updated_at = NOW(6),
                                           updated_by = @UpdatedBy
                                       WHERE tenant_id = @TenantId
                                         AND quote_id = @QuoteId
                                         AND is_deleted = 0
                                       """;

        await _db.ExecuteAsync(new CommandDefinition(
            updateHeaderSql,
            new
            {
                TenantId = tenantId,
                QuoteId = quoteId,
                request.PartnerId,
                request.EmployeeId,
                QuoteDate = quoteDate,
                request.ValidUntil,
                Status = normalizedStatus,
                TotalAmount = totalAmount,
                VatAmount = vatAmount,
                request.Memo,
                // 20260825작5: created_by 와 같은 체계(user_id)로 맞춘다.
                UpdatedBy = _currentTenant.UserId
            },
            cancellationToken: ct));

        const string deleteItemsSql = """
                                      DELETE FROM quotation_items
                                      WHERE quote_id = @QuoteId
                                      """;
        await _db.ExecuteAsync(new CommandDefinition(deleteItemsSql, new { QuoteId = quoteId }, cancellationToken: ct));

        await InsertItemsAsync(quoteId, request.Items, ct);
    }

    /// <inheritdoc />
    /// <summary>
    /// 견적서를 삭제한다. 전환 완료된 문서는 삭제할 수 없다.
    /// </summary>
    public async Task DeleteAsync(string tenantId, string quoteId, CancellationToken ct = default)
    {
        // 전환된 문서 삭제 차단
        var status = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            "SELECT status FROM quotations WHERE tenant_id=@TenantId AND quote_id=@QuoteId AND is_deleted=0",
            new { TenantId = tenantId, QuoteId = quoteId }, cancellationToken: ct));
        if (status == "converted")
        {
            throw new InvalidOperationException("수주로 전환된 견적서는 삭제할 수 없습니다.");
        }

        const string sql = """
                           UPDATE quotations
                           SET is_deleted = 1,
                               deleted_at = NOW(6),
                               updated_at = NOW(6)
                           WHERE tenant_id = @TenantId
                             AND quote_id = @QuoteId
                             AND is_deleted = 0
                           """;

        var affected = await _db.ExecuteAsync(
            new CommandDefinition(sql, new { TenantId = tenantId, QuoteId = quoteId }, cancellationToken: ct));
        if (affected == 0)
        {
            throw new InvalidOperationException("삭제할 견적서를 찾을 수 없습니다.");
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// 견적서를 수주서로 전환한다. 이미 전환된 견적은 중복 전환을 차단한다.
    /// </summary>
    public async Task<string> ConvertToSalesOrderAsync(string tenantId, string quoteId, CancellationToken ct = default)
    {
        var quote = await GetAsync(tenantId, quoteId, ct)
                    ?? throw new InvalidOperationException("견적서를 찾을 수 없습니다.");

        // 이미 전환된 견적서는 중복 전환 차단 (사장님: "사슬동작중 중복생성 절대금지")
        if (quote.Status == "converted")
        {
            // 🔴 20260827작10 W2 — converted_order_id 가 이제 order_id(UUID)라
            //   그대로 보여주면 사장님이 UUID 를 받는다. **번호로 되짚어 보여준다.**
            //   작8 교훈: 막는 것 ≠ 알려주는 것. 막으면서 "어느 수주로 갔는지" 를 줘야 한다.
            var convertedNo = await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
                "SELECT order_no FROM sales_orders WHERE order_id=@Oid AND tenant_id=@Tid",
                new { Oid = quote.ConvertedOrderId, Tid = tenantId }, cancellationToken: ct));

            // 못 찾으면(구 데이터라 번호가 그대로 들어있는 경우) 있는 값을 보여준다 — 빈 손으로 돌려보내지 않는다.
            throw new InvalidOperationException(
                $"이미 수주로 전환된 견적서입니다. (전환번호: {convertedNo ?? quote.ConvertedOrderId})");
        }

        // 수주 문서번호 채번 — WO-11 한글 prefix
        // 🔴 20260827작9 W2 — COUNT+1 → MAX+1 (위 GenerateQuoteNoAsync 와 같은 이유).
        // 🔴 W2-b — DateTime.UtcNow → BusinessDate.Today. KST 09시 이전에는 UTC 가 전날이라
        //   8/27 오전에 전환하면 '수-20260826-…' 이 나와 전표 일자와 번호 날짜가 어긋났다.
        //   매입은 이미 BusinessDate 를 쓴다(작18 W4). 매출만 UtcNow 로 남아 있었다.
        var today = BusinessDate.Today;
        var prefix = $"수-{today:yyyyMMdd}-";
        var orderNo = await DocumentNumberHelper.NextNumberAsync(
            _db, tenantId, "sales_orders", "order_no", prefix, ct);
        var orderId = Guid.NewGuid().ToString();

        // 수주 헤더 생성
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO sales_orders (order_id, tenant_id, order_no, partner_id, employee_id,
              order_date, delivery_date, status, total_amount, vat_amount, memo,
              quotation_id, created_at, updated_at)
            VALUES (@Id, @TenantId, @OrderNo, @PartnerId, NULL,
              @OrderDate, NULL, 'draft', @TotalAmount, @VatAmount, @Memo,
              @QuotationId, NOW(6), NOW(6))
            """,
            new
            {
                Id = orderId, TenantId = tenantId, OrderNo = orderNo,
                PartnerId = quote.PartnerId, OrderDate = today,
                TotalAmount = quote.TotalAmount, VatAmount = quote.VatAmount,

                // 🔴 20260827작10 W2 — memo 는 사람이 읽는 흔적으로 그대로 두고,
                //   기계가 되짚는 축은 quotation_id 컬럼으로 따로 둔다(DB-114).
                //   종전엔 이 memo 가 유일한 연결이었는데, 수주서를 수정하면 화면 값으로
                //   덮어써져 **연결이 조용히 소멸**했다.
                Memo = $"견적서 전환: {quote.QuoteNo}",
                QuotationId = quoteId
            }, cancellationToken: ct));

        // 수주 품목 생성
        foreach (var item in quote.Items)
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO sales_order_items (order_item_id, order_id, tenant_id,
                  item_id, ordered_qty, delivered_qty, unit_price, supply_amount, vat_amount, item_status)
                VALUES (UUID(), @OrderId, @TenantId,
                  @ItemId, @Qty, 0, @UnitPrice, @Amount, @VatAmount, 'pending')
                """,
                new
                {
                    OrderId = orderId, TenantId = tenantId,
                    ItemId = item.ItemId, Qty = item.Qty,
                    UnitPrice = item.UnitPrice, Amount = item.Amount, VatAmount = item.VatAmount
                }, cancellationToken: ct));
        }

        // 견적서 상태 업데이트
        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE quotations SET status = 'converted', converted_order_id = @OrderId, updated_at = NOW(6)
            WHERE tenant_id = @TenantId AND quote_id = @QuoteId AND is_deleted = 0
            """,
            // 🔴 20260827작10 W2 — 컬럼명이 converted_order_**id** 인데 종전엔 order_**no**(번호)를 넣었다.
            //   varchar(36) 이라 문자열이 그냥 들어가 에러가 안 났고, 이 값으로 JOIN 하는 코드도 없어
            //   아무도 못 봤다. 이제 id 를 넣는다 — 기존 행은 DB-114 가 되짚어 치환한다.
            new { TenantId = tenantId, QuoteId = quoteId, OrderId = orderId },
            cancellationToken: ct));

        return orderNo;
    }

    /// <summary>
    /// 품목 목록 유효성을 검증한다.
    /// </summary>
    /// <param name="items">검증할 품목 목록이다.</param>
    private static void ValidateRequest(List<QuotationItemDto> items)
    {
        if (items.Count == 0)
        {
            throw new InvalidOperationException("품목이 한 줄 이상 필요합니다.");
        }
    }

    /// <summary>
    /// 견적서 존재 여부를 검증한다.
    /// </summary>
    /// <param name="tenantId">테넌트 식별자다.</param>
    /// <param name="quoteId">견적서 식별자다.</param>
    /// <param name="ct">취소 토큰이다.</param>
    private async Task EnsureExistsAsync(string tenantId, string quoteId, CancellationToken ct)
    {
        const string sql = """
                           SELECT COUNT(1)
                           FROM quotations
                           WHERE tenant_id = @TenantId
                             AND quote_id = @QuoteId
                             AND is_deleted = 0
                           """;
        var exists = await _db.QuerySingleAsync<int>(
            new CommandDefinition(sql, new { TenantId = tenantId, QuoteId = quoteId }, cancellationToken: ct));
        if (exists == 0)
        {
            throw new InvalidOperationException("견적서를 찾을 수 없습니다.");
        }
    }

    /// <summary>
    /// 견적서 번호를 자동 채번한다.
    /// </summary>
    /// <param name="tenantId">테넌트 식별자다.</param>
    /// <param name="quoteDate">견적일자다.</param>
    /// <param name="ct">취소 토큰이다.</param>
    /// <returns>자동 채번된 견적번호다.</returns>
    private async Task<string> GenerateQuoteNoAsync(string tenantId, DateTime quoteDate, CancellationToken ct)
    {
        // WO-11: 한글 prefix 통일 (견적서 = 견-)
        // 🔴 20260827작9 W2 — COUNT+1 → MAX+1(DocumentNumberHelper).
        //   DocumentNumberHelper 주석이 COUNT+1 을 "진범"으로 지목했다
        //   (동시 호출 시 같은 번호 → UNIQUE 충돌 → 4/28 자동사슬 174건 중 71건 원장누락).
        //   여기서 더 큰 위험은 소프트삭제다 — COUNT 는 지워진 행만큼 줄어서
        //   '견-20260827-003' 을 지우면 다음 채번이 -003 을 다시 낸다.
        //   MAX+1 은 지워진 번호를 건너뛴다. 사장님: "사슬동작중 중복생성 절대금지".
        var prefix = $"견-{quoteDate:yyyyMMdd}-";
        return await DocumentNumberHelper.NextNumberAsync(
            _db, tenantId, "quotations", "quote_no", prefix, ct);
    }

    /// <summary>
    /// 견적서 품목을 일괄 삽입한다.
    /// </summary>
    /// <param name="quoteId">견적서 식별자다.</param>
    /// <param name="items">품목 목록이다.</param>
    /// <param name="ct">취소 토큰이다.</param>
    private async Task InsertItemsAsync(string quoteId, List<QuotationItemDto> items, CancellationToken ct)
    {
        const string insertItemSql = """
                                     INSERT INTO quotation_items
                                     (
                                         id, quote_id, item_id, spec, unit, qty, unit_price,
                                         discount_rate, amount, vat_amount, memo, sort_order
                                     )
                                     VALUES
                                     (
                                         @Id, @QuoteId, @ItemId, @Spec, @Unit, @Qty, @UnitPrice,
                                         @DiscountRate, @Amount, @VatAmount, @Memo, @SortOrder
                                     )
                                     """;

        var row = 0;
        foreach (var item in items)
        {
            row++;
            var sortOrder = item.SortOrder <= 0 ? row : item.SortOrder;
            await _db.ExecuteAsync(new CommandDefinition(
                insertItemSql,
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    QuoteId = quoteId,
                    ItemId = item.ItemId,
                    item.Spec,
                    item.Unit,
                    item.Qty,
                    item.UnitPrice,
                    item.DiscountRate,
                    item.Amount,
                    item.VatAmount,
                    item.Memo,
                    SortOrder = sortOrder
                },
                cancellationToken: ct));
        }
    }

    /// <summary>
    /// 상태 문자열을 허용 가능한 값으로 정규화한다.
    /// </summary>
    /// <param name="status">요청 상태값이다.</param>
    /// <returns>정규화된 상태 문자열이다.</returns>
    private static string NormalizeStatus(string? status)
    {
        var normalized = (status ?? "draft").Trim().ToLowerInvariant();
        return normalized switch
        {
            "draft" => "draft",
            "submitted" => "submitted",
            "accepted" => "accepted",
            "rejected" => "rejected",
            "expired" => "expired",
            "converted" => "converted",
            _ => "draft"
        };
    }
}
