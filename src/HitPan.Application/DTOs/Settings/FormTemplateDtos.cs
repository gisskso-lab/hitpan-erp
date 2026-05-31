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
