namespace HitPan.Web.Models;

/// <summary>
/// 챗봇 답변 모델 — /api/chatbot/ask 응답과 매핑.
/// confidenceScore가 0.5 이상이면 "히트판 공식 답변" 배지가 노출된다.
/// </summary>
public sealed class ChatAnswerModel
{
    public string ConvId { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public List<RelatedArticleModel> RelatedArticles { get; set; } = new();
    public decimal ConfidenceScore { get; set; }
    public int TokensUsed { get; set; }
    public int TokensRemaining { get; set; }
    public bool NeedsFollowUp { get; set; }
}

/// <summary>
/// 답변과 함께 반환되는 관련 KB 문서·메뉴 링크.
/// RelatedMenuUrl이 있을 때만 하단에 "관련 메뉴" 버튼으로 노출된다.
/// </summary>
public sealed class RelatedArticleModel
{
    public long ArticleId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? RelatedMenuUrl { get; set; }
}

/// <summary>
/// 테넌트의 이번달 CS 토큰 쿼터 현황.
/// ProgressBar 색상(Warning/Error)과 업그레이드 안내 표시 기준으로 쓰인다.
/// </summary>
public sealed class TokenQuotaModel
{
    public string AiMode { get; set; } = string.Empty;
    public int MonthlyLimit { get; set; }
    public int ExtraTokens { get; set; }
    public int UsedTokens { get; set; }
    public int Remaining { get; set; }
    public string? SubscriptionTier { get; set; }
    public bool AnthropicKeyConfigured { get; set; }
    public string? AnthropicKeyLast4 { get; set; }
}

/// <summary>
/// 인기 질문(KB 문서) 추천 모델.
/// 초기 진입 시 상위 5건이 "자주 묻는 질문"으로 버튼 렌더링된다.
/// </summary>
public sealed class KbArticleModel
{
    public long ArticleId { get; set; }
    public string? Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ContentMarkdown { get; set; }
    public string? RelatedMenuUrl { get; set; }
    public int HitCount { get; set; }
}
