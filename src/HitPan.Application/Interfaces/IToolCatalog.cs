namespace HitPan.Application.Interfaces;

/// <summary>
/// Tool 카탈로그 (Phase B 골격 / 2026-06-11 사장님 결재).
/// - 컨트롤러 47개 → Tool 자동 생성 (Roslyn Source Generator 또는 Reflection 영역).
/// - 빌드 시 1회 생성 + 캐시. 런타임 비용 0.
/// - 본 인터페이스는 골격. 실제 생성기는 작업지시서 S1 단계.
/// </summary>
public interface IToolCatalog
{
    /// <summary>전체 Tool 정의 (정렬·캐시됨).</summary>
    IReadOnlyList<AiToolDefinition> All { get; }

    /// <summary>이름으로 Tool 조회. 미존재 시 null.</summary>
    AiToolDefinition? Find(string toolName);

    /// <summary>
    /// 사용자 권한·역할에 따라 노출 가능한 Tool만 필터.
    /// 권한 매트릭스 (PRD OI-5 정합):
    /// - Read = 직원 이상
    /// - Draft = 영업 이상
    /// - Commit = 관리자만
    /// - Cancel = 관리자만
    /// </summary>
    IReadOnlyList<AiToolDefinition> FilterByRole(string roleCode);
}

/// <summary>
/// Tool 실행 라우터. 카탈로그 외 Tool 호출 시도 시 즉시 거부 + 감사 로그.
/// 헌법 #2 정합: tenant_id / user_id는 ICurrentTenant + ICurrentUser 주입만.
/// </summary>
public interface IToolInvoker
{
    /// <summary>
    /// Tool 호출 실행. 내부적으로 동일 ASP.NET Core 컨트롤러 경로 통과 →
    /// 기존 Authorization Policy 그대로 적용.
    /// </summary>
    Task<ToolInvocationResult> InvokeAsync(
        AiToolCall call,
        string tenantId,
        string userId,
        string roleCode,
        CancellationToken ct = default);
}

public sealed record ToolInvocationResult(
    bool Success,
    string? ResultJson,
    string? ErrorMessage,
    string? Message,
    IReadOnlyList<string>? SuggestedNextActions);
