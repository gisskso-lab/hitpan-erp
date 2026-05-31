using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Landing;

// 작9 랜딩 가입 폼 — 정식 가입 흐름 (W2 매니저 가도용 스켈레톤)
// 헌법 #22 본사 메타만 (사업자번호·이메일만, 업무 데이터 0)
// 헌법 #25 3대 원칙 — 쉽게·정확하게·안전하게

public class SignupRequest
{
    [Required] public string CompanyName { get; set; } = string.Empty;
    [Required, RegularExpression(@"^\d{3}-?\d{2}-?\d{5}$")] public string BizNo { get; set; } = string.Empty;
    [Required] public string CeoName { get; set; } = string.Empty;
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required] public string Phone { get; set; } = string.Empty;
    [Required] public string PlanType { get; set; } = "basic"; // basic · pro · enterprise
    public string? ResellerCode { get; set; } // 대리점 추천 코드 (선택)
    [Required] public bool AgreeTerms { get; set; }
    [Required] public bool AgreePrivacy { get; set; }
}

public class SignupResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? SignupToken { get; set; } // OTP 인증 단계로 진행할 토큰
}

public class OtpRequestDto
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required] public string SignupToken { get; set; } = string.Empty;
}

public class OtpVerifyDto
{
    [Required] public string SignupToken { get; set; } = string.Empty;
    [Required, StringLength(6, MinimumLength = 6)] public string Code { get; set; } = string.Empty;
}

public class OtpVerifyResponse
{
    public bool Verified { get; set; }
    public string? PaymentUrl { get; set; } // 토스 결재 URL
    public string? Message { get; set; }
}

public class LicenseClaimRequest
{
    [Required] public string LicenseKey { get; set; } = string.Empty;
}

public class LicenseClaimResponse
{
    public bool Valid { get; set; }
    public string? DownloadUrl { get; set; }
    public string? CompanyName { get; set; }
    public string? Message { get; set; }
}
