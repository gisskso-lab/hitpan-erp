// ============================================================================
// WS-20260601-15 백오피스 RecoveryLog — 8명제 #5 분실 복구 이력
// ----------------------------------------------------------------------------
// 정합 산출물 : database/migrations/20260601_eight_propositions.sql §5
// 헌법 #16   : 이메일·SMS 채널은 독립 MySqlConnection 으로 전송
// ============================================================================

namespace HitPan.Shared.Models.Backoffice;

/// <summary>분실 복구 대상 시리얼 유형.</summary>
public enum RecoverySerialType
{
    Tenant = 0,
    Reseller = 1
}

/// <summary>
/// 분실 복구 이력. 사등 재업로드 → 본사 어드민 검토 → 2채널 재발급.
/// </summary>
public class RecoveryLog
{
    public long RecoveryId { get; set; }

    /// <summary>복구 대상 시리얼.</summary>
    public string Serial { get; set; } = string.Empty;

    public RecoverySerialType SerialType { get; set; }

    /// <summary>이메일 채널 전송 여부.</summary>
    public bool ChannelEmail { get; set; }

    /// <summary>SMS 채널 전송 여부 (헌법 #16 독립 connection).</summary>
    public bool ChannelSms { get; set; }

    /// <summary>발급 어드민 user_id (platform_users).</summary>
    public long? AdminUserId { get; set; }

    public string? Ip { get; set; }
    public string? DeviceFingerprint { get; set; }

    public DateTime RequestedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
