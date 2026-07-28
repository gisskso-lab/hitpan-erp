using System.ComponentModel.DataAnnotations;
using HitPan.Application.DTOs.Purchase;

namespace HitPan.Tests.Workflow;

// 작지 #3 반품 전용 컬럼 검증 (사장님 작업지시 2026-05-31)
public class PurchaseReturnReasonTests
{
    [Fact(DisplayName = "RR-01: CreateRequest ReturnReason nullable 저장")]
    public void Create_ReturnReason_Nullable()
    {
        var req = new CreatePurchaseReturnRequest();
        Assert.Null(req.ReturnReason);
        Assert.Null(req.ReturnReasonMemo);

        req.ReturnReason = "defect";
        req.ReturnReasonMemo = "박스 파손";
        Assert.Equal("defect", req.ReturnReason);
        Assert.Equal("박스 파손", req.ReturnReasonMemo);
    }

    [Fact(DisplayName = "RR-02: ReturnReasonMemo 500자 초과 거부")]
    public void Create_ReturnReasonMemo_Over500_Rejected()
    {
        var req = new CreatePurchaseReturnRequest
        {
            PartnerId = "P-1",
            Items = new() { new CreatePurchaseReturnItemRequest { ItemId = "I-1", Qty = 1m, UnitPrice = 1m } },
            ReturnReasonMemo = new string('A', 501)
        };
        var ctx = new ValidationContext(req);
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(req, ctx, results, true));
    }

    [Theory(DisplayName = "RR-03: 5종 표준 사유 저장")]
    [InlineData("defect")]
    [InlineData("wrong_item")]
    [InlineData("over_qty")]
    [InlineData("customer_cancel")]
    [InlineData("etc")]
    public void Create_StandardReasons_Accepted(string reason)
    {
        var req = new CreatePurchaseReturnRequest
        {
            PartnerId = "P-1",
            Items = new() { new CreatePurchaseReturnItemRequest { ItemId = "I-1", Qty = 1m, UnitPrice = 1m } },
            ReturnReason = reason
        };
        var ctx = new ValidationContext(req);
        var results = new List<ValidationResult>();
        Assert.True(Validator.TryValidateObject(req, ctx, results, true));
    }

    [Fact(DisplayName = "RR-04: UpdateRequest도 ReturnReason 저장")]
    public void Update_HasReturnReason()
    {
        var type = typeof(UpdatePurchaseReturnRequest);
        Assert.NotNull(type.GetProperty("ReturnReason"));
        Assert.NotNull(type.GetProperty("ReturnReasonMemo"));
    }

    [Fact(DisplayName = "RR-05: ReturnReasonMemo MaxLength(500) attribute 저장")]
    public void Create_Memo_HasMaxLength500()
    {
        var prop = typeof(CreatePurchaseReturnRequest).GetProperty(nameof(CreatePurchaseReturnRequest.ReturnReasonMemo))!;
        var attr = prop.GetCustomAttributes(typeof(MaxLengthAttribute), false).FirstOrDefault() as MaxLengthAttribute;
        Assert.NotNull(attr);
        Assert.Equal(500, attr!.Length);
    }
}
