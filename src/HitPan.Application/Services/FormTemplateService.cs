using Dapper;
using HitPan.Application.DTOs.Settings;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

// 양식정보설정 Service (사장님 작업지시 2026-05-31 작지②·③)
// 1 form_type당 1 active default 보장
public class FormTemplateService : IFormTemplateService
{
    // 사장님 지시 2026-08-11 (챕터3):
    //   "견적서, 세금계산서, 거래명세서 수주서 발주서 반품명세서 매입명세서 등의 모든 양식"
    //
    // 🔴 봉합: 종전 6종만 생성돼 sales_return(판매반품)·tax_invoice(세금계산서)가 빠져 있었다.
    //   FormTypeNames 에는 8종이 다 정의돼 있는데 여기만 6종이라, 화면에서 [기본 양식 만들기]
    //   를 눌러도 그 2종은 만들어지지 않았다. 이름만 있고 실물이 없는 상태였다.
    //   ⇒ 세금계산서는 법정 서식이라 빠지면 판매 흐름이 끊긴다(헌법 #20).
    private static readonly string[] DefaultFormTypes =
    {
        "estimate", "sales_order", "delivery", "tax_invoice", "invoice_exempt",
        "purchase_order", "receipt", "purchase_return", "sales_return", "payment_receipt"
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
        ["tax_invoice"] = "세금계산서",
        // 사장님 결재 2026-08-11 (챕터3 조사분) — 8종 → 10종
        //   계산서: 유일한 **법정 서식** 누락이었다. 면세사업자(농수축산·출판·교육·의료)는
        //     세금계산서를 발행할 수 없고 계산서를 발급해야 하는데, 그들이 쓸 문서가 아예 없었다.
        //     세금계산서와 서식이 같고 세액란만 빠진다 ⇒ 2매 요건도 동일하게 적용된다.
        //   입금표: 종전 8종이 전부 "물건이 오간 기록"이고 "돈이 들어온 기록"이 0건이었다.
        //     도소매 수금 현장의 실물이라 [6]재무로 넘어가려면 어차피 메워야 할 구멍이다.
        ["invoice_exempt"] = "계산서",
        ["payment_receipt"] = "입금표"
    };

    /// <summary>
    /// 공급자·공급받는자 2매 발행이 필요한 양식 (사장님 지시 2026-08-11).
    /// </summary>
    /// <remarks>
    /// 세금계산서·계산서는 <b>법정 2매</b>다 — 부가가치세법 시행규칙이 적색(공급자보관용)·
    /// 청색(공급받는자보관용) 2매 작성, 1매 교부, 5년 보관을 요구한다. 지킬 수 없으면 위법이다.
    /// 거래명세서는 법정은 아니나 관행상 2장(공급자는 수령 확인 후 보관, 상대에게 1장 교부)이다.
    /// 나머지(견적·수주·발주·반품 등)는 2매를 요구하는 근거를 찾지 못해 넣지 않는다 —
    /// 근거 없이 넣으면 인쇄 옵션만 늘어 화면이 어려워진다(히트판 철학).
    /// </remarks>
    private static readonly HashSet<string> TwoCopyFormTypes = new()
    {
        "tax_invoice", "invoice_exempt", "delivery"
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
                print_copy_mode AS PrintCopyMode,
                style_key AS StyleKey,
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
                 print_copy_mode, style_key,
                 is_default, is_active)
              VALUES
                (@TemplateId, @TenantId, @FormType, @TemplateName, @PaperMode,
                 @PaperSize, @Orientation,
                 @MarginTopMm, @MarginLeftMm, @MarginRightMm, @MarginBottomMm,
                 @FieldCoordsJson, @BackgroundImageUrl,
                 @ShowCompanyLogo, @ShowCompanySeal, @ShowBorder,
                 @PrintCopyMode, @StyleKey,
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
                // 요청에 값이 없으면(옛 화면·API 호출) 그 양식의 기본 매수를 쓴다.
                //   세금계산서·계산서를 recipient 로 만들면 법정 2매 요건을 어기게 된다.
                PrintCopyMode = ResolvePrintCopyMode(request.FormType, request.PrintCopyMode),
                StyleKey = string.IsNullOrWhiteSpace(request.StyleKey) ? "basic" : request.StyleKey,
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
            // DB 에 실제로 저장된 값과 같아야 한다 — 요청이 비었을 때 서버가 정한 기본을 그대로 돌려준다.
            //   여기서 request 값을 그냥 넘기면 화면은 recipient 로 알고 DB 는 both 인 어긋남이 난다.
            PrintCopyMode = ResolvePrintCopyMode(request.FormType, request.PrintCopyMode),
            StyleKey = string.IsNullOrWhiteSpace(request.StyleKey) ? "basic" : request.StyleKey,
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
                print_copy_mode = @PrintCopyMode,
                style_key = @StyleKey,
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
                PrintCopyMode = ResolvePrintCopyMode(request.FormType, request.PrintCopyMode),
                StyleKey = string.IsNullOrWhiteSpace(request.StyleKey) ? "basic" : request.StyleKey,
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
                     show_border AS ShowBorder,
                     print_copy_mode AS PrintCopyMode, style_key AS StyleKey,
                     is_default AS IsDefault, is_active AS IsActive
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

    /// <summary>
    /// 인쇄 매수를 정한다 (DB-90). 값이 비면 그 양식의 법정·관행 기본값을 쓴다.
    /// </summary>
    /// <remarks>
    /// 빈 값을 그냥 recipient 로 떨어뜨리면 <b>세금계산서가 1장만 찍혀 법정 2매 요건을 어긴다.</b>
    /// 옛 화면이나 옛 API 호출은 이 값을 안 보내므로, 여기서 양식별 기본을 세워 준다.
    /// </remarks>
    private static string ResolvePrintCopyMode(string formType, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            ValidatePrintCopyMode(requested);
            return requested;
        }
        return TwoCopyFormTypes.Contains(formType) ? "both" : "recipient";
    }

    private static void ValidatePrintCopyMode(string mode)
    {
        if (mode != "both" && mode != "recipient" && mode != "supplier")
        {
            throw new InvalidOperationException(
                $"인쇄 매수는 'both'·'recipient'·'supplier' 만 허용됩니다. (입력: {mode})");
        }
    }

    private static void ValidatePaperMode(string paperMode)
    {
        if (paperMode != "plain" && paperMode != "preprint")
        {
            throw new InvalidOperationException($"paper_mode는 'plain' 또는 'preprint'만 허용됩니다. (입력: {paperMode})");
        }
    }
}
