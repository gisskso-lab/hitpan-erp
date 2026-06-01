using HitPan.Web.Models.Landing;
using MudBlazor;

namespace HitPan.Web.Services;

// 랜딩 v2 정적 콘텐츠 박제 서비스 (사장님 결재 2026-06-01).
// 카피는 placeholder — 마케팅팀장 docs/marketing/LANDING_COPY_V2.md 박제 후 교체.
// DB 미사용. 디자인은 수석 웹디자이너 책임, 본 서비스는 컨텐츠 모델만 제공.
public class LandingContentService
{
    // TODO(마케팅팀장 LANDING_COPY_V2.md): 카피 최종본 교체
    public HeroContent GetHeroContent() => new()
    {
        BadgeText = "히트판 ERP — 2026 베타",
        Headline = "현장에서 그냥 되는 ERP",
        SubHeadline = "쉽게·정확하게·안전하게. 설명서 없이 첫날부터 쓰는 히트판.",
        PrimaryCtaText = "무료로 시작하기",
        PrimaryCtaHref = "/landing/signup",
        SecondaryCtaText = "데모 둘러보기",
        SecondaryCtaHref = "/landing/demo",
    };

    // TODO(마케팅팀장): 셀링 카피 최종본 교체
    public List<FeatureItem> GetFeatures() => new()
    {
        new FeatureItem
        {
            Icon = Icons.Material.Filled.FlashOn,
            Title = "10분 설치",
            Description = "라이선스 키 하나로 끝. 도메인·DB·터널 자동 발급.",
        },
        new FeatureItem
        {
            Icon = Icons.Material.Filled.Lock,
            Title = "내 데이터는 내 PC에",
            Description = "본사는 결제·메타정보만. 매출·매입은 사장님 PC에 보관.",
        },
        new FeatureItem
        {
            Icon = Icons.Material.Filled.SwapHoriz,
            Title = "끊김 없는 흐름",
            Description = "매입·BOM·판매 3흐름이 한 번도 끊기지 않는 무결성.",
        },
        new FeatureItem
        {
            Icon = Icons.Material.Filled.School,
            Title = "처음 봐도 쓰는 UI",
            Description = "교육 없이 첫날부터 — 레거시 히트판의 쉬움을 그대로.",
        },
        new FeatureItem
        {
            Icon = Icons.Material.Filled.Receipt,
            Title = "전자세금계산서 완비",
            Description = "발급·전송·홈택스 신고까지 한 화면에서.",
        },
        new FeatureItem
        {
            Icon = Icons.Material.Filled.SupportAgent,
            Title = "AI CS + 원격 지원",
            Description = "챗봇이 1차, 안 되면 원격 지원으로 즉시 봉합.",
        },
    };

    // TODO(마케팅팀장): 가격·기능 라인업 최종 확정 후 교체 (헌법 #4 decimal)
    public List<PricingTier> GetPricingTiers() => new()
    {
        new PricingTier
        {
            Name = "스타터",
            Tagline = "1인 사장님·개인사업자",
            MonthlyPrice = 29_000m,
            Features = new[]
            {
                "기기 1대",
                "매입·판매·재고 기본",
                "전자세금계산서 월 50건",
                "이메일 지원",
            },
            IsRecommended = false,
            CtaText = "스타터 시작",
        },
        new PricingTier
        {
            Name = "비즈니스",
            Tagline = "직원 2~10명 소상공인",
            MonthlyPrice = 59_000m,
            Features = new[]
            {
                "기기 3대",
                "BOM·생산 포함",
                "전자세금계산서 월 300건",
                "AI CS 챗봇",
                "전화·원격 지원",
            },
            IsRecommended = true,
            CtaText = "비즈니스 시작",
        },
        new PricingTier
        {
            Name = "프로",
            Tagline = "직원 11~30명 중소기업",
            MonthlyPrice = 100_000m,
            Features = new[]
            {
                "기기 무제한",
                "회계·경리 풀세트",
                "전자세금계산서 무제한",
                "전담 매니저",
                "백업·DR 우선 지원",
            },
            IsRecommended = false,
            CtaText = "프로 시작",
        },
    };

    // TODO(마케팅팀장): 베타 체험단 실사례로 교체
    public List<TestimonialItem> GetTestimonials() => new()
    {
        new TestimonialItem
        {
            Author = "김OO 대표",
            Company = "OO상사",
            Role = "도소매업 15년차",
            Quote = "레거시 히트판 그대로의 쉬움인데, 화면이 한결 깔끔합니다.",
        },
        new TestimonialItem
        {
            Author = "박OO 경리",
            Company = "OO제조",
            Role = "경리 8년차",
            Quote = "세금계산서·매입·매출이 한 화면에서 끊김 없이 흘러요.",
        },
        new TestimonialItem
        {
            Author = "이OO 사장",
            Company = "OO유통",
            Role = "유통업 22년차",
            Quote = "내 PC에 데이터가 있다는 게 가장 안심됩니다.",
        },
    };

    // TODO(마케팅팀장): FAQ 최종본 + 법무팀장 검수
    public List<FaqItem> GetFaqs() => new()
    {
        new FaqItem
        {
            Question = "데이터는 어디에 저장되나요?",
            Answer = "사장님 PC(MariaDB)에 저장됩니다. 본사는 결제·라이선스 메타정보만 보유합니다.",
        },
        new FaqItem
        {
            Question = "설치가 어렵지 않나요?",
            Answer = "라이선스 키 하나만 입력하면 10분 안에 자동 설치됩니다. 도메인·DB·터널 모두 자동 발급.",
        },
        new FaqItem
        {
            Question = "기존 히트판(레거시)에서 옮겨올 수 있나요?",
            Answer = "네. MDB 파일을 선택하시면 마이그레이션 마법사가 자동 변환합니다.",
        },
        new FaqItem
        {
            Question = "전자세금계산서는 어떻게 발급하나요?",
            Answer = "거래명세서에서 한 번 클릭으로 발급 → 홈택스 자동 전송까지 처리됩니다.",
        },
        new FaqItem
        {
            Question = "베타 기간은 언제까지인가요?",
            Answer = "2026년 7월 15일까지 베타 운영, 정식 출시는 8월 24일 예정입니다.",
        },
    };

    public LandingContent GetAll() => new()
    {
        Hero = GetHeroContent(),
        Features = GetFeatures(),
        PricingTiers = GetPricingTiers(),
        Testimonials = GetTestimonials(),
        Faqs = GetFaqs(),
    };
}
