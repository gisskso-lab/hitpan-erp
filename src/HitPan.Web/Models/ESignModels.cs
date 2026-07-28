using System.Globalization;
using System.Text.Json.Serialization;

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
    // ── UI 바인딩용 프로퍼티 (화면 @bind-Value 그대로 유지) ──
    // 봉합 (2026-06-22, 10차 P0-2): 백엔드 CreateLaborContractRequest와 필드명/타입이 달라
    // 직렬화 시 급여·근무지 등이 silent 유실되고 EmployeeName 누락으로 400이 고정됐다.
    // 화면 코드를 건드리지 않기 위해 UI용 프로퍼티는 그대로 두되 [JsonIgnore] 처리하고,
    // 아래에 백엔드 DTO와 이름·타입이 정확히 일치하는 직렬화 전용 프로퍼티를 추가해 매핑한다.
    [JsonIgnore]
    public string EmployeeId { get; set; } = string.Empty;
    [JsonIgnore]
    public string ContractType { get; set; } = "regular";
    [JsonIgnore]
    public DateTime StartDate { get; set; } = DateTime.Today;
    [JsonIgnore]
    public DateTime? EndDate { get; set; }
    [JsonIgnore]
    public string? WorkLocation { get; set; }
    [JsonIgnore]
    public string? JobDescription { get; set; }
    [JsonIgnore]
    public string? WorkHours { get; set; }
    [JsonIgnore]
    public decimal Salary { get; set; }
    [JsonIgnore]
    public string SalaryType { get; set; } = "monthly";
    [JsonIgnore]
    public int? PayDay { get; set; } = 25;
    [JsonIgnore]
    public string? SocialInsurance { get; set; }
    [JsonIgnore]
    public decimal? AnnualLeaveDays { get; set; } = 15;
    [JsonIgnore]
    public string? SpecialTerms { get; set; }

    /// <summary>봉합 (2026-06-22, 10차 P0-2): 사원명. 미전송 시 백엔드가 400을 던진다. 필수.</summary>
    [JsonIgnore]
    public string EmployeeName { get; set; } = string.Empty;

    /// <summary>즉시 발송 여부 (true면 draft로 저장 후 send 호출)</summary>
    [JsonIgnore]
    public bool SendImmediately { get; set; }

    // ── 직렬화 전용 프로퍼티 (백엔드 CreateLaborContractRequest와 정확히 일치) ──
    // 봉합 (2026-06-22, 10차 P0-2): JSON 속성명을 백엔드 DTO와 1:1로 맞춘다.
    // PayDay·AnnualLeave는 백엔드가 string?이므로 문자열로 변환해 전송한다.

    [JsonPropertyName("employeeId")]
    public string SerEmployeeId => EmployeeId;

    [JsonPropertyName("employeeName")]
    public string SerEmployeeName => EmployeeName;

    [JsonPropertyName("contractType")]
    public string SerContractType => ContractType;

    [JsonPropertyName("startDate")]
    public DateTime SerStartDate => StartDate;

    [JsonPropertyName("endDate")]
    public DateTime? SerEndDate => EndDate;

    [JsonPropertyName("workPlace")]
    public string? SerWorkPlace => WorkLocation;

    [JsonPropertyName("jobDescription")]
    public string? SerJobDescription => JobDescription;

    [JsonPropertyName("workingHours")]
    public string? SerWorkingHours => WorkHours;

    [JsonPropertyName("salaryAmount")]
    public decimal? SerSalaryAmount => Salary;

    [JsonPropertyName("salaryType")]
    public string? SerSalaryType => SalaryType;

    [JsonPropertyName("payDay")]
    public string? SerPayDay => PayDay?.ToString(CultureInfo.InvariantCulture);

    [JsonPropertyName("socialInsurance")]
    public string? SerSocialInsurance => SocialInsurance;

    [JsonPropertyName("annualLeave")]
    public string? SerAnnualLeave => AnnualLeaveDays?.ToString(CultureInfo.InvariantCulture);

    [JsonPropertyName("extraTerms")]
    public string? SerExtraTerms => SpecialTerms;
}
