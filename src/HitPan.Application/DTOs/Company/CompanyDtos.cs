namespace HitPan.Application.DTOs.Company;

public class CompanyDto
{
    public string TenantId { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string? BizNo { get; set; }
    public string? CeoName { get; set; }
    public string? BizType { get; set; }
    public string? BizItem { get; set; }
    public string? Tel { get; set; }
    public string? Fax { get; set; }
    public string? Address { get; set; }
    public string? ZipCode { get; set; }
    public string? Email { get; set; }
    public string? LogoUrl { get; set; }
    public string TaxType { get; set; } = "taxable";
    public int FiscalMonth { get; set; } = 12;
}

public class UpdateCompanyDto
{
    public string CompanyName { get; set; } = "";
    public string? BizNo { get; set; }
    public string? CeoName { get; set; }
    public string? BizType { get; set; }
    public string? BizItem { get; set; }
    public string? Tel { get; set; }
    public string? Fax { get; set; }
    public string? Address { get; set; }
    public string? ZipCode { get; set; }
    public string? Email { get; set; }
    public string? LogoUrl { get; set; }
    public string TaxType { get; set; } = "taxable";
    public int FiscalMonth { get; set; } = 12;
}
