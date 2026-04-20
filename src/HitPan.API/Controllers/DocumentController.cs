using System.Data;
using System.Security.Claims;
using Dapper;
using HitPan.API.Security;
using HitPan.Application.DTOs.Document;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentController : ControllerBase
{
    private readonly ExcelExportService _excelService;
    private readonly PdfExportService _pdfService;
    private readonly ExcelImportService _importService;
    private readonly AccessTokenValidator _accessTokenValidator;
    private readonly IQuotationService _quotationService;
    private readonly ICompanyService _companyService;
    private readonly IDbConnection _db;

    public DocumentController(
        ExcelExportService excelService,
        PdfExportService pdfService,
        ExcelImportService importService,
        AccessTokenValidator accessTokenValidator,
        IQuotationService quotationService,
        ICompanyService companyService,
        IDbConnection db)
    {
        _excelService = excelService;
        _pdfService = pdfService;
        _importService = importService;
        _accessTokenValidator = accessTokenValidator;
        _quotationService = quotationService;
        _companyService = companyService;
        _db = db;
    }

    [HttpGet("{type}/{id}/excel")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadExcel(string type, string id, [FromQuery] string? token)
    {
        var principal = _accessTokenValidator.ValidateAccessToken(token);
        if (principal is null)
        {
            return Unauthorized();
        }

        var tenantId = principal.FindFirst("tenant_id")?.Value ?? string.Empty;
        var data = await LoadDocumentAsync(type, id, tenantId);

        // 문서 타입별 엑셀 생성 메서드 분기
        var bytes = type switch
        {
            "delivery" => _excelService.GenerateDeliveryExcel(data),
            "quotation" => _excelService.GenerateQuotationExcel(data),
            "tax-invoice" => _excelService.GenerateTaxInvoiceExcel(data),
            _ => _excelService.GenerateDeliveryExcel(data)
        };

        var title = GetTitleByType(type);
        var fileName = $"{title}_{id}_{DateTime.Now:yyyyMMdd}.xlsx";

        var encodedName = Uri.EscapeDataString(fileName);
        Response.Headers.Append("Content-Disposition",
            $"attachment; filename*=UTF-8''{encodedName}");
        return File(bytes,
            "application/vnd.openxmlformats-officedocument" +
            ".spreadsheetml.sheet");
    }

    [HttpGet("{type}/{id}/pdf")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadPdf(string type, string id, [FromQuery] string? token)
    {
        var principal = _accessTokenValidator.ValidateAccessToken(token);
        if (principal is null)
        {
            return Unauthorized();
        }

        var tenantId = principal.FindFirst("tenant_id")?.Value ?? string.Empty;
        var data = await LoadDocumentAsync(type, id, tenantId);

        // 문서 타입별 PDF 생성 메서드 분기
        var bytes = type switch
        {
            "delivery" => _pdfService.GenerateDeliveryPdf(data),
            "quotation" => _pdfService.GenerateQuotationPdf(data),
            "tax-invoice" => _pdfService.GenerateTaxInvoicePdf(data),
            _ => _pdfService.GenerateDeliveryPdf(data) // 기본값
        };

        var title = GetTitleByType(type);
        var fileName = $"{title}_{id}_{DateTime.Now:yyyyMMdd}.pdf";
        var encodedName = Uri.EscapeDataString(fileName);
        Response.Headers.Append("Content-Disposition",
            $"attachment; filename*=UTF-8''{encodedName}");
        return File(bytes, "application/pdf");
    }

    [Authorize]
    [HttpPost("import/excel")]
    [RequestSizeLimit(20_000_000)]
    public IActionResult ImportExcel(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("파일이 없습니다.");
        }

        try
        {
            using var stream = file.OpenReadStream();
            var result = _importService.ParseAnyExcel(stream);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest($"파싱 오류: {ex.Message}");
        }
    }

    [Authorize]
    [HttpPost("import/confirm")]
    public Task<IActionResult> ConfirmImport([FromBody] ImportPreviewDto preview)
    {
        if (!preview.IsValid)
        {
            return Task.FromResult<IActionResult>(BadRequest("유효하지 않은 데이터입니다."));
        }

        return Task.FromResult<IActionResult>(
            Ok(new { message = "저장 완료 (TODO: 실제 DB 연동)" }));
    }

    /// <summary>
    /// 문서 타입과 ID로 실제 DB 데이터를 로드하여 DocumentDto로 변환한다.
    /// </summary>
    private async Task<DocumentDto> LoadDocumentAsync(string type, string id, string tenantId)
    {
        // 테넌트(공급자) 정보 로드
        var tenantInfo = await LoadTenantInfoAsync(tenantId);

        return type switch
        {
            "quotation" => await LoadQuotationDocumentAsync(id, tenantId, tenantInfo),
            // TODO: delivery, tax-invoice 등 다른 타입은 서비스 구현 후 연동
            _ => CreateStubDocument(id, tenantId, tenantInfo)
        };
    }

    /// <summary>
    /// 견적서 데이터를 DB에서 로드하여 DocumentDto로 변환한다.
    /// </summary>
    private async Task<DocumentDto> LoadQuotationDocumentAsync(string id, string tenantId, TenantInfo tenantInfo)
    {
        var quote = await _quotationService.GetAsync(tenantId, id);
        if (quote is null)
        {
            return CreateStubDocument(id, tenantId, tenantInfo);
        }

        // 거래처 정보 로드
        var partnerInfo = await LoadPartnerInfoAsync(quote.PartnerId, tenantId);

        // 담당자명 조회
        var employeeName = "";
        if (!string.IsNullOrEmpty(quote.EmployeeId))
        {
            employeeName = await _db.QueryFirstOrDefaultAsync<string>(
                "SELECT employee_name FROM employees WHERE employee_id = @EmpId AND tenant_id = @TenantId",
                new { EmpId = quote.EmployeeId, TenantId = tenantId }) ?? "";
        }

        return new DocumentDto
        {
            Tenant = tenantInfo,
            Partner = partnerInfo,
            Header = new DocumentHeader
            {
                DocumentId = quote.QuoteId,
                DocNo = quote.QuoteNo,
                OrderDate = quote.QuoteDate,
                ValidUntil = quote.ValidUntil,
                EmployeeName = employeeName,
                SupplyAmount = quote.TotalAmount,
                VatAmount = quote.VatAmount,
                TotalAmount = quote.TotalAmount + quote.VatAmount
            },
            Items = quote.Items.Select(qi => new DocumentItem
            {
                ItemName = qi.ItemName,
                Spec = qi.Spec ?? "",
                Unit = qi.Unit ?? "",
                Qty = qi.Qty,
                UnitPrice = qi.UnitPrice,
                Amount = qi.Amount,
                VatAmount = qi.VatAmount,
                DiscountRate = qi.DiscountRate,
                Memo = qi.Memo ?? ""
            }).ToList()
        };
    }

    /// <summary>
    /// 테넌트(공급자) 정보를 DB에서 로드한다.
    /// </summary>
    private async Task<TenantInfo> LoadTenantInfoAsync(string tenantId)
    {
        var company = await _companyService.GetAsync(tenantId);
        if (company is null)
        {
            return new TenantInfo { TenantId = tenantId };
        }

        return new TenantInfo
        {
            TenantId = tenantId,
            CompanyName = company.CompanyName ?? "",
            BizNo = company.BizNo ?? "",
            CeoName = company.CeoName ?? "",
            Tel = company.Tel ?? "",
            Address = company.Address ?? "",
            BizType = company.BizType ?? "",
            BizItem = company.BizItem ?? ""
        };
    }

    /// <summary>
    /// 거래처 정보를 DB에서 로드한다.
    /// </summary>
    private async Task<PartnerInfo> LoadPartnerInfoAsync(string partnerId, string tenantId)
    {
        const string sql = """
                           SELECT
                               p.partner_id AS PartnerId,
                               p.partner_name AS PartnerName,
                               p.biz_no AS BizNo,
                               p.ceo_name AS CeoName,
                               p.tel AS Tel,
                               p.address AS Address,
                               p.biz_type AS BizType,
                               p.biz_item AS BizItem
                           FROM partners p
                           WHERE p.partner_id = @PartnerId
                             AND p.tenant_id = @TenantId
                           """;

        var partner = await _db.QueryFirstOrDefaultAsync<PartnerInfo>(sql,
            new { PartnerId = partnerId, TenantId = tenantId });

        return partner ?? new PartnerInfo { PartnerId = partnerId };
    }

    /// <summary>
    /// 실제 데이터가 없는 경우 빈 스텁 문서를 생성한다.
    /// </summary>
    private static DocumentDto CreateStubDocument(string id, string tenantId, TenantInfo tenantInfo)
    {
        return new DocumentDto
        {
            Tenant = tenantInfo,
            Partner = new PartnerInfo(),
            Header = new DocumentHeader
            {
                DocumentId = id,
                DocNo = "",
                OrderDate = DateTime.Today,
                EmployeeName = "",
                SupplyAmount = 0,
                VatAmount = 0,
                TotalAmount = 0,
                CashAmount = 0,
                CardAmount = 0,
                DiscountAmount = 0
            },
            Items = new List<DocumentItem>()
        };
    }

    private static string GetTitleByType(string type) => type switch
    {
        "delivery" => "거래명세서",
        "quotation" => "견적서",
        "tax-invoice" => "세금계산서",
        "po" => "발주서",
        "so" => "수주서",
        "po_receipt" => "매입명세서",
        "po_tax" => "매입계산서",
        _ => "거래문서"
    };
}
