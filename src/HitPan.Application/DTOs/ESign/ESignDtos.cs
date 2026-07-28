namespace HitPan.Application.DTOs.ESign;

/// <summary>
/// 전자서명 요청 DTO.
/// - 간편인증 4종(kakao/toss/finance/pass)은 signer_phone/signer_birth 기반 MockVerify
/// - 수동 3종(manual_upload/handwritten/admin_verified)은 signature_blob 또는 manual_reason 필수
/// </summary>
public sealed class ESignRequestDto
{
    public string DocumentType { get; set; } = "";
    public string DocumentId { get; set; } = "";
    public string DocumentTitle { get; set; } = "";
    public string DocumentHash { get; set; } = "";  // SHA-256 (문서 무결성 검증)
    public string Provider { get; set; } = "";       // kakao/toss/finance/pass/manual_upload/handwritten/admin_verified
    public string SignerName { get; set; } = "";
    public string? SignerPhone { get; set; }          // 간편인증용 (AES 암호화 저장)
    public string? SignerBirth { get; set; }          // 간편인증용 (AES 암호화 저장)
    public byte[]? SignatureBlob { get; set; }        // 손글씨 PNG / 스캔 이미지 원본
    public string? ManualReason { get; set; }         // 관리자 대리확인 사유
}

/// <summary>
/// 전자서명 결과 DTO.
/// </summary>
public sealed class ESignResultDto
{
    public string EsignId { get; set; } = "";
    public bool Success { get; set; }
    public string? ProviderTxId { get; set; }
    public string? Message { get; set; }
    public DateTime SignedAt { get; set; }
}

/// <summary>
/// 전자서명 기록 목록/상세 조회 DTO.
/// </summary>
public sealed class ESignRecordDto
{
    public string EsignId { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public string DocumentId { get; set; } = "";
    public string DocumentTitle { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ProviderLabel { get; set; } = "";  // UI 표시용 한글 라벨
    public string SignerName { get; set; } = "";
    public DateTime SignedAt { get; set; }
    public bool IsVoid { get; set; }
}

/// <summary>
/// 전자근로계약서 DTO.
/// </summary>
public sealed class LaborContractDto
{
    public string ContractId { get; set; } = "";
    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string ContractType { get; set; } = "regular";
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? WorkPlace { get; set; }
    public string? JobDescription { get; set; }
    public string? WorkingHours { get; set; }
    public decimal? SalaryAmount { get; set; }
    public string? SalaryType { get; set; }
    public string? PayDay { get; set; }
    public string? SocialInsurance { get; set; }
    public string? AnnualLeave { get; set; }
    public string? ExtraTerms { get; set; }
    public string Status { get; set; } = "draft";
    public DateTime? SentAt { get; set; }
    public DateTime? SignedAt { get; set; }
    public string? EsignId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 전자근로계약서 생성 요청 DTO.
/// </summary>
public sealed class CreateLaborContractRequest
{
    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string ContractType { get; set; } = "regular";
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? WorkPlace { get; set; }
    public string? JobDescription { get; set; }
    public string? WorkingHours { get; set; }
    public decimal? SalaryAmount { get; set; }
    public string? SalaryType { get; set; }
    public string? PayDay { get; set; }
    public string? SocialInsurance { get; set; }
    public string? AnnualLeave { get; set; }
    public string? ExtraTerms { get; set; }
}

/// <summary>
/// 전자서명 무효화 요청 DTO.
/// </summary>
public sealed class VoidESignRequest
{
    public string Reason { get; set; } = "";
}

/// <summary>
/// 전자근로계약서 서명 첨부 요청 DTO.
/// </summary>
public sealed class AttachSignatureRequest
{
    public string EsignId { get; set; } = "";
}
