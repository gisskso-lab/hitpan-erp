namespace HitPan.Application.DTOs.Document;

public class DocumentDto
{
    public TenantInfo Tenant { get; set; } = new();
    public PartnerInfo Partner { get; set; } = new();
    public DocumentHeader Header { get; set; } = new();
    public List<DocumentItem> Items { get; set; } = new();
}

public class TenantInfo
{
    public string TenantId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string BizNo { get; set; } = string.Empty;
    public string CeoName { get; set; } = string.Empty;
    public string Tel { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}

public class PartnerInfo
{
    public string PartnerId { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public string BizNo { get; set; } = string.Empty;
    public string CeoName { get; set; } = string.Empty;
    public string Tel { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}

public class DocumentHeader
{
    public string DocumentId { get; set; } = string.Empty;
    public string DocNo { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public decimal SupplyAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal CashAmount { get; set; }
    public decimal CardAmount { get; set; }
    public decimal DiscountAmount { get; set; }
}

public class DocumentItem
{
    public string ItemName { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }
    public string Memo { get; set; } = string.Empty;
}
