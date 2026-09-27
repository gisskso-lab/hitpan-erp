using System.Text.Json.Serialization;

namespace HitPan.Web.Models;

public static class AuthStorageKeys
{
    public const string AccessToken = "hitpan_access_token";
    public const string RefreshToken = "hitpan_refresh_token";
    public const string UserDisplayName = "hitpan_user_name";
}

public sealed class LoginRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    // 기기 기반 라이선싱 (선택값)
    public string? DeviceFingerprint { get; set; }
    public string? DeviceType { get; set; }
    public string? DeviceName { get; set; }

    // 🔴 장비넘버 — 지난번에 서버가 내려준 이 기기의 번호 (20260816작2 · 명세서 §4-4).
    //   지문보다 **먼저** 이 값으로 기기를 찾는다. 브라우저가 바뀌어도 안 바뀌므로
    //   Edge ↔ Chrome 을 오가도 같은 줄이다 = 슬롯이 새로 안 는다.
    //   ⚠️ 서버 LoginRequest.DeviceId 와 **이름이 같아야** 실려 간다(DTO 가 두 벌이다).
    public string? DeviceId { get; set; }

    /// <summary>
    /// 🔴 다른 컴퓨터의 접속을 끊고 여기서 쓰겠다 — <b>사용자가 버튼을 눌렀을 때만</b> true (20260927작1).
    /// </summary>
    /// <remarks>
    /// 🚫 <b>기본으로 true 를 보내지 마라.</b> 그러면 자동 밀어내기가 되어, 다른 컴퓨터에서
    /// 전표를 쓰던 화면이 예고 없이 튕긴다 — ERP 는 보는 서비스가 아니라 <b>입력하는 서비스</b>다.
    /// <para>⚠️ 서버 <c>LoginRequest.ForceSignOutOtherPc</c> 와 이름이 같아야 실려 간다(DTO 가 두 벌이다).</para>
    /// </remarks>
    public bool ForceSignOutOtherPc { get; set; }
}

public sealed class LoginApiResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool RedirectToWelcome { get; set; }
    public string? DeviceId { get; set; }

    // ── 접속 기기 슬롯 F3 안내 (작3 I3, 사장님 결재 2026-07-02) ──
    //   서버(LoginResponse)가 채워 보내는 값을 프론트가 소비(종전엔 필드가 없어 그냥 버려졌음).
    //   방식 = (a) 자동 등록 후 통지: 처음 등록된 신규 기기면 로그인 후 사용자에게 알린다.
    public bool DeviceNewlyRegistered { get; set; }
    public string? DeviceNotice { get; set; }

    // 🔴 이 기기가 아직 승인 대기인가 (20260816작2 — 사장님 전결).
    //   true 면 Login.razor 가 [디바이스 인증] 관문을 띄우고 ERP 진입을 막는다.
    //   ⚠️ 이 칸은 서버 LoginResponse.DeviceAwaitingApproval 과 **이름이 같아야** 채워진다.
    //     DTO 가 두 벌(서버/웹)이라 한쪽만 고치면 값이 조용히 false 로 온다.
    public bool DeviceAwaitingApproval { get; set; }

    // 헌법 #24: 약관 v2.0.0 미동의 시 true (Login.razor에서 /terms 강제 이동)
    public bool RequiresTermsConsent { get; set; }

    // ── 고리2(A안): 업데이트 동의 Y/N 팝업 (Login.razor에서 UpdateAvailable=true면 팝업) ──
    public string? CurrentVersion { get; set; }
    public bool UpdateAvailable { get; set; }
    public string? LatestVersion { get; set; }
    public string? UpdateChannel { get; set; }
    public string? ConsentMessage { get; set; }
}

/// <summary>고리2 업데이트 동의 요청 바디 (POST api/auth/update-consent).</summary>
public sealed class UpdateConsentRequestDto
{
    [JsonPropertyName("updateVersion")]
    public string UpdateVersion { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;
}

public sealed class RefreshTokenRequestDto
{
    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class ApiErrorMessageDto
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public sealed class AuthLoginResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public LoginApiResponse? Data { get; init; }

    /// <summary>
    /// 🔴 같은 계정이 <b>다른 컴퓨터에서 사용 중</b>이라 막힌 경우 (20260927작1 절D-2).
    /// </summary>
    /// <remarks>
    /// 이 값이 참이면 화면은 <b>비밀번호 오류로 보여주면 안 된다.</b> 그러면 사용자가
    /// 멀쩡한 비밀번호를 몇 번씩 다시 친다. 대신 마지막 사용 시각과
    /// [그 컴퓨터 접속을 끊고 여기서 사용하기] 를 보여준다.
    /// </remarks>
    public bool OtherPcInUse { get; init; }

    /// <summary>다른 컴퓨터가 마지막으로 움직인 시각. 사용자가 끊어도 될지 스스로 판단하는 근거다.</summary>
    public DateTime? OtherPcLastActiveAt { get; init; }
}

/// <summary>로그인이 「다른 PC 사용 중」으로 막혔을 때 서버가 주는 본문 (409).</summary>
public sealed class OtherPcInUseDto
{
    public string? Message { get; set; }
    public string? Code { get; set; }
    public DateTime? OtherPcLastActiveAt { get; set; }
    public bool CanForce { get; set; }
}
