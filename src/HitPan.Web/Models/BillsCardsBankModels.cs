namespace HitPan.Web.Models;

// ═══ Bills ═══════════════════════════════════════════════
public sealed class BillModel
{
    public string BillId { get; set; } = "";
    public string BillType { get; set; } = "R";
    public string BillNo { get; set; } = "";
    public string? BankName { get; set; }
    public string? IssuePlace { get; set; }
    public string? PartnerId { get; set; }
    public string? PartnerName { get; set; }
    public DateTime IssueDate { get; set; }
    public DateTime? MaturityDate { get; set; }
    public DateTime? DiscountDate { get; set; }
    public DateTime? SettledDate { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "issued";
    public string? Remark { get; set; }
}

public sealed class CreateBillModel
{
    public string BillType { get; set; } = "R";
    public string BillNo { get; set; } = "";
    public string? BankName { get; set; }
    public string? IssuePlace { get; set; }
    public string? PartnerId { get; set; }
    public DateTime IssueDate { get; set; } = DateTime.Today;
    public DateTime? MaturityDate { get; set; }
    public decimal Amount { get; set; }
    public string? Remark { get; set; }
}

public sealed class UpdateBillStatusModel
{
    public string Status { get; set; } = "issued";
    public DateTime? DiscountDate { get; set; }
    public DateTime? SettledDate { get; set; }
}

// ═══ Card Payments ═══════════════════════════════════════
public sealed class CardPaymentModel
{
    public string CardPaymentId { get; set; } = "";
    public string CardNo { get; set; } = "";
    public string? CardCompany { get; set; }
    public string? HolderName { get; set; }
    public DateTime PaymentDate { get; set; }
    public DateTime? BankSettleDate { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal InstallmentAmount { get; set; }
    public int InstallmentMonths { get; set; }
    public decimal SettledAmount { get; set; }
    public string Status { get; set; } = "pending";
    public string? Remark { get; set; }
    public List<CardPaymentLineModel> Lines { get; set; } = new();
}

public sealed class CardPaymentLineModel
{
    public string LineId { get; set; } = "";
    public int Seq { get; set; }
    public string? PartnerId { get; set; }
    public string? PartnerName { get; set; }
    public DateTime TxDate { get; set; }
    public decimal Amount { get; set; }
    public string? Remark { get; set; }
}

public sealed class CreateCardPaymentModel
{
    public string CardNo { get; set; } = "";
    public string? CardCompany { get; set; }
    public string? HolderName { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Today;
    public decimal TotalAmount { get; set; }
    public decimal InstallmentAmount { get; set; }
    public int InstallmentMonths { get; set; }
    public string? Remark { get; set; }
    public List<CreateCardPaymentLineModel> Lines { get; set; } = new();
}

public sealed class CreateCardPaymentLineModel
{
    public string? PartnerId { get; set; }
    public DateTime TxDate { get; set; } = DateTime.Today;
    public decimal Amount { get; set; }
    public string? Remark { get; set; }
}

// ═══ Bank Tx ════════════════════════════════════════════
public sealed class BankTxModel
{
    public string BankTxId { get; set; } = "";
    public string AccountNo { get; set; } = "";
    public string? BankName { get; set; }
    public DateTime TxDate { get; set; }
    public string TxType { get; set; } = "1";
    public decimal Amount { get; set; }
    public decimal? BalanceAfter { get; set; }
    public string? PartnerId { get; set; }
    public string? PartnerName { get; set; }
    public string? Description { get; set; }
    public string? Remark { get; set; }
    public string ImportedFrom { get; set; } = "manual";
}

public sealed class CreateBankTxModel
{
    public string AccountNo { get; set; } = "";
    public string? BankName { get; set; }
    public DateTime TxDate { get; set; } = DateTime.Today;
    public string TxType { get; set; } = "1";
    public decimal Amount { get; set; }
    public decimal? BalanceAfter { get; set; }
    public string? PartnerId { get; set; }
    public string? Description { get; set; }
    public string? Remark { get; set; }
}
