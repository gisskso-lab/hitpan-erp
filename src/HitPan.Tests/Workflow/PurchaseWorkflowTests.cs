using HitPan.Application.DTOs.Purchase;
using HitPan.Application.Interfaces;
using Moq;

namespace HitPan.Tests.Workflow;

/// <summary>
/// 매입 워크플로우 핵심 규칙 검증
/// 수정 필요 메모:
///   - ConfirmReceiptRequest가 현재 빈 클래스 — 실제 필드(확정자, 비고 등) 추가 시 테스트 업데이트 필요
/// </summary>
public class PurchaseWorkflowTests
{
    private readonly Mock<IPurchaseService> _svc = new(MockBehavior.Strict);

    [Fact(DisplayName = "T-P01: 매입 확정 호출 시 서비스가 정확히 1회 실행된다")]
    public async Task ConfirmReceipt_CallsServiceOnce()
    {
        var receiptId = "receipt-001";
        var req = new ConfirmReceiptRequest();

        _svc.Setup(s => s.ConfirmReceiptAsync(receiptId, req, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _svc.Object.ConfirmReceiptAsync(receiptId, req, CancellationToken.None);

        _svc.Verify(s => s.ConfirmReceiptAsync(receiptId, req, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "T-P02: 존재하지 않는 receipt_id 확정 시 예외 발생")]
    public async Task ConfirmReceipt_UnknownId_Throws()
    {
        var badId = "not-exist";
        var req = new ConfirmReceiptRequest();

        _svc.Setup(s => s.ConfirmReceiptAsync(badId, req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException($"매입명세서 {badId}를 찾을 수 없습니다."));

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.Object.ConfirmReceiptAsync(badId, req, CancellationToken.None));

        Assert.Contains(badId, ex.Message);
    }

    [Fact(DisplayName = "T-P03: 이미 확정된 receipt 재확정 시 예외 발생 (멱등 위반 방지)")]
    public async Task ConfirmReceipt_AlreadyConfirmed_Throws()
    {
        var receiptId = "receipt-already-confirmed";
        var req = new ConfirmReceiptRequest();

        _svc.Setup(s => s.ConfirmReceiptAsync(receiptId, req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("이미 확정된 매입명세서입니다."));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.Object.ConfirmReceiptAsync(receiptId, req, CancellationToken.None));
    }

    [Fact(DisplayName = "T-P04: 반품 확정 호출 시 서비스가 정확히 1회 실행된다")]
    public async Task ConfirmReturn_CallsServiceOnce()
    {
        var returnId = "return-001";
        var tenantId = "tenant-001";

        _svc.Setup(s => s.ConfirmPurchaseReturnAsync(returnId, tenantId, null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _svc.Object.ConfirmPurchaseReturnAsync(returnId, tenantId, null, CancellationToken.None);

        _svc.Verify(s => s.ConfirmPurchaseReturnAsync(returnId, tenantId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "T-P05: 발주서 → 매입전환 시 (ReceiptId, ReceiptNo) 반환")]
    public async Task ConvertOrderToReceipt_ReturnsIds()
    {
        var poId = "po-001";
        var tenantId = "tenant-001";
        var expected = ("receipt-999", "매입-20260506-001");

        _svc.Setup(s => s.ConvertOrderToReceiptAsync(poId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _svc.Object.ConvertOrderToReceiptAsync(poId, tenantId, CancellationToken.None);

        Assert.Equal(expected.Item1, result.ReceiptId);
        Assert.Equal(expected.Item2, result.ReceiptNo);
    }
}
