namespace HitPan.Application.DTOs.Document;

public class ImportPreviewDto
{
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public List<DocumentItem> ParsedItems { get; set; } = new();
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
}
