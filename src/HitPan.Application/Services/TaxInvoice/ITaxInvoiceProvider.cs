using System.Security.Cryptography.X509Certificates;

namespace HitPan.Application.Services.TaxInvoice;

/// <summary>
/// 전자세금계산서 발행 Provider 어댑터 패턴 — 작B v3.0 방식 A
///
/// 백엔드 매니저 권고 (Oracle 30년):
/// "어댑터 1개 두면 향후 ASP 백업/전환 30분"
///
/// 구현체:
/// - DirectHometaxProvider (메인, 다이렉트, 정상 시)
/// - MakebillFallbackProvider (폴백, 어댑터만 보유, 사용 안 함)
/// </summary>
public interface ITaxInvoiceProvider
{
    /// <summary>세금계산서 발행 (홈택스 또는 ASP).</summary>
    Task<IssueResult> IssueAsync(TaxInvoiceInput invoice, X509Certificate2 cert, CancellationToken ct = default);

    /// <summary>발행 상태 조회 (반송 폴링).</summary>
    Task<StatusResult> CheckStatusAsync(string approvalNumber, X509Certificate2 cert, CancellationToken ct = default);

    /// <summary>발행 취소 (24h 이내).</summary>
    Task<CancelResult> CancelAsync(string approvalNumber, X509Certificate2 cert, string reason, CancellationToken ct = default);

    /// <summary>사업자 휴폐업 조회 (발행 전 필수, 백엔드 매니저 30년 권고).</summary>
    Task<BusinessStatus> CheckBusinessStatusAsync(string businessNumber, CancellationToken ct = default);
}

public sealed record IssueResult(
    bool Success,
    string? ApprovalNumber,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime IssuedAt);

public sealed record StatusResult(
    string ApprovalNumber,
    InvoiceStatus Status,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime CheckedAt);

public sealed record CancelResult(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime CancelledAt);

public sealed record BusinessStatus(
    string BusinessNumber,
    bool IsActive,
    string? TaxType, // 일반·간이·면세
    string? StatusName);

public enum InvoiceStatus
{
    Pending = 0,
    Issued = 1,        // 정상 발행
    Approved = 2,      // 국세청 승인
    Bounced = 3,       // 반송 (5종 사고 중 최악)
    Cancelled = 4,     // 취소
    Failed = 99
}
