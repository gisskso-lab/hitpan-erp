using HitPan.Application.Services.Ai;

namespace HitPan.API.AI;

// ─────────────────────────────────────────────────────────────────
// AI 직원 System Prompt 공급자 — HITPAN_AI_AGENT.md 를 앱 시작 시 1회 로드(캐싱).
//   사장님 방침(페르소나 프롬프팅): 학습이 아니라 .md 정체성 프롬프트를 매 호출 주입.
//   Singleton — 매 요청 디스크 read 금지.
// ─────────────────────────────────────────────────────────────────
public sealed class AiAgentSystemPromptProvider : IAiAgentSystemPrompt
{
    public string Value { get; }

    public AiAgentSystemPromptProvider(IHostEnvironment env, ILogger<AiAgentSystemPromptProvider> logger)
    {
        Value = LoadOnce(env, logger);
    }

    private static string LoadOnce(IHostEnvironment env, ILogger logger)
    {
        var candidates = new[]
        {
            Path.Combine(env.ContentRootPath, "AI", "HITPAN_AI_AGENT.md"),
            Path.Combine(AppContext.BaseDirectory, "AI", "HITPAN_AI_AGENT.md")
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
                        logger.LogInformation("AI 직원 정체성 프롬프트 로드 완료: {Path} ({Len}자)", path, text.Length);
                        return text;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AI 직원 정체성 프롬프트 읽기 실패(후보 계속): {Path}", path);
            }
        }

        logger.LogWarning("AI 직원 정체성 프롬프트 파일을 찾지 못했습니다. 기본 지침 없이 동작합니다.");
        return "당신은 히트판 ERP의 AI 직원입니다. 제공된 도구로 사용자 명령을 처리하되, "
             + "데이터를 바꾸는 작업은 초안만 만들고 사람 승인을 받으세요.";
    }
}
