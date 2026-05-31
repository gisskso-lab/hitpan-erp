using System.ComponentModel.DataAnnotations;
using HitPan.Application.DTOs.Item;

namespace HitPan.Tests.Workflow;

// 작지① 상품 규격 1:N DTO 검증 (사장님 작업지시 2026-05-31)
public class ItemSpecDtoTests
{
    [Fact(DisplayName = "IS-01: ItemSpecDto 기본값 박제")]
    public void Default_IsActive_True_OthersEmpty()
    {
        var dto = new ItemSpecDto();
        Assert.Equal(string.Empty, dto.SpecId);
        Assert.Equal(string.Empty, dto.ItemId);
        Assert.Equal(string.Empty, dto.SpecValue);
        Assert.Equal(0, dto.DisplayOrder);
        Assert.False(dto.IsDefault);
        Assert.True(dto.IsActive);
    }

    [Fact(DisplayName = "IS-02: CreateItemSpecRequest.SpecValue [Required] + MaxLength(100)")]
    public void Create_SpecValue_HasRequiredAndMaxLength()
    {
        var prop = typeof(CreateItemSpecRequest).GetProperty(nameof(CreateItemSpecRequest.SpecValue))!;
        Assert.NotNull(prop.GetCustomAttributes(typeof(RequiredAttribute), false).FirstOrDefault());
        var maxLen = prop.GetCustomAttributes(typeof(MaxLengthAttribute), false).FirstOrDefault() as MaxLengthAttribute;
        Assert.NotNull(maxLen);
        Assert.Equal(100, maxLen!.Length);
    }

    [Fact(DisplayName = "IS-03: SpecValue 100자 초과 거부")]
    public void Create_SpecValue_Over100Chars_Rejected()
    {
        var req = new CreateItemSpecRequest { SpecValue = new string('A', 101) };
        var ctx = new ValidationContext(req);
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(req, ctx, results, true));
    }

    [Fact(DisplayName = "IS-04: SpecValue 정상 데이터 통과")]
    public void Create_Valid_Passes()
    {
        var req = new CreateItemSpecRequest { SpecValue = "100×200×3mm", DisplayOrder = 0, IsDefault = true };
        var ctx = new ValidationContext(req);
        var results = new List<ValidationResult>();
        Assert.True(Validator.TryValidateObject(req, ctx, results, true));
    }

    [Fact(DisplayName = "IS-05: UpdateItemSpecRequest IsActive 기본 true")]
    public void Update_IsActive_DefaultTrue()
    {
        var req = new UpdateItemSpecRequest();
        Assert.True(req.IsActive);
    }

    [Theory(DisplayName = "IS-06: SpecValue 빈 문자열·공백 거부")]
    [InlineData("")]
    [InlineData(null)]
    public void Create_SpecValue_NullOrEmpty_Rejected(string? value)
    {
        var req = new CreateItemSpecRequest { SpecValue = value! };
        var ctx = new ValidationContext(req);
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(req, ctx, results, true));
    }

    [Fact(DisplayName = "IS-07: 실제 규격 패턴 검증 (영문·숫자·기호 혼용)")]
    public void Create_RealSpec_Patterns_Accepted()
    {
        var patterns = new[] { "100×200×3mm", "1.0T", "M8×30", "Ø25", "Φ20×L300", "1500W", "AC220V" };
        foreach (var p in patterns)
        {
            var req = new CreateItemSpecRequest { SpecValue = p };
            var ctx = new ValidationContext(req);
            var results = new List<ValidationResult>();
            Assert.True(Validator.TryValidateObject(req, ctx, results, true), $"패턴 거부됨: {p}");
        }
    }

    [Fact(DisplayName = "IS-08: DisplayOrder는 int (음수 허용 — 정렬 자유도)")]
    public void DisplayOrder_IsInt_NegativeAllowed()
    {
        var dto = new ItemSpecDto { DisplayOrder = -1 };
        Assert.Equal(-1, dto.DisplayOrder);
    }
}
