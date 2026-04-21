using HitPan.Application.DTOs.ESign;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 전자서명 서비스 인터페이스.
/// - 간편인증 4종: kakao / toss / finance / pass (Mock 모드)
/// - 수동 3종: manual_upload / handwritten / admin_verified
/// </summary>
public interface IESignatureService
{
    /// <summary>전자서명 기록 목록 조회.</summary>
    Task<List<ESignRecordDto>> GetListAsync(
        string tenantId,
        string? documentType = null,
        string? documentId = null,
        CancellationToken ct = default);

    /// <summary>전자서명 기록 단건 조회.</summary>
    Task<ESignRecordDto?> GetAsync(
        string esignId,
        string tenantId,
        CancellationToken ct = default);

    /// <summary>간편인증(kakao/toss/finance/pass) 또는 수동(manual_upload/handwritten/admin_verified) 서명 기록.</summary>
    Task<ESignResultDto> SignAsync(
        ESignRequestDto req,
        string tenantId,
        string userId,
        string ipAddress,
        string? userAgent,
        string? deviceId,
        CancellationToken ct = default);

    /// <summary>전자서명 무효화 (TenantAdmin 전용).</summary>
    Task VoidAsync(
        string esignId,
        string tenantId,
        string userId,
        string reason,
        CancellationToken ct = default);
}

/// <summary>
/// 전자근로계약서 서비스 인터페이스.
/// </summary>
public interface ILaborContractService
{
    /// <summary>근로계약서 목록 조회.</summary>
    Task<List<LaborContractDto>> GetListAsync(
        string tenantId,
        string? employeeId = null,
        string? status = null,
        CancellationToken ct = default);

    /// <summary>근로계약서 단건 조회.</summary>
    Task<LaborContractDto?> GetAsync(
        string contractId,
        string tenantId,
        CancellationToken ct = default);

    /// <summary>근로계약서 생성 (draft 상태).</summary>
    Task<string> CreateAsync(
        CreateLaborContractRequest req,
        string tenantId,
        string userId,
        CancellationToken ct = default);

    /// <summary>근로계약서 발송 (sent 상태로 전환).</summary>
    Task SendAsync(
        string contractId,
        string tenantId,
        string userId,
        CancellationToken ct = default);

    /// <summary>서명 기록 첨부 (signed 상태로 전환).</summary>
    Task AttachSignatureAsync(
        string contractId,
        string esignId,
        string tenantId,
        CancellationToken ct = default);
}
