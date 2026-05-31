using System.Net.Http.Json;
using HitPan.Web.Models;

namespace HitPan.Web.Services;

/// <summary>
/// 히트판 AI 챗봇 API 클라이언트.
/// 전역 HttpClient(JWT Handler 장착)를 그대로 재사용한다.
/// 예외 발생 시 null/빈 리스트 반환 — UI는 스낵바로 안내만 한다.
/// </summary>
public sealed class ChatbotService(HttpClient http)
{
    /// <summary>
    /// 사용자 질문을 전송하고 AI 답변을 받는다.
    /// </summary>
    public async Task<ChatAnswerModel?> AskAsync(string message, CancellationToken ct = default)
    {
        // 빈 메시지는 서버 호출 전에 차단
        if (string.IsNullOrWhiteSpace(message)) return null;

        using var res = await http.PostAsJsonAsync("api/chatbot/ask", new { message }, ct)
            .ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            // 서버가 쿼터 초과/인증 실패 등을 반환할 수 있으므로 메시지를 예외로 던진다
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException($"{(int)res.StatusCode} {res.ReasonPhrase}: {body}");
        }
        return await res.Content.ReadFromJsonAsync<ChatAnswerModel>(cancellationToken: ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 답변에 대한 도움 여부를 서버에 기록한다 (학습 피드백).
    /// </summary>
    public async Task RecordFeedbackAsync(string convId, bool wasHelpful, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(convId)) return;
        using var res = await http.PostAsJsonAsync(
            "api/chatbot/feedback",
            new { convId, wasHelpful },
            ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// 이번달 토큰 쿼터·구독 티어·API 키 설정 상태를 조회한다.
    /// 비로그인 등 실패 시 null을 반환한다.
    /// </summary>
    public async Task<TokenQuotaModel?> GetQuotaAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<TokenQuotaModel>("api/chatbot/quota", ct)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 인기 질문(KB) 상위 목록을 조회한다. 초기 화면 추천에 쓰인다.
    /// </summary>
    public async Task<List<KbArticleModel>> GetPopularKbAsync(int limit = 10, CancellationToken ct = default)
    {
        try
        {
            var list = await http.GetFromJsonAsync<List<KbArticleModel>>(
                $"api/chatbot/kb/popular?limit={limit}", ct).ConfigureAwait(false);
            return list ?? new();
        }
        catch (Exception)
        {
            return new();
        }
    }
}
