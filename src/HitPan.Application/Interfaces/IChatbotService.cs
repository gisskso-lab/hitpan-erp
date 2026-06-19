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

    /// <summary>
    /// AI 직원이 만든 초안을 승인 처리한다 (Lv.3 워크플로우 연쇄, 사장님 결재 2026-06-20).
    /// 거래명세서 초안 승인 시 → 확정 + 대응 수주 자동생성(워크플로우 정합, 헌법 #20).
    /// 사람이 승인 버튼을 눌렀을 때만 호출(확정은 사람 — 헌법 #6).
    /// </summary>
    Task<ApproveActionResultDto> ApproveActionAsync(ApproveActionRequest req, string tenantId, string userId, CancellationToken ct = default);

    /// <summary>답변에 대한 도움됨/도움안됨 피드백을 기록한다.</summary>
    Task RecordFeedbackAsync(ChatFeedbackRequest req, string tenantId, CancellationToken ct = default);

    /// <summary>현재 테넌트의 월간 토큰 할당량/사용량을 조회한다.</summary>
    Task<TokenQuotaDto> GetQuotaAsync(string tenantId, CancellationToken ct = default);

    // ───────────────────────── BYOK (AI 도우미 연동 키) ─────────────────────────

    /// <summary>AI 도우미 연동 키를 AES-256 암호화하여 저장한다(last4만 평문 보관).</summary>
    Task SaveApiKeyAsync(string apiKey, string tenantId, CancellationToken ct = default);

    /// <summary>AI 도우미 연동 키를 삭제한다(컬럼 NULL, status='none').</summary>
    Task DeleteApiKeyAsync(string tenantId, CancellationToken ct = default);

    /// <summary>AI 도우미 설정 현황(키 설정 여부·last4·한도)을 조회한다. 평문 키는 반환하지 않는다.</summary>
    Task<AiSettingsDto> GetAiSettingsAsync(string tenantId, CancellationToken ct = default);

    /// <summary>이번 달 토큰 사용량을 ai_usage_logs(ym 기준)에서 집계한다.</summary>
    Task<AiUsageDto> GetUsageAsync(string tenantId, CancellationToken ct = default);

    /// <summary>KB 검색 (제목/키워드/본문 LIKE 매칭).</summary>
    Task<List<KbArticleDto>> SearchKbAsync(string query, string? category, int limit, CancellationToken ct = default);

    /// <summary>히트카운트 기준 인기 KB 목록.</summary>
    Task<List<KbArticleDto>> GetPopularKbAsync(int limit, CancellationToken ct = default);
}
