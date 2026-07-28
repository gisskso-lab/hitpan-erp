using System.ComponentModel.DataAnnotations;
using HitPan.Application.DTOs.Settings;

namespace HitPan.Tests.Workflow;

// 양식정보설정 DTO 검증 (사장님 작업지시 2026-05-31 작지②·③)
public class FormTemplateDtoTests
{
    [Fact(DisplayName = "FT-01: FormTemplateDto 기본값 저장")]
    public void Default_Values_Safe()
    {
        var dto = new FormTemplateDto();
        Assert.Equal("plain", dto.PaperMode);
        Assert.Equal("A4", dto.PaperSize);
        Assert.Equal("portrait", dto.Orientation);
        Assert.Equal(15m, dto.MarginTopMm);
        Assert.Equal(15m, dto.MarginLeftMm);
        Assert.Equal(15m, dto.MarginRightMm);
        Assert.Equal(15m, dto.MarginBottomMm);
        Assert.True(dto.ShowCompanyLogo);
        Assert.True(dto.ShowCompanySeal);
        Assert.True(dto.ShowBorder);
        Assert.True(dto.IsActive);
        Assert.False(dto.IsDefault);
    }

    [Fact(DisplayName = "FT-02: CreateRequest 필수 저장 (FormType, TemplateName, PaperMode)")]
    public void Create_Required_Attributes()
    {
        var type = typeof(CreateFormTemplateRequest);
        Assert.NotNull(type.GetProperty(nameof(CreateFormTemplateRequest.FormType))!
            .GetCustomAttributes(typeof(RequiredAttribute), false).FirstOrDefault());
        Assert.NotNull(type.GetProperty(nameof(CreateFormTemplateRequest.TemplateName))!
            .GetCustomAttributes(typeof(RequiredAttribute), false).FirstOrDefault());
        Assert.NotNull(type.GetProperty(nameof(CreateFormTemplateRequest.PaperMode))!
            .GetCustomAttributes(typeof(RequiredAttribute), false).FirstOrDefault());
    }

    [Theory(DisplayName = "FT-03: 여백 100mm 초과 거부")]
    [InlineData(101)]
    [InlineData(150)]
    public void Margin_Over100mm_Rejected(decimal margin)
    {
        var req = new CreateFormTemplateRequest
        {
            FormType = "estimate", TemplateName = "T1", PaperMode = "plain",
            MarginTopMm = margin
        };
        var ctx = new ValidationContext(req);
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(req, ctx, results, true));
    }

    [Fact(DisplayName = "FT-04: 여백 음수 거부")]
    public void Margin_Negative_Rejected()
    {
        var req = new CreateFormTemplateRequest
        {
            FormType = "estimate", TemplateName = "T1", PaperMode = "plain",
            MarginLeftMm = -1m
        };
        var ctx = new ValidationContext(req);
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(req, ctx, results, true));
    }

    [Fact(DisplayName = "FT-05: 정상 데이터 통과")]
    public void Create_Valid_Passes()
    {
        var req = new CreateFormTemplateRequest
        {
            FormType = "estimate",
            TemplateName = "기본 견적서 (순백지)",
            PaperMode = "plain",
            PaperSize = "A4",
            Orientation = "portrait",
            MarginTopMm = 15, MarginLeftMm = 15, MarginRightMm = 15, MarginBottomMm = 15
        };
        var ctx = new ValidationContext(req);
        var results = new List<ValidationResult>();
        Assert.True(Validator.TryValidateObject(req, ctx, results, true));
    }

    [Theory(DisplayName = "FT-06: 6대 form_type 검증")]
    [InlineData("estimate")]
    [InlineData("sales_order")]
    [InlineData("delivery")]
    [InlineData("purchase_order")]
    [InlineData("receipt")]
    [InlineData("purchase_return")]
    public void FormType_AllSix_Accepted(string formType)
    {
        var req = new CreateFormTemplateRequest
        {
            FormType = formType, TemplateName = "T", PaperMode = "plain"
        };
        var ctx = new ValidationContext(req);
        var results = new List<ValidationResult>();
        Assert.True(Validator.TryValidateObject(req, ctx, results, true));
    }

    [Theory(DisplayName = "FT-07: paper_mode plain·preprint 저장")]
    [InlineData("plain")]
    [InlineData("preprint")]
    public void PaperMode_TwoModes_Accepted(string mode)
    {
        var req = new CreateFormTemplateRequest
        {
            FormType = "estimate", TemplateName = "T", PaperMode = mode
        };
        var ctx = new ValidationContext(req);
        var results = new List<ValidationResult>();
        Assert.True(Validator.TryValidateObject(req, ctx, results, true));
    }

    [Fact(DisplayName = "FT-08: FormFieldCoord 기본값")]
    public void FieldCoord_Default()
    {
        var c = new FormFieldCoord();
        Assert.Equal("left", c.Align);
        Assert.Equal(10, c.FontPt);
        Assert.Null(c.WidthMm);
    }

    [Fact(DisplayName = "FT-09: TemplateName 100자 초과 거부")]
    public void TemplateName_Over100_Rejected()
    {
        var req = new CreateFormTemplateRequest
        {
            FormType = "estimate",
            TemplateName = new string('A', 101),
            PaperMode = "plain"
        };
        var ctx = new ValidationContext(req);
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(req, ctx, results, true));
    }

    [Fact(DisplayName = "FT-10: UpdateRequest는 CreateRequest 상속 + IsActive 저장")]
    public void Update_Inherits_Create()
    {
        Assert.True(typeof(UpdateFormTemplateRequest).IsSubclassOf(typeof(CreateFormTemplateRequest)));
        var update = new UpdateFormTemplateRequest();
        Assert.True(update.IsActive);
    }
}
