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

    // 작(2026-08-12) 단계0 P0-D: 무효화된 서명이 "유효"로 표시되던 결함 봉합.
    // API 는 is_void AS IsVoid 로 내려주는데(ESignatureService.cs:72) 이 모델에는 IsVoid 가
    // 아예 없었고, 화면은 존재하지 않는 Status 로 판정했다. Status 는 초기값 "signed" 에서
    // 바뀌지 않으므로 무효 서명도 항상 "유효"로 보였고, 무효 건에 [무효화] 버튼까지 계속 떴다.
    // 🔴 전자서명은 5년 보관·감사추적 대상이라 "무효인데 유효로 보이는 것"은 문서 신뢰의 문제다.
    public bool IsVoid { get; set; }

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

    // 작(2026-08-12) 단계0 P0-D: 계약서 급여가 항상 0원으로 보이던 결함 봉합.
    // API 는 salary_amount AS SalaryAmount 로 내려주는데
    // (src/HitPan.Infrastructure/Services/LaborContractService.cs:46)
    // 이 모델은 Salary 로 받고 있었다. 이름이 달라 역직렬화가 짝을 못 찾고 0 이 남았다.
    // 500 이 안 나서 더 위험했다 — 고객이 "급여 0원"을 사실로 믿는다.
    // 속성명을 바꾸면 이 모델을 쓰는 화면이 전부 깨지므로 JSON 이름만 맞춘다(헌법 #1).
    //
    // 🔴 검증팀 P0-2 봉합 — 이름만 맞추고 타입을 안 맞춰 새 결함을 만들었다.
    //    salary_amount 는 NULL 허용이고 API DTO 도 decimal? 인데 여기가 non-nullable decimal 이면
    //    NULL 이 오는 순간 JsonException 이 난다. 이름이 안 맞을 때는 그 키를 무시해서 조용했지만,
    //    맞추는 순간 예외 경로가 열린다. 게다가 목록 조회의 catch 가 그 예외를 삼켜
    //    급여 미입력 계약서가 한 장만 있어도 목록 전체가 빈 화면이 된다.
    //    ⇒ decimal? 로 받고, 화면 표시용 기본값은 Salary 로 노출한다(기존 바인딩 보존).
    [JsonPropertyName("salaryAmount")]
    public decimal? SalaryAmount { get; set; }

    /// <summary>화면 표시용. 급여 미입력이면 0 으로 본다.</summary>
    [JsonIgnore]
    public decimal Salary => SalaryAmount ?? 0m;
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

    // 작(2026-08-12) 단계0 P0-D: API 이름과 달라 값이 안 채워지던 필드 봉합.
    // API(LaborContractService.cs:43~50)는 WorkPlace/WorkingHours/SalaryAmount/AnnualLeave/
    // ExtraTerms 로 내려주는데 이 모델은 다른 이름으로 받고 있었다.
    // ⇒ 계약서 상세에서 근무장소·근로시간·급여·연차·특약이 전부 비어 보였다.
    // 속성명은 화면이 쓰고 있으므로 그대로 두고 JSON 이름만 맞춘다(헌법 #1).
    [JsonPropertyName("workPlace")]
    public string? WorkLocation { get; set; }
    public string? JobDescription { get; set; }
    [JsonPropertyName("workingHours")]
    public string? WorkHours { get; set; }

    // 🔴 검증팀 P0-2 봉합: NULL 허용 컬럼이므로 decimal? 로 받는다(위 목록 모델과 같은 이유).
    [JsonPropertyName("salaryAmount")]
    public decimal? SalaryAmount { get; set; }

    /// <summary>화면 표시용. 급여 미입력이면 0 으로 본다.</summary>
    [JsonIgnore]
    public decimal Salary => SalaryAmount ?? 0m;

    public string SalaryType { get; set; } = "monthly";

    // 🔴 검증팀 P0-3 봉합 — 타입이 실제 컬럼과 달랐다.
    //    DB 는 pay_day varchar(20) · annual_leave varchar(100) 이고 API DTO 도 둘 다 string? 이다.
    //    자유 문자열을 받으라고 만든 칸이라 인사담당자는 "매월 25일", "연 15일" 처럼 쓴다.
    //    그런데 여기가 int?/decimal? 이면 그 순간 JsonException 이 나고, 상세 조회의 catch 가
    //    이를 삼켜 화면이 통째로 안 열린다.
    //    ⚠️ PayDay 는 이름이 원래 맞아 있어서 봉합 전부터 잠복해 있던 결함이고
    //      (숫자 문자열 "25" 만 우연히 통과했다), AnnualLeave 는 이번에 이름을 맞추면서 드러났다.
    [JsonPropertyName("payDay")]
    public string? PayDay { get; set; }

    public string? SocialInsurance { get; set; }

    [JsonPropertyName("annualLeave")]
    public string? AnnualLeaveDays { get; set; }

    [JsonPropertyName("extraTerms")]
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
