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

    /// <summary>
    /// 🔴 회사 그림 경로 한 칸을 적는다 — 로고 · 직인 · 출력 머리 (20261026작1 §B-4).
    /// </summary>
    /// <param name="kind">🔴 <c>logo</c>·<c>seal</c>·<c>header</c> 만. 그 밖은 아무것도 하지 않는다.</param>
    /// <param name="storedPath">보관소가 돌려준 경로. <c>null</c> 이면 그 칸을 비운다.</param>
    /// <returns>적었으면 <c>true</c>, 모르는 종류라 아무것도 안 했으면 <c>false</c>.</returns>
    /// <remarks>
    /// 🔴 <b>왜 업로드와 같은 걸음에 적나</b> — 10/25 실측에서 드러난 사고가 이것이었다.
    ///   화면은 <i>"로고가 등록되었습니다"</i> 라고 말하고 서버는 <c>null</c> 을 저장했다.
    ///   <b>고객 화면에 거짓이 있었다.</b> 이 화면에는 「저장」 단추가 없으므로
    ///   올리는 그 걸음에 칸까지 적어야 「등록되었습니다」가 참이 된다.
    /// 🔴 <b>컬럼 이름을 바깥 글자로 만들지 않는다</b> — 허용한 세 종류를 코드가 직접 가른다.
    /// </remarks>
    Task<bool> SaveCompanyImagePathAsync(
        string tenantId, string kind, string? storedPath, CancellationToken ct = default);

    /// <summary>
    /// 🔴 인쇄 공통 설정을 읽는다 (<c>print_common_settings</c> · DB-148 · 결-I).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>행이 없는 것이 정상이다</b> — 고객이 한 번도 안 건드리면 행이 없다.
    ///   그때는 <b>제안값</b>(<see cref="HitPan.Contracts.Forms.WatermarkDefaults"/>)을 돌려준다.
    ///   화면이 빈 칸을 보여 주면 고객은 무엇을 적어야 할지 모른다(히트판 정신 — 쉬움).
    /// </remarks>
    Task<PrintCommonSettingsDto> GetPrintCommonAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// 🔴 인쇄 공통 설정을 저장한다 — 행이 없으면 만들고 있으면 고친다 (DB-148).
    /// </summary>
    /// <remarks>🔴 수치는 <b>범위로 조여서</b> 넣는다 — 종이가 안 나가는 일은 없어야 한다(#20).</remarks>
    Task SavePrintCommonAsync(string tenantId, PrintCommonSettingsDto dto, CancellationToken ct = default);

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
