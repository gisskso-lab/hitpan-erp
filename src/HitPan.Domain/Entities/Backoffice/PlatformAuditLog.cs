// ============================================================================
// WS-20260601-15 백오피스 PlatformAuditLog — 8명제 #7 JIT 복호화 6건 추적
// ----------------------------------------------------------------------------
// 정합 산출물 : database/migrations/20260601_eight_propositions.sql §4
// 절대 원칙   : INSERT ONLY (헌법 #3 원장 정합) — UPDATE / DELETE 금지
//              details JSON 에 평문 사업자 정보 박제 금지
// ============================================================================

namespace HitPan.Domain.Entities.Backoffice;

/// <summary>
/// 본사 운영 감사로그. INSERT ONLY. 월별 파티셔닝(DB 차원).
/// 본사 직원의 모든 시리얼 발급·환불·JIT 복호화·CS 처리 추적.
/// </summary>
public class PlatformAuditLog
{
    public long AuditId { get; set; }

    /// <summary>platform_users.user_id.</summary>
    public long UserId { get; set; }

    /// <summary>예: SERIAL_ISSUE / JIT_DECRYPT / REFUND / RECOVERY_APPROVE.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>대상 시리얼 (tenants/resellers, 없으면 null).</summary>
    public string? TargetSerial { get; set; }

    /// <summary>감사 메타 JSON. 평문 사업자 정보 박제 금지.</summary>
    public string? Details { get; set; }

    public string? Ip { get; set; }

    public DateTime RequestedAt { get; set; }
}
