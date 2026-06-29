using HitPan.Web.Models;

namespace HitPan.Web.Services;

public interface IAuthService
{
    Task<AuthLoginResult> LoginAsync(string email, string password, CancellationToken ct = default);
    Task<bool> RefreshAsync(CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);

    /// <summary>고리2(A안) — 업데이트 동의(approve/reject)를 로컬 ERP DB에 기록. 성공 시 true.</summary>
    Task<bool> SubmitUpdateConsentAsync(string updateVersion, string action, CancellationToken ct = default);
}
