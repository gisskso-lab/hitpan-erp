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
}
