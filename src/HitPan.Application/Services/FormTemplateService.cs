using System.Data.Common;
using System.Security.Cryptography;
using Dapper;
using HitPan.Application.DTOs.Settings;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;
using MySqlConnector;

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
                -- 🔴 20261021작 §10-4 S-2 (DB-147) — 안 읽으면 고객이 켜도 종이가 안 바뀐다.
                show_tear_line AS ShowTearLine,
                -- 🔴 20261026작1 §A-3 (DB-148) — 같은 까닭. 안 읽으면 켜도 종이가 안 바뀐다.
                --   수치 셋은 NULL 을 그대로 들고 온다 — NULL = 「공통값을 따른다」.
                print_background AS PrintBackground,
                show_watermark AS ShowWatermark,
                watermark_opacity AS WatermarkOpacity,
                watermark_saturate AS WatermarkSaturate,
                watermark_scale_pct AS WatermarkScalePct,
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

    // 🔴 [2026-11-19 W-G1-51 갈래 F-1 · 설계 §7-4 D-7 정정(고침 7차 · CTO Q1) · PM 재결재 7차 Q1 = 갈래 F]
    //   저장 문이 유일 키 충돌(1062)을 만나면 **사람 말 거절 · 500 0 · 행 0**.
    //   [언제 나나] DB-150 이 안 걸린 DB(② 건너뜀 · 옛 키 그대로)에서 같은 종 양식을 여럿 두려 할 때 ·
    //     새 키 아래에서는 두 화면이 같은 순간에 기본 양식을 바꿀 때.
    //   [행 0] 종전 본문은 「옛 기본 내림」 과 「저장」 을 따로 보냈다 → 뒤 줄이 1062 면 앞 줄만 남아 기본 양식이 사라졌다.
    //     ⇒ 본문을 한 트랜잭션에 싣는다. 본문 글자는 그대로 아래 `CreateCoreAsync` 로 이름만 옮겼다(#1).
    //   🔴 화면 응답 = 컨트롤러가 이미 `InvalidOperationException` 을 400 + 그 문구로 돌려준다(컨트롤러 무변).
    public async Task<FormTemplateDto> CreateAsync(string tenantId, CreateFormTemplateRequest request, CancellationToken ct = default)
    {
        // 🔴 [W-G1-51 갈래 F-1 · 편집창 고침 1 · [3-V] P3-5 · PM 판단] 그림 경로 앞뒤 공백은 서버 저장 입구에서도 자른다.
        request.BackgroundImageUrl = TrimBackgroundPath(request.BackgroundImageUrl);
        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var created = await CreateCoreAsync(tenantId, request, tx.DbTransaction, ct);
            await tx.CommitAsync(ct);
            return created;
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            _logger.LogWarning(ex, "[양식] 저장 거절 — 유일 키 충돌(1062) · 행 0 · tenant={Tenant} type={Type}",
                tenantId, request.FormType);
            throw new InvalidOperationException(DuplicateTemplateMessage(ex));
        }
        // 🔴 [2026-11-20 W-G1-51 갈래 F-1 · 편집창 부분 · 고침 1·2 좁은 재셈 [4] P3-A · PM 판단] 교착(1213)도 사람 말 · 500 0.
        //   트랜잭션은 DB 가 이미 되돌렸다 = 행 0. 고객은 잠시 뒤 다시 저장하면 된다(1062 줄 옆 1줄 · 컨트롤러 무변 = 400 + 문구).
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.LockDeadlock)
        {
            _logger.LogWarning(ex, "[양식] 저장 거절 — 교착(1213) · 행 0 · tenant={Tenant} type={Type}",
                tenantId, request.FormType);
            throw new InvalidOperationException(SaveDeadlockMessage);
        }
    }

    // 종전 `CreateAsync` 본문(W-G1-51 F-1 — 이름만 옮김 · 트랜잭션 인자 더함 · 양식지 그림 찍기 0 강제 더함).
    private async Task<FormTemplateDto> CreateCoreAsync(string tenantId, CreateFormTemplateRequest request, DbTransaction tx, CancellationToken ct)
    {
        ValidatePaperMode(request.PaperMode);

        var templateId = Guid.NewGuid().ToString();
        var db = _unitOfWork.GetDbConnection();
        // 🔴 W-G1-51 F-1 (설계 §7-3 · G5 ⑶ · 측정 ⓘ-11) — 양식지(`preprint`)는 「종이에도 함께 인쇄」 를 저장하지 않는다.
        //   양식지 그림을 종이에도 찍으려면 Y 템플릿(빈 A4 · 그림 찍음)으로 간다(대표 11/18 08시② · Q14).
        var printBackgroundToSave = ResolvePrintBackgroundToSave(request.PaperMode, request.PrintBackground);

        if (request.IsDefault)
        {
            await db.ExecuteAsync(new CommandDefinition(
                @"UPDATE form_templates SET is_default = 0
                  WHERE tenant_id = @TenantId AND form_type = @FormType AND is_default = 1",
                new { TenantId = tenantId, request.FormType }, transaction: tx, cancellationToken: ct));
        }

        await db.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO form_templates
                (template_id, tenant_id, form_type, template_name, paper_mode,
                 paper_size, orientation,
                 margin_top_mm, margin_left_mm, margin_right_mm, margin_bottom_mm,
                 field_coords_json, background_image_url,
                 show_company_logo, show_company_seal, show_border,
                 print_copy_mode, style_key, show_tear_line,
                 print_background, show_watermark,
                 watermark_opacity, watermark_saturate, watermark_scale_pct,
                 is_default, is_active)
              VALUES
                (@TemplateId, @TenantId, @FormType, @TemplateName, @PaperMode,
                 @PaperSize, @Orientation,
                 @MarginTopMm, @MarginLeftMm, @MarginRightMm, @MarginBottomMm,
                 @FieldCoordsJson, @BackgroundImageUrl,
                 @ShowCompanyLogo, @ShowCompanySeal, @ShowBorder,
                 @PrintCopyMode, @StyleKey, @ShowTearLine,
                 @PrintBackground, @ShowWatermark,
                 @WatermarkOpacity, @WatermarkSaturate, @WatermarkScalePct,
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
                ShowTearLine = request.ShowTearLine ? 1 : 0,
                // 🔴 DB-148 — 수치 셋은 null 을 **null 그대로** 넣는다.
                //   null = 「공통값을 따른다」이므로 여기서 기본값을 채워 넣으면
                //   그 양식은 공통 설정을 바꿔도 영영 안 따라온다 (0.00 과 null 은 다른 뜻).
                // 종전: PrintBackground = request.PrintBackground ? 1 : 0,
                PrintBackground = printBackgroundToSave ? 1 : 0,   // W-G1-51 F-1 — 양식지 = 0 강제
                // 🔴 [R-2 봉합] 켬/끔도 세 값이다 — null(공통 따름) 을 null 그대로 넣는다.
                //   종전에는 `? 1 : 0` 으로 **null 을 0 으로 바꿔** 넣어서,
                //   양식을 한 번 저장한 고객에게 공통 켜기가 영영 안 닿았다.
                request.ShowWatermark,
                request.WatermarkOpacity,
                request.WatermarkSaturate,
                request.WatermarkScalePct,
                IsDefault = request.IsDefault ? 1 : 0
            },
            transaction: tx,
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
            ShowTearLine = request.ShowTearLine,
            // DB-148 — 위 INSERT 에 넣은 값과 같아야 한다(요청값을 그대로 돌려준다).
            // 종전: PrintBackground = request.PrintBackground,
            PrintBackground = printBackgroundToSave,   // W-G1-51 F-1 — DB 에 실제로 넣은 값(양식지 = false)
            ShowWatermark = request.ShowWatermark,
            WatermarkOpacity = request.WatermarkOpacity,
            WatermarkSaturate = request.WatermarkSaturate,
            WatermarkScalePct = request.WatermarkScalePct,
            IsDefault = request.IsDefault,
            IsActive = true
        };
    }

    // 🔴 [2026-11-19 W-G1-51 갈래 F-1] `CreateAsync` 와 같은 까닭 — 1062 = 사람 말 · 500 0 · 행 0(한 트랜잭션).
    public async Task<FormTemplateDto> UpdateAsync(string tenantId, string templateId, UpdateFormTemplateRequest request, CancellationToken ct = default)
    {
        // 🔴 [W-G1-51 갈래 F-1 · 편집창 고침 1 · [3-V] P3-5 · PM 판단] 만들기와 같은 자름(아래 그림 찍기 유지 판정도 자른 값을 본다).
        request.BackgroundImageUrl = TrimBackgroundPath(request.BackgroundImageUrl);
        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var updated = await UpdateCoreAsync(tenantId, templateId, request, tx.DbTransaction, ct);
            await tx.CommitAsync(ct);
            return updated;
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            _logger.LogWarning(ex, "[양식] 저장 거절 — 유일 키 충돌(1062) · 행 0 · tenant={Tenant} template={Template}",
                tenantId, templateId);
            throw new InvalidOperationException(DuplicateTemplateMessage(ex));
        }
        // 🔴 [2026-11-20 W-G1-51 갈래 F-1 · 편집창 부분 · [4] P3-A · PM 판단] 교착(1213) — 고침 2 로 잠금 순서가
        //   「내 행 FOR UPDATE → 같은 종 다른 행」 이 되어, 기본 지정 저장과 옮기기 1회가 같은 순간 같은 종을 잡으면 날 수 있다.
        //   사람 말 · 500 0 · 행 0(DB 가 트랜잭션을 되돌림) · 1062 줄 옆 1줄.
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.LockDeadlock)
        {
            _logger.LogWarning(ex, "[양식] 저장 거절 — 교착(1213) · 행 0 · tenant={Tenant} template={Template}",
                tenantId, templateId);
            throw new InvalidOperationException(SaveDeadlockMessage);
        }
    }

    // 종전 `UpdateAsync` 본문(W-G1-51 F-1 — 이름만 옮김 · 트랜잭션 인자 더함 · 양식지 그림 찍기 0 강제 더함).
    private async Task<FormTemplateDto> UpdateCoreAsync(string tenantId, string templateId, UpdateFormTemplateRequest request, DbTransaction tx, CancellationToken ct)
    {
        ValidatePaperMode(request.PaperMode);

        var db = _unitOfWork.GetDbConnection();
        // 🔴 W-G1-51 F-1 — 양식지 저장 = `print_background` 0 강제(CreateCoreAsync 와 같은 규칙 · 측정 ⓘ-11).
        // 종전(고침 2 전): var printBackgroundToSave = ResolvePrintBackgroundToSave(request.PaperMode, request.PrintBackground);
        //   🔴 고침 2(Ⓐ · CTO 판정 (나) 조건 7) — 고치기는 서버가 DB 현재값으로 정한다(같은 트랜잭션 FOR UPDATE 읽기 → C# 판정 → 아래 UPDATE).
        //   이미 켬 · 그림 무변인 양식지는 1 유지(옮기기가 내리고 Y 를 만듦 · Q14 · Q16) · 그 밖 양식지 = 0(ⓘ-11 「새로 켜는 요청」).
        var printBackgroundToSave = await ResolvePrintBackgroundToSaveOnUpdateAsync(db, tx, tenantId, templateId, request, ct);

        if (request.IsDefault)
        {
            await db.ExecuteAsync(new CommandDefinition(
                @"UPDATE form_templates SET is_default = 0
                  WHERE tenant_id = @TenantId AND form_type = @FormType
                    AND template_id <> @TemplateId AND is_default = 1",
                new { TenantId = tenantId, request.FormType, TemplateId = templateId },
                transaction: tx,
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
                show_tear_line = @ShowTearLine,
                print_background = @PrintBackground,
                show_watermark = @ShowWatermark,
                watermark_opacity = @WatermarkOpacity,
                watermark_saturate = @WatermarkSaturate,
                watermark_scale_pct = @WatermarkScalePct,
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
                ShowTearLine = request.ShowTearLine ? 1 : 0,
                // 🔴 DB-148 — 수치 셋은 null 을 null 그대로 넘긴다(「공통값을 따른다」를 지키려면).
                // 종전: PrintBackground = request.PrintBackground ? 1 : 0,
                PrintBackground = printBackgroundToSave ? 1 : 0,   // W-G1-51 F-1 — 양식지 = 0 강제
                // 🔴 [R-2 봉합] 켬/끔도 세 값이다 — null(공통 따름) 을 null 그대로 넣는다.
                //   종전에는 `? 1 : 0` 으로 **null 을 0 으로 바꿔** 넣어서,
                //   양식을 한 번 저장한 고객에게 공통 켜기가 영영 안 닿았다.
                request.ShowWatermark,
                request.WatermarkOpacity,
                request.WatermarkSaturate,
                request.WatermarkScalePct,
                IsDefault = request.IsDefault ? 1 : 0,
                IsActive = request.IsActive ? 1 : 0
            },
            transaction: tx,
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
                     show_tear_line AS ShowTearLine,
                     print_background AS PrintBackground, show_watermark AS ShowWatermark,
                     watermark_opacity AS WatermarkOpacity, watermark_saturate AS WatermarkSaturate,
                     watermark_scale_pct AS WatermarkScalePct,
                     is_default AS IsDefault, is_active AS IsActive
              FROM form_templates WHERE template_id = @TemplateId",
            new { TemplateId = templateId }, transaction: tx, cancellationToken: ct));

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

    // ════════════════════════════════════════════════════════════════════
    // 🔴 W-G1-51 갈래 F-1 (2026-11-19) — Y 템플릿 만들기 · 켠 양식지 옮기기
    //   근거: 설계 20261118_WG1-51 §7-3(Q14 · Q15 · Q16 · 한 함수 · 멱등 · 재전진) · §7-4(D-7 · D-8 ⓑ 조건 D0~D7)
    //         CTO 판정 20261118_WG1-51_H1 §0 ② Q18 (가) · ③ D-8 ⓑ · §3 Q1 · Q2 · D0~D7 · 착수준비표 §1 F-1
    //   🔴 Y 템플릿 = 양식지 행 1개 복제 — 새 template_id · 같은 form_type · paper_mode='plain' · 칸 좌표 · 여백 ·
    //     워터마크 값 복사 · print_background=1 · 이름 = 원래 이름 + 「(양식 그림 포함)」 · 그림 파일 = **복사**(참조 아님 —
    //     원본에서 그림을 지워도 Y 그림이 살아야 한다 · PM Q15 조건 · 측정 ⓘ-5).
    //   🔴 tenant_id = 인자로만(조회 · UPDATE · INSERT 모두 tenant_id 조건 · 새 행 tenant_id = 원본 값 · H1 3인 G2).
    //     Y 단추 = JWT 테넌트 · 옮기기 = DB 행의 tenant_id 를 순회해 넘긴다(#2 — 새 API 인자 0).
    //   🔴 한 (회사 · 종) = 한 트랜잭션 · 그 종 행을 잠근 뒤 실효 기본을 **한 번** 뜬다(CTO D5 · J5 ⑴).
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Y 템플릿 이름 꼬리(설계 §7-3 · #23 점검 — 개발 용어 0).</summary>
    public const string YTemplateNameSuffix = " (양식 그림 포함)";

    /// <summary>옮기기 배경 확인 간격(설계 §7-4 D-8 ⓑ 조건 D4 · PM 재결재 7차 🟢 30초 · 코드 상수 · #21 설정 파일 0).</summary>
    public static readonly TimeSpan YMigrationInterval = TimeSpan.FromSeconds(30);

    /// <summary>옮기기 배경 확인 기한 = 기동 뒤 10분(D4 · 넘으면 다음 기동까지 쉼).</summary>
    public static readonly TimeSpan YMigrationDeadline = TimeSpan.FromMinutes(10);

    private const int TemplateNameMaxChars = 100;   // 출하 DDL form_templates.template_name varchar(100)

    // 기동마다 「적용 결과 표 없음(1146)」 경고를 한 번만 남긴다(D3 · D4 회차마다 0).
    private static int s_applyStatusTableMissingWarned;

    // 고침 1([4] 참고-3) — 판 글자를 못 읽어 옮기기가 안 도는 경우 경고를 프로세스당 한 번만 남긴다(30초 회차마다 0).
    private static int s_versionTextUnreadableWarned;

    // 🔴 [2026-11-20 W-G1-51 갈래 F-1 · 편집창 고침 1 · [4] P3-3 · PM 판단] 결과 형식 두 개(YTemplateOutcome · YTemplateResult)는
    //   인터페이스와 같은 층(`Interfaces/IFormTemplateService.cs`)으로 옮겼다 — 이름 · 값 · 설명 글자 무변.
    //   이 파일은 `using HitPan.Application.Interfaces;` 로 같은 이름 그대로 쓴다.
    // ⬛ 옛 줄(주석 보존 · #1):
    //    /// <summary>Y 템플릿 만들기 결과.</summary>
    //    public enum YTemplateOutcome
    //    {
    //        /// <summary>새 Y 를 만들었다.</summary>
    //        Created,
    //        /// <summary>같은 그림(해시) · 같은 칸 좌표의 Y 가 이미 있어 새로 만들지 않았다(재전진 멱등 · ⓘ-10 · D-12).</summary>
    //        Reused,
    //        /// <summary>원본 그림 경로가 비었거나 파일이 없다 — 복제 0 · 원본 print_background=0(설계 §7-3 멱등 줄 · ⓘ-6).</summary>
    //        NoPicture,
    //        /// <summary>원본 그림이 그 회사 그림 폴더 밖이거나 링크를 지난다 — 복제 0 · 원본 무변(결정적 실패 · CTO Q2).</summary>
    //        OutsideTenantRoot,
    //        /// <summary>그 회사에 그 양식이 없다(남의 회사 양식 id 포함 · ⓘ-8).</summary>
    //        NotFound,
    //        /// <summary>양식지가 아니거나 꺼진 양식 — 대상 아님 · 원본 무변.</summary>
    //        NotTarget,
    //        /// <summary>
    //        /// 원본(또는 이어 쓸 후보 Y) 그림 파일을 지금 읽지 못했다 — 접근 거부 · 잠김 · 경로가 폴더 등 「없음」 이 아닌 읽기 실패.
    //        /// 그 행 무변(그림 찍기 켬 유지) · 다음 기동에 다시(고침 1 · [3-V] P2-1 · PM 판단).
    //        /// </summary>
    //        PictureUnreadable,
    //    }

    /// <summary>그림 파일 읽기 결과(고침 1) — 「없음」 은 파일 · 폴더가 진짜 없을 때만.</summary>
    private enum PictureFileState
    {
        Present,
        Missing,
        Unreadable,
    }

    // ⬛ 옛 줄(주석 보존 · #1 · 고침 1 [4] P3-3 — 인터페이스 층으로 옮김):
    //    /// <summary>Y 템플릿 만들기 결과 — 만든(또는 이어 쓴) Y 의 id 는 Created · Reused 일 때만.</summary>
    //    public sealed record YTemplateResult(YTemplateOutcome Outcome, string? YTemplateId);

    /// <summary>옮기기를 지금 돌려도 되나(D-8 ⓑ).</summary>
    public enum YMigrationReadiness
    {
        /// <summary>옮길 행 0(새 설치 포함) — 기다림 0.</summary>
        NoTargets,
        /// <summary>새 키가 없거나 옛 키와 공존 — 옮기기 0 · 다음 기동에 다시(설계 §7-3 선행 조건 · J4 · D-9).</summary>
        KeysNotReady,
        /// <summary>이 판의 업데이트 성공 기록이 아직 없다(표 없음 1146 포함 · D3) — 배경에서 기다림.</summary>
        NotConfirmed,
        /// <summary>돌려도 된다.</summary>
        Ready,
    }

    /// <summary>
    /// 🔴 Y 템플릿 만들기 — Y/N 의 Y 단추가 부르는 입구(설계 §7-3 「한 함수」 · 시험 1벌).
    /// 옮기기(<see cref="RunYMigrationOnceAsync"/>)도 같은 몸통(<see cref="CreateYTemplateCoreAsync"/>)을 부른다.
    /// </summary>
    /// <param name="tenantId">🔴 JWT 클레임에서 온 값만(#2).</param>
    /// <param name="templateId">원본 양식지 id — 그 회사 행이 아니면 <see cref="YTemplateOutcome.NotFound"/>.</param>
    /// <exception cref="InvalidOperationException">유일 키 충돌(1062) — 사람 말 · 행 0(CTO Q1).</exception>
    /// <exception cref="InvalidOperationException">교착(1213) — 사람 말 · 행 0(고침 1 · §60 ⓟ4).</exception>
    public async Task<YTemplateResult> CreateYTemplateAsync(string tenantId, string templateId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("tenant_id required", nameof(tenantId));

        var db = _unitOfWork.GetDbConnection();
        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        // 고침 1([4] P2-1 · [3-V] P2-2) — 이번 거래가 만든 그림 사본. 커밋 전에 빠져나가면(되돌림 · 예외) 지운다(finally).
        var newCopies = new List<string>();
        var commitStarted = false;
        try
        {
            // 🔴🔴 [W-G1-51 갈래 F-1 · 편집창 고침 1 · [3-V] P2-1 · PM 판단] 거래의 **첫 문장을 잠그는 읽기**로(옮기기 첫 문장
            //   `LockTypeAndReadEffectiveDefaultAsync` 와 같은 꼴). 종전 첫 문장은 잠그지 않는 읽기라 반복 읽기 격리에서
            //   읽기 시점이 **잠금 전에** 잡혔다 → 뒤의 실효 기본 · 원본 · 이어 쓸 후보 읽기가 옛 모습을 봤다
            //   (「예」 두 건이 겹치면 뒤 건이 앞 건의 Y 를 못 봐 같은 그림 Y 2개 · 사본 2개).
            //   이제 원본 행을 잠근 뒤(→ 그 종 행 잠금) 첫 일반 읽기가 시작된다 = 앞 건이 끝난 뒤 모습을 본다 → 겹친 「예」 2건 = Y 1개.
            //   잠금 순서 = 「내 행 → 같은 종 행」 — 저장 문(고치기 · `ResolvePrintBackgroundToSaveOnUpdateAsync`)과 같은 순서.
            // ⬛ 옛 줄(주석 보존 · #1):
            //    @"SELECT form_type FROM form_templates
            //      WHERE tenant_id = @TenantId AND template_id = @TemplateId",
            var formType = await db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
                @"SELECT form_type FROM form_templates
                  WHERE tenant_id = @TenantId AND template_id = @TemplateId
                  FOR UPDATE",
                new { TenantId = tenantId, TemplateId = templateId },
                transaction: tx.DbTransaction, cancellationToken: ct));
            if (formType is null)
            {
                return new YTemplateResult(YTemplateOutcome.NotFound, null);
            }

            var effectiveDefaultId = await LockTypeAndReadEffectiveDefaultAsync(
                db, tx.DbTransaction, tenantId, formType, ct);
            var result = await CreateYTemplateCoreAsync(
                db, tx.DbTransaction, tenantId, templateId, effectiveDefaultId, newCopies, ct);   // 고침 1: newCopies 인자 더함
            commitStarted = true;
            await tx.CommitAsync(ct);
            newCopies.Clear();   // 고침 1 — 확정된 사본은 지울 대상 아님
            LogYOutcome(tenantId, templateId, result);
            return result;
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            _logger.LogWarning(ex, "[양식] Y 템플릿 만들기 거절 — 유일 키 충돌(1062) · 행 0 · tenant={Tenant} template={Template}",
                tenantId, templateId);
            // 🔴 [W-G1-51 갈래 F-1 · 편집창 고침 1 · §60 ⓟ2 · PM 판단] 「예」 길에 맞는 사람 말 — 저장 문 문구 끝
            //   「다시 저장해 주십시오」 는 이 길에서 거짓이다(그림 무변 저장엔 확인창이 다시 안 뜬다 · Q17).
            // ⬛ 옛 줄(주석 보존 · #1): throw new InvalidOperationException(DuplicateTemplateMessage(ex));
            throw new InvalidOperationException(DuplicateYTemplateMessage(ex));
        }
        // 🔴 [W-G1-51 갈래 F-1 · 편집창 고침 1 · §60 ⓟ4 · PM 판단] 교착(1213)도 저장 문과 같은 꼴 — 경고 1줄 · 사람 말 · 500 0 · 행 0
        //   (DB 가 거래를 되돌림 · 그림 사본은 아래 finally 가 지움). 컨트롤러 무변 = 400 + 문구.
        //   첫 문장 잠금(위 P2-1)으로 잠금 순서가 저장 문과 같아졌다 — 그 종 전체를 먼저 잡는 옮기기 1회와 겹치면 날 수 있다.
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.LockDeadlock)
        {
            _logger.LogWarning(ex, "[양식] Y 템플릿 만들기 거절 — 교착(1213) · 행 0 · tenant={Tenant} template={Template}",
                tenantId, templateId);
            throw new InvalidOperationException(YTemplateDeadlockMessage);
        }
        finally
        {
            DiscardUncommittedCopies(tenantId, newCopies, commitStarted);
        }
    }

    /// <summary>
    /// 🔴 옮기기를 지금 돌려도 되나 — 대상 · 키 · 이 판의 업데이트 성공 기록(D-8 ⓑ).
    /// </summary>
    /// <param name="apiVersion">🔴 `/health` 의 `checks.version` 과 같은 출처(CTO D2 · `UpdateOrchestrator.cs:769`) — 호출부가 넘긴다.</param>
    public async Task<YMigrationReadiness> CheckYMigrationReadinessAsync(string? apiVersion, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();

        // ① 대상 — 양식지 · 그림 찍기 켬 · 활성(설계 §7-3 대상 · Q14). 0 이면 기다림 0(새 설치 포함).
        var targets = await db.ExecuteScalarAsync<long>(new CommandDefinition(
            @"SELECT COUNT(*) FROM form_templates
              WHERE paper_mode = 'preprint' AND print_background = 1 AND is_active = 1",
            cancellationToken: ct));
        if (targets == 0) return YMigrationReadiness.NoTargets;

        // ② 키 — 새 키 있음 그리고 옛 키 없음(J4). 공존이면 Y INSERT 가 옛 키에 1062 날 길이 남는다(D-9).
        var keys = await db.QueryFirstAsync<(long NewKey, long OldKey)>(new CommandDefinition(
            @"SELECT
                (SELECT COUNT(*) FROM information_schema.statistics
                  WHERE table_schema = DATABASE() AND table_name = 'form_templates'
                    AND index_name = 'uk_form_templates_one_default') AS NewKey,
                (SELECT COUNT(*) FROM information_schema.statistics
                  WHERE table_schema = DATABASE() AND table_name = 'form_templates'
                    AND index_name = 'uk_form_templates_default') AS OldKey",
            cancellationToken: ct));
        if (keys.NewKey == 0 || keys.OldKey > 0) return YMigrationReadiness.KeysNotReady;

        // ③ 이 판의 업데이트 성공 기록(D-8 ⓑ · D0 출처 = local_update_apply_status · 시도 표 아님).
        //   D1 — 테넌트 조건 0(워치독은 tenant_id 를 늘 NULL 로 쓴다 · `WatchdogStatusWriter.cs:325`) ·
        //        result 는 'success' **정확 일치**(대조는 아래 C# Ordinal · 표 정렬 규칙의 대소문자 무시에 기대지 않음).
        //   D2 — 판 비교 = 워치독 `UpdateClient.IsSameVersion` 과 같은 규칙(<see cref="IsSameVersionAsWatchdog"/>).
        IEnumerable<(string AppliedVersion, string Result)> rows;
        try
        {
            rows = await db.QueryAsync<(string AppliedVersion, string Result)>(new CommandDefinition(
                @"SELECT applied_version AS AppliedVersion, result AS Result
                  FROM local_update_apply_status
                  WHERE result = 'success'",
                cancellationToken: ct));
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.NoSuchTable)
        {
            // D3 — 표 없음(1146) = 「아직 없음」 · 대기 · 오류 아님(옛 PC 는 워치독 첫 기록 때 표가 생긴다).
            if (Interlocked.Exchange(ref s_applyStatusTableMissingWarned, 1) == 0)
            {
                _logger.LogWarning(ex, "[양식 옮기기] 업데이트 적용 결과 표가 아직 없다 — 「아직 확정 전」 으로 보고 기다린다(D3).");
            }
            return YMigrationReadiness.NotConfirmed;
        }

        var confirmed = rows.Any(r =>
            string.Equals(r.Result, "success", StringComparison.Ordinal)
            && IsSameVersionAsWatchdog(r.AppliedVersion, apiVersion));

        // 🔴 고침 1([4] 참고-3 · PM 판단) — 판 글자를 못 읽으면(`Version.TryParse` 실패 · 비었음) 「같은 판 아님」 으로 남아
        //   옮기기가 조용히 영영 안 돈다 → 확정 못 함 + 못 읽은 글자가 있을 때 이름 붙은 경고 1줄(프로세스당 1번). 판정 규칙은 무변(D2).
        if (!confirmed)
        {
            var apiVersionReadable = TryParseThreePart(apiVersion, out _);
            var unreadableApplied = rows
                .Where(r => string.Equals(r.Result, "success", StringComparison.Ordinal)
                            && !TryParseThreePart(r.AppliedVersion, out _))
                .Select(r => r.AppliedVersion)
                .ToList();
            if ((!apiVersionReadable || unreadableApplied.Count > 0)
                && Interlocked.Exchange(ref s_versionTextUnreadableWarned, 1) == 0)
            {
                _logger.LogWarning(
                    "[양식 옮기기] 판 글자를 읽지 못해 이 판의 업데이트 확정을 맞춰 보지 못했다 — 옮기기를 미룬다 · 이 판={ApiVersion} 읽지 못한 적용 기록 판={AppliedVersions}",
                    apiVersion ?? "(없음)", string.Join(", ", unreadableApplied.Select(v => v ?? "(없음)")));
            }
        }

        return confirmed ? YMigrationReadiness.Ready : YMigrationReadiness.NotConfirmed;
    }

    /// <summary>
    /// 🔴 켠 양식지를 Y 템플릿으로 옮긴다 — 기동당 최대 1회(D4 · Q2) · 호출부는 <see cref="CheckYMigrationReadinessAsync"/> 가
    /// <see cref="YMigrationReadiness.Ready"/> 일 때만 부른다.
    /// </summary>
    /// <returns>새로 만든 Y 수(이어 쓴 것 제외).</returns>
    /// <remarks>
    /// 🔴 실패 = 그 (회사 · 종) 트랜잭션 되돌림 · 원본 그대로 · 경고 · 다음 기동에 다시(D-7 (가) · D-14).
    ///   안전망 = 렌더가 양식지 그림을 종이에 안 찍음(갈래 A) → 옮기기 실패 행도 이중 인쇄 0.
    /// 🔴 결정적 실패 행(그림 폴더 밖)은 기동마다 1번 다시 보고 행당 경고 1줄(CTO Q2 · PM 재결재 7차).
    /// </remarks>
    public async Task<int> RunYMigrationOnceAsync(CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var groups = (await db.QueryAsync<(string TenantId, string FormType)>(new CommandDefinition(
            @"SELECT DISTINCT tenant_id AS TenantId, form_type AS FormType
              FROM form_templates
              WHERE paper_mode = 'preprint' AND print_background = 1 AND is_active = 1
              ORDER BY tenant_id, form_type",
            cancellationToken: ct))).AsList();

        var created = 0;
        foreach (var g in groups)
        {
            ct.ThrowIfCancellationRequested();
            // 고침 1([4] P2-1 · [3-V] P2-2) — 이 (회사 · 종) 거래가 만든 그림 사본. 되돌림 · 예외 · 취소 때 지운다(finally ·
            //   거래는 try 블록을 나갈 때 이미 되돌려짐). 커밋을 시작했으면 지우지 않는다(확정 여부를 모름 · 그림 잃는 쪽보다 남는 쪽).
            var newCopies = new List<string>();
            var commitStarted = false;
            try
            {
                await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
                // 🔴 처리 전 한 번 — 그 종 행을 잠근 뒤 실효 기본을 뜬다(D5 · J5 ⑴ · 측정 D-11 · D-14).
                var effectiveDefaultId = await LockTypeAndReadEffectiveDefaultAsync(
                    db, tx.DbTransaction, g.TenantId, g.FormType, ct);
                var ids = (await db.QueryAsync<string>(new CommandDefinition(
                    @"SELECT template_id FROM form_templates
                      WHERE tenant_id = @TenantId AND form_type = @FormType
                        AND paper_mode = 'preprint' AND print_background = 1 AND is_active = 1
                      ORDER BY template_id",
                    new { g.TenantId, g.FormType },
                    transaction: tx.DbTransaction, cancellationToken: ct))).AsList();

                var results = new List<(string TemplateId, YTemplateResult Result)>();
                foreach (var id in ids)
                {
                    var r = await CreateYTemplateCoreAsync(
                        db, tx.DbTransaction, g.TenantId, id, effectiveDefaultId, newCopies, ct);   // 고침 1: newCopies 인자 더함
                    results.Add((id, r));
                }
                commitStarted = true;
                await tx.CommitAsync(ct);
                newCopies.Clear();   // 고침 1 — 확정된 사본은 지울 대상 아님

                foreach (var (id, r) in results)
                {
                    LogYOutcome(g.TenantId, id, r);
                    if (r.Outcome == YTemplateOutcome.Created) created++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 🔴 1062 · 교착 · 그 밖 — 이 (회사 · 종) 만 되돌림 · 원본 그대로 · 다음 기동에 다시(D-7 (가) · D-14).
                _logger.LogWarning(ex,
                    "[양식 옮기기] 이 회사 · 양식 종류는 되돌렸다 — 원래 양식 그대로 · 다음 기동에 다시 · tenant={Tenant} type={Type}",
                    g.TenantId, g.FormType);
            }
            finally
            {
                DiscardUncommittedCopies(g.TenantId, newCopies, commitStarted);
            }
        }
        return created;
    }

    /// <summary>
    /// 🔴 판 비교 — 워치독 `UpdateClient.IsSameVersion`(`src/HitPan.Watchdog/AutoUpdate/UpdateClient.cs:174`)과 **같은 규칙**:
    /// Major.Minor.Build 3자리로 잘라 비교 · 한쪽이라도 해석 불가면 false.
    /// </summary>
    /// <remarks>
    /// 🔴 CTO D2 — API 쪽 `WatchdogHandoffBootstrap.cs:609`(4자리 정규화 · 해석 불가 = 글자 비교) **재사용 금지**.
    ///   API 는 워치독 프로젝트를 참조하지 않으므로 규칙을 여기 옮겨 적는다(동치 시험 `1.2.34` · `1.2.34.0` · 형식 불가 = 갈래 D).
    /// </remarks>
    public static bool IsSameVersionAsWatchdog(string? a, string? b)
    {
        if (!TryParseThreePart(a, out var va)) return false;
        if (!TryParseThreePart(b, out var vb)) return false;
        return va == vb;
    }

    private static bool TryParseThreePart(string? text, out Version normalized)
    {
        normalized = new Version(0, 0, 0);
        if (!Version.TryParse(text, out var v)) return false;
        normalized = new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        return true;
    }

    /// <summary>그 (회사 · 종) 행을 잠그고 실효 기본(<see cref="GetDefaultAsync"/> 와 같은 규칙 — 명시 기본, 없으면 이름순 첫째)을 뜬다.</summary>
    private static async Task<string?> LockTypeAndReadEffectiveDefaultAsync(
        DbConnection db, DbTransaction tx, string tenantId, string formType, CancellationToken ct)
    {
        _ = await db.QueryAsync<string>(new CommandDefinition(
            @"SELECT template_id FROM form_templates
              WHERE tenant_id = @TenantId AND form_type = @FormType
              FOR UPDATE",
            new { TenantId = tenantId, FormType = formType },
            transaction: tx, cancellationToken: ct));

        // `ListAsync` 의 ORDER BY(form_type, is_default DESC, template_name) 를 종 하나로 좁힌 것과 같다.
        return await db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            @"SELECT template_id FROM form_templates
              WHERE tenant_id = @TenantId AND form_type = @FormType AND is_active = 1
              ORDER BY is_default DESC, template_name
              LIMIT 1",
            new { TenantId = tenantId, FormType = formType },
            transaction: tx, cancellationToken: ct));
    }

    private sealed class YSourceRow
    {
        public string TemplateId { get; set; } = "";
        public string FormType { get; set; } = "";
        public string TemplateName { get; set; } = "";
        public string PaperMode { get; set; } = "";
        public string? BackgroundImageUrl { get; set; }
        public string? FieldCoordsJson { get; set; }
        public bool PrintBackground { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// 🔴 Y 템플릿 만들기 몸통 — Y 단추와 옮기기가 같이 쓴다. 호출부가 트랜잭션과 「처리 전 실효 기본」 을 쥔다.
    /// 순서(H1 3인 G1 · G4 · G5 ⑵): ⑴ (원본이 실효 기본이면) 원본 is_default=0 ⑵ Y INSERT(실효 기본이었으면 1) ⑶ 원본 print_background=0
    /// — 새 키에서 기본 둘인 순간 0.
    /// </summary>
    private async Task<YTemplateResult> CreateYTemplateCoreAsync(
        DbConnection db, DbTransaction tx, string tenantId, string templateId,
        string? effectiveDefaultId, List<string> newCopies, CancellationToken ct)
    {
        // 종전(고침 1 전) 인자 끝: string? effectiveDefaultId, CancellationToken ct)
        //   고침 1([4] P2-1 · [3-V] P2-2) — newCopies = 이번에 만든 그림 사본 경로를 여기 더한다 · 호출부가 커밋 전 이탈 때 지운다.
        var src = await db.QueryFirstOrDefaultAsync<YSourceRow>(new CommandDefinition(
            @"SELECT template_id AS TemplateId, form_type AS FormType, template_name AS TemplateName,
                     paper_mode AS PaperMode, background_image_url AS BackgroundImageUrl,
                     field_coords_json AS FieldCoordsJson, print_background AS PrintBackground,
                     is_default AS IsDefault, is_active AS IsActive
              FROM form_templates
              WHERE tenant_id = @TenantId AND template_id = @TemplateId",
            new { TenantId = tenantId, TemplateId = templateId },
            transaction: tx, cancellationToken: ct));
        if (src is null) return new YTemplateResult(YTemplateOutcome.NotFound, null);
        if (!string.Equals(src.PaperMode, "preprint", StringComparison.Ordinal) || !src.IsActive)
        {
            return new YTemplateResult(YTemplateOutcome.NotTarget, null);
        }

        // 그림 — 비었거나 파일 없음 = 복제 0 · 원본 print_background=0(이미 그림이 안 찍히던 행 · 인쇄 모양 무변 · ⓘ-6).
        var picture = ResolveTenantPicture(tenantId, src.BackgroundImageUrl);
        if (picture.Outside)
        {
            return new YTemplateResult(YTemplateOutcome.OutsideTenantRoot, null);
        }
        if (picture.FullPath is null)
        {
            await LowerSourcePrintBackgroundAsync(db, tx, tenantId, templateId, ct);
            return new YTemplateResult(YTemplateOutcome.NoPicture, null);
        }

        var wasEffectiveDefault = string.Equals(src.TemplateId, effectiveDefaultId, StringComparison.Ordinal);

        // 🔴 고침 1([3-V] P2-1 · PM 판단) — 「그림 없음」 = 파일 · 폴더가 진짜 없을 때만(위 ⓘ-6 줄 그대로).
        //   접근 거부 · 잠김 · 경로가 폴더 등 그 밖 읽기 실패 = 그 행 무변(켬 유지) · 다음 기동에 다시 · 이름 붙은 경고.
        //   종전: 존재 확인(`File.Exists`)이 거짓이면 모두 「없음」 → 원본 켬이 한 번의 읽기 실패로 영구히 꺼졌다.
        var srcRead = await ReadPictureHashAsync(tenantId, picture.FullPath, ct);
        if (srcRead.State == PictureFileState.Missing)
        {
            await LowerSourcePrintBackgroundAsync(db, tx, tenantId, templateId, ct);
            return new YTemplateResult(YTemplateOutcome.NoPicture, null);
        }
        if (srcRead.State == PictureFileState.Unreadable || srcRead.Hash is null)
        {
            return new YTemplateResult(YTemplateOutcome.PictureUnreadable, null);
        }

        // 재전진 멱등 — 같은 회사 · 같은 종에 Y 꼴 행(빈 A4 · 그림 찍음 · 활성)이 있고
        //   그림 해시 **그리고** 칸 좌표 글자가 같으면 복제 0(ⓘ-10 · J5 ⑵ · D-12). 여백 · 워터마크는 판정에 안 쓴다(설계 §7-3).
        // 종전(고침 1 전): var srcHash = await HashFileAsync(picture.FullPath, ct);
        var srcHash = srcRead.Hash;
        string? reuseId = null;
        // 🔴 고침 1([4] P2-2 · PM 판단) — 이어 쓰기 비교에 용지(크기 · 방향) · 여백 4 · 매수(print_copy_mode) · 스타일(style_key) ·
        //   워터마크 4 칸을 더한다 · 하나라도 다르면 새 Y(모양 유지 · 대표 Q14). 위 「여백 · 워터마크는 판정에 안 쓴다(§7-3)」 줄은
        //   이 PM 판단으로 바뀜(설계 글자 고침 = 설계팀장 몫). 「출처 칸 안 둠」(§7-4) 그대로 — 새 칸 · DDL 0.
        //   비교는 SQL 안에서 원본 행(s)과 null 안전 같음(<=>) — 여백 · 워터마크 decimal 이 C# 를 거치지 않는다.
        //   EXISTS 안의 `form_templates.` = 바깥 후보 행(안쪽은 별칭 s 만 보임 · 표준 상관 이름 규칙).
        // 🔴 고침 2(PM 판단) — 로고 · 직인 · 테두리 · 자르는 선 4칸도 더함(16칸) · 원칙 = 모양 칸이 하나라도 다르면 새 Y(Q14).
        //   설계 §7-3 글자 정정 대기 → 지금 근거는 PM 판단(설계 정정 = 설계팀장 따로).
        var candidates = await db.QueryAsync<(string TemplateId, string? BackgroundImageUrl)>(new CommandDefinition(
            @"SELECT template_id AS TemplateId, background_image_url AS BackgroundImageUrl
              FROM form_templates
              WHERE tenant_id = @TenantId AND form_type = @FormType
                AND paper_mode = 'plain' AND print_background = 1 AND is_active = 1
                AND field_coords_json <=> @FieldCoordsJson
                AND template_id <> @TemplateId
                AND EXISTS (
                      SELECT 1 FROM form_templates s
                      WHERE s.tenant_id = @TenantId AND s.template_id = @TemplateId
                        AND s.paper_size          <=> form_templates.paper_size
                        AND s.orientation         <=> form_templates.orientation
                        AND s.margin_top_mm       <=> form_templates.margin_top_mm
                        AND s.margin_left_mm      <=> form_templates.margin_left_mm
                        AND s.margin_right_mm     <=> form_templates.margin_right_mm
                        AND s.margin_bottom_mm    <=> form_templates.margin_bottom_mm
                        AND s.print_copy_mode     <=> form_templates.print_copy_mode
                        AND s.style_key           <=> form_templates.style_key
                        AND s.show_company_logo   <=> form_templates.show_company_logo
                        AND s.show_company_seal   <=> form_templates.show_company_seal
                        AND s.show_border         <=> form_templates.show_border
                        AND s.show_tear_line      <=> form_templates.show_tear_line
                        AND s.show_watermark      <=> form_templates.show_watermark
                        AND s.watermark_opacity   <=> form_templates.watermark_opacity
                        AND s.watermark_saturate  <=> form_templates.watermark_saturate
                        AND s.watermark_scale_pct <=> form_templates.watermark_scale_pct)
              ORDER BY template_id",
            new { TenantId = tenantId, src.FormType, src.FieldCoordsJson, TemplateId = templateId },
            transaction: tx, cancellationToken: ct));
        foreach (var c in candidates)
        {
            var cPic = ResolveTenantPicture(tenantId, c.BackgroundImageUrl);
            if (cPic.FullPath is null) continue;
            // 종전(고침 1 전): var cHash = await HashFileAsync(cPic.FullPath, ct);
            //   고침 1 — 후보 그림이 없음 = 이 후보 건너뜀(종전과 같음) · 읽기 실패 = 이어 쓸지 못 가림 →
            //   그 행 무변 · 다음 기동에 다시(같은 그림 Y 가 둘 생기는 길을 막음 · 파일 · DB 쓰기 전 자리).
            var cRead = await ReadPictureHashAsync(tenantId, cPic.FullPath, ct);
            if (cRead.State == PictureFileState.Missing) continue;
            if (cRead.State == PictureFileState.Unreadable || cRead.Hash is null)
            {
                return new YTemplateResult(YTemplateOutcome.PictureUnreadable, null);
            }
            var cHash = cRead.Hash;
            if (CryptographicOperations.FixedTimeEquals(srcHash, cHash))
            {
                reuseId = c.TemplateId;
                break;
            }
        }

        string yId;
        string? copiedPath = null;
        if (reuseId is null)
        {
            copiedPath = CopyPictureWithinTenant(tenantId, picture.FullPath);
            if (copiedPath is null)
            {
                // 사본 자리를 못 만듦(폴더 쓰기 막힘 · 링크) — 원본 무변 · 다음 기동에 다시(ⓘ-12).
                return new YTemplateResult(YTemplateOutcome.OutsideTenantRoot, null);
            }
            newCopies.Add(copiedPath);   // 고침 1 — 사본이 생긴 바로 뒤에 적는다(아래 DB 문이 실패해도 지울 수 있게)
            yId = Guid.NewGuid().ToString();
        }
        else
        {
            yId = reuseId;
        }

        // ⑴ 원본이 실효 기본이었고 명시 기본이면 내림(이름순 실효 기본이면 이미 0).
        if (wasEffectiveDefault && src.IsDefault)
        {
            await db.ExecuteAsync(new CommandDefinition(
                @"UPDATE form_templates SET is_default = 0
                  WHERE tenant_id = @TenantId AND template_id = @TemplateId",
                new { TenantId = tenantId, TemplateId = templateId },
                transaction: tx, cancellationToken: ct));
        }

        // ⑵ Y — 새로 만들거나(INSERT … SELECT · 칸 목록 적음 · 계산 칸 default_key 에 값 넣지 않음) 이어 씀.
        if (reuseId is null)
        {
            await db.ExecuteAsync(new CommandDefinition(
                @"INSERT INTO form_templates
                    (template_id, tenant_id, form_type, template_name, paper_mode,
                     paper_size, orientation,
                     margin_top_mm, margin_left_mm, margin_right_mm, margin_bottom_mm,
                     field_coords_json, background_image_url,
                     show_company_logo, show_company_seal, show_border,
                     print_copy_mode, style_key, show_tear_line,
                     print_background, show_watermark,
                     watermark_opacity, watermark_saturate, watermark_scale_pct,
                     is_default, is_active)
                  SELECT @NewTemplateId, tenant_id, form_type, @NewTemplateName, 'plain',
                         paper_size, orientation,
                         margin_top_mm, margin_left_mm, margin_right_mm, margin_bottom_mm,
                         field_coords_json, @NewBackgroundImageUrl,
                         show_company_logo, show_company_seal, show_border,
                         print_copy_mode, style_key, show_tear_line,
                         1, show_watermark,
                         watermark_opacity, watermark_saturate, watermark_scale_pct,
                         @NewIsDefault, 1
                  FROM form_templates
                  WHERE tenant_id = @TenantId AND template_id = @TemplateId",
                new
                {
                    NewTemplateId = yId,
                    NewTemplateName = BuildYTemplateName(src.TemplateName),
                    NewBackgroundImageUrl = copiedPath,
                    NewIsDefault = wasEffectiveDefault ? 1 : 0,
                    TenantId = tenantId,
                    TemplateId = templateId
                },
                transaction: tx, cancellationToken: ct));
        }
        else if (wasEffectiveDefault)
        {
            await db.ExecuteAsync(new CommandDefinition(
                @"UPDATE form_templates SET is_default = 1
                  WHERE tenant_id = @TenantId AND template_id = @TemplateId",
                new { TenantId = tenantId, TemplateId = yId },
                transaction: tx, cancellationToken: ct));
        }

        // ⑶ 원본 = 데이터만(대표 Q14 「원래 양식지 = 데이터만」 · Y 단추 길에선 저장 문이 이미 0 이라 바뀌는 행 0).
        await LowerSourcePrintBackgroundAsync(db, tx, tenantId, templateId, ct);

        return new YTemplateResult(reuseId is null ? YTemplateOutcome.Created : YTemplateOutcome.Reused, yId);
    }

    /// <summary>
    /// 고침 1([4] P2-1 · [3-V] P2-2) — 커밋 전에 빠져나간 거래(되돌림 · 예외 · 취소)가 만든 그림 사본만 지운다.
    /// 커밋을 시작했는데 여기 왔으면(커밋 중 예외) DB 에 Y 가 섰는지 모르므로 지우지 않고 경고만 남긴다 — 선 Y 의 그림을 잃는 쪽보다 파일이 남는 쪽이 안전.
    /// 지움 실패 = 경고 1줄 · 원래 예외를 가리지 않게 여기서 다 잡는다(#15).
    /// </summary>
    private void DiscardUncommittedCopies(string tenantId, List<string> newCopies, bool commitStarted)
    {
        if (newCopies.Count == 0) return;
        if (commitStarted)
        {
            _logger.LogWarning(
                "[양식] 거래 확정 도중 멈춰 Y 템플릿 그림 사본 {Count}개를 그대로 두었다 — 확정 여부를 몰라 지우지 않음 · tenant={Tenant}",
                newCopies.Count, tenantId);
            return;
        }
        foreach (var path in newCopies)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[양식] 되돌린 Y 템플릿의 그림 사본을 지우지 못했다 — 파일만 남음 · tenant={Tenant}", tenantId);
            }
        }
        newCopies.Clear();
    }

    private static Task<int> LowerSourcePrintBackgroundAsync(
        DbConnection db, DbTransaction tx, string tenantId, string templateId, CancellationToken ct)
        => db.ExecuteAsync(new CommandDefinition(
            @"UPDATE form_templates SET print_background = 0
              WHERE tenant_id = @TenantId AND template_id = @TemplateId AND print_background = 1",
            new { TenantId = tenantId, TemplateId = templateId },
            transaction: tx, cancellationToken: ct));

    private void LogYOutcome(string tenantId, string templateId, YTemplateResult r)
    {
        switch (r.Outcome)
        {
            case YTemplateOutcome.Created:
            case YTemplateOutcome.Reused:
                _logger.LogInformation("[양식] Y 템플릿 {Outcome} — tenant={Tenant} source={Source} y={Y}",
                    r.Outcome, tenantId, templateId, r.YTemplateId);
                break;
            case YTemplateOutcome.NoPicture:
                _logger.LogWarning("[양식] 원본 그림이 없어 Y 템플릿을 만들지 않았다 — 원래 양식은 데이터만 · tenant={Tenant} source={Source}",
                    tenantId, templateId);
                break;
            case YTemplateOutcome.OutsideTenantRoot:
                _logger.LogWarning("[양식] 원본 그림이 그 회사 그림 폴더 밖이거나 사본 자리를 못 만들어 Y 템플릿을 만들지 않았다 — 원래 양식 그대로 · tenant={Tenant} source={Source}",
                    tenantId, templateId);
                break;
            case YTemplateOutcome.PictureUnreadable:
                // 고침 1([3-V] P2-1) — 이름 붙은 경고 · 행 무변 · 다음 기동에 다시.
                _logger.LogWarning("[양식] 원본 그림을 지금 읽지 못해 이번엔 Y 템플릿을 만들지 않았다 — 원래 양식 그대로(그림 찍기 켬 유지) · 다음 기동에 다시 · tenant={Tenant} source={Source}",
                    tenantId, templateId);
                break;
            default:
                _logger.LogInformation("[양식] Y 템플릿 대상 아님({Outcome}) — tenant={Tenant} source={Source}",
                    r.Outcome, tenantId, templateId);
                break;
        }
    }

    /// <summary>원래 이름 + 꼬리 — varchar(100) 를 넘지 않게 앞 이름을 자른다(서로게이트 쌍은 가르지 않음).</summary>
    private static string BuildYTemplateName(string original)
    {
        var max = TemplateNameMaxChars - YTemplateNameSuffix.Length;
        var head = original ?? string.Empty;
        if (head.Length > max)
        {
            var cut = max;
            if (cut > 0 && char.IsHighSurrogate(head[cut - 1])) cut--;
            head = head[..cut];
        }
        return head + YTemplateNameSuffix;
    }

    /// <summary>
    /// 저장된 그림 경로를 그 회사 그림 폴더 안의 실제 파일로 푼다.
    /// Outside = 폴더 밖 · 링크를 지남 · 회사 값이 폴더 이름으로 못 쓰는 모양. FullPath null(Outside 아님) = 비었거나 파일 없음.
    /// </summary>
    /// <remarks>고침 1([3-V] P2-1) — FullPath null(Outside 아님) = 저장값이 비었을 때뿐. 파일이 있는지 · 읽히는지는 <see cref="ReadPictureHashAsync"/> 가 가른다.</remarks>
    private (string? FullPath, bool Outside) ResolveTenantPicture(string tenantId, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return (null, false);
        if (string.IsNullOrWhiteSpace(tenantId)
            || tenantId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || tenantId.Contains("..", StringComparison.Ordinal))
        {
            return (null, true);
        }

        try
        {
            var root = Path.GetFullPath(FormBackgroundPaths.TenantRoot(tenantId))
                           .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(stored);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return (null, true);
            if (PassesThroughLink(full)) return (null, true);
            // 종전(고침 1 전): return File.Exists(full) ? (full, false) : (null, false);
            //   고침 1([3-V] P2-1) — 존재 확인은 여기서 안 한다. `File.Exists` 는 권한 거부 · 잠김 · 폴더에도 거짓이라
            //   「없음」 과 「못 읽음」 을 못 가른다 → 읽는 자리(<see cref="ReadPictureHashAsync"/>)에서 예외 종류로 가른다.
            return (full, false);
        }
        catch (Exception ex)
        {
            // 🔴 경로를 못 풀면 「밖」 으로 본다 — 원본 무변 쪽이 안전측(#15).
            _logger.LogWarning(ex, "[양식] 그림 경로를 풀지 못했다 — 밖으로 보고 원본 그대로 · tenant={Tenant}", tenantId);
            return (null, true);
        }
    }

    /// <summary>그림을 그 회사 그림 폴더 안 새 이름으로 **복사**(참조 아님 · 설계 §7-3 · PM Q15). 못 하면 null.</summary>
    private string? CopyPictureWithinTenant(string tenantId, string sourceFullPath)
    {
        try
        {
            var dir = FormBackgroundPaths.TenantRoot(tenantId);
            var ext = Path.GetExtension(sourceFullPath).ToLowerInvariant();
            if (!FormBackgroundPaths.AllowedExts.Contains(ext)) return null;

            // 서버가 이름을 만든다 — 저장 입구(`FormBackgroundStore`)와 같은 꼴.
            var fileName = $"{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}{ext}";
            var root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var dest = Path.GetFullPath(Path.Combine(dir, fileName));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
            if (PassesThroughLink(dest)) return null;

            File.Copy(sourceFullPath, dest, overwrite: false);
            return dest;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[양식] 그림 사본을 만들지 못했다 — 원래 양식 그대로 · 다음 기동에 다시 · tenant={Tenant}", tenantId);
            return null;
        }
    }

    /// <summary>파일 · 그 위 폴더 · 그림 뿌리 폴더 중 하나라도 링크면 true(저장 입구 R-7 · T-1 과 같은 쪽 — 막는 쪽이 안전측).</summary>
    private bool PassesThroughLink(string fullPath)
    {
        var rootNoSep = Path.GetFullPath(FormBackgroundPaths.Root).TrimEnd(Path.DirectorySeparatorChar);
        try
        {
            var file = new FileInfo(fullPath);
            if (file.Exists && file.LinkTarget is not null) return true;
            for (var dir = file.Directory;
                 dir is not null && dir.FullName.Length > rootNoSep.Length;
                 dir = dir.Parent)
            {
                if (!dir.FullName.StartsWith(rootNoSep, StringComparison.OrdinalIgnoreCase)) break;
                if (dir.Exists && dir.LinkTarget is not null) return true;
            }
            var rootDir = new DirectoryInfo(rootNoSep);
            return rootDir.Exists && rootDir.LinkTarget is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[양식] 링크 여부를 재지 못했다 — 막는 쪽으로 본다.");
            return true;
        }
    }

    private static async Task<byte[]> HashFileAsync(string fullPath, CancellationToken ct)
    {
        await using var fs = File.OpenRead(fullPath);
        return await SHA256.HashDataAsync(fs, ct);
    }

    /// <summary>
    /// 고침 1([3-V] P2-1 · PM 판단) — 그림을 읽어 해시를 낸다. 「없음」 = 파일(`FileNotFoundException`) ·
    /// 폴더(`DirectoryNotFoundException`)가 진짜 없을 때만. 접근 거부 · 잠김 · 경로가 폴더 · 그 밖 입출력 실패 = 「못 읽음」.
    /// 취소는 잡지 않는다(위로 다시 감).
    /// </summary>
    private async Task<(PictureFileState State, byte[]? Hash)> ReadPictureHashAsync(
        string tenantId, string fullPath, CancellationToken ct)
    {
        try
        {
            return (PictureFileState.Present, await HashFileAsync(fullPath, ct));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            _logger.LogWarning(ex, "[양식] 그림 파일이 없다 — 「그림 없음」 으로 본다 · tenant={Tenant}", tenantId);
            return (PictureFileState.Missing, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex,
                "[양식] 그림 파일을 지금 읽지 못했다(접근 거부 · 잠김 · 폴더 등) — 그 양식 그대로(그림 찍기 켬 유지) · 다음 기동에 다시 · tenant={Tenant}",
                tenantId);
            return (PictureFileState.Unreadable, null);
        }
    }

    /// <summary>양식지(`preprint`)는 그림 찍기 0 으로 저장한다(W-G1-51 F-1 · 측정 ⓘ-11). 빈 A4 는 요청값 그대로.</summary>
    private static bool ResolvePrintBackgroundToSave(string paperMode, bool requested)
        => !string.Equals(paperMode, "preprint", StringComparison.Ordinal) && requested;

    /// <summary>
    /// 🔴 고침 2 — Ⓐ 옮기기 전 창([4] P1-1 · CTO 판정 (나) 조건 7 · PM 22:3x 수용). 고치기 저장의 그림 찍기 값을
    /// **서버 · DB 현재값**으로 정한다(조건 1). 양식지 요청이면:
    /// DB 현재 `paper_mode`='preprint' 그리고 `print_background`=1 그리고 `background_image_url` 무변 → 1 유지 / 아니면 0
    /// (그림을 바꾼 저장 = 0 · ⓘ-11 그대로 · 조건 2). 양식지에서는 요청의 `PrintBackground` 를 읽지 않는다.
    /// 빈 A4 요청은 종전 규칙(<see cref="ResolvePrintBackgroundToSave"/> · 서명 무변 · 조건 3) 그대로.
    /// </summary>
    /// <remarks>
    /// 조건 4 — 한 UPDATE 문의 SET 안 CASE 로 하지 않는다: MariaDB 는 SET 을 왼쪽부터 적용해 뒤 칸이 앞 칸의 **새 값**을 본다
    ///   (`paper_mode` · `background_image_url` 이 `print_background` 보다 앞) → 「무변」 이 늘 참. 그래서 읽기 → 판정 → 쓰기를 나눈다.
    /// 조건 5 — 같은 트랜잭션 안 `FOR UPDATE` 로 읽는다 → 옮기기(그 종 행 잠금 뒤 대상 다시 뜸)와 순서가 선다.
    /// 조건 6 — 키 칸(`is_default` · `is_active`) 처리는 무변(옛 키 아래 새 1062 길 0) · 1 로 남기면 이름 붙은 정보 로그 1줄.
    /// </remarks>
    private async Task<bool> ResolvePrintBackgroundToSaveOnUpdateAsync(
        DbConnection db, DbTransaction tx, string tenantId, string templateId,
        UpdateFormTemplateRequest request, CancellationToken ct)
    {
        if (!string.Equals(request.PaperMode, "preprint", StringComparison.Ordinal))
        {
            return ResolvePrintBackgroundToSave(request.PaperMode, request.PrintBackground);
        }

        // 같은 표 행 모양(YSourceRow)에 필요한 세 칸만 읽는다 — 회사 조건(#2).
        var current = await db.QueryFirstOrDefaultAsync<YSourceRow>(new CommandDefinition(
            @"SELECT paper_mode AS PaperMode, print_background AS PrintBackground,
                     background_image_url AS BackgroundImageUrl
              FROM form_templates
              WHERE tenant_id = @TenantId AND template_id = @TemplateId
              FOR UPDATE",
            new { TenantId = tenantId, TemplateId = templateId },
            transaction: tx, cancellationToken: ct));
        if (current is null)
        {
            // 그 회사 행 없음 — 아래 UPDATE 가 0행 → 종전 「찾을 수 없음」 길 · 값은 종전 규칙(양식지 = 0)과 같다.
            return false;
        }

        var keep = string.Equals(current.PaperMode, "preprint", StringComparison.Ordinal)
                   && current.PrintBackground
                   && string.Equals(current.BackgroundImageUrl, request.BackgroundImageUrl, StringComparison.Ordinal);
        if (keep)
        {
            _logger.LogInformation(
                "[양식] 옮기기 대기 중 저장 — 그림 찍기 켬을 그대로 두었다 · 다음 옮기기에서 Y 템플릿으로 만듦 · tenant={Tenant} template={Template}",
                tenantId, templateId);
        }
        return keep;
    }

    /// <summary>1213(교착) 사람 말(#23 — 개발 용어 · 원문 0). 다음 할 일 = 잠시 뒤 다시 저장.</summary>
    private const string SaveDeadlockMessage =
        "다른 작업과 같은 순간에 겹쳐 저장하지 못했습니다. 잠시 뒤 다시 저장해 주십시오.";

    /// <summary>1062 사람 말(#23 — 개발 용어 · 키 이름 · 원문 0). 어느 키에 걸렸는지만 보고 두 문구 중 하나.</summary>
    private static string DuplicateTemplateMessage(MySqlException ex)
        => ex.Message.Contains("uk_form_templates_one_default", StringComparison.Ordinal)
            ? "방금 다른 화면에서 이 종류의 기본 양식이 바뀌었습니다. 목록을 새로 고친 뒤 다시 저장해 주십시오."
            : "이 양식 종류에는 지금 양식을 더 둘 수 없습니다. 쓰지 않는 양식을 정리한 뒤 다시 저장해 주십시오.";

    // ── 🔴 [2026-11-20 W-G1-51 갈래 F-1 · 편집창 고침 1 · §60 ⓟ2 · ⓟ4 · PM 판단] 확인창 「예」 길의 사람 말(#23) ─────────
    //   화면은 「빈 A4 양식을 만들지 못했습니다. 양식은 저장되었습니다.」 뒤에 이 말을 붙인다 → 여기는 까닭과 다음 할 일만.
    //   다음 할 일 = 그림을 다시 올려 저장(올리기는 늘 새 이름 → 「바뀐 저장」 → 확인창이 다시 뜸) — 「그림을 지금 읽지 못함」 줄과 같은 말.

    /// <summary>
    /// 그림 경로 앞뒤 공백을 자른다 — 공백만이면 null(「그림 지우기」와 같은 빈 값). 편집창 `TrimBackgroundPath` 와 같은 자름.
    /// </summary>
    /// <remarks>
    /// 🔴 [W-G1-51 갈래 F-1 · 편집창 고침 1 · [3-V] P3-5] 화면에서만 자르면 다른 호출(직접 호출 · 앞으로 생길 화면)로 공백 경로가
    ///   다시 행에 들어가 인쇄 때 그림이 조용히 빠진다 — 값 정규화는 서버 몫. 구분자 맞추기 등 그 밖의 정규화는 하지 않는다.
    /// </remarks>
    private static string? TrimBackgroundPath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    /// <summary>「예」 길 1213(교착) 사람 말 — 개발 용어 · 원문 0.</summary>
    private const string YTemplateDeadlockMessage =
        "다른 작업과 같은 순간에 겹쳤습니다. 잠시 뒤 그림을 다시 올려 저장하시면 다시 여쭙니다.";

    /// <summary>「예」 길 1062 사람 말 — 저장 문(<see cref="DuplicateTemplateMessage"/>)과 같은 키 가름 · 다음 할 일만 이 길에 맞춤.</summary>
    private static string DuplicateYTemplateMessage(MySqlException ex)
        => ex.Message.Contains("uk_form_templates_one_default", StringComparison.Ordinal)
            ? "방금 다른 화면에서 이 종류의 기본 양식이 바뀌었습니다. 목록을 새로 고친 뒤 그림을 다시 올려 저장하시면 다시 여쭙니다."
            : "이 양식 종류에는 지금 양식을 더 둘 수 없습니다. 쓰지 않는 양식을 정리한 뒤 그림을 다시 올려 저장하시면 다시 여쭙니다.";

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
