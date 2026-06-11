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
        var svc = CreateService();
        var req = new CreateFormTemplateRequest
        {
            FormType = "estimate",
            TemplateName = "T",
            PaperMode = paperMode
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync("tenant-1", req));
    }

    [Theory(DisplayName = "FTS-04: UpdateAsync — paper_mode 비정상 저장 시 InvalidOperationException")]
    [InlineData("preprent")]
    [InlineData("CUSTOM")]
    public async Task Update_InvalidPaperMode_Throws(string paperMode)
    {
        var svc = CreateService();
        var req = new UpdateFormTemplateRequest
        {
            FormType = "estimate",
            TemplateName = "T",
            PaperMode = paperMode
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.UpdateAsync("tenant-1", "template-1", req));
    }
}
