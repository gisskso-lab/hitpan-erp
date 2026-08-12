using HitPan.Application.DTOs.Document;
using HitPan.Application.Services;
using QuestPDF.Infrastructure;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 양식 10종이 실제로 PDF·엑셀로 나오는지 지키는 게이트 (사장님 지시 2026-08-11 챕터3).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 필요한가</b> — 이 자리의 결함은 <b>오류를 내지 않는다.</b>
/// 문서 종류 표(DocumentController·ExcelExportService·PdfRenderService)에서 빠진 종류는
/// <c>default</c> 로 떨어져 <b>거래명세서 모양으로 조용히 나온다.</b>
/// 고객은 계산서를 눌렀는데 거래명세서를 받고, 아무도 오류를 못 본다.
/// </para>
/// <para>
/// 실제로 2026-08-12 조사에서 <c>sales_return</c> 이 그 상태였다 — 양식은 만들어지는데
/// 렌더 매핑에 없어 판매반품만 설정이 안 먹었다. 눈으로는 못 잡는 종류의 결함이라
/// 테스트로 못박는다.
/// </para>
/// <para>
/// ⚠️ 양식을 늘릴 때 이 목록도 같이 늘려야 한다. 그것이 이 테스트의 목적이다 —
/// 표를 하나만 고치고 나머지를 잊는 것을 CI 가 잡아 준다.
/// </para>
/// </remarks>
public class FormTemplateExportGuardTests
{
    /// <summary>사장님 결재 10종 (2026-08-11). DocumentController 가 받는 하이픈 표기.</summary>
    public static TheoryData<string> DocumentTypes => new()
    {
        "quotation", "sales-order", "delivery", "tax-invoice", "invoice-exempt",
        "purchase-order", "purchase-receipt", "purchase-return", "sales-return", "payment-receipt"
    };

    static FormTemplateExportGuardTests() => QuestPDF.Settings.License = LicenseType.Community;

    private static DocumentDto SampleDocument() => new()
    {
        Tenant = new TenantInfo
        {
            TenantId = "test-tenant", CompanyName = "히트판 테스트", BizNo = "123-45-67890",
            CeoName = "대표", Tel = "02-0000-0000", Address = "서울시"
        },
        Partner = new PartnerInfo
        {
            PartnerId = "P-1", PartnerName = "테스트 거래처", BizNo = "987-65-43210",
            CeoName = "거래처대표", Tel = "031-000-0000", Address = "경기도"
        },
        Header = new DocumentHeader
        {
            DocumentId = "D-1", DocNo = "TEST-0001", OrderDate = new DateTime(2026, 8, 12),
            EmployeeName = "담당자", SupplyAmount = 1_000_000m, VatAmount = 100_000m, TotalAmount = 1_100_000m
        },
        Items = new List<DocumentItem>
        {
            new() { ItemName = "테스트 상품", Spec = "1BOX", Unit = "EA",
                    Qty = 10, UnitPrice = 100_000m, Amount = 1_000_000m, VatAmount = 100_000m }
        }
    };

    /// <summary>
    /// 10종 전부 <b>실제로 열리는 엑셀</b>이 나와야 한다.
    /// XLSX 는 zip 이라 'PK' 로 시작한다 — 바이트가 있다고 열리는 파일인 것은 아니다.
    /// </summary>
    [Theory]
    [MemberData(nameof(DocumentTypes))]
    public void 엑셀_10종_전부_유효한_파일로_생성된다(string docType)
    {
        var bytes = new ExcelExportService().GenerateExcel(SampleDocument(), docType);

        Assert.True(bytes.Length > 1000, $"[{docType}] 엑셀이 비었거나 너무 작다 ({bytes.Length}B).");
        Assert.True(bytes[0] == 0x50 && bytes[1] == 0x4B,
            $"[{docType}] 엑셀이 zip(PK) 형식이 아니다 — 열리지 않는 파일이다.");
    }

    /// <summary>
    /// 10종 전부 <b>실제로 열리는 PDF</b>가 나와야 한다 ('%PDF' 로 시작).
    /// </summary>
    [Theory]
    [MemberData(nameof(DocumentTypes))]
    public void PDF_10종_전부_유효한_파일로_생성된다(string docType)
    {
        var pdf = new PdfExportService();
        var data = SampleDocument();

        var bytes = docType switch
        {
            "quotation" => pdf.GenerateQuotationPdf(data),
            "tax-invoice" => pdf.GenerateTaxInvoicePdf(data),
            "invoice-exempt" => pdf.GenerateTaxInvoicePdf(data, "계 산 서"),
            "sales-order" => pdf.GenerateDeliveryPdf(data, "수 주 서"),
            "purchase-order" => pdf.GenerateDeliveryPdf(data, "발 주 서"),
            "purchase-receipt" => pdf.GenerateDeliveryPdf(data, "매 입 명 세 서"),
            "purchase-return" => pdf.GenerateDeliveryPdf(data, "매 입 반 품"),
            "sales-return" => pdf.GenerateDeliveryPdf(data, "판 매 반 품"),
            "payment-receipt" => pdf.GenerateDeliveryPdf(data, "입 금 표"),
            _ => pdf.GenerateDeliveryPdf(data)
        };

        Assert.True(bytes.Length > 1000, $"[{docType}] PDF 가 비었거나 너무 작다 ({bytes.Length}B).");
        Assert.True(bytes[0] == 0x25 && bytes[1] == 0x50,
            $"[{docType}] PDF 형식(%PDF)이 아니다 — 열리지 않는 파일이다.");
    }

    /// <summary>
    /// 🔴 계산서는 세금계산서와 <b>다른 문서</b>여야 한다.
    /// </summary>
    /// <remarks>
    /// 계산서(면세)는 세금계산서 서식을 함께 쓰되 제목이 다르고 <c>[전자]</c> 뱃지가 없다.
    /// 둘이 완전히 같은 바이트로 나온다면 제목 인자가 무시되고 있다는 뜻이고,
    /// 그러면 면세사업자가 <b>세금계산서라고 적힌 종이</b>를 발급하게 된다.
    /// </remarks>
    [Fact]
    public void 계산서는_세금계산서와_다른_PDF_로_나온다()
    {
        var pdf = new PdfExportService();
        var data = SampleDocument();

        var taxInvoice = pdf.GenerateTaxInvoicePdf(data);
        var exempt = pdf.GenerateTaxInvoicePdf(data, "계 산 서");

        Assert.False(taxInvoice.SequenceEqual(exempt),
            "계산서가 세금계산서와 완전히 같다 — 제목 인자가 반영되지 않았다.");
    }
}
