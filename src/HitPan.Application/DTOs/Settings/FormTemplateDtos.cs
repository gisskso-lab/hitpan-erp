using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Settings;

// 양식정보설정 DTO (사장님 작업지시 2026-05-31 작지②·③)
public class FormTemplateDto
{
    public string TemplateId { get; set; } = string.Empty;
    public string FormType { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string PaperMode { get; set; } = "plain";
    public string PaperSize { get; set; } = "A4";
    public string Orientation { get; set; } = "portrait";
    public decimal MarginTopMm { get; set; } = 15m;
    public decimal MarginLeftMm { get; set; } = 15m;
    public decimal MarginRightMm { get; set; } = 15m;
    public decimal MarginBottomMm { get; set; } = 15m;
    public string? FieldCoordsJson { get; set; }
    public string? BackgroundImageUrl { get; set; }
    public bool ShowCompanyLogo { get; set; } = true;
    public bool ShowCompanySeal { get; set; } = true;
    public bool ShowBorder { get; set; } = true;

    /// <summary>
    /// 한 번 인쇄할 때 누구 몫을 찍나 (DB-90, 사장님 지시 2026-08-11).
    /// both=공급자+공급받는자 2장 / recipient=공급받는자만 / supplier=공급자만.
    /// </summary>
    /// <remarks>
    /// paper_mode(어떤 <b>종이</b>) · style_key(어떤 <b>모양</b>) 와 다른 축이다.
    /// 세금계산서·계산서는 법정 2매라 both 가 기본이다(부가가치세법 시행규칙).
    /// </remarks>
    public string PrintCopyMode { get; set; } = "recipient";

    /// <summary>디자인 스타일 (DB-90). 4종 확정 전까지 basic 단일.</summary>
    public string StyleKey { get; set; } = "basic";

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CreateFormTemplateRequest
{
    [Required, MaxLength(30)]
    public string FormType { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string TemplateName { get; set; } = string.Empty;

    [Required]
    public string PaperMode { get; set; } = "plain";

    public string PaperSize { get; set; } = "A4";
    public string Orientation { get; set; } = "portrait";

    [Range(0, 100)] public decimal MarginTopMm { get; set; } = 15m;
    [Range(0, 100)] public decimal MarginLeftMm { get; set; } = 15m;
    [Range(0, 100)] public decimal MarginRightMm { get; set; } = 15m;
    [Range(0, 100)] public decimal MarginBottomMm { get; set; } = 15m;

    public string? FieldCoordsJson { get; set; }
    public string? BackgroundImageUrl { get; set; }
    public bool ShowCompanyLogo { get; set; } = true;
    public bool ShowCompanySeal { get; set; } = true;
    public bool ShowBorder { get; set; } = true;

    /// <summary>
    /// 한 번 인쇄할 때 누구 몫을 찍나 (DB-90, 사장님 지시 2026-08-11).
    /// both=공급자+공급받는자 2장 / recipient=공급받는자만 / supplier=공급자만.
    /// </summary>
    /// <remarks>
    /// paper_mode(어떤 <b>종이</b>) · style_key(어떤 <b>모양</b>) 와 다른 축이다.
    /// 세금계산서·계산서는 법정 2매라 both 가 기본이다(부가가치세법 시행규칙).
    /// </remarks>
    public string PrintCopyMode { get; set; } = "recipient";

    /// <summary>디자인 스타일 (DB-90). 4종 확정 전까지 basic 단일.</summary>
    public string StyleKey { get; set; } = "basic";

    public bool IsDefault { get; set; }
}

public class UpdateFormTemplateRequest : CreateFormTemplateRequest
{
    public bool IsActive { get; set; } = true;
}

// 필드 좌표 (preprint 모드, JSON 직렬화용)
public class FormFieldCoord
{
    public string Key { get; set; } = string.Empty;       // partner_name·tax_id·item_name·spec·qty·price 등
    public decimal XMm { get; set; }
    public decimal YMm { get; set; }
    public decimal? WidthMm { get; set; }
    public int FontPt { get; set; } = 10;
    public string Align { get; set; } = "left";            // left·right·center
}
