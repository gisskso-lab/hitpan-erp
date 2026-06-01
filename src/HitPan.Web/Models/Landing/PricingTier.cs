namespace HitPan.Web.Models.Landing;

// 가격 단일 티어 (헌법 #4: 금액은 decimal)
public class PricingTier
{
    public string Name { get; set; } = string.Empty;
    public string Tagline { get; set; } = string.Empty;
    public decimal MonthlyPrice { get; set; }
    public string PriceUnit { get; set; } = "원/월";
    public string[] Features { get; set; } = Array.Empty<string>();
    public string? PriceNote { get; set; }
    public bool IsRecommended { get; set; }
    public string CtaText { get; set; } = "시작하기";
    public string CtaHref { get; set; } = "/landing/signup";
}
