using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Company;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

public sealed class CompanyService : ICompanyService
{
    private readonly IDbConnection _db;
    private bool? _tenantHasExtendedColumns;

    public CompanyService(IDbConnection db)
    {
        _db = db;
    }

    public async Task<CompanyDto?> GetAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (await TenantsHasExtendedColumnsAsync(ct).ConfigureAwait(false))
        {
            const string sql = """
                SELECT
                    tenant_id AS TenantId,
                    company_name AS CompanyName,
                    biz_no AS BizNo,
                    ceo_name AS CeoName,
                    biz_type AS BizType,
                    biz_item AS BizItem,
                    tel AS Tel,
                    fax AS Fax,
                    address AS Address,
                    zip_code AS ZipCode,
                    email AS Email,
                    logo_url AS LogoUrl,
                    tax_type AS TaxType,
                    fiscal_month AS FiscalMonth
                FROM tenants
                WHERE tenant_id = @TenantId
                """;

            return await _db.QueryFirstOrDefaultAsync<CompanyDto>(
                new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        }

        const string baseSql = """
            SELECT
                tenant_id AS TenantId,
                company_name AS CompanyName,
                biz_no AS BizNo,
                ceo_name AS CeoName,
                tel AS Tel,
                address AS Address
            FROM tenants
            WHERE tenant_id = @TenantId
            """;

        var row = await _db.QueryFirstOrDefaultAsync<CompanyBaseRow>(
            new CommandDefinition(baseSql, new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        return new CompanyDto
        {
            TenantId = row.TenantId,
            CompanyName = row.CompanyName,
            BizNo = row.BizNo,
            CeoName = row.CeoName,
            Tel = row.Tel,
            Address = row.Address,
            TaxType = "taxable",
            FiscalMonth = 12
        };
    }

    public async Task UpdateAsync(UpdateCompanyDto dto, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (await TenantsHasExtendedColumnsAsync(ct).ConfigureAwait(false))
        {
            await _db.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE tenants SET
                        company_name = @CompanyName,
                        biz_no = @BizNo,
                        ceo_name = @CeoName,
                        biz_type = @BizType,
                        biz_item = @BizItem,
                        tel = @Tel,
                        fax = @Fax,
                        address = @Address,
                        zip_code = @ZipCode,
                        email = @Email,
                        logo_url = @LogoUrl,
                        tax_type = @TaxType,
                        fiscal_month = @FiscalMonth,
                        updated_at = NOW(6)
                    WHERE tenant_id = @TenantId
                    """,
                    new
                    {
                        TenantId = tenantId,
                        CompanyName = dto.CompanyName,
                        BizNo = dto.BizNo,
                        CeoName = dto.CeoName,
                        BizType = dto.BizType,
                        BizItem = dto.BizItem,
                        Tel = dto.Tel,
                        Fax = dto.Fax,
                        Address = dto.Address,
                        ZipCode = dto.ZipCode,
                        Email = dto.Email,
                        LogoUrl = dto.LogoUrl,
                        TaxType = dto.TaxType,
                        FiscalMonth = dto.FiscalMonth
                    },
                    cancellationToken: ct)).ConfigureAwait(false);
            return;
        }

        await _db.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE tenants SET
                    company_name = @CompanyName,
                    biz_no = @BizNo,
                    ceo_name = @CeoName,
                    tel = @Tel,
                    address = @Address,
                    updated_at = NOW(6)
                WHERE tenant_id = @TenantId
                """,
                new
                {
                    TenantId = tenantId,
                    CompanyName = dto.CompanyName,
                    BizNo = dto.BizNo,
                    CeoName = dto.CeoName,
                    Tel = dto.Tel,
                    Address = dto.Address
                },
                cancellationToken: ct)).ConfigureAwait(false);
    }

    private async Task<bool> TenantsHasExtendedColumnsAsync(CancellationToken ct)
    {
        if (_tenantHasExtendedColumns.HasValue)
        {
            return _tenantHasExtendedColumns.Value;
        }

        var n = await _db.ExecuteScalarAsync<long>(
            new CommandDefinition(
                """
                SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'tenants'
                  AND COLUMN_NAME = 'biz_type'
                """,
                cancellationToken: ct)).ConfigureAwait(false);

        _tenantHasExtendedColumns = n > 0;
        return _tenantHasExtendedColumns.Value;
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open)
        {
            return;
        }

        if (_db is DbConnection dbConnection)
        {
            await dbConnection.OpenAsync(ct).ConfigureAwait(false);
            return;
        }

        _db.Open();
    }

    private sealed class CompanyBaseRow
    {
        public string TenantId { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string? BizNo { get; set; }
        public string? CeoName { get; set; }
        public string? Tel { get; set; }
        public string? Address { get; set; }
    }
}
