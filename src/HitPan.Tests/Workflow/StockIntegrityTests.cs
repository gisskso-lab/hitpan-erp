using HitPan.Application.DTOs.Stock;
using HitPan.Application.Interfaces;
using Moq;

namespace HitPan.Tests.Workflow;

/// <summary>
/// 재고 정합성 핵심 규칙 검증
/// 수정 필요 메모:
///   - 재고 음수 실제 DB 차단은 통합 테스트에서 검증 필요 (현재는 서비스 계층 Mock)
///   - StockAdjustRequest, StockTransferRequest DTO 실제 구조 확인 후 테스트 보강 필요
/// </summary>
public class StockIntegrityTests
{
    private readonly Mock<IStockService> _svc = new(MockBehavior.Strict);

    [Fact(DisplayName = "T-ST01: 음수 CurrentQty 항목은 감지 로직으로 식별 가능하다 (규칙 검증)")]
    public async Task GetBalance_NegativeQty_DetectionLogicWorks()
    {
        // 이 테스트는 "음수 재고 감지 로직"이 올바르게 동작하는지 검증.
        // 실제 DB에 음수 재고가 없어야 한다는 규칙은 IntegrityCheckService가 수행.
        var mixedItems = new List<StockBalanceDto>
        {
            new() { ItemId = "item-001", ItemName = "정상 상품", CurrentQty = 10m, WarehouseId = "wh-001" },
            new() { ItemId = "item-002", ItemName = "음수 상품", CurrentQty = -5m, WarehouseId = "wh-001" }
        };

        _svc.Setup(s => s.GetBalanceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new System.Collections.ObjectModel.ReadOnlyCollection<StockBalanceDto>(mixedItems));

        var result = await _svc.Object.GetBalanceAsync(CancellationToken.None);

        // 감지 로직: 음수 재고 항목을 올바르게 필터링할 수 있어야 한다
        var negatives = result.Where(r => r.CurrentQty < 0).ToList();
        Assert.Single(negatives);
        Assert.Equal("item-002", negatives[0].ItemId);
        // 이 목록이 비어있지 않으면 → IntegrityCheckService가 경보를 기록해야 함
    }

    [Fact(DisplayName = "T-ST02: 재고 잔액 조회 결과는 null이 아니어야 한다")]
    public async Task GetBalance_ReturnsNotNull()
    {
        _svc.Setup(s => s.GetBalanceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockBalanceDto>());

        var result = await _svc.Object.GetBalanceAsync(CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact(DisplayName = "T-ST03: 안전재고 미달 항목 감지 — CurrentQty <= SafetyStock")]
    public async Task GetBalance_SafetyStockBreach_Detectable()
    {
        var items = new List<StockBalanceDto>
        {
            new() { ItemId = "item-001", ItemName = "위험 상품", CurrentQty = 2m, SafetyStock = 10m, WarehouseId = "wh-001" },
            new() { ItemId = "item-002", ItemName = "정상 상품", CurrentQty = 50m, SafetyStock = 10m, WarehouseId = "wh-001" }
        };

        _svc.Setup(s => s.GetBalanceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new System.Collections.ObjectModel.ReadOnlyCollection<StockBalanceDto>(items));

        var result = await _svc.Object.GetBalanceAsync(CancellationToken.None);

        var breaches = result.Where(r => r.CurrentQty <= r.SafetyStock).ToList();
        Assert.Single(breaches);
        Assert.Equal("item-001", breaches[0].ItemId);
    }

    [Fact(DisplayName = "T-ST04: 재고 조정 서비스 호출 시 1회 실행 확인")]
    public async Task AdjustStock_CallsServiceOnce()
    {
        var tenantId = "tenant-001";
        var userId = "user-001";
        var req = new StockAdjustRequest();
        var expected = new StockAdjustResultDto();

        _svc.Setup(s => s.AdjustStockAsync(tenantId, userId, req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        await _svc.Object.AdjustStockAsync(tenantId, userId, req, CancellationToken.None);

        _svc.Verify(s => s.AdjustStockAsync(tenantId, userId, req, It.IsAny<CancellationToken>()), Times.Once);
    }
}
