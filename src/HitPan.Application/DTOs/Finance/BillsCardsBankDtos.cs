namespace HitPan.Application.DTOs.Finance;

// ═══ Bills (어음) ═══════════════════════════════════════════
public sealed class BillDto
{
    public string BillId { get; set; } = "";
    public string BillType { get; set; } = "R";          // R=받을 / P=지급
    public string BillNo { get; set; } = "";
    public string? BankName { get; set; }
    public string? IssuePlace { get; set; }
    public string? PartnerId { get; set; }
    public string? PartnerName { get; set; }             // 조회 시 join — Service에서 채움
    public DateTime IssueDate { get; set; }
    public DateTime? MaturityDate { get; set; }
    public DateTime? DiscountDate { get; set; }
    public DateTime? SettledDate { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "issued";
    public string? Remark { get; set; }
}

public sealed class CreateBillRequest
{
    public string BillType { get; set; } = "R";
    public string BillNo { get; set; } = "";
    public string? BankName { get; set; }
    public string? IssuePlace { get; set; }
    public string? PartnerId { get; set; }
    public DateTime IssueDate { get; set; }
    public DateTime? MaturityDate { get; set; }
    public decimal Amount { get; set; }
    public string? Remark { get; set; }
}

public sealed class UpdateBillStatusRequest
{
    public string Status { get; set; } = "issued";       // issued / discounted / paid / defaulted / cancelled
    public DateTime? DiscountDate { get; set; }
    public DateTime? SettledDate { get; set; }
}

// ═══ Card Payments (카드결제) ═══════════════════════════════
public sealed class CardPaymentDto
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
    public List<CardPaymentLineDto> Lines { get; set; } = new();
}

public sealed class CardPaymentLineDto
{
    public string LineId { get; set; } = "";
    public int Seq { get; set; }
    public string? PartnerId { get; set; }
    public string? PartnerName { get; set; }
    public DateTime TxDate { get; set; }
    public decimal Amount { get; set; }
    public string? Remark { get; set; }
}

public sealed class CreateCardPaymentRequest
{
    public string CardNo { get; set; } = "";
    public string? CardCompany { get; set; }
    public string? HolderName { get; set; }
    public DateTime PaymentDate { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal InstallmentAmount { get; set; }
    public int InstallmentMonths { get; set; }
    public string? Remark { get; set; }
    public List<CreateCardPaymentLineRequest> Lines { get; set; } = new();
}

public sealed class CreateCardPaymentLineRequest
{
    public string? PartnerId { get; set; }
    public DateTime TxDate { get; set; }
    public decimal Amount { get; set; }
    public string? Remark { get; set; }
}

// ═══ Bank Transactions (은행거래) ═══════════════════════════
public sealed class BankTxDto
{
    public string BankTxId { get; set; } = "";
    public string AccountNo { get; set; } = "";
    public string? BankName { get; set; }
    public DateTime TxDate { get; set; }
    public string TxType { get; set; } = "1";            // 1=입금 / 2=출금
    public decimal Amount { get; set; }
    public decimal? BalanceAfter { get; set; }
    public string? PartnerId { get; set; }
    public string? PartnerName { get; set; }
    public string? Description { get; set; }
    public string? Remark { get; set; }
    public string ImportedFrom { get; set; } = "manual";
}

public sealed class CreateBankTxRequest
{
    public string AccountNo { get; set; } = "";
    public string? BankName { get; set; }
    public DateTime TxDate { get; set; }
    public string TxType { get; set; } = "1";
    public decimal Amount { get; set; }
    public decimal? BalanceAfter { get; set; }
    public string? PartnerId { get; set; }
    public string? Description { get; set; }
    public string? Remark { get; set; }
}
