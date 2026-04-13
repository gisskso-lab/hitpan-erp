namespace HitPan.Application.DTOs.Partner;

public class PartnerListDto
{
    public string PartnerId { get; set; } = "";

    public string PartnerCode { get; set; } = "";

    public string PartnerName { get; set; } = "";

    public string PartnerType { get; set; } = "";

    public string? BizNo { get; set; }

    public string? CeoName { get; set; }

    public string? Tel { get; set; }

    public string? Email { get; set; }

    public string? ManagerName { get; set; }

    public string PriceGrade { get; set; } = "A";

    public decimal CreditLimit { get; set; }

    public decimal Balance { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}

public class PartnerDetailDto : PartnerListDto
{
    public string? BizType { get; set; }

    public string? BizItem { get; set; }

    public string? Fax { get; set; }

    public string? ZipCode { get; set; }

    public string? Address { get; set; }

    public string? ManagerTel { get; set; }

    public string TaxType { get; set; } = "taxable";

    public int PaymentTerms { get; set; } = 30;

    public string? Memo { get; set; }

    public int RowVersion { get; set; }
}

public class CreatePartnerDto
{
    public string PartnerName { get; set; } = "";

    public string PartnerType { get; set; } = "both";

    public string? PartnerCode { get; set; }

    public string? BizNo { get; set; }

    public string? CeoName { get; set; }

    public string? BizType { get; set; }

    public string? BizItem { get; set; }

    public string? Tel { get; set; }

    public string? Fax { get; set; }

    public string? ZipCode { get; set; }

    public string? Address { get; set; }

    public string? Email { get; set; }

    public string? ManagerName { get; set; }

    public string? ManagerTel { get; set; }

    public decimal CreditLimit { get; set; }

    public string PriceGrade { get; set; } = "A";

    public string TaxType { get; set; } = "taxable";

    public int PaymentTerms { get; set; } = 30;

    public string? Memo { get; set; }
}

public class UpdatePartnerDto : CreatePartnerDto
{
    public bool IsActive { get; set; } = true;

    public int RowVersion { get; set; }
}

public class PartnerSpecialPriceDto
{
    public string PriceId { get; set; } = "";

    public string ItemId { get; set; } = "";

    public string? ItemName { get; set; }

    public string PriceType { get; set; } = "fixed";

    public decimal UnitPrice { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public bool IsActive { get; set; } = true;
}
