using HitPan.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers;

[ApiController]
[Route("api/documents")]
[Authorize(Policy = "TenantOnly")]
public class DocumentController : ControllerBase
{
    private static readonly HashSet<string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "sales-order", "purchase-order", "delivery", "stock", "partner", "item", "quotation"
    };

    private readonly DocumentService _documents;
    private readonly ExcelImportService _import;

    public DocumentController(DocumentService documents, ExcelImportService import)
    {
        _documents = documents;
        _import = import;
    }

    [HttpGet("excel/{kind}")]
    public IActionResult DownloadExcel([FromRoute] string kind)
    {
        if (!Kinds.Contains(kind))
        {
            return BadRequest("unknown kind");
        }

        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var bytes = kind switch
        {
            "sales-order" => _documents.CreateExcelSalesOrder(tenantId),
            "purchase-order" => _documents.CreateExcelPurchaseOrder(tenantId),
            "delivery" => _documents.CreateExcelDelivery(tenantId),
            "stock" => _documents.CreateExcelStock(tenantId),
            "partner" => _documents.CreateExcelPartner(tenantId),
            "item" => _documents.CreateExcelItemCatalog(tenantId),
            "quotation" => _documents.CreateExcelQuotation(tenantId),
            _ => Array.Empty<byte>()
        };

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"hitpan-{kind}.xlsx");
    }

    [HttpGet("pdf/{kind}")]
    public IActionResult DownloadPdf([FromRoute] string kind)
    {
        if (!Kinds.Contains(kind))
        {
            return BadRequest("unknown kind");
        }

        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var bytes = kind switch
        {
            "sales-order" => _documents.CreatePdfSalesOrder(tenantId),
            "purchase-order" => _documents.CreatePdfPurchaseOrder(tenantId),
            "delivery" => _documents.CreatePdfDelivery(tenantId),
            "stock" => _documents.CreatePdfStock(tenantId),
            "partner" => _documents.CreatePdfPartner(tenantId),
            "item" => _documents.CreatePdfItemCatalog(tenantId),
            "quotation" => _documents.CreatePdfQuotation(tenantId),
            _ => Array.Empty<byte>()
        };

        return File(bytes, "application/pdf", $"hitpan-{kind}.pdf");
    }

    [HttpPost("import/{kind}")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> UploadExcel([FromRoute] string kind, IFormFile file, CancellationToken ct)
    {
        if (!Kinds.Contains(kind))
        {
            return BadRequest("unknown kind");
        }

        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        if (file.Length == 0)
        {
            return BadRequest("empty file");
        }

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        ms.Position = 0;

        IReadOnlyList<IReadOnlyDictionary<string, string>> rows;
        try
        {
            rows = kind switch
            {
                "sales-order" => _import.ParseSalesOrderImport(ms),
                "purchase-order" => _import.ParsePurchaseOrderImport(ms),
                "delivery" => _import.ParseDeliveryImport(ms),
                "stock" => _import.ParseStockImport(ms),
                "partner" => _import.ParsePartnerImport(ms),
                "item" => _import.ParseItemCatalogImport(ms),
                "quotation" => _import.ParseQuotationImport(ms),
                _ => Array.Empty<IReadOnlyDictionary<string, string>>()
            };
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "parse_failed", message = ex.Message });
        }

        return Ok(new { tenantId, kind, rowCount = rows.Count, rows });
    }
}
