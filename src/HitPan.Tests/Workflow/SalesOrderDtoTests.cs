using System.ComponentModel.DataAnnotations;
using HitPan.Application.DTOs.Sales;

namespace HitPan.Tests.Workflow;

// 판매 흐름 정합 — 수주 DTO 검증 (헌법 #4·#20 정합)
public class SalesOrderDtoTests
{
    [Fact(DisplayName = "SO-01: CreateSalesOrderRequest 기본 박제")]
    public void Create_Default_ValuesAreSafe()
    {
        var req = new CreateSalesOrderRequest();
        Assert.Equal(string.Empty, req.PartnerId);
        Assert.Empty(req.Items);
        Assert.Null(req.EmployeeId);
        Assert.Null(req.DeliveryDate);
    }

    [Fact(DisplayName = "SO-02: PartnerId·Items 필수")]
    public void Create_RequiredAttributes_Present()
    {
        var type = typeof(CreateSalesOrderRequest);
        Assert.NotNull(type.GetProperty(nameof(CreateSalesOrderRequest.PartnerId))!
            .GetCustomAttributes(typeof(RequiredAttribute), false).FirstOrDefault());
        Assert.NotNull(type.GetProperty(nameof(CreateSalesOrderRequest.Items))!
            .GetCustomAttributes(typeof(RequiredAttribute), false).FirstOrDefault());
    }

    [Theory(DisplayName = "SO-03: OrderedQty 0 이하 거부")]
    [InlineData(0)]
    [InlineData(-1)]
    public void Item_OrderedQty_RejectsZeroOrNegative(decimal qty)
    {
        var item = new CreateSalesOrderItemRequest { ItemId = "ITM-1", OrderedQty = qty };
        var ctx = new ValidationContext(item);
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(item, ctx, results, true));
    }

    [Fact(DisplayName = "SO-04: UnitPrice 0 허용 (무상 샘플)")]
    public void Item_UnitPrice_ZeroAllowed()
    {
        var item = new CreateSalesOrderItemRequest
        {
            ItemId = "ITM-1", OrderedQty = 1m, UnitPrice = 0m, SupplyAmount = 0m, VatAmount = 0m
        };
        var ctx = new ValidationContext(item);
        var results = new List<ValidationResult>();
        Assert.True(Validator.TryValidateObject(item, ctx, results, true));
    }

    [Fact(DisplayName = "SO-05: 모든 금액 decimal (헌법 #4)")]
    public void Item_AllMoney_AreDecimal()
    {
        var type = typeof(CreateSalesOrderItemRequest);
        Assert.Equal(typeof(decimal), type.GetProperty(nameof(CreateSalesOrderItemRequest.OrderedQty))!.PropertyType);
        Assert.Equal(typeof(decimal), type.GetProperty(nameof(CreateSalesOrderItemRequest.UnitPrice))!.PropertyType);
        Assert.Equal(typeof(decimal), type.GetProperty(nameof(CreateSalesOrderItemRequest.SupplyAmount))!.PropertyType);
        Assert.Equal(typeof(decimal), type.GetProperty(nameof(CreateSalesOrderItemRequest.VatAmount))!.PropertyType);
    }

    [Fact(DisplayName = "SO-06: DeliveryDate nullable")]
    public void Create_DeliveryDate_IsNullable()
    {
        var req = new CreateSalesOrderRequest();
        Assert.Null(req.DeliveryDate);
        req.DeliveryDate = DateTime.Today.AddDays(7);
        Assert.NotNull(req.DeliveryDate);
    }
}
