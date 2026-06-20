namespace HitPan.Application.Interfaces;

/// <summary>
/// AI 공급자 추상화 (Phase B 골격 / 2026-06-11 사장님 결재).
/// - 1차 표준: Claude (Anthropic). 본사 풀 응답도 Claude.
/// - 멀티 공급자 확장: OpenAI / Google (BYOK 전용).
/// - 헌법 #2 (tenant_id JWT만), #18·#22 (본사 업무 데이터 0), #23 (5중 검증), #25 (안전).
/// - 본 인터페이스는 골격 정의만. 실제 구현은 작업지시서 S2 단계.
/// </summary>
public interface IAiProvider
{
    /// <summary>공급자 식별자 (anthropic / openai / google).</summary>
    string ProviderId { get; }

    /// <summary>기본 모델명 (예: claude-opus-4-7).</summary>
    string DefaultModel { get; }

    /// <summary>
    /// 단순 자연어 응답. Tool Use 미사용.
    /// 사장님 결재 의무 #1 (간단한 자연어 도움) 경로.
    /// </summary>
    Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct = default);

    /// <summary>
    /// Tool Use 포함 응답. 자연어 → Tool 호출 라우팅.
    /// 사장님 결재 의무 #2 (자연어로 모든 기능 가도) 경로.
    /// </summary>
    Task<AiToolUseResult> CompleteWithToolsAsync(AiToolUseRequest request, CancellationToken ct = default);
}

/// <summary>
/// AI 완료 요청 DTO. 시스템 프롬프트 + 대화 이력 + 사용자 발화.
/// Prompt Caching 정합: system / cachedContext 별도 필드.
/// </summary>
public sealed record AiCompletionRequest(
    string SystemPrompt,
    string? CachedContext,
    IReadOnlyList<AiChatMessage> Messages,
    string? Model = null,
    int MaxTokens = 2048,
    decimal Temperature = 0.2m);

public sealed record AiCompletionResult(
    string Text,
    int InputTokens,
    int OutputTokens,
    int CachedTokens,
    string Model,
    string FinishReason);

public sealed record AiChatMessage(string Role, string Content);

/// <summary>
/// Tool Use 요청. ToolCatalog는 ChatbotOrchestrator가 빌드 시 자동 생성.
/// </summary>
public sealed record AiToolUseRequest(
    string SystemPrompt,
    string? CachedContext,
    IReadOnlyList<AiChatMessage> Messages,
    IReadOnlyList<AiToolDefinition> Tools,
    string? Model = null,
    int MaxTokens = 4096);

public sealed record AiToolUseResult(
    string? Text,
    IReadOnlyList<AiToolCall> ToolCalls,
    int InputTokens,
    int OutputTokens,
    int CachedTokens,
    string Model,
    string FinishReason);

/// <summary>
/// Tool 정의 표준. snake_case 이름, 한국어 설명, JSON Schema 파라미터.
/// 헌법 #2 정합: tenant_id / user_id 파라미터 금지 (JWT 클레임에서만).
/// </summary>
public sealed record AiToolDefinition(
    string Name,
    string Description,
    string InputSchemaJson,
    AiToolRiskLevel Risk);

public sealed record AiToolCall(
    string ToolUseId,
    string Name,
    string InputJson);

/// <summary>
/// Tool 위험 영역 분류. commit/cancel은 사용자 확인 다이얼로그 강제.
/// </summary>
public enum AiToolRiskLevel
{
    /// <summary>조회 전용 (SELECT). 직원 이상.</summary>
    Read = 0,
    /// <summary>초안 생성 (DB 미반영). 영업 이상.</summary>
    Draft = 1,
    /// <summary>확정 (원장 반영). 관리자만 + 확인 다이얼로그.</summary>
    Commit = 2,
    /// <summary>취소·환원. 관리자만 + 확인 다이얼로그.</summary>
    Cancel = 3
}
