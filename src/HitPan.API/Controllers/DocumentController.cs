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

        // 문서 타입별 엑셀 생성 — 제목만 다르게 동일 양식 활용
        var bytes = type switch
        {
            "quotation" => _excelService.GenerateQuotationExcel(data),
            "sales-order" => _excelService.GenerateDeliveryExcel(data, "수주서", "수 주 서"),
            "delivery" => _excelService.GenerateDeliveryExcel(data),
            "tax-invoice" => _excelService.GenerateTaxInvoiceExcel(data),
            "purchase-order" => _excelService.GenerateDeliveryExcel(data, "발주서", "발 주 서"),
            "purchase-receipt" => _excelService.GenerateDeliveryExcel(data, "매입명세서", "매 입 명 세 서"),
            "return" => _excelService.GenerateDeliveryExcel(data, "반품처리서", "반 품 처 리 서"),
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

        // 문서 타입별 PDF 생성 — 제목만 다르게 동일 양식 활용
        var bytes = type switch
        {
            "quotation" => _pdfService.GenerateQuotationPdf(data),
            "sales-order" => _pdfService.GenerateDeliveryPdf(data, "수 주 서"),
            "delivery" => _pdfService.GenerateDeliveryPdf(data),
            "tax-invoice" => _pdfService.GenerateTaxInvoicePdf(data),
            "purchase-order" => _pdfService.GenerateDeliveryPdf(data, "발 주 서"),
            "purchase-receipt" => _pdfService.GenerateDeliveryPdf(data, "매 입 명 세 서"),
            "return" => _pdfService.GenerateDeliveryPdf(data, "반 품 처 리 서"),
            _ => _pdfService.GenerateDeliveryPdf(data)
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
            "sales-order" => await LoadSalesOrderDocumentAsync(id, tenantId, tenantInfo),
            "delivery" => await LoadDeliveryDocumentAsync(id, tenantId, tenantInfo),
            "purchase-order" => await LoadPurchaseOrderDocumentAsync(id, tenantId, tenantInfo),
            "purchase-receipt" => await LoadPurchaseReceiptDocumentAsync(id, tenantId, tenantInfo),
            "return" => await LoadReturnDocumentAsync(id, tenantId, tenantInfo),
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
        var employeeName = await LoadEmployeeNameAsync(quote.EmployeeId, tenantId);

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
    /// 수주서 데이터를 DB에서 로드하여 DocumentDto로 변환한다.
    /// </summary>
    private async Task<DocumentDto> LoadSalesOrderDocumentAsync(string id, string tenantId, TenantInfo tenantInfo)
    {
        const string headerSql = """
            SELECT order_id, order_no, order_date, partner_id, employee_id,
                   total_amount, vat_amount, memo
            FROM sales_orders
            WHERE order_id = @Id AND tenant_id = @TenantId AND is_deleted = 0
            """;

        var header = await _db.QueryFirstOrDefaultAsync<dynamic>(headerSql,
            new { Id = id, TenantId = tenantId });

        if (header is null)
            return CreateStubDocument(id, tenantId, tenantInfo);

        const string itemsSql = """
            SELECT i.item_id, it.item_name, it.spec, it.unit,
                   i.ordered_qty AS qty, i.unit_price, i.supply_amount AS amount, i.vat_amount
            FROM sales_order_items i
            LEFT JOIN items it ON it.item_id = i.item_id AND it.tenant_id = i.tenant_id
            WHERE i.order_id = @Id AND i.tenant_id = @TenantId
            """;

        var items = (await _db.QueryAsync<dynamic>(itemsSql,
            new { Id = id, TenantId = tenantId })).ToList();

        var partnerInfo = await LoadPartnerInfoAsync((string)header.partner_id, tenantId);
        var employeeName = await LoadEmployeeNameAsync((string?)header.employee_id, tenantId);

        return new DocumentDto
        {
            Tenant = tenantInfo,
            Partner = partnerInfo,
            Header = new DocumentHeader
            {
                DocumentId = (string)header.order_id,
                DocNo = (string)header.order_no,
                OrderDate = (DateTime)header.order_date,
                EmployeeName = employeeName,
                SupplyAmount = (decimal)header.total_amount,
                VatAmount = (decimal)header.vat_amount,
                TotalAmount = (decimal)header.total_amount + (decimal)header.vat_amount
            },
            Items = items.Select(i => new DocumentItem
            {
                ItemName = (string?)i.item_name ?? "",
                Spec = (string?)i.spec ?? "",
                Unit = (string?)i.unit ?? "",
                Qty = (decimal)i.qty,
                UnitPrice = (decimal)i.unit_price,
                Amount = (decimal)i.amount,
                VatAmount = (decimal)i.vat_amount
            }).ToList()
        };
    }

    /// <summary>
    /// 거래명세서 데이터를 DB에서 로드하여 DocumentDto로 변환한다.
    /// </summary>
    private async Task<DocumentDto> LoadDeliveryDocumentAsync(string id, string tenantId, TenantInfo tenantInfo)
    {
        const string headerSql = """
            SELECT delivery_id, delivery_no, delivery_date, partner_id, employee_id,
                   total_amount, vat_amount, memo
            FROM sales_deliveries
            WHERE delivery_id = @Id AND tenant_id = @TenantId AND is_deleted = 0
            """;

        var header = await _db.QueryFirstOrDefaultAsync<dynamic>(headerSql,
            new { Id = id, TenantId = tenantId });

        if (header is null)
            return CreateStubDocument(id, tenantId, tenantInfo);

        const string itemsSql = """
            SELECT i.item_id, it.item_name, it.spec, it.unit,
                   i.qty, i.unit_price, i.supply_amount AS amount, i.vat_amount
            FROM sales_delivery_items i
            LEFT JOIN items it ON it.item_id = i.item_id AND it.tenant_id = i.tenant_id
            WHERE i.delivery_id = @Id AND i.tenant_id = @TenantId
            """;

        var items = (await _db.QueryAsync<dynamic>(itemsSql,
            new { Id = id, TenantId = tenantId })).ToList();

        var partnerInfo = await LoadPartnerInfoAsync((string)header.partner_id, tenantId);
        var employeeName = await LoadEmployeeNameAsync((string?)header.employee_id, tenantId);

        return new DocumentDto
        {
            Tenant = tenantInfo,
            Partner = partnerInfo,
            Header = new DocumentHeader
            {
                DocumentId = (string)header.delivery_id,
                DocNo = (string)header.delivery_no,
                OrderDate = (DateTime)header.delivery_date,
                EmployeeName = employeeName,
                SupplyAmount = (decimal)header.total_amount,
                VatAmount = (decimal)header.vat_amount,
                TotalAmount = (decimal)header.total_amount + (decimal)header.vat_amount
            },
            Items = items.Select(i => new DocumentItem
            {
                ItemName = (string?)i.item_name ?? "",
                Spec = (string?)i.spec ?? "",
                Unit = (string?)i.unit ?? "",
                Qty = (decimal)i.qty,
                UnitPrice = (decimal)i.unit_price,
                Amount = (decimal)i.amount,
                VatAmount = (decimal)i.vat_amount
            }).ToList()
        };
    }

    /// <summary>
    /// 발주서 데이터를 DB에서 로드하여 DocumentDto로 변환한다.
    /// </summary>
    private async Task<DocumentDto> LoadPurchaseOrderDocumentAsync(string id, string tenantId, TenantInfo tenantInfo)
    {
        const string headerSql = """
            SELECT po_id, po_no, po_date, partner_id, employee_id,
                   total_amount, vat_amount, memo
            FROM purchase_orders
            WHERE po_id = @Id AND tenant_id = @TenantId AND is_deleted = 0
            """;

        var header = await _db.QueryFirstOrDefaultAsync<dynamic>(headerSql,
            new { Id = id, TenantId = tenantId });

        if (header is null)
            return CreateStubDocument(id, tenantId, tenantInfo);

        const string itemsSql = """
            SELECT i.item_id, it.item_name, it.spec, it.unit,
                   i.ordered_qty AS qty, i.unit_price, i.supply_amount AS amount, i.vat_amount
            FROM purchase_order_items i
            LEFT JOIN items it ON it.item_id = i.item_id AND it.tenant_id = i.tenant_id
            WHERE i.po_id = @Id AND i.tenant_id = @TenantId
            """;

        var items = (await _db.QueryAsync<dynamic>(itemsSql,
            new { Id = id, TenantId = tenantId })).ToList();

        var partnerInfo = await LoadPartnerInfoAsync((string)header.partner_id, tenantId);
        var employeeName = await LoadEmployeeNameAsync((string?)header.employee_id, tenantId);

        return new DocumentDto
        {
            Tenant = tenantInfo,
            Partner = partnerInfo,
            Header = new DocumentHeader
            {
                DocumentId = (string)header.po_id,
                DocNo = (string)header.po_no,
                OrderDate = (DateTime)header.po_date,
                EmployeeName = employeeName,
                SupplyAmount = (decimal)header.total_amount,
                VatAmount = (decimal)header.vat_amount,
                TotalAmount = (decimal)header.total_amount + (decimal)header.vat_amount
            },
            Items = items.Select(i => new DocumentItem
            {
                ItemName = (string?)i.item_name ?? "",
                Spec = (string?)i.spec ?? "",
                Unit = (string?)i.unit ?? "",
                Qty = (decimal)i.qty,
                UnitPrice = (decimal)i.unit_price,
                Amount = (decimal)i.amount,
                VatAmount = (decimal)i.vat_amount
            }).ToList()
        };
    }

    /// <summary>
    /// 매입명세서 데이터를 DB에서 로드하여 DocumentDto로 변환한다.
    /// </summary>
    private async Task<DocumentDto> LoadPurchaseReceiptDocumentAsync(string id, string tenantId, TenantInfo tenantInfo)
    {
        const string headerSql = """
            SELECT receipt_id, receipt_no, receipt_date, partner_id,
                   total_amount, vat_amount, memo
            FROM purchase_receipts
            WHERE receipt_id = @Id AND tenant_id = @TenantId
            """;

        var header = await _db.QueryFirstOrDefaultAsync<dynamic>(headerSql,
            new { Id = id, TenantId = tenantId });

        if (header is null)
            return CreateStubDocument(id, tenantId, tenantInfo);

        const string itemsSql = """
            SELECT i.item_id, it.item_name, it.spec, it.unit,
                   i.qty, i.unit_price, i.supply_amount AS amount, i.vat_amount
            FROM purchase_receipt_items i
            LEFT JOIN items it ON it.item_id = i.item_id AND it.tenant_id = i.tenant_id
            WHERE i.receipt_id = @Id AND i.tenant_id = @TenantId
            """;

        var items = (await _db.QueryAsync<dynamic>(itemsSql,
            new { Id = id, TenantId = tenantId })).ToList();

        var partnerInfo = await LoadPartnerInfoAsync((string)header.partner_id, tenantId);

        return new DocumentDto
        {
            Tenant = tenantInfo,
            Partner = partnerInfo,
            Header = new DocumentHeader
            {
                DocumentId = (string)header.receipt_id,
                DocNo = (string)header.receipt_no,
                OrderDate = (DateTime)header.receipt_date,
                SupplyAmount = (decimal)header.total_amount,
                VatAmount = (decimal)header.vat_amount,
                TotalAmount = (decimal)header.total_amount + (decimal)header.vat_amount
            },
            Items = items.Select(i => new DocumentItem
            {
                ItemName = (string?)i.item_name ?? "",
                Spec = (string?)i.spec ?? "",
                Unit = (string?)i.unit ?? "",
                Qty = (decimal)i.qty,
                UnitPrice = (decimal)i.unit_price,
                Amount = (decimal)i.amount,
                VatAmount = (decimal)i.vat_amount
            }).ToList()
        };
    }

    /// <summary>
    /// 반품처리서 데이터를 DB에서 로드하여 DocumentDto로 변환한다.
    /// </summary>
    private async Task<DocumentDto> LoadReturnDocumentAsync(string id, string tenantId, TenantInfo tenantInfo)
    {
        const string headerSql = """
            SELECT return_id, return_no, return_date, partner_id,
                   total_amount, vat_amount, memo
            FROM purchase_returns
            WHERE return_id = @Id AND tenant_id = @TenantId AND is_deleted = 0
            """;

        var header = await _db.QueryFirstOrDefaultAsync<dynamic>(headerSql,
            new { Id = id, TenantId = tenantId });

        if (header is null)
            return CreateStubDocument(id, tenantId, tenantInfo);

        const string itemsSql = """
            SELECT i.item_id, it.item_name, it.spec, it.unit,
                   i.qty, i.unit_price, i.supply_amount AS amount, i.vat_amount, i.memo
            FROM purchase_return_items i
            LEFT JOIN items it ON it.item_id = i.item_id AND it.tenant_id = i.tenant_id
            WHERE i.return_id = @Id AND i.tenant_id = @TenantId
            """;

        var items = (await _db.QueryAsync<dynamic>(itemsSql,
            new { Id = id, TenantId = tenantId })).ToList();

        var partnerInfo = await LoadPartnerInfoAsync((string)header.partner_id, tenantId);

        return new DocumentDto
        {
            Tenant = tenantInfo,
            Partner = partnerInfo,
            Header = new DocumentHeader
            {
                DocumentId = (string)header.return_id,
                DocNo = (string)header.return_no,
                OrderDate = (DateTime)header.return_date,
                SupplyAmount = (decimal)header.total_amount,
                VatAmount = (decimal)header.vat_amount,
                TotalAmount = (decimal)header.total_amount + (decimal)header.vat_amount
            },
            Items = items.Select(i => new DocumentItem
            {
                ItemName = (string?)i.item_name ?? "",
                Spec = (string?)i.spec ?? "",
                Unit = (string?)i.unit ?? "",
                Qty = (decimal)i.qty,
                UnitPrice = (decimal)i.unit_price,
                Amount = (decimal)i.amount,
                VatAmount = (decimal)i.vat_amount,
                Memo = (string?)i.memo ?? ""
            }).ToList()
        };
    }

    /// <summary>
    /// 담당자명을 DB에서 조회한다.
    /// </summary>
    private async Task<string> LoadEmployeeNameAsync(string? employeeId, string tenantId)
    {
        if (string.IsNullOrEmpty(employeeId))
            return "";

        return await _db.QueryFirstOrDefaultAsync<string>(
            "SELECT employee_name FROM employees WHERE employee_id = @EmpId AND tenant_id = @TenantId",
            new { EmpId = employeeId, TenantId = tenantId }) ?? "";
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

    /// <summary>
    /// 문서 타입 코드를 한글 제목으로 변환한다.
    /// </summary>
    private static string GetTitleByType(string type) => type switch
    {
        "quotation" => "견적서",
        "sales-order" => "수주서",
        "delivery" => "거래명세서",
        "tax-invoice" => "세금계산서",
        "purchase-order" => "발주서",
        "purchase-receipt" => "매입명세서",
        "return" => "반품처리서",
        _ => "거래문서"
    };
}
