using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

// ─────────────────────────────────────────────────────────────────
// 히트판 도우미 — 챗GPT(OpenAI) 직통 호출 어댑터 (BYOK)
//
// 신규(2026-08-12, 작업지시서 20260812작1 · 사장님 결재):
//   "기존 : 클로드API만 지원 -> 수정 : 클로드, 챗지피티, 제미나이API까지 받을 수 있게"
//
//  - 고객 PC → OpenAI API 직통 (헌법 #18·#22: 본사 프록시 0, 업무 데이터 본사 전송 0).
//  - 복호화된 BYOK 키는 호출 직전에만 메모리 보유, 로그·DB 저장 금지(헌법 #5).
//  - 빈 catch 금지(헌법 #15): 모든 실패는 LogWarning 후 Fail 반환 → 호출부가 KB-only 폴백.
//  - 기존 AnthropicChatProvider 는 한 줄도 수정하지 않는다(헌법 #1). 이 파일은 순수 추가다.
//
// 🔴 Tool Use 미지원 — 정직하게 실패를 반환한다 (아래 CompleteWithToolsAsync 주석 참조).
// ─────────────────────────────────────────────────────────────────

/// <summary>
/// OpenAI Chat Completions API 어댑터.
/// POST https://api.openai.com/v1/chat/completions
///   headers: Authorization: Bearer {key}
///   body: { model, max_completion_tokens, messages:[{role,content}] }
/// </summary>
public sealed class OpenAiChatProvider : IChatCompletionProvider
{
    // 모델은 설정 가능. 기본은 범용·비용 균형 모델.
    private const string DefaultModel = "gpt-4.1";
    private const int DefaultMaxTokens = 1024;
    private const string CompletionsUrl = "https://api.openai.com/v1/chat/completions";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OpenAiChatProvider> _logger;
    private readonly string _model;
    private readonly int _maxTokens;

    public OpenAiChatProvider(
        IHttpClientFactory httpFactory,
        ILogger<OpenAiChatProvider> logger,
        string? model = null,
        int? maxTokens = null)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model!;
        _maxTokens = maxTokens is > 0 ? maxTokens!.Value : DefaultMaxTokens;
    }

    public async Task<ChatProviderResult> CompleteAsync(
        string decryptedApiKey,
        string systemPrompt,
        string userMessage,
        IReadOnlyList<ChatHistoryTurn>? history = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(decryptedApiKey) || string.IsNullOrWhiteSpace(userMessage))
        {
            return ChatProviderResult.Fail();
        }

        try
        {
            // OpenAI 는 system 을 별도 필드가 아니라 messages 배열 첫 원소로 넣는다(클로드와 다른 점).
            var messages = new List<OpenAiMessage>();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                messages.Add(new OpenAiMessage { Role = "system", Content = systemPrompt });
            }

            if (history is { Count: > 0 })
            {
                foreach (var turn in history)
                {
                    var role = string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                        ? "assistant" : "user";
                    if (!string.IsNullOrWhiteSpace(turn.Content))
                    {
                        messages.Add(new OpenAiMessage { Role = role, Content = turn.Content });
                    }
                }
            }
            messages.Add(new OpenAiMessage { Role = "user", Content = userMessage });

            var payload = new OpenAiRequest
            {
                Model = _model,
                MaxCompletionTokens = _maxTokens,
                Messages = messages.ToArray()
            };

            var json = JsonSerializer.Serialize(payload, JsonOpts);

            using var req = new HttpRequestMessage(HttpMethod.Post, CompletionsUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            // 복호화 키는 Authorization 헤더로만. 본문에 실리지 않는다.
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", decryptedApiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var http = _httpFactory.CreateClient();
            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                // 키 무효(401)·과금(429)·요청형식(400)·서버오류(5xx) 등.
                // 키는 헤더라 본문에 없으나, 응답 echo 대비 500자 truncate 후 기록(기존 클로드 어댑터와 동일 정책).
                _logger.LogWarning(
                    "챗GPT 응답 실패: status={Status} body={Body}. KB-only 폴백으로 전환합니다.",
                    (int)resp.StatusCode, Truncate(body));
                // 상태코드를 함께 넘긴다 — 호출부가 "키 거부(401/403)" 와 "일시 장애" 를 구분해야 한다.
                return ChatProviderResult.Fail((int)resp.StatusCode);
            }

            var parsed = JsonSerializer.Deserialize<OpenAiResponse>(body, JsonOpts);
            if (parsed is null)
            {
                _logger.LogWarning("챗GPT 응답 파싱 실패(null). KB-only 폴백으로 전환합니다.");
                return ChatProviderResult.Fail();
            }

            var text = parsed.Choices is { Length: > 0 }
                ? parsed.Choices[0].Message?.Content ?? ""
                : "";

            if (string.IsNullOrWhiteSpace(text))
            {
                var finish = parsed.Choices is { Length: > 0 } ? parsed.Choices[0].FinishReason : null;
                _logger.LogWarning(
                    "챗GPT 응답에 텍스트 본문이 없습니다(finish_reason={Finish}). KB-only 폴백으로 전환합니다.",
                    finish);
                return ChatProviderResult.Fail();
            }

            return new ChatProviderResult
            {
                Succeeded = true,
                Answer = text.Trim(),
                InputTokens = parsed.Usage?.PromptTokens ?? 0,
                OutputTokens = parsed.Usage?.CompletionTokens ?? 0,
                Model = string.IsNullOrWhiteSpace(parsed.Model) ? _model : parsed.Model!
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("챗GPT 호출이 취소되었습니다. KB-only 폴백으로 전환합니다.");
            return ChatProviderResult.Fail();
        }
        catch (Exception ex)
        {
            // 헌법 #15: 빈 catch 금지.
            _logger.LogWarning(ex, "챗GPT 호출 중 예외. KB-only 폴백으로 전환합니다.");
            return ChatProviderResult.Fail();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Tool Use — 🔴 미지원. 성공을 가장하지 않는다.
    //
    // 왜 미구현인가 (2026-08-12 작업지시서 20260812작1 범위):
    //   이번 오더는 "API 연동에 대한 설정"이고, 사장님이 "챗봇은 나중에 할거야" 로 못박으셨다.
    //   ToolUseResponse 는 RawAssistantContent 로 클로드 고유 content 블록 원본을 그대로 돌려주는
    //   계약이고, AiAgentService(엔진)가 그 포맷에 직접 의존한다. 여기서 OpenAI 응답을 억지로
    //   그 포맷으로 흉내내면 엔진 동작(=챗봇)을 건드리게 되어 이번 범위를 벗어난다.
    //
    // 🔴 그래서 "되는 척" 대신 Fail 을 반환한다.
    //   오늘(2026-08-12) 본사 알림메일이 실제로는 안 보내면서 성공(true)을 반환하는 것을
    //   P0 로 적발했다. 같은 함정을 여기서 내가 만들지 않는다.
    //   호출부(ChatbotService)는 Fail 을 받으면 기존 KB/도우미 폴백으로 정상 처리한다.
    //
    // 다음 단계(챗봇 차수)에서 할 일:
    //   공급자 중립 Tool Use 추상화를 만들고 각 사 function calling 포맷으로 변환.
    // ─────────────────────────────────────────────────────────────
    public Task<ToolUseResponse> CompleteWithToolsAsync(
        string decryptedApiKey,
        string systemPrompt,
        IReadOnlyList<ToolUseMessage> messages,
        JsonElement tools,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "챗GPT 는 현재 도구 사용(Tool Use)을 지원하지 않습니다. 일반 응답 경로로 폴백합니다.");
        return Task.FromResult(ToolUseResponse.Fail());
    }

    private static string Truncate(string? s, int max = 500)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…(생략)");

    // ── OpenAI 와이어 포맷 DTO (SDK 미사용 — 기존 클로드 어댑터와 동일 방침) ──

    private sealed class OpenAiRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        // 최신 모델은 max_completion_tokens 를 사용한다(구 max_tokens 는 레거시).
        [JsonPropertyName("max_completion_tokens")] public int MaxCompletionTokens { get; set; }
        [JsonPropertyName("messages")] public OpenAiMessage[] Messages { get; set; } = Array.Empty<OpenAiMessage>();
    }

    private sealed class OpenAiMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "user";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private sealed class OpenAiResponse
    {
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("choices")] public OpenAiChoice[]? Choices { get; set; }
        [JsonPropertyName("usage")] public OpenAiUsage? Usage { get; set; }
    }

    private sealed class OpenAiChoice
    {
        [JsonPropertyName("message")] public OpenAiMessage? Message { get; set; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    }

    private sealed class OpenAiUsage
    {
        [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
        [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
    }
}
