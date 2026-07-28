using HitPan.Application.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HitPan.API.AI;

/// <summary>
/// 히트판 도우미 정체성 프롬프트(System Prompt) 로더.
///
/// 신규(2026-06-19): HITPAN_CHATBOT_IDENTITY.md 를 앱 시작 시 1회 읽어 캐싱한다.
///  - 매 요청마다 디스크 read 금지(작업지시 명세).
///  - 파일이 없거나 읽기 실패 시 빈 문자열 — 외부 도우미 호출 시 system 미지정으로 동작(헌법 #15 LogWarning).
///  - 헌법 #23: 파일 내용 자체가 'AI/Claude/바이브코딩' 단어 노출을 차단하는 정체성 규칙을 포함.
/// </summary>
public interface IChatbotIdentityProvider
{
    /// <summary>캐싱된 System Prompt 본문(없으면 빈 문자열).</summary>
    string SystemPrompt { get; }
}

public sealed class ChatbotIdentityProvider : IChatbotIdentityProvider, IChatbotSystemPrompt
{
    public string SystemPrompt { get; }

    /// <summary>IChatbotSystemPrompt 경계 — Infrastructure 의 ChatbotService 가 주입받는다.</summary>
    public string Value => SystemPrompt;

    public ChatbotIdentityProvider(IHostEnvironment env, ILogger<ChatbotIdentityProvider> logger)
    {
        SystemPrompt = LoadOnce(env, logger);
    }

    private static string LoadOnce(IHostEnvironment env, ILogger logger)
    {
        // 후보 경로: ① 콘텐츠 루트(개발: src/HitPan.API/AI) ② 실행 디렉터리(배포: copy 규칙)
        var candidates = new[]
        {
            Path.Combine(env.ContentRootPath, "AI", "HITPAN_CHATBOT_IDENTITY.md"),
            Path.Combine(AppContext.BaseDirectory, "AI", "HITPAN_CHATBOT_IDENTITY.md")
        };

        foreach (var path in candidates)
        {
            try
            {
                if (File.Exists(path))
                {
                    var text = File.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        logger.LogInformation("히트판 도우미 정체성 프롬프트 로드 완료: {Path} ({Len}자)", path, text.Length);
                        return text;
                    }
                }
            }
            catch (Exception ex)
            {
                // 헌법 #15: 빈 catch 금지. 한 후보 실패는 다음 후보 시도, 전체 실패는 아래에서 경고.
                logger.LogWarning(ex, "정체성 프롬프트 읽기 실패(후보 계속): {Path}", path);
            }
        }

        logger.LogWarning(
            "히트판 도우미 정체성 프롬프트 파일을 찾지 못했습니다. system 프롬프트 없이 외부 도우미를 호출합니다.");
        return string.Empty;
    }
}
