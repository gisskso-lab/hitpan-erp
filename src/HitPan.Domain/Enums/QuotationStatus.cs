namespace HitPan.Domain.Enums;

/// <summary>
/// 견적서 상태 값을 정의한다.
/// </summary>
public enum QuotationStatus
{
    /// <summary>
    /// 임시저장 상태.
    /// </summary>
    Draft = 1,

    /// <summary>
    /// 제출 상태.
    /// </summary>
    Submitted = 2,

    /// <summary>
    /// 수락 상태.
    /// </summary>
    Accepted = 3,

    /// <summary>
    /// 반려 상태.
    /// </summary>
    Rejected = 4,

    /// <summary>
    /// 만료 상태.
    /// </summary>
    Expired = 5,

    /// <summary>
    /// 수주 전환 완료 상태.
    /// </summary>
    Converted = 6
}
