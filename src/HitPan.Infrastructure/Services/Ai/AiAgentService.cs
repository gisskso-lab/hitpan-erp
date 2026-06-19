using System.Text;
using System.Text.Json;
using HitPan.Application.DTOs.Chatbot;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services.Ai;

// ─────────────────────────────────────────────────────────────────
// AI 직원 엔진 — Tool Use 멀티턴 루프 (뼈대의 심장)
//
// 사장님 비전 (2026-06-20): "히트판의 모든 기능을 챗봇이 할 줄 알아야 그게 기본 뼈대".
//   클로드에게 Tool 카탈로그를 주고, 명령을 보고 스스로 도구를 골라 호출하게 한다.
//   클로드가 tool_use 를 내면 우리 코드가 실제 히트판 기능을 실행하고 결과(tool_result)를
//   다시 클로드에게 돌려준다. 이 왕복을 클로드가 "끝(end_turn)" 할 때까지 반복한다.
//   → 한 명령으로 여러 도구를 연쇄("거래명세서 쓰고 수주도")할 수 있다.
//
//   생성(쓰기) Tool 이 초안을 만들고 승인 카드(PendingApproval)를 반환하면,
//   루프를 멈추고 사용자에게 승인/반려 카드를 보여준다("AI는 준비, 실행은 사람" — 헌법 #6).
// ─────────────────────────────────────────────────────────────────

/// <summary>AI 직원 1회 처리 결과.</summary>
public sealed class AgentRunResult
{
    public bool Handled { get; init; }              // 엔진이 처리했나(아니면 호출부가 기존 도우미로 폴백)
    public string Answer { get; init; } = "";       // 최종 사람 말 답변
    public AiAnalysisResultDto? Analysis { get; init; }   // 조회 Tool 이 만든 표/차트(있으면)
    public PendingActionDto? PendingAction { get; init; } // 생성 Tool 승인 카드(있으면)
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }

    public static AgentRunResult NotHandled() => new() { Handled = false };
}

/// <summary>AI 직원 엔진. Tool 카탈로그를 들고 멀티턴 루프를 돈다.</summary>
public interface IAiAgentService
{
    /// <summary>
    /// 명령을 Tool Use 로 처리. 키 없음·실패 시 Handled=false(→ 호출부가 기존 도우미 흐름).
    /// </summary>
    Task<AgentRunResult> RunAsync(
        string decryptedApiKey,
        string userMessage,
        IReadOnlyList<ChatHistoryTurn> history,
        ToolContext ctx,
        CancellationToken ct = default);
}

public sealed class AiAgentService : IAiAgentService
{
    private const int MaxToolTurns = 6;   // 무한 루프 방지 — 한 명령에 도구 호출 최대 6회.

    private readonly IChatCompletionProvider _provider;
    private readonly IHitpanToolRegistry _registry;
    private readonly IAiAgentSystemPrompt _systemPrompt;
    private readonly ILogger<AiAgentService> _logger;

    public AiAgentService(
        IChatCompletionProvider provider,
        IHitpanToolRegistry registry,
        IAiAgentSystemPrompt systemPrompt,
        ILogger<AiAgentService> logger)
    {
        _provider = provider;
        _registry = registry;
        _systemPrompt = systemPrompt;
        _logger = logger;
    }

    public async Task<AgentRunResult> RunAsync(
        string decryptedApiKey,
        string userMessage,
        IReadOnlyList<ChatHistoryTurn> history,
        ToolContext ctx,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(decryptedApiKey) || string.IsNullOrWhiteSpace(userMessage))
        {
            return AgentRunResult.NotHandled();
        }

        var tools = _registry.BuildToolCatalog();          // 클로드에 노출할 Tool 정의 JSON.
        var messages = new List<ToolUseMessage>();

        // 직전 대화(history) → user/assistant 단순 텍스트 메시지로 적재(맥락 기억).
        foreach (var turn in history)
        {
            var role = string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";
            if (!string.IsNullOrWhiteSpace(turn.Content))
            {
                messages.Add(new ToolUseMessage { Role = role, Content = TextContent(turn.Content) });
            }
        }
        // 이번 명령.
        messages.Add(new ToolUseMessage { Role = "user", Content = TextContent(userMessage) });

        var totalIn = 0;
        var totalOut = 0;
        AiAnalysisResultDto? analysis = null;   // 조회 Tool 이 표/차트를 만들면 마지막 것을 채택.

        for (var turn = 0; turn < MaxToolTurns; turn++)
        {
            var resp = await _provider.CompleteWithToolsAsync(
                decryptedApiKey, _systemPrompt.Value, messages, tools, ct).ConfigureAwait(false);

            if (!resp.Succeeded)
            {
                // 클로드 호출 실패 → 엔진 미처리(호출부가 기존 도우미로 폴백).
                return AgentRunResult.NotHandled();
            }

            totalIn += resp.InputTokens;
            totalOut += resp.OutputTokens;

            // 도구 호출이 없으면 = 최종 답변(end_turn).
            if (resp.ToolCalls.Count == 0)
            {
                return new AgentRunResult
                {
                    Handled = true,
                    Answer = string.IsNullOrWhiteSpace(resp.Text) ? "처리했습니다." : resp.Text,
                    Analysis = analysis,
                    InputTokens = totalIn,
                    OutputTokens = totalOut
                };
            }

            // assistant 의 이번 응답(content 블록 원본)을 대화에 추가(tool_result 와 짝 맞춤).
            messages.Add(new ToolUseMessage { Role = "assistant", Content = resp.RawAssistantContent });

            // 각 tool_use 실행 → tool_result 블록 수집.
            var toolResultBlocks = new List<object>();
            foreach (var call in resp.ToolCalls)
            {
                var tool = _registry.Find(call.Name);
                if (tool is null)
                {
                    toolResultBlocks.Add(ToolResultBlock(call.Id, $"알 수 없는 도구: {call.Name}", isError: true));
                    continue;
                }

                ToolResult result;
                try
                {
                    result = await tool.ExecuteAsync(call.Input, ctx, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 헌법 #15: 빈 catch 금지. 도구 실행 예외는 경고 + 클로드에 오류로 전달(루프 계속).
                    _logger.LogWarning(ex, "AI 직원 도구 실행 예외: {Tool}", call.Name);
                    toolResultBlocks.Add(ToolResultBlock(call.Id, $"도구 실행 중 오류: {ex.Message}", isError: true));
                    continue;
                }

                // 생성 Tool 이 승인 카드를 냈으면 → 즉시 루프 종료(사람 승인 대기).
                if (result.PendingApproval is not null)
                {
                    return new AgentRunResult
                    {
                        Handled = true,
                        Answer = string.IsNullOrWhiteSpace(resp.Text)
                            ? $"{result.PendingApproval.Title} 초안을 만들었습니다. 확인 후 승인해 주세요."
                            : resp.Text,
                        PendingAction = result.PendingApproval,
                        Analysis = analysis,
                        InputTokens = totalIn,
                        OutputTokens = totalOut
                    };
                }

                // 조회 Tool 이 표/차트를 냈으면 보관(가장 최근 것을 최종 채택).
                if (result.Analysis is not null)
                {
                    analysis = result.Analysis;
                }

                toolResultBlocks.Add(ToolResultBlock(call.Id, result.Content, isError: !result.Succeeded));
            }

            // tool_result 들을 user 메시지로 묶어 다음 턴에 전달 → 클로드가 이어서 판단.
            messages.Add(new ToolUseMessage
            {
                Role = "user",
                Content = JsonSerializer.SerializeToElement(toolResultBlocks)
            });
        }

        // 최대 턴 초과 — 안전 종료.
        _logger.LogWarning("AI 직원 도구 루프 최대 턴({Max}) 초과 — 안전 종료.", MaxToolTurns);
        return new AgentRunResult
        {
            Handled = true,
            Answer = "요청이 복잡해 한 번에 끝내지 못했어요. 조금 더 구체적으로 말씀해 주시겠어요?",
            Analysis = analysis,
            InputTokens = totalIn,
            OutputTokens = totalOut
        };
    }

    // content 블록: 단순 텍스트 1개.
    private static JsonElement TextContent(string text)
        => JsonSerializer.SerializeToElement(new[] { new { type = "text", text } });

    // content 블록: tool_result 1개.
    private static object ToolResultBlock(string toolUseId, string content, bool isError)
        => new
        {
            type = "tool_result",
            tool_use_id = toolUseId,
            content,
            is_error = isError
        };
}

/// <summary>AI 직원 엔진의 System Prompt 공급자(정체성 + 도구 사용 지침).</summary>
public interface IAiAgentSystemPrompt
{
    string Value { get; }
}
