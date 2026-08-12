using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

// ─────────────────────────────────────────────────────────────────
// 히트판 도우미 — 외부 모델 직통 호출 어댑터 (BYOK)
//
// 신규(2026-06-19): 챗봇 ask 흐름에서 KB 매칭이 없을 때 호출.
//  - 고객 PC → Anthropic Messages API 직통 (헌법 #18·#22: 본사 프록시 0, 업무 데이터 본사 전송 0).
//  - 복호화된 BYOK 키는 호출 직전에만 메모리 보유, 로그·DB 저장 금지(헌법 #5).
//  - System Prompt = HITPAN_CHATBOT_IDENTITY.md (앱 시작 1회 캐싱, 매 요청 디스크 read 금지).
//  - 빈 catch 금지(헌법 #15): 모든 실패는 LogWarning 후 ChatProviderResult.Failed 반환 → 호출부가 KB-only 폴백.
// ─────────────────────────────────────────────────────────────────

/// <summary>외부 도우미 모델 호출 결과. 성공 시 답변+토큰, 실패 시 Succeeded=false.</summary>
public sealed class ChatProviderResult
{
    public bool Succeeded { get; init; }
    public string Answer { get; init; } = "";
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public string Model { get; init; } = "";

    /// <summary>
    /// 실패했을 때의 HTTP 상태코드(응답을 받은 경우). 네트워크 장애·취소·파싱실패면 null.
    /// 🔴 추가 2026-08-12 (검증팀 P1-2): "키가 틀렸다" 와 "지금 인터넷이 안 된다" 는 다른 사실이다.
    ///    이 둘을 구분 못 하면, 잠깐 끊긴 사이 [연결 확인] 을 누른 고객의 멀쩡한 키를
    ///    invalid 로 적어 버려 도우미가 죽는다(고객은 키를 다시 넣어야 살아난다).
    /// </summary>
    public int? StatusCode { get; init; }

    /// <summary>
    /// 키 자체가 거부된 경우. 이때만 저장된 키를 invalid 로 내려도 된다.
    /// 400 을 포함하는 이유: 제미나이는 잘못된 키에 <b>400</b> 을 준다(2026-08-12 실측).
    /// 401/403 만 보면 제미나이의 틀린 키를 "일시 장애" 로 오판한다.
    /// 5xx·429·타임아웃은 여기 포함하지 않는다 — 키 문제가 아니기 때문.
    /// </summary>
    public bool IsAuthFailure => StatusCode is 400 or 401 or 403;

    public static ChatProviderResult Fail() => new() { Succeeded = false };

    /// <summary>HTTP 응답을 받고 실패한 경우 — 상태코드를 함께 남긴다.</summary>
    public static ChatProviderResult Fail(int statusCode) => new() { Succeeded = false, StatusCode = statusCode };
}

/// <summary>
/// 히트판 도우미 System Prompt 공급자. 구현체(API 계층)가 정체성 .md 를 1회 로드해 캐싱한다.
/// Infrastructure 의 ChatbotService 가 API 프로젝트를 참조하지 않고도 프롬프트를 주입받기 위한 경계.
/// </summary>
public interface IChatbotSystemPrompt
{
    /// <summary>캐싱된 System Prompt(없으면 빈 문자열).</summary>
    string Value { get; }
}

/// <summary>대화 1턴 (단기 기억 — 클로드 messages 에 함께 전송).</summary>
public sealed class ChatHistoryTurn
{
    /// <summary>"user" 또는 "assistant"</summary>
    public string Role { get; init; } = "user";
    public string Content { get; init; } = "";
}

/// <summary>외부 도우미 모델 직통 호출 추상화. 구현체는 Anthropic Messages API.</summary>
public interface IChatCompletionProvider
{
    /// <summary>
    /// 복호화된 키로 자연어 응답 호출. 시스템 프롬프트는 정체성 파일에서 로드한 값을 사용한다.
    /// history(직전 대화)를 함께 보내 맥락을 기억한다(사장님 결재 2026-06-20).
    /// 실패(키 무효·네트워크·HTTP 오류 등) 시 예외 대신 <see cref="ChatProviderResult.Fail"/> 반환.
    /// </summary>
    Task<ChatProviderResult> CompleteAsync(
        string decryptedApiKey,
        string systemPrompt,
        string userMessage,
        IReadOnlyList<ChatHistoryTurn>? history = null,
        CancellationToken ct = default);

    /// <summary>
    /// Tool Use 단일 왕복 호출 (사장님 비전 2026-06-20 — 클로드가 도구 스스로 선택).
    /// messages 는 이미 구성된 대화(이전 tool_result 포함 가능), tools 는 Tool 카탈로그(JSON).
    /// 멀티턴 루프(tool_use→실행→tool_result→재호출)는 호출부(엔진)가 돈다.
    /// 실패 시 Succeeded=false 반환(예외 대신).
    /// </summary>
    Task<ToolUseResponse> CompleteWithToolsAsync(
        string decryptedApiKey,
        string systemPrompt,
        IReadOnlyList<ToolUseMessage> messages,
        JsonElement tools,
        CancellationToken ct = default);
}

/// <summary>Tool Use 대화 메시지 1개 (content 는 구조화 블록 배열의 raw JSON).</summary>
public sealed class ToolUseMessage
{
    public string Role { get; init; } = "user";

    /// <summary>content 블록 배열의 JSON(text / tool_use / tool_result). 단순 텍스트면 문자열도 허용.</summary>
    public JsonElement Content { get; init; }
}

/// <summary>Tool Use 응답 — text 답변 + 클로드가 호출하려는 tool_use 블록들.</summary>
public sealed class ToolUseResponse
{
    public bool Succeeded { get; init; }

    /// <summary>stop_reason ("end_turn" / "tool_use" 등).</summary>
    public string StopReason { get; init; } = "";

    /// <summary>text 블록 합성(있으면).</summary>
    public string Text { get; init; } = "";

    /// <summary>클로드가 호출하려는 tool_use 블록들(있으면 엔진이 실행).</summary>
    public IReadOnlyList<ToolUseCall> ToolCalls { get; init; } = new List<ToolUseCall>();

    /// <summary>이번 왕복의 assistant content 블록 원본 JSON(다음 턴 messages 에 그대로 재전송).</summary>
    public JsonElement RawAssistantContent { get; init; }

    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }

    public static ToolUseResponse Fail() => new() { Succeeded = false };
}

/// <summary>클로드가 호출하려는 Tool 1건.</summary>
public sealed class ToolUseCall
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public JsonElement Input { get; init; }
}

/// <summary>
/// Anthropic Messages API 어댑터.
/// POST https://api.anthropic.com/v1/messages
///   headers: x-api-key, anthropic-version: 2023-06-01
///   body: { model, max_tokens, system, messages:[{role,content}] }
/// </summary>
public sealed class AnthropicChatProvider : IChatCompletionProvider
{
    // 모델은 설정 가능. 기본은 현재 권장 모델(claude-opus-4-8). 비용 절약 시 설정으로 sonnet 승격.
    private const string DefaultModel = "claude-opus-4-8";
    private const int DefaultMaxTokens = 1024;
    private const string AnthropicVersion = "2023-06-01";
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AnthropicChatProvider> _logger;
    private readonly string _model;
    private readonly int _maxTokens;

    public AnthropicChatProvider(
        IHttpClientFactory httpFactory,
        ILogger<AnthropicChatProvider> logger,
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
            // 키 또는 입력 없음 → 폴백(KB-only). 호출부가 처리.
            return ChatProviderResult.Fail();
        }

        try
        {
            // 직전 대화(history) + 이번 user 메시지를 함께 전송 → 맥락 기억(사장님 결재 2026-06-20).
            // role 은 user/assistant 만 허용, 연속 동일 role 방지를 위해 정제 후 추가.
            var messages = new List<AnthropicMessage>();
            if (history is { Count: > 0 })
            {
                foreach (var turn in history)
                {
                    var role = string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                        ? "assistant" : "user";
                    if (!string.IsNullOrWhiteSpace(turn.Content))
                    {
                        messages.Add(new AnthropicMessage { Role = role, Content = turn.Content });
                    }
                }
            }
            messages.Add(new AnthropicMessage { Role = "user", Content = userMessage });

            var payload = new AnthropicRequest
            {
                Model = _model,
                MaxTokens = _maxTokens,
                System = string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt,
                Messages = messages.ToArray()
            };

            var json = JsonSerializer.Serialize(payload, JsonOpts);

            using var req = new HttpRequestMessage(HttpMethod.Post, MessagesUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            // x-api-key 는 복호화 키. anthropic-version 필수.
            req.Headers.TryAddWithoutValidation("x-api-key", decryptedApiKey);
            req.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var http = _httpFactory.CreateClient();
            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                // 키 무효(401/403)·과금(429)·요청형식(400)·서버오류(5xx) 등. 본문에 평문 키 없음(헤더에만).
                // 봉합 (2026-06-23, 6차 PERM 후속 P2): 응답 본문을 앞 500자로 truncate 후 기록(보안 매니저 권고).
                //   키는 헤더라 안전하나 에러 응답에 요청 echo 가 섞일 가능성 대비 — 진단성은 유지하되 노출면 축소.
                _logger.LogWarning(
                    "외부 도우미 응답 실패: status={Status} body={Body}. KB-only 폴백으로 전환합니다.",
                    (int)resp.StatusCode, Truncate(body));
                // 상태코드를 함께 넘긴다 — 호출부가 "키 거부(401/403)" 와 "일시 장애" 를 구분해야 한다.
                return ChatProviderResult.Fail((int)resp.StatusCode);
            }

            var parsed = JsonSerializer.Deserialize<AnthropicResponse>(body, JsonOpts);
            if (parsed is null)
            {
                _logger.LogWarning("외부 도우미 응답 파싱 실패(null). KB-only 폴백으로 전환합니다.");
                return ChatProviderResult.Fail();
            }

            // content[] 중 type=="text" 블록을 합성. refusal·기타 stop_reason 도 텍스트 없으면 폴백.
            var text = ExtractText(parsed);
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning(
                    "외부 도우미 응답에 텍스트 본문이 없습니다(stop_reason={Stop}). KB-only 폴백으로 전환합니다.",
                    parsed.StopReason);
                return ChatProviderResult.Fail();
            }

            return new ChatProviderResult
            {
                Succeeded = true,
                Answer = text.Trim(),
                InputTokens = parsed.Usage?.InputTokens ?? 0,
                OutputTokens = parsed.Usage?.OutputTokens ?? 0,
                Model = string.IsNullOrWhiteSpace(parsed.Model) ? _model : parsed.Model!
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 요청 취소는 정상 흐름 — 폴백 처리하되 경고 로그는 남김(추적용).
            _logger.LogWarning("외부 도우미 호출이 취소되었습니다. KB-only 폴백으로 전환합니다.");
            return ChatProviderResult.Fail();
        }
        catch (Exception ex)
        {
            // 헌법 #15: 빈 catch 금지. 네트워크·역직렬화 등 모든 예외는 폴백 + 경고 로그.
            _logger.LogWarning(ex, "외부 도우미 호출 중 예외. KB-only 폴백으로 전환합니다.");
            return ChatProviderResult.Fail();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Tool Use 단일 왕복 (사장님 비전 2026-06-20 — 클로드가 도구 스스로 선택)
    // ─────────────────────────────────────────────────────────────
    public async Task<ToolUseResponse> CompleteWithToolsAsync(
        string decryptedApiKey,
        string systemPrompt,
        IReadOnlyList<ToolUseMessage> messages,
        JsonElement tools,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(decryptedApiKey) || messages.Count == 0)
        {
            return ToolUseResponse.Fail();
        }

        try
        {
            // 와이어 바디를 익명 객체로 직접 구성(content 가 구조화 블록이라 DTO 대신 JsonElement 그대로 실음).
            var msgArray = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray();
            var body = new Dictionary<string, object?>
            {
                ["model"] = _model,
                ["max_tokens"] = _maxTokens,
                ["messages"] = msgArray,
                ["tools"] = tools
            };
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                body["system"] = systemPrompt;
            }

            var json = JsonSerializer.Serialize(body, JsonOpts);

            using var req = new HttpRequestMessage(HttpMethod.Post, MessagesUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("x-api-key", decryptedApiKey);
            req.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var http = _httpFactory.CreateClient();
            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                // 봉합 (2026-06-23, 6차 PERM 후속 P2): 응답 본문 truncate 후 기록(보안 매니저 권고).
                _logger.LogWarning("AI 직원(Tool Use) 응답 실패: status={Status} body={Body}",
                    (int)resp.StatusCode, Truncate(respBody));
                return ToolUseResponse.Fail();
            }

            using var doc = JsonDocument.Parse(respBody);
            var root = doc.RootElement;

            var stopReason = root.TryGetProperty("stop_reason", out var sr) ? sr.GetString() ?? "" : "";
            var sb = new StringBuilder();
            var calls = new List<ToolUseCall>();

            if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    var type = block.TryGetProperty("type", out var t) ? t.GetString() : null;
                    if (type == "text" && block.TryGetProperty("text", out var txt))
                    {
                        sb.Append(txt.GetString());
                    }
                    else if (type == "tool_use")
                    {
                        calls.Add(new ToolUseCall
                        {
                            Id = block.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                            Name = block.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "",
                            Input = block.TryGetProperty("input", out var inp) ? inp.Clone() : default
                        });
                    }
                }
            }

            var usage = root.TryGetProperty("usage", out var u) ? u : default;
            return new ToolUseResponse
            {
                Succeeded = true,
                StopReason = stopReason,
                Text = sb.ToString().Trim(),
                ToolCalls = calls,
                RawAssistantContent = content.ValueKind == JsonValueKind.Array ? content.Clone() : default,
                InputTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0,
                OutputTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("AI 직원(Tool Use) 호출 취소.");
            return ToolUseResponse.Fail();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI 직원(Tool Use) 호출 중 예외.");
            return ToolUseResponse.Fail();
        }
    }

    // 봉합 (2026-06-23, 6차 PERM 후속 P2): Anthropic 에러 응답 본문 로깅 노출면 축소(앞 500자).
    //   키는 헤더라 본문엔 없으나, 진단성은 유지하되 보수적으로 자른다(보안 매니저 권고).
    private static string Truncate(string? s, int max = 500)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "…(truncated)");

    private static string ExtractText(AnthropicResponse parsed)
    {
        if (parsed.Content is null || parsed.Content.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var block in parsed.Content)
        {
            if (string.Equals(block.Type, "text", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(block.Text))
            {
                sb.Append(block.Text);
            }
        }
        return sb.ToString();
    }

    // ── 요청/응답 DTO (Anthropic Messages API 와이어 포맷) ──

    private sealed class AnthropicRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("system")] public string? System { get; set; }
        [JsonPropertyName("messages")] public AnthropicMessage[] Messages { get; set; } = Array.Empty<AnthropicMessage>();
    }

    private sealed class AnthropicMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private sealed class AnthropicResponse
    {
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("stop_reason")] public string? StopReason { get; set; }
        [JsonPropertyName("content")] public List<AnthropicContentBlock>? Content { get; set; }
        [JsonPropertyName("usage")] public AnthropicUsage? Usage { get; set; }
    }

    private sealed class AnthropicContentBlock
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
    }

    private sealed class AnthropicUsage
    {
        [JsonPropertyName("input_tokens")] public int InputTokens { get; set; }
        [JsonPropertyName("output_tokens")] public int OutputTokens { get; set; }
    }
}
