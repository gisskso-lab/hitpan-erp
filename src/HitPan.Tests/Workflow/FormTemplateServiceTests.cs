using HitPan.Application.DTOs.Settings;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HitPan.Tests.Workflow;

// 작지②·③ FormTemplateService 단위 테스트 (사장님 작업지시 2026-05-31)
public class FormTemplateServiceTests
{
    private static FormTemplateService CreateService()
    {
        var uow = new Mock<IUnitOfWork>();
        return new FormTemplateService(uow.Object, NullLogger<FormTemplateService>.Instance);
    }

    // 🔴 [2026-11-24 W-G1-51 P2-A · PM 결재 §112] 입구 검사 순서를 잠그는 도움 함수.
    // `BeginTransactionAsync` 에 가짜 트랜잭션을 준다 — 누가 검사를 다시 트랜잭션 뒤(Core 안)로 옮기면
    // 빨강이 NRE 가 아니라 `Times.Never` 위반(= 「검사 순서」 라는 바른 이름)으로 나게 하려고.
    private static (FormTemplateService Svc, Mock<IUnitOfWork> Uow) CreateServiceWithUow()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
           .ReturnsAsync(new Mock<ISharedTransaction>().Object);
        return (new FormTemplateService(uow.Object, NullLogger<FormTemplateService>.Instance), uow);
    }

    [Fact(DisplayName = "FTS-01: ListAsync — tenantId null 저장 시 ArgumentException")]
    public async Task List_NullTenantId_Throws()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.ListAsync(null!));
    }

    [Fact(DisplayName = "FTS-02: ListAsync — tenantId 공백 저장 시 ArgumentException")]
    public async Task List_EmptyTenantId_Throws()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.ListAsync("   "));
    }

    [Theory(DisplayName = "FTS-03: CreateAsync — paper_mode 비정상 저장 시 InvalidOperationException")]
    [InlineData("preprent")]   // 오타
    [InlineData("PLAIN")]      // 대문자
    [InlineData("")]
    [InlineData("custom")]
    public async Task Create_InvalidPaperMode_Throws(string paperMode)
    {
        // 종전(P2-A 전): var svc = CreateService();
        var (svc, uow) = CreateServiceWithUow();
        var req = new CreateFormTemplateRequest
        {
            FormType = "estimate",
            TemplateName = "T",
            PaperMode = paperMode
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync("tenant-1", req));
        // P2-A — 잘못된 입력은 트랜잭션을 열기 전에 거절한다.
        uow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory(DisplayName = "FTS-04: UpdateAsync — paper_mode 비정상 저장 시 InvalidOperationException")]
    [InlineData("preprent")]
    [InlineData("CUSTOM")]
    public async Task Update_InvalidPaperMode_Throws(string paperMode)
    {
        // 종전(P2-A 전): var svc = CreateService();
        var (svc, uow) = CreateServiceWithUow();
        var req = new UpdateFormTemplateRequest
        {
            FormType = "estimate",
            TemplateName = "T",
            PaperMode = paperMode
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.UpdateAsync("tenant-1", "template-1", req));
        // P2-A — 잘못된 입력은 트랜잭션을 열기 전에 거절한다.
        uow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "FTS-05: CreateAsync — 스타일 허용 4값 밖 저장 시 InvalidOperationException · 트랜잭션 0")]
    public async Task Create_InvalidStyleKey_ThrowsBeforeTransaction()
    {
        var (svc, uow) = CreateServiceWithUow();
        var req = new CreateFormTemplateRequest
        {
            FormType = "estimate",
            TemplateName = "T",
            PaperMode = "plain",
            StyleKey = "fancy"
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync("tenant-1", req));
        uow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "FTS-06: CreateAsync — 인쇄 매수 3값 밖 저장 시 InvalidOperationException · 트랜잭션 0")]
    public async Task Create_InvalidPrintCopyMode_ThrowsBeforeTransaction()
    {
        var (svc, uow) = CreateServiceWithUow();
        var req = new CreateFormTemplateRequest
        {
            FormType = "estimate",
            TemplateName = "T",
            PaperMode = "plain",
            PrintCopyMode = "triple"
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync("tenant-1", req));
        uow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // 🔴 [2026-11-25 W-G1-51 P2-A 보탬 1 · PM 결재 §115] Update 짝 — FTS-05 · 06 은 Create 만 물었다.
    [Fact(DisplayName = "FTS-07: UpdateAsync — 스타일 허용 4값 밖 저장 시 InvalidOperationException · 트랜잭션 0")]
    public async Task Update_InvalidStyleKey_ThrowsBeforeTransaction()
    {
        var (svc, uow) = CreateServiceWithUow();
        var req = new UpdateFormTemplateRequest
        {
            FormType = "estimate",
            TemplateName = "T",
            PaperMode = "plain",
            StyleKey = "fancy"
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.UpdateAsync("tenant-1", "template-1", req));
        uow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "FTS-08: UpdateAsync — 인쇄 매수 3값 밖 저장 시 InvalidOperationException · 트랜잭션 0")]
    public async Task Update_InvalidPrintCopyMode_ThrowsBeforeTransaction()
    {
        var (svc, uow) = CreateServiceWithUow();
        var req = new UpdateFormTemplateRequest
        {
            FormType = "estimate",
            TemplateName = "T",
            PaperMode = "plain",
            PrintCopyMode = "triple"
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.UpdateAsync("tenant-1", "template-1", req));
        uow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
