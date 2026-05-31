using HitPan.Application.DTOs.Settings;

namespace HitPan.Application.Interfaces;

public interface IFormTemplateService
{
    Task<IReadOnlyList<FormTemplateDto>> ListAsync(string tenantId, string? formType = null, bool activeOnly = true, CancellationToken ct = default);
    Task<FormTemplateDto?> GetDefaultAsync(string tenantId, string formType, CancellationToken ct = default);
    Task<FormTemplateDto> CreateAsync(string tenantId, CreateFormTemplateRequest request, CancellationToken ct = default);
    Task<FormTemplateDto> UpdateAsync(string tenantId, string templateId, UpdateFormTemplateRequest request, CancellationToken ct = default);
    Task DeactivateAsync(string tenantId, string templateId, CancellationToken ct = default);

    // 테넌트 신규 생성 시 6대 양식 기본 템플릿 시드 (plain 모드 6건)
    Task SeedDefaultsAsync(string tenantId, CancellationToken ct = default);
}
