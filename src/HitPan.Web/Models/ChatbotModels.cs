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

    /// <summary>AI 직원 분석 결과(있으면 표+차트 렌더). 단순 답변일 땐 null.</summary>
    public AiAnalysisResultModel? Analysis { get; set; }

    /// <summary>생성 명령 초안 — 있으면 승인/반려 카드 렌더. 승인 시 ApproveUrl 호출.</summary>
    public PendingActionModel? PendingAction { get; set; }
}

/// <summary>사람 승인 대기 액션 (AI 직원이 만든 초안). 승인/반려 카드 렌더용.</summary>
public sealed class PendingActionModel
{
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? DraftId { get; set; }
    public string? ApproveMethod { get; set; }
    public string? ApproveUrl { get; set; }
    public string? ChainNote { get; set; }
}

/// <summary>초안 승인 처리 결과 — /api/chatbot/approve-action 응답.</summary>
public sealed class ApproveActionResultModel
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ChainedId { get; set; }
    public string? ChainedNo { get; set; }
}

/// <summary>AI 직원 분석 결과 — 표 + 막대차트 렌더용.</summary>
public sealed class AiAnalysisResultModel
{
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();
    public List<ChartPointModel> Chart { get; set; } = new();
    public string ChartValueLabel { get; set; } = string.Empty;
}

public sealed class ChartPointModel
{
    public string Label { get; set; } = string.Empty;
    public double Value { get; set; }
}

/// <summary>화면 대화 1턴 (채팅 누적 + 서버 history 전송용).</summary>
public sealed class ChatTurnModel
{
    /// <summary>"user" 또는 "assistant"</summary>
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;

    /// <summary>도우미 답변 턴이면 전체 답변 모델(표·차트 렌더용). 사용자 턴이면 null.</summary>
    public ChatAnswerModel? Answer { get; set; }
}

/// <summary>
/// 답변과 함께 반환되는 관련 KB 문서·메뉴 링크.
/// RelatedMenuUrl이 있을 때만 하단에 "관련 메뉴" 버튼으로 노출된다.
/// </summary>
public sealed class RelatedArticleModel
{
    // UUID 기반 VARCHAR(36) — long 아님
    public string ArticleId { get; set; } = string.Empty;
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
/// AI 도우미 연동(BYOK) 설정 현황 — GET /api/ai-settings 응답과 매핑.
/// 평문 키는 절대 내려오지 않으며 끝 4자리(KeyLast4)만 표시한다.
/// </summary>
public sealed class AiSettingsModel
{
    public bool KeyConfigured { get; set; }
    public string? KeyLast4 { get; set; }
    public string KeyStatus { get; set; } = "none";
    public DateTime? KeySavedAt { get; set; }
    public string AiMode { get; set; } = "hitpan_pool";
    public int MonthlyLimit { get; set; }
    public int ExtraTokens { get; set; }
    public string SubscriptionTier { get; set; } = string.Empty;

    // ── 3사 확장 (2026-08-12, 20260812작1 · 사장님 결재) ──
    //    위 필드들은 "현재 선택된 공급자" 의 상태다(서버가 그렇게 채워 보낸다).

    /// <summary>현재 사용 공급자 — anthropic / openai / google</summary>
    public string AiProvider { get; set; } = "anthropic";

    /// <summary>3사 전체 연동 상태. 화면 탭이 이걸 보고 그린다.</summary>
    public List<AiProviderStatusModel> Providers { get; set; } = new();
}

/// <summary>공급자 1곳의 연동 상태 — GET /api/ai-settings 응답의 providers[] 와 매핑.</summary>
public sealed class AiProviderStatusModel
{
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>고객 화면 표기 (사장님 결재): 클로드AI / 챗GPT / 제미나이</summary>
    public string DisplayName { get; set; } = string.Empty;

    public bool KeyConfigured { get; set; }
    public string? KeyLast4 { get; set; }
    public string KeyStatus { get; set; } = "none";
    public DateTime? KeySavedAt { get; set; }

    /// <summary>실제 외부 호출로 연결이 확인된 시각. null 이면 아직 진짜로 확인된 적 없다.</summary>
    public DateTime? KeyVerifiedAt { get; set; }
}

/// <summary>[연결 확인] 결과 — POST /api/ai-settings/check/{providerId} 응답과 매핑.</summary>
public sealed class AiConnectionCheckModel
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime? VerifiedAt { get; set; }
}

/// <summary>
/// 이번 달 토큰 사용량 — GET /api/chatbot/usage 응답과 매핑.
/// </summary>
public sealed class AiUsageModel
{
    public string Ym { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }
    public int MonthlyLimit { get; set; }
    public int Remaining { get; set; }
}

/// <summary>
/// 인기 질문(KB 문서) 추천 모델.
/// 초기 진입 시 상위 5건이 "자주 묻는 질문"으로 버튼 렌더링된다.
/// </summary>
public sealed class KbArticleModel
{
    // UUID 기반 VARCHAR(36) — long 아님
    public string ArticleId { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ContentMarkdown { get; set; }
    public string? RelatedMenuUrl { get; set; }
    public int HitCount { get; set; }
}
