namespace HitPan.Web.Models;

/// <summary>
/// 서버 페이지네이션 응답 표준 컨테이너 (2026-05-13 야간, 헌법 #25 정공법).
/// 백엔드 HitPan.Application.Common.PagedResult&lt;T&gt;와 JSON 호환.
/// </summary>
public sealed class PagedResponse<T>
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public List<T> Items { get; set; } = new();
}
