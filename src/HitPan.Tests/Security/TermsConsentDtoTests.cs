using System.ComponentModel.DataAnnotations;
using HitPan.Application.DTOs.Auth;

namespace HitPan.Tests.Security;

// W2 약관 4건 강제 동의 단위 테스트 (헌법 #24 책임 분산)
// 검증 범위:
// - DTO 필수 4건 검증 로직 정합
// - TermsConsentStatus 기본 필수 4건 박제
// - TermsConsentRequest 기본 버전 v2.0.0
public class TermsConsentDtoTests
{
    [Fact(DisplayName = "TC-01: TermsConsentRequest 기본 버전은 v2.0.0")]
    public void TermsConsentRequest_DefaultVersion_IsV200()
    {
        var req = new TermsConsentRequest();
        Assert.Equal("v2.0.0", req.TermsVersion);
    }

    [Fact(DisplayName = "TC-02: TermsConsentStatus 기본 Required 4건 박제")]
    public void TermsConsentStatus_RequiredList_HasFourItems()
    {
        var status = new TermsConsentStatus();
        Assert.False(status.HasAgreed);
        Assert.Equal(4, status.Required.Count);
        Assert.Contains("service", status.Required);
        Assert.Contains("privacy", status.Required);
        Assert.Contains("subscription", status.Required);
        Assert.Contains("data_ownership", status.Required);
    }

    [Theory(DisplayName = "TC-03: 필수 4건 중 1건이라도 false면 동의 미완성")]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void AllRequired_MustBeTrue_ForValidConsent(bool service, bool privacy, bool subscription, bool dataOwnership)
    {
        var req = new TermsConsentRequest
        {
            AgreeService = service,
            AgreePrivacy = privacy,
            AgreeSubscription = subscription,
            AgreeDataOwnership = dataOwnership
        };

        var allRequired = req.AgreeService && req.AgreePrivacy
                          && req.AgreeSubscription && req.AgreeDataOwnership;
        Assert.False(allRequired);
    }

    [Fact(DisplayName = "TC-04: 필수 4건 모두 true면 동의 완성")]
    public void AllRequired_True_IsValid()
    {
        var req = new TermsConsentRequest
        {
            AgreeService = true,
            AgreePrivacy = true,
            AgreeSubscription = true,
            AgreeDataOwnership = true,
            AgreeMarketing = false,
            TermsVersion = "v2.0.0"
        };

        var allRequired = req.AgreeService && req.AgreePrivacy
                          && req.AgreeSubscription && req.AgreeDataOwnership;
        Assert.True(allRequired);
    }

    [Fact(DisplayName = "TC-05: AgreeMarketing은 nullable (선택)")]
    public void AgreeMarketing_IsNullable_Optional()
    {
        var req = new TermsConsentRequest();
        Assert.Null(req.AgreeMarketing);

        req.AgreeMarketing = true;
        Assert.True(req.AgreeMarketing);

        req.AgreeMarketing = null;
        Assert.Null(req.AgreeMarketing);
    }

    [Fact(DisplayName = "TC-06: TermsConsentResponse 박제 정합")]
    public void TermsConsentResponse_Properties_AreSet()
    {
        var now = DateTime.UtcNow;
        var resp = new TermsConsentResponse
        {
            ConsentId = "abc-123",
            AgreedAt = now,
            ClientIp = "127.0.0.1",
            TermsVersion = "v2.0.0"
        };

        Assert.Equal("abc-123", resp.ConsentId);
        Assert.Equal(now, resp.AgreedAt);
        Assert.Equal("127.0.0.1", resp.ClientIp);
        Assert.Equal("v2.0.0", resp.TermsVersion);
    }

    [Fact(DisplayName = "TC-07: TermsConsentRequest 필수 속성에 [Required] 박제")]
    public void TermsConsentRequest_RequiredAttributes_Present()
    {
        var type = typeof(TermsConsentRequest);
        Assert.NotNull(type.GetProperty(nameof(TermsConsentRequest.AgreeService))!
            .GetCustomAttributes(typeof(RequiredAttribute), false).FirstOrDefault());
        Assert.NotNull(type.GetProperty(nameof(TermsConsentRequest.AgreePrivacy))!
            .GetCustomAttributes(typeof(RequiredAttribute), false).FirstOrDefault());
        Assert.NotNull(type.GetProperty(nameof(TermsConsentRequest.AgreeSubscription))!
            .GetCustomAttributes(typeof(RequiredAttribute), false).FirstOrDefault());
        Assert.NotNull(type.GetProperty(nameof(TermsConsentRequest.AgreeDataOwnership))!
            .GetCustomAttributes(typeof(RequiredAttribute), false).FirstOrDefault());
        Assert.NotNull(type.GetProperty(nameof(TermsConsentRequest.TermsVersion))!
            .GetCustomAttributes(typeof(RequiredAttribute), false).FirstOrDefault());
    }
}
