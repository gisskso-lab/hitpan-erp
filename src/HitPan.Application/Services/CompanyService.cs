using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Company;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

// 헌법 #35 객체 완전 분리 (사장님 결재 2026-06-04):
//   - 회사정보 마스터 = ERP 로컬 DB의 local_company (고객사 PC)
//   - 백오피스 DB의 tenants는 본사 영역(라이선스·구독·AI·기기·대리점)만, ERP가 직접 참조 0건
//   - tenant_id는 양쪽 동일값 저장 (FK 정합 보존)
public sealed class CompanyService : ICompanyService
{
    private readonly IDbConnection _db;

    public CompanyService(IDbConnection db)
    {
        _db = db;
    }

    public async Task<CompanyDto?> GetAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

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
                fiscal_month AS FiscalMonth,
                is_locked_from_landing AS IsLockedFromLanding
            FROM local_company
            WHERE tenant_id = @TenantId
            """;

        return await _db.QueryFirstOrDefaultAsync<CompanyDto>(
            new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task UpdateAsync(UpdateCompanyDto dto, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 헌법 #35 (사장님 결재 2026-06-04) — 랜딩 자동 반영된 회사정보는 ERP 내 수정 금지.
        // 핵심 필드 3건(회사명·사업자번호·대표자명)은 잠금. 변경하려면 랜딩 사업자등록증 재등록.
        var lockedRaw = await _db.QueryFirstOrDefaultAsync<int?>(
            new CommandDefinition(
                "SELECT is_locked_from_landing FROM local_company WHERE tenant_id = @TenantId",
                new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        var isLocked = lockedRaw == 1;

        if (isLocked)
        {
            // 잠긴 경우 — 핵심 3필드는 기존 값 유지, 나머지(연락처·주소·이메일·업태·종목 등)만 갱신
            await _db.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE local_company SET
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
                UPDATE local_company SET
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
}
