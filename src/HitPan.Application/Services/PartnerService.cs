using HitPan.Application.DTOs.Partner;
using HitPan.Application.Interfaces;
using System.Data;
using Dapper;

namespace HitPan.Application.Services;

public class PartnerService : IPartnerService
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
        var sql = """
                  SELECT p.id,
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
            new
            {
                PartnerId = partnerId,
                TenantId = tenantId
            },
            cancellationToken: ct));

        return rows.ToList();
    }

    public async Task UpsertSpecialPriceAsync(string partnerId, SpecialPriceUpsertDto dto, string tenantId, string userId, CancellationToken ct = default)
    {
        var sql = """
                  INSERT INTO partner_special_prices
                    (tenant_id, partner_id, item_id,
                     spec, unit, special_price,
                     std_price, last_supply_date,
                     created_by, updated_by)
                  VALUES
                    (@TenantId, @PartnerId, @ItemId,
                     @Spec, @Unit, @SpecialPrice,
                     @StdPrice, @LastSupplyDate,
                     @UserId, @UserId)
                  ON DUPLICATE KEY UPDATE
                    special_price    = @SpecialPrice,
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
            cancellationToken: ct));
    }

    public async Task DeleteSpecialPriceAsync(string partnerId, string itemId, string tenantId, CancellationToken ct = default)
    {
        var sql = """
                  UPDATE partner_special_prices
                  SET is_active = 0,
                      updated_at = NOW(6)
                  WHERE partner_id = @PartnerId
                    AND item_id = @ItemId
                    AND tenant_id = @TenantId
                  """;
        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                PartnerId = partnerId,
                ItemId = itemId,
                TenantId = tenantId
            },
            cancellationToken: ct));
    }

    public Task<bool> IsAssignedPartnerAsync(string? employeeId, string partnerId, string tenantId, CancellationToken ct = default)
    {
        return Task.FromResult(true);
    }
}
