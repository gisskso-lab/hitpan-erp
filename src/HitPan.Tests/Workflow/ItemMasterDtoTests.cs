using HitPan.Application.DTOs.Item;

namespace HitPan.Tests.Workflow;

// 상품마스터 DTO 검증 (헌법 #4 decimal + 규격 콤보박스 저장 정합)
public class ItemMasterDtoTests
{
    [Fact(DisplayName = "IM-01: ItemListDto 기본 저장")]
    public void ItemList_Default_ValuesAreSafe()
    {
        var item = new ItemListDto();
        Assert.Equal("", item.ItemId);
        Assert.Equal("", item.ItemCode);
        Assert.Equal("", item.ItemName);
        Assert.Equal("product", item.ItemType);
        Assert.Equal("EA", item.Unit);
        Assert.Equal("taxable", item.TaxType);
        Assert.True(item.IsActive);
    }

    [Fact(DisplayName = "IM-02: Spec은 nullable (규격 콤보박스용 + 직접 입력 허용)")]
    public void ItemList_Spec_IsNullable()
    {
        var item = new ItemListDto();
        Assert.Null(item.Spec);

        item.Spec = "100×200×3mm";
        Assert.Equal("100×200×3mm", item.Spec);

        item.Spec = null;
        Assert.Null(item.Spec);

        // 공란 허용 (사장님 작업지시 2026-05-31)
        item.Spec = "";
        Assert.Equal("", item.Spec);
    }

    [Fact(DisplayName = "IM-03: 모든 금액·수량 decimal (헌법 #4)")]
    public void ItemList_AllMoneyAndQty_AreDecimal()
    {
        var type = typeof(ItemListDto);
        Assert.Equal(typeof(decimal), type.GetProperty(nameof(ItemListDto.SalePrice))!.PropertyType);
        Assert.Equal(typeof(decimal), type.GetProperty(nameof(ItemListDto.PurchasePrice))!.PropertyType);
        Assert.Equal(typeof(decimal), type.GetProperty(nameof(ItemListDto.StandardPrice))!.PropertyType);
        Assert.Equal(typeof(decimal), type.GetProperty(nameof(ItemListDto.CurrentStock))!.PropertyType);
        Assert.Equal(typeof(decimal), type.GetProperty(nameof(ItemListDto.SafetyStock))!.PropertyType);
    }

    [Fact(DisplayName = "IM-04: ItemDetailDto는 ItemListDto 상속")]
    public void ItemDetail_InheritsFrom_ItemList()
    {
        Assert.True(typeof(ItemDetailDto).IsSubclassOf(typeof(ItemListDto)));
    }

    [Fact(DisplayName = "IM-05: 자동발주 필드 — Bool + decimal")]
    public void ItemDetail_AutoOrder_FieldsValid()
    {
        var detail = new ItemDetailDto
        {
            AutoOrderEnabled = true,
            AutoOrderPartnerId = "P-1",
            AutoOrderQty = 100m,
            AutoReceiveOnOrder = true
        };
        Assert.True(detail.AutoOrderEnabled);
        Assert.Equal("P-1", detail.AutoOrderPartnerId);
        Assert.Equal(100m, detail.AutoOrderQty);
        Assert.True(detail.AutoReceiveOnOrder);
    }

    [Theory(DisplayName = "IM-06: ItemType은 product·material·service 3종")]
    [InlineData("product")]
    [InlineData("material")]
    [InlineData("service")]
    public void ItemType_AcceptsKnownValues(string type)
    {
        var item = new ItemListDto { ItemType = type };
        Assert.Equal(type, item.ItemType);
    }

    [Theory(DisplayName = "IM-07: TaxType은 taxable·zero·exempt 3종")]
    [InlineData("taxable")]
    [InlineData("zero")]
    [InlineData("exempt")]
    public void TaxType_AcceptsKnownValues(string tax)
    {
        var item = new ItemListDto { TaxType = tax };
        Assert.Equal(tax, item.TaxType);
    }
}
