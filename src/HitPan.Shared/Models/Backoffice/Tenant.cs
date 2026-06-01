// ============================================================================
// WS-20260601-15 백오피스 Tenant 모델 — 사장님 8명제 #3 정합 (평문 0)
// ----------------------------------------------------------------------------
// 작성일       : 2026-06-01
// 작성팀       : DB 매니저(Harvard·Oracle 30년) + DB 개발자
// 정합 산출물  : database/migrations/20260601_eight_propositions.sql §1
// 절대 금지    :
//   - 평문 사업자번호 / 상호 / 대표자 / 주소 / 이메일 / 휴대폰 프로퍼티
//   - 평문 비밀번호 프로퍼티
//   - tenant_id 파라미터 수신 (JWT 클레임 only — 헌법 #2)
// ============================================================================

namespace HitPan.Shared.Models.Backoffice;

/// <summary>
/// 백오피스 고객사 마스터. 8명제 #3 평문 0 — 해시·메타·구독 상태만 보유.
/// 평문 사업자 정보는 본사 ERP(물리 분리)에서 JIT 복호화로만 노출.
/// </summary>
public class Tenant
{
    /// <summary>시리얼 PK: HP-YYMM-XXXXXXXX-CRC (본사 발급, 8명제 #2).</summary>
    public string Serial { get; set; } = string.Empty;

    /// <summary>SHA-256(email). 평문 이메일 보관 금지.</summary>
    public string EmailHash { get; set; } = string.Empty;

    /// <summary>Argon2id 해시. 평문 비밀번호 보관 금지.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256(biz_no, pepper). pepper는 HSM 보관 (8명제 #3).</summary>
    public string BizNoHash { get; set; } = string.Empty;

    /// <summary>국세청 진위확인 결과.</summary>
    public bool Verified { get; set; }

    /// <summary>진위확인 시각.</summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>국세청 응답 SHA-256 (원문은 메모리 즉시 폐기, 8명제 #1).</summary>
    public string? NtsapiRawHash { get; set; }

    /// <summary>구독 티어: basic / pro / enterprise.</summary>
    public string? SubscriptionTier { get; set; }

    /// <summary>결제 상태: active / pending / expired / suspended.</summary>
    public string PaymentStatus { get; set; } = "pending";

    /// <summary>활성 상태: pending / active / suspended / revoked (8명제 #8 생애주기).</summary>
    public string ActivationStatus { get; set; } = "pending";

    /// <summary>대리점 시리얼 (직판 = null, 8명제 #6 RLS).</summary>
    public string? ResellerSerial { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    /// <summary>가입 IP (IPv4/IPv6).</summary>
    public string? Ip { get; set; }

    /// <summary>디바이스 핑거프린트 해시.</summary>
    public string? DeviceFingerprint { get; set; }
}
