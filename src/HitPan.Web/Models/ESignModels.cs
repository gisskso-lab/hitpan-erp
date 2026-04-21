namespace HitPan.Web.Models;

/// <summary>
/// 전자서명 이력 목록 행 모델이다.
/// </summary>
public sealed class ESignHistoryModel
{
    public string EsignId { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string? DocumentId { get; set; }
    public string? DocumentTitle { get; set; }
    public string? DocumentHash { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? SignerName { get; set; }
    public string? SignerPhone { get; set; }
    public string? SignerBirth { get; set; }
    public string? TxId { get; set; }
    public string Status { get; set; } = "signed";
    public DateTime SignedAt { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidReason { get; set; }
}

/// <summary>
/// 전자서명 요청 DTO이다. (POST /api/esign/sign)
/// </summary>
public sealed class ESignRequestModel
{
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string? DocumentTitle { get; set; }
    public string? DocumentHash { get; set; }
    public string Provider { get; set; } = "kakao";
    public string SignerName { get; set; } = string.Empty;
    public string? SignerPhone { get; set; }
    public string? SignerBirth { get; set; }
    public byte[]? SignatureBlob { get; set; }
    public string? ManualReason { get; set; }
}

/// <summary>
/// 전자서명 응답 모델이다.
/// </summary>
public sealed class ESignResponseModel
{
    public string EsignId { get; set; } = string.Empty;
    public string? TxId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; }
    public string Status { get; set; } = "signed";
}

/// <summary>
/// 근로계약서 목록 행 모델이다.
/// </summary>
public sealed class LaborContractListModel
{
    public string ContractId { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string ContractType { get; set; } = "regular";
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal Salary { get; set; }
    public string SalaryType { get; set; } = "monthly";
    public string Status { get; set; } = "draft";
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? SignedAt { get; set; }
    public string? EsignId { get; set; }
}

/// <summary>
/// 근로계약서 상세 모델이다.
/// </summary>
public sealed class LaborContractDetailModel
{
    public string ContractId { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string ContractType { get; set; } = "regular";
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? WorkLocation { get; set; }
    public string? JobDescription { get; set; }
    public string? WorkHours { get; set; }
    public decimal Salary { get; set; }
    public string SalaryType { get; set; } = "monthly";
    public int? PayDay { get; set; }
    public string? SocialInsurance { get; set; }
    public decimal? AnnualLeaveDays { get; set; }
    public string? SpecialTerms { get; set; }
    public string Status { get; set; } = "draft";
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? SignedAt { get; set; }
    public string? EsignId { get; set; }
}

/// <summary>
/// 근로계약서 생성 요청 모델이다.
/// </summary>
public sealed class CreateLaborContractModel
{
    public string EmployeeId { get; set; } = string.Empty;
    public string ContractType { get; set; } = "regular";
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }
    public string? WorkLocation { get; set; }
    public string? JobDescription { get; set; }
    public string? WorkHours { get; set; }
    public decimal Salary { get; set; }
    public string SalaryType { get; set; } = "monthly";
    public int? PayDay { get; set; } = 25;
    public string? SocialInsurance { get; set; }
    public decimal? AnnualLeaveDays { get; set; } = 15;
    public string? SpecialTerms { get; set; }
    /// <summary>즉시 발송 여부 (true면 draft로 저장 후 send 호출)</summary>
    public bool SendImmediately { get; set; }
}
