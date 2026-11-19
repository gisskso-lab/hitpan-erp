using HitPan.Application.DTOs.Settings;
using HitPan.Application.Services;

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

    // 🔴 [2026-11-20 W-G1-51 갈래 F-1 · 편집창 부분] 양식지 저장 뒤 확인창 「예」 — Y 템플릿 만들기(설계 §7-3 「한 함수」).
    //   구현체 = FormTemplateService 1개(#12 grep · 시험 쪽 가짜 구현 0). tenantId 는 JWT 클레임에서만(#2).
    Task<FormTemplateService.YTemplateResult> CreateYTemplateAsync(string tenantId, string templateId, CancellationToken ct = default);
}
