using HitPan.Application.DTOs.ApprovalLine;

namespace HitPan.Application.Interfaces;

public interface IApprovalLineService
{
    Task<List<ApprovalLineListDto>> GetListAsync(string tenantId, CancellationToken ct = default);
    Task<ApprovalLineDetailDto?> GetAsync(string tenantId, string approvalLineId, CancellationToken ct = default);
    Task<string> CreateAsync(string tenantId, SaveApprovalLineRequest request, CancellationToken ct = default);
    Task UpdateAsync(string tenantId, string approvalLineId, SaveApprovalLineRequest request, CancellationToken ct = default);
    Task DeleteAsync(string tenantId, string approvalLineId, CancellationToken ct = default);
}
