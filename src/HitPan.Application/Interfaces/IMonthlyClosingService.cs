using HitPan.Application.DTOs.Approval;

namespace HitPan.Application.Interfaces;

/// <summary>월마감 서비스 인터페이스</summary>
public interface IMonthlyClosingService
{
    /// <summary>최근 12개월 마감 현황 조회</summary>
    Task<List<MonthlyClosingDto>> GetStatusAsync(string tenantId, CancellationToken ct = default);

    /// <summary>월마감 처리</summary>
    Task CloseAsync(CloseMonthRequest request, string tenantId, string userId, CancellationToken ct = default);

    /// <summary>월마감 재오픈 (관리자 전용)</summary>
    Task ReopenAsync(ReopenMonthRequest request, string tenantId, string userId, CancellationToken ct = default);

    /// <summary>특정 월이 마감되었는지 확인 (전표 생성/수정 시 호출)</summary>
    Task<bool> IsClosedAsync(string tenantId, DateTime date, CancellationToken ct = default);
}
