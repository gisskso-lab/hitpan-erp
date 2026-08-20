using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.Common;
using HitPan.Application.DTOs.Partner;
using HitPan.Application.DTOs.Sales;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

public sealed class PartnerService : IPartnerService
{
    private readonly ICurrentTenant _currentTenant;
    private readonly IPartnerBalanceRepository _partnerBalanceRepository;
    private readonly IDbConnection _db;
    private readonly IGeocodingService _geocoding;

    public PartnerService(
        ICurrentTenant currentTenant,
        IPartnerBalanceRepository partnerBalanceRepository,
        IDbConnection db,
        IGeocodingService geocoding)
    {
        _currentTenant = currentTenant;
        _partnerBalanceRepository = partnerBalanceRepository;
        _db = db;
        _geocoding = geocoding;
    }

    /// <summary>
    /// 주소로 좌표를 채운다 (20260821작1 W1).
    ///
    /// 사장님 지적: "맵이 뜨긴 하지만 실제 해당주소 좌표가 안찍힘"
    ///   카카오맵·내비 딥링크는 좌표를 요구하는데 보관하는 값이 없었다.
    ///   업체를 저장할 때 주소로 좌표를 한 번 구해 둔다.
    ///
    /// 🔴 실패해도 저장을 막지 않는다 (§#20). 좌표가 없으면 지도는 현행 주소 방식으로 열린다.
    /// 🔴 반자동 원칙: 화면에서 이미 좌표를 보내왔으면(사람이 확인·수정한 값) 그대로 존중하고
    ///    덮어쓰지 않는다. 변환은 좌표가 비어 있을 때만 한다.
    /// </summary>
    private async Task<(decimal? Lat, decimal? Lng)> ResolveCoordinatesAsync(
        decimal? incomingLat, decimal? incomingLng, string? address, string? addressDetail, CancellationToken ct)
    {
        if (incomingLat.HasValue && incomingLng.HasValue)
            return (incomingLat, incomingLng);

        if (string.IsNullOrWhiteSpace(address))
            return (null, null);

        // 상세주소까지 붙이면 오히려 못 찾는 경우가 있어 기본주소로 조회한다.
        var result = await _geocoding.GeocodeAsync(address.Trim(), ct).ConfigureAwait(false);
        return result.Success ? (result.Latitude, result.Longitude) : (null, null);
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
                                  IFNULL(p.price_type, 'fixed') AS PriceType,
                                  p.discount_rate AS DiscountRate,
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

        // 봉합 (2026-06-23, 19차 업체특별단가 할인율): 종전엔 price_type='fixed' 하드코딩 + discount_rate 미저장이라
        //   화면에서 '할인' 모드를 골라도 할인율이 통째 유실됐다(상품 특별단가는 되는데 업체만 안 되는 비대칭).
        //   상품 패턴과 동일하게 — 할인 모드는 unit_price=0·discount_rate=값, 고정 모드는 discount_rate=null.
        var priceType = string.IsNullOrWhiteSpace(dto.PriceType) ? "fixed" : dto.PriceType.Trim();
        var unitPrice = priceType == "discount" ? 0m : dto.SpecialPrice;
        decimal? discountRate = priceType == "discount" ? (dto.DiscountRate ?? 0m) : (decimal?)null;
        if (priceType == "discount" && (discountRate < 0m || discountRate > 100m))
        {
            throw new InvalidOperationException("할인율은 0~100% 범위여야 합니다.");
        }

        const string sql = """
                           INSERT INTO partner_special_prices
                             (id, tenant_id, partner_id, item_id,
                              spec, unit, special_price,
                              std_price, last_supply_date,
                              price_type, unit_price, discount_rate, start_date, end_date,
                              is_active, created_by, updated_by, created_at, updated_at)
                           VALUES
                             (@Id, @TenantId, @PartnerId, @ItemId,
                              @Spec, @Unit, @SpecialPrice,
                              @StdPrice, @LastSupplyDate,
                              @PriceType, @UnitPrice, @DiscountRate, NULL, NULL,
                              1, @UserId, @UserId, NOW(6), NOW(6))
                           ON DUPLICATE KEY UPDATE
                             special_price    = @SpecialPrice,
                             price_type       = @PriceType,
                             unit_price       = @UnitPrice,
                             discount_rate    = @DiscountRate,
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
                PriceType = priceType,
                UnitPrice = unitPrice,
                DiscountRate = discountRate,
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
                           LIMIT 500
                           """;

        var rows = await _db.QueryAsync<PartnerListDto>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, Search = search?.Trim(), Type = type?.Trim() },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    /// <summary>
    /// 서버 페이지네이션 버전 (2026-05-13 야간, 헌법 #25 정공법).
    /// 기존 GetPartnerListAsync는 그대로 유지 — Razor가 ServerData 패턴으로 전환 시 이 메서드 사용.
    /// SQL 본문은 GetPartnerListAsync와 동일 — LIMIT/OFFSET + COUNT 분리만 추가.
    /// </summary>
    public async Task<PagedResult<PartnerListDto>> GetPartnerListPagedAsync(
        string tenantId, PagedRequest req, string? type = null, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string whereSql = """
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
                                """;

        var countSql = $"SELECT COUNT(*) {whereSql}";

        var listSql = $"""
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
                       {whereSql}
                       ORDER BY p.partner_name
                       LIMIT @Take OFFSET @Skip
                       """;

        var parameters = new
        {
            TenantId = tenantId,
            Search = req.Search?.Trim(),
            Type = type?.Trim(),
            req.Skip,
            req.Take
        };

        var totalCount = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            countSql, parameters, cancellationToken: ct)).ConfigureAwait(false);

        var items = totalCount == 0
            ? new List<PartnerListDto>()
            : (await _db.QueryAsync<PartnerListDto>(new CommandDefinition(
                listSql, parameters, cancellationToken: ct)).ConfigureAwait(false)).ToList();

        return new PagedResult<PartnerListDto>
        {
            Page = req.Page,
            PageSize = req.Take,
            TotalCount = totalCount,
            Items = items
        };
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
                             p.address_detail AS AddressDetail,
                             p.latitude AS Latitude,
                             p.longitude AS Longitude,
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

        // 20260821작1 W1: 좌표 확보 — 실패해도 저장은 계속된다 (§#20)
        var (geoLat, geoLng) = await ResolveCoordinatesAsync(
            dto.Latitude, dto.Longitude, dto.Address, dto.AddressDetail, ct).ConfigureAwait(false);

        var id = Guid.NewGuid().ToString();
        var code = string.IsNullOrWhiteSpace(dto.PartnerCode)
            ? "P-" + id[..Math.Min(8, id.Length)]
            : dto.PartnerCode.Trim();

        var codeDup = await _db.QueryFirstOrDefaultAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM partners
            WHERE tenant_id = @TenantId
              AND partner_code = @Code
              AND (is_deleted = 0 OR is_deleted IS NULL)
            """,
            new { TenantId = tenantId, Code = code },
            cancellationToken: ct)).ConfigureAwait(false);

        if (codeDup > 0)
        {
            throw new InvalidOperationException($"이미 사용 중인 업체코드입니다: {code}");
        }
        var pType = NormalizePartnerType(dto.PartnerType);

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO partners (
              partner_id, tenant_id,
              partner_code, partner_name,
              partner_type, biz_no, ceo_name,
              biz_type, biz_item,
              tel, fax, zip_code, address, address_detail,
              latitude, longitude,
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
              @Tel, @Fax, @ZipCode, @Address, @AddressDetail,
              @Latitude, @Longitude,
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
                AddressDetail = dto.AddressDetail,
                Latitude = geoLat,
                Longitude = geoLng,
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

        // 20260821작1 W1: 주소가 바뀌었을 수 있으므로 좌표를 다시 확보한다.
        //   화면이 좌표를 함께 보냈으면(사람이 확인한 값) 그것을 존중하고 변환하지 않는다.
        var (geoLat, geoLng) = await ResolveCoordinatesAsync(
            dto.Latitude, dto.Longitude, dto.Address, dto.AddressDetail, ct).ConfigureAwait(false);

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
                address_detail = @AddressDetail,
                latitude      = @Latitude,
                longitude     = @Longitude,
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
                AddressDetail = dto.AddressDetail,
                Latitude = geoLat,
                Longitude = geoLng,
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
                             sp.discount_rate AS DiscountRate,
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

        // 봉합 (2026-06-23, 19차 업체특별단가 할인율): 상품 특별단가(ItemService.UpsertSpecialPriceAsync)와
        //   동일 의미 분기 — 할인 모드는 unit_price=0·discount_rate=값, 고정 모드는 discount_rate=null.
        //   종전엔 discount_rate 를 INSERT/UPDATE 하지 않아 할인율 모드가 통째 유실됐다.
        var unit = priceType == "discount" ? 0m : dto.UnitPrice;
        decimal? discountRate = priceType == "discount" ? (dto.DiscountRate ?? 0m) : (decimal?)null;
        if (priceType == "discount" && (discountRate < 0m || discountRate > 100m))
        {
            throw new InvalidOperationException("할인율은 0~100% 범위여야 합니다.");
        }

        if (string.IsNullOrWhiteSpace(dto.PriceId))
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO partner_special_prices
                  (id, tenant_id, partner_id, item_id,
                   spec, unit, special_price, std_price, vs_ratio, last_supply_date,
                   price_type, unit_price, discount_rate, start_date, end_date, is_active,
                   created_at, updated_at)
                VALUES
                  (@Id, @TenantId, @PartnerId, @ItemId,
                   '', '', @UnitPrice, 0, 0, NULL,
                   @PriceType, @UnitPrice, @DiscountRate, @StartDate, @EndDate, @IsActive,
                   NOW(6), NOW(6))
                ON DUPLICATE KEY UPDATE
                   price_type = @PriceType,
                   unit_price = @UnitPrice,
                   discount_rate = @DiscountRate,
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
                    DiscountRate = discountRate,
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
                    discount_rate = @DiscountRate,
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
                    DiscountRate = discountRate,
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

    /// <summary>
    /// 단가 참고값 4종을 한 번에 읽는다 — 명세서 화면 말풍선용 (20260820작4 · 설계2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>왜 한 번에 읽나</b> — 헌법 #16: <c>Task.WhenAll</c> 로 같은 커넥션을 동시에 쓰면 터진다.
    /// <c>UNION ALL</c> 로 <b>한 왕복</b>에 끝낸다. 그리드에서 줄마다 부르는 자리라 왕복이 곧 체감 속도다.
    /// </para>
    ///
    /// <para>
    /// 🔴 <b><paramref name="isPurchase"/> 가 최종단가의 출처를 가른다.</b>
    /// 판 값과 산 값은 다른 금액이다 — 발주·매입·반품이면 <c>purchase_receipt_items</c>,
    /// 견적·수주·판매면 <c>sales_delivery_items</c> 에서 온다.
    /// ⚠️ 이 갈래를 지우고 한쪽만 쓰면 <b>매입 화면에 판 가격이 뜬다.</b>
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>값이 없으면 <c>null</c> 로 둔다. 0 으로 채우지 마라</b>(게이트 G-8) —
    /// 화면에서 <b>진짜 0원과 구별이 안 된다.</b>
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>할인율 모드</b>: <c>partner_special_prices.price_type='discount'</c> 면
    /// <c>special_price</c> 가 0 이고 할인율만 있다(<c>PartnerService.UpsertSpecialPriceAsync</c> 참고).
    /// 그대로 주면 화면에 <b>0원</b>이 뜨므로 여기서 <b>표준단가 × (1 - 할인율/100)</b> 로 환산한다.
    /// ⚠️ 표준단가가 없으면 환산할 밑값이 없다 ⇒ <c>NULL</c> 로 둔다(0 으로 만들지 않는다).
    /// </para>
    /// </remarks>
    public async Task<PriceHintDto?> GetPriceHintAsync(
        string partnerId, string itemId, string tenantId, bool isPurchase, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(partnerId) || string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 최종단가 — 화면 성격에 따라 매입/판매 중 한쪽만 읽는다(위 remarks 참고).
        //
        // 🔴 [괄호를 지우지 마라 — 실측으로 잡은 문법 오류] MariaDB 는 `UNION ALL` 로 묶인 각 갈래에
        //   `ORDER BY`·`LIMIT` 이 붙으면 **괄호를 요구한다.** 없으면 파싱 단계에서 1064 로 죽는다
        //   (2026-08-20 실 DB 실측 — 괄호 없이 짰다가 문법 오류를 받고 고쳤다).
        //   ⚠️ 빌드는 통과한다. SQL 은 문자열이라 컴파일러가 못 본다 ⇒ **런타임 500** 이 된다.
        var lastPriceSql = isPurchase
            ? """
              (SELECT 'last' AS Kind, ri.unit_price AS Price, r.receipt_date AS PriceDate
               FROM purchase_receipt_items ri
               JOIN purchase_receipts r
                 ON r.receipt_id = ri.receipt_id AND r.tenant_id = ri.tenant_id
               WHERE ri.tenant_id = @TenantId AND ri.item_id = @ItemId AND r.partner_id = @PartnerId
               ORDER BY r.receipt_date DESC, r.created_at DESC
               LIMIT 1)
              """
            : """
              (SELECT 'last' AS Kind, di.unit_price AS Price, d.delivery_date AS PriceDate
               FROM sales_delivery_items di
               JOIN sales_deliveries d
                 ON d.delivery_id = di.delivery_id AND d.tenant_id = di.tenant_id
               WHERE di.tenant_id = @TenantId AND di.item_id = @ItemId AND d.partner_id = @PartnerId
                 AND d.deleted_at IS NULL
               ORDER BY d.delivery_date DESC, d.created_at DESC
               LIMIT 1)
              """;

        var sql = $"""
                   (SELECT 'partner' AS Kind,
                           CASE
                             WHEN IFNULL(psp.price_type, 'fixed') = 'discount'
                               THEN CASE WHEN i.std_price IS NULL OR i.std_price = 0 THEN NULL
                                         ELSE i.std_price * (1 - IFNULL(psp.discount_rate, 0) / 100) END
                             ELSE psp.special_price
                           END AS Price,
                           NULL AS PriceDate
                    FROM partner_special_prices psp
                    LEFT JOIN items i ON i.item_id = psp.item_id AND i.tenant_id = psp.tenant_id
                    WHERE psp.tenant_id = @TenantId AND psp.partner_id = @PartnerId
                      AND psp.item_id = @ItemId AND psp.is_active = 1
                    LIMIT 1)

                   UNION ALL

                   (SELECT 'std' AS Kind, i.std_price AS Price, NULL AS PriceDate
                    FROM items i
                    WHERE i.tenant_id = @TenantId AND i.item_id = @ItemId
                    LIMIT 1)

                   UNION ALL

                   (SELECT 'item' AS Kind,
                           CASE
                             WHEN IFNULL(isp.price_type, 'fixed') = 'discount'
                               THEN CASE WHEN i2.std_price IS NULL OR i2.std_price = 0 THEN NULL
                                         ELSE i2.std_price * (1 - IFNULL(isp.discount_rate, 0) / 100) END
                             ELSE isp.unit_price
                           END AS Price,
                           NULL AS PriceDate
                    FROM item_special_prices isp
                    LEFT JOIN items i2 ON i2.item_id = isp.item_id AND i2.tenant_id = isp.tenant_id
                    WHERE isp.tenant_id = @TenantId AND isp.item_id = @ItemId
                      AND isp.partner_id = @PartnerId AND isp.is_active = 1
                    LIMIT 1)

                   UNION ALL

                   {lastPriceSql}
                   """;

        var rows = await _db.QueryAsync<(string Kind, decimal? Price, DateTime? PriceDate)>(
            new CommandDefinition(sql,
                new { TenantId = tenantId, PartnerId = partnerId, ItemId = itemId },
                cancellationToken: ct)).ConfigureAwait(false);

        var hint = new PriceHintDto { ItemId = itemId };
        foreach (var r in rows)
        {
            switch (r.Kind)
            {
                case "partner": hint.PartnerSpecialPrice = r.Price; break;
                case "std": hint.StdPrice = r.Price; break;
                case "item": hint.ItemSpecialPrice = r.Price; break;
                case "last":
                    hint.LastPrice = r.Price;
                    hint.LastPriceDate = r.PriceDate;
                    break;
            }
        }

        return hint;
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
