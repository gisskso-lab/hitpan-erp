using HitPan.Application.DTOs.Item;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HitPan.Tests.Workflow;

// 작지① ItemSpecService 단위 테스트 (사장님 작업지시 2026-05-31)
// DB 연결 mock 저장 — 입력 검증 + 예외 영역만 검증
// 비즈니스 로직 (Dapper SQL) 검증은 통합 테스트 영역
public class ItemSpecServiceTests
{
    private static ItemSpecService CreateService()
    {
        var uow = new Mock<IUnitOfWork>();
        return new ItemSpecService(uow.Object, NullLogger<ItemSpecService>.Instance);
    }

    [Fact(DisplayName = "ISS-01: GetByItemAsync — tenantId null 저장 시 ArgumentException")]
    public async Task GetByItem_NullTenantId_Throws()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.GetByItemAsync(null!, "item-1"));
    }

    [Fact(DisplayName = "ISS-02: GetByItemAsync — tenantId 공백 저장 시 ArgumentException")]
    public async Task GetByItem_EmptyTenantId_Throws()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.GetByItemAsync("   ", "item-1"));
    }

    [Fact(DisplayName = "ISS-03: GetByItemAsync — itemId null 저장 시 ArgumentException")]
    public async Task GetByItem_NullItemId_Throws()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.GetByItemAsync("tenant-1", null!));
    }

    [Fact(DisplayName = "ISS-04: CreateAsync — SpecValue 공백 저장 시 InvalidOperationException")]
    public async Task Create_EmptySpecValue_Throws()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateAsync("tenant-1", "item-1", new CreateItemSpecRequest { SpecValue = "" }));
    }

    [Fact(DisplayName = "ISS-05: CreateAsync — SpecValue 공백문자 저장 시 InvalidOperationException")]
    public async Task Create_WhitespaceSpecValue_Throws()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateAsync("tenant-1", "item-1", new CreateItemSpecRequest { SpecValue = "   " }));
    }

    [Fact(DisplayName = "ISS-06: UpdateAsync — SpecValue 공백 저장 시 InvalidOperationException")]
    public async Task Update_EmptySpecValue_Throws()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateAsync("tenant-1", "item-1", "spec-1", new UpdateItemSpecRequest { SpecValue = "" }));
    }
}
