using System.ComponentModel.DataAnnotations;

namespace HitPan.Application.DTOs.Auth;

// 첫 로그인 약관 4건 강제 동의 (헌법 #24 책임 분산 + 전자상거래법 정합)
public class TermsConsentRequest
{
    [Required] public bool AgreeService { get; set; }       // 서비스 이용 약관 (필수)
    [Required] public bool AgreePrivacy { get; set; }       // 개인정보 처리 방침 (필수)
    [Required] public bool AgreeSubscription { get; set; }  // 유료 구독·환불 (필수)
    [Required] public bool AgreeDataOwnership { get; set; } // 데이터 주권·책임 분산 (필수, 헌법 #22·#24)
    public bool? AgreeMarketing { get; set; }               // 마케팅 수신 (선택)

    [Required] public string TermsVersion { get; set; } = "v2.0.0";
}

public class TermsConsentResponse
{
    public string ConsentId { get; set; } = string.Empty;
    public DateTime AgreedAt { get; set; }
    public string ClientIp { get; set; } = string.Empty;
    public string TermsVersion { get; set; } = string.Empty;
}

public class TermsConsentStatus
{
    public bool HasAgreed { get; set; }
    public string? TermsVersion { get; set; }
    public DateTime? AgreedAt { get; set; }
    public List<string> Required { get; set; } = new() { "service", "privacy", "subscription", "data_ownership" };
}
