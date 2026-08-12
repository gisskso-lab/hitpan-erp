using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services.Ai;

// ─────────────────────────────────────────────────────────────────
// AI 공급자 선택 팩토리 (2026-08-12, 작업지시서 20260812작1 · 사장님 결재)
//
// 사장님 오더: "기존 : 클로드API만 지원 -> 수정 : 클로드, 챗지피티, 제미나이API까지 받을 수 있게"
// 결재된 설계: 3사 키를 각각 저장해 두고, 지금 쓸 곳 하나를 고른다.
//              (한 곳이 장애·한도 초과일 때 키를 다시 넣지 않고 갈아탄다)
//
// 🔴 왜 팩토리가 필요한가:
//   종전 Program.cs 는 IChatCompletionProvider 를 AddSingleton 으로 등록했다.
//   = 앱이 시작될 때 공급자가 하나로 굳는다. 고객이 화면에서 공급자를 바꿔도 반영될 자리가 없다.
//   그렇다고 Singleton 등록을 지우면 기존 코드 경로가 깨진다(헌법 #1).
//   → 기존 등록은 그대로 두고, 이름으로 골라 꺼내는 팩토리를 옆에 추가한다.
//
// 🔴 무회귀 원칙:
//   Resolve 가 모르는 값·빈 값을 받으면 반드시 클로드AI(anthropic)로 떨어진다.
//   기존 고객은 ai_provider 기본값이 'anthropic' 이므로 종전과 완전히 같게 동작한다.
// ─────────────────────────────────────────────────────────────────

/// <summary>공급자 식별자 상수 — DB ai_provider 컬럼 값과 1:1.</summary>
public static class AiProviderIds
{
    /// <summary>클로드AI (Anthropic) — 1차 표준·기본값.</summary>
    public const string Anthropic = "anthropic";

    /// <summary>챗GPT (OpenAI).</summary>
    public const string OpenAi = "openai";

    /// <summary>제미나이 (Google).</summary>
    public const string Google = "google";

    /// <summary>고객 화면에 보여줄 이름 (사장님 결재 2026-08-12 — 이 문구 그대로 쓴다).</summary>
    public static string DisplayName(string? providerId) => Normalize(providerId) switch
    {
        OpenAi => "챗GPT",
        Google => "제미나이",
        _ => "클로드AI"
    };

    /// <summary>알 수 없는 값·빈 값은 전부 기본값(클로드AI)으로 정규화한다.</summary>
    public static string Normalize(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return Anthropic;

        return providerId.Trim().ToLowerInvariant() switch
        {
            OpenAi => OpenAi,
            Google => Google,
            Anthropic => Anthropic,
            _ => Anthropic
        };
    }

    /// <summary>지원하는 공급자 전체 (화면 탭 순서 = 이 순서).</summary>
    public static IReadOnlyList<string> All { get; } = new[] { Anthropic, OpenAi, Google };
}

/// <summary>공급자 식별자로 실제 호출 어댑터를 고른다.</summary>
public interface IAiProviderFactory
{
    /// <summary>
    /// providerId 에 해당하는 어댑터를 돌려준다.
    /// 모르는 값이면 클로드AI(기본값)로 떨어진다 — 예외를 던지지 않는다.
    /// </summary>
    IChatCompletionProvider Resolve(string? providerId);
}

/// <summary>
/// 3사 어댑터를 미리 받아 두고 이름으로 골라 주는 팩토리.
/// 어댑터 자체는 상태가 없어(HttpClientFactory 사용) Singleton 으로 안전하다.
/// </summary>
public sealed class AiProviderFactory : IAiProviderFactory
{
    private readonly AnthropicChatProvider _anthropic;
    private readonly OpenAiChatProvider _openAi;
    private readonly GeminiChatProvider _gemini;
    private readonly ILogger<AiProviderFactory> _logger;

    public AiProviderFactory(
        AnthropicChatProvider anthropic,
        OpenAiChatProvider openAi,
        GeminiChatProvider gemini,
        ILogger<AiProviderFactory> logger)
    {
        _anthropic = anthropic;
        _openAi = openAi;
        _gemini = gemini;
        _logger = logger;
    }

    public IChatCompletionProvider Resolve(string? providerId)
    {
        var normalized = AiProviderIds.Normalize(providerId);

        // 정규화 과정에서 값이 바뀌었다면 = 모르는 값이 들어온 것. 조용히 넘기지 않는다(헌법 #15 정신).
        if (!string.IsNullOrWhiteSpace(providerId) &&
            !string.Equals(providerId.Trim(), normalized, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "알 수 없는 AI 공급자 값({Given})이라 기본값(클로드AI)으로 처리합니다.", providerId);
        }

        return normalized switch
        {
            AiProviderIds.OpenAi => _openAi,
            AiProviderIds.Google => _gemini,
            _ => _anthropic
        };
    }
}
