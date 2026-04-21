using HitPan.Application.DTOs.Chatbot;

namespace HitPan.Application.Interfaces;

/// <summary>
/// 히트판 AI 챗봇 서비스 인터페이스.
/// Phase A: FAQ/KB 매칭 기반 답변 + 대화 이력 축적(학습 자산).
/// Phase B 예정: Anthropic Claude 연동 + 토큰 차감/과금.
/// </summary>
public interface IChatbotService
{
    /// <summary>질문을 받아 답변을 구성하고 대화 이력에 저장한다.</summary>
    Task<ChatAnswerDto> AskAsync(ChatAskRequest req, string tenantId, string userId, CancellationToken ct = default);

    /// <summary>답변에 대한 도움됨/도움안됨 피드백을 기록한다.</summary>
    Task RecordFeedbackAsync(ChatFeedbackRequest req, string tenantId, CancellationToken ct = default);

    /// <summary>현재 테넌트의 월간 토큰 할당량/사용량을 조회한다.</summary>
    Task<TokenQuotaDto> GetQuotaAsync(string tenantId, CancellationToken ct = default);

    /// <summary>KB 검색 (제목/키워드/본문 LIKE 매칭).</summary>
    Task<List<KbArticleDto>> SearchKbAsync(string query, string? category, int limit, CancellationToken ct = default);

    /// <summary>히트카운트 기준 인기 KB 목록.</summary>
    Task<List<KbArticleDto>> GetPopularKbAsync(int limit, CancellationToken ct = default);
}
