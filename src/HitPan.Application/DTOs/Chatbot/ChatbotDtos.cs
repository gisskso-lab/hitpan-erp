namespace HitPan.Application.DTOs.Chatbot;

// ─────────────────────────────────────────────────────────────────
// 히트판 AI 챗봇 DTOs — Phase A (FAQ 매칭 + KB + 대화 이력 축적)
// ─────────────────────────────────────────────────────────────────

/// <summary>챗봇 질문 요청</summary>
public class ChatAskRequest
{
    public string Message { get; set; } = "";
}

/// <summary>챗봇 답변 응답</summary>
public class ChatAnswerDto
{
    public string ConvId { get; set; } = "";
    public string Answer { get; set; } = "";
    public List<RelatedArticleDto> RelatedArticles { get; set; } = new();
    public decimal ConfidenceScore { get; set; }
    public int TokensUsed { get; set; }
    public int TokensRemaining { get; set; }
    /// <summary>답변 애매할 때 추가 문의 유도</summary>
    public bool NeedsFollowUp { get; set; }
}

/// <summary>답변에 연결된 관련 KB 아티클 요약</summary>
public class RelatedArticleDto
{
    public string ArticleId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string? RelatedMenuUrl { get; set; }
}

/// <summary>답변 피드백 (도움됨/도움안됨)</summary>
public class ChatFeedbackRequest
{
    public string ConvId { get; set; } = "";
    public bool WasHelpful { get; set; }
}

/// <summary>테넌트 월간 토큰 할당량</summary>
public class TokenQuotaDto
{
    public string AiMode { get; set; } = "hitpan_pool";
    public int MonthlyLimit { get; set; }
    public int ExtraTokens { get; set; }
    public int UsedTokens { get; set; }
    public int Remaining { get; set; }
    public string SubscriptionTier { get; set; } = "";
    public bool AnthropicKeyConfigured { get; set; }
    public string? AnthropicKeyLast4 { get; set; }
}

/// <summary>KB 아티클 DTO (검색/인기 조회용)</summary>
public class KbArticleDto
{
    public string ArticleId { get; set; } = "";
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string ContentMarkdown { get; set; } = "";
    public string? RelatedMenuUrl { get; set; }
    public int HitCount { get; set; }
}
