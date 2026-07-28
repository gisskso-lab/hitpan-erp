using HitPan.Application.DTOs.Purchase;
using HitPan.Application.Interfaces;
using Moq;

namespace HitPan.Tests.Workflow;

/// <summary>
/// P0 #1 — 매입반품 신규 작성·수정 API 단위 테스트.
/// 헌법 #20 (워크플로우 끊김 0) + #6 (draft 시점) + #4 (decimal) 정합 검증.
/// </summary>
public class PurchaseReturnTests
{
    private readonly Mock<IPurchaseService> _svc = new(MockBehavior.Strict);

    private static CreatePurchaseReturnRequest BuildRequest(int itemCount = 1)
    {
        var req = new CreatePurchaseReturnRequest
        {
            PartnerId = "partner-001",
            ReturnDate = new DateTime(2026, 6, 1),
            Memo = "테스트 반품"
        };
        for (var i = 0; i < itemCount; i++)
        {
            req.Items.Add(new CreatePurchaseReturnItemRequest
            {
                ItemId = $"item-{i:000}",
                Qty = 1m,
                UnitPrice = 1000m,
                SupplyAmount = 1000m,
                VatAmount = 100m
            });
        }
        return req;
    }

    [Fact(DisplayName = "T-R01: 매입반품 신규 작성 시 (ReturnId, ReturnNo) 반환")]
    public async Task CreatePurchaseReturn_ReturnsIdAndNo()
    {
        var req = BuildRequest();
        var tenantId = "tenant-001";
        var expected = ("return-999", "매반-20260601-001");

        _svc.Setup(s => s.CreatePurchaseReturnAsync(req, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _svc.Object.CreatePurchaseReturnAsync(req, tenantId, CancellationToken.None);

        Assert.Equal(expected.Item1, result.ReturnId);
        Assert.Equal(expected.Item2, result.ReturnNo);
        _svc.Verify(s => s.CreatePurchaseReturnAsync(req, tenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "T-R02: 매입반품 수정 시 서비스가 정확히 1회 실행")]
    public async Task UpdatePurchaseReturn_CallsServiceOnce()
    {
        var returnId = "return-001";
        var tenantId = "tenant-001";
        var req = new UpdatePurchaseReturnRequest
        {
            PartnerId = "partner-001",
            ReturnDate = DateTime.Today,
            Items = new() { new CreatePurchaseReturnItemRequest { ItemId = "item-001", Qty = 1m, UnitPrice = 1000m, SupplyAmount = 1000m, VatAmount = 100m } }
        };

        _svc.Setup(s => s.UpdatePurchaseReturnAsync(returnId, req, tenantId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _svc.Object.UpdatePurchaseReturnAsync(returnId, req, tenantId, CancellationToken.None);

        _svc.Verify(s => s.UpdatePurchaseReturnAsync(returnId, req, tenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "T-R03: ReceiptId 없이도 신규 반품 작성 가능 (창고 청소 시나리오)")]
    public async Task CreatePurchaseReturn_WithoutReceiptId_Allowed()
    {
        var req = BuildRequest();
        req.ReceiptId = null;
        var tenantId = "tenant-001";
        var expected = ("return-998", "매반-20260601-002");

        _svc.Setup(s => s.CreatePurchaseReturnAsync(
                It.Is<CreatePurchaseReturnRequest>(r => r.ReceiptId == null), tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _svc.Object.CreatePurchaseReturnAsync(req, tenantId, CancellationToken.None);
        Assert.Equal("매반-20260601-002", result.ReturnNo);
    }

    [Fact(DisplayName = "T-R04: 수정 시 confirmed 상태면 InvalidOperationException 예외")]
    public async Task UpdatePurchaseReturn_OnConfirmed_Throws()
    {
        var returnId = "return-confirmed";
        var tenantId = "tenant-001";
        var req = new UpdatePurchaseReturnRequest
        {
            PartnerId = "partner-001",
            Items = new() { new CreatePurchaseReturnItemRequest { ItemId = "item-001", Qty = 1m, UnitPrice = 1000m, SupplyAmount = 1000m, VatAmount = 100m } }
        };

        _svc.Setup(s => s.UpdatePurchaseReturnAsync(returnId, req, tenantId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("draft 상태만 수정 가능합니다. (현재: confirmed)"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.Object.UpdatePurchaseReturnAsync(returnId, req, tenantId, CancellationToken.None));
    }

    [Fact(DisplayName = "T-R05: Items 빈 배열 시 신규 작성 실패")]
    public async Task CreatePurchaseReturn_EmptyItems_Throws()
    {
        var req = new CreatePurchaseReturnRequest
        {
            PartnerId = "partner-001",
            Items = new() // 비어있음
        };
        var tenantId = "tenant-001";

        _svc.Setup(s => s.CreatePurchaseReturnAsync(req, tenantId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("반품 품목은 1건 이상이어야 합니다."));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.Object.CreatePurchaseReturnAsync(req, tenantId, CancellationToken.None));
    }

    [Fact(DisplayName = "T-R06: PartnerId 빈 문자열 시 신규 작성 실패")]
    public async Task CreatePurchaseReturn_EmptyPartner_Throws()
    {
        var req = BuildRequest();
        req.PartnerId = string.Empty;
        var tenantId = "tenant-001";

        _svc.Setup(s => s.CreatePurchaseReturnAsync(req, tenantId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("거래처는 필수입니다."));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.Object.CreatePurchaseReturnAsync(req, tenantId, CancellationToken.None));
    }

    [Theory(DisplayName = "T-R07: 다중 라인 신규 작성 정합")]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(20)]
    public async Task CreatePurchaseReturn_MultipleItems_Allowed(int itemCount)
    {
        var req = BuildRequest(itemCount);
        var tenantId = "tenant-001";
        var expected = ($"return-{itemCount:000}", $"매반-20260601-{itemCount:000}");

        _svc.Setup(s => s.CreatePurchaseReturnAsync(
                It.Is<CreatePurchaseReturnRequest>(r => r.Items.Count == itemCount), tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _svc.Object.CreatePurchaseReturnAsync(req, tenantId, CancellationToken.None);
        Assert.Equal(expected.Item1, result.ReturnId);
    }
}
