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
        //
        // 🔴 2026-08-10 확장 (사장님 결재 · 20260810작1 T7 · [3-V] 병렬검증 적발):
        //   종전엔 여기서도 핵심 3필드(회사명·사업자번호·대표자명)만 지키고,
        //   업태·종목·주소·우편번호는 잠긴 상태에서도 그대로 갱신했다.
        //   그래서 SettingsService 쪽 잠금을 8필드로 넓혀도 **이 경로로 우회**할 수 있었다.
        //   같은 값을 쓰는 두 경로가 서로 다른 규칙을 가지면 느슨한 쪽이 곧 구멍이다.
        //   ⇒ 두 경로의 잠금 규칙을 같게 맞춘다(등록증 기재 8필드).
        //
        //   ⚠️ 단 "값이 이미 있을 때만" 잠근다. 기존 값이 비어 있는 칸까지 잠그면
        //      고객이 영원히 못 채우는 칸이 된다(SettingsService 와 동일 규칙).
        var lockedRow = await _db.QueryFirstOrDefaultAsync<CompanyLockRow>(
            new CommandDefinition(
                """
                SELECT
                  is_locked_from_landing AS IsLocked,
                  biz_type  AS BizType,
                  biz_item  AS BizItem,
                  zip_code  AS ZipCode,
                  address   AS Address
                FROM local_company
                WHERE tenant_id = @TenantId
                """,
                new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        var isLocked = lockedRow?.IsLocked == 1;

        if (isLocked)
        {
            // 잠긴 경우 — 등록증 기재 항목은 기존 값 유지, 등록증에 없는 항목(연락처 등)만 갱신.
            static string? Keep(string? current, string? incoming)
                => !string.IsNullOrWhiteSpace(current) ? current : incoming;

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
                        // 등록증 기재 항목 — 기존 값이 있으면 그것을 유지한다(변경 거부).
                        BizType = Keep(lockedRow?.BizType, dto.BizType),
                        BizItem = Keep(lockedRow?.BizItem, dto.BizItem),
                        Address = Keep(lockedRow?.Address, dto.Address),
                        ZipCode = Keep(lockedRow?.ZipCode, dto.ZipCode),
                        // 등록증에 없는 항목 — 고객이 자유롭게 고친다.
                        Tel = dto.Tel,
                        Fax = dto.Fax,
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

    /// <summary>
    /// 잠금 판정에 필요한 현재 값. 등록증 기재 항목은 기존 값이 있으면 변경을 거부한다.
    /// (2026-08-10 · 20260810작1 T7)
    /// </summary>
    private sealed class CompanyLockRow
    {
        public int IsLocked { get; set; }

        public string? BizType { get; set; }

        public string? BizItem { get; set; }

        public string? ZipCode { get; set; }

        public string? Address { get; set; }
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
