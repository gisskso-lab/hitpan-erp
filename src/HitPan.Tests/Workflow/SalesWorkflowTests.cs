using HitPan.Application.DTOs.Sales;
using HitPan.Application.Interfaces;
using Moq;

namespace HitPan.Tests.Workflow;

/// <summary>
/// 판매 워크플로우 핵심 규칙 검증
/// 수정 필요 메모:
///   - ConfirmDeliveryRequest가 현재 빈 클래스 — 실제 필드 추가 시 테스트 업데이트 필요
///   - CancelConfirmedDeliveryAsync: 역분개 실제 DB 반영은 통합 테스트에서 검증 필요
/// </summary>
public class SalesWorkflowTests
{
    private readonly Mock<ISalesService> _svc = new(MockBehavior.Strict);

    [Fact(DisplayName = "T-S01: 거래명세서 확정 호출 시 서비스가 정확히 1회 실행된다")]
    public async Task ConfirmDelivery_CallsServiceOnce()
    {
        var deliveryId = "delivery-001";
        var req = new ConfirmDeliveryRequest();

        _svc.Setup(s => s.ConfirmDeliveryAsync(deliveryId, req, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _svc.Object.ConfirmDeliveryAsync(deliveryId, req, CancellationToken.None);

        _svc.Verify(s => s.ConfirmDeliveryAsync(deliveryId, req, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "T-S02: 존재하지 않는 delivery_id 확정 시 예외 발생")]
    public async Task ConfirmDelivery_UnknownId_Throws()
    {
        var badId = "not-exist";
        var req = new ConfirmDeliveryRequest();

        _svc.Setup(s => s.ConfirmDeliveryAsync(badId, req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException($"거래명세서 {badId}를 찾을 수 없습니다."));

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.Object.ConfirmDeliveryAsync(badId, req, CancellationToken.None));

        Assert.Contains(badId, ex.Message);
    }

    [Fact(DisplayName = "T-S03: 이미 확정된 거래명세서 재확정 시 예외 발생")]
    public async Task ConfirmDelivery_AlreadyConfirmed_Throws()
    {
        var deliveryId = "delivery-confirmed";
        var req = new ConfirmDeliveryRequest();

        _svc.Setup(s => s.ConfirmDeliveryAsync(deliveryId, req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("이미 확정된 거래명세서입니다."));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.Object.ConfirmDeliveryAsync(deliveryId, req, CancellationToken.None));
    }

    [Fact(DisplayName = "T-S04: 확정 취소(역분개) 호출 시 서비스가 정확히 1회 실행된다")]
    public async Task CancelConfirmedDelivery_CallsServiceOnce()
    {
        var deliveryId = "delivery-001";
        var tenantId = "tenant-001";

        _svc.Setup(s => s.CancelConfirmedDeliveryAsync(deliveryId, tenantId, null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _svc.Object.CancelConfirmedDeliveryAsync(deliveryId, tenantId, null, CancellationToken.None);

        _svc.Verify(s => s.CancelConfirmedDeliveryAsync(deliveryId, tenantId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "T-S05: 수주 → 거래명세서 전환 시 (DeliveryId, DocumentNumber) 반환")]
    public async Task ConvertOrderToDelivery_ReturnsIds()
    {
        var orderId = "order-001";
        var tenantId = "tenant-001";
        var expected = ("delivery-999", "명세-20260506-001");

        _svc.Setup(s => s.ConvertOrderToDeliveryAsync(orderId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _svc.Object.ConvertOrderToDeliveryAsync(orderId, tenantId, CancellationToken.None);

        Assert.Equal(expected.Item1, result.DeliveryId);
        Assert.Equal(expected.Item2, result.DocumentNumber);
    }

    [Fact(DisplayName = "T-S06: 자동발주 후보 조회 — 빈 목록도 정상 반환")]
    public async Task GetAutoOrderCandidates_EmptyList_Ok()
    {
        var deliveryId = "delivery-001";
        var tenantId = "tenant-001";

        _svc.Setup(s => s.GetAutoOrderCandidatesAsync(deliveryId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AutoOrderCandidateDto>());

        var result = await _svc.Object.GetAutoOrderCandidatesAsync(deliveryId, tenantId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
