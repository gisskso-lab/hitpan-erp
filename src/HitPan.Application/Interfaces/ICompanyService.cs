using HitPan.Application.DTOs.Company;

namespace HitPan.Application.Interfaces;

public interface ICompanyService
{
    Task<CompanyDto?> GetAsync(string tenantId, CancellationToken ct = default);

    Task UpdateAsync(UpdateCompanyDto dto, string tenantId, CancellationToken ct = default);
}
