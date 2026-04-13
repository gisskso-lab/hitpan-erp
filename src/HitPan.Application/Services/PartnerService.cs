using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Partner;
using HitPan.Application.DTOs.Sales;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

public sealed class PartnerService : IPartnerService
{
    private readonly ICurrentTenant _currentTenant;
    private readonly IPartnerBalanceRepository _partnerBalanceRepository;
    private readonly IDbConnection _db;

    public PartnerService(
        ICurrentTenant currentTenant,
        IPartnerBalanceRepository partnerBalanceRepository,
        IDbConnection db)
    {
        _currentTenant = currentTenant;
        _partnerBalanceRepository = partnerBalanceRepository;
        _db = db;
    }

    public Task<PartnerBalanceDto?> GetBalanceAsync(string partnerId, CancellationToken ct = default)
    {
        return _partnerBalanceRepository.GetBalanceAsync(_currentTenant.TenantId, partnerId, ct);
    }

    public async Task<List<SpecialPriceItemDto>> GetSpecialPricesAsync(string partnerId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
                           SELECT p.id AS Id,
                                  p.tenant_id AS TenantId,
                                  p.partner_id AS PartnerId,
                                  pt.partner_name AS PartnerName,
                                  p.item_id AS ItemId,
                                  i.item_name AS ItemName,
                                  p.spec,
                                  p.unit,
                                  p.special_price AS SpecialPrice,
                                  p.std_price AS StdPrice,
                                  p.vs_ratio AS VsRatio,
                                  p.last_supply_date AS LastSupplyDate,
                                  p.is_active AS IsActive
                           FROM partner_special_prices p
                           LEFT JOIN partners pt ON pt.partner_id = p.partner_id
                           LEFT JOIN items i ON i.item_id = p.item_id
                           WHERE p.partner_id = @PartnerId
                             AND p.tenant_id = @TenantId
                             AND p.is_active = 1
                           ORDER BY i.item_name
                           """;

        var rows = await _db.QueryAsync<SpecialPriceItemDto>(new CommandDefinition(
            sql,
            new { PartnerId = partnerId, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    public async Task UpsertSpecialPriceAsync(string partnerId, SpecialPriceUpsertDto dto, string tenantId, string userId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var rowId = Guid.NewGuid().ToString();
        const string sql = """
                           INSERT INTO partner_special_prices
                             (id, tenant_id, partner_id, item_id,
                              spec, unit, special_price,
                              std_price, last_supply_date,
                              price_type, unit_price, start_date, end_date,
                              is_active, created_by, updated_by, created_at, updated_at)
                           VALUES
                             (@Id, @TenantId, @PartnerId, @ItemId,
                              @Spec, @Unit, @SpecialPrice,
                              @StdPrice, @LastSupplyDate,
                              'fixed', @SpecialPrice, NULL, NULL,
                              1, @UserId, @UserId, NOW(6), NOW(6))
                           ON DUPLICATE KEY UPDATE
                             special_price    = @SpecialPrice,
                             unit_price       = @SpecialPrice,
                             std_price        = @StdPrice,
                             spec             = @Spec,
                             unit             = @Unit,
                             last_supply_date = @LastSupplyDate,
                             updated_by       = @UserId,
                             updated_at       = NOW(6)
                           """;

        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                Id = rowId,
                TenantId = tenantId,
                PartnerId = partnerId,
                ItemId = dto.ItemId,
                Spec = dto.Spec,
                Unit = dto.Unit,
                SpecialPrice = dto.SpecialPrice,
                StdPrice = dto.StdPrice,
                LastSupplyDate = dto.LastSupplyDate,
                UserId = userId
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task DeleteSpecialPriceAsync(string partnerId, string itemId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
                           UPDATE partner_special_prices
                           SET is_active = 0,
                               updated_at = NOW(6)
                           WHERE partner_id = @PartnerId
                             AND item_id = @ItemId
                             AND tenant_id = @TenantId
                           """;

        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new { PartnerId = partnerId, ItemId = itemId, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public Task<bool> IsAssignedPartnerAsync(string? employeeId, string partnerId, string tenantId, CancellationToken ct = default)
    {
        return Task.FromResult(true);
    }

    public async Task<List<PartnerSearchDto>> SearchPartnersAsync(string tenantId, string keyword, CancellationToken ct = default)
    {
        keyword = keyword?.Trim() ?? string.Empty;
        if (keyword.Length == 0)
        {
            return new List<PartnerSearchDto>();
        }

        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
                           SELECT partner_id AS PartnerId,
                                  partner_name AS PartnerName,
                                  biz_no AS BizNo,
                                  tel AS Tel,
                                  address AS Address
                           FROM partners
                           WHERE tenant_id = @TenantId
                             AND (partner_name LIKE CONCAT('%', @Keyword, '%')
                               OR biz_no LIKE CONCAT('%', @Keyword, '%'))
                             AND is_active = 1
                             AND (is_deleted = 0 OR is_deleted IS NULL)
                           ORDER BY partner_name
                           LIMIT 20
                           """;

        var rows = await _db.QueryAsync<PartnerSearchDto>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, Keyword = keyword },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    public async Task<List<PartnerListDto>> GetPartnerListAsync(string tenantId, string? search = null, string? type = null, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
                           SELECT
                             p.partner_id AS PartnerId,
                             IFNULL(p.partner_code, '') AS PartnerCode,
                             p.partner_name AS PartnerName,
                             LOWER(p.partner_type) AS PartnerType,
                             p.biz_no AS BizNo,
                             p.ceo_name AS CeoName,
                             p.tel AS Tel,
                             p.email AS Email,
                             p.manager_name AS ManagerName,
                             IFNULL(p.price_grade, 'A') AS PriceGrade,
                             IFNULL(p.credit_limit, 0) AS CreditLimit,
                             COALESCE(pb.balance, 0) AS Balance,
                             p.is_active AS IsActive,
                             p.created_at AS CreatedAt
                           FROM partners p
                           LEFT JOIN partner_balance pb
                             ON pb.tenant_id = p.tenant_id
                            AND pb.partner_id = p.partner_id
                           WHERE p.tenant_id = @TenantId
                             AND (p.is_deleted = 0 OR p.is_deleted IS NULL)
                             AND (@Search IS NULL OR @Search = '' OR
                                  p.partner_name LIKE CONCAT('%', @Search, '%') OR
                                  IFNULL(p.partner_code, '') LIKE CONCAT('%', @Search, '%'))
                             AND (@Type IS NULL OR @Type = '' OR
                                  LOWER(p.partner_type) = LOWER(@Type) OR
                                  LOWER(p.partner_type) = 'both')
                           ORDER BY p.partner_name
                           """;

        var rows = await _db.QueryAsync<PartnerListDto>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, Search = search?.Trim(), Type = type?.Trim() },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    public async Task<PartnerDetailDto?> GetPartnerDetailAsync(string partnerId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
                           SELECT
                             p.partner_id AS PartnerId,
                             IFNULL(p.partner_code, '') AS PartnerCode,
                             p.partner_name AS PartnerName,
                             LOWER(p.partner_type) AS PartnerType,
                             p.biz_no AS BizNo,
                             p.ceo_name AS CeoName,
                             p.biz_type AS BizType,
                             p.biz_item AS BizItem,
                             p.tel AS Tel,
                             p.fax AS Fax,
                             p.zip_code AS ZipCode,
                             p.address AS Address,
                             p.email AS Email,
                             p.manager_name AS ManagerName,
                             p.manager_tel AS ManagerTel,
                             IFNULL(p.price_grade, 'A') AS PriceGrade,
                             IFNULL(p.credit_limit, 0) AS CreditLimit,
                             LOWER(IFNULL(p.tax_type, 'taxable')) AS TaxType,
                             IFNULL(p.payment_terms, 30) AS PaymentTerms,
                             p.memo AS Memo,
                             IFNULL(p.row_version, 0) AS RowVersion,
                             COALESCE(pb.balance, 0) AS Balance,
                             p.is_active AS IsActive,
                             p.created_at AS CreatedAt
                           FROM partners p
                           LEFT JOIN partner_balance pb
                             ON pb.tenant_id = p.tenant_id
                            AND pb.partner_id = p.partner_id
                           WHERE p.partner_id = @PartnerId
                             AND p.tenant_id = @TenantId
                             AND (p.is_deleted = 0 OR p.is_deleted IS NULL)
                           """;

        return await _db.QueryFirstOrDefaultAsync<PartnerDetailDto>(new CommandDefinition(
            sql,
            new { PartnerId = partnerId, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<string> CreatePartnerAsync(CreatePartnerDto dto, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var dup = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM partners
            WHERE tenant_id = @TenantId
              AND partner_name = @Name
              AND (is_deleted = 0 OR is_deleted IS NULL)
            """,
            new { TenantId = tenantId, Name = dto.PartnerName },
            cancellationToken: ct)).ConfigureAwait(false);

        if (dup > 0)
        {
            throw new InvalidOperationException("이미 등록된 거래처명입니다.");
        }

        var id = Guid.NewGuid().ToString();
        var code = string.IsNullOrWhiteSpace(dto.PartnerCode)
            ? "P-" + id[..Math.Min(8, id.Length)]
            : dto.PartnerCode.Trim();
        var pType = NormalizePartnerType(dto.PartnerType);

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO partners (
              partner_id, tenant_id,
              partner_code, partner_name,
              partner_type, biz_no, ceo_name,
              biz_type, biz_item,
              tel, fax, zip_code, address,
              email, manager_name, manager_tel,
              credit_limit, price_grade,
              tax_type, payment_terms, memo,
              is_active, is_deleted,
              row_version,
              created_at, updated_at)
            VALUES (
              @Id, @TenantId,
              @PartnerCode, @PartnerName,
              @PartnerType, @BizNo, @CeoName,
              @BizType, @BizItem,
              @Tel, @Fax, @ZipCode, @Address,
              @Email, @ManagerName, @ManagerTel,
              @CreditLimit, @PriceGrade,
              @TaxType, @PaymentTerms, @Memo,
              1, 0,
              0,
              NOW(6), NOW(6))
            """,
            new
            {
                Id = id,
                TenantId = tenantId,
                PartnerCode = code,
                PartnerName = dto.PartnerName.Trim(),
                PartnerType = pType,
                BizNo = dto.BizNo,
                CeoName = dto.CeoName,
                BizType = dto.BizType,
                BizItem = dto.BizItem,
                Tel = dto.Tel,
                Fax = dto.Fax,
                ZipCode = dto.ZipCode,
                Address = dto.Address,
                Email = dto.Email,
                ManagerName = dto.ManagerName,
                ManagerTel = dto.ManagerTel,
                CreditLimit = dto.CreditLimit,
                PriceGrade = FirstPriceGrade(dto.PriceGrade),
                TaxType = string.IsNullOrWhiteSpace(dto.TaxType) ? "taxable" : dto.TaxType.Trim(),
                PaymentTerms = dto.PaymentTerms,
                Memo = dto.Memo
            },
            cancellationToken: ct)).ConfigureAwait(false);

        return id;
    }

    public async Task UpdatePartnerAsync(string partnerId, UpdatePartnerDto dto, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var code = string.IsNullOrWhiteSpace(dto.PartnerCode)
            ? null
            : dto.PartnerCode.Trim();
        var pType = NormalizePartnerType(dto.PartnerType);

        var affected = await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE partners SET
                partner_code  = COALESCE(@PartnerCode, partner_code),
                partner_name  = @PartnerName,
                partner_type  = @PartnerType,
                biz_no        = @BizNo,
                ceo_name      = @CeoName,
                biz_type      = @BizType,
                biz_item      = @BizItem,
                tel           = @Tel,
                fax           = @Fax,
                zip_code      = @ZipCode,
                address       = @Address,
                email         = @Email,
                manager_name  = @ManagerName,
                manager_tel   = @ManagerTel,
                credit_limit  = @CreditLimit,
                price_grade   = @PriceGrade,
                tax_type      = @TaxType,
                payment_terms = @PaymentTerms,
                memo          = @Memo,
                is_active     = @IsActive,
                row_version   = row_version + 1,
                updated_at    = NOW(6)
            WHERE partner_id  = @PartnerId
              AND tenant_id   = @TenantId
              AND row_version = @RowVersion
              AND (is_deleted = 0 OR is_deleted IS NULL)
            """,
            new
            {
                PartnerId = partnerId,
                TenantId = tenantId,
                PartnerCode = code,
                PartnerName = dto.PartnerName.Trim(),
                PartnerType = pType,
                BizNo = dto.BizNo,
                CeoName = dto.CeoName,
                BizType = dto.BizType,
                BizItem = dto.BizItem,
                Tel = dto.Tel,
                Fax = dto.Fax,
                ZipCode = dto.ZipCode,
                Address = dto.Address,
                Email = dto.Email,
                ManagerName = dto.ManagerName,
                ManagerTel = dto.ManagerTel,
                CreditLimit = dto.CreditLimit,
                PriceGrade = FirstPriceGrade(dto.PriceGrade),
                TaxType = string.IsNullOrWhiteSpace(dto.TaxType) ? "taxable" : dto.TaxType.Trim(),
                PaymentTerms = dto.PaymentTerms,
                Memo = dto.Memo,
                IsActive = dto.IsActive ? 1 : 0,
                RowVersion = dto.RowVersion
            },
            cancellationToken: ct)).ConfigureAwait(false);

        if (affected == 0)
        {
            throw new InvalidOperationException("다른 사용자가 수정했습니다. 새로고침 후 다시 시도해주세요.");
        }
    }

    public async Task DeletePartnerAsync(string partnerId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE partners SET
                is_deleted = 1,
                is_active  = 0,
                deleted_at = NOW(6),
                updated_at = NOW(6)
            WHERE partner_id = @PartnerId
              AND tenant_id  = @TenantId
            """,
            new { PartnerId = partnerId, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<List<PartnerSpecialPriceDto>> GetPartnerSpecialPricesAsync(string partnerId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
                           SELECT
                             sp.id AS PriceId,
                             sp.item_id AS ItemId,
                             i.item_name AS ItemName,
                             IFNULL(sp.price_type, 'fixed') AS PriceType,
                             COALESCE(sp.unit_price, sp.special_price, 0) AS UnitPrice,
                             sp.start_date AS StartDate,
                             sp.end_date AS EndDate,
                             sp.is_active AS IsActive
                           FROM partner_special_prices sp
                           LEFT JOIN items i ON i.item_id = sp.item_id AND i.tenant_id = sp.tenant_id
                           WHERE sp.partner_id = @PartnerId
                             AND sp.tenant_id = @TenantId
                           ORDER BY i.item_name
                           """;

        var rows = await _db.QueryAsync<PartnerSpecialPriceDto>(new CommandDefinition(
            sql,
            new { PartnerId = partnerId, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    public async Task UpsertPartnerSpecialPriceAsync(string partnerId, PartnerSpecialPriceDto dto, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var id = string.IsNullOrWhiteSpace(dto.PriceId) ? Guid.NewGuid().ToString() : dto.PriceId.Trim();
        var priceType = string.IsNullOrWhiteSpace(dto.PriceType) ? "fixed" : dto.PriceType.Trim();
        var unit = dto.UnitPrice;

        if (string.IsNullOrWhiteSpace(dto.PriceId))
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO partner_special_prices
                  (id, tenant_id, partner_id, item_id,
                   spec, unit, special_price, std_price, vs_ratio, last_supply_date,
                   price_type, unit_price, start_date, end_date, is_active,
                   created_at, updated_at)
                VALUES
                  (@Id, @TenantId, @PartnerId, @ItemId,
                   '', '', @UnitPrice, 0, 0, NULL,
                   @PriceType, @UnitPrice, @StartDate, @EndDate, @IsActive,
                   NOW(6), NOW(6))
                ON DUPLICATE KEY UPDATE
                   price_type = @PriceType,
                   unit_price = @UnitPrice,
                   special_price = @UnitPrice,
                   start_date = @StartDate,
                   end_date = @EndDate,
                   is_active = @IsActive,
                   updated_at = NOW(6)
                """,
                new
                {
                    Id = id,
                    TenantId = tenantId,
                    PartnerId = partnerId,
                    ItemId = dto.ItemId,
                    PriceType = priceType,
                    UnitPrice = unit,
                    StartDate = dto.StartDate,
                    EndDate = dto.EndDate,
                    IsActive = dto.IsActive ? 1 : 0
                },
                cancellationToken: ct)).ConfigureAwait(false);
        }
        else
        {
            var affected = await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE partner_special_prices SET
                    price_type = @PriceType,
                    unit_price = @UnitPrice,
                    special_price = @UnitPrice,
                    start_date = @StartDate,
                    end_date = @EndDate,
                    is_active = @IsActive,
                    updated_at = NOW(6)
                WHERE id = @Id
                  AND tenant_id = @TenantId
                  AND partner_id = @PartnerId
                """,
                new
                {
                    Id = id,
                    TenantId = tenantId,
                    PartnerId = partnerId,
                    PriceType = priceType,
                    UnitPrice = unit,
                    StartDate = dto.StartDate,
                    EndDate = dto.EndDate,
                    IsActive = dto.IsActive ? 1 : 0
                },
                cancellationToken: ct)).ConfigureAwait(false);

            if (affected == 0)
            {
                throw new InvalidOperationException("특별단가 행을 찾을 수 없습니다.");
            }
        }
    }

    public async Task DeletePartnerSpecialPriceByIdAsync(string priceId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE partner_special_prices SET
                is_active = 0,
                updated_at = NOW(6)
            WHERE id = @PriceId
              AND tenant_id = @TenantId
            """,
            new { PriceId = priceId, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    private static string FirstPriceGrade(string? g)
    {
        var t = (g ?? "A").Trim();
        if (t.Length == 0)
        {
            return "A";
        }

        return t[..1];
    }

    private static string NormalizePartnerType(string? t)
    {
        var v = (t ?? "both").Trim().ToLowerInvariant();
        return v switch
        {
            "customer" or "supplier" or "both" => v,
            "1" => "customer",
            "2" => "supplier",
            "3" => "both",
            _ => "both"
        };
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open)
        {
            return;
        }

        if (_db is DbConnection dbConnection)
        {
            await dbConnection.OpenAsync(ct).ConfigureAwait(false);
            return;
        }

        _db.Open();
    }
}
