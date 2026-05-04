using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Backoffice;

// ── 대리점 목록 ────────────────────────────────────────────────
public class ResellerListItem
{
    public string ResellerId { get; set; } = string.Empty;
    public string ResellerCode { get; set; } = string.Empty;
    public string ResellerName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int CustomerCount { get; set; }
    public decimal MonthlyRevenue { get; set; }
    public decimal MonthlyCommission { get; set; }
    public DateTime JoinDate { get; set; }
}

public class ResellerListResponse
{
    public List<ResellerListItem> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int Size { get; set; }
    public int TotalPages { get; set; }
}

// ── 대리점 상세 ────────────────────────────────────────────────
public class ResellerDetail
{
    public string ResellerId { get; set; } = string.Empty;
    public string ResellerCode { get; set; } = string.Empty;
    public string ResellerName { get; set; } = string.Empty;
    public string BizNo { get; set; } = string.Empty;
    public string CeoName { get; set; } = string.Empty;
    public string? Tel { get; set; }
    public string? Address { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountMasked { get; set; }
    public string? AccountHolder { get; set; }
    public string? ContactPerson { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public DateTime JoinDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int CustomerCount { get; set; }
}

// ── 대리점 계좌 전체 (super_admin only) ───────────────────────
public class ResellerBankAccountResponse
{
    public string BankAccount { get; set; } = string.Empty;
}

// ── 대리점 등록 ────────────────────────────────────────────────
public class CreateResellerRequest
{
    [Required]
    [MaxLength(100)]
    public string ResellerName { get; set; } = string.Empty;

    [Required]
    [StringLength(12, MinimumLength = 10)]
    public string BizNo { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string CeoName { get; set; } = string.Empty;

    public string? Tel { get; set; }
    public string? Address { get; set; }
    public string? BankName { get; set; }
    public string? BankAccount { get; set; }
    public string? AccountHolder { get; set; }
    public string? ContactPerson { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }

    [Required]
    public DateTime JoinDate { get; set; }
}

public class CreateResellerResponse
{
    public string ResellerId { get; set; } = string.Empty;
    public string ResellerCode { get; set; } = string.Empty;
}

// ── 대리점 수정 ────────────────────────────────────────────────
public class UpdateResellerRequest
{
    public string? ResellerName { get; set; }
    public string? CeoName { get; set; }
    public string? Tel { get; set; }
    public string? Address { get; set; }
    public string? BankName { get; set; }
    public string? BankAccount { get; set; }
    public string? AccountHolder { get; set; }
    public string? ContactPerson { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
}

// ── 대리점 상태 변경 ───────────────────────────────────────────
public class UpdateResellerStatusRequest
{
    [Required]
    public string NewStatus { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

// ── 대리점 계정 ────────────────────────────────────────────────
public class ResellerAccountItem
{
    public string AccountId { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateResellerAccountRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string AccountName { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = "reseller_user";

    public string? Phone { get; set; }
}

public class CreateResellerAccountResponse
{
    public string AccountId { get; set; } = string.Empty;
    public string TempPassword { get; set; } = string.Empty;
}
