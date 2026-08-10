using HitPan.Application.DTOs.Settings;

namespace HitPan.Application.Interfaces;

public interface ISettingsService
{
    Task<TenantSettingsDto> GetAsync(string tenantId, CancellationToken ct = default);

    Task SaveAsync(UpdateTenantSettingsDto dto, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// tenants 행의 사업장 기본 정보를 갱신한다.
    /// </summary>
    /// <summary>
    /// 사업장 정보를 저장한다.
    /// 반환값은 <b>잠겨 있어 저장하지 않은 항목의 이름</b>이다(비어 있으면 전부 저장됨).
    /// 잠금 위반을 조용히 무시하면 고객은 "고쳤는데 안 바뀐다"는 상태에 빠진다(2026-08-10).
    /// </summary>
    Task<IReadOnlyList<string>> SaveCompanyAsync(UpdateTenantCompanyDto dto, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// tenants 테이블에서 사업장 기본 정보를 조회한다.
    /// </summary>
    Task<UpdateTenantCompanyDto?> GetCompanyAsync(string tenantId, CancellationToken ct = default);

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
