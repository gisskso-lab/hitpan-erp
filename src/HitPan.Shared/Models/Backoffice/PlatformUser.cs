// ============================================================================
// WS-20260601-15 백오피스 PlatformUser 모델 — 8명제 #7 본사 권한 4계층
// ----------------------------------------------------------------------------
// 정합 산출물 : database/migrations/20260601_eight_propositions.sql §3
// 헌법 #11   : 권한은 Owner가 직접 부여 (템플릿 금지)
// ============================================================================

namespace HitPan.Shared.Models.Backoffice;

/// <summary>본사 운영 계정 Role.</summary>
public enum PlatformRole
{
    Owner = 0,
    PlatformManager = 1,
    PlatformStaff = 2
}

/// <summary>본사 운영 계정 상태.</summary>
public enum PlatformUserStatus
{
    Active = 0,
    Suspended = 1,
    Revoked = 2
}

/// <summary>
/// 본사 운영 계정 (Owner / Manager / Staff). MFA 강제 + IP 화이트리스트.
/// 평문 이메일·휴대폰 보관 금지 — 이메일은 SHA-256 해시만.
/// </summary>
public class PlatformUser
{
    public long UserId { get; set; }

    /// <summary>SHA-256(email).</summary>
    public string EmailHash { get; set; } = string.Empty;

    /// <summary>Argon2id 해시.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public PlatformRole Role { get; set; }

    /// <summary>운영자명 (사내 표시용, 본사 내부 직원 신원).</summary>
    public string? FullName { get; set; }

    public PlatformUserStatus Status { get; set; } = PlatformUserStatus.Active;

    /// <summary>MFA 강제 (Owner 2/2 결재).</summary>
    public bool MfaEnabled { get; set; } = true;

    /// <summary>CIDR 화이트리스트 (개행 구분).</summary>
    public string? IpWhitelist { get; set; }

    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Owner 발급자 user_id.</summary>
    public long? CreatedBy { get; set; }
}
