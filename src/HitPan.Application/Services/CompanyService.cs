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
                    corp_no AS CorpNo,
                    subsidiary_no AS SubsidiaryNo,
                    homepage AS Homepage,
                    initial_date AS InitialDate,
                    e_invoice_server AS EInvoiceServer,
                    e_invoice_id AS EInvoiceId,
                    e_invoice_enabled AS EInvoiceEnabled,
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
            FiscalMonth = 12,
            EInvoiceEnabled = false
        };
    }

    public async Task UpdateAsync(UpdateCompanyDto dto, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 헌법 #35 (사장님 결재 2026-06-04) — 랜딩 자동 반영된 회사정보는 ERP 내 수정 금지.
        // 핵심 필드 3건(회사명·사업자번호·대표자명)은 잠금. 변경하려면 랜딩 사업자등록증 재등록.
        var lockedRaw = await _db.QueryFirstOrDefaultAsync<int?>(
            new CommandDefinition(
                "SELECT is_locked_from_landing FROM tenants WHERE tenant_id = @TenantId",
                new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        var isLocked = lockedRaw == 1;

        if (await TenantsHasExtendedColumnsAsync(ct).ConfigureAwait(false))
        {
            if (isLocked)
            {
                // 잠긴 경우 — 핵심 3필드는 기존 값 유지, 나머지(연락처·주소·이메일·업태·종목 등)만 갱신
                await _db.ExecuteAsync(
                    new CommandDefinition(
                        """
                        UPDATE tenants SET
                            biz_type = @BizType,
                            biz_item = @BizItem,
                            tel = @Tel,
                            fax = @Fax,
                            address = @Address,
                            zip_code = @ZipCode,
                            email = @Email,
                            logo_url = @LogoUrl,
                            subsidiary_no = @SubsidiaryNo,
                            homepage = @Homepage,
                            initial_date = @InitialDate,
                            e_invoice_server = @EInvoiceServer,
                            e_invoice_id = @EInvoiceId,
                            e_invoice_enabled = @EInvoiceEnabled,
                            tax_type = @TaxType,
                            fiscal_month = @FiscalMonth,
                            updated_at = NOW(6)
                        WHERE tenant_id = @TenantId
                        """,
                        new
                        {
                            TenantId = tenantId,
                            BizType = dto.BizType,
                            BizItem = dto.BizItem,
                            Tel = dto.Tel,
                            Fax = dto.Fax,
                            Address = dto.Address,
                            ZipCode = dto.ZipCode,
                            Email = dto.Email,
                            LogoUrl = dto.LogoUrl,
                            SubsidiaryNo = dto.SubsidiaryNo,
                            Homepage = dto.Homepage,
                            InitialDate = dto.InitialDate,
                            EInvoiceServer = dto.EInvoiceServer,
                            EInvoiceId = dto.EInvoiceId,
                            EInvoiceEnabled = dto.EInvoiceEnabled ? 1 : 0,
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
                        biz_type = @BizType,
                        biz_item = @BizItem,
                        tel = @Tel,
                        fax = @Fax,
                        address = @Address,
                        zip_code = @ZipCode,
                        email = @Email,
                        logo_url = @LogoUrl,
                        corp_no = @CorpNo,
                        subsidiary_no = @SubsidiaryNo,
                        homepage = @Homepage,
                        initial_date = @InitialDate,
                        e_invoice_server = @EInvoiceServer,
                        e_invoice_id = @EInvoiceId,
                        e_invoice_enabled = @EInvoiceEnabled,
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
                        CorpNo = dto.CorpNo,
                        SubsidiaryNo = dto.SubsidiaryNo,
                        Homepage = dto.Homepage,
                        InitialDate = dto.InitialDate,
                        EInvoiceServer = dto.EInvoiceServer,
                        EInvoiceId = dto.EInvoiceId,
                        EInvoiceEnabled = dto.EInvoiceEnabled ? 1 : 0,
                        TaxType = dto.TaxType,
                        FiscalMonth = dto.FiscalMonth
                    },
                    cancellationToken: ct)).ConfigureAwait(false);
            return;
        }

        // 비확장(베이스) 경로 — 잠긴 경우 tel·address만 갱신, 핵심 3필드 유지
        if (isLocked)
        {
            await _db.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE tenants SET
                        tel = @Tel,
                        address = @Address,
                        updated_at = NOW(6)
                    WHERE tenant_id = @TenantId
                    """,
                    new { TenantId = tenantId, dto.Tel, dto.Address },
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
