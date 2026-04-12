using System.Security.Claims;
using HitPan.API.Security;
using HitPan.Application.DTOs.Document;
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

    public DocumentController(
        ExcelExportService excelService,
        PdfExportService pdfService,
        ExcelImportService importService,
        AccessTokenValidator accessTokenValidator)
    {
        _excelService = excelService;
        _pdfService = pdfService;
        _importService = importService;
        _accessTokenValidator = accessTokenValidator;
    }

    [HttpGet("{type}/{id}/excel")]
    [AllowAnonymous]
    public IActionResult DownloadExcel(string type, string id, [FromQuery] string? token)
    {
        var principal = _accessTokenValidator.ValidateAccessToken(token);
        if (principal is null)
        {
            return Unauthorized();
        }

        var data = CreateStubDocument(id, principal);
        byte[] bytes;
        string fileName;

        if (type == "delivery")
        {
            bytes = _excelService.GenerateDeliveryExcel(data);
            fileName = $"거래명세서_{id}_{DateTime.Now:yyyyMMdd}.xlsx";
        }
        else
        {
            bytes = _excelService.GenerateExcel(data, type);
            var title = GetTitleByType(type);
            fileName = $"{title}_{id}_{DateTime.Now:yyyyMMdd}.xlsx";
        }

        var encodedName = Uri.EscapeDataString(fileName);
        Response.Headers.Append("Content-Disposition",
            $"attachment; filename*=UTF-8''{encodedName}");
        return File(bytes,
            "application/vnd.openxmlformats-officedocument" +
            ".spreadsheetml.sheet");
    }

    [HttpGet("{type}/{id}/pdf")]
    [AllowAnonymous]
    public IActionResult DownloadPdf(string type, string id, [FromQuery] string? token)
    {
        var principal = _accessTokenValidator.ValidateAccessToken(token);
        if (principal is null)
        {
            return Unauthorized();
        }

        var data = CreateStubDocument(id, principal);
        var title = GetTitleByType(type);
        var bytes = type == "delivery"
            ? _pdfService.GenerateDeliveryPdf(data)
            : _pdfService.GeneratePdf(data, title);
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

    private DocumentDto CreateStubDocument(string id, ClaimsPrincipal? principal = null)
    {
        var tenantId = principal?.FindFirst("tenant_id")?.Value
            ?? HttpContext.Items["TenantId"]?.ToString()
            ?? string.Empty;
        return new DocumentDto
        {
            Tenant = new TenantInfo
            {
                TenantId = tenantId,
                CompanyName = "",
                BizNo = "",
                CeoName = "",
                Tel = "",
                Address = ""
            },
            Partner = new PartnerInfo
            {
                PartnerId = "",
                PartnerName = "",
                BizNo = "",
                CeoName = "",
                Tel = "",
                Address = ""
            },
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
        "po" => "발주서",
        "so" => "수주서",
        "po_receipt" => "매입명세서",
        "po_tax" => "매입계산서",
        _ => "거래문서"
    };
}
