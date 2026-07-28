using HitPan.Application.DTOs.Certificate;

namespace HitPan.Application.Interfaces;

public interface ITenantCertificateService
{
    Task<List<CertificateListDto>> GetAllAsync(string tenantId, CancellationToken ct = default);
    Task<CertificateListDto?> GetAsync(string certId, string tenantId, CancellationToken ct = default);

    /// <summary>PFX/P12 파일 업로드. 파일은 AES-256 암호화, 비밀번호는 DPAPI 보호.</summary>
    Task<string> UploadAsync(UploadCertificateRequest request, byte[] pfxBytes, string tenantId, string userId, CancellationToken ct = default);

    Task UpdateStatusAsync(string certId, UpdateCertificateStatusRequest request, string tenantId, string userId, CancellationToken ct = default);
    Task SetPrimaryAsync(string certId, string tenantId, string userId, CancellationToken ct = default);
    Task DeleteAsync(string certId, string tenantId, string userId, CancellationToken ct = default);
}
