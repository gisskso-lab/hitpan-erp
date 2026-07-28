using HitPan.Application.DTOs.Position;

namespace HitPan.Application.Interfaces;

public interface IPositionService
{
    Task<List<PositionListDto>> GetListAsync(string tenantId, CancellationToken ct = default);
    Task<string> CreateAsync(string tenantId, CreatePositionRequest request, CancellationToken ct = default);
    Task UpdateAsync(string tenantId, string positionId, UpdatePositionRequest request, CancellationToken ct = default);
    Task DeleteAsync(string tenantId, string positionId, CancellationToken ct = default);
}
