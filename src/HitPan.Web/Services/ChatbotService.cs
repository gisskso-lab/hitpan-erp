using System.Net.Http.Json;
using System.Text.Json;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

/// <summary>
/// 히트판 AI 챗봇 API 클라이언트.
/// 전역 HttpClient(JWT Handler 장착)를 그대로 재사용한다.
/// 예외 발생 시 null/빈 리스트 반환 — UI는 스낵바로 안내만 한다.
/// </summary>
public sealed class ChatbotService(HttpClient http, ILogger<ChatbotService> logger)
{
    /// <summary>
    /// 사용자 질문을 전송하고 AI 답변을 받는다.
    /// </summary>
    public async Task<ChatAnswerModel?> AskAsync(
        string message,
        IEnumerable<ChatTurnModel>? history = null,
        CancellationToken ct = default)
    {
        // 빈 메시지는 서버 호출 전에 차단
        if (string.IsNullOrWhiteSpace(message)) return null;

        // 직전 대화(history)를 함께 보내 맥락 기억 — "다시/그거 말고" 재명령 처리(사장님 결재 2026-06-20).
        var payload = new
        {
            message,
            history = (history ?? Enumerable.Empty<ChatTurnModel>())
                .Select(t => new { role = t.Role, content = t.Content })
                .ToList()
        };

        using var res = await http.PostAsJsonAsync("api/chatbot/ask", payload, ct)
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
    /// AI 직원 초안을 승인(확정 + 워크플로우 연쇄)한다 — /api/chatbot/approve-action 호출.
    /// 사람이 승인 버튼을 눌렀을 때만 호출(헌법 #6: 확정은 사람).
    /// 성공 시 처리 메시지(거래명세서 확정 + 수주 자동생성 등) 반환, 실패 시 null.
    /// </summary>
    public async Task<string?> ApproveActionAsync(string kind, string draftId, bool chain = true, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(draftId)) return null;
        try
        {
            using var res = await http.PostAsJsonAsync("api/chatbot/approve-action",
                new { kind, draftId, chain }, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;

            var dto = await res.Content.ReadFromJsonAsync<ApproveActionResultModel>(cancellationToken: ct)
                .ConfigureAwait(false);
            return dto is { Succeeded: true } ? dto.Message : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "도우미 초안 승인 실패 (kind={Kind} draftId={DraftId})", kind, draftId);
            return null;
        }
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
        catch (Exception ex)
        {
            // 헌법 #15 — 빈 catch 금지.
            logger.LogWarning(ex, "토큰 쿼터 조회 실패");
            return null;
        }
    }

    /// <summary>
    /// AI 도우미 연동(BYOK) 설정 현황을 조회한다. 평문 키는 내려오지 않는다.
    /// 실패 시 null을 반환한다.
    /// </summary>
    /// <remarks>
    /// 🔴 <c>null</c> 은 "연동 안 됨" 이 아니라 "못 불러왔다" 이다.
    /// 호출부는 반드시 둘을 구분해야 한다 — 실패를 미연결로 단정하면
    /// 키가 멀쩡히 저장돼 있는데도 고객이 키를 다시 붙여넣게 된다.
    /// </remarks>
    public async Task<AiSettingsModel?> GetAiSettingsAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<AiSettingsModel>("api/ai-settings", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 헌법 #15 — 빈 catch 금지. 진단 근거를 남긴다.
            logger.LogWarning(ex, "AI 도우미 연동 설정 조회 실패");
            return null;
        }
    }

    /// <summary>
    /// AI 도우미 연동 키를 저장한다(서버에서 AES-256 암호화).
    /// </summary>
    /// <remarks>
    /// 🔴 성공 여부만 돌려주면 화면이 "저장에 실패했습니다" 한 줄밖에 못 보여준다.
    /// 서버는 거절 사유를 한글로 담아 보내므로(예: 키 형식이 올바르지 않음)
    /// 그 사유를 그대로 화면까지 전달한다. 사유를 숨기면 고객은 무엇을 고쳐야 할지 알 수 없다.
    /// </remarks>
    public async Task<(bool ok, string? error)> SaveApiKeyAsync(string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return (false, "연동 키를 입력해주세요.");
        try
        {
            using var res = await http.PutAsJsonAsync("api/ai-settings/apikey", new { apiKey }, ct)
                .ConfigureAwait(false);
            if (res.IsSuccessStatusCode) return (true, null);

            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var reason = ExtractErrorMessage(body);
            logger.LogWarning("AI 도우미 연동 키 저장 실패 ({Status}): {Body}", (int)res.StatusCode, body);
            return (false, reason);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI 도우미 연동 키 저장 실패");
            return (false, "연결이 원활하지 않아 저장하지 못했습니다.");
        }
    }

    /// <summary>
    /// AI 도우미 연동 키를 삭제한다.
    /// </summary>
    public async Task<bool> DeleteApiKeyAsync(CancellationToken ct = default)
    {
        try
        {
            using var res = await http.DeleteAsync("api/ai-settings/apikey", ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                logger.LogWarning("AI 도우미 연동 키 삭제 실패 ({Status}): {Body}", (int)res.StatusCode, body);
            }
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI 도우미 연동 키 삭제 실패");
            return false;
        }
    }

    /// <summary>
    /// 서버가 <c>{ "error": "…" }</c> 형태로 보낸 거절 사유를 꺼낸다.
    /// </summary>
    /// <remarks>
    /// 응답 본문을 그대로 화면에 뿌리면 고객이 개발 용어를 보게 되므로,
    /// 사람이 읽을 수 있는 사유만 추려낸다. 형식이 다르면 무난한 안내 문구로 대체한다.
    /// </remarks>
    private static string ExtractErrorMessage(string? body)
    {
        const string fallback = "저장하지 못했습니다. 입력하신 내용을 확인해주세요.";
        if (string.IsNullOrWhiteSpace(body)) return fallback;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var err)
                && err.ValueKind == JsonValueKind.String)
            {
                var msg = err.GetString();
                if (!string.IsNullOrWhiteSpace(msg)) return msg;
            }
        }
        catch (JsonException)
        {
            // JSON 이 아닌 응답(HTML 오류 페이지 등)은 고객에게 보여줄 내용이 아니다.
            // 헌법 #15 — 삼키지 않고 아래 기본 안내로 넘긴다.
        }
        return fallback;
    }

    /// <summary>
    /// 이번 달 토큰 사용량(입력/출력/총·한도·잔여)을 조회한다.
    /// 실패 시 null을 반환한다.
    /// </summary>
    public async Task<AiUsageModel?> GetUsageAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<AiUsageModel>("api/chatbot/usage", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "이번 달 사용량 조회 실패");
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
        catch (Exception ex)
        {
            // 헌법 #15 — 빈 catch 금지. 추천 질문은 없어도 화면은 동작하므로 빈 목록으로 진행한다.
            logger.LogWarning(ex, "인기 질문 조회 실패");
            return new();
        }
    }
}
