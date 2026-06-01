// ============================================================================
// WS-20260601-15 백오피스 Reseller 모델 — 8명제 #6 RLS 정합 (평문 0)
// ----------------------------------------------------------------------------
// 정합 산출물 : database/migrations/20260601_eight_propositions.sql §2
// 절대 금지   : 평문 사업자번호 / 상호 / 대표자 / 주소 / 이메일 / 휴대폰
// ============================================================================

namespace HitPan.Shared.Models.Backoffice;

/// <summary>
/// 대리점 마스터. 고객사와 동일 패턴 (시리얼 PK + 해시 only).
/// </summary>
public class Reseller
{
    /// <summary>시리얼 PK: HR-YYMM-XXXXXXXX-CRC.</summary>
    public string Serial { get; set; } = string.Empty;

    public string EmailHash { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256(biz_no, pepper). pepper는 HSM 보관.</summary>
    public string BizNoHash { get; set; } = string.Empty;

    public DateTime VerifiedAt { get; set; }
    public string? NtsapiRawHash { get; set; }

    /// <summary>대리점 유형: individual / corp.</summary>
    public string ResellerType { get; set; } = "corp";

    /// <summary>계약 상태: active / expired / terminated.</summary>
    public string ContractStatus { get; set; } = "active";

    /// <summary>수수료율 % (헌법 #4 decimal).</summary>
    public decimal CommissionRate { get; set; }

    /// <summary>활성 상태: pending / active / suspended / revoked.</summary>
    public string ActivationStatus { get; set; } = "pending";

    public DateTime CreatedAt { get; set; }
}
