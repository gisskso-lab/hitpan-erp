using HitPan.Application.DTOs.Settings;

namespace HitPan.Application.Interfaces;

public interface ISettingsService
{
    Task<TenantSettingsDto> GetAsync(string tenantId, CancellationToken ct = default);

    Task SaveAsync(UpdateTenantSettingsDto dto, string tenantId, CancellationToken ct = default);

    Task<UnitPriceValidationDto> ValidateUnitPriceAsync(
        string tenantId,
        decimal unitPrice,
        decimal referencePrice,
        CancellationToken ct = default);

    bool IsUnitPriceWithinDeviation(
        decimal unitPrice,
        decimal referencePrice,
        int priceDeviationLimitPercent);

    Task LogForceEditAsync(
        string tenantId,
        string userId,
        string tableName,
        string recordId,
        string fieldName,
        string? beforeValue,
        string? afterValue,
        string? reason,
        string? ip,
        CancellationToken ct = default);

    Task<bool> VerifyForceEditPasswordAsync(
        string tenantId,
        string inputPassword,
        CancellationToken ct = default);
}
