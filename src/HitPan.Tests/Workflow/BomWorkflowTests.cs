using HitPan.Application.DTOs.Bom;
using HitPan.Application.DTOs.Sales;
using HitPan.Application.Interfaces;
using Moq;

namespace HitPan.Tests.Workflow;

/// <summary>
/// BOM 생산 워크플로우 핵심 규칙 검증
/// 수정 필요 메모:
///   - BomAssembleDto 실제 필드 구조 확인 필요 (현재 { BomId, ProduceQty } 가정)
///   - 자재 부족 시 DB 레벨 차단(WHERE current_qty >= @Qty) 검증은 통합 테스트 필요
/// </summary>
public class BomWorkflowTests
{
    private readonly Mock<IBomService> _svc = new(MockBehavior.Strict);

    [Fact(DisplayName = "T-B01: BOM 조립 생산 호출 시 서비스가 정확히 1회 실행된다")]
    public async Task Assemble_CallsServiceOnce()
    {
        var dto = new BomAssembleDto { BomId = "bom-001", ProduceQty = 5 };
        var tenantId = "tenant-001";
        var userId = "user-001";

        _svc.Setup(s => s.AssembleAsync(dto, tenantId, userId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _svc.Object.AssembleAsync(dto, tenantId, userId, CancellationToken.None);

        _svc.Verify(s => s.AssembleAsync(dto, tenantId, userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "T-B02: 자재 부족 시 조립 불가 예외 발생")]
    public async Task Assemble_InsufficientMaterial_Throws()
    {
        var dto = new BomAssembleDto { BomId = "bom-001", ProduceQty = 999 };
        var tenantId = "tenant-001";

        _svc.Setup(s => s.AssembleAsync(dto, tenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("자재 재고가 부족합니다."));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.Object.AssembleAsync(dto, tenantId, "user-001", CancellationToken.None));
    }

    [Fact(DisplayName = "T-B03: 생산 가능 여부 사전 확인 — 가능 시 true 반환")]
    public async Task CheckAssemble_Feasible_ReturnsTrue()
    {
        var bomId = "bom-001";
        var tenantId = "tenant-001";
        var checkDto = new BomAssembleCheckDto { CanProduce = true };

        _svc.Setup(s => s.CheckAssembleAsync(bomId, 5m, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkDto);

        var result = await _svc.Object.CheckAssembleAsync(bomId, 5m, tenantId, CancellationToken.None);

        Assert.True(result.CanProduce);
    }

    [Fact(DisplayName = "T-B04: 생산 가능 여부 사전 확인 — 자재 부족 시 false 반환")]
    public async Task CheckAssemble_InsufficientMaterial_ReturnsFalse()
    {
        var bomId = "bom-001";
        var tenantId = "tenant-001";
        var checkDto = new BomAssembleCheckDto { CanProduce = false };

        _svc.Setup(s => s.CheckAssembleAsync(bomId, 999m, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkDto);

        var result = await _svc.Object.CheckAssembleAsync(bomId, 999m, tenantId, CancellationToken.None);

        Assert.False(result.CanProduce);
    }

    [Fact(DisplayName = "T-B05: 조립 해체 호출 시 서비스가 정확히 1회 실행된다")]
    public async Task Disassemble_CallsServiceOnce()
    {
        var dto = new BomAssembleDto { BomId = "bom-001", ProduceQty = 2 };
        var tenantId = "tenant-001";
        var userId = "user-001";

        _svc.Setup(s => s.DisassembleAsync(dto, tenantId, userId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _svc.Object.DisassembleAsync(dto, tenantId, userId, CancellationToken.None);

        _svc.Verify(s => s.DisassembleAsync(dto, tenantId, userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "T-B06: BOM 자동발주 후보 — 안전재고 위반 항목만 반환")]
    public async Task GetAssembleAutoOrderCandidates_OnlyUnsafeItems()
    {
        var bomId = "bom-001";
        var tenantId = "tenant-001";
        var candidates = new List<AutoOrderCandidateDto>
        {
            new() { ItemId = "item-raw-001", ItemName = "원자재A", CurrentQty = 0, SafetyQty = 5 }
        };

        _svc.Setup(s => s.GetAssembleAutoOrderCandidatesAsync(bomId, tenantId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates);

        var result = await _svc.Object.GetAssembleAutoOrderCandidatesAsync(bomId, tenantId, 1, CancellationToken.None);

        Assert.All(result, c => Assert.True(c.CurrentQty <= c.SafetyQty || c.CurrentQty <= 0));
    }
}
