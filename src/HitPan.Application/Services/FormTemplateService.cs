using Dapper;
using HitPan.Application.DTOs.Settings;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

// 양식정보설정 Service (사장님 작업지시 2026-05-31 작지②·③)
// 1 form_type당 1 active default 보장
public class FormTemplateService : IFormTemplateService
{
    private static readonly string[] DefaultFormTypes =
    {
        "estimate", "sales_order", "delivery",
        "purchase_order", "receipt", "purchase_return"
    };

    private static readonly Dictionary<string, string> FormTypeNames = new()
    {
        ["estimate"] = "견적서",
        ["sales_order"] = "수주서",
        ["delivery"] = "거래명세서",
        ["purchase_order"] = "발주서",
        ["receipt"] = "매입명세서",
        ["purchase_return"] = "매입반품",
        ["sales_return"] = "판매반품",
        ["tax_invoice"] = "세금계산서"
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<FormTemplateService> _logger;

    public FormTemplateService(IUnitOfWork unitOfWork, ILogger<FormTemplateService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FormTemplateDto>> ListAsync(string tenantId, string? formType = null, bool activeOnly = true, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("tenant_id required", nameof(tenantId));

        var db = _unitOfWork.GetDbConnection();
        var sql = @"SELECT
                template_id AS TemplateId,
                form_type AS FormType,
                template_name AS TemplateName,
                paper_mode AS PaperMode,
                paper_size AS PaperSize,
                orientation AS Orientation,
                margin_top_mm AS MarginTopMm,
                margin_left_mm AS MarginLeftMm,
                margin_right_mm AS MarginRightMm,
                margin_bottom_mm AS MarginBottomMm,
                field_coords_json AS FieldCoordsJson,
                background_image_url AS BackgroundImageUrl,
                show_company_logo AS ShowCompanyLogo,
                show_company_seal AS ShowCompanySeal,
                show_border AS ShowBorder,
                is_default AS IsDefault,
                is_active AS IsActive
            FROM form_templates
            WHERE tenant_id = @TenantId
              AND (@FormType IS NULL OR form_type = @FormType)
              AND (@ActiveOnly = 0 OR is_active = 1)
            ORDER BY form_type, is_default DESC, template_name";

        var rows = await db.QueryAsync<FormTemplateDto>(new CommandDefinition(sql,
            new { TenantId = tenantId, FormType = formType, ActiveOnly = activeOnly ? 1 : 0 },
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<FormTemplateDto?> GetDefaultAsync(string tenantId, string formType, CancellationToken ct = default)
    {
        var list = await ListAsync(tenantId, formType, activeOnly: true, ct);
        return list.FirstOrDefault(t => t.IsDefault) ?? list.FirstOrDefault();
    }

    public async Task<FormTemplateDto> CreateAsync(string tenantId, CreateFormTemplateRequest request, CancellationToken ct = default)
    {
        ValidatePaperMode(request.PaperMode);

        var templateId = Guid.NewGuid().ToString();
        var db = _unitOfWork.GetDbConnection();

        if (request.IsDefault)
        {
            await db.ExecuteAsync(new CommandDefinition(
                @"UPDATE form_templates SET is_default = 0
                  WHERE tenant_id = @TenantId AND form_type = @FormType AND is_default = 1",
                new { TenantId = tenantId, request.FormType }, cancellationToken: ct));
        }

        await db.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO form_templates
                (template_id, tenant_id, form_type, template_name, paper_mode,
                 paper_size, orientation,
                 margin_top_mm, margin_left_mm, margin_right_mm, margin_bottom_mm,
                 field_coords_json, background_image_url,
                 show_company_logo, show_company_seal, show_border,
                 is_default, is_active)
              VALUES
                (@TemplateId, @TenantId, @FormType, @TemplateName, @PaperMode,
                 @PaperSize, @Orientation,
                 @MarginTopMm, @MarginLeftMm, @MarginRightMm, @MarginBottomMm,
                 @FieldCoordsJson, @BackgroundImageUrl,
                 @ShowCompanyLogo, @ShowCompanySeal, @ShowBorder,
                 @IsDefault, 1)",
            new
            {
                TemplateId = templateId,
                TenantId = tenantId,
                request.FormType,
                request.TemplateName,
                request.PaperMode,
                request.PaperSize,
                request.Orientation,
                request.MarginTopMm,
                request.MarginLeftMm,
                request.MarginRightMm,
                request.MarginBottomMm,
                request.FieldCoordsJson,
                request.BackgroundImageUrl,
                ShowCompanyLogo = request.ShowCompanyLogo ? 1 : 0,
                ShowCompanySeal = request.ShowCompanySeal ? 1 : 0,
                ShowBorder = request.ShowBorder ? 1 : 0,
                IsDefault = request.IsDefault ? 1 : 0
            },
            cancellationToken: ct));

        _logger.LogInformation("FormTemplate created: tenant={Tenant} type={Type} mode={Mode}",
            tenantId, request.FormType, request.PaperMode);

        return new FormTemplateDto
        {
            TemplateId = templateId,
            FormType = request.FormType,
            TemplateName = request.TemplateName,
            PaperMode = request.PaperMode,
            PaperSize = request.PaperSize,
            Orientation = request.Orientation,
            MarginTopMm = request.MarginTopMm,
            MarginLeftMm = request.MarginLeftMm,
            MarginRightMm = request.MarginRightMm,
            MarginBottomMm = request.MarginBottomMm,
            FieldCoordsJson = request.FieldCoordsJson,
            BackgroundImageUrl = request.BackgroundImageUrl,
            ShowCompanyLogo = request.ShowCompanyLogo,
            ShowCompanySeal = request.ShowCompanySeal,
            ShowBorder = request.ShowBorder,
            IsDefault = request.IsDefault,
            IsActive = true
        };
    }

    public async Task<FormTemplateDto> UpdateAsync(string tenantId, string templateId, UpdateFormTemplateRequest request, CancellationToken ct = default)
    {
        ValidatePaperMode(request.PaperMode);

        var db = _unitOfWork.GetDbConnection();

        if (request.IsDefault)
        {
            await db.ExecuteAsync(new CommandDefinition(
                @"UPDATE form_templates SET is_default = 0
                  WHERE tenant_id = @TenantId AND form_type = @FormType
                    AND template_id <> @TemplateId AND is_default = 1",
                new { TenantId = tenantId, request.FormType, TemplateId = templateId },
                cancellationToken: ct));
        }

        var rows = await db.ExecuteAsync(new CommandDefinition(
            @"UPDATE form_templates SET
                template_name = @TemplateName,
                paper_mode = @PaperMode,
                paper_size = @PaperSize,
                orientation = @Orientation,
                margin_top_mm = @MarginTopMm,
                margin_left_mm = @MarginLeftMm,
                margin_right_mm = @MarginRightMm,
                margin_bottom_mm = @MarginBottomMm,
                field_coords_json = @FieldCoordsJson,
                background_image_url = @BackgroundImageUrl,
                show_company_logo = @ShowCompanyLogo,
                show_company_seal = @ShowCompanySeal,
                show_border = @ShowBorder,
                is_default = @IsDefault,
                is_active = @IsActive
              WHERE tenant_id = @TenantId AND template_id = @TemplateId",
            new
            {
                TenantId = tenantId,
                TemplateId = templateId,
                request.TemplateName,
                request.PaperMode,
                request.PaperSize,
                request.Orientation,
                request.MarginTopMm,
                request.MarginLeftMm,
                request.MarginRightMm,
                request.MarginBottomMm,
                request.FieldCoordsJson,
                request.BackgroundImageUrl,
                ShowCompanyLogo = request.ShowCompanyLogo ? 1 : 0,
                ShowCompanySeal = request.ShowCompanySeal ? 1 : 0,
                ShowBorder = request.ShowBorder ? 1 : 0,
                IsDefault = request.IsDefault ? 1 : 0,
                IsActive = request.IsActive ? 1 : 0
            },
            cancellationToken: ct));

        if (rows == 0) throw new InvalidOperationException($"양식 템플릿을 찾을 수 없습니다. template_id={templateId}");

        var updated = await db.QueryFirstAsync<FormTemplateDto>(new CommandDefinition(
            @"SELECT template_id AS TemplateId, form_type AS FormType, template_name AS TemplateName,
                     paper_mode AS PaperMode, paper_size AS PaperSize, orientation AS Orientation,
                     margin_top_mm AS MarginTopMm, margin_left_mm AS MarginLeftMm,
                     margin_right_mm AS MarginRightMm, margin_bottom_mm AS MarginBottomMm,
                     field_coords_json AS FieldCoordsJson, background_image_url AS BackgroundImageUrl,
                     show_company_logo AS ShowCompanyLogo, show_company_seal AS ShowCompanySeal,
                     show_border AS ShowBorder, is_default AS IsDefault, is_active AS IsActive
              FROM form_templates WHERE template_id = @TemplateId",
            new { TemplateId = templateId }, cancellationToken: ct));

        return updated;
    }

    public async Task DeactivateAsync(string tenantId, string templateId, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        await db.ExecuteAsync(new CommandDefinition(
            @"UPDATE form_templates SET is_active = 0, is_default = 0
              WHERE tenant_id = @TenantId AND template_id = @TemplateId",
            new { TenantId = tenantId, TemplateId = templateId }, cancellationToken: ct));
        _logger.LogInformation("FormTemplate deactivated: tenant={Tenant} template={Template}", tenantId, templateId);
    }

    public async Task SeedDefaultsAsync(string tenantId, CancellationToken ct = default)
    {
        var existing = await ListAsync(tenantId, null, activeOnly: false, ct);
        var existingTypes = new HashSet<string>(existing.Select(e => e.FormType));

        var toSeed = DefaultFormTypes.Where(t => !existingTypes.Contains(t)).ToList();
        if (toSeed.Count == 0) return;

        foreach (var ftype in toSeed)
        {
            await CreateAsync(tenantId, new CreateFormTemplateRequest
            {
                FormType = ftype,
                TemplateName = FormTypeNames.GetValueOrDefault(ftype, ftype) + " (기본 순백지)",
                PaperMode = "plain",
                PaperSize = "A4",
                Orientation = "portrait",
                MarginTopMm = 15m,
                MarginLeftMm = 15m,
                MarginRightMm = 15m,
                MarginBottomMm = 15m,
                ShowCompanyLogo = true,
                ShowCompanySeal = true,
                ShowBorder = true,
                IsDefault = true
            }, ct);
        }

        _logger.LogInformation("FormTemplate defaults seeded: tenant={Tenant} count={Count}", tenantId, toSeed.Count);
    }

    private static void ValidatePaperMode(string paperMode)
    {
        if (paperMode != "plain" && paperMode != "preprint")
        {
            throw new InvalidOperationException($"paper_mode는 'plain' 또는 'preprint'만 허용됩니다. (입력: {paperMode})");
        }
    }
}
