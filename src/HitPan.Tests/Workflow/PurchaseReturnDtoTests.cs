using System.ComponentModel.DataAnnotations;
using HitPan.Application.DTOs.Purchase;

namespace HitPan.Tests.Workflow;

// P0 #1 매입반품 DTO 검증 (헌법 #20 흐름 끊김 봉합 정합)
public class PurchaseReturnDtoTests
{
    [Fact(DisplayName = "PR-01: CreatePurchaseReturnRequest 기본 박제")]
    public void Create_Default_PartnerIdEmpty_ItemsEmpty()
    {
        var req = new CreatePurchaseReturnRequest();
        Assert.Equal(string.Empty, req.PartnerId);
        Assert.Empty(req.Items);
        Assert.Null(req.ReceiptId);
        Assert.Null(req.Memo);
    }

    [Fact(DisplayName = "PR-02: PartnerId 필수 [Required]")]
    public void Create_PartnerId_HasRequiredAttribute()
    {
        var prop = typeof(CreatePurchaseReturnRequest).GetProperty(nameof(CreatePurchaseReturnRequest.PartnerId))!;
        Assert.NotNull(prop.GetCustomAttributes(typeof(RequiredAttribute), false).FirstOrDefault());
    }

    [Fact(DisplayName = "PR-03: Items [MinLength(1)] 박제")]
    public void Create_Items_HasMinLength1()
    {
        var prop = typeof(CreatePurchaseReturnRequest).GetProperty(nameof(CreatePurchaseReturnRequest.Items))!;
        var minLen = prop.GetCustomAttributes(typeof(MinLengthAttribute), false).FirstOrDefault() as MinLengthAttribute;
        Assert.NotNull(minLen);
        Assert.Equal(1, minLen!.Length);
    }

    [Theory(DisplayName = "PR-04: ItemRequest Qty Range 검증 (0 이하 거부)")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-99.99)]
    public void Item_Qty_RangeAttribute_RejectsZeroOrNegative(decimal qty)
    {
        var item = new CreatePurchaseReturnItemRequest { ItemId = "ITM-1", Qty = qty, UnitPrice = 100m };
        var ctx = new ValidationContext(item);
        var results = new List<ValidationResult>();
        var ok = Validator.TryValidateObject(item, ctx, results, validateAllProperties: true);
        Assert.False(ok);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CreatePurchaseReturnItemRequest.Qty)));
    }

    [Fact(DisplayName = "PR-05: ItemRequest 정상 데이터 통과")]
    public void Item_Valid_PassesValidation()
    {
        var item = new CreatePurchaseReturnItemRequest
        {
            ItemId = "ITM-1",
            Qty = 10m,
            UnitPrice = 1000m,
            SupplyAmount = 10000m,
            VatAmount = 1000m
        };
        var ctx = new ValidationContext(item);
        var results = new List<ValidationResult>();
        var ok = Validator.TryValidateObject(item, ctx, results, validateAllProperties: true);
        Assert.True(ok);
        Assert.Empty(results);
    }

    [Fact(DisplayName = "PR-06: SupplyAmount는 0 허용 (음수만 거부)")]
    public void Item_SupplyAmount_ZeroAllowed()
    {
        var item = new CreatePurchaseReturnItemRequest
        {
            ItemId = "ITM-1", Qty = 1m, UnitPrice = 1m, SupplyAmount = 0m, VatAmount = 0m
        };
        var ctx = new ValidationContext(item);
        var results = new List<ValidationResult>();
        var ok = Validator.TryValidateObject(item, ctx, results, validateAllProperties: true);
        Assert.True(ok);
    }

    [Theory(DisplayName = "PR-07: SupplyAmount 음수 거부")]
    [InlineData(-1)]
    [InlineData(-100.5)]
    public void Item_SupplyAmount_NegativeRejected(decimal amount)
    {
        var item = new CreatePurchaseReturnItemRequest
        {
            ItemId = "ITM-1", Qty = 1m, UnitPrice = 1m, SupplyAmount = amount, VatAmount = 0m
        };
        var ctx = new ValidationContext(item);
        var results = new List<ValidationResult>();
        var ok = Validator.TryValidateObject(item, ctx, results, validateAllProperties: true);
        Assert.False(ok);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CreatePurchaseReturnItemRequest.SupplyAmount)));
    }

    [Fact(DisplayName = "PR-08: UpdatePurchaseReturnRequest는 ReceiptId 미포함 (draft만 수정)")]
    public void Update_HasNo_ReceiptId()
    {
        var type = typeof(UpdatePurchaseReturnRequest);
        Assert.Null(type.GetProperty("ReceiptId"));
        Assert.NotNull(type.GetProperty(nameof(UpdatePurchaseReturnRequest.PartnerId)));
        Assert.NotNull(type.GetProperty(nameof(UpdatePurchaseReturnRequest.Items)));
    }

    [Fact(DisplayName = "PR-09: ReceiptId는 nullable (창고 청소 시 별도 반품 발행)")]
    public void Create_ReceiptId_IsNullable()
    {
        var req = new CreatePurchaseReturnRequest { PartnerId = "P-1" };
        Assert.Null(req.ReceiptId);
        req.ReceiptId = "REC-001";
        Assert.Equal("REC-001", req.ReceiptId);
    }

    [Fact(DisplayName = "PR-10: WarehouseId는 nullable")]
    public void Item_WarehouseId_IsNullable()
    {
        var item = new CreatePurchaseReturnItemRequest { ItemId = "ITM-1", Qty = 1m, UnitPrice = 1m };
        Assert.Null(item.WarehouseId);
        item.WarehouseId = "WH-1";
        Assert.Equal("WH-1", item.WarehouseId);
    }

    [Fact(DisplayName = "PR-11: 모든 금액 필드는 decimal (헌법 #4)")]
    public void Item_AllMoney_AreDecimal()
    {
        var type = typeof(CreatePurchaseReturnItemRequest);
        Assert.Equal(typeof(decimal), type.GetProperty(nameof(CreatePurchaseReturnItemRequest.Qty))!.PropertyType);
        Assert.Equal(typeof(decimal), type.GetProperty(nameof(CreatePurchaseReturnItemRequest.UnitPrice))!.PropertyType);
        Assert.Equal(typeof(decimal), type.GetProperty(nameof(CreatePurchaseReturnItemRequest.SupplyAmount))!.PropertyType);
        Assert.Equal(typeof(decimal), type.GetProperty(nameof(CreatePurchaseReturnItemRequest.VatAmount))!.PropertyType);
    }
}
