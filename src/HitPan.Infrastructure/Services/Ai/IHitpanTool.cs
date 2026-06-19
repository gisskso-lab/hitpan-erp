using System.Text.Json;
using HitPan.Application.DTOs.Chatbot;

namespace HitPan.Application.Services.Ai;

// ─────────────────────────────────────────────────────────────────
// 히트판 AI 직원 — Tool 추상화 (뼈대)
//
// 사장님 비전 (2026-06-20): "히트판의 모든 기능을 챗봇이 할 줄 알아야 그게 기본 뼈대".
//   클로드가 명령을 보고 스스로 적합한 Tool 을 골라 호출(Tool Use). 우리 코드가 실제 히트판
//   기능을 실행하고 결과를 돌려준다. Tool 만 추가하면 챗봇이 할 줄 아는 일이 무한 확장된다.
//
//   - 조회 Tool (IsWrite=false): 즉시 실행. 데이터 변경 0(헌법 #6).
//   - 생성 Tool (IsWrite=true): 초안만 만들고 승인 카드 반환 → 사람 승인 후 확정.
//     ("AI는 준비, 실행은 사람" — 헌법 #6. 승인 후 워크플로우 연쇄는 별도 단계.)
//   - 모든 Tool 은 tenantId(JWT 유래, 헌법 #2)·userId·권한 안에서만 동작.
// ─────────────────────────────────────────────────────────────────

/// <summary>Tool 실행에 필요한 호출자 맥락 (테넌트·사용자·권한).</summary>
public sealed class ToolContext
{
    public required string TenantId { get; init; }
    public required string UserId { get; init; }

    /// <summary>로그인 사용자의 권한 정책 집합(SalesOnly·PurchaseOnly·TenantAdminOnly 등). Tool 이 게이트에 사용.</summary>
    public IReadOnlySet<string> Policies { get; init; } = new HashSet<string>();
}

/// <summary>Tool 실행 결과.</summary>
public sealed class ToolResult
{
    /// <summary>클로드에게 돌려줄 결과 본문(JSON 문자열 권장 — 클로드가 해석).</summary>
    public string Content { get; init; } = "";

    /// <summary>실행 성공 여부. 실패 시 Content 에 사유.</summary>
    public bool Succeeded { get; init; } = true;

    /// <summary>
    /// 생성(쓰기) Tool 이 초안을 만들었을 때 사람 승인이 필요하면 채운다.
    /// 채워지면 엔진이 루프를 멈추고 승인 카드를 사용자에게 보여준다(실행은 사람).
    /// </summary>
    public PendingActionDto? PendingApproval { get; init; }

    /// <summary>
    /// 조회·분석 Tool 이 표/차트를 만들었으면 채운다(프론트가 챗봇 답변에 함께 렌더).
    /// Content(클로드용 요약)와 별개 — 사람이 보는 시각화.
    /// </summary>
    public AiAnalysisResultDto? Analysis { get; init; }

    public static ToolResult Ok(string content) => new() { Content = content, Succeeded = true };
    public static ToolResult Fail(string reason) => new() { Content = reason, Succeeded = false };
}

/// <summary>히트판 기능 1개를 클로드가 호출할 수 있게 감싼 Tool.</summary>
public interface IHitpanTool
{
    /// <summary>클로드에 노출되는 Tool 이름(영문 snake/dot, 예: "sales.profitability").</summary>
    string Name { get; }

    /// <summary>클로드가 언제 이 Tool 을 쓸지 판단하는 설명(한국어 OK).</summary>
    string Description { get; }

    /// <summary>입력 파라미터 JSON Schema(Anthropic tools 규격의 input_schema).</summary>
    JsonElement InputSchema { get; }

    /// <summary>쓰기 Tool 여부. true면 초안→승인 게이트 대상(헌법 #6).</summary>
    bool IsWrite { get; }

    /// <summary>
    /// 이 Tool 실행에 필요한 권한 정책명(예: "SalesOnly"·"PurchaseOnly"). null/빈이면 권한 제약 없음(조회).
    /// 봉합 (2026-06-20, 3차 전수조사 AICHAT-SEC-01-F1): 챗봇 ask 경로(TenantOnly)로 쓰기 Tool 이
    ///   무권한 실행되던 비대칭을 닫는다. 엔진(AiAgentService)이 실행 전 ctx.Policies 에 이 권한이 있는지 검사.
    ///   정식 SalesController(SalesOnly) 와 동일 게이트를 챗봇 경로에도 적용(헌법 #7 직무권한 일관).
    /// </summary>
    string? RequiredPolicy => null;

    /// <summary>클로드가 채워 보낸 인자(input)로 실제 히트판 기능 실행.</summary>
    Task<ToolResult> ExecuteAsync(JsonElement input, ToolContext ctx, CancellationToken ct = default);
}
