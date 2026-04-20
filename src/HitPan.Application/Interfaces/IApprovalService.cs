using HitPan.Application.DTOs.Approval;

namespace HitPan.Application.Interfaces;

/// <summary>결재 서비스 인터페이스</summary>
public interface IApprovalService
{
    // ── 결재 설정 ──
    Task<List<ApprovalSettingDto>> GetSettingsAsync(string tenantId, CancellationToken ct = default);
    Task SaveSettingAsync(SaveApprovalSettingRequest request, string tenantId, string userId, CancellationToken ct = default);

    // ── 결재 라인 ──
    Task<List<ApprovalLineDto>> GetLinesAsync(string tenantId, string docType, CancellationToken ct = default);
    Task SaveLinesAsync(SaveApprovalLinesRequest request, string tenantId, CancellationToken ct = default);

    // ── 결재 문서 ──
    Task<string> CreateApprovalAsync(CreateApprovalRequest request, string tenantId, string userId, string userName, CancellationToken ct = default);
    Task<List<ApprovalDocumentDto>> GetPendingAsync(string tenantId, string employeeId, CancellationToken ct = default);
    Task<List<ApprovalDocumentDto>> GetSentAsync(string tenantId, string employeeId, CancellationToken ct = default);
    Task<List<ApprovalDocumentDto>> GetCompletedAsync(string tenantId, string employeeId, CancellationToken ct = default);
    Task<ApprovalDetailDto?> GetDetailAsync(string approvalId, string tenantId, CancellationToken ct = default);
    Task ProcessAsync(string approvalId, ProcessApprovalRequest request, string tenantId, string employeeId, string employeeName, CancellationToken ct = default);
}
