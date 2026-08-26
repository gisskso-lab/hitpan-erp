using HitPan.Application.Interfaces;
using HitPan.Application.DTOs.Stock;
using Moq;

namespace HitPan.Tests.Security;

/// <summary>
/// 멀티테넌트 격리 핵심 규칙 검증
/// 수정 필요 메모:
///   - tenant_id JWT 클레임 주입 검증은 통합 테스트(WebApplicationFactory)에서 수행 필요
///   - 현재는 서비스 계층이 tenantId를 올바르게 전달하는지 구조적으로 검증
/// </summary>
public class MultiTenantIsolationTests
{
    private readonly Mock<IStockService> _stockSvc = new(MockBehavior.Strict);
    private readonly Mock<IPurchaseService> _purchaseSvc = new(MockBehavior.Strict);

    [Fact(DisplayName = "T-ISO01: 재고 조정 시 tenantId가 서비스에 전달된다")]
    public async Task AdjustStock_TenantId_PassedToService()
    {
        var tenantId = "tenant-001";
        var userId = "user-001";
        var req = new StockAdjustRequest();

        _stockSvc.Setup(s => s.AdjustStockAsync(tenantId, userId, req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockAdjustResultDto());

        await _stockSvc.Object.AdjustStockAsync(tenantId, userId, req, CancellationToken.None);

        // 다른 tenant로는 호출되지 않아야 함
        _stockSvc.Verify(s => s.AdjustStockAsync("tenant-002", userId, req, It.IsAny<CancellationToken>()), Times.Never);
        _stockSvc.Verify(s => s.AdjustStockAsync(tenantId, userId, req, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "T-ISO02: 매입 반품 확정 시 tenantId가 서비스에 전달된다")]
    public async Task ConfirmReturn_TenantId_PassedToService()
    {
        var tenantId = "tenant-001";
        var returnId = "return-001";

        _purchaseSvc.Setup(s => s.ConfirmPurchaseReturnAsync(returnId, tenantId, null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _purchaseSvc.Object.ConfirmPurchaseReturnAsync(returnId, tenantId, null, CancellationToken.None);

        // 다른 tenant의 반품에 접근 불가
        _purchaseSvc.Verify(s => s.ConfirmPurchaseReturnAsync(returnId, "tenant-002", null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "T-ISO03: 매입 목록 조회 시 tenantId 필수 전달")]
    public async Task GetReceipts_RequiresTenantId()
    {
        var tenantId = "tenant-001";

        // 🔴 20260827작1 §8-B — `includeReturns` 가 붙으면서 **식 트리에는 선택적 인수를 못 쓴다**
        //   (CS0854). 값을 손으로 적어 넘긴다. 판정 내용은 종전과 **한 글자도 다르지 않다** —
        //   이 시험이 보는 것은 여전히 "tenantId 가 그대로 전달되는가"(헌법 #2) 뿐이다.
        _purchaseSvc.Setup(s => s.GetReceiptsAsync(tenantId, null, null, null, It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(new List<HitPan.Application.DTOs.Purchase.PurchaseReceiptListDto>());

        var result = await _purchaseSvc.Object.GetReceiptsAsync(tenantId, null, null, null, CancellationToken.None);

        Assert.NotNull(result);
        // 다른 tenant로는 단 한 번도 호출되지 않아야 함
        _purchaseSvc.Verify(s => s.GetReceiptsAsync("tenant-999", null, null, null, It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact(DisplayName = "T-ISO04: 매입 상세 조회 시 잘못된 tenantId로 null 반환 (접근 차단)")]
    public async Task GetReceiptDetail_WrongTenant_ReturnsNull()
    {
        var receiptId = "receipt-001";
        var correctTenant = "tenant-001";
        var wrongTenant = "tenant-999";

        // 정상 tenant → 데이터 반환
        _purchaseSvc.Setup(s => s.GetReceiptDetailAsync(receiptId, correctTenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HitPan.Application.DTOs.Purchase.PurchaseReceiptDetailDto());

        // 다른 tenant → null 반환 (격리)
        _purchaseSvc.Setup(s => s.GetReceiptDetailAsync(receiptId, wrongTenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HitPan.Application.DTOs.Purchase.PurchaseReceiptDetailDto?)null);

        var correctResult = await _purchaseSvc.Object.GetReceiptDetailAsync(receiptId, correctTenant, CancellationToken.None);
        var wrongResult = await _purchaseSvc.Object.GetReceiptDetailAsync(receiptId, wrongTenant, CancellationToken.None);

        Assert.NotNull(correctResult);
        Assert.Null(wrongResult);
    }
}
