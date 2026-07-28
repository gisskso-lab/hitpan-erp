namespace HitPan.Web.Models.Landing;

// 랜딩 v2 통합 콘텐츠 모델 (사장님 결재 2026-06-01: 좀 더 이쁘게)
// 데이터 소스: 정적 저장 (LandingContentService). 카피는 마케팅팀장
// docs/marketing/LANDING_COPY_V2.md 저장 후 차후 교체 표시.
public class LandingContent
{
    public HeroContent Hero { get; set; } = new();
    public List<FeatureItem> Features { get; set; } = new();
    public List<PricingTier> PricingTiers { get; set; } = new();
    public List<TestimonialItem> Testimonials { get; set; } = new();
    public List<FaqItem> Faqs { get; set; } = new();
}

// 히어로 영역 (대문 첫인상)
public class HeroContent
{
    public string Headline { get; set; } = string.Empty;
    public string SubHeadline { get; set; } = string.Empty;
    public string PrimaryCtaText { get; set; } = string.Empty;
    public string PrimaryCtaHref { get; set; } = string.Empty;
    public string SecondaryCtaText { get; set; } = string.Empty;
    public string SecondaryCtaHref { get; set; } = string.Empty;
    public string BadgeText { get; set; } = string.Empty;
}
