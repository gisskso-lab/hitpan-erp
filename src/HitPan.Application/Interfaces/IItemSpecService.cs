using HitPan.Application.DTOs.Item;

namespace HitPan.Application.Interfaces;

public interface IItemSpecService
{
    Task<IReadOnlyList<ItemSpecDto>> GetByItemAsync(string tenantId, string itemId, bool activeOnly = true, CancellationToken ct = default);
    Task<ItemSpecDto> CreateAsync(string tenantId, string itemId, CreateItemSpecRequest request, CancellationToken ct = default);
    Task<ItemSpecDto> UpdateAsync(string tenantId, string itemId, string specId, UpdateItemSpecRequest request, CancellationToken ct = default);
    Task DeactivateAsync(string tenantId, string itemId, string specId, CancellationToken ct = default);
}
