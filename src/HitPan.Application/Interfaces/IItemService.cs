using HitPan.Application.DTOs.Item;

namespace HitPan.Application.Interfaces;

public interface IItemService
{
    Task<List<ItemListDto>> GetListAsync(
        string tenantId,
        string? search = null,
        string? group = null,
        string? type = null,
        CancellationToken ct = default);

    Task<ItemDetailDto?> GetAsync(string itemId, string tenantId, CancellationToken ct = default);

    Task<string> CreateAsync(CreateItemDto dto, string tenantId, CancellationToken ct = default);

    Task UpdateAsync(string itemId, UpdateItemDto dto, string tenantId, CancellationToken ct = default);

    Task DeleteAsync(string itemId, string tenantId, CancellationToken ct = default);

    Task<List<ItemSpecialPriceDto>> GetSpecialPricesAsync(string itemId, string tenantId, CancellationToken ct = default);

    Task UpsertSpecialPriceAsync(string itemId, ItemSpecialPriceDto dto, string tenantId, CancellationToken ct = default);

    Task DeleteSpecialPriceAsync(string priceId, string tenantId, CancellationToken ct = default);

    Task<List<ItemGroupDto>> GetGroupsAsync(string tenantId, CancellationToken ct = default);
}
