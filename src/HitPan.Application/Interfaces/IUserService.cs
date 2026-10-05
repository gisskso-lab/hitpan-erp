using HitPan.Application.DTOs.User;

namespace HitPan.Application.Interfaces;

public interface IUserService
{
    Task<List<UserListDto>> GetListAsync(string tenantId, CancellationToken ct = default);

    Task<UserListDto?> GetAsync(string userId, string tenantId, CancellationToken ct = default);

    Task<string> CreateAsync(CreateUserDto dto, string tenantId, CancellationToken ct = default);

    Task UpdateAsync(string userId, UpdateUserDto dto, string tenantId, CancellationToken ct = default);

    Task DeactivateAsync(string userId, string tenantId, CancellationToken ct = default);

    Task<string> ResetPasswordAsync(string userId, string tenantId, CancellationToken ct = default);

    /// <summary>엑셀 행 리스트를 일괄 생성 — 각 행은 독립 처리(한 행 실패해도 나머지 진행)</summary>
    Task<BulkCreateResultDto> BulkCreateAsync(List<CreateUserDto> rows, string tenantId, CancellationToken ct = default);

    // ── 20261005작3 계정 과금 (설계 §1·§4) — 구현체 전수 grep(#12): UserService 하나 · 시험 가짜 0 ──
    Task<HitPan.Application.Services.AccountSeatSnapshot> GetSeatsAsync(string tenantId, CancellationToken ct = default);

    /// <summary>사용 중 → 사용 안 함 (대표 거절)</summary>
    Task SuspendAsync(string userId, string tenantId, CancellationToken ct = default);

    /// <summary>사용 안 함 → 사용 중 · 판정기 통과 후</summary>
    Task ResumeAsync(string userId, string tenantId, CancellationToken ct = default);

    /// <summary>계정폐기 — 아이디 비움 · 사원 연결 끊기 · 되살림 없음 (대표 거절)</summary>
    Task RetireAsync(string userId, string tenantId, CancellationToken ct = default);
}
