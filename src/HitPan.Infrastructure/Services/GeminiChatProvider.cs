using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

// ─────────────────────────────────────────────────────────────────
// 히트판 도우미 — 제미나이(Google) 직통 호출 어댑터 (BYOK)
//
// 신규(2026-08-12, 작업지시서 20260812작1 · 사장님 결재):
//   "기존 : 클로드API만 지원 -> 수정 : 클로드, 챗지피티, 제미나이API까지 받을 수 있게"
//
//  - 고객 PC → Google Generative Language API 직통 (헌법 #18·#22).
//  - 복호화된 BYOK 키는 호출 직전에만 메모리 보유, 로그·DB 저장 금지(헌법 #5).
//  - 빈 catch 금지(헌법 #15).
//  - 기존 AnthropicChatProvider 는 한 줄도 수정하지 않는다(헌법 #1). 이 파일은 순수 추가다.
//
// 🔴 제미나이는 앞의 두 곳과 형식이 크게 다르다 — 옮겨 적을 때 주의:
//    · role 은 "user" / "model" (assistant 가 아니다)
//    · 본문은 messages 가 아니라 contents[].parts[].text
//    · system 은 systemInstruction 이라는 별도 최상위 필드
//    · 키는 헤더 x-goog-api-key (URL 쿼리에 넣으면 로그·프록시에 남는다 — 헌법 #5)
// ─────────────────────────────────────────────────────────────────

/// <summary>
/// Google Generative Language API 어댑터.
/// POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent
///   headers: x-goog-api-key: {key}
///   body: { systemInstruction, contents:[{role,parts:[{text}]}], generationConfig }
/// </summary>
public sealed class GeminiChatProvider : IChatCompletionProvider
{
    private const string DefaultModel = "gemini-2.5-flash";
    private const int DefaultMaxTokens = 1024;
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<GeminiChatProvider> _logger;
    private readonly string _model;
    private readonly int _maxTokens;

    public GeminiChatProvider(
        IHttpClientFactory httpFactory,
        ILogger<GeminiChatProvider> logger,
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
            var contents = new List<GeminiContent>();
            if (history is { Count: > 0 })
            {
                foreach (var turn in history)
                {
                    // 제미나이는 assistant 를 "model" 이라 부른다. 그대로 보내면 400 이 난다.
                    var role = string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                        ? "model" : "user";
                    if (!string.IsNullOrWhiteSpace(turn.Content))
                    {
                        contents.Add(new GeminiContent
                        {
                            Role = role,
                            Parts = new[] { new GeminiPart { Text = turn.Content } }
                        });
                    }
                }
            }
            contents.Add(new GeminiContent
            {
                Role = "user",
                Parts = new[] { new GeminiPart { Text = userMessage } }
            });

            var payload = new GeminiRequest
            {
                SystemInstruction = string.IsNullOrWhiteSpace(systemPrompt)
                    ? null
                    : new GeminiContent { Parts = new[] { new GeminiPart { Text = systemPrompt } } },
                Contents = contents.ToArray(),
                GenerationConfig = new GeminiGenerationConfig { MaxOutputTokens = _maxTokens }
            };

            var json = JsonSerializer.Serialize(payload, JsonOpts);

            var url = $"{BaseUrl}/{Uri.EscapeDataString(_model)}:generateContent";
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            // 🔴 키는 반드시 헤더로. URL 쿼리(?key=)에 실으면 접속 로그·프록시에 평문으로 남는다(헌법 #5).
            req.Headers.TryAddWithoutValidation("x-goog-api-key", decryptedApiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var http = _httpFactory.CreateClient();
            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "제미나이 응답 실패: status={Status} body={Body}. KB-only 폴백으로 전환합니다.",
                    (int)resp.StatusCode, Truncate(body));
                // 상태코드를 함께 넘긴다 — 호출부가 "키 거부(401/403)" 와 "일시 장애" 를 구분해야 한다.
                //   ⚠️ 제미나이는 잘못된 키에 400 을 주기도 한다(401 아님). 아래 호출부에서 함께 다룬다.
                return ChatProviderResult.Fail((int)resp.StatusCode);
            }

            var parsed = JsonSerializer.Deserialize<GeminiResponse>(body, JsonOpts);
            if (parsed is null)
            {
                _logger.LogWarning("제미나이 응답 파싱 실패(null). KB-only 폴백으로 전환합니다.");
                return ChatProviderResult.Fail();
            }

            var text = ExtractText(parsed);
            if (string.IsNullOrWhiteSpace(text))
            {
                var finish = parsed.Candidates is { Length: > 0 } ? parsed.Candidates[0].FinishReason : null;
                _logger.LogWarning(
                    "제미나이 응답에 텍스트 본문이 없습니다(finishReason={Finish}). KB-only 폴백으로 전환합니다.",
                    finish);
                return ChatProviderResult.Fail();
            }

            return new ChatProviderResult
            {
                Succeeded = true,
                Answer = text.Trim(),
                InputTokens = parsed.UsageMetadata?.PromptTokenCount ?? 0,
                OutputTokens = parsed.UsageMetadata?.CandidatesTokenCount ?? 0,
                Model = string.IsNullOrWhiteSpace(parsed.ModelVersion) ? _model : parsed.ModelVersion!
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("제미나이 호출이 취소되었습니다. KB-only 폴백으로 전환합니다.");
            return ChatProviderResult.Fail();
        }
        catch (Exception ex)
        {
            // 헌법 #15: 빈 catch 금지.
            _logger.LogWarning(ex, "제미나이 호출 중 예외. KB-only 폴백으로 전환합니다.");
            return ChatProviderResult.Fail();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Tool Use — 🔴 미지원. 성공을 가장하지 않는다.
    //   사유는 OpenAiChatProvider.CompleteWithToolsAsync 주석과 동일:
    //   이번 오더는 "API 연동 설정"이고 사장님이 "챗봇은 나중에" 로 못박으셨다.
    //   되는 척하는 대신 Fail 을 반환해 호출부가 기존 폴백으로 정상 처리하게 한다.
    // ─────────────────────────────────────────────────────────────
    public Task<ToolUseResponse> CompleteWithToolsAsync(
        string decryptedApiKey,
        string systemPrompt,
        IReadOnlyList<ToolUseMessage> messages,
        JsonElement tools,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "제미나이는 현재 도구 사용(Tool Use)을 지원하지 않습니다. 일반 응답 경로로 폴백합니다.");
        return Task.FromResult(ToolUseResponse.Fail());
    }

    /// <summary>candidates[0].content.parts[] 의 text 를 이어붙인다.</summary>
    private static string ExtractText(GeminiResponse parsed)
    {
        if (parsed.Candidates is not { Length: > 0 }) return "";
        var parts = parsed.Candidates[0].Content?.Parts;
        if (parts is null || parts.Length == 0) return "";

        var sb = new StringBuilder();
        foreach (var p in parts)
        {
            if (!string.IsNullOrEmpty(p.Text)) sb.Append(p.Text);
        }
        return sb.ToString();
    }

    private static string Truncate(string? s, int max = 500)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…(생략)");

    // ── 제미나이 와이어 포맷 DTO (SDK 미사용 — 기존 방침 동일) ──

    private sealed class GeminiRequest
    {
        [JsonPropertyName("systemInstruction")] public GeminiContent? SystemInstruction { get; set; }
        [JsonPropertyName("contents")] public GeminiContent[] Contents { get; set; } = Array.Empty<GeminiContent>();
        [JsonPropertyName("generationConfig")] public GeminiGenerationConfig? GenerationConfig { get; set; }
    }

    private sealed class GeminiContent
    {
        // systemInstruction 에는 role 을 넣지 않는다 → null 이면 직렬화에서 빠진다.
        [JsonPropertyName("role")] public string? Role { get; set; }
        [JsonPropertyName("parts")] public GeminiPart[] Parts { get; set; } = Array.Empty<GeminiPart>();
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")] public string Text { get; set; } = "";
    }

    private sealed class GeminiGenerationConfig
    {
        [JsonPropertyName("maxOutputTokens")] public int MaxOutputTokens { get; set; }
    }

    private sealed class GeminiResponse
    {
        [JsonPropertyName("candidates")] public GeminiCandidate[]? Candidates { get; set; }
        [JsonPropertyName("usageMetadata")] public GeminiUsage? UsageMetadata { get; set; }
        [JsonPropertyName("modelVersion")] public string? ModelVersion { get; set; }
    }

    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")] public GeminiContent? Content { get; set; }
        [JsonPropertyName("finishReason")] public string? FinishReason { get; set; }
    }

    private sealed class GeminiUsage
    {
        [JsonPropertyName("promptTokenCount")] public int PromptTokenCount { get; set; }
        [JsonPropertyName("candidatesTokenCount")] public int CandidatesTokenCount { get; set; }
    }
}
