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

    // ── 20261005작5 사원 ↔ 계정 연결 (설계 §3·§4·§5-3) — 구현체 전수 grep(#12): UserService 하나 · 시험 가짜 0(Moq 만) ──

    /// <summary>기존 사원에게 계정 만들기(사원 행 INSERT 0). 대표·관리자가 아니면 일반 고정 · 일반 직무 사원만.</summary>
    Task<string> CreateForEmployeeAsync(CreateForEmployeeDto dto, string tenantId, bool actorIsAdmin, CancellationToken ct = default);

    /// <summary>계정을 만들 수 있는 사원(재직 · 계정 없음). name 이 있으면 그 이름만.</summary>
    Task<List<HitPan.Application.Services.LinkableEmployeeDto>> ListLinkableEmployeesAsync(string tenantId, string? name, bool onlyGeneralRoles, CancellationToken ct = default);

    /// <summary>2단계 직원의 사용 안 함 — 대표·관리자·본인·권한 가진 직원 대상이면 거절.</summary>
    Task SuspendAsStaffAsync(string userId, string actorUserId, string tenantId, CancellationToken ct = default);

    /// <summary>2단계 직원의 다시 사용 — 같은 울타리.</summary>
    Task ResumeAsStaffAsync(string userId, string actorUserId, string tenantId, CancellationToken ct = default);
}
