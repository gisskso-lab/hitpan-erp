namespace HitPan.Application.Common;

/// <summary>
/// 서버 페이지네이션 응답 표준 컨테이너 (2026-05-13 야간, 헌법 #25 정공법).
/// 기존 List 반환 메서드와 병행 — 신규 PagedAsync 메서드만 이 타입을 반환한다.
/// </summary>
public sealed class PagedResult<T>
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public int TotalCount { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
}

/// <summary>
/// 서버 페이지네이션 요청 표준 파라미터.
/// PageSize는 기본 50, 최대 500으로 클램핑(악의·실수 방어, 헌법 EVF #3·#4).
/// </summary>
public sealed class PagedRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public string? Search { get; init; }
    public string? SortBy { get; init; }
    public bool SortDesc { get; init; }

    public int Skip => Math.Max(0, (Page - 1) * Math.Clamp(PageSize, 1, 500));
    public int Take => Math.Clamp(PageSize, 1, 500);
}
