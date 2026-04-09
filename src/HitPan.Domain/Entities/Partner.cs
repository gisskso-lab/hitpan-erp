using HitPan.Domain.Common;
using HitPan.Domain.Enums;

namespace HitPan.Domain.Entities;

public class Partner : BaseEntity, ITenantEntity
{
    public string PartnerId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string PartnerCode { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public PartnerType PartnerType { get; set; }
    public string? BizNo { get; set; }
    public string? CeoName { get; set; }
    public string? BizType { get; set; }
    public string? BizItem { get; set; }
    public string? Tel { get; set; }
    public string? Fax { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public decimal? CreditLimit { get; set; }
    public byte? PaymentTerms { get; set; }
    public string? BankName { get; set; }
    public string? BankAccount { get; set; }
    public string? AccountHolder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Memo { get; set; }
}
