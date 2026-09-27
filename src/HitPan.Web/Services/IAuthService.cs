using HitPan.Web.Models;

namespace HitPan.Web.Services;

public interface IAuthService
{
    /// <param name="forceSignOutOtherPc">
    /// 🔴 다른 컴퓨터 접속을 끊고 여기서 쓰겠다 — <b>사용자가 버튼을 눌렀을 때만</b> true (20260927작1).
    /// 기본 false. 기본을 true 로 두면 자동 밀어내기가 되어 남이 쓰던 입력이 날아간다.
    /// </param>
    Task<AuthLoginResult> LoginAsync(
        string email, string password, bool forceSignOutOtherPc = false, CancellationToken ct = default);
    Task<bool> RefreshAsync(CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);

    /// <summary>고리2(A안) — 업데이트 동의(approve/reject)를 로컬 ERP DB에 기록. 성공 시 true.</summary>
    Task<bool> SubmitUpdateConsentAsync(string updateVersion, string action, CancellationToken ct = default);
}
