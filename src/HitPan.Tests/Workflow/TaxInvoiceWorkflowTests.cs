using HitPan.Application.Interfaces;
using HitPan.Contracts.Sales;
using Moq;

namespace HitPan.Tests.Workflow;

/// <summary>
/// 세금계산서 워크플로우 핵심 규칙 검증
/// 수정 필요 메모:
///   - 역분개·monthly_summary 차감은 현재 비범위(작2 §3) — 구현 시 테스트 추가 필요
///   - 전자세금계산서 2/3계층 구현 시 EtaxStatus 상태 전이 테스트 추가 필요
/// </summary>
public class TaxInvoiceWorkflowTests
{
    private readonly Mock<ITaxInvoiceService> _svc = new(MockBehavior.Strict);

    [Fact(DisplayName = "T-T01: 확정된 거래명세서로 세금계산서 발행 시 InvoiceId 반환")]
    public async Task Issue_ConfirmedDelivery_ReturnsInvoice()
    {
        var tenantId = "tenant-001";
        var userId = "user-001";
        var req = new IssueTaxInvoiceRequest("delivery-confirmed", null);
        var expected = new TaxInvoiceResponse(
            "invoice-001", tenantId, "delivery-confirmed",
            "세-20260506-001", DateTime.UtcNow, userId,
            100000, 10000, 110000, "issued", "pending", null);

        _svc.Setup(s => s.IssueAsync(req, tenantId, userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _svc.Object.IssueAsync(req, tenantId, userId, null, CancellationToken.None);

        Assert.Equal("invoice-001", result.InvoiceId);
        Assert.Equal("issued", result.Status);
    }

    [Fact(DisplayName = "T-T02: Draft 상태 거래명세서로 계산서 발행 시 예외 발생")]
    public async Task Issue_DraftDelivery_Throws()
    {
        var req = new IssueTaxInvoiceRequest("delivery-draft", null);

        _svc.Setup(s => s.IssueAsync(req, It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("확정된 거래명세서만 계산서 발행이 가능합니다."));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.Object.IssueAsync(req, "tenant-001", "user-001", null, CancellationToken.None));
    }

    [Fact(DisplayName = "T-T03: 동일 거래명세서 중복 발행 시 예외 발생 (멱등 보호)")]
    public async Task Issue_DuplicateDelivery_Throws()
    {
        var req = new IssueTaxInvoiceRequest("delivery-already-issued", null);

        _svc.Setup(s => s.IssueAsync(req, It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("이미 계산서가 발행된 거래명세서입니다."));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.Object.IssueAsync(req, "tenant-001", "user-001", null, CancellationToken.None));
    }

    [Fact(DisplayName = "T-T04: 계산서 취소 시 status=canceled 반환")]
    public async Task Cancel_ReturnsStatusCanceled()
    {
        var invoiceId = "invoice-001";
        var tenantId = "tenant-001";
        var cancelReq = new CancelTaxInvoiceRequest("고객 요청");
        var expected = new CancelTaxInvoiceResponse(invoiceId, DateTime.UtcNow, "canceled");

        _svc.Setup(s => s.CancelAsync(invoiceId, cancelReq, tenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _svc.Object.CancelAsync(invoiceId, cancelReq, tenantId, "user-001", CancellationToken.None);

        Assert.Equal("canceled", result.Status);
        Assert.Equal(invoiceId, result.InvoiceId);
    }

    [Fact(DisplayName = "T-T05: 이미 취소된 계산서 재취소 시 예외 발생")]
    public async Task Cancel_AlreadyCanceled_Throws()
    {
        var invoiceId = "invoice-canceled";
        var cancelReq = new CancelTaxInvoiceRequest("중복취소");

        _svc.Setup(s => s.CancelAsync(invoiceId, cancelReq, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("이미 취소된 계산서입니다."));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.Object.CancelAsync(invoiceId, cancelReq, "tenant-001", "user-001", CancellationToken.None));
    }

    [Fact(DisplayName = "T-T06: GrandTotal = AmountTotal + VatTotal 정합성 검증")]
    public async Task Issue_GrandTotal_EqualsAmountPlusVat()
    {
        var tenantId = "tenant-001";
        var userId = "user-001";
        var req = new IssueTaxInvoiceRequest("delivery-001", null);
        var amountTotal = 200000m;
        var vatTotal = 20000m;
        var response = new TaxInvoiceResponse(
            "invoice-001", tenantId, "delivery-001",
            "세-20260506-001", DateTime.UtcNow, userId,
            amountTotal, vatTotal, amountTotal + vatTotal, "issued", "pending", null);

        _svc.Setup(s => s.IssueAsync(req, tenantId, userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _svc.Object.IssueAsync(req, tenantId, userId, null, CancellationToken.None);

        Assert.Equal(result.AmountTotal + result.VatTotal, result.GrandTotal);
    }
}
